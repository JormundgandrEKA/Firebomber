using System;
using System.Collections.Generic;
using System.Linq;

namespace Firebomber;

/// <summary>What a squadron is doing as a whole. Free: each pilot for themselves (searching, at night, or broken). Gather: forming up on the leader for an attack. Attack: going in. Reform: rallying for the next pass.</summary>
public enum WPhase { Free, Gather, Attack, Reform }
/// <summary>How interceptors go about it. Direct: straight at the bombers. OnTrack: half to meet them on their track, half waiting ahead of the city.</summary>
public enum InterceptMode { Direct, OnTrack }
/// <summary>A pilot's job in the squadron. Bombers: the main body. Cover: told off to tie up the escort. Sweep and Close: a Vulfen escort group's two halves.</summary>
public enum Role { Any, Bombers, Cover, Sweep, Close }

/// <summary>The squadron layer: the leader decides where the squadron forms up, when it goes in, and who keeps the escort busy; pilots still choose their own targets within that.
/// A UCS squadron gets in front of its box if it can, forms a loose line abreast on its leader, and goes in together head-on; then it rallies on the leader and comes again, pass after pass, until fuel, ammunition or nerve runs out.
/// When Hunds are about, a third of it is told off to mob them, from several sides at once, and drag them into a turning fight. A Vulfen escort group splits: the best half sweeps out to bounce squadrons while they form up, the rest stays close.</summary>
public sealed partial class FighterSim
{
    double tacT;

    void Tactics()
    {
        tacT -= Dt; if (tacT > 0) return; tacT = 1;
        foreach (var w in Wings)
        {
            if (w.Wolf) { if (w.Box != null) EscortRoles(w); }
            else if (w.Box == null) Squadron(w);
        }
    }

    void EscortRoles(Wing w)
    {
        var up = w.Pilots.Where(p => p.Airborne).OrderByDescending(p => p.Skill + p.Aggression).ToList();
        for (int i = 0; i < up.Count; i++) up[i].Role = i < (up.Count + 1) / 2 ? Role.Sweep : Role.Close;
    }

    void Squadron(Wing w)
    {
        var up = w.Pilots.Where(p => p.Airborne && p.Mood == Mood.Normal && p.Act is not (Act.Home or Act.Ditch or Act.Climb)).ToList();
        void Free() { w.Phase = WPhase.Free; foreach (var p in w.Pilots) p.Role = Role.Any; }
        if (up.Count == 0 || w.Broken) { Free(); return; }
        if (w.Lead == null || !up.Contains(w.Lead)) w.Lead = up.OrderByDescending(p => p.Skill).First();
        var l = w.Lead; var box = w.Target;
        bool live = box != null && box.Ac.Count > 0 && box.Phase is Phase.Outbound or Phase.Egress or Phase.Returning && box.Lead.Z < l.P.Ceil + 40;
        // no squadron tactics at night, and none until the raid is in sight
        if (!live || air.Dark > .6 || Dist(l.X, l.Y, box!.Lead.X, box.Lead.Y) > 30000) { Free(); return; }
        var b = box.Lead; double hx = Math.Cos(b.Th), hy = Math.Sin(b.Th);

        // roles: the leader and most of the squadron for the bombers; the boldest few for the escort, if there is one
        int hunds = All.Count(e => e.Wolf && e.Airborne && Dist(e.X, e.Y, b.X, b.Y) < 10000);
        bool heavies = l.Kind == Craft.I7;      // an I-7 squadron is all for the bombers; an I-8 squadron sends half to tie up the escort
        int cover = hunds == 0 || heavies ? 0 : Math.Min(hunds * 3, Math.Min(up.Count * 2 / 3, Math.Max(2, (int)Math.Ceiling(up.Count * .5))));
        var order = up.Where(p => p != l).OrderByDescending(p => p.Aggression + p.Skill).ToList();
        l.Role = Role.Bombers; for (int i = 0; i < order.Count; i++) order[i].Role = i < cover ? Role.Cover : Role.Bombers;
        int k = 0; foreach (var p in up.Where(p => p.Role == Role.Bombers && p != l)) p.Slot = ++k;

        // where to form: in front of the box if the squadron can get there, otherwise behind and above it for a diving pass
        double along = (l.X - b.X) * hx + (l.Y - b.Y) * hy; int side = w.Name.Sum(c => c) % 2 == 0 ? 1 : -1;
        bool front = along > -1500 || l.P.VMax - b.V > 30, endRun = false;
        if (!front)
        {
            // the box is past: the next one in the stream is coming straight at the squadron, so take that one head-on instead of chasing this one
            Formation? next = null; double nd = 1e12;
            foreach (var fm in air.Formations)
            {
                if (fm.Wolf == w.Wolf || fm == box || fm.Ac.Count == 0 || fm.Phase is not (Phase.Outbound or Phase.Egress or Phase.Returning) || fm.Lead.Z > l.P.Ceil + 40) continue;
                double fa = (l.X - fm.Lead.X) * Math.Cos(fm.Lead.Th) + (l.Y - fm.Lead.Y) * Math.Sin(fm.Lead.Th), dd = Dist(l.X, l.Y, fm.Lead.X, fm.Lead.Y);
                if (fa > 500 && dd < 30000 && dd < nd) { nd = dd; next = fm; }
            }
            if (next == null) { Free(); return; }      // nothing still coming: the squadron breaks up and harasses the stream from astern, each pilot at full throttle
            w.Target = box = next;
            {
 b = box.Lead; hx = Math.Cos(b.Th); hy = Math.Sin(b.Th); along = (l.X - b.X) * hx + (l.Y - b.Y) * hy; front = true;
            if (w.Phase == WPhase.Attack) foreach (var p in up) p.NextThink = T;
            }
        }
        w.EndRun = endRun;
        double fwd = front ? 9000 : -3500, lat = side * (front ? 2500 : 1800);
        if (w.Phase == WPhase.Reform) { fwd = front ? 7000 : -3000; lat = side * 4000; }      // off to the flank to rally, clear of the gunners
        w.GoX = b.X + hx * fwd + hy * lat; w.GoY = b.Y + hy * fwd - hx * lat; w.GoZ = Math.Min(b.Z + (front ? 200 : 700), l.P.Ceil);

        var main = up.Where(p => p.Role == Role.Bombers).ToList();
        switch (w.Phase)
        {
            case WPhase.Free: w.Phase = WPhase.Gather; w.PhaseT = T; break;
            case WPhase.Gather:
            case WPhase.Reform:
            {
                int formed = main.Count(p => Dist(p.X, p.Y, l.X, l.Y) < 2200);
                bool there = Dist(l.X, l.Y, w.GoX, w.GoY) < 3500 || (front && along > 4000);
                if ((there && formed >= .6 * main.Count) || T - w.PhaseT > (w.Phase == WPhase.Gather ? 90 : endRun ? 420 : 60)) { w.Phase = WPhase.Attack; w.PhaseT = T; w.Passes++; foreach (var p in main) p.NextThink = T; }
                break;
            }
            case WPhase.Attack:
            {
                int done = main.Count(p => p.LastPass > w.PhaseT && p.Act != Act.AttackBomber);
                if (done >= .7 * main.Count || T - w.PhaseT > 70) { w.Phase = WPhase.Reform; w.PhaseT = T; }
                break;
            }
        }
    }
}
