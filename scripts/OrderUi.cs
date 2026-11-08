using Godot;
using System;
using System.Linq;

namespace Firebomber;

/// <summary>The two console parts that deal with aircraft: the formations panel and the order bar.</summary>
public partial class ConsoleUi
{
    /// <summary>Returns the height used.</summary>
    float FormationsPanel(float y)
    {
        const float x = 8, w = 306, rowH = 44;
        var v = M.View; var air = M.Air;
        bool hunds = M.Sim.All.Any(q => q.Wolf && q.Airborne); var own = air.Formations.Where(q => q.Wolf).ToList();      // our boxes from ordering to landing; Hunds only while some are up
        if (own.Count == 0 && !hunds) return 0;
        float h = 32 + (own.Count + (hunds ? 1 : 0)) * rowH + (own.Count > 0 ? 22 : 4);
        Panel(new Rect2(x, y, w, h), Wolf);
        Text(x + 12, y + 22, "OURS IN THE AIR", 15, Amber);
        if (own.Count > 1) Text(x + 160, y + 21, "shift-click: several", 11, Dim);
        if (own.Count > 0) Button(new Rect2(x + w - 62, y + 6, 52, 22), "ALL", v.Chosen.Count == own.Count, v.ChooseAll);
        float ry = y + 32;
        foreach (var f in own)
        {
            var row = new Rect2(x + 6, ry, w - 12, rowH - 4); bool sel = v.Chosen.Contains(f);
            DrawRect(row, sel ? new Color(.2f, .34f, .18f, .9f) : new Color(.07f, .12f, .07f, .85f));
            DrawRect(new Rect2(row.Position, new Vector2(4, row.Size.Y)), Wolf);
            HoverIf(row, f.Kind); Sprites.Card(this, f.Kind, row.Position + new Vector2(34, 20), f.Kind == Craft.V16 ? 36 : 30, f.Phase == Phase.Landed ? new Color(.55f, .6f, .55f) : Colors.White);
            Text(row.Position.X + 68, ry + 16, $"{f.Name}  {Sprites.Specs[f.Kind].Name}  x{f.Ac.Count + f.BackEarly}{(f.Ac.Count + f.BackEarly < f.Start ? "/" + f.Start : "")}", 14, f.Phase == Phase.Landed ? Dim : Ink);
            Text(row.Position.X + 68, ry + 33, M.Planning ? $"{f.Plan.Sum(b => b.Kg) / 1000.0:0.##} t each, {Bombs.HeShare(f.Plan) * 100:0}% HE, {f.Plan.Sum(b => b.Kg) * f.Ac.Count / 1000.0:0} t in all" : air.Status(f), 12, Dim);
            var ff = f;
            buttons.Add((row, () =>
            {
                if (Input.IsKeyPressed(Key.Shift) || Input.IsKeyPressed(Key.Ctrl)) v.Toggle(ff);
                else { if (v.Chosen.Count == 1 && v.Chosen.Contains(ff)) M.GoTo(ff); v.ChooseOnly(ff); }
            }));
            Named["row " + f.Name] = row;
            ry += rowH;
        }
        var sim2 = M.Sim;
        void Side(bool wolf, Craft k, string title, int total, int lost, string line)
        {
            var row = new Rect2(x + 6, ry, w - 12, rowH - 4);
            DrawRect(row, wolf ? new Color(.07f, .12f, .07f, .85f) : new Color(.14f, .07f, .06f, .85f)); DrawRect(new Rect2(row.Position, new Vector2(4, row.Size.Y)), wolf ? new Color(.6f, .9f, .6f) : Deer);
            HoverIf(row, k); Sprites.Card(this, k, row.Position + new Vector2(34, 20), 26, Colors.White);
            Text(row.Position.X + 68, ry + 16, wolf || k != Craft.I8 ? $"{title}  x{total - lost}/{total}" : $"{title}  {sim2.All.Count(q => !q.Wolf && q.Airborne)} up, {lost} down", 14, wolf ? Ink : new Color(1f, .6f, .5f));
            Text(row.Position.X + 68, ry + 33, line, 12, wolf ? Dim : new Color(.9f, .65f, .55f));
            ry += rowH;
        }
        var sim = M.Sim;
        int Cnt(bool wolf, params Act[] a) => sim.All.Count(q => q.Wolf == wolf && a.Contains(q.Act));
        if (hunds) Side(true, Craft.F5, "F-5 Hund", sim.All.Count(q => q.Wolf), sim.WolfLost, sim.Wings.Any(q => q.Wolf && q.Withdrawing) ? $"{sim.Wings.Count(q => q.Wolf && q.Withdrawing)}/{sim.Wings.Count(q => q.Wolf)} groups withdrawing, {Cnt(true, Act.Fight)} fighting" : (sim.Wings.Any(q => q.Wolf && q.Pressing) ? "pressing: " : "") + $"{Cnt(true, Act.Fight)} fighting, {Cnt(true, Act.Cover, Act.Follow)} cover, {Cnt(true, Act.Home)} left");
        if (own.Count > 0) Text(x + 12, ry + 13, $"LOST {air.BombersLost}   RELEASED {own.SelectMany(f => f.Ac).Count(a => a.Released)}/{own.Sum(f => f.Ac.Count)}   {air.Tonnes:0} TONNES", 12, Ink);
        return h;
    }

    /// <summary>The enemy in the air, in a panel of its own: their fighters, and their bombers by type. Kept apart from ours so that a glance tells whose is whose.</summary>
    float EnemyPanel(float y)
    {
        const float x = 8, w = 306, rowH = 40; var air = M.Air; var sim = M.Sim;
        var foes = air.Formations.Where(q => !q.Wolf && q.Phase is Phase.Outbound or Phase.Egress or Phase.Returning).ToList(); var kinds = foes.Select(q => q.Kind).Distinct().ToList(); bool fighters = sim.All.Any(q => !q.Wolf && q.Airborne);      // only what is up: not boxes still on the ground, or long landed
        if (foes.Count == 0 && !fighters) return 0;
        float h = 32 + (kinds.Count + (fighters ? 1 : 0)) * rowH + 4;
        Panel(new Rect2(x, y, w, h), Deer);
        Text(x + 12, y + 22, "ENEMY IN THE AIR", 15, Deer);
        if (!double.IsNaN(sim.AlertT) || foes.Any(q => q.Phase != Phase.Waiting)) Text(x + 168, y + 21, foes.Any(q => q.Phase == Phase.Outbound) ? "raid coming in" : fighters && sim.All.Any(q => !q.Wolf && q.Airborne) ? "fighters up" : "", 11, new Color(1f, .6f, .5f));
        float ry = y + 32;
        void Row(Craft k, string title, string line)
        {
            var row = new Rect2(x + 6, ry, w - 12, rowH - 4);
            DrawRect(row, new Color(.14f, .07f, .06f, .85f)); DrawRect(new Rect2(row.Position, new Vector2(4, row.Size.Y)), Deer);
            HoverIf(row, k); Sprites.Card(this, k, row.Position + new Vector2(34, 18), 26, Colors.White);
            Text(row.Position.X + 68, ry + 15, title, 14, new Color(1f, .6f, .5f)); Text(row.Position.X + 68, ry + 31, line, 12, new Color(.9f, .65f, .55f));
            ry += rowH;
        }
        int Cnt(params Act[] a) => sim.All.Count(q => !q.Wolf && a.Contains(q.Act));
        string Some(params (int n, string what)[] parts) { var s = string.Join(", ", parts.Where(p => p.n > 0).Select(p => $"{p.n} {p.what}")); return s == "" ? "climbing" : s; }
        if (fighters) Row(Craft.I8, $"Fighters  {sim.All.Count(q => !q.Wolf && q.Airborne)} up, {sim.UcsLost} down", sim.Breaks > 0 ? $"{sim.Breaks} squadron{(sim.Breaks > 1 ? "s" : "")} BROKEN, {Cnt(Act.AttackBomber)} on bombers" : Some((Cnt(Act.AttackBomber), "on bombers"), (Cnt(Act.Fight), "on fighters"), (Cnt(Act.Form, Act.Hunt, Act.Stalk), "closing"), (Cnt(Act.Home), "leaving")));
        foreach (var k in kinds)
        {
            var g = foes.Where(q => q.Kind == k).ToList(); int left = g.Sum(q => q.Ac.Count + q.BackEarly), start = g.Sum(q => q.Start);
            Row(k, $"{Sprites.Specs[k].Name}  {g.Count} box{(g.Count > 1 ? "es" : "")}, x{left}{(left < start ? "/" + start : "")}", Some((g.Count(q => q.Phase == Phase.Outbound), "inbound"), (g.Count(q => q.Phase is Phase.Egress or Phase.Returning && !q.CommanderTurned), "going home"), (g.Count(q => q.CommanderTurned), "turned back")));
        }
        return h;
    }

    /// <summary>One pilot: who, what state, and what they have decided and why. Click a fighter to open it.</summary>
    void PilotPanel(Vector2 size, float top)
    {
        var f = M.Sim.Watch; if (f == null) return;
        float w = 400, h = f.H.Note != "" ? 166 : 150;
        var pr = AircraftPop(f, M.FView.Screen(f), new Vector2(w, h), f.Wolf ? Green : Deer); float x = pr.Position.X; top = pr.Position.Y;
        Panel(pr, f.Wolf ? Green : Deer);
        Text(x + 14, top + 24, $"{Sprites.Specs[f.Kind].Name}: {(f.Ace ? "ACE " : "")}{(f.Wolf ? "Lt." : "Sgt.")} {f.Name}".ToUpperInvariant(), 15, f.Wolf ? Amber : new Color(1f, .6f, .5f));
        Text(x + 14, top + 46, f.Alive ? f.Why : "shot down", 13, f.Alive ? Ink : Deer);
        Text(x + 14, top + 68, $"{f.Z:0} m   {f.V * 3.6:0} km/h   hull {(1 - f.Damage) * 100:0}%{(f.H.Burning ? "   ON FIRE" : "")}{(f.H.Eng[0] <= 0 ? "   ENGINE DEAD" : f.H.Eng[0] < f.H.D.EngHp ? "   engine hit" : "")}", 12, f.H.Burning ? Deer : Dim);
        Text(x + 14, top + 86, $"fuel {f.Fuel / 60:0} min{(f.H.Leak > .1 ? " LEAKING" : "")}   {f.Ammo:0} rounds   from {f.HomeName}", 12, Dim);
        Text(x + 14, top + 104, $"kills today: {f.KillsB} bombers, {f.KillsF} fighters   before: {f.Career}", 12, Dim);
        float ny = top + 122; if (f.H.Note != "") { Text(x + 14, ny, f.H.Note, 12, Amber); ny += 18; }      // its own line: it used to sit across the kills
        Text(x + 14, ny, $"{(f.Skill < .55 ? "green" : f.Skill < .9 ? "competent" : "expert")}   {(f.Hunter > .2 ? "hunts fighters" : f.Hunter < -.2 ? "prefers bombers" : "no preference")}   {f.W?.Name}: {Standing(f.W)}", 12, Dim);
        Button(new Rect2(x + w - 62, top + 8, 52, 22), "CLOSE", false, () => M.Sim.Watch = null);
    }

    /// <summary>One bomber: its captain, what they have decided, and the state of everything aboard.</summary>
    void BomberPanel(Vector2 size, float top)
    {
        var a = M.Air.Watch; if (a == null) return;
        var h = a.H; var d = h.D; bool wolf = a.F.Wolf; float w = 350, ht = 190;
        var pr = AircraftPop(a, M.View.Screen(a), new Vector2(w, ht), wolf ? Green : Deer); float x = pr.Position.X; top = pr.Position.Y;
        Panel(pr, wolf ? Green : Deer);
        Text(x + 14, top + 24, $"{Sprites.Specs[a.F.Kind].Name.Split(' ')[0]}: Capt. {a.Captain}, box {a.F.Name}".ToUpperInvariant(), 14, wolf ? Amber : new Color(1f, .6f, .5f));
        Button(new Rect2(x + w - 62, top + 8, 52, 22), "CLOSE", false, () => M.Air.Watch = null);
        Text(x + 14, top + 44, a.Alive ? a.Why : "lost" + (h.Cause != "" ? $": {h.Cause}" : ""), 13, a.Alive ? Ink : Deer);
        Text(x + 14, top + 64, $"{a.Z:0} m   {a.V * 3.6:0} km/h   airframe {(1 - h.Damage) * 100:0}%{(h.Burning ? "   ON FIRE" : "")}", 12, h.Burning ? Deer : Dim);
        string eng = string.Join(" ", h.Eng.Select(e => h.Dry || e <= 0 ? "X" : e < d.EngHp ? "o" : "O"));
        Text(x + 14, top + 82, $"engines  {eng}   ({h.Working} of {d.Engines})   {(h.Sink > 0 ? "LOSING HEIGHT" : "")}", 12, h.Working < d.Engines ? Amber : Dim);
        Text(x + 14, top + 100, $"fuel {h.Fuel * Weight.Cruise(d, a.Kg) / 1000:0} km{(h.Leak > .1 ? "  LEAKING" : "")}{(d.SelfSeal ? "  self-sealing" : "")}   bombs {(a.Released ? "gone" : $"{a.Kg / 1000:0.##} t")}", 12, h.Leak > .1 ? Amber : Dim);
        int fit = h.CrewState.Count(s => s == 0);
        string casualties = string.Join(", ", Enumerable.Range(0, h.CrewState.Length).Where(i => h.CrewState[i] > 0).Select(i => d.Crew[i].ToLowerInvariant() + (h.CrewState[i] == 2 ? " dead" : " hurt")));
        Text(x + 14, top + 118, $"crew {fit} of {d.Crew.Length} fit" + (casualties != "" ? ": " + casualties : ""), 12, casualties != "" ? Amber : Dim);
        if (h.Turrets.Count > 0) Text(x + 14, top + 136, $"guns: above {h.Turrets.Where(t => t.S.Top).Sum(t => t.Rounds):0} rounds, below {h.Turrets.Where(t => !t.S.Top).Sum(t => t.Rounds):0}", 12, Dim);
        else Text(x + 14, top + 136, "no turrets: relies on speed", 12, Dim);
        Text(x + 14, top + 154, h.Note != "" ? "last: " + h.Note : $"sorties {a.Sorties}   roof now {Weight.Roof(d, a.Kg) * h.RoofShare:0} m", 12, h.Note != "" ? Amber : Dim);
        Text(x + 14, top + 172, a.Released ? "" : Bombs.Describe(a.F.Plan), 11, Dim);
    }

    /// <summary>Before the raid: each flight picks one of its type's loads. The heavier the load, the shorter the reach; the row says at once if it will not get home.</summary>
    void PlanPanel(Vector2 size)
    {
        if (!M.Planning) return;
        var v = M.View; var air = M.Air; var own = air.Formations.Where(q => q.Wolf).ToList(); if (own.Count == 0) return;
        if (v.Primary == null) v.ChooseOnly(own[0]);
        var f = v.Primary!; var d = Design.Of[f.Kind]; var list = Loadouts.Of[f.Kind];
        float x = 322, y = 42, w = 520, rowH = 38, h = 38 + list.Length * rowH + 112 + (M.Forces != null ? 20 : 0);
        Panel(new Rect2(x, y, w, h), Amber);
        Text(x + 14, y + 24, "LOADOUT", 15, Amber);
        string who = v.Chosen.Count > 1 ? "flights " + string.Join("+", v.Chosen.Where(c => c.Kind == f.Kind).Select(c => c.Name).OrderBy(n => n)) : "flight " + f.Name;
        Text(x + 92, y + 24, $"{who}: {Sprites.Specs[f.Kind].Name}", 14, Ink);
        if (M.CanGoBack) Button(new Rect2(x + w - 70, y + 6, 60, 22), "BACK", false, M.BackToSetup);
        var heCol = new Color(.75f, .78f, .82f); var fireCol = new Color(1f, .5f, .15f); double trip = air.TripKm(f); float ry = y + 34;
        foreach (var l in list)
        {
            var row = new Rect2(x + 8, ry, w - 16, rowH - 3); bool on = f.Preset == l; var ll = l; var pl = l.Plan; double he = pl.Where(b => b.He).Sum(b => b.Kg), range = Weight.RangeKm(d, l.Kg); bool shortOf = range < trip * 1.05;
            DrawRect(row, on ? new Color(.2f, .34f, .18f, .9f) : new Color(.07f, .12f, .07f, .85f));
            Text(x + 16, ry + 15, l.Name, 14, on ? Amber : Ink); Text(x + 16, ry + 30, Bombs.Describe(pl), 12, Dim);
            // the balance of this load: grey for blast, orange for fire, against what the aircraft can lift
            float bw = 110, hw = bw * (float)(he / d.MaxKg), iw = bw * (float)((l.Kg - he) / d.MaxKg), bx0 = x + w - 250;
            if (new Rect2(bx0, ry + 2, bw, 14).HasPoint(Mouse)) { tip.Add(($"{he / 1000:0.##} t of blast", Ink)); tip.Add(($"{(l.Kg - he) / 1000:0.##} t of fire", new Color(1f, .6f, .25f))); tip.Add(($"of {d.MaxKg / 1000:0.##} t the aircraft can lift", Dim)); }
            DrawRect(new Rect2(bx0, ry + 5, bw, 8), new Color(0, 0, 0, .5f)); DrawRect(new Rect2(bx0, ry + 5, hw, 8), heCol); DrawRect(new Rect2(bx0 + hw, ry + 5, iw, 8), fireCol);
            Text(x + w - 130, ry + 15, $"{l.Kg / 1000:0.##} t", 13, Ink); Text(x + w - 130, ry + 30, shortOf ? $"{range:N0} km: TOO FAR" : $"{range:N0} km", 12, shortOf ? Deer : Dim);
            buttons.Add((row, () => v.Each(c => { if (c.Kind == f.Kind) Air.SetPreset(c, ll); }))); Named["load " + l.Name] = row;
            ry += rowH;
        }
        double kg = f.Plan.Sum(b => b.Kg);
        Text(x + 14, ry + 14, $"Trip {trip:N0} km, {Weight.Cruise(d, kg) * 3.6:0} km/h, roof {Weight.Roof(d, kg):N0} m, aim good to about {air.AimScatter(f):N0} m", 13, Dim);
        Text(x + 14, ry + 38, f.ArriveMin == 0 ? "Over the target with the first wave" : $"Over the target {f.ArriveMin} min after the first wave", 13, Ink);
        Button(new Rect2(x + w - 186, ry + 20, 84, 24), "EARLIER", false, () => v.Each(c => c.ArriveMin = Math.Max(0, c.ArriveMin - 5)));
        Button(new Rect2(x + w - 96, ry + 20, 84, 24), "LATER", false, () => v.Each(c => c.ArriveMin = Math.Min(60, c.ArriveMin + 5)));
        double raid = own.Sum(q => q.Plan.Sum(b => b.Kg) * q.Ac.Count) / 1000.0, raidHe = own.Sum(q => q.Plan.Where(b => b.He).Sum(b => b.Kg) * q.Ac.Count) / 1000.0;
        Text(x + 14, ry + 64, $"The raid: {raid:0} t, {raidHe:0} t blast and {raid - raidHe:0} t fire", 13, Ink);
        if (M.Forces != null)
        {
            var ind = M.Forces.Ind; var need = M.RaidNeeds();
            Text(x + 14, ry + 84, $"Dumps: HE {ind.He[1]:N0} t, incendiary {ind.Inc[1]:N0} t, fuel {ind.Fuel[1]:N0} t (this raid {need.fuel:N0} t)", 12, Dim); ry += 20;
        }
        var why = M.ShortOf();
        if (why != null) { var r = new Rect2(x + 14, ry + 72, w - 28, 30); DrawRect(r, new Color(.2f, .06f, .05f, .9f)); DrawRect(r, new Color(Deer, .8f), false, 1); Text(x + 24, ry + 92, why, 13, Deer); }
        else Button(new Rect2(x + 14, ry + 72, w - 28, 30), "SEND THE RAID", true, M.SendRaid);
    }

    /// <summary>When everyone is down: what it cost, and on to the next. Crews carry over; aircraft are repaired.</summary>
    void RaidReport(Vector2 size)
    {
        if (!M.RaidOver || M.Camp != null && (M.ReportDone || M.Air.Formations.Count == 0)) return; var air = M.Air; var c = air.Crews; float w = 600, h = M.Camp != null ? 470 : 340, x = (size.X - w) / 2, y = (size.Y - h) / 2;
        Panel(new Rect2(x, y, w, h), Amber);
        Text(x + 16, y + 28, $"RAID {c.Raid} REPORT", 17, Amber);
        var own = air.Formations.Where(q => q.Wolf).ToList(); var foes = air.Formations.Where(q => !q.Wolf).ToList(); double ly = y + 56;
        void Line(string t, Color col) { Text(x + 16, (float)ly, t, 13, col); ly += 20; }
        Line($"Vulfen bombers lost {air.BombersLost} of {own.Sum(q => q.Start)}, {own.Count(q => q.CommanderTurned)} boxes turned back", Ink);
        Line($"UCS bombers lost {air.UcsBombersLost} of {foes.Sum(q => q.Start)}, {foes.Count(q => q.CommanderTurned)} boxes turned back", Ink);
        Line($"Hund lost {M.Sim.WolfLost}, I-8 lost {M.Sim.UcsLost}", Ink);
        if (air.Flak != null) Line($"Flak: {air.FlakLost[1]} Vulfen bombers brought down and {air.Flak.Holed[1].Count} holed, {air.Flak.Shells[1]:N0} shells; {air.FlakLost[0]} UCS down", Ink);
        Line($"Bombs: {air.Tonnes:0} tonnes in all", Ink);
        { double onCity = 0; int ruined = 0; var theirs = M.Fire.Cities.Where(q => !q.P.Wolf).ToList(); foreach (var q in theirs) { foreach (var i in air.Hits) if (Math.Abs(i.X - q.P.X) < q.P.R * 2.4 && Math.Abs(i.Y - q.P.Y) < q.P.R * 2.4) onCity += i.Kg / 1000.0; for (int m = 0; m < 3; m++) ruined += q.Count[m, 1] + q.Count[m, 2] + q.Count[m, 3]; }
          Line($"On UCS cities: {onCity:0} tonnes, {ruined:N0} buildings burning or destroyed", Amber); } ly += 8;
        Line($"Vulfen crews: {c.Dead[1]} killed, {c.Recovered[1]} recovered, {c.Prisoners[1]} prisoners, {c.LostAtSea[1]} lost at sea", new Color(.6f, .8f, 1f));
        Line($"UCS crews: {c.Dead[0]} killed, {c.Recovered[0]} recovered, {c.Prisoners[0]} prisoners, {c.LostAtSea[0]} lost at sea", new Color(1f, .65f, .55f));
        Line($"Aces flying: {M.Sim.All.Count(q => !q.Wolf && q.Ace && (q.Alive || q.Back))} UCS, {M.Sim.All.Count(q => q.Wolf && q.Ace && (q.Alive || q.Back))} Vulfen", Dim);
        if (M.Camp != null && own.Count > 0)
        {
            // operational research: what the raid did, by what means, for the next one
            ly += 8; Line("OPERATIONAL RESEARCH", Amber);
            var ours = air.Hits.Where(i => !World.North(i.X, i.Y)).ToList(); double ax = own.Average(q => q.AimX), ay = own.Average(q => q.AimY);
            double D(Impact i) => Math.Sqrt((i.X - ax) * (i.X - ax) + (i.Y - ay) * (i.Y - ay));
            if (ours.Count > 0) Line($"Accuracy: {100.0 * ours.Count(i => D(i) < 1000) / ours.Count:0}% of bombs within 1 km of the aim point, {100.0 * ours.Count(i => D(i) < 3000) / ours.Count:0}% within 3 km", Ink);
            if (ours.Count > 0) Line($"Dropped: {ours.Where(i => !i.Incendiary).Sum(i => i.Kg) / 1000.0:0} t of blast, {ours.Where(i => i.Incendiary).Sum(i => i.Kg) / 1000.0:0} t of fire", Ink);
            int flak = air.FlakLost[1], fighters = Math.Max(0, air.BombersLost - flak);
            Line($"Our bombers lost to fighters {fighters}, to flak {flak}; by type: " + string.Join(", ", own.GroupBy(q => q.Kind).Select(g => $"{Sprites.Specs[g.Key].Name.Split(' ')[0]} {g.Sum(q => q.Start) - g.Sum(q => q.Ac.Count) - air.BackHome.Count(a => a.F.Wolf && a.F.Kind == g.Key)} of {g.Sum(q => q.Start)}")), Ink);
            var t = M.Target; if (M.Fire.Records.TryGetValue(t, out var r) && !t.Wolf)
                Line($"{t.Name}: {r.Lost - r.LostAtRaid:N0} buildings lost, about {Main.Round(r.Dead - r.DeadAtRaid)} dead, {Main.Round(r.Homeless - r.HomelessAtRaid)} homeless; its works at {M.Fire.Plant(t) * 100:0}%", Amber);
        }
        if (M.Camp != null) { Button(new Rect2(x + 16, y + h - 44, 160, 30), "CARRY ON", true, M.CloseReport); Button(new Rect2(x + 186, y + h - 44, 160, 30), "PLAN A RAID", false, M.StartSetup); return; }
        Button(new Rect2(x + 16, y + h - 44, 150, 30), "NEXT RAID, DAY", false, () => M.NextRaid(8));
        Button(new Rect2(x + 176, y + h - 44, 160, 30), "NEXT RAID, NIGHT", false, () => M.NextRaid(22));
        Button(new Rect2(x + 346, y + h - 44, 160, 30), "NEW MISSION", true, M.NewMission);
    }

    static string Standing(Wing? w) => w == null ? "" : w.Wolf ? (w.Withdrawing ? "withdrawing" : w.Pressing ? "pressing" : "steady") : w.Broken ? "BROKEN" : w.Morale > .75 ? "winning" : w.Morale > .4 ? "holding" : "shaken";

    void OrderBar(Vector2 size)
    {
        var v = M.View; var f = v.Primary; if (f == null) return;
        float clear = M.Camp != null && !M.Planning ? 552 : 0, w = Math.Min(1160, size.X - clear - 150), h = 62, x = Math.Max((size.X - w) / 2, clear), y = size.Y - 30 - h - 6;
        Panel(new Rect2(x, y, w, h), Amber);
        string sp = f.Spread < .9 ? "TIGHT" : f.Spread > 1.1 ? "LOOSE" : "NORMAL";
        string who = v.Chosen.Count > 1 ? "FORMATIONS " + string.Join("+", v.Chosen.Select(c => c.Name).OrderBy(n => n)) : "FORMATION " + f.Name;
        Text(x + 12, y + 17, $"ORDERS  {who}   alt {f.TargetAlt:0} m (now {f.Lead.Z:0})   {sp}   {f.LoadKg / 1000:0.##} t, {Weight.RangeKm(Design.Of[f.Kind], f.LoadKg):0} km range: {Bombs.Describe(f.Plan)}{(v.Following != null ? "   following " + v.Following.Name : "")}", 13, Amber);
        string hint = v.Mode == 1 ? "click the map for a waypoint, right-click to finish" : v.Mode == 2 ? "click the map for the new aim point" : v.Mode == 3 ? "click one of our fields in reach (ringed green)" : v.Picked.Count > 0 ? $"{v.Picked.Count} aircraft picked" : "shift-drag on the map to pick aircraft";
        if (v.Mode != 0) Text(x + w - Width(hint, 12) - 12, y + 17, hint, 12, new Color(1f, .9f, .5f));
        float bx = x + 8, by = y + 28;
        void B(string t, float bw, bool act, Action a) { Button(new Rect2(bx, by, bw, 26), t, act, a); bx += bw + 5; }
        B("ROUTE", 64, v.Mode == 1, () => v.Mode = v.Mode == 1 ? 0 : 1);
        B("AIM", 48, v.Mode == 2, () => v.Mode = v.Mode == 2 ? 0 : 2);
        B("ALT +", 56, false, () => v.Each(c => Air.ChangeAltitude(c, 500)));
        B("ALT -", 56, false, () => v.Each(c => Air.ChangeAltitude(c, -500)));
        B("TIGHT", 58, f.Spread < .9, () => v.Each(c => Air.SetSpacing(c, .65)));
        B("NORMAL", 66, f.Spread >= .9 && f.Spread <= 1.1, () => v.Each(c => Air.SetSpacing(c, 1)));
        B("LOOSE", 58, f.Spread > 1.1, () => v.Each(c => Air.SetSpacing(c, 1.5)));
        B("BOMB NOW", 92, f.ForceRelease, () => v.Each(Air.BombNow));
        B("ABORT", 68, f.Aborted, () => v.Each(Air.Abort));
        if (M.Forces != null) B("ABORT TO", 90, v.Mode == 3, () => v.Mode = v.Mode == 3 ? 0 : 3);
        if (v.Picked.Count > 0) { var r = new Rect2(bx, by, 150, 26); Button(r, $"NEW FORMATION ({v.Picked.Count})", false, v.NewFormation); Named["NEW"] = r; }
    }
}
