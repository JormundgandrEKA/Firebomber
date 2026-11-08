using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Firebomber;

/// <summary>The campaign clock without frames: quiet days jumped, operations stepped, raids launched from the queue. Run with --campaign --campaigntest file.</summary>
public partial class Main
{
    /// <summary>Runs the war on for this many seconds as fast as the machine allows. Reports nobody reads are closed.</summary>
    void RunCampaign(double secs, Action? each = null)
    {
        if (Camp == null) return;
        double end = Camp.Now + secs; int guard = 0;
        while (Camp.Now < end - 1 && guard++ < 1_000_000)
        {
            if (!ReportDone && OpOver && !OpBusy && Air.Formations.Count > 0) EndOp();
            CampaignStep(OpBusy ? 20 : end - Camp.Now); Fire.SyncTo(Air.T); Air.Recent.Clear(); Sim.Tracers.Clear();
            each?.Invoke();
        }
    }

    /// <summary>Run with --campaign --planlog file --plandays N: the war run on its own for N days (56 if not said), with the UCS planner's decisions, and week by week the UCS bomber arm, support on both sides and the weather.</summary>
    void PlanLog(string path)
    {
        var o = new StringBuilder(); var ra = OS.GetCmdlineUserArgs(); int di = Array.IndexOf(ra, "--plandays"); int days = di >= 0 ? int.Parse(ra[di + 1]) : 56;
        var F = Forces!; var sw = System.Diagnostics.Stopwatch.StartNew();
        string Row() { int b = F.All(false, Craft.TB6) + F.All(false, Craft.SB3), r = F.Ready(false, Craft.TB6) + F.Ready(false, Craft.SB3); var (c, m) = Campaign.WeatherAt(Camp!.Seed, Camp.Now);
            return $"{Camp.DateText}  UCS bombers {b} ({r} ready, {F.Ind.BomberCrews[0]} crews)  I-8 {F.All(false, Craft.I8)}  I-7 {F.All(false, Craft.I7)}  support V {F.War.Support[1]:0} U {F.War.Support[0]:0}  doctrine {F.Planner.Doctrine}  day loss {F.Planner.DayLoss:P0} night {F.Planner.NightLoss:P0}  cloud {c:P0} moon {m:P0}"; }
        o.AppendLine(Row());
        for (int wk = 0; wk < (days + 6) / 7; wk++) { RunCampaign(Math.Min(7, days - wk * 7) * Campaign.Day); o.AppendLine(Row()); }
        o.AppendLine(); o.AppendLine($"{F.Planner.Raids} raids planned, {F.Planner.BigRaids} big, in {sw.Elapsed.TotalSeconds:0} s"); o.AppendLine("Planner:"); foreach (var n in F.Planner.Notes) o.AppendLine("  " + n);
        o.AppendLine("Flak moved: " + string.Join(", ", F.Planner.Flak.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key} x{kv.Value:0.00}")));
        o.AppendLine("Loss by target: " + string.Join(", ", F.Planner.LossAt.Select(kv => $"{kv.Key} {kv.Value:P0}")));
        o.AppendLine("UCS works: " + string.Join(", ", F.Ind.Works.Where(q => !q.Wolf).Select(q => $"{q.City.Name} {q.Points:0} {Industry.Short(q.Making)} plant {Fire.Plant(q.City):P0}")));
        o.AppendLine("Diary:"); foreach (var l in Camp!.Log) o.AppendLine($"  {Camp.Stamp(l.T),-18} {(l.Alarm ? "!" : " ")} {l.Text}");
        System.IO.File.WriteAllText(path, o.ToString()); GetTree().Quit();
    }

    void CampaignTest(string path)
    {
        var o = new StringBuilder(); int fails = 0;
        void R(string name, bool ok, string info = "") { o.AppendLine($"{(ok ? "PASS" : "FAIL")}  {name}{(info != "" ? "   " + info : "")}"); if (!ok) fails++; }
        if (Camp == null) { o.AppendLine("FAIL  run with --campaign"); System.IO.File.WriteAllText(path, o.ToString()); GetTree().Quit(); return; }
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var F = Forces!; var ind = F.Ind; int ucsBombers0 = F.All(false, Craft.TB6) + F.All(false, Craft.SB3), hunds0 = F.All(true, Craft.F5);
        { var ww = ind.Works.Where(q => q.Wolf).ToList(); var wu = ind.Works.Where(q => !q.Wolf).ToList();
          R("every city has works, sized by its factory floor", ww.Count == World.Cities.Count(c => c.Wolf) && wu.Count == World.Cities.Count(c => !c.Wolf) && Math.Abs(ww.Sum(q => q.Points) - Industry.WolfPoints) < 1e-6 && Math.Abs(wu.Sum(q => q.Points) - Industry.UcsPoints) < 1e-6,
            string.Join(", ", ww.OrderByDescending(q => q.Points).Select(q => $"{q.City.Name} {q.Points:0} {Industry.Short(q.Making)}")));
          double Wk(Product p) => ind.Works.Where(q => q.Making == p).Sum(q => Industry.PerWeek(q, 1, 0));
          R("at the start they make about what the old deliveries were", Wk(Product.V16) > 4 && Wk(Product.F5) > 4 && Wk(Product.U4) > 2 && Wk(Product.HE) > 100 && Wk(Product.Incendiary) > 50 && Wk(Product.SB3) > 30 && Wk(Product.I8) > 30,
            $"a week: Vulfen {Wk(Product.V16):0.0} V-16, {Wk(Product.U4):0.0} U-4, {Wk(Product.F5):0.0} F-5, {Wk(Product.HE):0} t HE, {Wk(Product.Incendiary):0} t INC; UCS {Wk(Product.TB6):0} TB-6, {Wk(Product.SB3):0} SB-3, {Wk(Product.I8):0} I-8, {Wk(Product.I7):0} I-7"); }
        int crews0 = ind.BomberCrews[1], pilots0 = ind.Pilots[1]; double he0 = ind.He[1];
        R("every city has a field", World.Fields.Count == World.Cities.Count(c => c.Wolf) && World.EnemyFields.Count == World.Cities.Count(c => !c.Wolf), $"{World.Fields.Count} Vulfen, {World.EnemyFields.Count} UCS");
        R("the order of battle", F.All(true, Craft.V16) == 288 && F.All(true, Craft.U4) == 96 && hunds0 > 100 && F.All(false, Craft.I8) > 400 && ucsBombers0 > 400,
            $"Vulfen {F.All(true, Craft.V16)} V-16, {F.All(true, Craft.U4)} U-4, {hunds0} F-5; UCS {F.All(false, Craft.TB6)} TB-6, {F.All(false, Craft.SB3)} SB-3, {F.All(false, Craft.I7)} I-7, {F.All(false, Craft.I8)} I-8");
        R("the war starts on a Monday at 06:00", Camp.Date.DayOfWeek == DayOfWeek.Monday && Math.Abs(Camp.Hour - 6) < 1e-9, Camp.DateText);

        // two weeks of war on its own: UCS raids come from the queue as operations, and quiet time is jumped
        double worstHour = 0, worstFire = 0; int busySteps = 0, ops = 0; Air lastAir = Air;
        RunCampaign(14 * Campaign.Day, () =>
        {
            if (Air != lastAir) { ops++; lastAir = Air; }
            if (OpBusy) { busySteps++; double d = Math.Abs(Air.Hour - Camp.Hour); worstHour = Math.Max(worstHour, Math.Min(d, 24 - d)); }
            worstFire = Math.Max(worstFire, Camp.Now - Fire.T);
        });
        double secs = sw.Elapsed.TotalSeconds;
        R("two weeks pass", Camp.DayNo == 15, $"day {Camp.DayNo}, {Camp.DateText}");
        R("weekly ticks", Camp.Log.Count(l => l.Text.StartsWith("Week ")) == 2, string.Join(", ", Camp.Log.Where(l => l.Text.StartsWith("Week ")).Select(l => Camp.Stamp(l.T))));
        R("the UCS raids on its own", ops >= 1 && Camp.Log.Any(l => l.Text.StartsWith("RADAR")), $"{ops} operations, {Camp.Log.Count(l => l.Text.StartsWith("RADAR"))} radar warnings");
        R("bombs fall on Vulfen cities", Camp.Log.Any(l => l.Text.StartsWith("Bombs falling on")), string.Join("; ", Camp.Log.Where(l => l.Text.StartsWith("Bombs falling")).Select(l => l.Text.Replace("Bombs falling on ", "").TrimEnd('.'))));
        R("the air's hour is the calendar's", worstHour < .01, $"worst {worstHour * 60:0.0} min over {busySteps} busy steps");
        R("the fire keeps up with the clock", worstFire < 120, $"worst lag {worstFire:0} s");
        R("fires go out, and the diary gives the toll", Camp.Log.Any(l => l.Text.StartsWith("Fires out in") && l.Text.Contains("dead")), Camp.Log.FirstOrDefault(l => l.Text.StartsWith("Fires out in")).Text ?? "");
        { var hit = Fire.Records.Where(kv => kv.Key.Wolf && kv.Value.Raids > 0).ToList();
          R("bombed cities have a record: raids, tonnes, dead, homeless", hit.Count > 0 && hit.All(kv => kv.Value.Tonnes > 0 && (kv.Value.Lost > 0 || kv.Value.Tonnes < 20)) && hit.Sum(kv => kv.Value.Dead) > 0 && hit.Sum(kv => kv.Value.Homeless) > 0,
            string.Join("; ", hit.Select(kv => $"{kv.Key.Name} {kv.Value.Raids} raids {kv.Value.Tonnes:0} t, {kv.Value.Lost} lost, {Round(kv.Value.Dead)} dead, {Round(kv.Value.Homeless)} homeless"))); }
        { int ucsNow = F.All(false, Craft.TB6) + F.All(false, Craft.SB3), delivered = (int)(ind.Ever.GetValueOrDefault(Product.TB6) + ind.Ever.GetValueOrDefault(Product.SB3)), hundsMade = (int)ind.Ever.GetValueOrDefault(Product.F5);
          R("UCS raids cost UCS bombers", ucsNow < ucsBombers0 + delivered, $"{ucsBombers0} at the start, {delivered} made, {ucsNow} now");
          R("our losses are written off, and the diary says so", F.All(true, Craft.F5) == hunds0 + hundsMade && !Camp.Log.Any(l => l.Text.StartsWith("Written off")) || F.All(true, Craft.F5) < hunds0 + hundsMade && Camp.Log.Any(l => l.Text.StartsWith("Written off")), $"F-5 {hunds0} + {hundsMade} made, {F.All(true, Craft.F5)} now; {Camp.Log.Count(l => l.Text.StartsWith("Written off"))} entries");
          R("each week the diary has what the works made", Camp.Log.Count(l => l.Text.StartsWith("Manufactured")) == 2, (Camp.Log.LastOrDefault(l => l.Text.StartsWith("Manufactured")).Text ?? "").Replace("\n", " / "));
          R("aircraft and bombs come from the works", ind.Ever.GetValueOrDefault(Product.V16) >= 6 && ind.Ever.GetValueOrDefault(Product.HE) > 150 && ind.He[1] > he0, $"{ind.Ever.GetValueOrDefault(Product.V16):0} V-16, {ind.Ever.GetValueOrDefault(Product.U4):0} U-4, {hundsMade} F-5, {ind.Ever.GetValueOrDefault(Product.HE):0} t HE made; HE in the dumps {he0:0} t to {ind.He[1]:0} t");
          R("crews pass out of the schools", ind.BomberCrews[1] > crews0 - 10 && ind.Pilots[1] > pilots0, $"bomber crews {crews0} to {ind.BomberCrews[1]}, pilots {pilots0} to {ind.Pilots[1]}"); }
        R("quickly", secs < 120, $"{secs:0.0} s for 14 days");

        // a raid ordered for tonight waits on the ground, then goes at 22:00
        RunCampaign(0); while (OpBusy) RunCampaign(600);
        // no raid goes without its bombs, and none flies more bombers than there are crews
        { StartSetup(); double h0 = ind.He[1], i0 = ind.Inc[1]; ind.He[1] = 0; ind.Inc[1] = 0; FinishSetup(); string? why = ShortOf(); SendRaid();
          R("a raid cannot go short of bombs", why != null && Planning, why ?? "it went"); BackToSetup(); ind.He[1] = h0; ind.Inc[1] = i0;
          int c0 = ind.BomberCrews[1]; ind.BomberCrews[1] = 20; FinishSetup(); int n = Air.Formations.Where(f => f.Wolf).Sum(f => f.Ac.Count);
          R("only as many bombers as crews", n > 0 && n <= 20, $"{n} bombers with 20 crews"); BackToSetup(); ind.BomberCrews[1] = c0; CancelSetup(); Sortie = null;      // the cut flights would otherwise be the next raid's
          int p0 = ind.Pilots[0]; ind.Pilots[0] = 50; int up = GarrisonFor().Where(g => !g.Wolf).Sum(g => g.N); ind.Pilots[0] = p0;
          R("only as many fighters up as pilots", up <= 50 && up >= 40, $"{up} UCS fighters with 50 pilots"); }
        var dueBefore = ind.Schools.Where(c => c.Wolf && c.Bomber).Select(c => (c.T, c.N)).ToList(); int crewsBefore = ind.BomberCrews[1];
        double before = Camp.Now; StartSetup(); var s = Sortie!; s.Target = World.City("Kolvograd"); s.Hour = 22; int v16Ready = F.Ready(true, Craft.V16), v16All = F.All(true, Craft.V16); double made0 = ind.Ever.GetValueOrDefault(Product.V16); FinishSetup();
        var need = RaidNeeds(); double heB = ind.He[1], incB = ind.Inc[1], fuelB = ind.Fuel[1]; SendRaid();
        R("the raid takes its bombs and fuel from the dumps", need.he + need.inc > 50 && Math.Abs(ind.He[1] - (heB - need.he)) < 1e-6 && Math.Abs(ind.Inc[1] - (incB - need.inc)) < 1e-6 && Math.Abs(ind.Fuel[1] - (fuelB - need.fuel)) < 1e-6,
            $"{need.he:0} t HE, {need.inc:0} t incendiary, {need.fuel:0} t fuel; HE {heB:0} to {ind.He[1]:0} t");
        int sent = Air.Formations.Where(f => f.Wolf && f.Kind == Craft.V16).Sum(f => f.Ac.Count);
        R("the raid is drawn from the fields", sent > 0 && F.Ready(true, Craft.V16) == v16Ready - sent, $"{sent} V-16 sent of {v16Ready} ready, {F.Ready(true, Craft.V16)} left");
        var own = Air.Formations.Where(f => f.Wolf).ToList(); double launch = Air.LaunchAt; var opAir = Air;      // a UCS raid in the next two days makes a new operation: what came home early is in this one's list
        R("the raid is set for 22:00", Math.Abs(Campaign.HourAt(launch) - 22) < 1e-6 && launch > before, Camp.Stamp(launch));
        R("its flights wait for it", own.Count == s.Flights.Count(q => q.N > 0) && own.All(f => f.Phase == Phase.Waiting && f.DepartT >= launch - 1), $"first {Camp.Stamp(own.Min(f => f.DepartT))}");
        R("the wait is quiet", Air.Quiet && !OpBusy, "");
        double offAt = double.NaN; RunCampaign(2 * Campaign.Day, () => { if (double.IsNaN(offAt) && offTold) offAt = Camp.Now; });
        R("it takes off on time", Math.Abs(offAt - launch) < 30, double.IsNaN(offAt) ? "never" : Camp.Stamp(offAt));
        R("its bombs fall on Kolvograd", Camp.Log.Any(l => l.Text == "Our bombs falling on Kolvograd."), "");
        R("and it comes home", own.All(f => f.Phase == Phase.Landed), string.Join(" ", own.Select(f => $"{f.Name}:{f.Phase}")));
        R("crews carry over", Air.Crews.WolfCaptains.Count > 0 || !opKept, $"raid {Air.Crews.Raid}");
        { EndOp(); int back = own.Where(f => f.Kind == Craft.V16).Sum(f => f.Ac.Count) + opAir.BackHome.Count(a => a.F.Wolf && a.F.Kind == Craft.V16), lostV = sent - back, all = F.All(true, Craft.V16), made = (int)(ind.Ever.GetValueOrDefault(Product.V16) - made0);
          R("those that came back are home again, the lost written off", all == v16All - lostV + made, $"{v16All} before, {sent} sent, {back} back ({opAir.BackHome.Count(q => q.F.Wolf && q.F.Kind == Craft.V16)} landed early), {lostV} lost, {made} new: {all} V-16 now ({F.Ready(true, Craft.V16)} ready, {F.Side(true).Sum(b => b.S(Craft.V16).ComingN)} in repair)");
          R("after servicing they are ready again", F.Ready(true, Craft.V16) > v16Ready - sent, $"{v16Ready - sent} ready when the raid had gone, {F.Ready(true, Craft.V16)} now");
          int grads = dueBefore.Where(c => c.T <= Camp.Now).Sum(c => c.N), expect = crewsBefore + grads, lostB = own.Sum(f => f.Start) - own.Sum(f => f.Ac.Count) - opAir.BackHome.Count(a => a.F.Wolf);
          R("lost crews come out of the pool, less those who got back", lostB == 0 ? ind.BomberCrews[1] == expect : ind.BomberCrews[1] < expect && ind.BomberCrews[1] >= expect - lostB, $"{crewsBefore} crews, {grads} passed out, {lostB} bombers lost: {ind.BomberCrews[1]} now"); }
        // retooling: a works changed over makes nothing for weeks, then works up
        { var q = ind.Works.First(w => w.Wolf && w.Making == Product.F5); double t = Camp.Now; ind.Order(q, Product.U4, t);
          R("a works retooling makes nothing for four weeks, then works up over two", Industry.PerWeek(q, 1, t + 10 * Campaign.Day) == 0 && q.Factor(t + 29 * Campaign.Day) is > .3 and < 1 && q.Factor(t + 43 * Campaign.Day) == 1,
            $"{q.City.Name}: F-5 to U-4, output {q.Factor(t + 10 * Campaign.Day):0.00} at 10 days, {q.Factor(t + 29 * Campaign.Day):0.00} at 29, {q.Factor(t + 43 * Campaign.Day):0.00} at 43");
          var h = ind.Works.First(w => w.Wolf && w.Making == Product.HE); ind.Order(h, Product.Incendiary, t);
          R("bombs to bombs is a week", h.Factor(t + 6 * Campaign.Day) == 0 && h.Factor(t + 8 * Campaign.Day) > 0, h.City.Name);
          ind.Order(q, Product.F5, t); ind.Order(h, Product.HE, t); }
        // bombing the works: half the factory floor gone, half the output
        { var c = World.Cities.Where(p => !p.Wolf).OrderBy(p => p.Size).First(); var cf = Fire.Materialise(c); var q = ind.Of(c)!; double full = Industry.PerWeek(q, Fire.Plant(c), Camp.Now);
          int k = 0; for (int i = 0; i < cf.B.Length; i++) if (cf.B[i].Mat == CityPlan.Factory && k++ % 2 == 0) Fire.Apply(cf, i, FireSim.Gutted, 0, false);
          double cut = Industry.PerWeek(q, Fire.Plant(c), Camp.Now);
          R("bombing a city's works cuts its output", Fire.Plant(c) is > .35 and < .65 && cut < full * .7, $"{c.Name}: plant {Fire.Plant(c) * 100:0}%, {Industry.Short(q.Making)} {full:0.0} to {cut:0.0} a week"); }
        // ferrying six Hunds from the field with most ready to the one with most room
        { var a0 = F.Side(true).OrderByDescending(b => b.S(Craft.F5).Ready).First(); var b0 = F.Side(true).Where(b => b != a0).OrderByDescending(b => b.Capacity - b.Total).First(); int ra = a0.S(Craft.F5).Ready, rb = b0.S(Craft.F5).Ready;
          var why = F.Ferry(a0, b0, Craft.F5, 6, Camp.Now);
          R("a ferry flight leaves", why == null && a0.S(Craft.F5).Ready == ra - 6 && b0.S(Craft.F5).ComingN >= 6, why ?? "");
          F.Tick(Camp.Now + Campaign.Day); R("and arrives", b0.S(Craft.F5).Ready >= rb + 6, $"{a0.Name} to {b0.Name}: F-5 there {rb} to {b0.S(Craft.F5).Ready}");
          R("a field cannot take more than it has room for", F.Ferry(a0, b0, Craft.F5, 1000, Camp.Now) != null, F.Ferry(a0, b0, Craft.F5, 1000, Camp.Now) ?? ""); }

        // skipping goes to the next thing that matters, and no further
        RunCampaign(0); while (OpBusy) RunCampaign(600);
        double t0 = Camp.Now, next = Camp.NextKnown; SkipAhead();
        R("skip goes to the next tick or the next operation", Camp.Now > t0 && Camp.Now <= next + 1, $"{Camp.Stamp(t0)} to {Camp.Stamp(Camp.Now)}");

        // repair: two months on, much of the works is back, little of the housing
        {
            int Down() => Fire.Cities.Sum(c => c.Count[0, 2] + c.Count[0, 3] + c.Count[1, 2] + c.Count[1, 3] + c.Count[2, 2] + c.Count[2, 3]);
            int WorksDown() => Fire.Cities.Sum(c => c.Count[2, 2] + c.Count[2, 3]);
            int downBefore = Down(), worksBefore = WorksDown(); double homelessBefore = Fire.Records.Values.Sum(r => r.Homeless);
            for (int k = 0; k < 60; k++) Fire.Repair(1);
            int after = Down(), worksAfter = WorksDown();
            R("works come back in months, housing more slowly", worksBefore == 0 || worksAfter < worksBefore * .6, $"works down {worksBefore} to {worksAfter}, all buildings down {downBefore} to {after}");
            { var c = World.Cities.Where(p => !p.Wolf).OrderBy(p => p.Size).First(); R("and so does their output", Fire.Plant(c) > .6, $"{c.Name}: plant {Fire.Plant(c) * 100:0}%"); }
            R("rebuilding rehouses people", Fire.Records.Values.Sum(r => r.Homeless) < homelessBefore && Fire.Records.Values.Sum(r => r.Rebuilt) > 0, $"homeless {Round(homelessBefore)} to {Round(Fire.Records.Values.Sum(r => r.Homeless))}");
        }
        // the UCS planner (part 5)
        {
            var pl = F.Planner; var kol = World.City("Kolvograd");
            R("the planner sends the UCS raids, and reads its results", pl.Raids >= 1 && pl.Notes.Any(n => n.Contains("raid")) && (pl.LossAt.Count > 0 || Math.Abs(pl.NightLoss - .06) + Math.Abs(pl.DayLoss - .08) > 1e-9),
              $"{pl.Raids} raids, {pl.BigRaids} big; losses by day {pl.DayLoss:P0}, by night {pl.NightLoss:P0}; escorts {pl.EscortsPerBox} a box; " + string.Join(" | ", pl.Notes.TakeLast(3)));
            R("a city we bombed is on its mind", pl.Alarm(kol, Fire, Camp.Now) > 0, $"Kolvograd alarm {pl.Alarm(kol, Fire, Camp.Now):0.0}");
            pl.Weekly(Camp.Now, Camp, World, F, Fire, F.War, new Random(1));
            R("it moves flak towards it, taking guns from quieter cities", pl.FlakOf(kol) > 1 && World.Cities.Where(c => !c.Wolf).Any(c => pl.FlakOf(c) < 1), string.Join(", ", pl.Flak.OrderByDescending(kv => kv.Value).Take(3).Select(kv => $"{kv.Key} x{kv.Value:0.00}")));
            var marren = World.Cities.Where(p => !p.Wolf).OrderBy(p => p.Size).First();
            R("and rebuilds bombed works first", pl.RepairBoost(marren, Fire) > 1 && pl.RepairBoost(World.City("Tamarsk"), Fire) == 1, $"{marren.Name} x{pl.RepairBoost(marren, Fire)}");
            { var a2 = new Air(World, 1); var tgt = World.City("Ostrand"); var fld = World.EnemyFields.OrderBy(f => Math.Abs(f.X - tgt.X) + Math.Abs(f.Y - tgt.Y)).First(); a2.Raid(tgt, new[] { (fld, Craft.SB3, 12) }, false, 0, true); var wk = CityPlan.Works(tgt);
              R("a raid on the works is aimed at the works", Math.Abs(a2.Formations[0].AimX - wk.x) < 1000 && Math.Abs(a2.Formations[0].AimY - wk.y) < 1000, $"aim {(a2.Formations[0].AimX - wk.x):0}, {(a2.Formations[0].AimY - wk.y):0} m from the works"); }
            { var w1 = Campaign.WeatherAt(Camp.Seed, Camp.Now); var w2 = Campaign.WeatherAt(Camp.Seed, Camp.Now); var half = Campaign.WeatherAt(Camp.Seed, Camp.Now + 14.76 * Campaign.Day);
              var clouds = Enumerable.Range(0, 60).Select(k => Campaign.WeatherAt(Camp.Seed, k * Campaign.Day).cloud).ToList();
              R("the weather is the same however often it is asked; the moon waxes and wanes; cloud varies", w1 == w2 && Math.Abs(w1.moon - half.moon) > .8 && clouds.Min() < .35 && clouds.Max() > .65 && clouds.All(c => c is >= 0 and <= 1),
                $"moon {w1.moon:P0} now, {half.moon:P0} in a fortnight; cloud over 60 days {clouds.Min():P0} to {clouds.Max():P0}"); }
        }
        // the console's records (second roadmap, stage 1)
        {
            var h = F.Hist; int days = Camp.DayNo;
            R("each side is sampled every morning, for the graphs", h.Side.All(q => q.Count >= days - 1 && q.Count <= days + 1) && h.Side[1][^1].Output > 0 && h.Side[1][^1].Bombers > 0, $"{h.Side[1].Count} samples in {days} days; output {h.Side[1][^1].Output:0}, HE {h.Side[1][^1].He:0} t, {h.Side[1][^1].Bombers} bombers");
            var hit = World.Cities.First(c => c.Wolf && Fire.Records.TryGetValue(c, out var r0) && r0.Dead > 0); var quiet = World.Cities.FirstOrDefault(c => !Fire.Records.ContainsKey(c));
            R("a city is sampled when it changes", h.City[hit.Name].Count > 1 && (quiet == null || h.City[quiet.Name].Count == 1) && h.City[hit.Name][^1].Alive < FireSim.PopulationOf(hit), $"{hit.Name}: {h.City[hit.Name].Count} samples" + (quiet != null ? $"; {quiet.Name}, never bombed: {h.City[quiet.Name].Count}" : ""));
            var pl2 = F.Planner; int before2 = Camp.Log.Count; string was = pl2.WarnTarget; bool any = false;
            for (int k = 0; k < 400 && !any; k++) { pl2.StandDownUntil = 0; pl2.LastBig = -1e9; var c0 = new Campaign(World, 77 + k) { }; pl2.Daily(0, c0, World, F, Fire, new Random(k)); any = c0.Log.Any(l => l.Text.Contains("may strike")); if (any) R("intelligence warns of raids, naming a city", pl2.WarnTarget != "" && pl2.WarnN > 0 && pl2.NewWarning, c0.Log.First(l => l.Text.Contains("may strike")).Text); }
            if (!any) R("intelligence warns of raids, naming a city", false, "none in 400 tries");
            pl2.NewWarning = false; pl2.WarnTarget = was; pl2.WarnUntil = 0; R("warnings are the planner's, not the war's", Camp.Log.Count == before2, "");
        }
        // weather and accuracy (second roadmap, stage 2)
        {
            int sd = Camp.Seed; double cx = World.WidthM / 2, cy = World.HeightM / 2; int n = 0, rainy = 0, storms = 0, cloudy = 0, fair = 0, windy = 0; double wMin = 99, wMax = 0, dMin = 1, dMax = 0;
            for (double t = 0; t < 365 * Campaign.Day; t += 6 * 3600) { var (c, r) = Weather.At(sd, cx, cy, t); n++; if (r > .08) rainy++; if (r > .6) storms++; if (c > .7) cloudy++; if (c < .4) fair++; var (ws, _) = Weather.WindAt(sd, t); wMin = Math.Min(wMin, ws); wMax = Math.Max(wMax, ws); if (ws > 10) windy++; double dr = Weather.Dryness(sd, cx, cy, t); dMin = Math.Min(dMin, dr); dMax = Math.Max(dMax, dr); }
            R("a year of weather is varied but not nutty", rainy > n * .05 && rainy < n * .4 && storms > 0 && storms < n * .12 && cloudy > n * .07 && fair > n * .25 && wMin < 5 && wMax > 18 && windy > n * .04 && windy < n * .3 && dMin < .15 && dMax > .7, $"of {n} six-hourly looks: rain {100.0 * rainy / n:0}%, storm {100.0 * storms / n:0}%, heavy cloud {100.0 * cloudy / n:0}%, fair {100.0 * fair / n:0}%; wind {wMin:0}-{wMax:0} m/s, over 10 m/s {100.0 * windy / n:0}%; dryness {dMin:0.00}-{dMax:0.00}");
            { double best = -1, bt = 0; for (double t = 0; t < 120 * Campaign.Day; t += 3600) { var g = Weather.At(sd, cx, cy, t).cloud; if (g > best) { best = g; bt = t; } }
              double Peak(double t) { double mx = 0, mc = -1; for (double x = 0; x < World.WidthM; x += 12000) { double c = Weather.At(sd, x, cy, t).cloud; if (c > mc) { mc = c; mx = x; } } return mx; }
              double was = Peak(bt - 10 * 3600), then = Peak(bt + 10 * 3600);
              R("fronts move across the map", then - was > 150000, $"the thickest cloud moves from {was / 1000:0} km to {then / 1000:0} km east in 20 hours"); }
            var a1 = FireSim.FireDay(.9, 13, 0); var a2 = FireSim.FireDay(.4, 6, 0); var a3 = FireSim.FireDay(.1, 2, .7);
            R("firestorms come easier when dry and windy, harder in rain", a1.area < a2.area && a2.area < a3.area && a1.heat > a2.heat && a2.heat > a3.heat && Math.Abs(a2.area - 1) < .05, $"area needed: dry and windy {a1.area:0.00}, ordinary {a2.area:0.00}, wet {a3.area:0.00}");
            R("a gale over dry ground throws embers; a calm or wet day does not", FireSim.Embers(.8, 22, 0) > FireSim.Embers(.4, 12, 0) && FireSim.Embers(.4, 12, 0) > 0 && FireSim.Embers(.8, 7, 0) == 0 && FireSim.Embers(.8, 22, 1) == 0, $"chance a second: gale {FireSim.Embers(.8, 22, 0):0.0000}, breeze {FireSim.Embers(.4, 12, 0):0.0000}");
            R("scatter falls away with height", Air.HeightAim(6000) == 1 && Air.HeightAim(1000) < .1 && Air.HeightAim(2000) < .25, $"against 6,000 m: 4,000 m {Air.HeightAim(4000):0.00}, 2,000 m {Air.HeightAim(2000):0.00}, 1,000 m {Air.HeightAim(1000):0.00}");
            double worst = 0; Place from = World.EnemyFields[0], to = World.Cities.First(c => c.Wolf); double wt = 0; for (double t = 0; t < 365 * Campaign.Day; t += 12 * 3600) { double r = UcsPlanner.RouteRain(sd, from, to, t); if (r > worst) { worst = r; wt = t; } }
            var cam2 = new Campaign(World, sd); var ev = new CampaignEvent { T = wt, Kind = Ev.UcsRaid, Target = to, Waves = 5 }; typeof(Campaign).GetProperty("Now")!.SetValue(cam2, wt);
            R("a big raid will not take off into a storm", worst > .55 && F.Planner.Scrub(ev, cam2, F), $"rain {worst:P0} on the way, {cam2.Stamp(wt)}");
        }
        // orders and flights (second roadmap, stage 3)
        {
            var bh = F.Bases.First(b => b.Wolf && b.S(Craft.F5).Ready >= 6); int all = GarrisonFor().Where(g => g.Field == bh.Field && g.Escort == null).Sum(g => g.N);
            bh.StandDown = true; int down = GarrisonFor().Where(g => g.Field == bh.Field && g.Escort == null).Sum(g => g.N); bh.StandDown = false;
            R("a field stood down sends nothing up", all > 0 && down == 0, $"{bh.Name}: {all} on alert, {down} stood down");
            var pa = new Patrol { Kind = Craft.F5, N = 4, X = bh.Field.X, Y = bh.Field.Y - 120000, Hours = 2 }; bh.Patrols.Add(pa); bh.Intercept = InterceptMode.OnTrack; var gs = GarrisonFor();
            R("a patrol is drawn from its field, and the field's setting goes with its fighters", gs.Any(g => g.On == pa && g.N == 4) && gs.Where(g => g.Field == bh.Field && g.Escort == null).All(g => g.Mode == InterceptMode.OnTrack) && gs.Where(g => g.Field == bh.Field && g.Escort == null).Sum(g => g.N) == all, $"{gs.Where(g => g.Field == bh.Field && g.Escort == null).Sum(g => g.N)} in all");
            var h0 = pa.Hours; pa.Hours = 0; bool dayUp = pa.Up(10), dayNight = pa.Up(23); pa.Hours = 1; bool nightUp = pa.Up(23), nightDay = pa.Up(10); pa.Hours = h0;
            R("patrols keep their hours", dayUp && !dayNight && nightUp && !nightDay, "");
            bh.Intercept = InterceptMode.Direct;
            { var hj = World.City("Hjalmar"); var a2 = new Air(World, 9); a2.TestRaid(hj, 1, new[] { Craft.TB6 }, false); var box = a2.Formations[0]; var fld = World.Fields.First(); double px = hj.X + (box.HomeX - hj.X) * .25, py = hj.Y + (box.HomeY - hj.Y) * .25; var pt = new Patrol { Kind = Craft.F5, N = 6, X = px, Y = py, Hours = 2 };
              var s2 = new FighterSim(World, a2, new[] { new Garrison(fld, Craft.F5, 6, true, null, pt) }, 5); bool waited = s2.All.All(q => q.Act == Act.Ground); bool quietFirst = !a2.Busy!(); double firstUp = double.NaN, upAt = 0; int n = 0; for (; n < 2400 && double.IsNaN(firstUp); n++) { a2.Advance(5); a2.Recent.Clear(); s2.Tracers.Clear(); var u = s2.All.FirstOrDefault(q => q.Airborne); if (u != null) { firstUp = a2.T; upAt = Math.Sqrt(Math.Pow(u.X - px, 2) + Math.Pow(u.Y - py, 2)); } }
              R("a patrol waits as a timetable, and is up at its station when a raid comes near", waited && quietFirst && !double.IsNaN(firstUp) && upAt < 30000, $"up at T+{firstUp / 60:0} min, {upAt / 1000:0} km from its station");
              F.Planner.Daily(Camp.Now, new Campaign(World, 123), World, F, Fire, new Random(7)); R("the UCS patrols the cities it is worried about", F.Side(false).Any(b => b.Patrols.Count > 0), string.Join(", ", F.Side(false).Where(b => b.Patrols.Count > 0).Select(b => $"{b.Name} {b.Patrols[0].N}"))); F.Planner.NewWarning = false; }
            { var a0 = F.Side(true).OrderByDescending(b => b.S(Craft.F5).Ready).First(); var b0 = F.Side(true).Where(b => b != a0).OrderByDescending(b => b.Capacity - b.Total).First(); int fl = F.Flights.Count; F.Ferry(a0, b0, Craft.F5, 2, Camp.Now);
              R("a ferry is on the map until it lands", F.Flights.Count == fl + 1 && F.Flights[^1].Land > Camp.Now, $"{a0.Name} to {b0.Name}, {(F.Flights[^1].Land - Camp.Now) / 60:0} min"); }
            { var a3 = new Air(World, 3); var tg = World.City("Kolvograd"); a3.TestRaid(tg, 1, new[] { Craft.V16 }, true); var fm = a3.Formations[0]; for (int n = 0; n < 200 && fm.Phase != Phase.Outbound; n++) a3.Advance(30); a3.Advance(600); double km0 = Air.FuelKm(fm); var other = World.Fields.First(q => q.Name != fm.HomeName); Air.Divert(fm, other);
              R("a formation can abort to another field, and knows how far it can go", fm.Phase == Phase.Returning && fm.HomeName == other.Name && fm.Ac.All(q => q.HomeName == other.Name) && km0 > 100, $"to {other.Name}, fuel for {km0:N0} km"); }
        }
        // physical targets (second roadmap, stage 4)
        {
            var S = F.Sites; bool clear = true; string bad = "";
            foreach (var f in World.Fields.Concat(World.EnemyFields)) foreach (var c in World.Cities) { if (c.Layout == null) continue; double m = c.Layout.Plots.Min(p => Math.Abs(p.X - f.X) + Math.Abs(p.Y - f.Y)); if (m < 1100 || World.IsWater(f.X, f.Y, 64)) { clear = false; bad = $"{f.Name} {m:0} m from {c.Name}"; } }
            R("every airfield is on clear, dry ground", clear, bad == "" ? $"{World.Fields.Concat(World.EnemyFields).Count()} fields" : bad);
            R("the sites are all there", S.All.Count(q => q.Kind == SiteKind.Field) == World.Fields.Count + World.EnemyFields.Count && S.All.Count(q => q.Kind == SiteKind.Depot) == 13 && S.All.Count(q => q.Kind == SiteKind.Radar) == 7 && S.All.Count(q => q.Kind == SiteKind.Flak) > 100, $"{S.All.Count} sites");
            var rg = new Random(5); var st = S.Of(World.Fields[0]); var bs = F.Of(World.Fields[0]); int parked0 = bs.Of.Values.Sum(q => q.Ready); double t0h = Camp.Now;
            foreach (var p in st.Parts.Where(q => q.Kind == PartKind.Runway)) S.Hit(new Impact { X = p.X, Y = p.Y, Kg = 500 }, t0h, F, rg);
            R("bombs on a runway close the field", st.Closed && S.Closed(World.Fields[0]) && GarrisonFor().All(g => g.Field != World.Fields[0] || g.Escort != null), $"{st.RunwayDown} sections cratered");
            for (int k = 0; k < 40; k++) S.Hit(new Impact { X = st.X + rg.NextDouble() * 400 - 200, Y = st.Y + rg.NextDouble() * 400 - 200, Kg = 250 }, t0h, F, rg);
            R("bombs on a field destroy aircraft on the ground", bs.Of.Values.Sum(q => q.Ready) < parked0, $"{parked0} to {bs.Of.Values.Sum(q => q.Ready)}");
            S.Daily(t0h + 3 * Campaign.Day); R("and the runway is filled in within days", !st.Closed, $"{st.RunwayDown} sections still down");
            var dep = S.All.First(q => q.Kind == SiteKind.Depot && !q.Wolf); double heD = F.Ind.He[0], fu0 = F.Ind.Fuel[0]; foreach (var p in dep.Parts.ToList()) S.Hit(new Impact { X = p.X, Y = p.Y, Kg = 1000 }, t0h, F, rg);
            R("a depot destroyed takes its share of the stocks with it", dep.Lost >= 5 && F.Ind.He[0] < heD * .95 && F.Ind.Fuel[0] < fu0 * .95, $"{dep.Name}: HE {heD:0} to {F.Ind.He[0]:0} t, fuel {fu0:0} to {F.Ind.Fuel[0]:0} t");
            var dp2 = S.All.First(q => q.Kind == SiteKind.Depot && q.Wolf); var chain = dp2.Parts.First(q => q.Kind == PartKind.Bombs); int sc0 = dp2.Lost; S.Hit(new Impact { X = chain.X, Y = chain.Y, Kg = 1000 }, t0h, F, new Random(1)); R("stores explode and take their neighbours", dp2.Lost - sc0 >= 2, $"{dp2.Lost - sc0} sheds from one bomb");
            var kol = World.City("Kolvograd"); var fk = S.FlakOf(kol).First(); int g0 = fk.Guns; var rg2 = new Random(2); foreach (var p in fk.Parts.Take(2)) for (int q2 = 0; q2 < 8; q2++) S.Hit(new Impact { X = p.X, Y = p.Y, Kg = 500 }, t0h, F, rg2);
            var fl = new FlakSim(World, new Air(World, 2), 1, 1, 3, null, S); R("flak batteries stay put and lose guns", fk.Guns < g0 && fl.Batteries.First(q => q.City == kol).X == fk.X && fl.Batteries.First(q => q.City == kol).Guns == fk.Guns, $"{g0} to {fk.Guns} guns");
            var rad = S.All.First(q => q.Kind == SiteKind.Radar); double ox = rad.X, oy = rad.Y - 200000; bool cov0 = S.RadarCovers(ox, oy); foreach (var r2 in S.All.Where(q => q.Kind == SiteKind.Radar)) foreach (var p in r2.Parts) p.Down = true; bool cov1 = S.RadarCovers(ox, oy); foreach (var r2 in S.All.Where(q => q.Kind == SiteKind.Radar)) foreach (var p in r2.Parts) p.Down = false;
            R("the radar chain watches the coast, until it is knocked out", cov0 && !cov1, "");
            { var c9 = World.Cities.First(q => q.Wolf && q.Name == "Tarvik"); var cf = Fire.Materialise(c9); int vi = Array.FindIndex(cf.B, b => b.Volatile); if (vi >= 0) { int b0 = cf.Count[0, FireSim.Blasted] + cf.Count[1, FireSim.Blasted]; Fire.Apply(cf, vi, FireSim.Burning, 2, false); double t8 = Fire.T + 90; Fire.SyncTo(t8); R("volatile works go up when they catch", !cf.B[vi].Volatile && cf.Count[0, FireSim.Burning] + cf.Count[2, FireSim.Burning] + cf.Count[2, FireSim.Gutted] >= 0, $"{cf.P.Name}: {cf.Count[0, FireSim.Blasted] + cf.Count[1, FireSim.Blasted] - b0} neighbours flattened"); } else R("volatile works go up when they catch", false, "none in Tarvik"); }
            var pp = F.Planner; var d0 = pp.Doctrine; pp.Doctrine = Doctrine.CounterAir; pp.LastBig = -1e9; pp.StandDownUntil = 0; bool aimed = false;
            for (int k = 0; k < 300 && !aimed; k++) { var c0 = new Campaign(World, 300 + k); pp.Daily(0, c0, World, F, Fire, new Random(k)); aimed = c0.Pending.Any(e => e.Site != ""); }
            pp.Doctrine = d0; pp.NewWarning = false; pp.WarnUntil = 0; pp.WarnTarget = ""; R("the UCS planner goes for fields and radar", aimed, "");
            foreach (var q in S.All) foreach (var p in q.Parts) { p.Down = false; }
            S.Report.Clear();
        }
        // war support (part 4)
        {
            var war = F.War;
            R("each week the diary books war support and intelligence", Camp.Log.Count(l => l.Text.StartsWith("War support")) >= 2 && Camp.Log.Count(l => l.Text.StartsWith("Intelligence:")) >= 2,
              Camp.Log.Last(l => l.Text.StartsWith("War support")).Text + " / " + Camp.Log.Last(l => l.Text.StartsWith("Intelligence:")).Text);
            R("being bombed costs support", war.Habit[1] > 0 && war.Support[1] < 80, $"Vulfen {war.Support[1]:0}, UCS {war.Support[0]:0} (estimated {war.UcsEstimate:0})");
            { var hit = Fire.Records.Where(kv => kv.Key.Wolf && kv.Value.Homeless > 1000).Select(kv => kv.Key).FirstOrDefault();
              R("the dead and the homeless cut a city's labour", hit != null && war.Labour(hit, Fire) < war.Turnout(true), hit == null ? "no city hit" : $"{hit.Name}: labour {war.Labour(hit, Fire) * 100:0}%"); }
            { double s0 = war.Support[1]; war.Support[1] = 30; double low = war.Turnout(true); war.Support[1] = s0;
              R("low support keeps people from work", low < .9 && war.Turnout(true) <= 1, $"turnout at support 30: {low * 100:0}%"); }
            { var q = ind.Works.First(w => w.Wolf && w.Making == Product.HE); var was = q.Making; double p0 = war.DefencePoints; ind.Order(q, Product.CivilDefence, Camp.Now); q.Retooled = -1e18; ind.Daily(Camp.Now, Fire, F);
              R("the works can make civil defence, and it gets more people into shelters", war.DefencePoints > p0 && FireSim.Shelter(true, war.Defence(true)) > FireSim.Shelter(true, .15),
                $"{q.City.Name}: {war.DefencePoints - p0:0} points in a day; shelters {FireSim.Shelter(true, .15) * 100:0}% to {FireSim.Shelter(true, war.Defence(true)) * 100:0}%"); ind.Order(q, was, Camp.Now); q.Retooled = -1e18; }
            { var w2 = new War(); w2.Support[0] = 10; w2.Baseline[0] = 10; for (int k = 0; k < 4; k++) w2.Weekly(World, Fire, ind, new Random(1));
              R("four weeks broken and a side asks for terms", w2.Loser == 0, $"UCS support {w2.Support[0]:0}, weeks broken {w2.WeeksBroken[0]}"); }
        }
        // a save, and reading it back
        {
            RunCampaign(0); while (OpBusy) RunCampaign(600); if (!opKept) EndOp();
            string savePath = System.IO.Path.Combine(SaveGame.Dir, "test.fbsave"); var sw2 = System.Diagnostics.Stopwatch.StartNew();
            R("a quiet moment can be saved", CanSave, "");
            SaveGame.Write(savePath, World, Camp, Fire, Air.Crews, Forces!); double wms = sw2.ElapsedMilliseconds; sw2.Restart();
            var st = SaveGame.Read(savePath, World); double rms = sw2.ElapsedMilliseconds; long kb = new System.IO.FileInfo(savePath).Length / 1024;
            R("it is small and quick", kb < 2048 && wms < 2000 && rms < 15000, $"{kb} KB, written in {wms:0} ms, read in {rms:0} ms ({Fire.Cities.Count} cities materialised)");
            R("the clock, queue and diary come back", Math.Abs(st.Camp.Now - Camp.Now) < 1e-6 && st.Camp.Log.Count == Camp.Log.Count && st.Camp.Pending.Count() == Camp.Pending.Count() && st.Camp.Seed == Camp.Seed, st.Camp.DateText);
            bool same = Fire.Cities.All(c => { var d2 = st.Fire.Cities.FirstOrDefault(q => q.P == c.P); if (d2 == null) return false; for (int m = 0; m < 3; m++) for (int s2 = 0; s2 < 4; s2++) { int a1 = c.Count[m, s2], b1 = d2.Count[m, s2]; if (s2 == 1) continue; if (s2 == 2) { a1 += c.Count[m, 1]; } if (a1 != b1) return false; } return true; });
            R("every city's damage comes back, building by building", same, string.Join(", ", st.Fire.Cities.Select(c => $"{c.P.Name} {c.Count[0, 2] + c.Count[1, 2] + c.Count[2, 2] + c.Count[0, 3] + c.Count[1, 3] + c.Count[2, 3]}")));
            R("and every city's record", Fire.Records.All(kv => st.Fire.Records.TryGetValue(kv.Key, out var r2) && Math.Abs(r2.Dead - kv.Value.Dead) < 1e-6 && Math.Abs(r2.Homeless - kv.Value.Homeless) < 1e-6 && r2.Raids == kv.Value.Raids && r2.Lost == kv.Value.Lost), "");
            R("and the aircraft at every field", F.Bases.All(b => { var b2 = st.Forces.Named(b.Field.Name)!; return b.Of.All(kv => b2.S(kv.Key).Ready == kv.Value.Ready && b2.S(kv.Key).ComingN == kv.Value.ComingN); }), $"{F.Side(true).Sum(b => b.Total)} Vulfen, {F.Side(false).Sum(b => b.Total)} UCS aircraft");
            R("and the works, dumps, crews and schools", ind.Works.All(q => { var q2 = st.Forces.Ind.Of(q.City)!; return q2.Making == q.Making && q2.Retooled == q.Retooled && Math.Abs(q2.Progress - q.Progress) < 1e-9; })
                && Enumerable.Range(0, 2).All(z => st.Forces.Ind.He[z] == ind.He[z] && st.Forces.Ind.Inc[z] == ind.Inc[z] && st.Forces.Ind.Fuel[z] == ind.Fuel[z] && st.Forces.Ind.BomberCrews[z] == ind.BomberCrews[z] && st.Forces.Ind.Pilots[z] == ind.Pilots[z])
                && st.Forces.Ind.Schools.Count == ind.Schools.Count && st.Forces.Ind.BomberIntake == ind.BomberIntake, $"{ind.Schools.Count} classes at school, {ind.BomberCrews[1]} bomber crews");
            R("and the planner", st.Forces.Planner.Doctrine == F.Planner.Doctrine && st.Forces.Planner.EscortsPerBox == F.Planner.EscortsPerBox && st.Forces.Planner.Raids == F.Planner.Raids && F.Planner.Flak.All(kv => Math.Abs(st.Forces.Planner.FlakOf(World.City(kv.Key)) - kv.Value) < 1e-9)
                && st.Camp.Pending.Count(e => e.Works) == Camp.Pending.Count(e => e.Works), $"{F.Planner.Doctrine}, escorts {F.Planner.EscortsPerBox}");
            R("and the history behind the graphs", st.Forces.Hist.Side[1].Count == F.Hist.Side[1].Count && st.Forces.Hist.City.Count == F.Hist.City.Count && st.Forces.Hist.Side[1][^1].He == F.Hist.Side[1][^1].He, $"{F.Hist.Side[1].Count} days, {F.Hist.City.Sum(kv => kv.Value.Count)} city samples");
            F.Bases.First(b => b.Wolf).StandDown = true; F.Bases.First(b => b.Wolf).Patrols.Add(new Patrol { Kind = Craft.F5, N = 8, X = 1, Y = 2, Hours = 1 }); SaveGame.Write(savePath, World, Camp, Fire, Air.Crews, Forces!); var st2 = SaveGame.Read(savePath, World); var b2 = st2.Forces.Bases.First(b => b.Wolf);
            R("and the fields' orders, patrols and ferries", b2.StandDown && b2.Patrols.Count == F.Bases.First(b => b.Wolf).Patrols.Count && b2.Patrols[^1].Hours == 1 && st2.Forces.Flights.Count == F.Flights.Count, $"{st2.Forces.Bases.Sum(b => b.Patrols.Count)} patrols, {st2.Forces.Flights.Count} ferries");
            F.Bases.First(b => b.Wolf).StandDown = false; F.Bases.First(b => b.Wolf).Patrols.Clear();
            { var sx = F.Sites.All[0]; sx.Parts[0].Down = true; sx.Parts[0].BackAt = Camp.Now + 86400; SaveGame.Write(savePath, World, Camp, Fire, Air.Crews, Forces!); var st3 = SaveGame.Read(savePath, World); R("and what is broken at the sites", st3.Forces.Sites.All[0].Parts[0].Down && st3.Forces.Sites.All.Sum(q => q.Lost) == F.Sites.All.Sum(q => q.Lost), $"{F.Sites.All.Sum(q => q.Lost)} parts down"); sx.Parts[0].Down = false; }
            R("and the war support", Enumerable.Range(0, 2).All(z => st.Forces.War.Support[z] == F.War.Support[z] && st.Forces.War.DeadSeen[z] == F.War.DeadSeen[z]) && st.Forces.War.DefencePoints == F.War.DefencePoints, $"Vulfen {F.War.Support[1]:0}, UCS {F.War.Support[0]:0}");
            R("and the crews", st.Crews.WolfPilots.Count == Air.Crews.WolfPilots.Count && st.Crews.UcsCaptains.Count == Air.Crews.UcsCaptains.Count && st.Crews.Raid == Air.Crews.Raid, $"{st.Crews.WolfPilots.Count} Hund pilots, {st.Crews.WolfCaptains.Count} captains, raid {st.Crews.Raid}");
            System.IO.File.Delete(savePath);
        }

        o.AppendLine(); o.AppendLine("War diary:"); foreach (var l in Camp.Log) o.AppendLine($"  {Camp.Stamp(l.T),-18} {(l.Alarm ? "!" : " ")} {l.Text}");
        o.Insert(0, $"{(fails == 0 ? "ALL PASS" : $"{fails} FAILED")}\n");
        System.IO.File.WriteAllText(path, o.ToString()); GetTree().Quit();
    }
}
