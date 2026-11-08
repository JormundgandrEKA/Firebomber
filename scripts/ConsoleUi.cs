using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Firebomber;

/// <summary>The console skin: scanline panels with corner brackets, top bar with clock and speeds, the theatre panel, the city panel, the scale bar and the help bar.
/// It only draws and takes clicks; it knows nothing about how the world is made.</summary>
public partial class ConsoleUi : Node2D
{
    public static readonly Color Green = new(.55f, .85f, .45f), Amber = new(.95f, .75f, .3f), Ink = new(.88f, .9f, .78f), Dim = new(.7f, .8f, .6f), Wolf = new(.37f, .69f, 1f), Deer = new(1f, .42f, .35f);
    public Main M = null!;
    /// <summary>The rectangles the console covers, so map labels keep clear of them and clicks go to them.</summary>
    public readonly List<Rect2> Panels = new();
    sealed class ButtonList : List<(Rect2 r, Action act, int layer)> { public Func<int> Layer = () => 0; public void Add((Rect2 r, Action act) b) => base.Add((b.r, b.act, Layer())); }
    readonly ButtonList buttons = new();
    bool collapsed = true;
    /// <summary>Where each named control was last drawn: for the self-test.</summary>
    public readonly Dictionary<string, Rect2> Named = new();
    Font font = null!;

    public bool Collapsed { get => collapsed; set => collapsed = value; }
    public override void _Ready() { buttons.Layer = () => Panels.Count; TextureFilter = TextureFilterEnum.Linear; font = ThemeDB.FallbackFont; }
    public override void _Process(double delta) => QueueRedraw();

    public bool Covers(Vector2 p) => Panels.Any(r => r.HasPoint(p));
    public bool Click(Vector2 p)
    {
        // the last drawn is on top: a button under a later panel cannot be pressed through it
        for (int i = buttons.Count - 1; i >= 0; i--)
        {
            var (r, a, layer) = buttons[i]; if (!r.HasPoint(p)) continue;
            bool under = false; for (int j = layer; j < Panels.Count; j++) if (Panels[j].HasPoint(p)) under = true;
            if (under) continue;
            a(); return true;
        }
        return Covers(p);
    }

    void Text(float x, float y, string t, int fs, Color c) => DrawString(font, new Vector2(x, y), t, HorizontalAlignment.Left, -1, fs, c);
    float Width(string t, int fs) => font.GetStringSize(t, HorizontalAlignment.Left, -1, fs).X;

    void Panel(Rect2 r, Color accent, bool brackets = true)
    {
        Panels.Add(r);
        DrawRect(r.Grow(2), new Color(.01f, .02f, .01f, .85f));
        DrawRect(r, new Color(.03f, .055f, .03f, .9f));
        for (float y = r.Position.Y + 2; y < r.End.Y; y += 3) DrawLine(new Vector2(r.Position.X, y), new Vector2(r.End.X, y), new Color(0, 0, 0, .22f), 1);
        DrawLine(r.Position, new Vector2(r.End.X, r.Position.Y), new Color(1, 1, 1, .1f), 1);
        DrawRect(r, new Color(accent, .55f), false, 1);
        if (!brackets) return;
        if (r.Size.X > 200 && r.Size.Y > 60) foreach (var p in new[] { r.Position + new Vector2(6, 6), new Vector2(r.End.X - 6, r.Position.Y + 6), new Vector2(r.Position.X + 6, r.End.Y - 6), r.End - new Vector2(6, 6) }) Screw(p);
        var c = new Color(accent, .95f); const float L = 10;
        foreach (var (p, dx, dy) in new[] { (r.Position, 1, 1), (new Vector2(r.End.X, r.Position.Y), -1, 1), (new Vector2(r.Position.X, r.End.Y), 1, -1), (r.End, -1, -1) })
        { DrawLine(p, p + new Vector2(dx * L, 0), c, 2.5f); DrawLine(p, p + new Vector2(0, dy * L), c, 2.5f); }
    }

    void Button(Rect2 r, string t, bool active, Action act, int fs = 14, Color? tint = null)
    {
        DrawRect(r, tint ?? (active ? new Color(Green, .9f) : new Color(.08f, .14f, .08f, .9f)));
        DrawLine(r.Position, new Vector2(r.End.X, r.Position.Y), new Color(1, 1, 1, .22f), 1);
        DrawRect(r, new Color(Green, .8f), false, 1);
        Text(r.Position.X + (r.Size.X - Width(t, fs)) / 2, r.Position.Y + r.Size.Y / 2 + fs * .36f, t, fs, active || tint != null ? new Color(.03f, .06f, .03f) : Green);
        buttons.Add((r, act)); Named[t] = r;
    }

    public override void _Draw()
    {
        MapMarks();
        Panels.Clear(); buttons.Clear(); hoverKind = null; tip.Clear();
        var size = GetViewportRect().Size;
        TopBar(size);
        if (M.Setup) { SetupPanel(size); HelpBar(size); ScaleBar(size); return; }
        if (!ShowBases && !ShowIndustry && !ShowAircraft) { float top = 42; top += RaidPlannerStrip(top); float a = FormationsPanel(top); if (a > 0) top += a + 8; float e = EnemyPanel(top); if (e > 0) top += e + 8; TheatrePanel(top); }
        PlanPanel(size); OrderBar(size); DiaryPanel(size); BasesPanel(size); IndustryPanel(size); AircraftPanel(size); CityPopup(); FieldPopup(); SitePopup(); PilotPanel(size, 0); BomberPanel(size, 0); RaidReport(size); HelpBar(size); ScaleBar(size); AircraftCard(size); DrawTip(size);
    }

    void TopBar(Vector2 size)
    {
        var clock = M.Clock; var camp = M.Camp;
        Panel(new Rect2(0, 0, size.X, 34), Green);
        string date = camp != null ? camp.DateText : clock.Text; float tw = Width("FIREBOMBER", 16), dw = Width(date, 15), bw = tw + dw + 46;
        Bezel(new Rect2(4, 4, bw, 26)); Text(12, 23, "FIREBOMBER", 16, Amber); Text(19 + tw, 23, "//", 15, Dim); Text(40 + tw, 23, date, 15, Ink);
        float x = bw + 14;
        if (M.Holding) { Text(x, 23, M.Setup ? (camp != null ? "RAID ORDERS" : "SANDBOX SETUP") : "PLANNING THE RAID", 15, Amber); x += 344; }
        else
        {
            Button(new Rect2(x, 5, 76, 24), clock.Paused ? "RESUME" : "PAUSE", clock.Paused, () => clock.Paused = !clock.Paused, 14, clock.Paused ? new Color(1f, .6f, .15f) : null); x += 84;
            bool busy = camp != null && M.OpBusy;
            for (int i = 0; i < (camp != null ? Clock.Speeds.Length : Clock.OpMax + 1); i++)
            {
                int k = i; if (busy && i > Clock.OpMax) { var r = new Rect2(x, 5, 46, 24); DrawRect(r, new Color(.05f, .08f, .05f, .9f)); DrawRect(r, new Color(Green, .3f), false, 1); Text(x + (46 - Width(Clock.Label(i), 14)) / 2, 22, Clock.Label(i), 14, new Color(Green, .35f)); x += 52; continue; }
                Button(new Rect2(x, 5, 46, 24), Clock.Label(i), !clock.Paused && clock.SpeedIdx == i, () => clock.SetSpeed(k)); x += 52;
            }
            if (camp != null)
            {
                if (!busy) { Button(new Rect2(x, 5, 60, 24), "NEXT", false, M.SkipAhead); x += 66; }
                void Tab(string t, float w, bool on, Action a) { Button(new Rect2(x, 3, w, 28), t, on, a, 16); x += w + 5; }
                Tab("INDUSTRY", 100, ShowIndustry, () => { ShowIndustry = !ShowIndustry; ShowBases = ShowAircraft = false; });
                Tab("BASES", 72, ShowBases, () => { ShowBases = !ShowBases; ShowIndustry = ShowAircraft = false; });
                Tab("AIRCRAFT", 100, ShowAircraft, () => { ShowAircraft = !ShowAircraft; ShowBases = ShowIndustry = false; });
                x += 3;
                if (M.CanSave) { Button(new Rect2(x, 5, 58, 24), "SAVE", false, M.SaveNow); x += 64; }
                if (SaveGame.Latest != null) { Button(new Rect2(x, 5, 58, 24), "LOAD", false, M.LoadLatest); x += 64; }
            }
        }
        var mouse = M.MouseForReadout;
        string here = Covers(mouse) ? "" : M.World.Describe(M.S2Wx(mouse.X), M.S2Wy(mouse.Y));
        double hr = M.Air.Hour; string light = M.Air.Dark > .6 ? "NIGHT" : M.Air.Dark > 0 ? (hr < 12 ? "DAWN" : "DUSK") : "DAY";
        var sky = camp != null ? Weather.At(camp.Seed, M.S2Wx(size.X / 2), M.S2Wy(size.Y / 2), camp.Now) : (0, 0);
        string cloud = camp != null ? $"{light}   cloud {sky.cloud * 100:0}%" + (sky.rain > .08 ? $", {Weather.Rainy(sky.rain)}" : "") : $"{(int)hr:00}:{(int)(hr * 60 % 60):00} {light}", full = camp != null ? $"{cloud}   WIND {Weather.WindAt(camp.Seed, camp.Now, M.S2Wx(size.X / 2), M.S2Wy(size.Y / 2)).speed:0} m/s" : $"{cloud}   {M.Fire.WindText}";
        float rw = Math.Max(240, Width(here, 14) + 16), rx = size.X - rw - 5;
        foreach (var t in new[] { full, cloud, light }) if (x + 10 + Width(t, 13) < rx - 6) { Text(x + 10, 23, t, 13, Dim); break; }
        Bezel(new Rect2(rx, 4, rw, 26)); Text(size.X - Width(here, 14) - 13, 23, here, 14, Ink);
    }

    /// <summary>The way into the raid orders, top left, while no raid is under way.</summary>
    float RaidPlannerStrip(float y)
    {
        if (M.Camp == null || M.Holding) return 0; const float x = 8, w = 306, h = 34;
        Panel(new Rect2(x, y, w, h), Green); Plate(x + 8, y + 6, "RAID PLANNER", M.OpOver ? Green : new Color(Green, .4f), 13);
        if (M.OpOver) Button(new Rect2(x + w - 40, y + 6, 30, 22), "+", false, M.StartSetup); else Text(x + 150, y + 22, "raid under way", 12, Dim);
        return h + 8;
    }

    void TheatrePanel(float y)
    {
        const float x = 8, w = 306, rowH = 19;
        var wolves = M.World.Cities.Where(c => c.Wolf).OrderByDescending(c => c.Size).ToList(); var deer = M.World.Cities.Where(c => !c.Wolf).OrderByDescending(c => c.Size).ToList();
        int rows = Math.Max(wolves.Count, deer.Count);
        float h = collapsed ? 34 : 56 + rows * rowH + 6;
        Panel(new Rect2(x, y, w, h), Green);
        Text(x + 12, y + 22, "THEATRE", 15, Amber);
        Button(new Rect2(x + w - 40, y + 6, 30, 22), collapsed ? "+" : "-", false, () => collapsed = !collapsed);
        if (collapsed) return;
        Text(x + 12, y + 46, "Vulfen", 12, new Color(.6f, .8f, 1f));
        Text(x + 12 + w / 2, y + 46, "UCS", 12, new Color(1f, .65f, .55f));
        void Column(List<Place> cities, float cx, Color bar)
        {
            float ry = y + 52;
            foreach (var c in cities)
            {
                var row = new Rect2(cx, ry, w / 2 - 12, rowH - 1);
                bool sel = M.Selected == c;
                DrawRect(row, sel ? new Color(.2f, .34f, .18f, .9f) : new Color(.07f, .12f, .07f, .85f));
                DrawRect(new Rect2(row.Position, new Vector2(3, row.Size.Y)), bar);
                Text(cx + 10, ry + 14, c.Name, 13, sel ? Amber : Ink);
                buttons.Add((row, () => M.SelectCity(c, true))); Named[c.Name] = row;
                ry += rowH;
            }
        }
        Column(wolves, x + 6, Wolf); Column(deer, x + w / 2 + 6, Deer);
    }

    void HelpBar(Vector2 size)
    {
        Panel(new Rect2(0, size.Y - 30, size.X, 30), Green, false);
        if (M.Camp != null) { Text(12, size.Y - 10, "Space pause   1-7 speed   N next event   wheel zoom   drag or WASD pan   click a formation or city   right-click aim here   F follow   H hangar   C next city   Home whole theatre   Esc close / quit", 13, Ink); return; }
        Text(12, size.Y - 10, "Space pause   1-5 speed   wheel zoom   drag or WASD pan   click a formation or city   shift-drag pick aircraft   right-click aim here   F follow   H hangar   C next city   Home whole theatre   Esc close / quit", 13, Ink);
    }

    void ScaleBar(Vector2 size)
    {
        double metres = 1; while (metres * 10 * M.Zoom < 140) metres *= 10;
        if (metres * 5 * M.Zoom < 140) metres *= 5; else if (metres * 2 * M.Zoom < 140) metres *= 2;
        if (metres * 10 * M.Zoom < 140) metres *= 10;
        string t = metres >= 1000 ? $"{metres / 1000:0} km" : $"{metres:0} m";
        float len = (float)(metres * M.Zoom), rx = size.X - 18, y = size.Y - 42;
        Panel(new Rect2(rx - len - 8, y - 22, len + 16, 30), Green, false);
        DrawLine(new Vector2(rx - len, y), new Vector2(rx, y), Ink, 1.5f);
        DrawLine(new Vector2(rx - len, y - 5), new Vector2(rx - len, y), Ink, 1.5f); DrawLine(new Vector2(rx, y - 5), new Vector2(rx, y), Ink, 1.5f);
        Text(rx - len, y - 8, t, 13, Ink);
    }
}
