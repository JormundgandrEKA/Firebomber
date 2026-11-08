using System;
using System.Collections.Generic;
using System.Linq;

namespace Firebomber;

public enum Mood { Normal, Ramming, Fleeing }

/// <summary>A fighter group: one UCS field's squadron, or the Hunds with one Vulfen box.</summary>
public sealed class Wing
{
    public string Name = ""; public bool Wolf; public readonly List<Fighter> Pilots = new(); public Formation? Box;
    public double Morale = .65; public int Lost, Kills; public bool Broken, Pressing, Withdrawing; public double BrokenT;
    /// <summary>The box this squadron has been told to go for, so that it attacks together and leaves the next box to the next squadron.</summary>
    public Formation? Target;
    /// <summary>The squadron layer (FighterTactics): what it is doing, since when, its leader, where it forms up, and how many passes it has made.</summary>
    public WPhase Phase; public double PhaseT, GoX, GoY, GoZ; public Fighter? Lead; public int Passes; public bool EndRun;
    public int Size => Pilots.Count;
}

/// <summary>Two different kinds of morale.
/// UCS: feeling. Honour lies in victory, so a winning squadron hardly ever retreats. Losses wear it down, and when it finally breaks, order goes: some pilots flee, some throw themselves at the bombers, and panic spreads to squadrons close by.
/// Vulfen: arithmetic. Pilots are too few to waste. While the exchange is good they press harder; when losses pass a set point the whole group turns back together, its best pilots holding the rear, and a bomber box does the same.</summary>
public sealed partial class FighterSim
{
    public readonly List<Wing> Wings = new();
    public int Rams, Breaks, Withdrawals, BoxesTurnedBack;
    double moraleT;
    double Sight => 6000 * (1 - .8 * air.Murk);

    void Moralestep()
    {
        moraleT -= Dt; if (moraleT > 0) return; moraleT = 10;
        AssignBoxes();
        foreach (var w in Wings)
        {
            if (w.Wolf && w.Box != null) Reckon(w);      // defenders are over their own ground: they land and come back rather than withdraw
            else if (!w.Broken)
            {
                // left alone, a squadron drifts back towards its normal confidence
                w.Morale += (.65 - w.Morale) * .02;
                if (w.Morale < .14 && w.Lost >= 3) BreakWing(w);
            }
        }
        BoxCommanders();
    }

    /// <summary>Each squadron goes for the nearest box that is not already getting more than its share of attention.</summary>
    void AssignBoxes()
    {
        var given = new Dictionary<Formation, int>();
        foreach (var w in Wings)
        {
            if (w.Box != null) continue;
            var up = w.Pilots.Where(p => p.Airborne).ToList(); if (up.Count == 0) { w.Target = null; continue; }
            double cx = up.Average(p => p.X), cy = up.Average(p => p.Y), best = 1e18; Formation? pick = null;
            foreach (var fm in air.Formations)
            {
                if (fm.Wolf == w.Wolf || fm.Phase is Phase.Waiting or Phase.Landed || fm.Ac.Count == 0) continue;
                given.TryGetValue(fm, out int n);
                double score = Dist(cx, cy, fm.Lead.X, fm.Lead.Y) + 25000.0 * n / fm.Ac.Count + (fm == w.Target ? -8000 : 0)
                    + (fm.Phase != Phase.Outbound ? (fm.Aborted ? 45000 : 30000) : -15000 * air.Progress(fm));
                if (!w.Wolf) { double hx = Math.Cos(fm.Lead.Th), hy = Math.Sin(fm.Lead.Th), along = (cx - fm.Lead.X) * hx + (cy - fm.Lead.Y) * hy; if (along < -2000 && up[0].P.VMax - fm.Lead.V < 30) score += 60000; }      // a box already past the squadron cannot be caught: one still coming its way can be met head-on      // a box on its way home has done its harm; the nearer one gets to its target, the more it matters
                if (score < best) { best = score; pick = fm; }
            }
            w.Target = pick; if (pick != null) { given.TryGetValue(pick, out int m); given[pick] = m + up.Count; }
        }
    }

    // ---------------------------------------------------------------- UCS
    void Feel(Fighter f, double change)
    {
        if (f.W == null || f.W.Broken) return;
        f.W.Morale = Math.Clamp(f.W.Morale + change * 24 / Math.Max(8, f.W.Size), 0, 1);
    }
    void BreakWing(Wing w)
    {
        w.Broken = true; w.BrokenT = T; Breaks++;
        foreach (var f in w.Pilots)
        {
            if (!f.Airborne) continue;
            // aces and the steadiest carry on as they were; the rest choose, each alone
            if (f.Ace && rng.NextDouble() < .7) continue;
            double r = rng.NextDouble();
            if (r < .12 + .22 * f.Aggression) { f.Mood = Mood.Ramming; f.Why = "no retreat: going for a bomber at any cost"; f.NextThink = T; }
            else if (r < .85) { f.Mood = Mood.Fleeing; GoHome(f, "the squadron has broken: running"); }
        }
        // panic is catching
        foreach (var o in Wings)
        {
            if (o.Wolf || o == w || o.Broken) continue;
            if (o.Pilots.Any(a => a.Airborne && w.Pilots.Any(b => b.Airborne && Dist(a.X, a.Y, b.X, b.Y) < 12000))) o.Morale -= .25;
        }
    }

    // ---------------------------------------------------------------- Vulfen
    /// <summary>The account is kept in cold figures, and acted on as a group.</summary>
    void Reckon(Wing w)
    {
        if (w.Withdrawing) return;
        w.Pressing = w.Kills >= 2 + 2 * w.Lost;
        bool badTrade = w.Lost >= Math.Max(2, Math.Ceiling(w.Size * .34)) && w.Kills < w.Lost * 3;
        if (!badTrade) return;
        w.Withdrawing = true; Withdrawals++;
        // the best with ammunition left hold the rear while the others leave
        var left = w.Pilots.Where(p => p.Airborne).OrderByDescending(p => p.Skill * (p.Ammo > 2 ? 1 : 0)).ToList();
        int guard = left.Count >= 3 ? 1 : 0;
        for (int i = 0; i < left.Count; i++)
        {
            if (i < guard) { left[i].Rear = T + 240; left[i].Why = "rear guard while the group withdraws"; left[i].NextThink = T; }
            else GoHome(left[i], "losses too high: the group is withdrawing");
        }
    }
    /// <summary>Half the box peels off and both halves go in on different lines, a few kilometres apart, so the fighters have two targets instead of one.</summary>
    void SplitBox(Formation fm)
    {
        var half = fm.Ac.Where(a => !a.Straggler && a != fm.Lead).Where((a, i) => i % 2 == 0).ToList(); fm.SplitDone = true; if (half.Count < 3) return;
        var nf = air.Split(half, fm.Name + "b"); if (nf == null) return; nf.SplitDone = true; nf.Spread = fm.Spread = 1.3;
        foreach (var (g, side) in new[] { (fm, 1), (nf, -1) })
        {
            var l = g.Lead; double fx = Math.Cos(l.Th), fy = Math.Sin(l.Th);
            g.Route.Insert(0, (l.X + fx * 7000 + fy * side * 5000, l.Y + fy * 7000 - fx * side * 5000)); air.SetAim(g, g.AimX + fy * side * 600, g.AimY - fx * side * 600);
        }
    }
    /// <summary>A box commander does the same sum, but the sum changes with how far along the run is and what is in sight: see Air.TurnBackAt.</summary>
    void BoxCommanders()
    {
        foreach (var fm in air.Formations.ToList())      // a copy: a box may split as we go
        {
            if (fm.Phase != Phase.Outbound || fm.Aborted) continue;
            if (Design.Of[fm.Kind].Dash && All.Any(f => f.Wolf != fm.Wolf && f.Airborne && Dist(f.X, f.Y, fm.Lead.X, fm.Lead.Y) < Sight)) fm.DashUntil = T + 120;
            if (!fm.Wolf && Design.Of[fm.Kind].Strike && !fm.SplitDone && fm.Ac.Count >= 8 && (fm.Attackers >= 4 || 1 - fm.Ac.Count / (double)Math.Max(1, fm.Start) > .12)) SplitBox(fm);
            int inBox = fm.Ac.Count(a => !a.Straggler), seen = All.Count(f => f.Wolf != fm.Wolf && f.Airborne && Dist(f.X, f.Y, fm.Lead.X, fm.Lead.Y) < Sight);
            double lost = 1 - inBox / (double)Math.Max(1, fm.Start), prog = air.Progress(fm);
            if (lost <= Air.TurnBackAt(prog, seen, inBox, fm.Wolf, fm.Nerve)) continue;
            Air.Abort(fm); fm.CommanderTurned = true; BoxesTurnedBack++; fm.TurnWhy = $"turned back: {lost * 100:0}% lost, {prog * 100:0}% there, {seen} seen";
        }
    }
}
