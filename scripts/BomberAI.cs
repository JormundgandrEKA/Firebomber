using System;
using System.Collections.Generic;
using System.Linq;

namespace Firebomber;

/// <summary>What happens to a bomber, and what its captain decides. In a box a bomber follows the leader; once it cannot, it is on its own: press on alone, drop the bombs to stay up, or make for the nearest field it can reach.</summary>
public sealed partial class Air
{
    public static readonly string[] WolfCaptains = { "Aalto", "Brandt", "Ekman", "Hallberg", "Isaksen", "Krogh", "Lindqvist", "Nyberg", "Rydell", "Sundin", "Thorsen", "Wik" }, DeerCaptains = { "Andreev", "Bykov", "Chernov", "Denisov", "Egorov", "Fomin", "Grachev", "Isaev", "Kovalev", "Lapin", "Maslov", "Noskov" };
    public readonly Dictionary<string, int> LostTo = new();
    public int UcsBombersLost, Jettisoned, PressedOn, Ditched;
    /// <summary>Bombers whose last wound was from flak, by side: 0 UCS, 1 Vulfen.</summary>
    public readonly int[] FlakLost = new int[2];
    public Bomber? Watch;
    public readonly List<Bomber> Returned = new();     // came back by parachute or dinghy: they fly again
    double hullT; const double CompanyM = 25000;

    void Hulls()
    {
        hullT -= Dt; if (hullT > 0) return; hullT = 1;
        foreach (var f in Formations)
        {
            if (f.Phase is Phase.Waiting or Phase.Landed) continue; int n = 0;
            foreach (var o in Formations) if (o.Wolf == f.Wolf && o.Phase is not (Phase.Waiting or Phase.Landed) && Dist(o.Lead.X, o.Lead.Y, f.Lead.X, f.Lead.Y) < CompanyM) n += o.Ac.Count;
            f.Company = Math.Max(1, n);
        }
        foreach (var f in Formations)
        {
            if (f.Phase is Phase.Waiting or Phase.Landed) continue;
            foreach (var a in f.Ac)
            {
                a.H.Second(rng, 1); if (a.H.Dead) { burnedOut.Add(a); continue; }
                if (T >= a.NextThink) { a.NextThink = T + 3 + rng.NextDouble() * 2; Captain(a); }
            }
        }
    }

    /// <summary>Called by anything that shoots a bomber.</summary>
    public void Hit(Bomber a, Weapon w, int hits, double critMul = 1, string by = "fighters")
    {
        if (!a.Alive) return;
        a.H.By = by; a.H.Take(w, hits, rng, T, critMul);
        if (a.H.Dead) Lose(a);
    }
    public void Ram(Bomber a)
    {
        if (!a.Alive) return;
        a.H.Struct -= a.H.D.Struct * .75; a.H.Eng[rng.Next(a.H.Eng.Length)] = 0; a.H.Note = "rammed"; a.H.Cause = "ram"; if (a.H.Dead) Lose(a);
    }

    void Captain(Bomber a)
    {
        var f = a.F; var d = a.H.D; double power = a.H.Power, topNow = Weight.Top(d, a.Kg), cruiseNow = Weight.Cruise(d, f.Lead.Kg);
        if (!a.Straggler)
        {
            if (f.Phase is Phase.Waiting) return;
            bool cannotKeepUp = power * topNow < cruiseNow || a.H.Sink > 0;
            if (a == f.Lead && a.H.Working < d.Engines && f.Ac.Any(q => q != a && !q.Straggler)) { NewLeadExcept(f, a); a.Why = "handed over the lead: engine out"; return; }
            if (cannotKeepUp) { FallOut(a, a.H.Sink > 0 ? "losing height, cannot hold the box" : "too slow to hold the box"); }
            else if (a.H.Damage > .8) FallOut(a, "too badly damaged to hold the box");
            else if (a.H.Burning && a.H.Damage > .5) FallOut(a, "on fire and falling behind");
            else a.Why = f.Phase == Phase.Returning ? "with the box, going home" : a.H.Burning ? "fighting a fire, holding station" : a.H.Damage > .3 ? "damaged, holding station" : a == f.Lead ? "leading the box" : "in formation";
            return;
        }
        double fuelRange = a.H.Fuel * Weight.Cruise(d, a.Kg) * Math.Max(.4, power), toHome = Dist(a.X, a.Y, a.HomeX, a.HomeY);
        // bombs still aboard: this captain's choice
        if (!a.Released && double.IsNaN(a.DropAt))
        {
            double p = Progress(f); bool sound = a.H.Sink == 0 && power >= .7 && !a.H.Burning && a.H.Damage < .6;
            if (!a.Press && sound && f.Phase == Phase.Outbound && p + a.Nerve * .35 > 1 && fuelRange > toHome * 1.1) { a.Press = true; PressedOn++; a.Why = $"pressing on alone: {(1 - p) * 100:0}% of the way left"; }
            else if (!sound && !a.Press) { Jettison(a); a.Why = "bombs gone, to stay in the air"; }
            else if (a.Press && !sound) { a.Press = false; Jettison(a); a.Why = "can't go on: bombs gone"; }
        }
        // can it get home? If not, the nearest friendly field; if not that, as far as it will go
        if (!a.Press && fuelRange < toHome)
        {
            var near = (a.F.Wolf ? w.Fields : w.EnemyFields).OrderBy(q => Dist(q.X, q.Y, a.X, a.Y)).FirstOrDefault();
            if (near != null && Dist(near.X, near.Y, a.X, a.Y) < toHome) { a.HomeX = near.X; a.HomeY = near.Y; a.HomeName = near.Name; toHome = Dist(a.X, a.Y, a.HomeX, a.HomeY); }
            a.Why = fuelRange < toHome ? $"will not reach {a.HomeName}: going as far as possible" : $"low on fuel: making for {a.HomeName}";
        }
        else if (!a.Press && a.Released) a.Why = a.H.Sink > 0 ? $"losing height, nursing it to {a.HomeName}" : $"alone, heading for {a.HomeName}";
    }
    void FallOut(Bomber a, string why)
    {
        if (a.Straggler) return;
        a.Straggler = true; a.Why = why; var f = a.F;
        if (a == f.Lead) NewLeadExcept(f, a);
    }
    void NewLeadExcept(Formation f, Bomber old)
    {
        var next = f.Ac.Where(q => q != old && q.Alive && !q.Straggler && q.H.Working == q.H.D.Engines).OrderByDescending(q => q.X * Math.Cos(q.Th) + q.Y * Math.Sin(q.Th)).FirstOrDefault()
                ?? f.Ac.Where(q => q != old && q.Alive && !q.Straggler).FirstOrDefault();
        if (next != null) { f.Lead = next; Reslot(f); return; }
        var any = f.Ac.FirstOrDefault(q => q.Alive && q != old); if (any == null) return;
        f.Lead = any; f.Route.Clear(); if (f.Phase == Phase.Outbound) { f.Phase = Phase.Returning; f.TurnWhy = "nobody left in formation"; }
    }
    void Jettison(Bomber a)
    {
        a.Released = true; Jettisoned++; var plan = a.F.Plan; int n = plan.Count; a.Kg = 0; double tf = Math.Sqrt(2 * Math.Max(a.Z, 1) / G) * DragFactor;
        for (int k = 0; k < n; k++)
        {
            var i = new Impact { T = T + k * ReleaseInterval + tf, X = a.X + Math.Cos(a.Th) * a.V * (tf + k * ReleaseInterval) + Gauss(120), Y = a.Y + Math.Sin(a.Th) * a.V * (tf + k * ReleaseInterval) + Gauss(120), Incendiary = !plan[k].He, Kg = plan[k].Kg, Bomblets = plan[k].Bomblets };
            falling.Enqueue(i, i.T);
        }
    }
    void Lose(Bomber a)
    {
        if (!a.Alive) return;
        a.Alive = false; var f = a.F; f.Ac.Remove(a); if (f.Wolf) BombersLost++; else UcsBombersLost++; if (a.H.By == "flak") FlakLost[f.Wolf ? 1 : 0]++;
        string c = a.H.CauseOr; LostTo.TryGetValue(c, out int n); LostTo[c] = n + 1;
        bool back = Crews.Down(a.H, w, rng, a.X, a.Y, a.Z, f.Wolf, c); if (back) Returned.Add(a); else Crews.CaptainsLost++;
        if (f.Ac.Count == 0) { f.Destroyed = f.BackEarly == 0; f.Phase = Phase.Landed; }
        else if (a == f.Lead) NewLeadExcept(f, a);
        Downed?.Invoke(a);
    }
    void Ditch(Bomber a)
    {
        Ditched++; a.H.Cause = "ditched"; a.Why = "ditched"; Lose(a);
    }
    /// <summary>How far along its way this box is: distance flown over distance flown plus distance left.</summary>
    public double Progress(Formation f)
    {
        if (f.Phase != Phase.Outbound) return f.Phase == Phase.Waiting ? 0 : 1;
        double left = 0, x = f.Lead.X, y = f.Lead.Y;
        foreach (var r in f.Route) { left += Dist(x, y, r.x, r.y); x = r.x; y = r.y; }
        left += Dist(x, y, f.AimX, f.AimY);
        return f.Flown / Math.Max(1, f.Flown + left);
    }
    /// <summary>The losses a commander will accept before turning a box back, at this point in the run. If fighters are in sight: low early, stoic near the target (10% to half way, 25% at two thirds, 60% at the end). With none in sight a box goes on further, which may be a false comfort.</summary>
    public static double TurnBackAt(double progress, int fightersInSight, int boxNow, bool wolf, double nerve)
    {
        double t = progress <= .5 ? .10 : progress <= 2.0 / 3 ? .10 + (progress - .5) * 6 * .15 : .25 + (progress - 2.0 / 3) * 3 * .35;
        if (fightersInSight == 0) t += .3; else if (fightersInSight <= boxNow) t += .08;
        if (!wolf) t += .1;     // honour, not sums: the UCS crews hold on longer
        return Math.Min(.9, t + (nerve - .5) * .1);
    }
}
