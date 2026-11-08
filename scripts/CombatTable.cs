using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Firebomber;

/// <summary>Run with --combattable file. How fights go, measured: bursts each type takes before it goes down, how often each fighter fires and hits,
/// whether interceptors spread over the boxes or pile onto one, whether they attack or loiter, and whether escorts fight. Eight runs of each.</summary>
public partial class Main
{
    void CombatTable(string path)
    {
        var jobs = new List<(string name, Craft[] kinds, bool wolf, string target, int esc, int perField, int def)>
        {
            ("UCS TB-6 x54 on Hjalmar, 6 F-5/field", new[] { Craft.TB6 }, false, "Hjalmar", 0, 0, 6),
            ("UCS SB-3 x108 on Hjalmar, 6 F-5/field", new[] { Craft.SB3 }, false, "Hjalmar", 0, 0, 6),
            ("UCS SB-3 x108, 4 I-8/box, 6 F-5/field", new[] { Craft.SB3 }, false, "Hjalmar", 4, 0, 6),
            ("UCS mix 12 boxes, 2 I-8/box, 6 F-5/field", new[] { Craft.TB6, Craft.SB3, Craft.SB3 }, false, "Hjalmar", 2, 0, 6),
            ("U-4 x36 on Kolvograd, no escort", new[] { Craft.U4 }, true, "Kolvograd", 0, 24, 0),
            ("U-4 x36 on Kolvograd, 4 F-5/box", new[] { Craft.U4 }, true, "Kolvograd", 4, 24, 0),
            ("V-16 x72 on Kolvograd, no escort", new[] { Craft.V16 }, true, "Kolvograd", 0, 24, 0),
            ("V-16 x72 on Kolvograd, 4 F-5/box", new[] { Craft.V16 }, true, "Kolvograd", 4, 24, 0),
            ("Mixed raid on Kolvograd, 4 F-5/box", new[] { Craft.V16, Craft.V16, Craft.U4 }, true, "Kolvograd", 4, 24, 0),
        };
        var lines = new string[jobs.Count]; var detail = new string[jobs.Count];
        static double D2(double ax, double ay, double bx, double by) => Math.Sqrt((ax - bx) * (ax - bx) + (ay - by) * (ay - by));
        System.Threading.Tasks.Parallel.For(0, jobs.Count, j =>
        {
            var q = jobs[j]; const int Reps = 8; double lostBefore = 0, lostAfter = 0, lead = 0; int leads = 0; double start = 0, lost = 0, ucsF = 0, wolfF = 0, back = 0, maxShare = 0, untouched = 0, boxes = 0, splits = 0;
            double attackS = 0, loiterS = 0, escS = 0, escFightS = 0, escNearS = 0, ucsUp = 0, wolfUp = 0;
            var idle = new Dictionary<string, double>(); var left = new Dictionary<string, int>(); double passes = 0, i8s = 0, sqPasses = 0, sqs = 0; var died = new Dictionary<string, int>(); var kb = new Dictionary<Craft, List<int>>(); var gun = new Dictionary<Craft, double[]>(); var sorties = new Dictionary<Craft, int>();
            for (int r = 0; r < Reps; r++)
            {
                var a = new Air(World, 300 + j * 10 + r); bool big = q.kinds.Length == 3 && !q.wolf; a.TestRaid(World.City(q.target), big ? 4 : 1, q.kinds, q.wolf, 0, big ? 900 : 0);
                var s = new FighterSim(World, a, q.wolf ? q.esc : 0, q.perField, 50 + j * 10 + r, q.def, q.wolf ? 0 : q.esc);
                var startBoxes = a.Formations.ToList(); start = a.Formations.Sum(f => f.Start); var lostBy = new Dictionary<string, int>(); var was = a.Downed;
                a.Downed = b => { was?.Invoke(b); var nm = b.F.Name.TrimEnd('b', 'c'); lostBy[nm] = lostBy.GetValueOrDefault(nm) + 1; if (b.Released || b.F.Phase != Phase.Outbound) lostAfter++; else lostBefore++; }; double firstAttack = -1, firstBomb = -1;
                for (int n = 0; n < 4000 && (a.Formations.Any(f => f.Phase != Phase.Landed) || s.All.Any(f => f.Airborne)); n++)
                {
                    a.Advance(10); a.Recent.Clear(); s.Tracers.Clear();
                    if (firstAttack < 0 && s.All.Any(f => f.Wolf != q.wolf && f.Act == Act.AttackBomber)) firstAttack = a.T; if (firstBomb < 0 && a.Hits.Count > 0) firstBomb = a.T;
                    foreach (var f in s.All)
                    {
                        if (!f.Airborne) continue;
                        if (f.Wolf != q.wolf && f.Charge == null)
                        {
                            // an interceptor near enemy bombers: attacking, or hanging about
                            bool near = a.Formations.Any(b => b.Wolf == q.wolf && b.Phase is Phase.Outbound or Phase.Egress or Phase.Returning && D2(b.Lead.X, b.Lead.Y, f.X, f.Y) < 8000);
                            if (near) { if (f.Act is Act.AttackBomber && f.TB != null && D2(f.TB.X, f.TB.Y, f.X, f.Y) < 1200 || f.Act is Act.Fight or Act.Extend) attackS += 10; else { loiterS += 10; idle[f.Act + (f.Act == Act.AttackBomber ? "/" + f.Pass : "")] = idle.GetValueOrDefault(f.Act + (f.Act == Act.AttackBomber ? "/" + f.Pass : "")) + 10; } }
                        }
                        if (f.Wolf && f.Charge != null && f.Charge.Phase == Phase.Outbound && f.Act is not (Act.Home or Act.Climb))
                        {
                            escS += 10; if (D2(f.X, f.Y, f.Charge.Lead.X, f.Charge.Lead.Y) < 7000) escNearS += 10; if (f.Act is Act.Fight or Act.Zoom) escFightS += 10;
                        }
                    }
                }
                if (firstAttack > 0 && firstBomb > 0) { lead += firstBomb - firstAttack; leads++; }
                lost += a.BombersLost + a.UcsBombersLost; ucsF += s.UcsLost; wolfF += s.WolfLost; back += s.BoxesTurnedBack;
                ucsUp += s.All.Count(f => !f.Wolf && f.Act != Act.Ground); wolfUp += s.All.Count(f => f.Wolf && f.Act != Act.Ground);
                int tot = lostBy.Values.Sum(); if (tot > 0) maxShare += lostBy.Values.Max() / (double)tot; untouched += startBoxes.Count(b => !lostBy.ContainsKey(b.Name)); boxes += startBoxes.Count; splits += a.Formations.Count - startBoxes.Count;
                foreach (var kv in s.KillBursts) { if (!kb.ContainsKey(kv.Key)) kb[kv.Key] = new(); kb[kv.Key].AddRange(kv.Value); }
                foreach (var kv in s.Gunnery) { if (!gun.ContainsKey(kv.Key)) gun[kv.Key] = new double[4]; for (int i = 0; i < 4; i++) gun[kv.Key][i] += kv.Value[i]; }
                foreach (var f in s.All) if (!f.Wolf && f.Charge == null && f.Act != Act.Ground) { passes += f.Passes; i8s++; }
                foreach (var w in s.Wings) if (!w.Wolf && w.Box == null && w.Pilots.Any(p => p.Act != Act.Ground)) { sqPasses += w.Passes; sqs++; }
                foreach (var f in s.All) if (f.Wolf && !f.Alive) died[f.ActAtDeath] = died.GetValueOrDefault(f.ActAtDeath) + 1;
                foreach (var kv in s.LeftBecause) left[kv.Key] = left.GetValueOrDefault(kv.Key) + kv.Value;
                foreach (var f in s.All) if (f.Act != Act.Ground) sorties[f.Kind] = sorties.GetValueOrDefault(f.Kind) + 1;
            }
            string K(Craft c) => Sprites.Specs[c].Name.Split(' ')[0];
            var sb = new StringBuilder();
            sb.Append($"{q.name,-42} lost {100 * lost / Reps / start,4:0}% of {start,3}  back {back / Reps,3:0.0}  I-8 down {ucsF / Reps,4:0.0} of {ucsUp / Reps,3:0}  F-5 down {wolfF / Reps,4:0.0} of {wolfUp / Reps,3:0}");
            sb.Append($"  | lost before bombing {100 * lostBefore / Math.Max(1, lostBefore + lostAfter),3:0}%, first attack {(leads > 0 ? lead / leads / 60 : 0),4:0} min before the bombs");
            sb.Append($"  | worst box {100 * maxShare / Reps,3:0}% of losses, {100 * untouched / Math.Max(1, boxes),3:0}% boxes untouched{(splits > 0 ? $", {splits / Reps:0.0} splits" : "")}");
            sb.Append($"  | interceptors within 8 km of bombers that are within 1.2 km of one or fighting {100 * attackS / Math.Max(1, attackS + loiterS),3:0}%");
            sb.Append($"  (the rest: {string.Join(", ", idle.OrderByDescending(k => k.Value).Take(5).Select(k => $"{k.Key} {100 * k.Value / Math.Max(1, attackS + loiterS):0}%"))})");
            if (escS > 0) sb.Append($"  | escorts within 7 km {100 * escNearS / escS:0}%, fighting {100 * escFightS / escS:0}%");
            var dt = new StringBuilder();
            foreach (var kv in kb.OrderBy(k => k.Key)) { var l = kv.Value.OrderBy(x => x).ToList(); dt.Append($"   {K(kv.Key)} downed by fighters {l.Count / (double)Reps,4:0.0}/run, hitting bursts to kill mean {l.Average(),4:0.0} median {l[l.Count / 2]}, 1-2: {100.0 * l.Count(x => x <= 2) / l.Count,3:0}%\n"); }
            foreach (var kv in gun.OrderBy(k => k.Key)) { var g = kv.Value; int so = Math.Max(1, sorties.GetValueOrDefault(kv.Key)); dt.Append($"   {K(kv.Key)} gunnery: {g[0] / so,5:0.0} bursts per sortie, {100 * g[1] / Math.Max(1, g[0]),3:0}% hit, {g[2] / Math.Max(1, g[1]),4:0.0} hits per hitting burst, {g[3] / so,5:0.0} s firing per sortie\n"); }
            dt.Append($"   left because: {string.Join(", ", left.OrderByDescending(k => k.Value).Take(6).Select(k => $"{k.Key} {k.Value / (double)Reps:0.0}"))}\n");
            if (i8s > 0) dt.Append($"   interceptor passes at bombers per sortie {passes / i8s:0.0}, squadron attacks per squadron {sqPasses / Math.Max(1, sqs):0.0}\n");
            if (died.Count > 0) dt.Append($"   Hunds lost while: {string.Join(", ", died.OrderByDescending(k => k.Value).Select(k => $"{k.Key} {k.Value / (double)Reps:0.0}"))}\n");
            lines[j] = sb.ToString(); detail[j] = dt.ToString();

        });
        var o = new StringBuilder(); for (int j = 0; j < jobs.Count; j++) { o.AppendLine(lines[j]); o.Append(detail[j]); }
        System.IO.File.WriteAllText(path, o.ToString()); GetTree().Quit();
    }
}
