using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Firebomber;

/// <summary>The camera and the input. The world, the terrain, the labels and the console are each their own thing; this only connects them.</summary>
public partial class Main : Node2D
{
    public World World = null!;
    public Clock Clock = new();
    public Air Air = null!;
    public AirView View = null!;
    public FireSim Fire = null!;
    public FighterSim Sim = null!; public FighterView FView = null!; public bool FollowPilot;
    FireView fireView = null!;
    public ConsoleUi Ui = null!;
    public Place? Selected; public Place Target = null!;
    /// <summary>Before the raid: the clock waits while the loads, aims and routes are settled. Nothing flies until SendRaid.</summary>
    public bool Planning;
    readonly System.Collections.Generic.Dictionary<string, (Loadout? l, int arrive)> lastLoads = new();
    public void SendRaid() { if (!Planning || ShortOf() != null) return; Planning = false; Air.Launch(); CommitOp(); Clock.SetSpeed(Clock.SpeedIdx); }
    /// <summary>What our raid as planned will take from the dumps, tonnes: blast bombs, fire bombs, and fuel for the bombers and their escorts.</summary>
    public (double he, double inc, double fuel) RaidNeeds()
    {
        double he = 0, inc = 0, fuel = 0;
        foreach (var f in Air.Formations.Where(q => q.Wolf)) { he += f.Plan.Where(b => b.He).Sum(b => b.Kg) * f.Ac.Count / 1000.0; inc += f.Plan.Where(b => !b.He).Sum(b => b.Kg) * f.Ac.Count / 1000.0; fuel += Industry.FuelFor(f.Kind) * f.Ac.Count; }
        fuel += Sim.All.Count(q => q.Wolf && q.Charge != null) * Industry.FuelFor(Craft.F5);
        return (he, inc, fuel);
    }
    /// <summary>Why the raid as planned cannot go, or null if the dumps hold enough. Only in the campaign.</summary>
    public string? ShortOf()
    {
        if (Forces == null) return null; var ind = Forces.Ind; var (he, inc, fuel) = RaidNeeds();
        if (he > ind.He[1] + .5) return $"Not enough HE: the loads need {he:N0} t, the dumps hold {ind.He[1]:N0} t.";
        if (inc > ind.Inc[1] + .5) return $"Not enough incendiaries: the loads need {inc:N0} t, the dumps hold {ind.Inc[1]:N0} t.";
        if (fuel > ind.Fuel[1] + .5) return $"Not enough fuel: the raid needs {fuel:N0} t, the tanks hold {ind.Fuel[1]:N0} t.";
        return null;
    }
    /// <summary>The opening screen: nothing exists yet but what the player is asking for.</summary>
    public bool Setup; public SandboxSetup? Sortie; SandboxSetup? flying;
    public bool Holding => Planning || Setup;
    /// <summary>The war's running clock, when playing the campaign; null in the sandbox and the tests.</summary>
    public Campaign? Camp;
    /// <summary>Every aircraft both sides hold, field by field (campaign only).</summary>
    public Forces? Forces;
    bool opCommitted; readonly System.Collections.Generic.List<(Base b, Craft k, int n)> committed = new();
    static double D2(Place a, Place b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
    /// <summary>What a UCS raid is made of: whole boxes from the bomber fields nearest the target that can reach it, the UCS keeping two-fifths of each field back.</summary>
    System.Collections.Generic.List<(Place field, Craft kind, int n)> UcsBoxes(CampaignEvent e, System.Collections.Generic.Dictionary<(Base, Craft), int> taken)
    {
        var list = new System.Collections.Generic.List<(Place, Craft, int)>(); int want = e.Waves * 3, crews = Forces!.Ind.BomberCrews[0];      // only as many bombers as there are crews for
        double bombsLeft = Forces!.Ind.He[0] + Forces.Ind.Inc[0];      // it can only load what its depots hold
        foreach (var b in Forces!.Side(false).Where(q => !Forces.Sites.Closed(q.Field)).OrderBy(q => D2(q.Field, e.Target!)))
            foreach (var k in new[] { Craft.TB6, Craft.SB3 })
            {
                var ds = Design.Of[k]; if (Weight.RangeKm(ds, ds.UsualKg) < 2.2 * D2(b.Field, e.Target!) / 1000) continue;
                int have = (int)(b.S(k).Ready * .6) - taken.GetValueOrDefault((b, k));
                while (have >= ds.Box / 2 && crews >= ds.Box / 2 && list.Count < want && bombsLeft >= ds.UsualKg / 1000 * ds.Box / 2) { int n = Math.Min(Math.Min(ds.Box, have), crews); bombsLeft -= ds.UsualKg / 1000 * n; list.Add((b.Field, k, n)); have -= n; crews -= n; taken[(b, k)] = taken.GetValueOrDefault((b, k)) + n; }
            }
        return list;
    }
    /// <summary>The fighters in this operation, from what each field holds: escorts first, from each box's own field; then everything still ready at every field, on alert.</summary>
    System.Collections.Generic.List<Garrison> GarrisonFor(double opT = double.NaN)
    {
        double opHour = Campaign.HourAt(double.IsNaN(opT) ? Camp!.Now : opT);
        var g = new System.Collections.Generic.List<Garrison>(); var used = new System.Collections.Generic.Dictionary<(Base, Craft), int>();
        int[] pilots = { Forces!.Ind.Pilots[0], Forces.Ind.Pilots[1] };      // no more fighters fly than there are pilots for
        int Take(Base b, Craft k, int n) { if (Forces.Sites.Closed(b.Field)) return 0; int s = b.Wolf ? 1 : 0, have = Math.Min(b.S(k).Ready - used.GetValueOrDefault((b, k)), pilots[s]), t = Math.Clamp(n, 0, Math.Max(0, have)); used[(b, k)] = used.GetValueOrDefault((b, k)) + t; pilots[s] -= t; return t; }
        foreach (var fm in Air.Formations)
        {
            var b = Forces!.Named(fm.HomeName); if (b == null) continue;
            if (fm.Wolf) { int n = Take(b, Craft.F5, flying?.Escorts ?? 0); if (n > 0) g.Add(new Garrison(b.Field, Craft.F5, n, true, fm)); }
            else { int esc = Forces.Planner.EscortsPerBox, n7 = Take(b, Craft.I7, Math.Min(esc, 3)), n8 = Take(b, Craft.I8, esc - n7); if (n7 > 0) g.Add(new Garrison(b.Field, Craft.I7, n7, false, fm)); if (n8 > 0) g.Add(new Garrison(b.Field, Craft.I8, n8, false, fm)); }      // the planner's escort for each box, heavies first
        }
        // patrols first: those whose shift this is, if there is fuel for them
        foreach (var b in Forces!.Bases)
            foreach (var p in b.Patrols.Where(q => q.Up(opHour) && (!b.Wolf || Forces.Ind.Fuel[1] > 0))) { int n = Take(b, p.Kind, p.N); if (n > 0) g.Add(new Garrison(b.Field, p.Kind, n, b.Wolf, null, p, b.Intercept)); }
        // the rest on alert, every field cut by the same share when pilots are short
        double[] share = new double[2];
        for (int s = 0; s < 2; s++) { int left = Forces!.Side(s == 1).Sum(b => (s == 1 ? new[] { Craft.F5 } : new[] { Craft.I7, Craft.I8 }).Sum(k => b.S(k).Ready - used.GetValueOrDefault((b, k)))); share[s] = left <= 0 ? 0 : Math.Min(1, pilots[s] / (double)left); }
        foreach (var b in Forces!.Bases.Where(q => !q.StandDown))
            foreach (var k in b.Wolf ? new[] { Craft.F5 } : new[] { Craft.I7, Craft.I8 }) { int n = Take(b, k, (int)Math.Ceiling((b.S(k).Ready - used.GetValueOrDefault((b, k))) * share[b.Wolf ? 1 : 0])); if (n > 0) g.Add(new Garrison(b.Field, k, n, b.Wolf, null, null, b.Intercept)); }
        return g;
    }
    /// <summary>The raid goes: its bombers and escorts are taken off their fields' ready stock.</summary>
    void CommitOp()
    {
        if (Forces == null || opCommitted) return; opCommitted = true; committed.Clear();
        foreach (var f in Air.Formations) { var b = Forces.Named(f.HomeName); if (b == null || f.Ac.Count == 0) continue; int n = Math.Min(f.Ac.Count, b.S(f.Kind).Ready); b.S(f.Kind).Ready -= n; committed.Add((b, f.Kind, n)); if (f.Wolf) Forces.Planner.PlayerFrom[b.Town.Name] = Forces.Planner.PlayerFrom.GetValueOrDefault(b.Town.Name) + n; }
        foreach (var f in Sim.All.Where(q => q.Charge != null)) { var b = Forces.Named(f.HomeName); if (b == null || b.S(f.Kind).Ready <= 0) continue; b.S(f.Kind).Ready--; committed.Add((b, f.Kind, 1)); }
        // the bombs and the fuel leave the dumps with them
        var (he, inc, fuel) = RaidNeeds(); var ind = Forces.Ind; ind.He[1] = Math.Max(0, ind.He[1] - he); ind.Inc[1] = Math.Max(0, ind.Inc[1] - inc); ind.Fuel[1] = Math.Max(0, ind.Fuel[1] - fuel);
        foreach (var f in Air.Formations.Where(q => !q.Wolf && q.Ac.Count > 0)) { double t = f.Plan.Sum(b => b.Kg) * f.Ac.Count / 1000.0; double hs = Math.Min(ind.He[0], t * .6); ind.He[0] -= hs; ind.Inc[0] = Math.Max(0, ind.Inc[0] - (t - hs)); ind.Fuel[0] = Math.Max(0, ind.Fuel[0] - Industry.FuelFor(f.Kind) * f.Ac.Count); }
    }
    /// <summary>After an operation: every aircraft that came through goes into servicing or repair at the field it came down at; the rest are written off. The diary has our side's account.</summary>
    void Settle()
    {
        if (Forces == null) return; double now = Camp!.Now; var lost = new System.Collections.Generic.Dictionary<(bool wolf, Craft k), int>(); var shop = new System.Collections.Generic.Dictionary<Craft, int>();
        void Back(string field, Craft k, double dmg, bool wolf) { var b = Forces.Named(field) ?? Forces.Side(wolf).OrderBy(q => q.Total).First(); b.S(k).Coming.Add((now + Forces.Out(dmg), 1)); if (wolf && dmg > .08) shop[k] = shop.GetValueOrDefault(k) + 1; }
        if (opCommitted)
        {
            var home = Air.Formations.SelectMany(f => f.Ac).Concat(Air.BackHome).ToList();
            foreach (var a in home) Back(a.HomeName, a.F.Kind, a.Damage, a.F.Wolf);
            foreach (var grp in committed.Where(c => !Design.Of[c.k].IsFighter).GroupBy(c => (c.b.Wolf, c.k)))
            { int n = grp.Sum(c => c.n) - home.Count(a => a.F.Wolf == grp.Key.Wolf && a.F.Kind == grp.Key.k); if (n > 0) lost[grp.Key] = n; }
        }
        foreach (var f in Sim.All)
        {
            var b = Forces.Named(f.HomeName); if (b == null) continue; bool escort = f.Charge != null && opCommitted;
            if (!f.Alive) { if (!escort) b.S(f.Kind).Ready = Math.Max(0, b.S(f.Kind).Ready - 1); lost[(f.Wolf, f.Kind)] = lost.GetValueOrDefault((f.Wolf, f.Kind)) + 1; continue; }
            if (escort) Back(f.HomeName, f.Kind, f.Damage, f.Wolf);
            else if (f.Act != Act.Ground) { b.S(f.Kind).Ready = Math.Max(0, b.S(f.Kind).Ready - 1); Back(f.HomeName, f.Kind, f.Damage, f.Wolf); if (f.Wolf) Forces.Ind.Fuel[1] = Math.Max(0, Forces.Ind.Fuel[1] - Industry.FuelFor(f.Kind)); }
        }
        // crews: for each aircraft lost, a crew lost, less the share of the people in this operation who got back to their own side
        var cr = Air.Crews; int crewsLost = 0;
        foreach (var ((wolf, k), n) in lost)
        {
            int s = wolf ? 1 : 0, people = cr.Dead[s] + cr.Recovered[s] + cr.Prisoners[s] + cr.LostAtSea[s]; double back = people > 0 ? cr.Recovered[s] / (double)people : 0;
            int gone = Math.Min(n, (int)Math.Round(n * (1 - back)));
            if (Design.Of[k].IsFighter) Forces.Ind.Pilots[s] = Math.Max(0, Forces.Ind.Pilots[s] - gone); else { Forces.Ind.BomberCrews[s] = Math.Max(0, Forces.Ind.BomberCrews[s] - gone); Forces.War.BombersDowned[s] += n; }
            Forces.War.CrewsLost[s] += gone;
            if (wolf) crewsLost += gone;
        }
        if (opCommitted && opUcs.Count > 0)
        {
            int sentU = committed.Where(c => !c.b.Wolf && !Design.Of[c.k].IsFighter).Sum(c => c.n), lostU = lost.Where(kv => !kv.Key.wolf && !Design.Of[kv.Key.k].IsFighter).Sum(kv => kv.Value);
            double h = Campaign.HourAt(opUcs[0].T); Forces.Planner.Report(Camp, opUcs[0].Target, h < 7 || h >= 19, sentU, lostU, warRng);
        }
        string Types(System.Collections.Generic.IEnumerable<(Craft k, int n)> g) => string.Join(" // ", g.Where(q => q.n > 0).OrderBy(q => q.k).Select(q => $"{q.n} {Sprites.Specs[q.k].Name.Split(' ')[0]}"));
        string off = Types(lost.Where(kv => kv.Key.wolf).Select(kv => (kv.Key.k, kv.Value))), fix = Types(shop.Select(kv => (kv.Key, kv.Value))), att = "";
        int sentW = committed.Where(c => c.b.Wolf && !Design.Of[c.k].IsFighter).Sum(c => c.n);
        if (opCommitted && sentW > 0)
        {
            double rate = lost.Where(kv => kv.Key.wolf && !Design.Of[kv.Key.k].IsFighter).Sum(kv => kv.Value) / (double)sentW, was = Forces.War.LastAttrition; Forces.War.LastAttrition = rate;
            att = $"Attrition: {rate * 100:0}%" + (was < 0 ? "" : Math.Abs(rate - was) < .005 ? ", as last time" : $", {(rate > was ? "up" : "down")} {Math.Abs(rate - was) * 100:0}% from last");
        }
        var note = new System.Collections.Generic.List<string>(); if (off != "") note.Add("Written off: " + off); if (fix != "") note.Add("Repairs: " + fix); if (att != "") note.Add(att); if (crewsLost > 0) note.Add($"{crewsLost} crew{(crewsLost > 1 ? "s" : "")} lost");
        if (note.Count > 0) Camp.Note(string.Join("\n", note));
        foreach (var (t, alarm) in Forces.Sites.Account(Camp.Now)) Camp.Note(t, alarm);
        opCommitted = false; committed.Clear();
    }
    /// <summary>Whether this operation's crews are back on the roster; whether its report has been read.</summary>
    bool opKept = true; public bool ReportDone; bool offTold, radarTold, overTold; int hitsSeen;
    readonly System.Collections.Generic.HashSet<Place> bombedTold = new(), burningNow = new();
    System.Collections.Generic.List<CampaignEvent> opUcs = new();
    /// <summary>Nothing in this operation is still to fly: no aircraft out, none waiting to go.</summary>
    public bool OpOver => Air.Formations.Count == 0 || RaidOver;
    /// <summary>Something is flying, about to fly, or burning: the clock must go at operational speed.</summary>
    public bool OpBusy => !Air.Quiet || Fire.Raging;
    public void StartSetup()
    {
        if (Camp != null) EndOp();
        Setup = true; Planning = false; Clock.Paused = true; View.Chosen.Clear(); Air.Watch = null; Sim.Watch = null;
        if (Sortie == null && Forces != null)
        {
            Sortie = new SandboxSetup { Target = World.Cities.First(c => c.Name == "Kolvograd") };
            for (int i = 0; i < World.Fields.Count; i++) foreach (var k in new[] { Craft.V16, Craft.U4 }) { if (Forces.Sites.Closed(World.Fields[i])) continue; int n = Forces.Of(World.Fields[i]).S(k).Ready; if (n >= 6) Sortie.Flights.Add(new SandboxFlight { Kind = k, N = Math.Min(k == Craft.V16 ? 24 : 12, n / 6 * 6), Field = i }); }
        }
        if (Sortie == null)
        {
            Sortie = new SandboxSetup { Target = World.Cities.First(c => c.Name == "Kolvograd") };
            for (int i = 0; i < 4; i++) Sortie.Flights.Add(new SandboxFlight { Kind = Craft.V16, N = 36, Field = i % World.Fields.Count });
            for (int i = 0; i < 2; i++) Sortie.Flights.Add(new SandboxFlight { Kind = Craft.U4, N = 12, Field = (i + 1) % World.Fields.Count });
        }
        Selected = Sortie.Target;
    }
    /// <summary>Raid orders for one flight from one field, at a chosen city; the time, escort and load are still the player's to set.</summary>
    public void RaidFrom(Base b, Craft k, int n, Place target)
    {
        StartSetup(); var s = Sortie!; s.Flights.Clear(); s.Flights.Add(new SandboxFlight { Kind = k, N = n, Field = World.Fields.IndexOf(b.Field) }); s.Target = target; Selected = target;
    }
    /// <summary>Raid orders at one site: the usual flights, aimed at it.</summary>
    public void RaidAtSite(Site site) { Sortie = null; StartSetup(); var s = Sortie!; s.Target = site.Town; s.AimKind = 5; s.AimSite = (site.X, site.Y); Selected = site.Town; }
    public void PickTarget(Place c) { if (Sortie != null && !c.Wolf) { Sortie.Target = c; Selected = c; } }
    /// <summary>From what was asked for to aircraft on the ground, with the clock still stopped.</summary>
    public void FinishSetup()
    {
        if (Sortie == null) return;
        if (Camp != null)
        {
            Setup = false; Selected = null; flying = Sortie; Target = Sortie.Target; raidSeed = Random.Shared.Next(100000);
            { int crews = Forces!.Ind.BomberCrews[1]; foreach (var f in Sortie.Flights) { f.N = Math.Min(f.N, crews); crews -= f.N; } }      // no more bombers than crews to fly them
            Build(Camp.Now, Camp.Hour, Air.Crews, NextHour(Sortie.Hour)); View.Following = null; FollowPilot = false; StartPlan(); return;
        }
        Setup = false; Selected = null; flying = Sortie; Target = Sortie.Target; raidSeed = Random.Shared.Next(100000);
        Build(Air.T + (Air.T > Air.StartT ? 600 : 0), Sortie.Hour, Air.Crews); View.Following = null; FollowPilot = false; StartPlan();
    }
    public bool CanGoBack => flying != null;
    public void BackToSetup() { if (!Planning) return; KeepCrews(0); opKept = true; if (Camp != null) foreach (var e in opUcs) Camp.Schedule(e); opUcs.Clear(); StartSetup(); }
    /// <summary>Campaign only: the report is read; its crews go back on the roster.</summary>
    public void CloseReport() => EndOp();
    /// <summary>Campaign only: back to the map without ordering a raid.</summary>
    public void CancelSetup() { if (!Setup || Camp == null) return; Setup = false; Selected = null; }
    /// <summary>The next time the clock shows this hour, at least half an hour off.</summary>
    double NextHour(double h)
    {
        double day0 = Math.Floor(Camp!.Now / Campaign.Day) * Campaign.Day, t = day0 + (h - 6 + 24) % 24 * 3600;
        while (t < Camp.Now + 1800) t += Campaign.Day;
        return t;
    }
    /// <summary>An operation is finished with: its tallies are cleared and everyone who came through goes on the roster.</summary>
    void EndOp()
    {
        if (opKept) return; Settle(); var c = Air.Crews; if (Air.Formations.Count > 0) c.Raid++; c.Clear(); KeepCrews(1); opKept = true; ReportDone = true;
    }
    /// <summary>A UCS raid falls due. If another operation is under way the UCS waits a few hours; otherwise its bombers take off now.</summary>
    void OnDue(CampaignEvent e)
    {
        if (e.Kind == Ev.Day) { Fire.Repair(1); Forces?.Sites.Daily(Camp!.Now); Forces?.Ind.Daily(Camp!.Now, Fire, Forces); Forces?.Tick(Camp!.Now); Forces?.Hist.Sample(Camp!.Now, World, Forces, Fire); Forces?.UcsMoves(Camp!.Now, c => Forces.Planner.Alarm(c, Fire, Camp.Now)); autosave = true; return; }
        if (e.Kind == Ev.Week)
        {
            if (Forces == null) return; Camp!.Note(Forces.Ind.Weekly(Camp.Now));
            bool ended = Forces.War.Loser >= 0;
            foreach (var (t, alarm) in Forces.War.Weekly(World, Fire, Forces.Ind, warRng)) Camp.Note(t, alarm);
            Forces.Planner.Weekly(Camp.Now, Camp, World, Forces, Fire, Forces.War, warRng);
            { // what intelligence makes of the UCS bomber arm: its size to the nearest fifty, and its habits, usually right
              var pl = Forces.Planner; int bombers = Forces.All(false, Craft.TB6) + Forces.All(false, Craft.SB3);
              var seen = warRng.NextDouble() < .75 ? pl.Doctrine : (Doctrine)warRng.Next(4);
              Camp.Note($"Intelligence: about {Math.Round(bombers / 50.0) * 50:0} UCS bombers; " + seen switch { Doctrine.Burn => "they seem set on burning our cities by night.", Doctrine.Works => "they seem to be going for our works by day.", Doctrine.CounterAir => "they seem to be going for our airfields and radar.", _ => "they seem to be keeping up pressure with small raids." }); }
            if (!ended && Forces.War.Loser >= 0) Clock.Paused = true;      // the war is decided: stop for the player to see it
            return;
        }
        if (e.Kind != Ev.UcsRaid) return;
        if (Forces != null && Forces.Planner.Scrub(e, Camp!, Forces)) return;
        if (!OpOver || Holding) { e.T += 6 * 3600; Camp!.Schedule(e); return; }
        EndOp(); flying = null; Target = e.Target!; raidSeed = Random.Shared.Next(100000);
        Build(Camp!.Now, Camp.Hour, Air.Crews, double.NaN, false, e); CommitOp();
        View.Chosen.Clear(); View.Picked.Clear(); View.Following = null; FollowPilot = false;
    }
    /// <summary>Over quiet time to the next thing that matters: a tick or a take-off. Not while anything flies or burns.</summary>
    public void SkipAhead()
    {
        if (Camp == null || Holding || OpBusy) return;
        Camp.RunTo(Math.Min(Camp.NextKnown, Air.NextWake), () => OpBusy);
        if (Air.Quiet) Air.JumpTo(Camp.Now); Fire.SyncTo(Air.T);
    }
    /// <summary>One frame of campaign time. Quiet time is jumped; an operation is stepped; the clock stops at a take-off or at an event that starts one, so an operation is never run through at a whole hour a second.</summary>
    void CampaignStep(double dt)
    {
        if (dt <= 0) return;
        (Fire.WindX, Fire.WindY) = Weather.WindVec(Camp!.Seed, Camp.Now);
        double target = Camp!.Now + dt;
        if (!OpBusy) { target = Math.Min(target, Air.NextWake); if (Fire.AnyBurning) target = Math.Min(target, Camp.Now + 3600); }     // an hour at a time while anything smoulders, so the diary knows when it went out
        Camp.RunTo(target, () => OpBusy);
        if (Air.Quiet) Air.JumpTo(Camp.Now); else Air.Advance(Math.Max(0, Camp.Now - Air.T - Air.Frac));
        if (!Fire.Raging) Fire.SyncTo(Air.T);      // small fires are cheap: keep them level with the clock, so the diary says when they went out
        OpNotes();
        if (autosave && autosaving && CanSave) { autosave = false; Save(SaveGame.Auto, false); }
    }
    bool autosave, autosaving; readonly Random warRng = new();      // autosaving: only in real play, never in tests or screenshot runs, which would overwrite the player's autosave
    /// <summary>A count as a person would give it: to two figures.</summary>
    public static string Round(double n) { if (n < 100) return $"{Math.Round(n):N0}"; double p = Math.Pow(10, Math.Floor(Math.Log10(n)) - 1); return $"{Math.Round(n / p) * p:N0}"; }
    /// <summary>A save is only made at a quiet moment: nothing flying or about to, no fire raging, no orders open, the last raid's report read.</summary>
    public bool CanSave => Camp != null && !Holding && !OpBusy && OpOver && opKept;
    /// <summary>The path to load from when the scene comes back up: LOAD reloads the whole scene, so nothing of the old campaign lingers.</summary>
    public static string? PendingLoad;
    public void Save(string path, bool tell = true)
    {
        if (!CanSave) return;
        try { var sw = System.Diagnostics.Stopwatch.StartNew(); SaveGame.Write(path, World, Camp!, Fire, Air.Crews, Forces!); if (tell) Camp!.Note($"Saved ({new System.IO.FileInfo(path).Length / 1024} KB)."); GD.Print($"saved {path} in {sw.ElapsedMilliseconds} ms"); }
        catch (Exception ex) { Camp!.Note($"Could not save: {ex.Message}", true); }
    }
    public void SaveNow() => Save(SaveGame.Manual);
    public void LoadLatest() { var p = SaveGame.Latest; if (p == null || Holding) return; PendingLoad = p; GetTree().ReloadCurrentScene(); }
    /// <summary>What goes in the war diary as the operation runs, and when the clock stops for the player.</summary>
    void OpNotes()
    {
        var camp = Camp!;
        if (!offTold && Air.Formations.Any(f => f.Wolf && f.Phase != Phase.Waiting && !f.Aborted))
        { offTold = true; camp.Note($"Our raid on {Target.Name} takes off."); if (Clock.SpeedIdx > 2) Clock.SpeedIdx = 2; }
        if (!radarTold)
            foreach (var f in Air.Formations.Where(f => !f.Wolf && f.Phase == Phase.Outbound))
            {
                var near = World.Cities.Where(c => c.Wolf).OrderBy(c => Math.Abs(c.X - f.Lead.X) + Math.Abs(c.Y - f.Lead.Y)).First();
                double km = Math.Sqrt(Math.Pow(near.X - f.Lead.X, 2) + Math.Pow(near.Y - f.Lead.Y, 2)) / 1000; if (km > 300) continue;
                radarTold = true; int n = Air.Formations.Count(q => !q.Wolf && q.Phase is Phase.Outbound or Phase.Waiting);
                camp.Note($"RADAR: enemy bombers, about {n} formation{(n > 1 ? "s" : "")}, {km:0} km from {near.Name}.", true); Clock.Paused = true; if (Clock.SpeedIdx > 2) Clock.SpeedIdx = 2; break;
            }
        for (; hitsSeen < Air.Hits.Count; hitsSeen++)
        {
            var c = World.CityNear(Air.Hits[hitsSeen].X, Air.Hits[hitsSeen].Y);
            if (c != null && bombedTold.Add(c)) camp.Note(c.Wolf ? $"Bombs falling on {c.Name}." : $"Our bombs falling on {c.Name}.", c.Wolf);
        }
        foreach (var cf in Fire.Cities)
        {
            if (cf.Burning.Count > 0) burningNow.Add(cf.P);
            else if (burningNow.Remove(cf.P)) { var r = Fire.Rec(cf.P); camp.Note($"Fires out in {cf.P.Name}: {r.Lost - r.LostAtRaid:N0} buildings lost, about {Round(r.Dead - r.DeadAtRaid)} dead, {Round(r.Homeless - r.HomelessAtRaid)} homeless.", cf.P.Wolf); }
        }
        if (!overTold && Air.Formations.Count > 0 && RaidOver) { overTold = true; camp.Note("All aircraft are down. The report is ready."); Clock.Paused = true; }
    }
    /// <summary>A new mission after a raid: the same crews, somewhere else.</summary>
    public void NewMission() { Air.Crews.Raid++; Air.Crews.Clear(); KeepCrews(1); StartSetup(); }
    void StartPlan()
    {
        Planning = true; Clock.Paused = true;
        foreach (var f in Air.Formations.Where(f => f.Wolf))
        {
            var was = lastLoads.TryGetValue(f.Name, out var l) ? l : default; var list = Loadouts.Of[f.Kind]; var d = Design.Of[f.Kind]; double trip = Air.TripKm(f);
            bool keep = was.l != null && list.Contains(was.l);
            // the usual load if it reaches, otherwise the first that does, otherwise the one that gets furthest
            var fits = list.FirstOrDefault(q => Weight.RangeKm(d, q.Kg) >= trip * 1.05) ?? list.OrderBy(q => q.Kg).First();
            Air.SetPreset(f, keep ? was.l! : fits); f.ArriveMin = keep ? was.arrive : 0;
        }
        var own = Air.Formations.FirstOrDefault(f => f.Wolf); if (own != null && View.Chosen.Count == 0) View.ChooseOnly(own);
    }
    TerrainLayer terrain = null!; ChartLabels labels = null!; Hangar hangar = null!;
    double zoom, camX = World.WidthM / 2, camY = World.HeightM / 2;
    bool drag, moved; Vector2 pressAt;
    bool gliding; double glideX, glideY, glideZ;
    int cityIdx = -1, idleFrames; string? shot; bool waitBoom;
    public double Zoom => zoom;

    public override void _Ready()
    {
        TextureFilter = TextureFilterEnum.NearestWithMipmaps;
        Sprites.Build();
        var startSw = System.Diagnostics.Stopwatch.StartNew(); World = new World(); GD.Print($"world and cities built in {startSw.ElapsedMilliseconds} ms");
        foreach (var c in World.Cities) if (c.Layout != null) GD.Print($"{c.Name,-10} like {c.Style!.Like,-26} {c.Layout.Plots.Length,7} buildings  wood {c.Layout.Plots.Count(q => q.Mat == 0),6}  brick {c.Layout.Plots.Count(q => q.Mat == 1),6}  works {c.Layout.Plots.Count(q => q.Mat == 2),5}");
        terrain = new TerrainLayer(World);
        zoom = MinZoom();
        Fire = new FireSim(World);
        {
            var ra0 = OS.GetCmdlineUserArgs(); int ti = Array.IndexOf(ra0, "--target"); Target = World.City(ti >= 0 ? ra0[ti + 1] : "Kolvograd"); int hi = Array.IndexOf(ra0, "--hour");
            string? load = PendingLoad ?? (ra0.Contains("--continue") ? SaveGame.Latest : null); PendingLoad = null;
            SaveGame.State? st = null;
            if (load != null) { try { st = SaveGame.Read(load, World); } catch (Exception ex) { GD.PrintErr($"could not load {load}: {ex.Message}"); } }
            autosaving = ra0.Length == 0 || ra0.Contains("--continue") || load != null;
            if (st != null) { Camp = st.Camp; Camp.Due = OnDue; Fire = st.Fire; Forces = st.Forces; Camp.Note($"Campaign loaded ({System.IO.Path.GetFileNameWithoutExtension(load)})."); Build(Camp.Now, Camp.Hour, st.Crews, double.NaN, false); EndOp(); Clock.SpeedIdx = 5; }
            else if (ra0.Length == 0 || ra0.Contains("--campaign") || ra0.Contains("--campaigntest") || load != null) { Camp = new Campaign(World, ra0.Length > 0 ? 5 : Random.Shared.Next(100000)) { Due = OnDue }; Forces = Forces.Start(World); Build(0, Campaign.HourAt(0), null, double.NaN, false); EndOp(); Clock.SpeedIdx = 5; }
            else Build(0, hi >= 0 ? double.Parse(ra0[hi + 1], System.Globalization.CultureInfo.InvariantCulture) : 8);
        }
        Fire.Hour = () => Air.Hour; Fire.Defence = wolf => Forces?.War.Defence(wolf) ?? .15; Fire.RepairBoost = c => Forces?.Planner.RepairBoost(c, Fire) ?? 1;
        if (Camp != null) Fire.Climate = c => (Weather.Dryness(Camp.Seed, c.X, c.Y, Camp.Now), Weather.WindAt(Camp.Seed, Camp.Now, c.X, c.Y).speed, Weather.At(Camp.Seed, c.X, c.Y, Camp.Now).rain);
        if (Camp != null && Forces != null)
        {
            Camp.PlanDaily = () => { Forces.Planner.Daily(Camp.Now, Camp, World, Forces, Fire, new Random(Camp.Seed * 31 + Camp.DayNo)); if (Forces.Planner.NewWarning) { Forces.Planner.NewWarning = false; Clock.Paused = true; } };
            if (Forces.Hist.Empty) Forces.Hist.Sample(Camp.Now, World, Forces, Fire);
        }
        night = new CanvasModulate(); AddChild(night);
        fireView = new FireView { M = this }; AddChild(fireView);
        AddChild(new WeatherView { M = this });
        View = new AirView { M = this };
        FView = new FighterView { M = this }; labels = new ChartLabels { M = this }; AddChild(View); AddChild(FView);
        var over = new CanvasLayer { Layer = 1 }; AddChild(over); over.AddChild(new Glow { M = this }); over.AddChild(labels);   // above the darkness of night; names stay readable over aircraft and bomb marks
        var layer = new CanvasLayer { Layer = 2 }; AddChild(layer);
        Ui = new ConsoleUi { M = this }; layer.AddChild(Ui);
        hangar = new Hangar(); layer.AddChild(hangar);
        var ci = System.Globalization.CultureInfo.InvariantCulture; var a = OS.GetCmdlineUserArgs();
        for (int i = 0; i < a.Length; i++)
        {
            string v = i + 1 < a.Length ? a[i + 1] : "";
            switch (a[i])
            {
                case "--shot": shot = v; labels.Snap = true; break;
                case "--zoom": zoom = double.Parse(v, ci); break;
                case "--center": { var s = v.Split(','); camX = double.Parse(s[0], ci) * 1000; camY = double.Parse(s[1], ci) * 1000; break; }
                case "--city": { var c = World.City(v); camX = c.X; camY = c.Y; break; }
                case "--select": Selected = World.City(v); break;
                case "--hangar": CallDeferred(nameof(ShowHangar)); break;
                case "--damage": { var r = new Random(4); foreach (var q in Air.Formations.SelectMany(f => f.Ac)) { if (r.NextDouble() < .3) { q.H.Struct *= .4; q.H.Burning = r.NextDouble() < .5; q.H.Eng[0] = 0; } } break; }
                case "--load": foreach (var f in Air.Formations.Where(f => f.Wolf)) Air.SetLoad(f, v == "he" ? 30 : v == "inc" ? 0 : v == "fire" ? 5 : 15); break;
                case "--aimat": { var kv = Target; var q = v == "works" ? CityPlan.Works(kv) : (kv.X, kv.Y); int n = 0; foreach (var f in Air.Formations.Where(f => f.Wolf)) Air.ReAim(f, q.Item1 + (n++ % 3 - 1) * 300, q.Item2); break; }
                case "--cities": Ui.Collapsed = false; break;
                case "--bases": Ui.ShowBases = true; break;
                case "--mouse": { var q = v.Split(','); shotMouse = new Vector2(float.Parse(q[0], ci), float.Parse(q[1], ci)); break; }
                case "--field": Ui.OpenField(Forces!.Bases.First(q => q.Town.Name == v)); break;
                case "--fieldload": { var q = v.Split(':'); Ui.PickLoad(Enum.Parse<Craft>(q[0]), int.Parse(q[1])); break; }
                case "--diary": Ui.DiaryMode = int.Parse(v); break;
                case "--support": Ui.ShowSupport = true; break;
                case "--aircraft": Ui.ShowAircraft = true; break;
                case "--patrol": { var q = v.Split(':'); var b0 = Forces!.Bases.First(z => z.Town.Name == q[0]); b0.Patrols.Add(new Patrol { Kind = Craft.F5, N = 12, X = b0.Field.X + double.Parse(q[1], ci) * 1000, Y = b0.Field.Y + double.Parse(q[2], ci) * 1000, Hours = 2 }); break; }
                case "--ferry": { var q = v.Split(':'); Forces!.Ferry(Forces.Bases.First(z => z.Town.Name == q[0]), Forces.Bases.First(z => z.Town.Name == q[1]), Craft.F5, 6, Camp!.Now - 1800); break; }
                case "--fieldcam": { var q = Forces!.Bases.First(z => z.Town.Name == v).Field; camX = q.X; camY = q.Y; break; }
                case "--site": Ui.OpenSite(Forces!.Sites.Named(v.Replace('_', ' '))!); break;
                case "--bombsite": { var q = v.Replace('_', ' '); var st0 = Forces!.Sites.Named(q)!; var rg = new Random(3); foreach (var p in st0.Parts) { for (int n = 0; n < 2; n++) Forces.Sites.Hit(new Impact { X = p.X, Y = p.Y, Kg = 250 }, Camp!.Now, Forces, rg); } break; }
                case "--warn": { var pl = Forces!.Planner; pl.WarnTarget = v; pl.WarnUntil = Camp!.Now + Campaign.Day; pl.WarnN = 220; break; }
                case "--industry": { var q = v.Split(':'); Ui.OpenWorks(World.City(q[0])); if (q.Length > 1) Ui.PickProduct(Enum.Parse<Product>(q[1])); break; }      // shots: the industry panel on one works, with a change chosen
                case "--ucsbases": if (Forces != null) Ui.OpenBase(Forces.Side(false).OrderByDescending(b => b.Town.Y).First()); break;
                case "--dumpsave": CallDeferred(nameof(StartDump), v); break;
                case "--run": { double t = double.Parse(v, ci); for (double u = 0; u < t; u += 20) { Air.Advance(20); Fire.SyncTo(Air.T); } Air.Recent.Clear(); fireView.CallDeferred(nameof(FireView.Prewarm)); Clock.Skip(t); break; }
                case "--watchbomber": { var b = Air.Formations.Where(q => q.Wolf).SelectMany(q => q.Ac).OrderByDescending(q => q.Damage + (q.Straggler ? 1 : 0)).FirstOrDefault(); if (b != null) { Air.Watch = b; camX = b.X; camY = b.Y; } break; }
                case "--choose": View.ChooseOnly(Air.Formations.First(f => f.Name == v)); break;
                case "--follow": { var f = Air.Formations.First(q => q.Name == v); camX = f.Lead.X; camY = f.Lead.Y; View.Following = f; break; }
                case "--speed": Clock.SetSpeed(int.Parse(v)); break;
                case "--waitboom": waitBoom = true; break;
                case "--zone":
                {
                    var q = v.Split(':'); var pl = World.City(q[0]); var kind = Enum.Parse<BK>(q[1]); var bl = pl.Layout!.Blocks.Where(b => b.Kind == kind).OrderBy(b => Math.Abs(b.Outer.Center.x - pl.X) + Math.Abs(b.Outer.Center.y - pl.Y)).ToList();
                    if (bl.Count > 0)
                    {
                        // the middle of the largest gathering of this kind of block, so the picture shows that kind and little else
                        var best = bl.Where((b, n) => n % Math.Max(1, bl.Count / 200) == 0).OrderByDescending(b => bl.Count(o => Math.Abs(o.Outer.Center.x - b.Outer.Center.x) < 260 && Math.Abs(o.Outer.Center.y - b.Outer.Center.y) < 180)).First().Outer.Center;
                        camX = best.x; camY = best.y;
                    }
                    break;
                }
                case "--noui": Ui.Visible = false; Ui.Panels.Clear(); break;
                case "--atfight":
                {
                    var q = Sim.All.Where(f => f.Airborne && f.Act is Act.Fight or Act.AttackBomber).ToList();
                    if (q.Count > 0) { var m = q[q.Count / 2]; camX = m.X; camY = m.Y; if (v == "watch") Sim.Watch = m; }
                    break;
                }
                case "--raidtable": CallDeferred(nameof(StartRaids), v); break;
                case "--nichetable": CallDeferred(nameof(StartNiche), v); break;
                case "--perftest": CallDeferred(nameof(StartPerf), v); break;
                case "--wavetable": CallDeferred(nameof(StartWave), v); break;
                case "--ucstable": CallDeferred(nameof(StartUcs), v); break;
                case "--flaktable": CallDeferred(nameof(StartFlak), v); break;
                case "--combattable": CallDeferred(nameof(StartCombat), v); break;
                case "--combattrace": CallDeferred(nameof(StartTrace), v); break;
                case "--defencetrace": CallDeferred(nameof(StartDefTrace), v); break;
                case "--hunttrace": CallDeferred(nameof(StartHuntTrace), v); break;
                case "--accuracytable": CallDeferred(nameof(StartAccuracy), v); break;
                case "--damagetable": CallDeferred(nameof(StartDamage), v); break;
                case "--airtable": CallDeferred(nameof(StartAir), v); break;
                case "--fighttest": CallDeferred(nameof(StartFight), v); break;
                case "--afterbombs":
                {
                    // fly until the first bombs land, then this many seconds more
                    double t = double.Parse(v, ci); int guard = 0; while (Air.Hits.Count == 0 && guard++ < 2000) { Air.Advance(20); Fire.SyncTo(Air.T); Air.Recent.Clear(); }
                    for (double u = 0; u < t; u += 20) { Air.Advance(20); Fire.SyncTo(Air.T); Air.Recent.Clear(); }
                    fireView.CallDeferred(nameof(FireView.Prewarm)); Clock.Skip(Air.T); camX = Target.X; camY = Target.Y; break;
                }
                case "--untilfight":
                {
                    // fly until the escorts are at it, then this many seconds more, and watch one
                    int guard = 0; while (Sim.All.Count(q => q.Wolf && q.Act is Act.Fight or Act.Zoom) < 3 && guard++ < 800) { Air.Advance(20); Fire.SyncTo(Air.T); Air.Recent.Clear(); }
                    Air.Advance(double.Parse(v, ci)); Air.Recent.Clear(); Clock.Skip(Air.T); var m = Sim.All.FirstOrDefault(q => q.Wolf && q.Airborne && q.Act is Act.Fight or Act.Zoom); if (m != null) { camX = m.X; camY = m.Y; Sim.Watch = m; }
                    break;
                }
                case "--untilbreak":
                {
                    int guard = 0; while (Sim.Breaks == 0 && guard++ < 800) { Air.Advance(20); Fire.SyncTo(Air.T); Air.Recent.Clear(); }
                    Air.Advance(double.Parse(v, ci)); Air.Recent.Clear(); Clock.Skip(Air.T);
                    var w = Sim.Wings.FirstOrDefault(q => q.Broken); var m = w?.Pilots.FirstOrDefault(p => p.Airborne && p.Mood == Mood.Ramming) ?? w?.Pilots.FirstOrDefault(p => p.Airborne);
                    if (m != null) { camX = m.X; camY = m.Y; Sim.Watch = m; }
                    break;
                }
                case "--untilflak":
                {
                    // fly until the UCS guns are firing at a box, then this many seconds more, and follow that box
                    int guard = 0; while ((Air.Flak == null || !Air.Flak.Bursts.Any(q => !q.Wolf)) && guard++ < 3000) { Air.Advance(10); Fire.SyncTo(Air.T); Air.Recent.Clear(); Sim.Tracers.Clear(); }
                    Air.Advance(double.Parse(v, ci)); Fire.SyncTo(Air.T); Air.Recent.Clear(); Clock.Skip(Air.T); var on = Air.Flak?.Batteries.FirstOrDefault(q => !q.Wolf && q.On != null)?.On;
                    if (on != null) { camX = on.Lead.X; camY = on.Lead.Y; View.Following = on; }
                    break;
                }
                case "--playraid": { double lim = Air.T + double.Parse(v, ci); while (!RaidOver && Air.T < lim) { Air.Advance(20); Fire.SyncTo(Air.T); Air.Recent.Clear(); Sim.Tracers.Clear(); } Fire.SyncTo(Air.T); camX = Target.X; camY = Target.Y; break; }
                case "--firetable": CallDeferred(nameof(StartTable), v); break;
                case "--firecurve": CallDeferred(nameof(StartCurve), v); break;
                case "--firebench": CallDeferred(nameof(StartBench), v); break;
                case "--selftest": CallDeferred(nameof(StartSelfTest), v); break;
                case "--days": RunCampaign(double.Parse(v, ci) * Campaign.Day); fireView.CallDeferred(nameof(FireView.Prewarm)); break;
                case "--campaigntest": CallDeferred(nameof(StartCampTest), v); break;
                case "--planlog": CallDeferred(nameof(StartPlanLog), v); break;
                case "--savemanual": { while (Camp != null && !CanSave) { RunCampaign(600); if (!opKept && OpOver && !OpBusy) EndOp(); } Save(SaveGame.Manual); break; }      // tests: run on to a quiet moment and save
                case "--bridge": { var q = World.FirstBridge(); camX = q.x; camY = q.y; break; }
            }
        }
        if (a.Contains("--setup")) StartSetup(); else if (a.Contains("--plan")) StartPlan(); else if (a.Contains("--campplan") && Camp != null)
        {
            StartSetup(); FinishSetup();
            if (a.Contains("--flyop")) { SendRaid(); int g = 0, fm0 = Array.IndexOf(a, "--flymin"); double stopAt = fm0 >= 0 ? Air.LaunchAt + double.Parse(a[fm0 + 1], ci) * 60 : double.MaxValue; while (!(OpOver && !OpBusy && Air.Formations.Count > 0) && Air.T < stopAt && g++ < 100000) { CampaignStep(20); Fire.SyncTo(Air.T); Air.Recent.Clear(); Sim.Tracers.Clear(); } camX = Target.X; camY = Target.Y; }      // shots: fly it through to the report
        }
        Clamp();
    }

    void ShowHangar() { hangar.Visible = !hangar.Visible; hangar.QueueRedraw(); }
    void StartAir(string path) => AirTable(path);
    void StartUcs(string path) => UcsTable(path);
    void StartFlak(string path) => FlakTable(path);
    void StartCombat(string path) => CombatTable(path);
    void StartDump(string path) => DumpSaves(path);
    void StartTrace(string path) => CombatTrace(path);
    void StartDefTrace(string path) => DefenceTrace(path);
    void StartHuntTrace(string path) => HuntTrace(path);
    void StartAccuracy(string path) => AccuracyTable(path);
    void StartDamage(string path) => DamageTable(path);
    void StartCampTest(string path) => CampaignTest(path);
    void StartPlanLog(string path) => PlanLog(path);
    void StartRaids(string path) => RaidTable(path);
    void StartNiche(string path) => NicheTable(path);
    void StartPerf(string path) => PerfTest(path);
    void StartWave(string path) => WaveTable(path);
    void StartFight(string path) => FightTest(path);
    void StartBench(string path) => FireBench(path);
    void StartTable(string path) => FireTable(path);
    void StartCurve(string path) => FireCurve(path);
    void StartSelfTest(string path) => SelfTest(path);
    double MinZoom() { var s = GetViewportRect().Size; return Math.Max(s.X / World.WidthM, s.Y / World.HeightM); }
    void Clamp()
    {
        var s = GetViewportRect().Size; zoom = Math.Clamp(zoom, MinZoom(), 8);
        double hx = s.X / 2 / zoom, hy = s.Y / 2 / zoom;
        camX = Math.Clamp(camX, hx, World.WidthM - hx); camY = Math.Clamp(camY, hy, World.HeightM - hy);     // the view never leaves the world
    }
    public Vector2 W2S(double x, double y) { var c = GetViewportRect().Size / 2; return new Vector2((float)(c.X + (x - camX) * zoom), (float)(c.Y - (y - camY) * zoom)); }
    public double S2Wx(float sx) => camX + (sx - GetViewportRect().Size.X / 2) / zoom;
    public double S2Wy(float sy) => camY - (sy - GetViewportRect().Size.Y / 2) / zoom;
    public Vector2 MouseForReadout => shotMouse ?? (shot != null ? GetViewportRect().Size / 2 : GetViewport().GetMousePosition());
    Vector2? shotMouse;

    public void SelectCity(Place c, bool fly)
    {
        if (Setup) { PickTarget(c); return; }
        Selected = c;
        if (fly) { glideX = c.X; glideY = c.Y; glideZ = Math.Max(zoom, .035); gliding = true; }
    }

    public void GoTo(Formation f) { glideX = f.Lead.X; glideY = f.Lead.Y; glideZ = Math.Max(zoom, .1); gliding = true; }

    CanvasModulate night = null!; public FireView FireV => fireView; int raidSeed;
    /// <summary>Sets up one raid. Crews who came through it are in the roster for this one.</summary>
    void Build(double startT, double hour, Roster? crews = null, double launchAt = double.NaN, bool own = true, CampaignEvent? ucs = null)
    {
        var ra = OS.GetCmdlineUserArgs(); int Arg(string n, int d) { int q = Array.IndexOf(ra, n); return q >= 0 ? int.Parse(ra[q + 1]) : d; }
        bool off = ra.Contains("--ucstable") || ra.Contains("--combattable") || ra.Contains("--combattrace") || ra.Contains("--defencetrace") || ra.Contains("--hunttrace") || ra.Contains("--accuracytable") || ra.Contains("--damagetable") || ra.Contains("--flaktable") || ra.Contains("--raidtable") || ra.Contains("--nichetable") || ra.Contains("--wavetable") || ra.Contains("--selftest") || ra.Contains("--firetable") || ra.Contains("--firecurve") || ra.Contains("--firebench") || ra.Contains("--nofighters");
        bool test = off || ra.Contains("--airtable") || ra.Contains("--v16only");
        Air = new Air(World, 1 + raidSeed, startT, hour); if (crews != null) Air.Crews = crews; Air.LaunchAt = launchAt;
        if (Camp != null) { int wx = Camp.Seed; Air.Moon = Campaign.WeatherAt(wx, double.IsNaN(launchAt) ? startT : launchAt).moon; Air.WxAt = (x, y, t) => Weather.At(wx, x, y, t); Air.WindAt = (x, y, t) => Weather.WindAt(wx, t, x, y).speed; }
        if (Camp != null)
        {
            // one operation: the player's raid if there is one, and whatever UCS raids fall due while it is out
            if (own && flying != null) Air.Flights(flying.Aim.x, flying.Aim.y, flying.Flights.Where(q => q.N > 0).Select(q => (World.Fields[q.Field], q.Kind, q.N)).ToList());
            opUcs = Camp.TakeUcsRaids((double.IsNaN(launchAt) ? startT : launchAt) + 6 * 3600); if (Forces != null) opUcs.RemoveAll(q => Forces.Planner.Scrub(q, Camp, Forces)); if (ucs != null) opUcs.Insert(0, ucs);
            if (Forces != null) { var taken = new System.Collections.Generic.Dictionary<(Base, Craft), int>(); foreach (var e in opUcs) { var at = e.Site != "" ? Forces.Sites.Named(e.Site) : null; Air.Raid(e.Target!, UcsBoxes(e, taken), false, e.T - startT, e.Works, at != null ? (at.X, at.Y) : null); } opCommitted = false; committed.Clear(); }
            else foreach (var e in opUcs) Air.TestRaid(e.Target!, e.Waves, new[] { Craft.TB6, Craft.SB3, Craft.SB3 }, false, e.T - startT, 900);
            opKept = false; ReportDone = false; offTold = radarTold = overTold = false; hitsSeen = 0; bombedTold.Clear();
        }
        else
        {
            if (flying != null) Air.Flights(flying.Aim.x, flying.Aim.y, flying.Flights.Select(q => (World.Fields[q.Field], q.Kind, q.N)).ToList());
            else Air.TestRaid(Target, Arg("--waves", 1), test ? null : new[] { Craft.V16, Craft.V16, Craft.U4 });
            if (!test && !ra.Contains("--noucsraid") && (flying == null || flying.UcsRaid)) Air.TestRaid(World.City("Hjalmar"), Arg("--ucswaves", 4), new[] { Craft.TB6, Craft.SB3, Craft.SB3 }, false, 1800, 900);
        }
        Air.Landed = Fire.Hit;
        if (Camp != null && Forces != null) { var hitRng = new Random(raidSeed + 5); var a0 = Air; Air.Landed = h => { Fire.Hit(h); Forces.Sites.Hit(h, Camp.Now, Forces, hitRng); if (Forces.Sites.FlakChanged) { Forces.Sites.FlakChanged = false; a0.Flak?.Sync(); } }; Air.RadarCovers = Forces.Sites.RadarCovers; }
        { int fq = Array.IndexOf(ra, "--flak"); double ucsFlak = fq >= 0 ? double.Parse(ra[fq + 1], System.Globalization.CultureInfo.InvariantCulture) : flying?.Flak ?? 1; if (!off || fq >= 0) Air.Flak = new FlakSim(World, Air, ucsFlak, fq >= 0 && ucsFlak == 0 ? 0 : 1, 70 + raidSeed, Forces != null ? Forces.Planner.FlakOf : null, Forces?.Sites); }
        Sim = Camp != null && Forces != null ? new FighterSim(World, Air, GarrisonFor(double.IsNaN(launchAt) ? (opUcs.Count > 0 ? opUcs[0].T : startT) : launchAt), Arg("--seed", 3) + raidSeed) : new FighterSim(World, Air, off ? 0 : flying?.Escorts ?? Arg("--esc", 4), off ? 0 : flying?.Fighters ?? Arg("--int", 24), Arg("--seed", 3) + raidSeed, off ? 0 : Arg("--def", 6), off ? 0 : Arg("--ucsesc", 2)); Sim.Crashed = Fire.Crash;
    }
    public bool RaidOver => Air.Formations.All(f => f.Phase == Phase.Landed) && !Sim.All.Any(f => f.Airborne) && Air.T > Air.StartT + 60;
    /// <summary>Next raid: the same air forces. Everyone who got back flies again with their record; the aircraft are repaired; replacements fill the gaps.</summary>
    public void NextRaid(double hour)
    {
        var c = Air.Crews; c.Raid++; c.Clear(); raidSeed++; KeepCrews(1);
        Build(Air.T + 600, hour, c); View.Chosen.Clear(); View.Picked.Clear(); View.Following = null; FollowPilot = false;
        StartPlan();
    }
    /// <summary>Everyone who came through goes on the roster for the next raid, and each flight's load is remembered.</summary>
    void KeepCrews(int flown)
    {
        var c = Air.Crews;
        var wp = Sim.All.Where(f => f.Wolf && (f.Alive || f.Back)).ToList(); var up = Sim.All.Where(f => !f.Wolf && (f.Alive || f.Back)).ToList();
        PilotRec Rec(Fighter f) => new() { Name = f.Name, Skill = f.Skill, Aggression = f.Aggression, Hunter = f.Hunter, Career = f.Career + f.KillsB + f.KillsF, Sorties = f.Sorties + (f.Act == Act.Ground ? 0 : 1) };
        // whoever flew was taken off the roster when the operation was made; the survivors go back on it. Whoever did not fly is still on it (before, a raid with no Vulfen bombers in it emptied the Vulfen captains)
        c.WolfPilots.AddRange(wp.Select(Rec)); c.UcsPilots.AddRange(up.OrderBy(f => f.Id * 7919 % 101).Select(Rec));
        CaptainRec Cap(Bomber a) => new() { Name = a.Captain, Nerve = a.Nerve, Sorties = a.Sorties + flown };
        var all = Air.Formations.SelectMany(f => f.Ac).Concat(Air.Returned).Concat(Air.BackHome).ToList();
        c.WolfCaptains.AddRange(all.Where(a => a.F.Wolf).Select(Cap)); c.UcsCaptains.AddRange(all.Where(a => !a.F.Wolf).Select(Cap));
        lastLoads.Clear(); foreach (var f in Air.Formations.Where(f => f.Wolf)) lastLoads[f.Name] = (f.Preset, f.ArriveMin);
    }

    public override void _Process(double delta)
    {
        { float dk = (float)Air.Dark; night.Color = Colors.White.Lerp(new Color(.36f, .4f, .56f), dk); }
        var pf = System.Diagnostics.Stopwatch.StartNew(); if (Camp != null) { if (OpBusy && Clock.SpeedIdx > Clock.OpMax) Clock.SpeedIdx = Clock.OpMax; if (!Holding) CampaignStep(Clock.Advance(Math.Min(delta, .1))); } else if (!Holding) Air.Advance(Clock.Advance(delta)); Prof.Add("air", pf.Elapsed.TotalMilliseconds); pf.Restart(); Fire.SyncTo(Air.T, 4); foreach (var f in Air.Formations) if (f.Phase == Phase.Outbound) { var near = World.CityNear(f.AimX, f.AimY); if (near != null) Fire.Prepare(near); } Prof.Add("fire", pf.Elapsed.TotalMilliseconds);
        if (FollowPilot && Sim.Watch != null && Sim.Watch.Airborne) { var pw = FView.Screen(Sim.Watch); camX = S2Wx(pw.X); camY = S2Wy(pw.Y); gliding = false; }
        if (FollowPilot && Air.Watch != null && Air.Watch.Alive) { var pw = View.Screen(Air.Watch); camX = S2Wx(pw.X); camY = S2Wy(pw.Y); gliding = false; }
        if (View.Following != null) { var (fx, fy) = View.Pos(View.Following.Lead); camX = fx; camY = fy; gliding = false; }
        double pan = 600 / zoom * delta;
        if (Input.IsKeyPressed(Key.W)) { camY += pan; gliding = false; View.Following = null; }
        if (Input.IsKeyPressed(Key.S)) { camY -= pan; gliding = false; View.Following = null; }
        if (Input.IsKeyPressed(Key.A)) { camX -= pan; gliding = false; View.Following = null; }
        if (Input.IsKeyPressed(Key.D)) { camX += pan; gliding = false; View.Following = null; }
        if (gliding)
        {
            double k = 1 - Math.Exp(-3.5 * delta);
            camX += (glideX - camX) * k; camY += (glideY - camY) * k; zoom = Math.Exp(Math.Log(zoom) + (Math.Log(glideZ) - Math.Log(zoom)) * k);
            if (Math.Abs(glideX - camX) * zoom < 1 && Math.Abs(glideY - camY) * zoom < 1 && Math.Abs(zoom / glideZ - 1) < .005) gliding = false;
        }
        Clamp(); QueueRedraw();
        if (shot != null)
        {
            idleFrames = terrain.Idle ? idleFrames + 1 : 0;
            if (idleFrames > 5 && (!waitBoom || fireView.BoomCount >= 150))
            {
                GD.Print($"labels shown {labels.Shown}, overlapping pairs {labels.Overlaps()}");
                FireReport(shot);
                GetViewport().GetTexture().GetImage().SavePng(shot); GetTree().Quit();
            }
        }
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is InputEventKey k && k.Pressed && !k.Echo)
        {
            if (k.Keycode == Key.Space && !Holding) Clock.Paused = !Clock.Paused;
            if (k.Keycode >= Key.Key1 && k.Keycode <= (Camp != null ? Key.Key7 : Key.Key5) && !Holding) Clock.SetSpeed((int)(k.Keycode - Key.Key1));
            if (k.Keycode == Key.N) SkipAhead();
            if (k.Keycode == Key.C) { cityIdx = (cityIdx + 1) % World.Cities.Count; SelectCity(World.Cities[cityIdx], true); }
            if (k.Keycode == Key.Home) { glideX = World.WidthM / 2; glideY = World.HeightM / 2; glideZ = MinZoom(); gliding = true; }
            if (k.Keycode == Key.H) ShowHangar();
            if (k.Keycode == Key.F) { if (Sim.Watch != null || Air.Watch != null) FollowPilot = !FollowPilot; else View.Following = View.Following == null ? View.Primary : null; }
            if (k.Keycode == Key.Escape) { if (Ui.CancelPick()) { } else if (Air.Watch != null) { Air.Watch = null; } else if (Sim.Watch != null) { Sim.Watch = null; FollowPilot = false; } else if (View.Mode != 0) View.Mode = 0; else if (View.Picked.Count > 0) View.Picked.Clear(); else if (Selected != null) Selected = null; else GetTree().Quit(); }
        }
        else if (e is InputEventMouseButton b)
        {
            bool wheel = b.ButtonIndex == MouseButton.WheelUp || b.ButtonIndex == MouseButton.WheelDown;
            if (wheel && b.Pressed)
            {
                if (Ui.Wheel(b.Position, b.ButtonIndex == MouseButton.WheelUp) || Ui.Covers(b.Position)) return;
                gliding = false;
                double wx = S2Wx(b.Position.X), wy = S2Wy(b.Position.Y);     // the point under the cursor stays under the cursor
                zoom *= b.ButtonIndex == MouseButton.WheelUp ? 1.2 : 1 / 1.2; Clamp();
                var c = GetViewportRect().Size / 2;
                camX = wx - (b.Position.X - c.X) / zoom; camY = wy + (b.Position.Y - c.Y) / zoom;
            }
            else if (b.ButtonIndex == MouseButton.Right && b.Pressed) { if (Ui.CancelPick()) return; if (!Ui.Covers(b.Position)) View.RightClick(b.Position); }
            else if (b.ButtonIndex == MouseButton.Left || b.ButtonIndex == MouseButton.Middle)
            {
                if (b.Pressed)
                {
                    if (b.ButtonIndex == MouseButton.Left && Ui.Click(b.Position)) return;
                    if (b.ButtonIndex == MouseButton.Left && b.ShiftPressed) { View.Boxing = true; View.BoxA = View.BoxB = b.Position; return; }
                    drag = true; moved = false; pressAt = b.Position;
                }
                else if (View.Boxing) View.FinishBox(b.Position);
                else
                {
                    if (drag && !moved && b.ButtonIndex == MouseButton.Left && !View.MapClick(b.Position)) { var c = labels.CityAt(b.Position); if (c != null) SelectCity(c, false); }
                    drag = false;
                }
            }
        }
        else if (e is InputEventMouseMotion mb && View.Boxing) View.BoxB = mb.Position;
        else if (e is InputEventMouseMotion m && drag)
        {
            if ((m.Position - pressAt).Length() > 4) { moved = true; gliding = false; View.Following = null; }
            if (moved) { camX -= m.Relative.X / zoom; camY += m.Relative.Y / zoom; }
        }
    }

    public override void _Draw()
    {
        var size = GetViewportRect().Size;
        DrawRect(new Rect2(Vector2.Zero, size), new Color(.11f, .16f, .2f));   // under the first frame only: the coarsest tiles cover every view
        var pf = System.Diagnostics.Stopwatch.StartNew(); terrain.Draw(this, W2S, zoom, camX, camY, size); Prof.Add("terrain", pf.Elapsed.TotalMilliseconds);
        if (Selected != null)
        {
            var p = W2S(Selected.X, Selected.Y); float r = (float)Math.Clamp(Selected.R * 1.6 * zoom, 11, 70), l = r * .4f; var col = ConsoleUi.Amber;
            foreach (var (dx, dy) in new[] { (1, 1), (-1, 1), (1, -1), (-1, -1) })
            { var q = p + new Vector2(dx * r, dy * r); DrawLine(q, q - new Vector2(dx * l, 0), col, 1.5f); DrawLine(q, q - new Vector2(0, dy * l), col, 1.5f); }
        }
    }
}
