using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace Firebomber;

public static class N
{
    public static double Hash(int x, int y, int s) { unchecked { uint h = (uint)(x * 374761393 + y * 668265263 + s * 1442695041); h = (h ^ (h >> 13)) * 1274126177; return ((h ^ (h >> 16)) & 0xFFFFF) / (double)0xFFFFF; } }
    public static double V(double x, double y, int s)
    {
        double fx = Math.Floor(x), fy = Math.Floor(y); int xi = (int)fx, yi = (int)fy; double xf = x - fx, yf = y - fy, u = xf * xf * (3 - 2 * xf), v = yf * yf * (3 - 2 * yf);
        double a = Hash(xi, yi, s), b = Hash(xi + 1, yi, s), c = Hash(xi, yi + 1, s), d = Hash(xi + 1, yi + 1, s);
        return a + (b - a) * u + (c - a) * v + (a - b - c + d) * u * v;
    }
    /// <summary>Octave noise (0..1) in metres. Octaves too fine for this pixel size are replaced by their mean, so a coarse level is the same land, just less detailed.</summary>
    public static double Oct(double x, double y, int s, double lam, int n, double mpp)
    {
        double sum = 0, amp = 1, tot = 0;
        for (int k = 0; k < n; k++) { sum += amp * (lam >= 2 * mpp ? V(x / lam, y / lam, s + k) : .5); tot += amp; amp *= .5; lam *= .5; }
        return sum / tot;
    }
    public static double S(double t, int s) => 2 * (V(t, .37, s) - .5);
}

public sealed class Place { public string Name = ""; public double X, Y, R, Ang, RiverAng, SX, SY; public int Size, Seed; public bool City, Wolf; public CityStyle? Style; public CityLayout? Layout;
    /// <summary>For an airfield: the city it serves.</summary>
    public Place? Town; }

/// <summary>A polyline in world metres, with a grid so a tile can find the pieces it touches.</summary>
public sealed class Line
{
    public readonly List<(double x, double y)> P; public readonly double W0, W1;
    readonly Dictionary<long, List<int>> grid = new(); const double Cell = 2000;
    public Line(List<(double x, double y)> pts, double w0, double w1)
    {
        P = pts; W0 = w0; W1 = w1;
        for (int i = 0; i < P.Count - 1; i++)
        {
            long k = K(Math.Floor(P[i].x / Cell), Math.Floor(P[i].y / Cell));
            if (!grid.TryGetValue(k, out var l)) grid[k] = l = new List<int>();
            l.Add(i);
        }
    }
    static long K(double i, double j) => ((long)i << 24) ^ (long)j;
    public double WidthAt(int i) => W0 + (W1 - W0) * i / Math.Max(1, P.Count - 1);
    public IEnumerable<int> Near(double x0, double y0, double x1, double y1)
    {
        if (x1 - x0 > 64 * Cell) { for (int s = 0; s < P.Count - 1; s++) yield return s; yield break; }   // a far-out tile: the caller checks each piece anyway
        for (double i = Math.Floor(x0 / Cell) - 1; i <= Math.Floor(x1 / Cell) + 1; i++)
            for (double j = Math.Floor(y0 / Cell) - 1; j <= Math.Floor(y1 / Cell) + 1; j++)
                if (grid.TryGetValue(K(i, j), out var l)) foreach (int s in l) yield return s;
    }
    /// <summary>Midpoint displacement: a natural wander between control points, the same at every zoom.</summary>
    public static List<(double x, double y)> Wander((double x, double y)[] ctrl, double amp, double minSeg, int seed)
    {
        var p = ctrl.ToList(); double d = amp; int round = 0;
        while (Longest(p) > minSeg)
        {
            var o = new List<(double x, double y)>(p.Count * 2);
            for (int i = 0; i < p.Count - 1; i++)
            {
                var a = p[i]; var b = p[i + 1]; double dx = b.x - a.x, dy = b.y - a.y, l = Math.Sqrt(dx * dx + dy * dy) + 1e-9, m = d * (2 * N.Hash(i, round, seed) - 1);
                o.Add(a); o.Add(((a.x + b.x) / 2 - dy / l * m, (a.y + b.y) / 2 + dx / l * m));
            }
            o.Add(p[^1]); p = o; d *= .56; round++;
        }
        return p;
    }
    static double Longest(List<(double x, double y)> p) { double m = 0; for (int i = 0; i < p.Count - 1; i++) { double dx = p[i + 1].x - p[i].x, dy = p[i + 1].y - p[i].y; m = Math.Max(m, dx * dx + dy * dy); } return Math.Sqrt(m); }
}

/// <summary>The one world: fixed coordinates, metres, x east, y north, origin at the south-west corner of the theatre (1,728 x 972 km). It alone answers "what is here?".
/// Cities have permanent positions. Nothing re-centres. Everything drawn or generated later must ask this class.</summary>
public sealed class World
{
    public const double U = 2700, WidthM = 640 * U, HeightM = 360 * U;   // layout written in map units (640 x 360, y down), the same layout as the old simple theatre
    public static (double x, double y) FromMap(double mx, double my) => (mx * U, HeightM - my * U);
    public readonly List<Place> Cities = new();
    /// <summary>The way every runway lies: into the prevailing wind.</summary>
    public const double RunwayAng = .61;
    readonly HashSet<long> Taken = new(); const double ClearCell = 400;
    static long CellKey(double x, double y) => ((long)Math.Floor(x / ClearCell) << 32) ^ (uint)(int)Math.Floor(y / ClearCell);
    /// <summary>The nearest good spot to build on, from a direction out from a point: dry land, with no buildings, roads, rails or villages within this half-width. What is placed here is then taken.</summary>
    public (double x, double y) ClearSpot(double cx, double cy, double ang, double dmin, double dmax, double half)
    {
        bool Good(double x, double y)
        {
            int n = (int)Math.Ceiling((half + 500) / ClearCell);
            for (int j = -n; j <= n; j++) for (int i = -n; i <= n; i++) if (Taken.Contains(CellKey(x + i * ClearCell, y + j * ClearCell))) return false;
            for (int j = -2; j <= 2; j++) for (int i = -2; i <= 2; i++) if (IsWater(x + i * half / 2, y + j * half / 2, 64)) return false;
            var near = PlacesNear(x - half, y - half, x + half, y + half); if (Urb(x, y, near, 64) > .04) return false;
            foreach (var l in Roads.Concat(Rails))
                foreach (int s in l.Near(x - half - 300, y - half - 300, x + half + 300, y + half + 300))
                {
                    var (ax, ay) = l.P[s]; var (bx, by) = l.P[s + 1]; double vx = bx - ax, vy = by - ay, t = Math.Clamp(((x - ax) * vx + (y - ay) * vy) / Math.Max(1, vx * vx + vy * vy), 0, 1), dx = ax + vx * t - x, dy = ay + vy * t - y;
                    if (dx * dx + dy * dy < (half + 150) * (half + 150)) return false;
                }
            return true;
        }
        for (double d = dmin; d <= dmax; d += 1500)
            for (int k = 0; k < 13; k++)
            {
                double a = ang + (k % 2 == 0 ? 1 : -1) * ((k + 1) / 2) * .26, x = cx + Math.Cos(a) * d, y = cy + Math.Sin(a) * d;
                if (!Good(x, y)) continue;
                int m = (int)Math.Ceiling(half / ClearCell); for (int j = -m; j <= m; j++) for (int i = -m; i <= m; i++) Taken.Add(CellKey(x + i * ClearCell, y + j * ClearCell));
                return (x, y);
            }
        return SnapToLand(cx + Math.Cos(ang) * dmax, cy + Math.Sin(ang) * dmax);
    }
    public readonly List<Place> Fields = new();     // Wolf airfields
    public readonly List<Place> EnemyFields = new();  // UCS fighter fields
    public readonly List<Line> Rivers = new(), Roads = new(), Rails = new();
    readonly ConcurrentDictionary<long, Place?> villages = new();

    static readonly (string n, double x, double y, int s, bool w)[] CityList =
    {
        ("Ostrand", 330, 95, 9, true), ("Hjalmar", 420, 140, 7, true), ("Fjorgard", 120, 110, 7, true), ("Nordhavn", 250, 30, 6, true), ("Pohjaa", 520, 40, 5, true), ("Tarvik", 200, 70, 4, true),
        ("Skarn", 360, 140, 3, true), ("Vess", 480, 130, 3, true), ("Orsvik", 75, 130, 3, true), ("Hogvik", 395, 60, 3, true), ("Kaldvik", 290, 120, 4, true),
        ("Tamarsk", 330, 320, 10, false), ("Kolvograd", 300, 245, 7, false), ("Verchny", 570, 300, 8, false), ("Pelkaja", 440, 247, 6, false), ("Dravik", 200, 250, 4, false), ("Marren", 120, 260, 3, false),
        ("Sund", 380, 243, 3, false), ("Borrow", 80, 300, 4, false), ("Gorsk", 250, 340, 4, false), ("Lieben", 470, 310, 5, false), ("Karst", 520, 270, 4, false), ("Ludna", 610, 340, 3, false),
    };
    static readonly (double x, double y, double rx, double ry)[] Lakes = { (300, 70, 14, 7), (180, 95, 10, 6), (500, 60, 12, 8), (200, 320, 9, 5), (440, 330, 13, 7) };

    public World()
    {
        (double, double)[] M(params (double x, double y)[] pts) => pts.Select(p => FromMap(p.x, p.y)).ToArray();
        int seed = 900;
        foreach (var c in new[] { M((330, 360), (322, 320), (340, 290), (325, 225)), M((520, 360), (505, 320), (520, 285), (500, 225)), M((120, 360), (135, 320), (118, 285), (130, 225)),
                                  M((300, 40), (310, 90), (295, 170)), M((470, 20), (455, 80), (470, 170)), M((130, 25), (120, 70), (135, 170)) })
            Rivers.Add(new Line(Line.Wander(c, 24000, 40, seed++), 28, 150));
        foreach (var c in CityList)
        {
            var p = FromMap(c.x, c.y);
            var s = SnapToLand(p.x, p.y);
            var pl = new Place { Name = c.n, X = s.x, Y = s.y, Size = c.s, R = 500 + 230 * c.s, Ang = (N.Hash(c.s, (int)c.x, 3) - .5) * .9, City = true, Wolf = c.w, Seed = (int)(c.x * 31 + c.y), Style = CityStyle.For(c.n) };
            pl.SX = pl.X; pl.SY = pl.Y;
            Cities.Add(pl);
        }
        foreach (var c in Cities) if (c.Style != null) PrepareCity(c);
        // one airfield for every city. The three Vulfen bomber bases come first: the test raids fly from them
        foreach (var c in new[] { "Ostrand", "Hjalmar", "Skarn" }.Select(City).Concat(Cities.Where(q => q.Wolf && q.Name is not ("Ostrand" or "Hjalmar" or "Skarn"))))
        {
            double fd = Math.Max(13600, c.R * 1.8 + 4000); var s = SnapToLand(c.X + fd * .588, c.Y + fd * .809);      // clear of the suburbs
            Fields.Add(new Place { Name = c.Name + " field", X = s.x, Y = s.y, R = 300, Size = 1, Wolf = true, Town = c });
        }
        foreach (var c in Cities.Where(q => !q.Wolf))
        {
            double fa = N.Hash(c.Seed, 7, 501) * Math.Tau, d = c.R * 1.6 + 4000; var s = SnapToLand(c.X + Math.Cos(fa) * d, c.Y + Math.Sin(fa) * d);
            EnemyFields.Add(new Place { Name = c.Name + " field", X = s.x, Y = s.y, R = 300, Size = 1, Town = c });
        }
        Link(Roads, Cities, 2, 12, 12, .07, 60, 2000);
        Link(Rails, Cities.Where(c => c.Size >= 5).ToList(), 2, 6, 6, .04, 120, 4000, true);
        var vess = City("Vess"); var karst = City("Karst"); var a = FromMap(585, 150); var b = FromMap(585, 240);
        Roads.Add(new Line(Line.Wander(new[] { (vess.X, vess.Y), a, b, (karst.X, karst.Y) }, 16000, 60, 77), 12, 12));
        Rails.Add(new Line(Line.Wander(new[] { (vess.SX, vess.SY), a, b, (karst.SX, karst.SY) }, 9000, 120, 78), 6, 6));
        Dry(Roads, true); Dry(Rails);
        System.Threading.Tasks.Parallel.ForEach(Cities.Where(c => c.Style != null), c => c.Layout = CityGen.Build(this, c));
        // now the cities exist, the fields go where there is room for them: as near where they were meant to be as clear, dry ground allows
        foreach (var c in Cities) if (c.Layout != null) foreach (var p in c.Layout.Plots) Taken.Add(CellKey(p.X, p.Y));
        foreach (var f in Fields.Concat(EnemyFields))
        {
            double fang = Math.Atan2(f.Y - f.Town!.Y, f.X - f.Town.X); var (x, y) = ClearSpot(f.Town.X, f.Town.Y, fang, Math.Max(f.Town.R, Math.Sqrt((f.X - f.Town.X) * (f.X - f.Town.X) + (f.Y - f.Town.Y) * (f.Y - f.Town.Y)) * .5), 60000, 1100);
            f.X = x; f.Y = y;
        }
    }
    /// <summary>A road or railway that runs along a river rather than across it is cut there: a bridge is only ever a short crossing.</summary>
    void Dry(List<Line> lines, bool cutCentres = false)
    {
        double width = 0;
        bool Wet((double x, double y) q)
        {
            foreach (var r in Rivers) foreach (int i in r.Near(q.x, q.y, q.x, q.y))
                {
                    var a = r.P[i]; var b = r.P[i + 1]; double dx = b.x - a.x, dy = b.y - a.y, t = Math.Clamp(((q.x - a.x) * dx + (q.y - a.y) * dy) / (dx * dx + dy * dy + 1e-9), 0, 1), w = r.WidthAt(i) / 2 + 30;
                    if (Sq(a.x + dx * t - q.x) + Sq(a.y + dy * t - q.y) < w * w) { width = w * 2; return true; }
                }
            return false;
        }
        var res = new List<Line>();
        foreach (var l in lines)
        {
            var keep = new bool[l.P.Count]; Array.Fill(keep, true);
            for (int i = 0; i < l.P.Count;)
            {
                if (!Wet(l.P[i])) { i++; continue; }
                int j = i; double run = 0; while (j < l.P.Count - 1 && Wet(l.P[j + 1])) { run += Math.Sqrt(Sq(l.P[j + 1].x - l.P[j].x) + Sq(l.P[j + 1].y - l.P[j].y)); j++; }
                if (run > width * 1.5 + 60) for (int k = i; k <= j; k++) keep[k] = false;
                i = j + 1;
            }
            // inside a real city its own avenues take over
            if (cutCentres) for (int i = 0; i < l.P.Count; i++) foreach (var c in Cities) if (c.Style != null && Sq(l.P[i].x - c.X) + Sq(l.P[i].y - c.Y) < Sq(c.R * .5)) keep[i] = false;
            var cur = new List<(double x, double y)>();
            for (int i = 0; i < l.P.Count; i++) { if (keep[i]) cur.Add(l.P[i]); else { if (cur.Count > 1) res.Add(new Line(cur, l.W0, l.W1)); cur = new(); } }
            if (cur.Count > 1) res.Add(new Line(cur, l.W0, l.W1));
        }
        lines.Clear(); lines.AddRange(res);
    }
    public Place? CityNear(double x, double y) { foreach (var p in Cities) if (Math.Abs(p.X - x) < p.R * 2.4 && Math.Abs(p.Y - y) < p.R * 2.4) return p; return null; }
    public Place City(string name) => Cities.First(c => c.Name == name);
    public static string SizeName(Place c) => c.Size >= 8 ? "major city" : c.Size >= 5 ? "city" : "town";
    /// <summary>In words, what is at this point: used by the map readout. It asks the same questions the terrain painter asks.</summary>
    public string Describe(double x, double y)
    {
        if (x < 0 || y < 0 || x > WidthM || y > HeightM) return "";
        const double Mpp = 64; string what; BK zone = BK.None;
        foreach (var q in Cities) if (q.Layout != null && zone == BK.None) zone = q.Layout.ZoneAt(x, y);
        if (zone != BK.None) what = zone switch { BK.Old => "Old town", BK.Tenement => "Tenement blocks", BK.Mixed => "Mixed streets", BK.Rows => "Terraced rows", BK.Barracks => "Workers' barracks", BK.Cottage => "Wooden houses", BK.Superblock => "New apartment blocks", BK.Villa => "Villas", BK.Works => "Factories", BK.Park => "Park", BK.Cemetery => "Cemetery", BK.Allot => "Allotments", BK.Square => "Central square", BK.Dock => "Docks", BK.Downtown => "Downtown", BK.Frame => "Frame houses", BK.Walkup => "Brick walk-ups", BK.Slab => "New slab blocks", BK.WoodTown => "Painted wooden houses", BK.Airy => "Perimeter blocks", BK.Rock => "Rock and pine", BK.Plant => "Assembly plant", BK.Timber => "Timber yard", _ => "Built-up area" };
        else if (Land(x, y, Mpp) < 0) what = "Open sea";
        else if (InLake(x, y, Mpp)) what = "Lake";
        else if (IsWater(x, y, Mpp)) what = "River";
        else
        {
            double u = Urb(x, y, PlacesNear(x - 500, y - 500, x + 500, y + 500), Mpp);
            what = u > .35 ? "Built-up area" : u > .06 ? "Outskirts" : Elev(x, y, Mpp) > 830 ? "Bare high ground" : Forest(x, y, Mpp) - u * .7 > .56 ? "Forest" : "Farmland";
        }
        var c = Cities.OrderBy(q => Sq(q.X - x) + Sq(q.Y - y)).First(); double d = Math.Sqrt(Sq(c.X - x) + Sq(c.Y - y));
        if (d < 1500) return $"{what}, {c.Name}";
        string[] dirs = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };
        int o = (int)Math.Round((Math.Atan2(x - c.X, y - c.Y) * 180 / Math.PI + 360) / 45) % 8;
        return $"{what}, {d / 1000:0} km {dirs[o]} of {c.Name}";
    }
    /// <summary>Where a road first crosses a river: for looking at bridges.</summary>
    public (double x, double y) FirstBridge()
    {
        foreach (var r in Roads) for (int i = 0; i < r.P.Count; i += 4)
            foreach (var v in Rivers) foreach (int s in v.Near(r.P[i].x, r.P[i].y, r.P[i].x, r.P[i].y))
                if (Math.Abs(v.P[s].x - r.P[i].x) < 60 && Math.Abs(v.P[s].y - r.P[i].y) < 60 && Land(r.P[i].x, r.P[i].y, 64) > 1) return r.P[i];
        return (WidthM / 2, HeightM / 2);
    }

    void Link(List<Line> into, List<Place> nodes, int nearest, double w0, double w1, double amp, double seg, int seed, bool toStation = false)
    {
        var seen = new HashSet<(int, int)>();
        for (int i = 0; i < nodes.Count; i++)
            foreach (int j in Enumerable.Range(0, nodes.Count).Where(j => j != i).OrderBy(j => Dist(nodes[i], nodes[j])).Take(nearest))
            {
                var key = (Math.Min(i, j), Math.Max(i, j));
                if (!seen.Add(key) || !ClearOverLand(nodes[i], nodes[j])) continue;
                into.Add(new Line(Line.Wander(new[] { toStation ? (nodes[i].SX, nodes[i].SY) : (nodes[i].X, nodes[i].Y), toStation ? (nodes[j].SX, nodes[j].SY) : (nodes[j].X, nodes[j].Y) }, amp * Dist(nodes[i], nodes[j]), seg, seed + i * 40 + j), w0, w1));
            }
    }
    static double Dist(Place a, Place b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
    bool ClearOverLand(Place a, Place b)
    {
        for (double t = 0; t <= 1; t += 1.0 / 60) { double x = a.X + (b.X - a.X) * t, y = a.Y + (b.Y - a.Y) * t; if (Land(x, y, 1024) < 0 || InLake(x, y, 1024)) return false; }
        return true;
    }
    /// <summary>A real city stands on a river. Moves it onto one close by, or gives it its own, running through it to the nearest sea. Also sets its size and where its station is.</summary>
    void PrepareCity(Place c)
    {
        var st = c.Style!; c.R = st.Km * 1000;
        (double x, double y, double ang)? best = null; double bd = 30000 * 30000;
        foreach (var r in Rivers)
            for (int i = 8; i < r.P.Count - 8; i += 8)
            {
                double d = Sq(r.P[i].x - c.X) + Sq(r.P[i].y - c.Y);
                if (d < bd && Land(r.P[i].x, r.P[i].y, 256) > 1.5 && r.WidthAt(i) > st.RiverW * .3) { bd = d; best = (r.P[i].x, r.P[i].y, Math.Atan2(r.P[i + 8].y - r.P[i - 8].y, r.P[i + 8].x - r.P[i - 8].x)); }
            }
        if (best != null && st.RiverW < 200) { c.X = best.Value.x; c.Y = best.Value.y; c.RiverAng = best.Value.ang; }
        else
        {
            double ba = 0, bl = 1e12;
            for (double a = 0; a < 6.28; a += .26)
                for (double r = 2000; r < 300000; r += 2000)
                    if (Land(c.X + Math.Cos(a) * r, c.Y + Math.Sin(a) * r, 256) < -.2 || InLake(c.X + Math.Cos(a) * r, c.Y + Math.Sin(a) * r, 256)) { if (r < bl) { bl = r; ba = a; } break; }
            if (bl > 1e11) bl = 60000;
            (double, double) At(double r, double side) => (c.X + Math.Cos(ba) * r - Math.Sin(ba) * side, c.Y + Math.Sin(ba) * r + Math.Cos(ba) * side);
            double up = 30000 + c.R * 2, amp = st.Shape == CityShape.Strip ? 1800 : 1200, nearEnd = Math.Min(bl, c.R * 2.5);
            // gentle through the city, wandering freely on the long way to the sea
            var pts = Line.Wander(new[] { At(-up, up * .22), At(-up * .4, -up * .04), (c.X, c.Y), At(nearEnd, 0) }, amp, 40, c.Seed);
            if (bl > nearEnd) pts.AddRange(Line.Wander(new[] { At(nearEnd, 0), At(bl + 1500, 0) }, (bl - nearEnd) * .14, 40, c.Seed + 1).Skip(1));
            Rivers.Add(new Line(pts, st.RiverW * .55, st.RiverW * 1.25));
            c.RiverAng = ba;
        }
        double sa = c.RiverAng + 1.2 + N.Hash(c.Seed, 1, 401); c.SX = c.X + Math.Cos(sa) * c.R * .3; c.SY = c.Y + Math.Sin(sa) * c.R * .3;
        if (Land(c.SX, c.SY, 256) < .5) { c.SX = c.X - Math.Cos(sa) * c.R * .3; c.SY = c.Y - Math.Sin(sa) * c.R * .3; }
    }
    public bool InBasin(double x, double y) { foreach (var c in Cities) if (c.Layout != null && c.Layout.Basins.Count > 0 && Math.Abs(c.X - x) < 3 * c.R && Math.Abs(c.Y - y) < 3 * c.R) foreach (var q in c.Layout.Basins) if (q.Has(x, y)) return true; return false; }
    (double x, double y) SnapToLand(double x, double y)
    {
        const double Margin = 1.2 * U;                     // cities sit clear of open water
        if (Land(x, y, 256) > 1.2) return (x, y);
        for (double r = U; r < 60 * U; r += U)
            for (double a = 0; a < 6.28; a += .35) { double px = x + r * Math.Cos(a), py = y + r * Math.Sin(a); if (Land(px, py, 256) > Margin / U && !InLake(px, py, 256)) return (px, py); }
        return (x, y);
    }

    // ---------------------------------------------------------------- land and water
    /// <summary>Signed distance-like field in map units: positive on land, negative at sea. Fractal detail is added near the coast down to the pixel size.</summary>
    public double Land(double x, double y, double mpp)
    {
        // the layout is bent by slow noise first, so no coast is ever a straight line or a hard corner
        double mx = x / U + 9 * (N.V(x / (70 * U), y / (70 * U), 91) - .5) + 4 * (N.V(x / (20 * U), y / (20 * U), 92) - .5), my = (HeightM - y) / U + 9 * (N.V(x / (70 * U), y / (70 * U), 93) - .5) + 4 * (N.V(x / (20 * U), y / (20 * U), 94) - .5);
        double nc = 150 + 20 * Math.Sin(mx / 55) + 12 * Math.Sin(mx / 23 + 2) + 10 * N.S(mx / 60, 1) + 3.5 * N.S(mx / 15, 2);
        double sc = 238 + 14 * Math.Sin(mx / 70 + 1) + 6 * N.S(mx / 90, 3) + 1.6 * N.S(mx / 20, 4);
        double s = Math.Max(nc - my, my - sc);
        s = Math.Min(s, mx - (38 + 12 * Math.Sin(my / 40) + 6 * N.S(my / 45, 5)));
        double cx = 585 + 12 * Math.Sin(my / 30);
        s = SMax(s, SMin(24 - Math.Abs(mx - cx), SMin(my - 100, 270 - my, 8), 8), 10);                      // the land corridor
        s = Math.Max(s, (1 - Math.Sqrt(Sq((mx - 330) / 34) + Sq((my - 196) / 9))) * 9);                       // the island
        s = SMin(s, (Math.Sqrt(Sq((mx - 250) / 30) + Sq((my - 142) / 20)) - 1) * 20, 7);                      // two bays
        s = SMin(s, (Math.Sqrt(Sq((mx - 450) / 36) + Sq((my - 150) / 17)) - 1) * 17, 7);
        if (my > 222 && my < 300) s = SMin(s, Math.Abs(mx - (175 + (my - 270) * .39 + 7 * Math.Sin(my / 12))) - (3.6 * (300 - my) / 64 - .2), 1.5);   // the Kurin Channel
        if (Math.Abs(s) > 8) return s;
        double rag = .8 + .7 * Math.Clamp((215 - my) / 30, 0, 1), amp = 2.3 * rag, lam = 24;
        for (int k = 0; k < 11 && lam * U >= 2 * mpp; k++) { s += amp * 2 * (N.V(x / (lam * U), y / (lam * U), 40 + k) - .5); amp *= .54; lam *= .5; }
        return s;
    }
    static double Sq(double v) => v * v;
    static double SMin(double a, double b, double k) { double h = Math.Clamp(.5 + .5 * (b - a) / k, 0, 1); return b + (a - b) * h - k * h * (1 - h); }
    static double SMax(double a, double b, double k) => -SMin(-a, -b, k);
    public bool InLake(double x, double y, double mpp)
    {
        double mx = x / U + 6 * (N.V(x / (16 * U), y / (16 * U), 72) - .5), my = (HeightM - y) / U + 6 * (N.V(x / (16 * U), y / (16 * U), 73) - .5);
        foreach (var l in Lakes)
        {
            double e = 1 - Math.Sqrt(Sq((mx - l.x) / l.rx) + Sq((my - l.y) / l.ry));
            if (e < -1) continue;
            if (e * Math.Min(l.rx, l.ry) + 7 * (N.Oct(x, y, 71, 22000, 7, mpp) - .5) > 0) return true;
        }
        return false;
    }
    public bool IsWater(double x, double y, double mpp = 16)
    {
        if (Land(x, y, mpp) < 0 || InLake(x, y, mpp) || InBasin(x, y)) return true;
        foreach (var r in Rivers) foreach (int i in r.Near(x, y, x, y))
            { var a = r.P[i]; var b = r.P[i + 1]; double dx = b.x - a.x, dy = b.y - a.y, t = Math.Clamp(((x - a.x) * dx + (y - a.y) * dy) / (dx * dx + dy * dy + 1e-9), 0, 1), ex = a.x + dx * t - x, ey = a.y + dy * t - y; double w = r.WidthAt(i) / 2; if (ex * ex + ey * ey < w * w) return true; }
        return false;
    }
    public double Elev(double x, double y, double mpp)
    {
        double mx = x / U, my = (HeightM - y) / U;
        double e = 120 + 700 * Math.Exp(-(Sq((mx - 120) / 90) + Sq((my - 70) / 60))) + 350 * Math.Exp(-(Sq((mx - 470) / 80) + Sq((my - 55) / 45)));
        double wx = x + 50000 * (N.V(x / 110000, y / 110000, 33) - .5), wy = y + 50000 * (N.V(x / 110000, y / 110000, 34) - .5);
        return Math.Max(5, e + 460 * (N.Oct(wx, wy, 31, 60000, 9, mpp) - .5));
    }
    public static double NorthF(double x, double y) => (HeightM - y) / U + 26 * (N.V(x / (34 * U), y / (34 * U), 95) - .5) + 5 * (N.V(x / (3 * U), y / (3 * U), 96) - .5);
    public static bool North(double x, double y) => NorthF(x, y) < 196;
    public double Forest(double x, double y, double mpp) => .55 * N.Oct(x, y, 51, 50000, 3, mpp) + .45 * N.Oct(x, y, 56, 3000, 5, mpp) + (North(x, y) ? .055 : -.035);

    // ---------------------------------------------------------------- settlements
    const double VCell = 9000;
    /// <summary>The village of a grid cell, if it has one: a deterministic function of position, so it is the same at every zoom.</summary>
    public Place? VillageAt(int i, int j) => villages.GetOrAdd(((long)i << 24) ^ (uint)j, _ =>
    {
        double x = (i + .15 + .7 * N.Hash(i, j, 702)) * VCell, y = (j + .15 + .7 * N.Hash(i, j, 703)) * VCell;
        if (N.Hash(i, j, 701) > (North(x, y) ? .42 : .6) || x < 0 || y < 0 || x > WidthM || y > HeightM) return null;
        if (Land(x, y, 64) < .35 || InLake(x, y, 64) || Forest(x, y, 64) > .6) return null;
        foreach (var c in Cities) if (Math.Sqrt(Sq(c.X - x) + Sq(c.Y - y)) < c.R * (c.Style != null ? 1.3 : 1.7)) return null;
        return new Place { X = x, Y = y, R = 130 + 220 * N.Hash(i, j, 704), Ang = N.Hash(i, j, 705) * 3, Seed = i * 977 + j };
    });
    public List<Place> PlacesNear(double x0, double y0, double x1, double y1)
    {
        var l = new List<Place>();
        foreach (var c in Cities) if (c.X > x0 - 3 * c.R && c.X < x1 + 3 * c.R && c.Y > y0 - 3 * c.R && c.Y < y1 + 3 * c.R) l.Add(c);
        for (int i = (int)Math.Floor(x0 / VCell) - 1; i <= (int)Math.Floor(x1 / VCell) + 1; i++)
            for (int j = (int)Math.Floor(y0 / VCell) - 1; j <= (int)Math.Floor(y1 / VCell) + 1; j++)
                { var v = VillageAt(i, j); if (v != null && v.X > x0 - 3 * v.R && v.X < x1 + 3 * v.R && v.Y > y0 - 3 * v.R && v.Y < y1 + 3 * v.R) l.Add(v); }
        return l;
    }
    /// <summary>How built-up a point is (0..1). The distance is warped, so settlements have irregular edges, never circles.</summary>
    public static double Urb(double x, double y, List<Place> places, double mpp)
    {
        if (places.Count == 0) return 0;
        double ox = (N.Oct(x, y, 810, 2400, 3, mpp) - .5) * 1.5, oy = (N.Oct(x, y, 815, 2400, 3, mpp) - .5) * 1.5, u = 0;
        foreach (var p in places)
        {
            if (p.Layout != null) continue;
            double dx = (x - p.X) / p.R + ox, dy = (y - p.Y) / p.R + oy, q = dx * dx + dy * dy;
            if (q < 9) u = Math.Max(u, Math.Exp(-q));
        }
        return u;
    }
}
