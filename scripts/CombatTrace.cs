using System;
using System.Linq;
using System.Text;

namespace Firebomber;

/// <summary>Run with --combattrace file: one unescorted V-16 raid on Kolvograd, following the I-8 squadrons that engage, every twenty seconds. Where each is relative to its box (metres ahead of it, to the side, above), its phase, and what its pilots are doing.</summary>
public partial class Main
{
    /// <summary>Run with --defencetrace file: a UCS TB-6 raid on Hjalmar against the Hunds on alert, once a minute: how far the raid is, when the alert went, and where the defenders are and what they are doing.</summary>
    void DefenceTrace(string path)
    {
        var o = new StringBuilder(); var hj = World.City("Hjalmar");
        var a = new Air(World, 702); a.TestRaid(hj, 1, new[] { Craft.TB6 }, false);
        var s = new FighterSim(World, a, 0, 0, 92, 6, 0);
        static double D(double ax, double ay, double bx, double by) => Math.Sqrt((ax - bx) * (ax - bx) + (ay - by) * (ay - by));
        for (int n = 0; n < 600 && (a.Formations.Any(f => f.Phase != Phase.Landed) || s.All.Any(f => f.Airborne)); n++)
        {
            a.Advance(60); a.Recent.Clear(); s.Tracers.Clear(); var l = a.Formations.OrderBy(f => D(f.Lead.X, f.Lead.Y, hj.X, hj.Y)).First().Lead;
            var up = s.All.Where(f => f.Wolf && f.Airborne).ToList();
            o.AppendLine($"T+{a.T / 60,3:0}  raid {D(l.X, l.Y, hj.X, hj.Y) / 1000,4:0} km from Hjalmar at {l.Z:0} m, {a.Formations.Count(f => f.Phase == Phase.Outbound)} boxes in, bombs {a.Hits.Count}, lost {a.UcsBombersLost}  alert {(double.IsNaN(s.AlertT) ? "-" : $"T+{s.AlertT / 60:0}")}  Hunds up {up.Count}"
                + (up.Count > 0 ? $", mean {up.Average(f => D(f.X, f.Y, l.X, l.Y)) / 1000:0} km from the raid, height {up.Average(f => f.Z):0} m: " + string.Join(", ", up.GroupBy(f => f.Act).Select(g => $"{g.Key} {g.Count()}")) + "  e.g. \"" + up[0].Why + "\"" : ""));
        }
        System.IO.File.WriteAllText(path, o.ToString()); GetTree().Quit();
    }

    /// <summary>Run with --hunttrace file: a V-16 raid on Kolvograd, by day and by night, against I-8s from their fields. Once a minute: what the interceptors are doing, how many are close to the bombers without attacking, how many the raid has gone past, and the losses before and after the bombs.</summary>
    void HuntTrace(string path)
    {
        var o = new StringBuilder();
        static double D(double ax, double ay, double bx, double by) => Math.Sqrt((ax - bx) * (ax - bx) + (ay - by) * (ay - by));
        foreach (double hour in new double[] { 10, 22 })
        {
            var a = new Air(World, 306, 0, hour); a.TestRaid(World.City("Kolvograd"), 1, new[] { Craft.V16 }, true);
            var s = new FighterSim(World, a, 0, 24, 56, 0, 0);
            o.AppendLine($"=== V-16 raid on Kolvograd, take-off {hour}:00 ===");
            int before = -1, everIdle = 0, everPast = 0, n0 = 0; double firstAttack = double.NaN, firstBomb = double.NaN;
            for (int n = 0; n < 1500 && (a.Formations.Any(f => f.Phase != Phase.Landed) || s.All.Any(f => f.Airborne)); n++)
            {
                a.Advance(20); a.Recent.Clear(); s.Tracers.Clear();
                if (before < 0 && a.Hits.Any(h => D(h.X, h.Y, World.City("Kolvograd").X, World.City("Kolvograd").Y) < 15000)) { before = a.BombersLost; firstBomb = a.T; }      // on the city: bombs jettisoned on the way in do not count
                var up = s.All.Where(f => !f.Wolf && f.Airborne).ToList();
                if (double.IsNaN(firstAttack) && up.Any(f => f.Act == Act.AttackBomber)) firstAttack = a.T;
                var boxes = a.Formations.Where(f => f.Wolf && f.Phase == Phase.Outbound && f.Ac.Count > 0).ToList();
                int idle = 0, past = 0; double sumD = 0; int hunts = 0;
                foreach (var f in up.Where(f => f.Act is Act.Hunt or Act.Patrol or Act.Climb))
                {
                    if (boxes.Count == 0) break;
                    var b = boxes.OrderBy(q => D(q.Lead.X, q.Lead.Y, f.X, f.Y)).First().Lead; double d = D(b.X, b.Y, f.X, f.Y);
                    double along = (f.X - b.X) * Math.Cos(b.Th) + (f.Y - b.Y) * Math.Sin(b.Th);
                    hunts++; sumD += d; if (d < 8000) idle++; if (along < -3000 && d < 30000) past++;
                }
                everIdle += idle; everPast += past;
                if (n % 3 == 0 && up.Count > 0)
                {
                    var l = boxes.Count > 0 ? boxes[0].Lead : null;
                    o.AppendLine($"T+{a.T / 60,3:0}  raid {(l == null ? "-" : $"{D(l.X, l.Y, World.City("Kolvograd").X, World.City("Kolvograd").Y) / 1000,4:0} km out")}  dark {a.Dark:0.0}  up {up.Count}  lost {a.BombersLost}  hunting {hunts} (mean {(hunts > 0 ? sumD / hunts / 1000 : 0):0} km from a box)  within 8 km not attacking {idle}  raid gone past {past}  "
                        + string.Join(", ", up.GroupBy(f => f.Act).Select(g => $"{g.Key} {g.Count()}")) + (up.Any(f => f.Act == Act.Hunt) ? "  e.g. \"" + up.First(f => f.Act == Act.Hunt).Why + "\"" : ""));
                }
            }
            if (before < 0) before = a.BombersLost;
            o.AppendLine($"SUMMARY {hour}:00  lost {a.BombersLost}, of them before the bombs {before}  first attack {(double.IsNaN(firstAttack) ? "never" : $"{(firstBomb - firstAttack) / 60:0} min before the bombs")}  fighter-samples near and idle {everIdle}, gone past {everPast}");
            o.AppendLine("   left because: " + string.Join("; ", s.LeftBecause.OrderByDescending(k => k.Value).Select(k => $"{k.Key} {k.Value}")));
            o.AppendLine($"   UCS fighters lost {s.UcsLost}, passes on bombers {s.All.Where(f => !f.Wolf).Sum(f => f.Passes)}");
            o.AppendLine();
        }
        System.IO.File.WriteAllText(path, o.ToString()); GetTree().Quit();
    }

    /// <summary>Run with --accuracytable file: a box of 24 V-16s, undefended, from several heights by day and by night: how close the bombs fall to the aim point.</summary>
    void AccuracyTable(string path)
    {
        var o = new StringBuilder(); var c = World.City("Kolvograd"); o.AppendLine("height   light   within 100 m   300 m   1 km   median miss");
        foreach (double z in new double[] { 1000, 2000, 3000, 4500, 6000, 8000 })
            foreach (double hour in new double[] { 10, 22 })
            {
                int all = 0, a = 0, b = 0, d = 0; var miss = new System.Collections.Generic.List<double>();
                for (int seed = 1; seed <= 12; seed++)
                {
                    var air = new Air(World, seed, 0, hour); air.TestRaid(c, 1, new[] { Craft.V16 }, true);
                    foreach (var f in air.Formations) f.TargetAlt = Math.Min(z, Weight.Roof(Design.Of[f.Kind], f.LoadKg));
                    for (int n = 0; n < 9000 && air.Formations.Any(f => f.Phase is Phase.Waiting or Phase.Outbound); n++) air.Advance(5);
                    air.Advance(120);
                    foreach (var h in air.Hits) { foreach (var f in air.Formations.Take(1)) { } var f0 = air.Formations.OrderBy(f => (f.AimX - h.X) * (f.AimX - h.X) + (f.AimY - h.Y) * (f.AimY - h.Y)).First(); double m = Math.Sqrt((h.X - f0.AimX) * (h.X - f0.AimX) + (h.Y - f0.AimY) * (h.Y - f0.AimY)); miss.Add(m); all++; if (m < 100) a++; if (m < 300) b++; if (m < 1000) d++; }
                }
                miss.Sort(); o.AppendLine($"{z,6:0}   {(hour > 18 ? "night" : "day  ")}   {100.0 * a / Math.Max(1, all),10:0}%   {100.0 * b / Math.Max(1, all),4:0}%   {100.0 * d / Math.Max(1, all),3:0}%   {(miss.Count > 0 ? miss[miss.Count / 2] : 0),8:0} m   ({all} bombs)");
            }
        System.IO.File.WriteAllText(path, o.ToString()); GetTree().Quit();
    }

    /// <summary>Run with --damagetable file: 72 undefended V-16s on Kolvograd by night, with each load in each kind of weather: what was blown down, what burned, who died, and how long it went on.</summary>
    void DamageTable(string path)
    {
        var o = new StringBuilder(); var c = World.City("Kolvograd"); o.AppendLine("load       weather         tonnes  blasted  burned out  storm  dead  (a tonne)  hurt  homeless  fire lasted  peak burning");
        foreach (var (lname, he) in new[] { ("all HE", 30), ("half", 15), ("fire", 6) })
            foreach (var (wname, dry, wind, rain) in new[] { ("wet, calm", .1, 3.0, .4), ("ordinary", .4, 6.0, 0.0), ("dry, gale", .9, 20.0, 0.0) })
            {
                double tons = 0, blasted = 0, burned = 0, dead = 0, hurt = 0, home = 0, hours = 0, peak = 0; int storms = 0; const int Reps = 3;
                for (int seed = 1; seed <= Reps; seed++)
                {
                    var fire = new FireSim(World, 40 + seed) { Climate = _ => (dry, wind, rain) }; var air = new Air(World, seed, 0, 22); air.TestRaid(c, 1, new[] { Craft.V16 }, true); air.Landed = fire.Hit; fire.Hour = () => air.Hour;
                    foreach (var f in air.Formations) Air.SetLoad(f, he);
                    double first = double.NaN;
                    for (int n = 0; n < 9000 && air.Formations.Any(f => f.Phase is Phase.Waiting or Phase.Outbound); n++) { air.Advance(5); fire.SyncTo(air.T); fire.Changed.Clear(); fire.Craters.Clear(); if (double.IsNaN(first) && air.Hits.Count > 0) first = air.T; air.Recent.Clear(); }
                    for (int n = 0; n < 60; n++) { air.Advance(5); fire.SyncTo(air.T); fire.Changed.Clear(); fire.Craters.Clear(); air.Recent.Clear(); }
                    double t = air.T, last = t;
                    while (t < air.T + 24 * 3600 && fire.Cities.Any(q => q.Burning.Count > 5)) { t += 60; fire.SyncTo(t); fire.Changed.Clear(); peak = Math.Max(peak, fire.Cities.Sum(q => q.Burning.Count)); last = t; }
                    t += 7200; fire.SyncTo(t); fire.Changed.Clear();
                    var cf = fire.Cities.First(q => q.P == c); var r = fire.Rec(c); tons += air.Tonnes; for (int m = 0; m < 3; m++) { blasted += cf.Count[m, FireSim.Blasted]; burned += cf.Count[m, FireSim.Gutted]; }
                    dead += r.Dead; hurt += r.Injured; home += r.Homeless; hours += (last - first) / 3600; if (r.Firestorms > 0) storms++;
                }
                o.AppendLine($"{lname,-10} {wname,-12} {tons / Reps,8:0} {blasted / Reps,8:0} {burned / Reps,11:0} {storms,4}/{Reps} {dead / Reps,7:0} {dead / Math.Max(1, tons),7:0.0} {hurt / Reps,7:0} {home / Reps,9:0} {hours / Reps,8:0.0} h {peak,9:0}");
            }
        System.IO.File.WriteAllText(path, o.ToString()); GetTree().Quit();
    }

    void CombatTrace(string path)
    {
        var o = new StringBuilder();
        var a = new Air(World, 306); a.TestRaid(World.City("Kolvograd"), 1, new[] { Craft.V16 }, true);
        var s = new FighterSim(World, a, 0, 24, 56, 0, 0);
        static double D(double ax, double ay, double bx, double by) => Math.Sqrt((ax - bx) * (ax - bx) + (ay - by) * (ay - by));
        for (int n = 0; n < 3000 && (a.Formations.Any(f => f.Phase != Phase.Landed) || s.All.Any(f => f.Airborne)); n++)
        {
            a.Advance(20); a.Recent.Clear(); s.Tracers.Clear();
            if (n % 6 == 0) o.AppendLine($"T+{a.T / 60:0.0} min  alert {(double.IsNaN(s.AlertT) ? "none" : $"T+{s.AlertT / 60:0.0}")}  I-8 up {s.All.Count(p => !p.Wolf && p.Airborne)}, mean height {(s.All.Any(p => !p.Wolf && p.Airborne) ? s.All.Where(p => !p.Wolf && p.Airborne).Average(p => p.Z) : 0):0} m, box A {D(a.Formations[0].Lead.X, a.Formations[0].Lead.Y, World.City("Kolvograd").X, World.City("Kolvograd").Y) / 1000:0} km from Kolvograd at {a.Formations[0].Lead.Z:0} m, over the south half {World.North(a.Formations[0].Lead.X, a.Formations[0].Lead.Y) == false}  by act: {string.Join(", ", s.All.Where(p => !p.Wolf && p.Airborne).GroupBy(p => p.Act).Select(g => $"{g.Key} {g.Count()} at {g.Average(p => p.Z):0} m, {g.Average(p => p.V):0} m/s"))}");
            var wings = s.Wings.Where(w => !w.Wolf && w.Pilots.Any(p => p.Airborne) && w.Target != null && w.Target.Ac.Count > 0 && D(w.Pilots.First(p => p.Airborne).X, w.Pilots.First(p => p.Airborne).Y, w.Target.Lead.X, w.Target.Lead.Y) < 40000).ToList();
            if (wings.Count == 0) continue;
            o.AppendLine($"T+{a.T / 60:0.0} min  V-16 lost {a.BombersLost}  boxes: {string.Join(" ", a.Formations.Select(f => $"{f.Name}:{f.Phase.ToString()[0]}{f.Ac.Count}@{f.Lead.V:0}m/s"))}");
            foreach (var w in wings)
            {
                var b = w.Target!.Lead; double hx = Math.Cos(b.Th), hy = Math.Sin(b.Th);
                string Rel(Fighter p) { double al = (p.X - b.X) * hx + (p.Y - b.Y) * hy, la = (p.X - b.X) * hy - (p.Y - b.Y) * hx; return $"{al / 1000:+0.0;-0.0}/{la / 1000:+0.0;-0.0}/{(p.Z - b.Z) / 100:+0;-0}"; }
                var up = w.Pilots.Where(p => p.Airborne).ToList();
                o.AppendLine($"   {w.Name,-18} {w.Phase,-7} on {w.Target.Name}  lead {(w.Lead != null ? Rel(w.Lead) : "-")}  up {up.Count}  passes {up.Sum(p => p.Passes)}  " + string.Join(", ", up.GroupBy(p => p.Act + (p.Act == Act.AttackBomber ? "/" + p.Pass : "") + (p.Role != Role.Any ? $"[{p.Role}]" : "")).Select(g => $"{g.Key} {g.Count()}")));
                foreach (var p in up.Where(p => p.Act == Act.AttackBomber).Take(3)) o.AppendLine($"      {p.Name,-8} {p.Act}/{p.Pass} at {p.TB?.F.Name} {Rel(p)} v {p.V:0}  \"{p.Why}\"");
            }
        }
        System.IO.File.WriteAllText(path, o.ToString()); GetTree().Quit();
    }
}
