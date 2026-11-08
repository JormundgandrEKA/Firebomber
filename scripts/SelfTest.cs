using Godot;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Firebomber;

/// <summary>Adds up where frame time goes, for the bench.</summary>
public static class Prof
{
    static readonly Dictionary<string, (double sum, double max, int n)> t = new();
    public static void Add(string k, double ms) { t.TryGetValue(k, out var v); t[k] = (v.sum + ms, Math.Max(v.max, ms), v.n + 1); }
    public static void Reset() => t.Clear();
    public static string Text => string.Join(", ", t.Select(kv => $"{kv.Key} {kv.Value.sum / Math.Max(1, kv.Value.n):0.0}/{kv.Value.max:0}ms"));
}

/// <summary>Run with --selftest file: real input events through the real game, a label sweep across zoom and position, and a timed zoom from the theatre to field level.</summary>
public partial class Main
{
    async Task Frames(int n) { for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
    async Task Mouse(Vector2 p, MouseButton b, bool down) { Input.ParseInputEvent(new InputEventMouseButton { Position = p, GlobalPosition = p, ButtonIndex = b, Pressed = down }); await Frames(3); }
    async Task Click(Vector2 p) { await Mouse(p, MouseButton.Left, true); await Mouse(p, MouseButton.Left, false); }

    /// <summary>A whole raid through the real controls: fly, take orders, split, abort, bomb, turn back, land.</summary>
    async Task RaidTest(Action<string, bool, string> R, StringBuilder o)
    {
        Vector2 At(string n) => Ui.Named[n].GetCenter();
        Formation F(string n) => Air.Formations.First(f => f.Name == n);
        zoom = MinZoom(); camX = World.WidthM / 2; camY = World.HeightM / 2; Clamp();
        StartPlan(); double planT = Air.T; await Click(At("x256")); await Frames(10); R("nothing flies while the raid is planned", Air.T == planT, "");
        await Click(At("row A")); R("row selects a formation", View.Chosen.Count == 1 && View.Primary == F("A"), "");
        { var bb = Loadouts.Of[Craft.V16].First(l => l.Name == "Break and burn"); double far = Weight.RangeKm(Design.Of[Craft.V16], F("A").Lead.Kg); await Click(At("load Break and burn"));
          R("a preset loads the flight", F("A").Preset == bb && F("A").Lead.Kg == 4000 && F("A").Plan.Count(b => b == Bombs.HE1000) == 2 && F("A").Plan.Count(b => b == Bombs.Cluster250) == 8, Bombs.Describe(F("A").Plan));
          R("a lighter load reaches further", Weight.RangeKm(Design.Of[Craft.V16], 4000) > far, $"{far:0} to {Weight.RangeKm(Design.Of[Craft.V16], 4000):0} km");
          await Click(At("LATER")); R("a later arrival can be set", F("A").ArriveMin == 5, ""); await Click(At("EARLIER"));
          await Click(At("load Half and half"));
          foreach (var kv in Loadouts.Of) foreach (var l in kv.Value) { var ds = Design.Of[kv.Key]; if (l.Kg > ds.MaxKg || l.Plan.Count > ds.Slots || l.Plan.Any(b => b.Kg > ds.MaxBomb)) R($"{kv.Key} {l.Name} fits the aircraft", false, ""); } }
        await Click(At("SEND THE RAID")); R("the raid goes", !Planning && !Clock.Paused, ""); await Frames(3);
        await Click(At("x256")); var sw = Stopwatch.StartNew();
        while (Air.Formations.Any(f => f.Phase == Phase.Waiting) && sw.Elapsed.TotalSeconds < 30) await Frames(1);
        R("all formations take off", Air.Formations.All(f => f.Phase == Phase.Outbound), $"last at T+{Air.T / 60:0} min");
        Clock.Paused = true; await Frames(3);
        { var was = F("A").Plan; Air.SetPreset(F("A"), Loadouts.Of[Craft.V16][4]); R("loads are fixed once away", F("A").Plan == was, ""); }
        await Click(At("ALT +")); R("altitude order", F("A").TargetAlt == 6500, "");
        await Click(At("TIGHT")); R("spacing order", F("A").Spread == .65, "");
        int wp0 = F("A").Route.Count; await Click(At("ROUTE"));
        await Click(new Vector2(1000, 560)); await Mouse(new Vector2(1000, 560), MouseButton.Right, true); await Mouse(new Vector2(1000, 560), MouseButton.Right, false);
        R("route order adds a waypoint", F("A").Route.Count == wp0 + 1 && View.Mode == 0, $"{F("A").Route.Count} waypoints");
        await Click(At("row C")); await Click(At("ABORT")); R("abort turns back", F("C").Phase == Phase.Returning && F("C").Aborted, "");
        await Click(At("row B"));
        camX = F("B").Lead.X; camY = F("B").Lead.Y; zoom = .15; Clamp(); await Frames(3);
        var sxs = F("B").Ac.Select(a => W2S(a.X, a.Y)).OrderBy(p => p.X).ToList(); var boxA = new Vector2(sxs[0].X - 5, sxs.Min(p => p.Y) - 5); var boxB = new Vector2(sxs[12].X, sxs.Max(p => p.Y) + 5);
        Input.ParseInputEvent(new InputEventMouseButton { Position = boxA, GlobalPosition = boxA, ButtonIndex = MouseButton.Left, Pressed = true, ShiftPressed = true }); await Frames(3);
        Input.ParseInputEvent(new InputEventMouseMotion { Position = boxB, GlobalPosition = boxB }); await Frames(3);
        await Mouse(boxB, MouseButton.Left, false);
        int picked = View.Picked.Count; R("shift-drag picks aircraft", picked > 0 && picked < 24, $"{picked} picked");
        await Click(At("NEW")); R("new formation splits off", Air.Formations.Count == 4 && Air.Formations[3].Ac.Count + F("B").Ac.Count == 24, $"{Air.Formations[3].Name} x{Air.Formations[3].Ac.Count}, B x{F("B").Ac.Count}");
        await Click(At("row A"));
        zoom = MinZoom(); camX = World.WidthM / 2; camY = World.HeightM / 2; Clamp();
        await Click(At("x256")); sw.Restart();
        double worstSlot = 0;
        while (Air.Formations.Any(f => f.Phase != Phase.Landed) && sw.Elapsed.TotalSeconds < 120)
        {
            await Frames(1);
            foreach (var f in Air.Formations) if (f.Phase == Phase.Outbound) foreach (var a in f.Ac) worstSlot = Math.Max(worstSlot, Math.Sqrt(Math.Pow(a.X - f.Lead.X, 2) + Math.Pow(a.Y - f.Lead.Y, 2)));
        }
        R("everyone lands", Air.Formations.All(f => f.Phase == Phase.Landed), $"T+{Air.T / 60:0} min, {sw.Elapsed.TotalSeconds:0} s real");
        R("the aborted formation kept its bombs", F("C").Ac.All(a => !a.Released), "");
        var kolv = World.City("Kolvograd"); var hits = Air.Hits; double mean = hits.Count == 0 ? 1e9 : hits.Average(h => Math.Sqrt(Math.Pow(h.X - kolv.X, 2) + Math.Pow(h.Y - kolv.Y, 2)));
        R("bombs land on the target", hits.Count == 24 * (F("A").Plan.Count + F("B").Plan.Count) && mean < 2500, $"{hits.Count} bombs, mean {mean:0} m from the aim, {hits.Count(h => h.Water)} in water");
        var city = Fire.Cities[0]; int burnt = 0; for (int m = 0; m < 3; m++) burnt += city.Count[m, 1] + city.Count[m, 2] + city.Count[m, 3];
        R("bombs reach the fire simulation", burnt > 0, $"{burnt} buildings hit, burning or burnt");
        o.AppendLine($"INFO  widest formation on the way out: {worstSlot:0} m");
    }

    void FireReport(string name)
    {
        foreach (var c in Fire.Cities)
        {
            var q = c.Count; string L(int m) => $"{q[m, 1]}/{q[m, 2]}/{q[m, 3]}";
            System.IO.File.AppendAllText("shots/fire_report.txt", $"{name,-34} T+{Air.T / 60,4:0} wood {L(0),-18} brick {L(1),-16} works {L(2),-11} crews {c.Brigade:0.0}/min {(c.Storm ? "STORM" : "")} breaks {Sim.Breaks} rams {Sim.Rams} withdrawals {Sim.Withdrawals} boxes back {Sim.BoxesTurnedBack}\n");
        }
    }
    /// <summary>The worst case: every bomber's incendiaries on the biggest city. Frame times at each speed, split by what took the time, and how long terrain takes there.</summary>
    async void FireBench(string path)
    {
        var o = new StringBuilder(); var kv = World.City("Kolvograd"); var tam = World.City("Tamarsk");
        o.AppendLine($"memory after loading: {GC.GetTotalMemory(false) / 1048576} MB");
        foreach (int mpp in TerrainLayer.Mpp)
        {
            var sw1 = Stopwatch.StartNew(); double ts = TerrainPaint.T * (double)mpp; int ix = (int)(tam.X / ts), iy = (int)(tam.Y / ts);
            for (int k = 0; k < 3; k++) TerrainPaint.Render(World, mpp, ix + k, iy, false);
            o.AppendLine($"terrain tile at {mpp} m a pixel, in the city: {sw1.ElapsedMilliseconds / 3} ms");
        }
        var sw0 = Stopwatch.StartNew(); Fire.Materialise(tam); o.AppendLine($"Tamarsk's 302,000 buildings, if not prepared ahead: {sw0.ElapsedMilliseconds} ms"); Fire.Cities.Clear();
        foreach (var f in Air.Formations) { Air.SetLoad(f, 0); Air.ReAim(f, kv.X + (f.Name[0] - 'B') * 300, kv.Y); }
        while (Air.Hits.Count == 0) Air.Advance(10);
        Air.Recent.Clear(); camX = kv.X; camY = kv.Y; zoom = .15; Clamp();
        var cf = Fire.Cities.First(c => c.P == kv);
        foreach (var (spd, label, secs) in new[] { (1, "x4", 20), (2, "x16", 15), (3, "x64", 10), (4, "x256", 8) })
        {
            Clock.SetSpeed(spd); await Frames(10); Prof.Reset(); int n = 0; double worst = 0, last = 0; var sw = Stopwatch.StartNew();
            while (sw.Elapsed.TotalSeconds < secs) { await Frames(1); n++; double now = sw.Elapsed.TotalMilliseconds; worst = Math.Max(worst, now - last); last = now; }
            o.AppendLine($"{label}: {n / (double)secs:0} fps, slowest {worst:0} ms, T+{Air.T / 60:0} min, burning {cf.Burning.Count}, {Prof.Text}");
        }
        o.AppendLine($"memory at the end: {GC.GetTotalMemory(false) / 1048576} MB");
        FileAccess.Open(path, FileAccess.ModeFlags.Write).StoreString(o.ToString());
        GetTree().Quit();
    }

    /// <summary>Many raids, with no drawing: every load and raid size on different kinds of district. Shows how often each ends in a firestorm.</summary>
    /// <summary>What a raid of a given size does: incendiaries from three boxes, each box off its aim by its own error, then the fire left to run out. Shows how sharply the result turns on the size of the raid, and how much it swings between runs.</summary>
    void FireCurve(string path)
    {
        var o = new System.Text.StringBuilder(); var opts = new System.Threading.Tasks.ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, System.Environment.ProcessorCount - 1) };
        var cities = new[] { ("Kolvograd", BK.None), ("Hjalmar", BK.None), ("Hjalmar", BK.WoodTown), ("Fjorgard", BK.WoodTown), ("Ostrand", BK.None), ("Ostrand", BK.Frame), ("Nordhavn", BK.Frame), ("Pohjaa", BK.WoodTown), ("Tarvik", BK.None), ("Gorsk", BK.None) }; var sizes = new[] { 120, 300, 500, 800, 1400 }; var errs = new[] { 300.0, 900.0 };
        {
            foreach (var (cn, zk) in cities) foreach (var err in errs)
            {
                o.AppendLine($"{cn}{(zk == BK.None ? "" : " " + zk)}, boxes off aim by about {err:0} m: bombs, then ruined buildings min / median / max, storms");
                var p = World.City(cn); (double X, double Y) aimAt = (p.X, p.Y);
                if (zk != BK.None) { var bl = p.Layout!.Blocks.Where(b => b.Kind == zk).OrderBy(b => Math.Abs(b.Outer.Center.x - p.X) + Math.Abs(b.Outer.Center.y - p.Y)).ToList(); if (bl.Count > 0) { var c0 = bl[bl.Count / 5].Outer.Center; aimAt = (c0.x, c0.y); } }
                foreach (int n in sizes)
                {
                    var res = new (int ruin, bool storm)[8];
                    System.Threading.Tasks.Parallel.For(0, res.Length, opts, j =>
                    {
                        var r = new Random(1000 * n + j); double G(double sd) => sd * Math.Sqrt(-2 * Math.Log(1 - r.NextDouble())) * Math.Cos(Math.Tau * r.NextDouble());
                        var fire = new FireSim(World, 50 + j); double T = 0; bool storm = false; var aim = new (double x, double y)[3]; for (int b = 0; b < 3; b++) aim[b] = (aimAt.X + G(err), aimAt.Y + G(err));
                        for (int k = 0; k < n; k++)
                        {
                            var a = aim[k % 3]; var i = new Impact { T = T, X = a.x + G(250), Y = a.y + G(250), Incendiary = true, Kg = 250 }; i.Water = World.IsWater(i.X, i.Y, 16); fire.Hit(i);
                            if (k % Math.Max(1, n / 18) == 0) { T += 10; fire.SyncTo(T); fire.Changed.Clear(); }
                        }
                        fire.Craters.Clear(); int ruin = 0;
                        while (T < 21600) { T += 30; fire.SyncTo(T); fire.Changed.Clear(); if (fire.Cities.Count == 0 || fire.Cities[0].Burning.Count == 0) break; storm |= fire.Cities[0].Storm; }
                        if (fire.Cities.Count > 0) { var q = fire.Cities[0].Count; for (int m = 0; m < 3; m++) ruin += q[m, 1] + q[m, 2] + q[m, 3]; }
                        res[j] = (ruin, storm);
                    });
                    var s = res.Select(x => x.ruin).OrderBy(x => x).ToArray();
                    o.AppendLine($"  {n,5}  {s[0],7} / {s[s.Length / 2],7} / {s[^1],7}   {res.Count(x => x.storm)}/8");
                    System.IO.File.WriteAllText(path, o.ToString());
                }
            }
        }
        System.IO.File.WriteAllText(path, o.ToString()); GetTree().Quit();
    }
    void FireTable(string path)
    {
        Place C(string n) => World.City(n);
        (double, double) Toward(Place p, double f) => (p.X + p.R * f * .8, p.Y + p.R * f * .6);
        var targets = new (string name, Place p, (double, double) at)[]
        {
            ("Kolvograd old town", C("Kolvograd"), (C("Kolvograd").X, C("Kolvograd").Y)), ("Kolvograd brick", C("Kolvograd"), Toward(C("Kolvograd"), .4)),
            ("Tamarsk wood belt", C("Tamarsk"), Toward(C("Tamarsk"), .6)), ("Verchny tenements", C("Verchny"), Toward(C("Verchny"), .25)),
            ("Gorsk (wood town)", C("Gorsk"), (C("Gorsk").X, C("Gorsk").Y)), ("Lieben settlements", C("Lieben"), Toward(C("Lieben"), .3)), ("Karst works", C("Karst"), CityPlan.Works(C("Karst"))),
        };
        var jobs = new List<(int t, int he, int boxes)>();
        for (int t = 0; t < targets.Length; t++) foreach (int he in new[] { 30, 15, 0 }) foreach (int boxes in new[] { 1, 3, 9 }) for (int rep = 0; rep < 4; rep++) jobs.Add((t, he, boxes));
        var results = new (string k, int total, int wood, int brick, int works, int storm, double km2)[jobs.Count];
        System.Threading.Tasks.Parallel.For(0, jobs.Count, new System.Threading.Tasks.ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, System.Environment.ProcessorCount - 1) }, j =>
        {
            var (ti, he, boxes) = jobs[j]; var (name, p, at) = targets[ti];
            var fire = new FireSim(World, 11 + j); var airs = new List<Air>();
            {
                // the raid as the game flies it: boxes close together in a stream, aiming at points a few hundred metres apart
                var a = new Air(World, 100 + j * 7); int waves = Math.Max(1, boxes / 3); a.TestRaid(p, waves); a.Landed = fire.Hit; int n = 0;
                foreach (var f in a.Formations) { Air.SetLoad(f, he); a.ReAim(f, at.Item1 + (n % 3 - 1) * 300, at.Item2 + (n / 3 - (waves - 1) / 2.0) * 300); n++; if (boxes == 1 && f.Name != "A") Air.Abort(f); }
                airs.Add(a);
            }
            double T = 0, end = 1e12;
            while (T < 40000)
            {
                T += 20; foreach (var a in airs) a.Advance(20); fire.SyncTo(T); fire.Changed.Clear(); fire.Craters.Clear(); foreach (var a in airs) { a.Recent.Clear(); }
                bool flying = airs.Any(a => a.Formations.Any(f => f.Phase == Phase.Waiting || f.Phase == Phase.Outbound || f.Phase == Phase.Egress));
                int burning = fire.Cities.Sum(c => c.Burning.Count);
                if (!flying && burning == 0) { end = T; break; }
            }
            var c = fire.Cities.FirstOrDefault(q => q.P == p); if (c == null) { results[j] = ($"{name,-20} {boxes * 24,4} bombers {(he == 30 ? "HE  " : he == 15 ? "HALF" : "INC ")}", 0, 0, 0, 0, 0, 0); return; }
            int G(int m) => c.Count[m, 2] + c.Count[m, 3]; int total = G(0) + G(1) + G(2);
            string L = he == 30 ? "HE  " : he == 15 ? "HALF" : "INC ";
            results[j] = ($"{name,-20} {boxes * 24,4} bombers {L}", total, G(0), G(1), G(2), c.StormSecs > 0 ? 1 : 0, c.StormCells * .0225);
        });
        var o = new StringBuilder();
        foreach (var g in results.GroupBy(r => r.k)) o.AppendLine($"{g.Key}  destroyed {g.Average(r => r.total),7:0}  (wood {g.Average(r => r.wood),6:0}, brick {g.Average(r => r.brick),6:0}, works {g.Average(r => r.works),4:0})  storms {g.Sum(r => r.storm)}/{g.Count()}{(g.Sum(r => r.storm) > 0 ? $", {g.Max(r => r.km2):0.0} km2" : "")}");
        System.IO.File.WriteAllText(path, o.ToString());
        GetTree().Quit();
    }

    /// <summary>Many raids with fighters, no drawing: escorts against interceptors, and what the pilots chose to do.</summary>
    void AirTable(string path)
    {
        var target = World.City("Kolvograd");
        var jobs = new List<(string name, int esc, int per, double alt, double spread)>();
        foreach (int per in new[] { 12, 24, 40 }) foreach (int esc in new[] { 0, 4, 8, 16 }) jobs.Add(($"{per * World.EnemyFields.Count} I-8 in all, {esc * 3,2} escorts", esc, per, 6000, 1));
        jobs.Add(("24/field, 4/box, tight box", 4, 24, 6000, .65)); jobs.Add(("24/field, 4/box, loose box", 4, 24, 6000, 1.5)); jobs.Add(("24/field, 0/box, tight box", 0, 24, 6000, .65)); jobs.Add(("24/field, 0/box, loose box", 0, 24, 6000, 1.5)); jobs.Add(("24/field, 4/box, at 8000 m", 4, 24, 8000, 1));
        var res = new (string k, int bLost, int bDam, int ucsLost, int wolfLost, int bombs, double onB, double onF, int stuck, int ucsUp)[jobs.Count * 4];
        System.Threading.Tasks.Parallel.For(0, res.Length, j =>
        {
            var q = jobs[j / 4]; var a = new Air(World, 500 + j); a.TestRaid(target); foreach (var f in a.Formations) { f.TargetAlt = q.alt; f.Spread = q.spread; }
            var s = new FighterSim(World, a, q.esc, q.per, 40 + j);
            for (int n = 0; n < 2400 && a.Formations.Any(f => f.Phase != Phase.Landed); n++) { a.Advance(10); a.Recent.Clear(); s.Tracers.Clear(); }
            for (int n = 0; n < 300 && s.All.Any(f => f.Airborne); n++) { a.Advance(10); s.Tracers.Clear(); }
            res[j] = (q.name, a.BombersLost, a.Formations.SelectMany(f => f.Ac).Count(b => b.Damage > .05), s.UcsLost, s.WolfLost, (int)a.Tonnes, s.SecsOnBombers, s.SecsOnFighters, s.All.Count(f => f.Airborne) + a.Formations.Count(f => f.Phase != Phase.Landed), s.All.Count(f => !f.Wolf && f.Act != Act.Ground));
        });
        var o = new StringBuilder();
        foreach (var g in res.GroupBy(r => r.k)) o.AppendLine($"{g.Key,-34} bombers lost {g.Average(r => r.bLost),5:0.0}/72  damaged home {g.Average(r => r.bDam),4:0}  bombs {g.Average(r => r.bombs),5:0}  I-8 lost {g.Average(r => r.ucsLost),5:0.0} of {g.Average(r => r.ucsUp),3:0} up  Hund lost {g.Average(r => r.wolfLost),4:0.0}  time on bombers {100 * g.Sum(r => r.onB) / Math.Max(1, g.Sum(r => r.onB + r.onF)),3:0}%  stuck {g.Sum(r => r.stuck)}");
        System.IO.File.WriteAllText(path, o.ToString());
        GetTree().Quit();
    }
    /// <summary>A spread of different raids: other cities, routes, stagings, loads and targets. No drawing. What is it like to do something other than the usual raid?</summary>
    void RaidTable(string path)
    {
        (double x, double y) M(double a, double b) => World.FromMap(a, b);
        Place C(string n) => World.City(n);
        (double x, double y) P(Place p) => (p.X, p.Y);
        (double x, double y) Off(Place p, double kmE, double kmN) => (p.X + kmE * 1000, p.Y + kmN * 1000);
        var kv = C("Kolvograd"); var ta = C("Tamarsk"); var ve = C("Verchny"); var pe = C("Pelkaja"); var li = C("Lieben"); var ka = C("Karst"); var bo = C("Borrow"); var go = C("Gorsk"); var dr = C("Dravik");
        (double x, double y) Zone(Place p, BK k) { var b = p.Layout!.Blocks.Where(q => q.Kind == k).OrderBy(q => Math.Abs(q.Outer.Center.x - p.X) + Math.Abs(q.Outer.Center.y - p.Y)).ToList(); return b.Count == 0 ? (p.X, p.Y) : b[b.Count / 5].Outer.Center; }
        var why = new Dictionary<string, int>(); var tiers = new double[2, 3, 3]; object gate = new();
        var plans = new List<(string name, Place p, Action<Air> set)>();
        void Plan(string name, Place p, (double x, double y)[] aims, int[] he, double[] delayMin, (double x, double y)[]? route = null, double alt = 6000, double hour = 8, double kg = 0)
            => plans.Add((name, p, a =>
            {
                a.Hour0 = hour;
                for (int i = 0; i < a.Formations.Count; i++)
                {
                    var f = a.Formations[i]; var q = aims[i % aims.Length]; if (kg > 0) { f.LoadKg = kg; Air.ChangeKg(f, 0); } Air.SetLoad(f, he[i % he.Length]); f.DepartT += delayMin[i % delayMin.Length] * 60; f.TargetAlt = alt;
                    if (route != null) { f.Route.Clear(); f.Route.AddRange(route); }
                    a.SetAim(f, q.x, q.y);
                }
            }));
        (double x, double y)[] One((double x, double y) q) => new[] { (q.x - 300, q.y), q, (q.x + 300, q.y) };
        double[] Now = { 0 };
        int[] Half = { 15 }, Inc = { 0 }, He = { 30 }, Demo = { -1 }, FireR = { 6 };
        Plan("Kolvograd old town, half, direct", kv, One(P(kv)), Half, Now);
        Plan("Kolvograd x144, half", kv, One(P(kv)), Half, Now);
        Plan("Kolvograd x144, fire load", kv, One(P(kv)), FireR, Now);
        Plan("Kolvograd x144, night, half", kv, One(P(kv)), Half, Now, null, 6000, 22);
        Plan("Tamarsk x144, half", ta, One(P(ta)), Half, Now);
        Plan("Verchny x144, half", ve, One(P(ve)), Half, Now);
        Plan("Kolvograd x144, general HE", kv, One(P(kv)), He, Now);
        Plan("Kolvograd old town, general HE", kv, One(P(kv)), He, Now);
        Plan("Kolvograd works only, demolition", kv, One(CityPlan.Works(kv)), Demo, Now);
        Plan("Kolvograd old town, fire load", kv, One(P(kv)), FireR, Now);
        Plan("Kolvograd old town, night, fire load", kv, One(P(kv)), FireR, Now, null, 6000, 22);
        Plan("Kolvograd old town, night, half", kv, One(P(kv)), Half, Now, null, 6000, 22);
        Plan("Kolvograd old town, max load", kv, One(P(kv)), FireR, Now, null, 6000, 8, 6500);
        Plan("Kolvograd old town, 3 t, 8,000 m", kv, One(P(kv)), FireR, Now, null, 8000, 8, 3000);
        Plan("Kolvograd works only, all inc", kv, One(CityPlan.Works(kv)), Inc, Now);
        Plan("Kolvograd docks, half", kv, One(Zone(kv, BK.Dock)), Half, Now);
        Plan("Kolvograd HE first, inc 20 min later", kv, One(P(kv)), new[] { 30, 0, 0 }, new double[] { 0, 20, 20 });
        Plan("Kolvograd inc first, HE 20 min later", kv, One(P(kv)), new[] { 0, 0, 30 }, new double[] { 0, 0, 20 });
        Plan("Kolvograd staged 40 min apart, inc", kv, One(P(kv)), Inc, new double[] { 0, 40, 80 });
        Plan("Kolvograd inc core, HE upwind+downwind", kv, new[] { Off(kv, -1.2, -.4), P(kv), Off(kv, 1.2, .4) }, new[] { 30, 0, 30 }, Now);
        Plan("Kolvograd three spread aims, inc", kv, new[] { Off(kv, -1.5, 0), P(kv), Off(kv, 1.5, 1.5) }, Inc, Now);
        Plan("Kolvograd from the south (dog-leg)", kv, One(P(kv)), Half, Now, new[] { M(170, 215), M(230, 300), Off(kv, 0, -60) });
                Plan("Kolvograd at 4,000 m", kv, One(P(kv)), Half, Now, null, 4000);
        Plan("Tamarsk centre, half, direct", ta, One(P(ta)), Half, Now);
        Plan("Tamarsk wooden houses, inc", ta, One(Zone(ta, BK.Cottage)), Inc, Now);
        Plan("Tamarsk works, HE", ta, One(CityPlan.Works(ta)), He, Now);
        Plan("Verchny via the corridor, half", ve, One(P(ve)), Half, Now, new[] { M(585, 145), M(585, 240) });
        Plan("Verchny across the gulf, half", ve, One(P(ve)), Half, Now);
        Plan("Pelkaja centre, inc", pe, One(P(pe)), Inc, Now);
        Plan("Lieben works end, HE", li, One(CityPlan.Works(li)), He, Now);
        Plan("Lieben wooden settlements, inc", li, One(Zone(li, BK.Cottage)), Inc, Now);
        Plan("Karst works, half", ka, One(CityPlan.Works(ka)), Half, Now);
        Plan("Borrow terraces, inc", bo, One(Zone(bo, BK.Rows)), Inc, Now);
        Plan("Gorsk wooden town, inc, deep", go, One(P(go)), Inc, Now);
        Plan("Dravik harbour, half", dr, One(P(dr)), Half, Now);
        const int Reps = 4;
        var res = new (string k, int bLost, int bombs, int home, int turned, int wood, int brick, int works, int worksAll, int total, int storm, int ucsLost, int up, int hLost, int hBingo, int breaks, int rams, int withdraw, int aces, double firstFight, double dead, double homeless)[plans.Count * Reps];
        System.Threading.Tasks.Parallel.For(0, res.Length, j =>
        {
            var q = plans[j / Reps]; var a = new Air(World, 900 + j); a.TestRaid(q.p, q.name.Contains("x144") ? 2 : 1); var fire = new FireSim(World, 60 + j) { Hour = () => a.Hour }; a.Landed = fire.Hit;
            var s = new FighterSim(World, a, 4, 24, 700 + j); s.Crashed = fire.Crash; q.set(a);
            double firstF = -1;
            for (int n = 0; n < 4000 && (a.Formations.Any(f => f.Phase != Phase.Landed) || s.All.Any(f => f.Airborne) || fire.Cities.Any(c => c.Burning.Count > 0)); n++)
            {
                a.Advance(10); fire.SyncTo(a.T); fire.Changed.Clear(); fire.Craters.Clear(); a.Recent.Clear(); s.Tracers.Clear();
                if (firstF < 0 && s.All.Any(f => f.Act is Act.AttackBomber or Act.Fight)) firstF = a.T;
            }
            lock (gate)
            {
                foreach (var kv2 in s.LeftBecause) { why.TryGetValue(kv2.Key, out int n0); why[kv2.Key] = n0 + kv2.Value; }
                foreach (var f in s.All) { if (f.Act == Act.Ground) continue; int t = f.Skill0 < .55 ? 0 : f.Skill0 < .9 ? 1 : 2; tiers[f.Wolf ? 1 : 0, t, 0]++; tiers[f.Wolf ? 1 : 0, t, 1] += f.KillsB + f.KillsF; if (!f.Alive) tiers[f.Wolf ? 1 : 0, t, 2]++; }
            }
            var c = fire.Cities.FirstOrDefault(z => z.P == q.p); int G(int m) => c == null ? 0 : c.Count[m, 2] + c.Count[m, 3]; int Tot(int m) { if (c == null) return 0; int t = 0; for (int st = 0; st < 4; st++) t += c.Count[m, st]; return t; }
            res[j] = (q.name, a.BombersLost, (int)a.Tonnes, a.Formations.Sum(f => f.Ac.Count + f.BackEarly), s.BoxesTurnedBack, G(0), G(1), G(2), Tot(2), G(0) + G(1) + G(2), c != null && c.StormSecs > 0 ? 1 : 0, s.UcsLost, s.All.Count(f => !f.Wolf && f.Act != Act.Ground), s.WolfLost, s.LeftBecause.GetValueOrDefault("Hund: low on fuel"), s.Breaks, s.Rams, s.Withdrawals, s.All.Count(f => !f.Wolf && f.Alive && f.KillsB + f.KillsF >= 2), firstF, fire.Rec(q.p).Dead, fire.Rec(q.p).Homeless);
        });
        var o = new StringBuilder();
        foreach (var g in res.GroupBy(r => r.k))
            o.AppendLine($"{g.Key,-40} lost {g.Average(r => r.bLost),5:0.0}  back {g.Sum(r => r.turned),1}/{Reps * 3}  bombs {g.Average(r => r.bombs),5:0}  ruin {g.Average(r => r.total),6:0} (w {g.Average(r => r.wood),5:0} b {g.Average(r => r.brick),5:0} works {g.Average(r => r.works),4:0}/{g.Average(r => r.worksAll),4:0})  storm {g.Sum(r => r.storm)}/{Reps}  dead {g.Average(r => r.dead),6:0} ({g.Average(r => r.dead) / Math.Max(1, g.Average(r => r.bombs)),4:0.0}/t)  homeless {g.Average(r => r.homeless),7:0}  I8 up {g.Average(r => r.up),3:0} down {g.Average(r => r.ucsLost),4:0.0}  Hund down {g.Average(r => r.hLost),3:0.0} out of fuel {g.Average(r => r.hBingo),3:0.0}  breaks {g.Average(r => r.breaks),3:0.0} rams {g.Average(r => r.rams),3:0.0} withdraw {g.Average(r => r.withdraw),3:0.0}  first fight T+{g.Average(r => r.firstFight) / 60,3:0}");
        o.AppendLine(); o.AppendLine("WHY PILOTS LEFT, all raids"); foreach (var kv3 in why.OrderByDescending(q => q.Value)) o.AppendLine($"{kv3.Value,6}  {kv3.Key}");
        o.AppendLine(); o.AppendLine("BY TRAINING, all raids: flew / kills per pilot / lost per cent");
        string[] tn = { "green", "competent", "expert" };
        for (int side = 0; side < 2; side++) for (int t = 0; t < 3; t++) if (tiers[side, t, 0] > 0) o.AppendLine($"{(side == 1 ? "Hund" : "I-8 "),-5} {tn[t],-10} {tiers[side, t, 0],6:0}  {tiers[side, t, 1] / tiers[side, t, 0],5:0.00}  {100 * tiers[side, t, 2] / tiers[side, t, 0],4:0}%");
        System.IO.File.WriteAllText(path, o.ToString());
        GetTree().Quit();
    }

    /// <summary>What the guns do on their own and alongside the fighters: by height, by day and night, for each side. Shells per bomber brought down is the number history gives, in the thousands.</summary>
    void FlakTable(string path)
    {
        var jobs = new List<(string name, Craft[] kinds, bool wolf, string target, double alt, double hour, int perField, int def, double flak)>
        {
            ("V-16 x72 Kolvograd, 6,000 m, day", new[] { Craft.V16 }, true, "Kolvograd", 6000, 8, 0, 0, 1), ("V-16 x72 Kolvograd, 4,500 m, day", new[] { Craft.V16 }, true, "Kolvograd", 4500, 8, 0, 0, 1),
            ("V-16 x72 Kolvograd, 7,500 m, day", new[] { Craft.V16 }, true, "Kolvograd", 7500, 8, 0, 0, 1), ("V-16 x72 Kolvograd, 6,000 m, night", new[] { Craft.V16 }, true, "Kolvograd", 6000, 22, 0, 0, 1),
            ("V-16 x72 Kolvograd, heavy flak", new[] { Craft.V16 }, true, "Kolvograd", 6000, 8, 0, 0, 2), ("V-16 x72 Tamarsk, 6,000 m, day", new[] { Craft.V16 }, true, "Tamarsk", 6000, 8, 0, 0, 1),
            ("U-4 x36 Kolvograd, 5,000 m, day", new[] { Craft.U4 }, true, "Kolvograd", 5000, 8, 0, 0, 1),
            ("UCS TB-6 x36 Hjalmar, 5,000 m, day", new[] { Craft.TB6 }, false, "Hjalmar", 5000, 8, 0, 0, 1), ("UCS TB-6 x36 Hjalmar, 5,000 m, night", new[] { Craft.TB6 }, false, "Hjalmar", 5000, 22, 0, 0, 1),
            ("V-16 x72 Kolvograd, I-8s, no flak", new[] { Craft.V16 }, true, "Kolvograd", 6000, 8, 24, 0, 0), ("V-16 x72 Kolvograd, I-8s and flak", new[] { Craft.V16 }, true, "Kolvograd", 6000, 8, 24, 0, 1),
            ("UCS TB-6 x36 Hjalmar, F-5s, no flak", new[] { Craft.TB6 }, false, "Hjalmar", 5000, 8, 0, 6, 0), ("UCS TB-6 x36 Hjalmar, F-5s and flak", new[] { Craft.TB6 }, false, "Hjalmar", 5000, 8, 0, 6, 1),
        };
        var lines = new string[jobs.Count];
        System.Threading.Tasks.Parallel.For(0, jobs.Count, j =>
        {
            var q = jobs[j]; const int Reps = 8; double lost = 0, flakLost = 0, holed = 0, shells = 0, splinters = 0, start = 0, bombs = 0, turned = 0;
            for (int r = 0; r < Reps; r++)
            {
                var a = new Air(World, 700 + j * 10 + r, 0, q.hour); a.TestRaid(World.City(q.target), 1, q.kinds, q.wolf); foreach (var f in a.Formations) f.TargetAlt = q.alt;
                a.Flak = new FlakSim(World, a, q.wolf ? q.flak : 1, q.wolf ? 1 : q.flak, 80 + j * 10 + r);
                var s = new FighterSim(World, a, 0, q.perField, 90 + j * 10 + r, q.def, 0); start = a.Formations.Sum(f => f.Start);
                for (int n = 0; n < 4000 && (a.Formations.Any(f => f.Phase != Phase.Landed) || s.All.Any(f => f.Airborne)); n++) { a.Advance(10); a.Recent.Clear(); s.Tracers.Clear(); }
                int side = q.wolf ? 1 : 0; lost += q.wolf ? a.BombersLost : a.UcsBombersLost; flakLost += a.FlakLost[side]; holed += a.Flak.Holed[side].Count; shells += a.Flak.Shells[side]; splinters += a.Flak.Splinters[side]; bombs += a.Tonnes; turned += s.BoxesTurnedBack;
            }
            lines[j] = $"{q.name,-40} of {start,3}: lost {lost / Reps,5:0.0} ({100 * lost / Reps / start,4:0}%)  to flak {flakLost / Reps,4:0.0} ({100 * flakLost / Reps / start,4:0.0}%)  holed by flak {holed / Reps,5:0.0} ({100 * holed / Reps / start,3:0}%)  shells {shells / Reps,6:0}  per flak kill {(flakLost > 0 ? shells / flakLost : 0),7:0}  splinters {splinters / Reps,5:0}  tonnes {bombs / Reps,4:0}  boxes back {turned / Reps,3:0.0}";
        });
        System.IO.File.WriteAllLines(path, lines);
        GetTree().Quit();
    }

    /// <summary>Every aircraft against its enemies: UCS raids on the Vulfen side, Vulfen strike boxes, and how each type dies.</summary>
    void UcsTable(string path)
    {
        var res = new List<string>(); object gate = new();
        var jobs = new List<(string name, Craft[] kinds, bool wolf, string target, int esc, int perField, int def)>
        {
            ("UCS TB-6 x36, 6 F-5/field, no escort", new[] { Craft.TB6 }, false, "Hjalmar", 0, 0, 6), ("UCS TB-6 x36, 6 F-5/field, 4 I-8/box", new[] { Craft.TB6 }, false, "Hjalmar", 4, 0, 6), ("UCS TB-6 x36, 6 F-5/field, 8 I-8/box", new[] { Craft.TB6 }, false, "Hjalmar", 8, 0, 6),
            ("UCS SB-3 x54, 6 F-5/field, no escort", new[] { Craft.SB3 }, false, "Hjalmar", 0, 0, 6), ("UCS SB-3 x54, 6 F-5/field, 4 I-8/box", new[] { Craft.SB3 }, false, "Hjalmar", 4, 0, 6),
            ("Vulfen U-4 x36 on Kolvograd, no escort", new[] { Craft.U4 }, true, "Kolvograd", 0, 24, 0), ("Vulfen U-4 x36 on Kolvograd, 12 F-5", new[] { Craft.U4 }, true, "Kolvograd", 4, 24, 0),
            ("UCS 12 boxes on Hjalmar, 18 F-5, 2 I-8/box", new[] { Craft.TB6, Craft.SB3, Craft.SB3 }, false, "Hjalmar", 2, 0, 6),
            ("Default mix on Kolvograd, no escort", new[] { Craft.V16, Craft.V16, Craft.U4 }, true, "Kolvograd", 0, 24, 0), ("Default mix on Kolvograd, 12 F-5", new[] { Craft.V16, Craft.V16, Craft.U4 }, true, "Kolvograd", 4, 24, 0),
            ("Vulfen V-16 x72 on Kolvograd, no escort", new[] { Craft.V16 }, true, "Kolvograd", 0, 24, 0), ("Vulfen V-16 x72 on Kolvograd, 12 F-5", new[] { Craft.V16 }, true, "Kolvograd", 4, 24, 0),
        };
        var lines = new string[jobs.Count];
        System.Threading.Tasks.Parallel.For(0, jobs.Count, j =>
        {
            var q = jobs[j]; int u4Lost = 0; double lost = 0, ucsF = 0, wolfF = 0, bombs = 0, back = 0, start = 0, press = 0, jett = 0, ditch = 0; var causes = new Dictionary<string, int>(); const int Reps = 8; double ruined = 0, sEsc = 0, sNear = 0, sFight = 0, sV = 0; static double Dist2(double ax, double ay, double bx, double by) => Math.Sqrt((ax - bx) * (ax - bx) + (ay - by) * (ay - by));
            for (int r = 0; r < Reps; r++)
            {
                var a = new Air(World, 300 + j * 10 + r); bool big = q.kinds.Length == 3 && !q.wolf; a.TestRaid(World.City(q.target), big ? 4 : 1, q.kinds, q.wolf, 0, big ? 900 : 0);
                var s = new FighterSim(World, a, q.wolf ? q.esc : 0, q.perField, 50 + j * 10 + r, q.def, q.wolf ? 0 : q.esc); var fire = q.wolf ? null : new FireSim(World, 60 + r); if (fire != null) { a.Landed = fire.Hit; s.Crashed = fire.Crash; } start = a.Formations.Sum(f => f.Start); var was = a.Downed; a.Downed = b => { was?.Invoke(b); if (b.F.Kind == Craft.U4) u4Lost++; };
                for (int n = 0; n < 4000 && (a.Formations.Any(f => f.Phase != Phase.Landed) || s.All.Any(f => f.Airborne)); n++)
                {
                    a.Advance(10); a.Recent.Clear(); s.Tracers.Clear(); if (fire != null) { fire.SyncTo(a.T); fire.Changed.Clear(); fire.Craters.Clear(); }
                    foreach (var f in s.All) if (f.Airborne && f.Wolf && f.Charge != null && f.Charge.Phase == Phase.Outbound && f.Act != Act.Home && f.Act != Act.Climb) { sEsc++; if (Dist2(f.X, f.Y, f.Charge.Lead.X, f.Charge.Lead.Y) < 7000) sNear++; if (f.Act is Act.Fight or Act.Zoom or Act.Evade) { sFight++; sV += f.V; } }
                }
                if (fire != null) { double ft = a.T; while (ft < a.T + 14400 && fire.Cities.Any(c => c.Burning.Count > 0)) { ft += 30; fire.SyncTo(ft); fire.Changed.Clear(); } foreach (var c in fire.Cities) for (int m = 0; m < 3; m++) ruined += c.Count[m, 2] + c.Count[m, 3]; }
                lost += a.BombersLost + a.UcsBombersLost; ucsF += s.UcsLost; wolfF += s.WolfLost; bombs += a.Tonnes; back += s.BoxesTurnedBack; press += a.PressedOn; jett += a.Jettisoned; ditch += a.Ditched;
                foreach (var c in a.LostTo) { causes.TryGetValue(c.Key, out int m); causes[c.Key] = m + c.Value; }
            }
            lines[j] = $"{q.name,-40} of {start,3}: lost {lost / Reps,5:0.0} (U-4 {u4Lost / (double)Reps,4:0.0})  boxes back {back / Reps,3:0.0}  tonnes {bombs / Reps,4:0}  pressed on {press / Reps,4:0.0} jettisoned {jett / Reps,4:0.0} ditched {ditch / Reps,4:0.0}  {(q.wolf ? "" : $"ruined {ruined / Reps,6:0}  ")}I-8 down {ucsF / Reps,4:0.0}  F-5 down {wolfF / Reps,4:0.0}{(sEsc > 0 ? $"  escorts within 7 km {100 * sNear / sEsc:0}%, fighting {100 * sFight / sEsc:0}% at {3.6 * sV / Math.Max(1, sFight):0} km/h" : "")}  causes {string.Join(", ", causes.OrderByDescending(c => c.Value).Select(c => $"{c.Key} {c.Value}"))}";
        });
        System.IO.File.WriteAllLines(path, lines);
        GetTree().Quit();
    }

    /// <summary>The biggest usual raid through the real game, with everything drawn: does the frame rate hold?</summary>
    async void PerfTest(string path)
    {
        var o = new StringBuilder(); await Frames(10); StartSetup(); Sortie!.UcsRaid = true; FinishSetup(); SendRaid(); camX = Target.X; camY = Target.Y; zoom = .15; Clamp();
        while (Air.Hits.Count < 40 && Air.T < 9000) { Air.Advance(20); Fire.SyncTo(Air.T); Air.Recent.Clear(); Sim.Tracers.Clear(); await Frames(1); }
        foreach (var (spd, label, secs) in new[] { (0, "x1", 10), (2, "x16", 10), (3, "x64", 10) })
        {
            Clock.SetSpeed(spd); await Frames(5); Prof.Reset(); int n = 0; double worst = 0, last = 0; var sw = Stopwatch.StartNew();
            while (sw.Elapsed.TotalSeconds < secs) { await Frames(1); n++; double now = sw.Elapsed.TotalMilliseconds; if (n > 2) worst = Math.Max(worst, now - last); last = now; }
            o.AppendLine($"{label}: {n / (double)secs:0} fps, slowest {worst:0} ms, T+{Air.T / 60:0}, {Air.Formations.Sum(f => f.Ac.Count)} bombers, {Sim.All.Count(f => f.Airborne)} fighters up, {Fire.Cities.Sum(c => c.Burning.Count)} burning, {Prof.Text}");
        }
        System.IO.File.WriteAllText(path, o.ToString()); GetTree().Quit();
    }

    /// <summary>What group size does to each bomber: V-16 and U-4 in small and big groups against the same interceptors, no escort. A lone group takes off from each field in turn, since each route meets different fighters.</summary>
    void NicheTable(string path)
    {
        var jobs = new List<(string name, Craft kind, int boxes, int box)>
        {
            ("U-4 x6", Craft.U4, 1, 6), ("U-4 x12", Craft.U4, 1, 12), ("U-4 x24", Craft.U4, 2, 12), ("U-4 x36", Craft.U4, 3, 12), ("U-4 x72", Craft.U4, 6, 12),
            ("V-16 x6", Craft.V16, 1, 6), ("V-16 x12", Craft.V16, 1, 12), ("V-16 x24", Craft.V16, 1, 24), ("V-16 x72", Craft.V16, 3, 24), ("V-16 x144", Craft.V16, 6, 24),
        };
        var lines = new string[jobs.Count];
        System.Threading.Tasks.Parallel.For(0, jobs.Count, j =>
        {
            var q = jobs[j]; const int Reps = 18; double lost = 0, tons = 0, up = 0, downF = 0; int n = 0; var target = World.City("Kolvograd"); var byField = new double[3];
            for (int r = 0; r < Reps; r++)
            {
                var a = new Air(World, 4000 + j * 31 + r); a.TestRaid(target, Math.Max(1, (q.boxes + 2) / 3), new[] { q.kind }, true, 0, 0, q.box);
                for (int k = 0; k < a.Formations.Count; k++) if ((k - r % 3 + a.Formations.Count) % a.Formations.Count >= q.boxes) Air.Abort(a.Formations[k]);
                var s = new FighterSim(World, a, 0, 24, 6000 + j * 17 + r, 0); n = a.Formations.Where(f => !f.Aborted).Sum(f => f.Start);
                for (int k = 0; k < 4000 && (a.Formations.Any(f => f.Phase != Phase.Landed) || s.All.Any(f => f.Airborne)); k++) { a.Advance(10); a.Recent.Clear(); s.Tracers.Clear(); }
                lost += a.BombersLost; byField[r % 3] += a.BombersLost; tons += a.Tonnes; up += s.All.Count(f => !f.Wolf && f.Act != Act.Ground); downF += s.UcsLost;
            }
            lines[j] = $"{q.name,-10} lost {lost / Reps,5:0.0} = {100 * lost / Reps / n,4:0.0}%  from each field {byField[0] / (Reps / 3),4:0.0} {byField[1] / (Reps / 3),4:0.0} {byField[2] / (Reps / 3),4:0.0}   tonnes {tons / Reps,5:0}   tonnes per bomber lost {(lost > 0 ? tons / lost : 0),5:0}   I-8 up {up / Reps,4:0} down {downF / Reps,4:0.0}";
        });
        System.IO.File.WriteAllLines(path, lines); GetTree().Quit();
    }

    /// <summary>Do waves pay? The mixed raid on Kolvograd, with the U-4s softening the target before the V-16s or not.</summary>
    void WaveTable(string path)
    {
        var plans = new (string name, string v16, string u4, int vLate, int uLate)[]
        {
            ("All at once, half and half / mixed", "Half and half", "Mixed", 0, 0),
            ("All at once, break and burn / strike", "Break and burn", "Strike", 0, 0),
            ("U-4 strike, break and burn 10 min later", "Break and burn", "Strike", 10, 0),
            ("U-4 strike, break and burn 20 min later", "Break and burn", "Strike", 20, 0),
            ("All at once, all incendiary / strike", "All incendiary", "Strike", 0, 0),
            ("U-4 strike, all incendiary 5 min later", "All incendiary", "Strike", 5, 0),
            ("U-4 strike, all incendiary 10 min later", "All incendiary", "Strike", 10, 0),
            ("U-4 strike, all incendiary 20 min later", "All incendiary", "Strike", 20, 0),
            ("All incendiary with U-4 fire marker", "All incendiary", "Fire marker", 0, 0),
            ("U-4 fire marker, all incendiary 10 min later", "All incendiary", "Fire marker", 10, 0),
        };
        var lines = new string[plans.Length]; var kv = World.City("Kolvograd");
        System.Threading.Tasks.Parallel.For(0, plans.Length, j =>
        {
            var q = plans[j]; const int Reps = 12; double lost = 0, tons = 0, ruin = 0; var runs = new int[Reps];
            for (int r = 0; r < Reps; r++)
            {
                var a = new Air(World, 7000 + j * 13 + r); a.TestRaid(kv, 1, new[] { Craft.V16, Craft.V16, Craft.U4 }); var fire = new FireSim(World, 80 + r); a.Landed = fire.Hit;
                foreach (var f in a.Formations) { bool v = f.Kind == Craft.V16; Air.SetPreset(f, Loadouts.Of[f.Kind].First(l => l.Name == (v ? q.v16 : q.u4))); f.ArriveMin = v ? q.vLate : q.uLate; }
                a.Launch(); var s = new FighterSim(World, a, 4, 24, 900 + r); s.Crashed = fire.Crash;
                for (int n = 0; n < 4000 && (a.Formations.Any(f => f.Phase != Phase.Landed) || s.All.Any(f => f.Airborne) || fire.Cities.Any(c => c.Burning.Count > 0)); n++) { a.Advance(10); fire.SyncTo(a.T); fire.Changed.Clear(); fire.Craters.Clear(); a.Recent.Clear(); s.Tracers.Clear(); }
                var c = fire.Cities.FirstOrDefault(z => z.P == kv); int g = 0; if (c != null) for (int m = 0; m < 3; m++) g += c.Count[m, 2] + c.Count[m, 3];
                lost += a.BombersLost; tons += a.Tonnes; ruin += g; runs[r] = g;
            }
            Array.Sort(runs); lines[j] = $"{q.name,-46} tonnes {tons / Reps,4:0}  lost {lost / Reps,4:0.0}  ruined {ruin / Reps,6:0}  (range {runs[0]}-{runs[^1]})  per tonne {ruin / Math.Max(1, tons),5:0.0}";
        });
        System.IO.File.WriteAllLines(path, lines); GetTree().Quit();
    }

    /// <summary>The fighters through the real game: they scramble, fight, can be clicked, and the frame rate holds at each speed.</summary>
    async void FightTest(string path)
    {
        var o = new StringBuilder(); void R(string name, bool ok, string more = "") => o.AppendLine($"{(ok ? "PASS" : "FAIL")}  {name}  {more}");
        try
        {
            bool Near(double a, double b) => Math.Abs(a - b) < .005;
            R("turn back: 10% at half way, fighters all round", Near(Air.TurnBackAt(.5, 40, 24, true, .5), .10));
            R("turn back: 25% at two thirds", Near(Air.TurnBackAt(2.0 / 3, 40, 24, true, .5), .25));
            R("turn back: 60% at the target", Near(Air.TurnBackAt(1, 40, 24, true, .5), .60));
            R("turn back: braver with nothing in sight", Air.TurnBackAt(.5, 0, 24, true, .5) > .35);
            Hull H(Craft c, int alive) { var h = new Hull(Design.Of[c]); for (int i = alive; i < h.Eng.Length; i++) h.Eng[i] = 0; return h; }
            R("four engines: flies on two, slowly", H(Craft.V16, 2).Sink == 0 && H(Craft.V16, 2).Power < .8);
            R("four engines: falls slowly on one", H(Craft.V16, 1).Sink > 0 && H(Craft.V16, 1).Sink < 3);
            R("two engines: barely limps on one", H(Craft.SB3, 1).Sink > 0 && H(Craft.SB3, 1).Sink < .5 && H(Craft.SB3, 1).Power * Design.Of[Craft.SB3].Max < Design.Of[Craft.SB3].Cruise);
            R("no engines: comes down", H(Craft.TB6, 0).Sink >= 5 && H(Craft.I8, 0).Working == 0);
            R("ammunition as specified", new Hull(Design.Of[Craft.F5]).Ammo == 1200 && new Hull(Design.Of[Craft.I8]).Ammo == 800 && new Hull(Design.Of[Craft.V16]).Turrets.Sum(t => t.Rounds) == 6400);
            { var rr = new Random(1); double seal = 0, open = 0; for (int n = 0; n < 400; n++) { var a1 = new Hull(Design.Of[Craft.F5]); a1.Take(Weapon.T15, 1, rr, 0); seal += a1.Leak; var b1 = new Hull(Design.Of[Craft.I8]); b1.Take(Weapon.T15, 1, rr, 0); open += b1.Leak; } R("self-sealing tanks leak far less", seal * 5 < open, $"F-5 {seal:0}, I-8 {open:0}"); }
            { double Bat(Weapon w, int n, double skill) => n * w.Rpm / 60 * w.Acc * skill * (w.Dmg + w.Crit * w.CritDmg); double ra = Bat(Weapon.T15, 4, 1.1) / Bat(Weapon.KA31, 2, .55); R("F-5 battery about 3x the I-8's", ra > 2.5 && ra < 3.8, $"{ra:0.0}x"); }
            { var rr = new Random(3); var ro = new Roster(); for (int n = 0; n < 500; n++) ro.Down(new Hull(Design.Of[Craft.V16]), World, rr, World.City("Kolvograd").X, World.City("Kolvograd").Y, 6000, true, "fire"); int caught = ro.Prisoners[1], walked = ro.Recovered[1]; R("over enemy land: about 90% taken prisoner", caught > 8 * walked, $"{caught} prisoners, {walked} home, {ro.Dead[1]} killed");
              var so = new Roster(); var sea = World.FromMap(330, 170); for (int n = 0; n < 500; n++) so.Down(new Hull(Design.Of[Craft.V16]), World, rr, sea.x, sea.y, 6000, true, "fire"); R("at sea: half or fewer rescued", so.Recovered[1] <= so.LostAtSea[1] && so.Recovered[1] > 0, $"{so.Recovered[1]} rescued, {so.LostAtSea[1]} lost");
              var ho = new Roster(); var home = World.City("Ostrand"); for (int n = 0; n < 200; n++) ho.Down(new Hull(Design.Of[Craft.V16]), World, rr, home.X, home.Y, 6000, true, "fire"); R("over home ground: recovered", ho.Prisoners[1] == 0 && ho.Recovered[1] > 0); }
            { var d = Design.Of[Craft.V16]; R("V-16 max 6.5 t, usual 5 t, high 3 t", d.MaxKg == 6500 && d.UsualKg == 5000 && Weight.Roof(d, 3000) > 7800 && Weight.Roof(d, 6500) < 6500, $"roof {Weight.Roof(d, 0):0} / {Weight.Roof(d, 3000):0} / {Weight.Roof(d, 5000):0} / {Weight.Roof(d, 6500):0} m"); }
            { var d = Design.Of[Craft.U4]; R("U-4 slowed by bombs slung outside", Weight.Top(d, 2000) < 140 && Weight.Top(d, 0) > 175, $"top {Weight.Top(d, 0) * 3.6:0} clean, {Weight.Top(d, 2000) * 3.6:0} at 2 t"); }
            R("SB-3 carries nothing over 250 kg", Bombs.Load(Design.Of[Craft.SB3], 1800, Mission.Demolition).All(b => b.Kg <= 250));
            foreach (var (kk, ms) in new[] { (Craft.V16, Mission.Fire), (Craft.U4, Mission.Demolition), (Craft.SB3, Mission.General), (Craft.TB6, Mission.Half) }) { var pl = Bombs.Load(Design.Of[kk], Design.Of[kk].UsualKg, ms); o.AppendLine($"INFO  {kk} {Bombs.MissionNames[(int)ms]} at {Design.Of[kk].UsualKg} kg: {Bombs.Describe(pl)} = {pl.Sum(b => b.Kg)} kg"); }
            await Frames(10); R("fighters exist", Sim.EscortCount == 12 && Sim.UcsCount > 0, $"{Sim.EscortCount} escorts, {Sim.UcsCount} I-8, {Air.Formations.Count} boxes");
            R("all six types are in the raid", Air.Formations.Select(f => f.Kind).Concat(Sim.All.Select(f => f.Kind)).Distinct().Count() >= 5, string.Join(" ", Air.Formations.Select(f => f.Kind).Concat(Sim.All.Select(f => f.Kind)).Distinct()));
            R("nobody up before the raid", Sim.All.All(f => !f.Airborne));
            while (Sim.All.Count(f => f.Act is Act.Fight or Act.AttackBomber) < 3 && Air.T < 8000) { Air.Advance(10); Fire.SyncTo(Air.T); Air.Recent.Clear(); Sim.Tracers.Clear(); }
            R("a fight starts", Air.T < 8000, $"T+{Air.T / 60:0} min, alert at T+{Sim.AlertT / 60:0}");
            var m = Sim.All.First(f => f.Act is Act.Fight or Act.AttackBomber); camX = m.X; camY = m.Y; zoom = .3; Clamp(); Clock.Paused = true; await Frames(3);
            await Click(W2S(m.X, m.Y)); R("clicking a fighter opens the pilot", Sim.Watch == m, Sim.Watch?.Why ?? "");
            Input.ParseInputEvent(new InputEventKey { Keycode = Key.F, Pressed = true }); await Frames(3); R("F follows the pilot", FollowPilot);
            await Click(Ui.Named["CLOSE"].GetCenter()); R("close the pilot panel", Sim.Watch == null); FollowPilot = false;
            var bw = Air.Formations.First(q => q.Wolf && q.Ac.Count > 0).Ac[3]; camX = bw.X; camY = bw.Y; Clamp(); await Frames(3); await Click(W2S(bw.X, bw.Y)); R("clicking a bomber opens its captain", Air.Watch == bw, Air.Watch?.Why ?? "");
            await Click(Ui.Named["CLOSE"].GetCenter()); R("close the bomber panel", Air.Watch == null);
            camX = m.X; camY = m.Y; zoom = .15; Clamp();
            foreach (var (spd, label, secs) in new[] { (0, "x1", 8), (2, "x16", 8), (4, "x256", 8) })
            {
                Clock.SetSpeed(spd); await Frames(5); Prof.Reset(); int n = 0; double worst = 0, last = 0; var sw = Stopwatch.StartNew();
                while (sw.Elapsed.TotalSeconds < secs) { await Frames(1); n++; double now = sw.Elapsed.TotalMilliseconds; worst = Math.Max(worst, now - last); last = now; }
                o.AppendLine($"INFO  {label}: {n / (double)secs:0} fps, slowest {worst:0} ms, T+{Air.T / 60:0}, {Sim.All.Count(f => f.Airborne)} fighters up, lost {Air.BombersLost} bombers, {Prof.Text}");
            }
            R("the game ran on", Air.T > 4000, $"T+{Air.T / 60:0}");
            Clock.SetSpeed(4); while (!RaidOver && Air.T < 40000) { Air.Advance(30); Fire.SyncTo(Air.T); Air.Recent.Clear(); Sim.Tracers.Clear(); await Frames(1); }
            R("the raid ends", RaidOver, $"T+{Air.T / 60:0}"); await Frames(5);
            var cr = Air.Crews; int downed = cr.Dead[0] + cr.Dead[1] + cr.Recovered[0] + cr.Recovered[1] + cr.Prisoners[0] + cr.Prisoners[1] + cr.LostAtSea[0] + cr.LostAtSea[1];
            o.AppendLine($"INFO  fates: Vulfen {cr.Dead[1]} dead {cr.Recovered[1]} back {cr.Prisoners[1]} POW {cr.LostAtSea[1]} sea; UCS {cr.Dead[0]} dead {cr.Recovered[0]} back {cr.Prisoners[0]} POW {cr.LostAtSea[0]} sea");
            R("crew fates are counted", downed > 0, $"{downed}");
            var ace = Sim.All.Where(f => f.Alive && f.Wolf).OrderByDescending(f => f.Career + f.KillsB + f.KillsF).First(); int score = ace.Career + ace.KillsF + ace.KillsB; string aceName = ace.Name; double t0 = Air.T;
            await Click(Ui.Named["NEXT RAID, NIGHT"].GetCenter()); await Frames(3);
            R("next raid starts, at night", Air.T > t0 && Air.Hour0 == 22 && !RaidOver && Air.Dark == 1);
            R("crews carry over", Sim.All.Any(f => f.Wolf && f.Name == aceName && f.Career == score), $"{aceName} with {score}");
            R("damage is repaired", Air.Formations.All(q => q.Ac.All(a => a.H.Damage == 0 && a.H.Working == a.H.D.Engines)) && Sim.All.All(f => f.H.Damage == 0));
            // the sandbox: choose a target and some bombers, and they are what flies
            { Vector2 At(string n) => Ui.Named[n].GetCenter(); NewMission(); await Frames(3); await Click(At("Tamarsk")); await Click(At("NIGHT")); await Click(At("ADD U-4")); await Click(At("THE WORKS")); await Frames(3); double t1 = Air.T; R("setup holds the clock", Setup && Sortie!.Target.Name == "Tamarsk" && Sortie.Hour == 22 && Sortie.Flights.Count == 7);
              await Click(At("TO THE PLANNING TABLE")); var own = Air.Formations.Where(q => q.Wolf).ToList(); var tw = CityPlan.Works(World.City("Tamarsk"));
              R("crews carry into a new mission", Sim.All.Any(f => f.Wolf && f.Name == aceName && f.Career == score), $"{aceName} with {score}");
              R("the bombers asked for are on the ground", !Setup && Planning && own.Count == 7 && own.Sum(q => q.Ac.Count) == 180 && own[6].Kind == Craft.U4 && own.All(q => q.Phase == Phase.Waiting), string.Join(" ", own.Select(q => $"{q.Name}:{q.Kind}x{q.Ac.Count}")));
              R("they aim where asked, at the time asked", Math.Abs(own[0].AimX - tw.Item1) < 1500 && Math.Abs(own[0].AimY - tw.Item2) < 1500 && Air.Hour0 == 22, $"{Air.Formations.Count} boxes in all");
              R("each flight starts on a load that gets it home if one can", own.All(q => Weight.RangeKm(Design.Of[q.Kind], q.Plan.Sum(b => b.Kg)) >= Air.TripKm(q) * 1.05 || q.Preset == Loadouts.Of[q.Kind].OrderBy(l => l.Kg).First()), string.Join(", ", own.Select(q => q.Preset?.Name)));
              await Click(At("BACK")); R("back to setup keeps the choices", Setup && Sortie!.Flights.Count == 7); await Click(At("TO THE PLANNING TABLE"));
              await Click(At("SEND THE RAID")); await Frames(3); Clock.SetSpeed(4); await Frames(60); R("and the raid flies", !Planning && Air.T > t1, $"T+{Air.T / 60:0}"); }
        }
        catch (Exception ex) { o.AppendLine("FAIL  stopped: " + ex.Message + " " + ex.StackTrace); }
        FileAccess.Open(path, FileAccess.ModeFlags.Write).StoreString(o.ToString());
        GetTree().Quit();
    }

    async void SelfTest(string path)
    {
        var o = new StringBuilder(); void R(string name, bool ok, string more = "") => o.AppendLine($"{(ok ? "PASS" : "FAIL")}  {name}  {more}");
        await Frames(20);
        var size = GetViewportRect().Size; var mid = size / 2;
        Vector2 At(string n) => Ui.Named[n].GetCenter();
        await Click(At("RESUME")); R("resume button", !Clock.Paused);
        await Click(At("x16")); R("speed button x16", !Clock.Paused && Clock.SpeedIdx == 2);
        double t0 = Clock.T; await Frames(30); R("clock runs", Clock.T > t0, $"{Clock.T - t0:0.0} s in 30 frames");
        Input.ParseInputEvent(new InputEventKey { Keycode = Key.Space, Pressed = true }); await Frames(3); R("space pauses", Clock.Paused);
        await Frames(3);
        double z0 = zoom; var at = new Vector2(1000, 500); double wx = S2Wx(at.X), wy = S2Wy(at.Y);
        for (int i = 0; i < 3; i++) { await Mouse(at, MouseButton.WheelUp, true); await Mouse(at, MouseButton.WheelUp, false); }
        double px = (S2Wx(at.X) - wx) * zoom, py = (S2Wy(at.Y) - wy) * zoom;
        R("wheel zooms at the cursor", zoom > z0 * 1.7 && Math.Abs(px) < 1 && Math.Abs(py) < 1, $"zoom x{zoom / z0:0.00}, point moved {Math.Abs(px) + Math.Abs(py):0.00} px");
        double cx0 = camX; await Mouse(mid, MouseButton.Left, true);
        Input.ParseInputEvent(new InputEventMouseMotion { Position = mid + new Vector2(100, 0), GlobalPosition = mid + new Vector2(100, 0), Relative = new Vector2(100, 0) }); await Frames(3);
        await Mouse(mid + new Vector2(100, 0), MouseButton.Left, false); R("drag pans", camX < cx0, $"{(cx0 - camX) * zoom:0} px");
        double z1 = zoom; var panelPoint = new Vector2(100, 120); await Mouse(panelPoint, MouseButton.WheelUp, true); await Mouse(panelPoint, MouseButton.WheelUp, false); R("wheel over a panel does nothing", zoom == z1);
        Ui.Collapsed = false; await Frames(3);
        await Click(At("Tamarsk")); R("city list selects", Selected?.Name == "Tamarsk");
        var sw = Stopwatch.StartNew(); await Frames(5); while (gliding && sw.Elapsed.TotalSeconds < 10) await Frames(1);
        var s = W2S(Selected!.X, Selected.Y); R("go to city arrives", (s - mid).Length() < 3 && !gliding, $"{sw.Elapsed.TotalSeconds:0.0} s");
        await Click(At("CLOSE")); R("close button", Selected == null);
        Ui.Collapsed = true; await Frames(3);
        zoom = MinZoom(); camX = World.WidthM / 2; camY = World.HeightM / 2; Clamp(); await Frames(5);
        var lp = W2S(World.City("Pohjaa").X, World.City("Pohjaa").Y); await Click(lp); R("click on a map marker selects", Selected?.Name == "Pohjaa"); Selected = null;   // a city with no airfield beside it: a click near a formation rightly picks the formation

        // labels: every zoom and position in a grid, instantly
        labels.Snap = true; int layouts = 0, bad = 0, most = 0, least = 999;
        for (double z = MinZoom(); z < .4; z *= 1.09)
            for (double fx = .1; fx < 1; fx += .2) for (double fy = .1; fy < 1; fy += .2)
            {
                zoom = z; camX = World.WidthM * fx; camY = World.HeightM * fy; Clamp();
                labels.Test(); layouts++; bad += labels.Overlaps(); most = Math.Max(most, labels.Shown); least = Math.Min(least, labels.Shown);
            }
        labels.Snap = false;
        R("labels never overlap", bad == 0, $"{layouts} layouts, {bad} overlapping pairs, {least} to {most} labels shown");

        // a real zoom from the whole theatre to field level, as a player would: how long frames take while tiles are being made
        zoom = MinZoom(); camX = World.City("Kolvograd").X; camY = World.City("Kolvograd").Y; Clamp(); await Frames(10);
        int frames = 0; double worst = 0; var run = Stopwatch.StartNew(); double last = 0; const double Secs = 8; double zs = zoom, ze = 2;
        while (run.Elapsed.TotalSeconds < Secs)
        {
            zoom = zs * Math.Pow(ze / zs, run.Elapsed.TotalSeconds / Secs); camX = World.City("Kolvograd").X + 4000; camY = World.City("Kolvograd").Y; Clamp();
            await Frames(1); frames++; double now = run.Elapsed.TotalMilliseconds; if (frames > 3) worst = Math.Max(worst, now - last); last = now;
        }
        o.AppendLine($"INFO  zoom theatre to fields in {Secs:0} s: {frames / Secs:0} frames per second on average, slowest frame {worst:0} ms, {terrain.Count} tiles held");
        try { await RaidTest(R, o); } catch (Exception ex) { o.AppendLine("FAIL  raid test stopped: " + ex.Message + " " + ex.StackTrace); }
        FileAccess.Open(path, FileAccess.ModeFlags.Write).StoreString(o.ToString());
        GetTree().Quit();
    }
}
