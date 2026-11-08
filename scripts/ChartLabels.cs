using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Firebomber;

/// <summary>Names on the map, and the city markers. Placed afresh every frame, most important first: each label takes the first spot beside its anchor that is free of
/// other labels, markers and console panels, or it stays hidden. So two labels can never overlap, whatever the zoom. A label keeps its spot while it can, so names do not jump about.</summary>
public partial class ChartLabels : Node2D
{
    enum Kind { City, Water, Region, Field }
    sealed class L
    {
        public string Text = ""; public double X, Y, MinZoom, MaxZoom; public int Fs, Pri, Cand; public Kind K; public Place? City;
        public float W, Alpha; public bool Placed; public Rect2 R; public Vector2 Anchor;
    }
    public Main M = null!;
    public bool Snap;
    readonly List<L> all = new();
    Font font = null!;
    const double CityMaxZoom = .3;

    public override void _Ready()
    {
        TextureFilter = TextureFilterEnum.Linear;
        font = ThemeDB.FallbackFont;
        foreach (var c in M.World.Cities) Add(c.Name, c.X, c.Y, Kind.City, c.Size >= 6 ? 13 : 11, 100 + c.Size, 0, CityMaxZoom, c);
        void Mapped(string t, double mx, double my, Kind k, int fs, int pri, double z0, double z1) { var p = World.FromMap(mx, my); Add(t, p.x, p.y, k, fs, pri, z0, z1, null); }
        Mapped("the Fjordsee", 200, 195, Kind.Water, 16, 50, 0, .008);
        Mapped("the Rock Gulf", 495, 205, Kind.Water, 16, 50, 0, .008);
        Mapped("the Vestapproach", 16, 205, Kind.Water, 13, 40, .0016, .012);
        Mapped("Kurin Channel", 171, 268, Kind.Water, 11, 45, .0024, .03);
        Mapped("V U L F E N   D I R E C T O R A T E", 335, 28, Kind.Region, 20, 10, 0, .0032);
        Mapped("U N I O N   O F   C O M M I T T E E   S T A T E S", 236, 303, Kind.Region, 20, 10, 0, .0032);
        foreach (var f in M.World.Fields) Add(f.Name, f.X, f.Y, Kind.Field, 10, 60, .0022, .3, f);
        foreach (var l in all) l.W = font.GetStringSize(l.Text, HorizontalAlignment.Left, -1, l.Fs).X;
    }
    void Add(string t, double x, double y, Kind k, int fs, int pri, double z0, double z1, Place? c) => all.Add(new L { Text = t, X = x, Y = y, K = k, Fs = fs, Pri = pri, MinZoom = z0, MaxZoom = z1, City = c });

    static float MarkerR(Place c) => c.Size >= 8 ? 3.5f : c.Size >= 5 ? 2.5f : 2f;
    double MarkerAlpha => Math.Clamp((.03 - M.Zoom) / .012, 0, 1);

    Rect2 Spot(L l, int cand)
    {
        float h = l.Fs + 3, w = l.W, r = l.City != null ? MarkerR(l.City) + 3 + (float)Math.Min(l.City.R * 1.3 * M.Zoom, 14) : 0; var a = l.Anchor;
        if (l.City == null) return new Rect2(a.X - w / 2, a.Y - h / 2, w, h);
        return cand switch
        {
            0 => new Rect2(a.X - w / 2, a.Y + r, w, h),          // below, the usual place
            1 => new Rect2(a.X - w / 2, a.Y - r - h, w, h),
            2 => new Rect2(a.X + r + 1, a.Y - h / 2, w, h),
            3 => new Rect2(a.X - r - 1 - w, a.Y - h / 2, w, h),
            4 => new Rect2(a.X + r - 2, a.Y + r - 1, w, h),
            5 => new Rect2(a.X + r - 2, a.Y - r - h + 1, w, h),
            6 => new Rect2(a.X - r + 2 - w, a.Y + r - 1, w, h),
            _ => new Rect2(a.X - r + 2 - w, a.Y - r - h + 1, w, h),
        };
    }

    void Layout(float dt)
    {
        var size = GetViewportRect().Size; var screen = new Rect2(4, 0, size.X - 8, size.Y);
        var taken = new List<Rect2>(M.Ui.Panels); taken.AddRange(M.View.Tags);
        foreach (var l in all) l.Anchor = M.W2S(l.X, l.Y);
        if (MarkerAlpha > 0) foreach (var l in all) if (l.City != null) { float r = MarkerR(l.City) + 1; taken.Add(new Rect2(l.Anchor - new Vector2(r, r), new Vector2(2 * r, 2 * r))); }
        foreach (var l in all) if (!Snap && !l.Placed && l.Alpha > .05f) taken.Add(Spot(l, l.Cand));   // a name still fading out keeps its room until it has gone
        bool Free(Rect2 r) { if (!screen.Encloses(r)) return false; foreach (var t in taken) if (t.Intersects(r)) return false; return true; }
        foreach (var l in all.OrderByDescending(l => l.Pri))
        {
            l.Placed = false;
            if (M.Zoom < l.MinZoom || M.Zoom > l.MaxZoom) continue;
            if (M.Ui.Panels.Any(p => p.HasPoint(l.Anchor))) continue;
            int n = l.City != null ? 8 : 1;
            for (int t = -1; t < n && !l.Placed; t++)
            {
                int c = t < 0 ? l.Cand : t;
                var r = Spot(l, c);
                if (!Free(r)) continue;
                l.Cand = c; l.Placed = true; l.R = r; taken.Add(r);
            }
        }
        foreach (var l in all) l.Alpha = Snap ? (l.Placed ? 1 : 0) : Mathf.MoveToward(l.Alpha, l.Placed ? 1 : 0, dt * 6);
    }

    public override void _Process(double delta) { Layout((float)delta); QueueRedraw(); }
    public void Test() => Layout(0);

    public override void _Draw()
    {
        float ink = (float)Math.Clamp((.012 - M.Zoom) / .006, 0, 1);
        Color Mix(Color near, Color far) => ink > .5f ? far : near;   // never a blend: halfway between pale and dark text is unreadable
        Color text = Mix(new Color(.92f, .89f, .74f), new Color(.17f, .13f, .08f)), halo = Mix(new Color(.04f, .05f, .03f), new Color(.86f, .82f, .64f));
        Color water = Mix(new Color(.62f, .76f, .8f), new Color(.2f, .32f, .38f)), region = Mix(new Color(.8f, .78f, .6f), new Color(.25f, .19f, .1f));
        float ma = (float)MarkerAlpha;
        if (ma > 0)
            foreach (var l in all.Where(l => l.K == Kind.City))
            {
                float r = MarkerR(l.City!); var a = l.Anchor.Round();
                DrawRect(new Rect2(a - new Vector2(r + 1, r + 1), new Vector2(2 * r + 2, 2 * r + 2)), new Color(halo, ma * .9f));
                DrawRect(new Rect2(a - new Vector2(r, r), new Vector2(2 * r, 2 * r)), new Color(text, ma));
            }
        foreach (var l in all)
        {
            if (l.Alpha <= 0) continue;
            var r = l.Placed ? l.R : Spot(l, l.Cand);
            var at = new Vector2(Mathf.Round(r.Position.X), Mathf.Round(r.Position.Y + l.Fs));
            switch (l.K)
            {
                case Kind.City:
                case Kind.Field:
                    DrawStringOutline(font, at, l.Text, HorizontalAlignment.Left, -1, l.Fs, 3, new Color(halo, l.Alpha * .75f));
                    DrawString(font, at, l.Text, HorizontalAlignment.Left, -1, l.Fs, new Color(text, l.Alpha));
                    break;
                case Kind.Water: DrawString(font, at, l.Text, HorizontalAlignment.Left, -1, l.Fs, new Color(water, l.Alpha * .8f)); break;
                default: DrawString(font, at, l.Text, HorizontalAlignment.Left, -1, l.Fs, new Color(region, l.Alpha * .5f)); break;
            }
        }
    }

    /// <summary>The city whose name or marker is under this screen point.</summary>
    public Place? CityAt(Vector2 p)
    {
        foreach (var l in all.Where(l => l.K == Kind.City))
            if ((l.Placed && l.R.Grow(2).HasPoint(p)) || (MarkerAlpha > 0 && (l.Anchor - p).Length() < MarkerR(l.City!) + 5)) return l.City;
        return null;
    }
    /// <summary>For testing: how many pairs of visible labels overlap right now (it should always be none).</summary>
    public int Overlaps() { var v = all.Where(l => l.Placed).ToList(); int n = 0; for (int i = 0; i < v.Count; i++) for (int j = i + 1; j < v.Count; j++) if (v[i].R.Intersects(v[j].R)) n++; return n; }
    public int Shown => all.Count(l => l.Placed);
}
