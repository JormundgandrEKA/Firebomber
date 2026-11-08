using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Firebomber;

/// <summary>What opens where you clicked: the city pop-up with its little map, the airfield pop-up with its range ring, the card by the mouse for any aircraft type, and the aircraft menu.</summary>
public partial class ConsoleUi
{
    // ---------------------------------------------------------------- the city
    sealed class CityMap { public ImageTexture Tex = null!; public Image Img = null!; public byte[] Px = null!; public int Stamp = -1; public long At; }
    readonly Dictionary<Place, CityMap> cityMaps = new();
    const int MapPx = 300;

    /// <summary>The city from above, one pixel to every few buildings: wood, brick and works in their colours, then what is burning or gone drawn over them. Remade when the city changes, and at most once a second.</summary>
    Texture2D MapOf(Place c, CityFire? fire, int stamp)
    {
        if (!cityMaps.TryGetValue(c, out var m)) cityMaps[c] = m = new CityMap { Px = new byte[MapPx * MapPx * 4] };
        long now = System.Environment.TickCount64; if (m.Tex != null && (m.Stamp == stamp || now - m.At < 1000)) return m.Tex;
        m.Stamp = stamp; m.At = now; var px = m.Px;
        for (int i = 0; i < px.Length; i += 4) { px[i] = 26; px[i + 1] = 34; px[i + 2] = 24; px[i + 3] = 255; }
        double ext = c.R * 2.4; if (c.Layout != null && c.Layout.Plots.Length > 0) ext = c.Layout.Plots.Max(p => Math.Max(Math.Abs(p.X - c.X), Math.Abs(p.Y - c.Y))) * 1.04;
        void Dot(double x, double y, byte r, byte g, byte b, int size)
        {
            int ix = (int)((x / ext + 1) / 2 * MapPx), iy = (int)((1 - y / ext) / 2 * MapPx);
            for (int v = iy; v < iy + size; v++) for (int u = ix; u < ix + size; u++) { if (u < 0 || v < 0 || u >= MapPx || v >= MapPx) continue; int k = (v * MapPx + u) * 4; px[k] = r; px[k + 1] = g; px[k + 2] = b; }
        }
        static (byte, byte, byte) Mat(byte m) => m == CityPlan.Factory ? ((byte)120, (byte)150, (byte)190) : m == CityPlan.Brick ? ((byte)165, (byte)95, (byte)75) : ((byte)190, (byte)165, (byte)115);
        if (fire != null)
        {
            foreach (ref var b in fire.B.AsSpan()) if (b.State == FireSim.Intact) { var (r, g, bl) = Mat(b.Mat); Dot(b.X, b.Y, r, g, bl, b.Mat == CityPlan.Factory ? 2 : 1); }
            foreach (ref var b in fire.B.AsSpan()) if (b.State != FireSim.Intact) { if (b.State == FireSim.Burning) Dot(b.X, b.Y, 255, 140, 30, 2); else if (b.State == FireSim.Gutted) Dot(b.X, b.Y, 14, 12, 12, 2); else Dot(b.X, b.Y, 95, 95, 95, 2); }
        }
        else if (c.Layout != null) foreach (var p in c.Layout.Plots) { var (r, g, bl) = Mat(p.Mat); Dot(p.X - c.X, p.Y - c.Y, r, g, bl, p.Mat == CityPlan.Factory ? 2 : 1); }
        if (m.Tex == null) { m.Img = Image.CreateFromData(MapPx, MapPx, false, Image.Format.Rgba8, px); m.Tex = ImageTexture.CreateFromImage(m.Img); }
        else { m.Img.SetData(MapPx, MapPx, false, Image.Format.Rgba8, px); m.Tex.Update(m.Img); }
        return m.Tex;
    }

    /// <summary>A city, where it is on the map: a little map of it, how much is left, what has been done to it, and how that has gone over time.</summary>
    void CityPopup()
    {
        var c = M.Selected; if (c == null) return;
        var fire = M.Fire.Cities.FirstOrDefault(q => q.P == c); M.Fire.Records.TryGetValue(c, out var rec); bool hasCharts = M.Forces != null;
        float w = hasCharts ? 610 : 420, h = 244; var anchor = M.W2S(c.X, c.Y); var r0 = PopRect(anchor, new Vector2(w, h)); Color side = c.Wolf ? Wolf : Deer;
        Leader(anchor, r0, side); Panel(r0, side); float x = r0.Position.X, y = r0.Position.Y;
        float pw = Plate(x + 10, y + 8, c.Name.ToUpperInvariant(), side, 15);
        int guns = M.Air.Flak?.Batteries.Where(q => q.City == c).Sum(q => q.Guns) ?? 0;
        Text(x + 18 + pw, y + 23, (c.Wolf ? "Vulfen Directorate, " : "Union of Committee States, ") + World.SizeName(c) + (guns > 0 ? $", {guns} heavy guns" : ", no heavy guns"), 12, Dim);
        Button(new Rect2(x + w - 62, y + 8, 52, 22), "CLOSE", false, () => M.Selected = null);
        // the map, and what to do about it
        var map = new Rect2(x + 10, y + 38, 160, 160); Bezel(map);
        int stamp = (rec?.Lost ?? 0) * 3 + (rec?.Rebuilt ?? 0) + (fire?.Burning.Count ?? 0) * 7919;
        if (c.Layout != null || fire != null) DrawTextureRect(MapOf(c, fire, stamp), map.Grow(-1), false);
        else Text(x + 40, y + 120, "not surveyed", 12, Dim);
        Button(new Rect2(x + 10, y + 206, 56, 26), "GO TO", false, () => M.SelectCity(c, true));
        if (M.View.Chosen.Count > 0) Button(new Rect2(x + 72, y + 206, 98, 26), "AIM HERE", false, () => M.View.Each(f => M.Air.SetAim(f, c.X, c.Y)));
        // how much is left
        float mx = x + 180, mw = 226; var (alive, standing, plant) = History.Now(c, M.Fire); int pop = FireSim.PopulationOf(c);
        string Pc(double v) => c.Wolf ? $"{v * 100:0}%" : $"~{Math.Round(v * 20) * 5:0}%";
        Bar(new Rect2(mx, y + 38, mw, 16), alive / Math.Max(1f, pop), new Color(.55f, .8f, .95f), "Population", Main.Round(alive), $"{alive:N0} of {pop:N0}");
        Bar(new Rect2(mx, y + 58, mw, 16), standing, new Color(.8f, .7f, .45f), "Buildings", Pc(standing), $"{standing * 100:0.0}%" + (rec != null && c.Layout != null ? $", {c.Layout.Plots.Length - (rec.Lost - rec.Rebuilt):N0} of {c.Layout.Plots.Length:N0}" : ""));
        Bar(new Rect2(mx, y + 78, mw, 16), plant, plant < .9 ? Deer : Green, "Industry", Pc(plant), $"{plant * 100:0.0}%");
        var q0 = M.Forces?.Ind.Of(c); if (q0 != null && c.Wolf) Text(mx, y + 110, $"Making {Industry.Short(q0.Making)}, labour {M.Forces!.War.Labour(c, M.Fire) * 100:0}%", 12, Dim);
        // what has been done to it
        var box = new Rect2(mx, y + 118, mw, 114); Bezel(box); Plate(mx + 4, y + 122, "LOSSES AND RAIDS", side, 11); float ly = y + 154;
        void L(string t, Color col) { Text(mx + 8, ly, t, 12, col); ly += 15; }
        if (rec == null || rec.Raids == 0) L("Not yet bombed.", Dim);
        else
        {
            L($"{rec.Raids} raid{(rec.Raids > 1 ? "s" : "")}, {rec.Tonnes:N0} t" + (rec.Firestorms > 0 ? $", {rec.Firestorms} firestorm{(rec.Firestorms > 1 ? "s" : "")}" : ""), Ink);
            L($"Dead {Main.Round(rec.Dead)}, injured {Main.Round(rec.Injured)}", c.Wolf ? Deer : Amber);
            L($"Homeless now {Main.Round(rec.Homeless)}", c.Wolf ? Deer : Amber);
            L($"Buildings lost {rec.Lost:N0}, rebuilt {rec.Rebuilt:N0}", Dim);
        }
        if (fire != null && fire.Storm) L("FIRESTORM", new Color(1f, .5f, .2f)); else if (fire != null && fire.Burning.Count > 0) L($"Burning: {fire.Burning.Count:N0} buildings", new Color(1f, .6f, .25f));
        // how it has gone
        if (!hasCharts) return;
        var hist = M.Forces!.Hist.City.GetValueOrDefault(c.Name) ?? new List<CitySample>(); float cx = x + 416, cw = w - 426; float Day(double t) => (float)(t / Campaign.Day);
        Chart(new Rect2(cx, y + 38, cw, 94), "POPULATION", Dim, null, (hist.Select(s => new Vector2(Day(s.T), s.Alive)).ToList(), new Color(.55f, .8f, .95f), "people"));
        Chart(new Rect2(cx, y + 138, cw, 94), "STANDING %", Dim, 100, (hist.Select(s => new Vector2(Day(s.T), s.Standing * 100)).ToList(), new Color(.8f, .7f, .45f), "bldg"), (hist.Select(s => new Vector2(Day(s.T), s.Works * 100)).ToList(), Green, "works"));
    }

    // ---------------------------------------------------------------- pop-ups for one aircraft: they stay where they first opened, with a line to the aircraft
    object? popFor; Vector2 popAt;
    Rect2 AircraftPop(object who, Vector2 anchor, Vector2 size, Color c)
    {
        if (popFor != who) { popFor = who; popAt = PopRect(anchor, size).Position; }
        var view = GetViewportRect().Size; var p = new Vector2(Math.Clamp(popAt.X, 6, view.X - size.X - 6), Math.Clamp(popAt.Y, 42, view.Y - size.Y - 36)); var r = new Rect2(p, size);
        Leader(anchor, r, c); return r;
    }

    // ---------------------------------------------------------------- the airfield
    Base? fieldOpen; Craft ringKind = Craft.F5; int ringLoad;
    /// <summary>Opens an airfield where it is on the map.</summary>
    public void OpenField(Base b)
    {
        fieldOpen = b; ringLoad = 0; var types = b.Wolf ? Forces.WolfTypes : Forces.UcsTypes;
        ringKind = types.Where(k => b.S(k).Ready > 0).OrderBy(k => Design.Of[k].IsFighter).DefaultIfEmpty(types[^1]).First();
    }
    public void PickLoad(Craft k, int load) { ringKind = k; ringLoad = load; }
    public Base? FieldOpen => fieldOpen;
    static double LoadKg(Craft k, int idx) => Loadouts.Of.TryGetValue(k, out var l) && l.Length > 0 ? l[((idx % l.Length) + l.Length) % l.Length].Kg : 0;

    /// <summary>How far aircraft from this field can go: the solid ring is out and back, the faint one a single flight.</summary>
    void RangeRing(Place field, Craft k, double kg, Color c, bool label)
    {
        double km = Weight.RangeKm(Design.Of[k], kg); var p = M.W2S(field.X, field.Y); float one = (float)(km * 1000 * M.Zoom), back = one / 2;
        DrawArc(p, back, 0, Mathf.Tau, 160, new Color(c, .9f), 2); DrawArc(p, one, 0, Mathf.Tau, 200, new Color(c, .35f), 1.5f);
        if (!label) return;
        void Tag(float r, string t, float alpha) { var at = p + new Vector2(-Width(t, 13) / 2, -r - 5); DrawStringOutline(font, at, t, HorizontalAlignment.Left, -1, 13, 3, new Color(.04f, .05f, .03f, .85f)); DrawString(font, at, t, HorizontalAlignment.Left, -1, 13, new Color(c, alpha)); }
        Tag(back, $"out and back, {km / 2:N0} km", 1); Tag(one, $"one way, {km:N0} km", .75f);
    }

    /// <summary>Our patrols on the map: a ring at the station, bright on its shift, with fighters going round it.</summary>
    void Patrols()
    {
        double hour = M.Camp!.Hour;
        foreach (var b in M.Forces!.Side(true))
            foreach (var p in b.Patrols)
            {
                var at = M.W2S(p.X, p.Y); bool up = p.Up(hour); var c = new Color(Wolf, up ? .95f : .35f); DrawArc(at, 16, 0, Mathf.Tau, 28, c, 1.5f); var home = M.W2S(b.Field.X, b.Field.Y); DrawDashedLine(home, at, new Color(Wolf, .3f), 1, 6);
                if (up) for (int i = 0; i < Math.Min(4, p.N / 3 + 1); i++) { float a = (float)(M.Camp.Now / 600 + i * 1.57); Sprites.Card(this, p.Kind, at + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 16, 10, Colors.White); }
                string t = $"{p.N} {Sprites.Specs[p.Kind].Name.Split(' ')[0]}"; var l = at + new Vector2(20, 4); DrawStringOutline(font, l, t, HorizontalAlignment.Left, -1, 12, 3, new Color(.04f, .05f, .03f, .85f)); DrawString(font, l, t, HorizontalAlignment.Left, -1, 12, c);
            }
    }
    /// <summary>Ferries of ours in the air, moving along their line.</summary>
    void Ferries()
    {
        double now = M.Camp!.Now;
        foreach (var q in M.Forces!.Flights)
        {
            if (!q.From.Wolf || now >= q.Land) continue; var a = M.W2S(q.From.X, q.From.Y); var b = M.W2S(q.To.X, q.To.Y); float k = (float)Math.Clamp((now - q.T0) / Math.Max(1, q.Land - q.T0), 0, 1); var at = a.Lerp(b, k);
            DrawDashedLine(a, b, new Color(Wolf, .45f), 1, 6); Sprites.Card(this, q.Kind, at, 16, Colors.White);
            string t = $"{q.N} {Sprites.Specs[q.Kind].Name.Split(' ')[0]}"; var l = at + new Vector2(12, 4); DrawStringOutline(font, l, t, HorizontalAlignment.Left, -1, 12, 3, new Color(.04f, .05f, .03f, .85f)); DrawString(font, l, t, HorizontalAlignment.Left, -1, 12, new Color(.75f, .88f, 1f));
        }
    }
    /// <summary>For the formation chosen, in flight: how far its fuel will take it, and which of our fields are in reach.</summary>
    void InFlight()
    {
        var f = M.View.Primary; if (f == null || !f.Wolf || f.Phase is not (Phase.Outbound or Phase.Egress or Phase.Returning) || f.Ac.Count == 0) return;
        double km = Air.FuelKm(f); var p = M.W2S(f.Lead.X, f.Lead.Y); float rad = (float)(km * 1000 * M.Zoom); DrawArc(p, rad, 0, Mathf.Tau, 200, new Color(Amber, .7f), 1.5f);
        string t = $"fuel for {km:N0} km"; var l = p + new Vector2(-Width(t, 13) / 2, -rad - 5); DrawStringOutline(font, l, t, HorizontalAlignment.Left, -1, 13, 3, new Color(.04f, .05f, .03f, .85f)); DrawString(font, l, t, HorizontalAlignment.Left, -1, 13, Amber);
        foreach (var q in M.World.Fields) if (Math.Sqrt(Math.Pow(q.X - f.Lead.X, 2) + Math.Pow(q.Y - f.Lead.Y, 2)) / 1000 <= km) DrawArc(M.W2S(q.X, q.Y), 11, 0, Mathf.Tau, 20, new Color(Green, .9f), 2);
    }

    /// <summary>On the map, under everything else on the console: range rings for the open or hovered field, and the suspected target of a warned raid.</summary>
    void MapMarks()
    {
        if (M.Forces == null || M.Camp == null) return;
        if (fieldOpen != null) RangeRing(fieldOpen.Field, ringKind, LoadKg(ringKind, ringLoad), fieldOpen.Wolf ? Wolf : Deer, true);
        else if (!M.Holding && !Covers(Mouse))
            foreach (var f in M.World.Fields.Concat(M.World.EnemyFields))
            {
                if ((M.W2S(f.X, f.Y) - Mouse).Length() >= 9) continue;
                var b = M.Forces.Of(f); var types = f.Wolf ? Forces.WolfTypes : Forces.UcsTypes; var k = types.Where(q => b.S(q).Ready > 0).OrderBy(q => Design.Of[q].IsFighter).DefaultIfEmpty(types[^1]).First();
                RangeRing(f, k, Design.Of[k].UsualKg, f.Wolf ? Wolf : Deer, true); break;
            }
        Patrols(); Ferries(); InFlight();
        var pl = M.Forces.Planner;
        if (pl.WarnTarget != "" && M.Camp.Now < pl.WarnUntil)
        {
            var c = M.World.City(pl.WarnTarget); var p = M.W2S(c.X, c.Y); float pulse = 22 + 4 * (float)Math.Sin(Time.GetTicksMsec() / 250.0);
            DrawArc(p, pulse, 0, Mathf.Tau, 40, new Color(Deer, .95f), 2.5f); string t = $"SUSPECTED TARGET, ABOUT {pl.WarnN} BOMBERS"; var at = p + new Vector2(-Width(t, 13) / 2, -pulse - 6);
            DrawStringOutline(font, at, t, HorizontalAlignment.Left, -1, 13, 3, new Color(.04f, .05f, .03f, .9f)); DrawString(font, at, t, HorizontalAlignment.Left, -1, 13, Deer);
        }
    }

    public PickMode Pick; Base? pickFrom; int sendN = 12; string fieldMsg = "";
    public bool CancelPick() { if (Pick == PickMode.None) return false; Pick = PickMode.None; return true; }

    /// <summary>A click on the map while a field is waiting to be told where.</summary>
    public bool MapPicked(Vector2 p)
    {
        var b = pickFrom; var forces = M.Forces; var mode = Pick; Pick = PickMode.None; if (b == null || forces == null || M.Camp == null) return true;
        double wx = M.S2Wx(p.X), wy = M.S2Wy(p.Y);
        if (mode == PickMode.Patrol)
        {
            double km = Math.Sqrt(Math.Pow(wx - b.Field.X, 2) + Math.Pow(wy - b.Field.Y, 2)) / 1000, reach = Weight.RangeKm(Design.Of[Craft.F5], 0) / 2 * .8;
            if (km > reach) { fieldMsg = $"Too far: {km:N0} km, and a patrol can go {reach:N0} km"; return true; }
            b.Patrols.Add(new Patrol { Kind = Craft.F5, N = Math.Min(12, Math.Max(2, b.S(Craft.F5).Ready)), X = wx, Y = wy, Hours = 2 }); fieldMsg = "";
        }
        else if (mode == PickMode.FerryTo)
        {
            var to = forces.Side(true).FirstOrDefault(q => q != b && (M.W2S(q.Field.X, q.Field.Y) - p).Length() < 14); if (to == null) { fieldMsg = "Click one of our other fields"; return true; }
            var why = forces.Ferry(b, to, ringKind, sendN, M.Camp.Now); fieldMsg = why ?? $"{sendN} {Sprites.Specs[ringKind].Name.Split(' ')[0]} on their way to {to.Name}";
            if (why == null) M.Camp.Note($"Ferrying {sendN} {Sprites.Specs[ringKind].Name.Split(' ')[0]} from {b.Name} to {to.Name}.");
        }
        else if (mode == PickMode.BombAt)
        {
            var c = M.World.Cities.Where(q => !q.Wolf).OrderBy(q => (M.W2S(q.X, q.Y) - p).Length()).First(); if ((M.W2S(c.X, c.Y) - p).Length() > 30) { fieldMsg = "Click a UCS city"; return true; }
            fieldOpen = null; M.RaidFrom(b, ringKind, Math.Min(sendN, b.S(ringKind).Ready), c);
        }
        return true;
    }

    /// <summary>An airfield, where it is on the map: what it holds, how far it reaches with the load chosen, sending aircraft from it, and its fighters' standing orders.</summary>
    void FieldPopup()
    {
        var b = fieldOpen; var forces = M.Forces; if (b == null || forces == null) return;
        var types = b.Wolf ? Forces.WolfTypes : Forces.UcsTypes; const float w = 400; float rowH = 34, h = 40 + types.Length * rowH + (b.Wolf ? 36 + 36 + 62 + b.Patrols.Count * 28 + 36 + (fieldMsg != "" ? 20 : 0) : 72);
        var anchor = M.W2S(b.Field.X, b.Field.Y); var r = PopRect(anchor, new Vector2(w, h)); Color side = b.Wolf ? Wolf : Deer;
        Leader(anchor, r, side); Panel(r, side); float x = r.Position.X, y = r.Position.Y;
        float pw = Plate(x + 10, y + 8, b.Field.Name.ToUpperInvariant(), side, 15); var fs = forces.Sites.Of(b.Field); Text(x + 18 + pw, y + 23, fs.Closed ? $"RUNWAY CLOSED, {M.Camp!.Stamp(fs.Parts.Where(q => q.Kind == PartKind.Runway && q.Down).Max(q => q.BackAt))}" : fs.Lost > 0 ? $"{fs.Lost} parts damaged" : b.Wolf ? $"room for {b.Capacity - b.Total}" : "estimates", 12, fs.Closed ? Deer : Dim);
        Button(new Rect2(x + w - 62, y + 8, 52, 22), "CLOSE", false, () => { fieldOpen = null; CancelPick(); });
        float ry = y + 38;
        foreach (var k in types)
        {
            var row = new Rect2(x + 8, ry, w - 16, rowH - 3); bool sel = ringKind == k; var s = b.S(k); var kk = k;
            DrawRect(row, sel ? new Color(.2f, .34f, .18f, .9f) : new Color(.07f, .12f, .07f, .85f));
            Sprites.Card(this, k, row.Position + new Vector2(30, 15), 26, Colors.White);
            Text(x + 66, ry + 20, Sprites.Specs[k].Name, 14, sel ? Amber : Ink);
            Text(x + 220, ry + 20, b.Wolf ? (s.ComingN > 0 ? $"{s.Ready} ready, {s.ComingN} back soon" : $"{s.Ready} ready") : s.Ready == 0 ? "-" : $"~{Forces.About(s.Ready)}", 13, Ink);
            buttons.Add((row, () => { ringKind = kk; ringLoad = 0; })); Named["field " + Sprites.Specs[k].Name.Split(' ')[0]] = row; HoverIf(row, k); ry += rowH;
        }
        ry += 4;
        if (Loadouts.Of.TryGetValue(ringKind, out var loads) && loads.Length > 0)
        {
            var l = loads[((ringLoad % loads.Length) + loads.Length) % loads.Length];
            Button(new Rect2(x + 10, ry, 24, 24), "<", false, () => ringLoad--); Button(new Rect2(x + 40, ry, 24, 24), ">", false, () => ringLoad++);
            Text(x + 72, ry + 17, $"{l.Name}, {l.Kg / 1000:0.##} t: reach {Weight.RangeKm(Design.Of[ringKind], l.Kg) / 2:N0} km", 13, Ink);
        }
        else Text(x + 12, ry + 17, $"Reach {Weight.RangeKm(Design.Of[ringKind], 0) / 2:N0} km out and back", 13, Ink);
        ry += 32;
        if (!b.Wolf) { if (M.OpOver) Button(new Rect2(x + w - 150, ry, 140, 24), "BOMB THIS FIELD", false, () => { var ss = forces.Sites.Of(b.Field); fieldOpen = null; M.RaidAtSite(ss); }); return; }
        // sending aircraft
        var bb = b; float bx = x + 10;
        Button(new Rect2(bx, ry, 24, 24), "-", false, () => sendN = Math.Max(2, sendN - 6)); Text(bx + 30, ry + 17, sendN.ToString(), 14, Amber); bx += 58;
        Button(new Rect2(bx, ry, 24, 24), "+", false, () => sendN = Math.Min(120, sendN + 6)); bx += 34;
        Button(new Rect2(bx, ry, 104, 24), "FERRY TO...", Pick == PickMode.FerryTo, () => { Pick = Pick == PickMode.FerryTo ? PickMode.None : PickMode.FerryTo; pickFrom = bb; fieldMsg = "Click the field to send them to"; }); Named["FERRY TO"] = new Rect2(bx, ry, 104, 24); bx += 110;
        if (!Design.Of[ringKind].IsFighter && M.OpOver) Button(new Rect2(bx, ry, 80, 24), "BOMB...", Pick == PickMode.BombAt, () => { Pick = Pick == PickMode.BombAt ? PickMode.None : PickMode.BombAt; pickFrom = bb; fieldMsg = "Click the UCS city to bomb"; });
        ry += 34;
        // the fighters' standing orders
        Plate(x + 10, ry, "FIGHTERS", side, 12); bx = x + 96;
        Button(new Rect2(bx, ry - 1, 66, 22), "ALERT", !b.StandDown, () => bb.StandDown = false); bx += 70;
        Button(new Rect2(bx, ry - 1, 104, 22), "STAND DOWN", b.StandDown, () => bb.StandDown = true); ry += 28;
        Text(x + 12, ry + 15, "Intercept:", 12, Dim); bx = x + 96;
        Button(new Rect2(bx, ry - 1, 142, 22), "STRAIGHT AT THEM", b.Intercept == InterceptMode.Direct, () => bb.Intercept = InterceptMode.Direct, 12); bx += 146;
        Button(new Rect2(bx, ry - 1, 146, 22), "WAIT ON THEIR TRACK", b.Intercept == InterceptMode.OnTrack, () => bb.Intercept = InterceptMode.OnTrack, 12); ry += 30;
        foreach (var p in b.Patrols.ToList())
        {
            var pp = p; bx = x + 10; double km = Math.Sqrt(Math.Pow(p.X - b.Field.X, 2) + Math.Pow(p.Y - b.Field.Y, 2)) / 1000;
            Text(bx, ry + 16, $"Patrol {km:N0} km out: {p.N} {Sprites.Specs[p.Kind].Name.Split(' ')[0]}", 12, Ink); bx += 190;
            Button(new Rect2(bx, ry, 22, 22), "-", false, () => pp.N = Math.Max(2, pp.N - 2)); bx += 26;
            Button(new Rect2(bx, ry, 22, 22), "+", false, () => pp.N = Math.Min(48, pp.N + 2)); bx += 28;
            Button(new Rect2(bx, ry, 60, 22), p.HoursText, false, () => pp.Hours = (pp.Hours + 1) % 3, 12); bx += 64;
            Button(new Rect2(bx, ry, 24, 22), "X", false, () => bb.Patrols.Remove(pp)); ry += 28;
        }
        Button(new Rect2(x + 10, ry, 100, 24), "+ PATROL", Pick == PickMode.Patrol, () => { Pick = Pick == PickMode.Patrol ? PickMode.None : PickMode.Patrol; pickFrom = bb; fieldMsg = "Click the map where they should patrol"; });
        Button(new Rect2(x + 116, ry, 92, 24), "BASES...", false, () => { fieldOpen = null; OpenBase(bb); });
        ry += 28; if (fieldMsg != "") Text(x + 12, ry + 13, fieldMsg, 12, Pick != PickMode.None ? Amber : fieldMsg.Contains("way") ? Green : Deer);
    }

    Site? siteOpen;
    public void OpenSite(Site s) => siteOpen = s;
    /// <summary>A depot or a radar station: what state it is in and, if it is the enemy's, the means to bomb it.</summary>
    void SitePopup()
    {
        var st = siteOpen; if (st == null || M.Forces == null) return; const float w = 340, h = 128; var anchor = M.W2S(st.X, st.Y); var r = PopRect(anchor, new Vector2(w, h)); Color side = st.Wolf ? Wolf : Deer;
        Leader(anchor, r, side); Panel(r, side); float x = r.Position.X, y = r.Position.Y; Plate(x + 10, y + 8, st.Name.ToUpperInvariant(), side, 14); Button(new Rect2(x + w - 62, y + 8, 52, 22), "CLOSE", false, () => siteOpen = null);
        Bar(new Rect2(x + 12, y + 42, w - 24, 16), st.Condition, st.Condition < .7 ? Deer : Green, "Condition", $"{st.Condition * 100:0}%", $"{st.Parts.Count - st.Lost} of {st.Parts.Count} parts standing");
        var ind = M.Forces.Ind; int s0 = st.Wolf ? 1 : 0; double share = 1.0 / M.Forces.Sites.DepotCount(st.Wolf);
        string line = st.Kind == SiteKind.Depot ? (st.Wolf ? $"Holds about {(ind.He[s0] + ind.Inc[s0]) * share:N0} t of bombs and {ind.Fuel[s0] * share:N0} t of fuel" : "Holds a share of the UCS's bombs and fuel") : st.Working ? $"On the air: watches {Sites.RadarKm:0} km" : "Off the air: a gap in the chain";
        Text(x + 12, y + 80, line, 12, Ink); if (st.Lost > 0) Text(x + 12, y + 97, $"Mended by {M.Camp!.Stamp(st.ReadyAt)}", 12, Dim);
        if (!st.Wolf && M.OpOver) Button(new Rect2(x + w - 100, y + h - 34, 90, 26), "BOMB IT", false, () => { var q = st; siteOpen = null; M.RaidAtSite(q); });
    }

    // ---------------------------------------------------------------- aircraft cards and the menu
    Craft? hoverKind;
    void HoverIf(Rect2 r, Craft k) { if (r.HasPoint(Mouse)) hoverKind = k; }
    static string Armament(Design d)
    {
        var bits = new List<string>();
        if (d.Fixed != null) bits.Add(d.Fixed.Name.Contains(" and ") ? d.Fixed.Name : $"{d.FixedN} x {d.Fixed.Name}");
        foreach (var g in d.Turrets.GroupBy(t => t.W.Name)) bits.Add($"{g.Count()} turret{(g.Count() > 1 ? "s" : "")}, {g.Sum(t => t.Guns)} x {g.Key}");
        return bits.Count == 0 ? "none" : string.Join("; ", bits);
    }

    /// <summary>The card by the mouse for whatever aircraft type is under it, in a panel or on the map.</summary>
    void AircraftCard(Vector2 size)
    {
        var m = Mouse; var k = hoverKind;
        if (k == null && !M.Holding && !Covers(m))
        {
            var f = M.Sim.At(M.FView.Screen, m, 10); if (f != null) k = f.Kind; else { var b = M.View.BomberAt(m); if (b != null) k = b.F.Kind; }
        }
        if (k == null) return;
        var d = Design.Of[k.Value]; var side = d.Wolf ? Wolf : Deer; const float w = 400; var lines = Wrap("Armament: " + Armament(d), 12, w - 24); float h = 124 + lines.Count * 15;
        float x = m.X + 18, y = m.Y + 18; if (x + w > size.X - 6) x = m.X - w - 18; if (y + h > size.Y - 34) y = m.Y - h - 18;
        var r = new Rect2(Math.Max(6, x), Math.Max(38, y), w, h); Panel(r, side); Panels.RemoveAt(Panels.Count - 1);
        x = r.Position.X; y = r.Position.Y; float pw = Plate(x + 10, y + 8, d.Name, side, 14); Text(x + 18 + pw, y + 22, d.Wolf ? "Vulfen Directorate" : "UCS", 12, Dim);
        Sprites.Card(this, k.Value, new Vector2(x + 340, y + 68), 66, Colors.White);
        float ly = y + 50; void L(string t) { Text(x + 12, ly, t, 13, Ink); ly += 18; }
        L($"Speed: {d.Cruise * 3.6:0} km/h cruise, {d.Max * 3.6:0} top");
        L($"Ceiling: {d.Ceil:N0} m   Crew: {Math.Max(1, d.Crew.Length)}");
        L(d.MaxKg > 0 ? $"Payload: {d.MaxKg / 1000:0.#} t most, {d.UsualKg / 1000:0.#} t usual" : "Payload: none");
        L($"Range: {Weight.RangeKm(d, d.UsualKg):N0} km with the usual load");
        ly -= 3; foreach (var l in lines) { Text(x + 12, ly, l, 12, Dim); ly += 15; }
    }

    public bool ShowAircraft;
    /// <summary>Every type in the war and what it can do.</summary>
    void AircraftPanel(Vector2 size)
    {
        if (!ShowAircraft) return; var kinds = Forces.WolfTypes.Concat(Forces.UcsTypes).ToArray();
        const float x = 8, y = 42, w = 940, rowH = 58; float h = 58 + kinds.Length * rowH; Panel(new Rect2(x, y, w, h), Amber);
        Plate(x + 10, y + 8, "AIRCRAFT", Amber, 15); Button(new Rect2(x + w - 74, y + 8, 64, 22), "CLOSE", false, () => ShowAircraft = false);
        float[] cx = { x + 100, x + 268, x + 374, x + 450, x + 524, x + 600 }; string[] head = { "Type", "Speed km/h", "Ceiling", "Payload", "Range", "Armament" };
        for (int i = 0; i < head.Length; i++) Text(cx[i], y + 50, head[i], 12, Dim);
        float ry = y + 56;
        foreach (var k in kinds)
        {
            var d = Design.Of[k]; var row = new Rect2(x + 8, ry, w - 16, rowH - 3); DrawRect(row, new Color(.07f, .12f, .07f, .85f)); DrawRect(new Rect2(row.Position, new Vector2(4, row.Size.Y)), d.Wolf ? Wolf : Deer);
            Sprites.Card(this, k, row.Position + new Vector2(48, 27), 58, Colors.White);
            Text(cx[0], ry + 24, d.Name, 14, Ink); Text(cx[0], ry + 42, d.Strike ? "strike bomber" : d.IsFighter ? "fighter" : "heavy bomber", 12, Dim);
            Text(cx[1], ry + 24, $"{d.Cruise * 3.6:0} / {d.Max * 3.6:0}", 13, Ink); Text(cx[2], ry + 24, $"{d.Ceil:N0} m", 13, Ink);
            Text(cx[3], ry + 24, d.MaxKg > 0 ? $"{d.MaxKg / 1000:0.#} t" : "-", 13, Ink); Text(cx[4], ry + 24, $"{Weight.RangeKm(d, d.UsualKg):N0} km", 13, Ink);
            float ay = ry + 18; foreach (var l in Wrap(Armament(d), 12, w - (cx[5] - x) - 14)) { Text(cx[5], ay, l, 12, Dim); ay += 15; }
            HoverIf(row, k); ry += rowH;
        }
    }
}
