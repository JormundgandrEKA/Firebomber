using System;
using System.Collections.Generic;
using System.Linq;

namespace Firebomber;

/// <summary>Four corners going round: 0, 1 along the block's length, 2 opposite 0, 3.</summary>
public struct Quad
{
    public double X0, Y0, X1, Y1, X2, Y2, X3, Y3;
    public (double x, double y) Map(double s, double t)
    {
        double ax = X0 + (X1 - X0) * s, ay = Y0 + (Y1 - Y0) * s, bx = X3 + (X2 - X3) * s, by = Y3 + (Y2 - Y3) * s;
        return (ax + (bx - ax) * t, ay + (by - ay) * t);
    }
    public double Lu => Math.Sqrt((X1 - X0) * (X1 - X0) + (Y1 - Y0) * (Y1 - Y0));
    public double Lv => Math.Sqrt((X3 - X0) * (X3 - X0) + (Y3 - Y0) * (Y3 - Y0));
    public double Ang => Math.Atan2(Y1 - Y0, X1 - X0);
    public (double x, double y) Center => ((X0 + X1 + X2 + X3) / 4, (Y0 + Y1 + Y2 + Y3) / 4);
    public double MinX => Math.Min(Math.Min(X0, X1), Math.Min(X2, X3)); public double MaxX => Math.Max(Math.Max(X0, X1), Math.Max(X2, X3));
    public double MinY => Math.Min(Math.Min(Y0, Y1), Math.Min(Y2, Y3)); public double MaxY => Math.Max(Math.Max(Y0, Y1), Math.Max(Y2, Y3));
    public bool Has(double x, double y)
    {
        static double Cr(double ax, double ay, double bx, double by, double px, double py) => (bx - ax) * (py - ay) - (by - ay) * (px - ax);
        double a = Cr(X0, Y0, X1, Y1, x, y), b = Cr(X1, Y1, X2, Y2, x, y), c = Cr(X2, Y2, X3, Y3, x, y), d = Cr(X3, Y3, X0, Y0, x, y);
        return (a >= 0 && b >= 0 && c >= 0 && d >= 0) || (a <= 0 && b <= 0 && c <= 0 && d <= 0);
    }
}
public struct CityBlock { public Quad Outer, Inner; public byte Ground, Street; public BK Kind; }

/// <summary>A finished city: every block, building, avenue and rail track, with lookups by area, and a low-resolution picture of it for far views.</summary>
public sealed class CityLayout
{
    public Plot[] Plots = { }; public CityBlock[] Blocks = { };
    public readonly List<Line> Roads = new(), Tracks = new(); public readonly List<Quad> Basins = new();
    public double WorksX, WorksY, X0, Y0, X1, Y1;
    Dictionary<long, int[]> plotCells = new(), blockCells = new();
    const double GridM = 500, Pad = 500;
    static long K(int i, int j) => ((long)i << 32) ^ (uint)j;
    static Dictionary<long, int[]> Index(int n, Func<int, (double x, double y)> at)
    {
        var d = new Dictionary<long, List<int>>();
        for (int i = 0; i < n; i++) { var p = at(i); long k = K((int)Math.Floor(p.x / GridM), (int)Math.Floor(p.y / GridM)); if (!d.TryGetValue(k, out var l)) d[k] = l = new List<int>(); l.Add(i); }
        return d.ToDictionary(kv => kv.Key, kv => kv.Value.ToArray());
    }
    public void BuildIndex() { plotCells = Index(Plots.Length, i => (Plots[i].X, Plots[i].Y)); blockCells = Index(Blocks.Length, i => Blocks[i].Outer.Center); }
    static IEnumerable<int> Query(Dictionary<long, int[]> c, double x0, double y0, double x1, double y1)
    {
        for (int i = (int)Math.Floor((x0 - Pad) / GridM); i <= (int)Math.Floor((x1 + Pad) / GridM); i++)
            for (int j = (int)Math.Floor((y0 - Pad) / GridM); j <= (int)Math.Floor((y1 + Pad) / GridM); j++)
                if (c.TryGetValue(K(i, j), out var l)) foreach (int n in l) yield return n;
    }
    public IEnumerable<int> PlotsIn(double x0, double y0, double x1, double y1) => Query(plotCells, x0, y0, x1, y1);
    public IEnumerable<int> BlocksIn(double x0, double y0, double x1, double y1) => Query(blockCells, x0, y0, x1, y1);

    // ---------------------------------------------------------------- the far picture: colour and coverage on 64, 256 and 1024 m cells
    const int Cell = 64; int nx, ny; float[][] cov = new float[3][]; int[][] col = new int[3][]; byte[] zone = { };
    float[] ar = { }, rr = { }, gg = { }, bb = { };
    public void BeginFar(double x0, double y0, double x1, double y1) { X0 = x0; Y0 = y0; X1 = x1; Y1 = y1; nx = (int)((x1 - x0) / Cell) + 1; ny = (int)((y1 - y0) / Cell) + 1; ar = new float[nx * ny]; rr = new float[nx * ny]; gg = new float[nx * ny]; bb = new float[nx * ny]; zone = new byte[nx * ny]; }
    public void AddFar(double x, double y, float area, int rgb, BK kind = BK.None)
    {
        int i = (int)((x - X0) / Cell), j = (int)((y - Y0) / Cell); if (i < 0 || j < 0 || i >= nx || j >= ny) return;
        int o = j * nx + i; ar[o] += area; rr[o] += area * (rgb >> 16 & 255); gg[o] += area * (rgb >> 8 & 255); bb[o] += area * (rgb & 255); if (kind != BK.None) zone[o] = (byte)kind;
    }
    public void FinishFar()
    {
        int w = nx, h = ny;
        for (int lv = 0, f = 1; lv < 3; lv++, f *= 4)
        {
            int lw = w / f + 1, lh = h / f + 1; var a = new float[lw * lh]; var r = new float[lw * lh]; var g = new float[lw * lh]; var b = new float[lw * lh];
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) { int s = y * w + x, d = (y / f) * lw + x / f; a[d] += ar[s]; r[d] += rr[s]; g[d] += gg[s]; b[d] += bb[s]; }
            var c = new float[lw * lh]; var o = new int[lw * lh]; float cellArea = Cell * f * Cell * f;
            for (int i = 0; i < a.Length; i++) { if (a[i] <= 0) continue; c[i] = Math.Min(1, a[i] / cellArea); o[i] = (int)(r[i] / a[i]) << 16 | (int)(g[i] / a[i]) << 8 | (int)(b[i] / a[i]); }
            cov[lv] = c; col[lv] = o;
        }
        ar = rr = gg = bb = Array.Empty<float>();
    }
    public (float cov, int col) Far(double x, double y, int mpp)
    {
        if (x < X0 || y < Y0 || x >= X1 || y >= Y1) return (0, 0);
        int lv = mpp >= 1024 ? 2 : mpp >= 256 ? 1 : 0, f = 1 << (2 * lv), lw = nx / f + 1, o = ((int)((y - Y0) / Cell) / f) * lw + ((int)((x - X0) / Cell) / f);
        return (cov[lv][o], col[lv][o]);
    }
    /// <summary>A soft reading of how built-up a place is, for tinting the country at the city's edge.</summary>
    public double Soft(double x, double y)
    {
        if (x < X0 || y < Y0 || x >= X1 - 256 || y >= Y1 - 256) return 0;
        const int F = 4; int lw = nx / F + 1; double fx = (x - X0) / (Cell * F), fy = (y - Y0) / (Cell * F); int i = (int)fx, j = (int)fy; double u = fx - i, v = fy - j;
        var c = cov[1]; float At(int a, int b) { int o = b * lw + a; return o >= 0 && o < c.Length ? c[o] : 0; }
        return (At(i, j) * (1 - u) + At(i + 1, j) * u) * (1 - v) + (At(i, j + 1) * (1 - u) + At(i + 1, j + 1) * u) * v;
    }
    public BK ZoneAt(double x, double y) { if (x < X0 || y < Y0 || x >= X1 || y >= Y1) return BK.None; return (BK)zone[(int)((y - Y0) / Cell) * nx + (int)((x - X0) / Cell)]; }
}

/// <summary>Builds a city from its style. Top down, as cities are read on a map: avenues and rings cut it into sectors; a sector is cut into neighbourhoods, each with one character;
/// a neighbourhood is cut into blocks; blocks are filled with buildings (CityBlocks.cs). Rivers, rail and docks are laid down first and everything else keeps clear of them.</summary>
public sealed partial class CityGen
{
    readonly World w; readonly Place p; readonly CityStyle s; readonly double ox, oy, R, nX, nY, tX, tY; readonly Random rng; readonly CityLayout L = new();
    readonly List<Plot> plots = new(); readonly List<CityBlock> blocks = new();
    const int MC = 16; int mN; double mX, mY; byte[] mask = { }; byte[] railNear = { }, waterNear = { }; const int NC = 128; int nN;
    readonly double[] ringWob = new double[64], sideWob = new double[64]; double[] ringR = { }; double[] radAng = { }; readonly double[][] radWob; readonly List<(double x, double y, double r)> sats = new();
    readonly double[] riverOff = new double[129];

    CityGen(World world, Place place)
    {
        w = world; p = place; s = place.Style!; R = s.Km * 1000; rng = new Random(place.Seed);
        tX = Math.Cos(p.RiverAng); tY = Math.Sin(p.RiverAng); nX = -tY; nY = tX;
        double bank = s.RiverW / 2 + (s.Shape == CityShape.Strip ? 0 : Math.Min(600, R * .1));
        ox = p.X + nX * bank; oy = p.Y + nY * bank;
        for (int i = 0; i < 64; i++) { double a = i / 64.0 * Math.Tau; ringWob[i] = .13 * Math.Sin(a * 2 + p.Seed) + .08 * Math.Sin(a * 3 + p.Seed * 2) + .05 * Math.Sin(a * 7 + p.Seed * 3); sideWob[i] = .2 * Math.Sin(a * 2 + p.Seed * 7) + .13 * Math.Sin(a * 3 + p.Seed * .3) + .07 * Math.Sin(a * 7 + p.Seed); }
        ringR = s.Rings.Select(f => f * R).ToArray();
        radAng = Enumerable.Range(0, s.Radials).Select(i => (i + .15 + .7 * rng.NextDouble()) / s.Radials * Math.Tau - Math.PI).ToArray();
        radWob = radAng.Select(_ => { double a = rng.NextDouble() * 6, b = rng.NextDouble() * 6; return Enumerable.Range(0, 32).Select(i => .1 * Math.Sin(i * .33 + a) + .05 * Math.Sin(i * .9 + b)).ToArray(); }).ToArray();
    }
    public static CityLayout Build(World w, Place p)
    {
        var g = new CityGen(w, p); g.Run(); return g.L;
    }

    double ReachM => s.Shape == CityShape.Strip ? R * 1.7 : R * 2;
    void Run()
    {
        double re = ReachM + 1500; mX = ox - re; mY = oy - re; mN = (int)(2 * re / MC) + 1; mask = new byte[mN * mN];
        L.BeginFar(ox - re, oy - re, ox + re, oy + re);
        MarkWorld();
        RiverTable();
        for (int i = 0; i < s.Sats; i++)
        {
            double a = rng.NextDouble() * Math.Tau, r = R * (1.2 + .5 * rng.NextDouble()); if (s.Shape == CityShape.Strip) a = rng.NextDouble() < .5 ? p.RiverAng : p.RiverAng + Math.PI;
            double x = ox + Math.Cos(a) * r + nX * (s.Shape == CityShape.Strip ? 600 : 0), y = oy + Math.Sin(a) * r + nY * (s.Shape == CityShape.Strip ? 600 : 0);
            if (w.Land(x, y, 256) > .4) sats.Add((x, y, R * (.1 + .08 * rng.NextDouble()) + 250));
        }
        if (s.Arms > 0) Inlets();
        if (s.Port) Docks();
        Rail();
        if (s.Citadel) Citadel();
        Arterials();
        Proximity();
        Sectors();
        L.Plots = plots.ToArray(); L.Blocks = blocks.ToArray(); L.BuildIndex(); L.FinishFar();
        var works = plots.Where(q => q.Mat == CityPlan.Factory).ToList();
        if (works.Count > 0)
        {
            // the middle of the biggest gathering of factories
            var cells = works.GroupBy(q => (Math.Floor(q.X / 1500), Math.Floor(q.Y / 1500))).OrderByDescending(g => g.Sum(q => q.W * q.D)).First();
            L.WorksX = cells.Average(q => q.X); L.WorksY = cells.Average(q => q.Y);
        }
        else { L.WorksX = ox; L.WorksY = oy; }
        mask = railNear = waterNear = Array.Empty<byte>();
        p.X = ox; p.Y = oy;
    }

    // ---------------------------------------------------------------- what is already here, on a 16 m grid
    const byte Water = 1, Way = 2, Taken = 3;
    bool Free(double x, double y) { int i = (int)((x - mX) / MC), j = (int)((y - mY) / MC); return i >= 0 && j >= 0 && i < mN && j < mN && mask[j * mN + i] == 0; }
    void Disc(double x, double y, double r, byte v)
    {
        int cx = (int)((x - mX) / MC), cy = (int)((y - mY) / MC), n = (int)(r / MC) + 1; double rr = (r / MC) * (r / MC) + .5;
        for (int j = Math.Max(0, cy - n); j <= Math.Min(mN - 1, cy + n); j++) for (int i = Math.Max(0, cx - n); i <= Math.Min(mN - 1, cx + n); i++)
                if ((i - cx) * (i - cx) + (j - cy) * (j - cy) <= rr) { ref byte m = ref mask[j * mN + i]; if (m == 0 || v == Water) m = v; }
    }
    void MarkLine(Line l, double extra, byte v)
    {
        for (int k = 0; k < l.P.Count - 1; k++)
        {
            var a = l.P[k]; var b = l.P[k + 1];
            if (a.x < mX || a.y < mY || a.x > mX + mN * MC || a.y > mY + mN * MC) continue;
            double len = Math.Sqrt((b.x - a.x) * (b.x - a.x) + (b.y - a.y) * (b.y - a.y)), r = l.WidthAt(k) / 2 + extra; int steps = Math.Max(1, (int)(len / Math.Max(10, r * .7)));
            for (int t = 0; t < steps; t++) Disc(a.x + (b.x - a.x) * t / steps, a.y + (b.y - a.y) * t / steps, r, v);
        }
    }
    void MarkQuad(Quad q, byte v)
    {
        for (double y = q.MinY; y <= q.MaxY; y += MC) for (double x = q.MinX; x <= q.MaxX; x += MC)
                if (q.Has(x, y)) { int i = (int)((x - mX) / MC), j = (int)((y - mY) / MC); if (i >= 0 && j >= 0 && i < mN && j < mN) mask[j * mN + i] = v; }
    }
    void MarkWorld()
    {
        for (int j = 0; j < mN; j += 2)
            for (int i = 0; i < mN; i += 2)
            {
                double x = mX + (i + 1) * MC, y = mY + (j + 1) * MC;
                if (w.Land(x, y, 64) < .06 || w.InLake(x, y, 64)) for (int b = j; b < Math.Min(mN, j + 2); b++) for (int a = i; a < Math.Min(mN, i + 2); a++) mask[b * mN + a] = Water;
            }
        foreach (var r in w.Rivers) MarkLine(r, 6, Water);
        foreach (var l in w.Roads) MarkLine(l, 8, Way);
        foreach (var l in w.Rails) MarkLine(l, 14, Way);
    }
    void Proximity()
    {
        nN = mN * MC / NC + 1; railNear = new byte[nN * nN]; waterNear = new byte[nN * nN];
        void Dil(byte[] t, int i, int j, int d) { for (int b = Math.Max(0, j - d); b <= Math.Min(nN - 1, j + d); b++) for (int a = Math.Max(0, i - d); a <= Math.Min(nN - 1, i + d); a++) t[b * nN + a] = 1; }
        foreach (var l in w.Rails.Concat(L.Tracks)) foreach (var q in l.P) { int i = (int)((q.x - mX) / NC), j = (int)((q.y - mY) / NC); if (i >= 0 && j >= 0 && i < nN && j < nN) Dil(railNear, i, j, 3); }
        for (int j = 0; j < mN; j += 8) for (int i = 0; i < mN; i += 8) if (mask[j * mN + i] == Water) Dil(waterNear, i * MC / NC, j * MC / NC, 2);
    }
    bool Near(byte[] t, double x, double y) { int i = (int)((x - mX) / NC), j = (int)((y - mY) / NC); return i >= 0 && j >= 0 && i < nN && j < nN && t[j * nN + i] == 1; }

    // ---------------------------------------------------------------- river frame, for strip cities
    (double u, double v) Frame(double x, double y) { double dx = x - ox, dy = y - oy; return (dx * tX + dy * tY, dx * nX + dy * nY); }
    void RiverTable()
    {
        Array.Clear(riverOff);
        if (s.Shape != CityShape.Strip) return;
        var seen = new double[129]; var cnt = new int[129];
        foreach (var r in w.Rivers) foreach (var q in r.P) { var (u, v) = Frame(q.x, q.y); int k = (int)((u + 2 * R) / (4 * R) * 128); if (k >= 0 && k <= 128 && Math.Abs(v) < 6000) { seen[k] += v; cnt[k]++; } }
        double last = 0; for (int k = 0; k <= 128; k++) { if (cnt[k] > 0) last = seen[k] / cnt[k]; riverOff[k] = last; }
    }
    double Off(double u) { double f = Math.Clamp((u + 2 * R) / (4 * R) * 128, 0, 127.99); int k = (int)f; return riverOff[k] + (riverOff[k + 1] - riverOff[k]) * (f - k); }

    // ---------------------------------------------------------------- shape: which sector a point lies in, and how far out it is
    double RingAt(int i, double th) { double f = (th + Math.PI) / Math.Tau * 64; int k = (int)Math.Floor(f); double u = f - k; return ringR[i] * (1 + ringWob[(k + i * 9) & 63] * (1 - u) + ringWob[(k + 1 + i * 9) & 63] * u); }
    double RadAt(int j, double r) => radAng[j] + radWob[j][Math.Min(31, (int)(r / (R * 2) * 32))];
    (int a, int b) Sector(double x, double y)
    {
        switch (s.Shape)
        {
            case CityShape.Radial:
            {
                double dx = x - ox, dy = y - oy, r = Math.Sqrt(dx * dx + dy * dy), th = Math.Atan2(dy, dx); int i = 0;
                while (i < ringR.Length && r > RingAt(i, th)) i++;
                if (i == 0) return (0, 0);
                int m = radAng.Length, best = m - 1; for (int j = 0; j < m; j++) if (RadAt(j, r) <= th) best = j;
                return (i, best);
            }
            case CityShape.Grid:
            {
                double ca = Math.Cos(s.Angle), sa = Math.Sin(s.Angle), dx = x - ox, dy = y - oy, st = s.AxisKm * 1000;
                return ((int)Math.Floor((dx * ca + dy * sa) / st), (int)Math.Floor((-dx * sa + dy * ca) / st));
            }
            default:
            {
                var (u, v) = Frame(x, y); double vv = v - Off(u), W = s.StripWidthKm * 1000;
                int b = vv < 0 ? -1 : vv < .3 * W ? 0 : vv < .65 * W ? 1 : vv < W ? 2 : 3;
                return ((int)Math.Floor(u / 1900), b);
            }
        }
    }
    /// <summary>0 at the centre, 1 at the edge of the built city, more beyond. Never a clean circle: the edge wanders and the city runs out along its avenues.</summary>
    double Out(double x, double y)
    {
        double dx = x - ox, dy = y - oy, r = Math.Sqrt(dx * dx + dy * dy), th = Math.Atan2(dy, dx), t;
        double side = 1 + sideWob[(int)((th + Math.PI) / Math.Tau * 63.99)];
        switch (s.Shape)
        {
            case CityShape.Radial:
                t = r / (R * side);
                foreach (var a in radAng) { double d = Math.Abs(((th - a + Math.PI * 3) % Math.Tau) - Math.PI) * r; if (d < 420 && t > .5) { t = .5 + (t - .5) * .68; break; } }
                break;
            case CityShape.Grid:
            {
                double ca = Math.Cos(s.Angle), sa = Math.Sin(s.Angle), u = Math.Abs(dx * ca + dy * sa), v = Math.Abs(-dx * sa + dy * ca);
                t = Math.Pow(Math.Pow(u, 3.2) + Math.Pow(v, 3.2), 1 / 3.2) / (R * side);
                // no clear edge: houses run on out along the arterials
                if (s.Strict && t > .6) { double st = s.AxisKm * 1000, near = Math.Min(Math.Abs(Math.IEEERemainder(u, st)), Math.Abs(Math.IEEERemainder(v, st))); if (near < 340) t = .6 + (t - .6) * .68; }
                break;
            }
            default:
            {
                var (u, v) = Frame(x, y); double vv = v - Off(u), W = s.StripWidthKm * 1000 * (1 + .4 * sideWob[(int)(((u / R) + 2) * 15.9) & 63]);
                t = Math.Max(Math.Abs(u) / R, vv < 0 ? 9 : vv / W * .92); break;
            }
        }
        foreach (var q in sats) { double d = Math.Sqrt((x - q.x) * (x - q.x) + (y - q.y) * (y - q.y)) / q.r; if (d < 1.3) t = Math.Min(t, .72 + .3 * d); }
        return t + (N.V(x / 900, y / 900, p.Seed % 97) - .5) * .16;
    }

    // ---------------------------------------------------------------- avenues
    void Avenue(List<(double x, double y)> pts, double width, double maxOut = 1.3)
    {
        double maxCross = s.RiverW * 1.7 + 80;
        for (int i = 0; i < pts.Count;)
        {
            if (Free2Water(pts[i].x, pts[i].y)) { i++; continue; }
            int j = i; while (j < pts.Count - 1 && !Free2Water(pts[j + 1].x, pts[j + 1].y)) j++;
            double wetRun = 0; for (int k = i; k < j; k++) wetRun += Math.Sqrt(Math.Pow(pts[k + 1].x - pts[k].x, 2) + Math.Pow(pts[k + 1].y - pts[k].y, 2));
            if (wetRun > maxCross) for (int k = i; k <= j; k++) pts[k] = (-1e9, -1e9);
            i = j + 1;
        }
        var run = new List<(double x, double y)>();
        void Flush() { while (run.Count > 0 && !Free2Water(run[0].x, run[0].y)) run.RemoveAt(0); while (run.Count > 0 && !Free2Water(run[^1].x, run[^1].y)) run.RemoveAt(run.Count - 1); if (run.Count > 1) { var l = new Line(run, width, width); L.Roads.Add(l); MarkLine(l, 5, Way); } run = new(); }
        foreach (var q in pts)
        {
            if (q.x < -1e8) { Flush(); continue; }
            bool sea = w.Land(q.x, q.y, 256) < .05; bool wet = !Free2Water(q.x, q.y);
            if (sea || (wet && !s.Bridges) || Out(q.x, q.y) > maxOut) Flush(); else run.Add(q);
        }
        Flush();
    }
    bool Free2Water(double x, double y) { int i = (int)((x - mX) / MC), j = (int)((y - mY) / MC); return i < 0 || j < 0 || i >= mN || j >= mN || mask[j * mN + i] != Water; }
    void Arterials()
    {
        switch (s.Shape)
        {
            case CityShape.Radial:
                for (int j = 0; j < radAng.Length; j++)
                {
                    var pts = new List<(double, double)>(); double end = R * (1.1 + .35 * rng.NextDouble());
                    for (double r = ringR[0] * .9; r < end; r += 45) { double a = RadAt(j, r); pts.Add((ox + Math.Cos(a) * r, oy + Math.Sin(a) * r)); }
                    Avenue(pts, s.Avenue * .75);
                }
                for (int i = 0; i < ringR.Length; i++)
                {
                    var pts = new List<(double, double)>(); double step = 40 / ringR[i];
                    for (double a = -Math.PI; a <= Math.PI; a += step) { double r = RingAt(i, a); pts.Add((ox + Math.Cos(a) * r, oy + Math.Sin(a) * r)); }
                    Avenue(pts, i <= 1 ? s.Avenue : s.Avenue * .7, 1.0);     // the outer rings only exist where the city has reached them
                }
                break;
            case CityShape.Grid:
            {
                double ca = Math.Cos(s.Angle), sa = Math.Sin(s.Angle), st = s.AxisKm * 1000, span = R * 1.25;
                for (int a = -6; a <= 6; a++)
                {
                    double o = a * st; if (Math.Abs(o) > span) continue;
                    var u = new List<(double, double)>(); var v = new List<(double, double)>();
                    for (double t = -span; t <= span; t += 45) { u.Add((ox + ca * t - sa * o, oy + sa * t + ca * o)); v.Add((ox + ca * o - sa * t, oy + sa * o + ca * t)); }
                    Avenue(u, a == 0 ? s.Avenue * 1.3 : s.Avenue * .7, 1.08); Avenue(v, a == 0 ? s.Avenue * 1.3 : s.Avenue * .7, 1.08);
                }
                break;
            }
            default:
            {
                double W = s.StripWidthKm * 1000;
                foreach (double f in new[] { .3, .65 })
                {
                    var pts = new List<(double, double)>();
                    for (double u = -R * 1.05; u <= R * 1.05; u += 45) { double v = Off(u) + f * W; pts.Add((ox + tX * u + nX * v, oy + tY * u + nY * v)); }
                    Avenue(pts, f < .5 ? s.Avenue : s.Avenue * .65);
                }
                for (int a = (int)(-R / 1900); a <= (int)(R / 1900); a++)
                {
                    double u = a * 1900.0; var pts = new List<(double, double)>();
                    for (double t = 0; t < W; t += 45) { double v = Off(u) + t + s.RiverW * .5; pts.Add((ox + tX * u + nX * v, oy + tY * u + nY * v)); }
                    Avenue(pts, s.Avenue * .55);
                }
                break;
            }
        }
    }

    // ---------------------------------------------------------------- sectors, neighbourhoods, blocks
    void Sectors()
    {
        const double PatchM = 620;
        foreach (var (a, b, ang, u0, u1, v0, v1) in SectorFrames())
        {
            double ca = Math.Cos(ang), sa = Math.Sin(ang);
            (double x, double y) W(double u, double v) => (ox + ca * u - sa * v, oy + sa * u + ca * v);
            double ps = s.Strict ? PatchM : PatchM * (.8 + .5 * rng.NextDouble());
            int nu = Math.Max(1, (int)Math.Ceiling((u1 - u0) / ps)), nv = Math.Max(1, (int)Math.Ceiling((v1 - v0) / ps));
            for (int pi = 0; pi < nu; pi++)
                for (int pj = 0; pj < nv; pj++)
                {
                    double pu = u0 + pi * ps, pv = v0 + pj * ps; var c = W(pu + ps / 2, pv + ps / 2);
                    if (Sector(c.x, c.y) != (a, b)) { var cx = W(pu + ps * .15, pv + ps * .15); if (Sector(cx.x, cx.y) != (a, b)) { var cy = W(pu + ps * .85, pv + ps * .85); if (Sector(cy.x, cy.y) != (a, b)) continue; else c = cy; } else c = cx; }
                    var k = Pick(c.x, c.y, a, b); if (k == BK.None) continue;
                    var (bw, bh) = Size(k);
                    int cu = Math.Max(1, (int)Math.Round(ps / bw)), cv = Math.Max(1, (int)Math.Round(ps / bh));
                    // split lines are uneven, except where the plan is regular
                    double[] Cuts(int n)
                    {
                        var t = new double[n + 1]; double wob = s.Shape == CityShape.Grid ? .03 : k == BK.Old ? .32 : .12;
                        for (int q = 0; q <= n; q++) t[q] = (q == 0 || q == n) ? q / (double)n : (q + (rng.NextDouble() - .5) * wob * 2) / n;
                        return t;
                    }
                    var cuts = Cuts(cu); var cvts = Cuts(cv);
                    for (int i = 0; i < cu; i++)
                        for (int j = 0; j < cv; j++)
                        {
                            double a0 = pu + cuts[i] * ps, a1 = pu + cuts[i + 1] * ps, b0 = pv + cvts[j] * ps, b1 = pv + cvts[j + 1] * ps;
                            var q = new Quad();
                            (q.X0, q.Y0) = Warp(W(a0, b0)); (q.X1, q.Y1) = Warp(W(a1, b0)); (q.X2, q.Y2) = Warp(W(a1, b1)); (q.X3, q.Y3) = Warp(W(a0, b1));
                            Try(q, k, a, b, 0);
                        }
                }
        }
    }
    IEnumerable<(int a, int b, double ang, double u0, double u1, double v0, double v1)> SectorFrames()
    {
        (double, double, double, double) Bounds(IEnumerable<(double x, double y)> pts, double ang)
        {
            double ca = Math.Cos(ang), sa = Math.Sin(ang), u0 = 1e12, u1 = -1e12, v0 = 1e12, v1 = -1e12;
            foreach (var q in pts) { double dx = q.x - ox, dy = q.y - oy, u = dx * ca + dy * sa, v = -dx * sa + dy * ca; u0 = Math.Min(u0, u); u1 = Math.Max(u1, u); v0 = Math.Min(v0, v); v1 = Math.Max(v1, v); }
            return (u0, u1, v0, v1);
        }
        switch (s.Shape)
        {
            case CityShape.Radial:
            {
                int m = radAng.Length;
                for (int i = 0; i <= ringR.Length; i++)
                    for (int j = 0; j < (i == 0 ? 1 : m); j++)
                    {
                        double r0 = i == 0 ? 0 : ringR[i - 1] * .9, r1 = i < ringR.Length ? ringR[i] * 1.12 : R * 2, t0 = i == 0 ? -Math.PI : radAng[j] - .12, t1 = i == 0 ? Math.PI : (j + 1 < m ? radAng[j + 1] : radAng[0] + Math.Tau) + .12;
                        var pts = new List<(double, double)>();
                        for (int e = 0; e <= 6; e++) for (int f = 0; f <= 3; f++) { double th = t0 + (t1 - t0) * e / 6, r = r0 + (r1 - r0) * f / 3; pts.Add((ox + Math.Cos(th) * r, oy + Math.Sin(th) * r)); }
                        double ang = i == 0 ? p.RiverAng + (rng.NextDouble() - .5) : (t0 + t1) / 2 + (rng.NextDouble() - .5) * .3;
                        var (u0, u1, v0, v1) = Bounds(pts, ang);
                        // sector ids use the last radial whose angle is below the point, as Sector() does
                        yield return (i, j, ang, u0, u1, v0, v1);
                    }
                break;
            }
            case CityShape.Grid:
            {
                double ca = Math.Cos(s.Angle), sa = Math.Sin(s.Angle), st = s.AxisKm * 1000; int n = (int)(ReachM / st) + 1;
                for (int a = -n; a < n; a++) for (int b = -n; b < n; b++)
                    {
                        var pts = new[] { (a * st, b * st), ((a + 1) * st, b * st), (a * st, (b + 1) * st), ((a + 1) * st, (b + 1) * st) }.Select(t => (ox + ca * t.Item1 - sa * t.Item2, oy + sa * t.Item1 + ca * t.Item2));
                        double ang = s.Strict ? s.Angle : s.Angle + (rng.NextDouble() - .5) * .08 + (rng.NextDouble() < .25 ? Math.PI / 2 : 0);
                        var (u0, u1, v0, v1) = Bounds(pts, ang);
                        yield return (a, b, ang, u0 - 60, u1 + 60, v0 - 60, v1 + 60);
                    }
                break;
            }
            default:
            {
                double W = s.StripWidthKm * 1000;
                for (int a = (int)Math.Floor(-ReachM / 1900); a <= (int)Math.Floor(ReachM / 1900); a++)
                    for (int b = -1; b <= 3; b++)
                    {
                        double f0 = b == -1 ? -2.5 * W : b == 0 ? 0 : b == 1 ? .3 * W : b == 2 ? .65 * W : W, f1 = b == -1 ? 0 : b == 0 ? .3 * W : b == 1 ? .65 * W : b == 2 ? W : 3 * W, u0 = a * 1900.0, u1 = u0 + 1900;
                        var pts = new List<(double, double)>();
                        for (double u = u0; u <= u1; u += 380) foreach (double f in new[] { f0, f1 }) { double v = Off(u) + f; pts.Add((ox + tX * u + nX * v, oy + tY * u + nY * v)); }
                        double ang = p.RiverAng + (rng.NextDouble() - .5) * .1;
                        var (bu0, bu1, bv0, bv1) = Bounds(pts, ang);
                        yield return (a, b, ang, bu0, bu1, bv0, bv1);
                    }
                break;
            }
        }
    }
    (double x, double y) Warp((double x, double y) q)
    {
        double a = s.Warp, x = q.x, y = q.y, t = Out(x, y);
        double core = t < s.CoreT * 1.2 ? 2.2 : 1;       // old streets wander more
        return (x + (N.V(x / 620, y / 620, 331) - .5) * 2 * a * core + (N.V(x / 170, y / 170, 332) - .5) * a * (core - 1), y + (N.V(x / 620, y / 620, 333) - .5) * 2 * a * core + (N.V(x / 170, y / 170, 334) - .5) * a * (core - 1));
    }

    (double w, double h) Size(BK k) => k switch
    {
        BK.Old => (52, 66), BK.Tenement => (s.Deep ? 150 : 115, s.Deep ? 205 : 150), BK.Mixed => (95, 130), BK.Rows => (62, 150), BK.Barracks => (120, 150), BK.Cottage => (50, 150), BK.Superblock => (230, 310),
        BK.Villa => (90, 130), BK.Works => (250, 330), BK.Park => (310, 310), BK.Cemetery => (260, 340), BK.Allot => (150, 190), BK.Square => (120, 120),
        BK.Downtown => (86, 104), BK.Frame => (112, 190), BK.Walkup => (96, 130), BK.Slab => (170, 230), BK.WoodTown => (84, 100), BK.Airy => (120, 150), BK.Rock => (96, 96), BK.Plant => (440, 560), BK.Timber => (190, 240), _ => (100, 100),
    };
    double StreetW(BK k) => k switch { BK.Old => 5, BK.Tenement => 15, BK.Superblock => 22, BK.Works => 16, BK.Mixed => 11, BK.Rows => 9, BK.Villa => 10, BK.Barracks => 9, BK.Cottage => 9, BK.Allot => 5, BK.Downtown => 16, BK.Frame => 13, BK.Walkup => 13, BK.Slab => 16, BK.WoodTown => 20, BK.Airy => 16, BK.Rock => 4, BK.Plant => 20, BK.Timber => 10, _ => 10 };

    /// <summary>What a neighbourhood is. Old town in the middle, then bands of housing as the style says; works by rail and water, or in a quarter of their own; parks and cemeteries here and there.</summary>
    BK Pick(double x, double y, int a, int b)
    {
        double t = Out(x, y);
        if (t > 1.38) return BK.None;
        int sd = p.Seed; double wx = x + (N.V(x / 900, y / 900, sd + 21) - .5) * 800, wy = y + (N.V(x / 900, y / 900, sd + 22) - .5) * 800;
        int hx = (int)Math.Floor(wx / 560), hy = (int)Math.Floor(wy / 560);
        double h1 = N.Hash(hx, hy, sd + 11), h2 = N.Hash(hx, hy, sd + 12), h3 = N.Hash(hx, hy, sd + 13), h4 = N.Hash((int)Math.Floor(wx / 1700), (int)Math.Floor(wy / 1700), sd + 14);
        if (t < s.CoreT) return s.Shape == CityShape.Strip && b != 0 && b != 1 ? BK.Mixed : s.CoreKind;
        if (s.Rock > 0 && t < 1.25 && N.V(x / 640, y / 640, sd + 31) * .7 + N.V(x / 230, y / 230, sd + 32) * .3 < s.Rock) return BK.Rock;      // left as it was: bare rock and pine
        bool strip = s.Shape == CityShape.Strip; double uPos = strip ? Frame(x, y).u / R : 0;
        double ind = s.Industry * (t < s.InnerT * .7 ? .15 : t > 1.1 ? .4 : 1);
        if (Near(railNear, x, y) && h1 < ind) return s.WorksKind;
        if (Near(waterNear, x, y) && h1 < ind * 1.1) return s.WaterWorks;
        if (h4 < s.Industry * .2 && t > s.InnerT && t < 1 && h2 < .75) return s.WorksKind;     // a quarter of its own
        if (strip && uPos > .42 && b <= 1 && h2 < .72) return s.WorksKind;
        if (strip && b == 0 && h2 < s.Industry * .9) return s.WaterWorks;      // the river bank is lined with works
        if (h3 < .035 && t < 1.1) return BK.Park;
        if (h3 > .955 && t > .55 && t < 1.25) return BK.Cemetery;
        int band = t < s.InnerT ? 0 : t < s.MidT ? 1 : t < 1 ? 2 : 3;
        var mix = s.Mix[band]; double total = mix.Sum(m => m.w), roll = h2 * total;
        foreach (var (k, wt) in mix) { roll -= wt; if (roll <= 0) return k; }
        return mix[^1].k;
    }

    void Try(Quad q, BK k, int a, int b, int depth)
    {
        double lu = q.Lu, lv = q.Lv; if (lu < 22 || lv < 22) return;
        var mid = q.Center; bool ok = Sector(mid.x, mid.y) == (a, b); int n = Math.Max(lu, lv) > 200 ? 5 : 3;
        for (int i = 0; i < n && ok; i++) for (int j = 0; j < n && ok; j++)
            {
                var c = q.Map((i + .08) / (n - 1 + .16), (j + .08) / (n - 1 + .16));
                if (!Free(c.x, c.y)) ok = false;
            }
        if (!ok)
        {
            if (depth >= 4) return;
            Quad A = q, B = q;
            if (lu >= lv)
            {
                var m0 = q.Map(.5, 0); var m1 = q.Map(.5, 1);
                A.X1 = m0.x; A.Y1 = m0.y; A.X2 = m1.x; A.Y2 = m1.y; B.X0 = m0.x; B.Y0 = m0.y; B.X3 = m1.x; B.Y3 = m1.y;
            }
            else
            {
                var m0 = q.Map(0, .5); var m1 = q.Map(1, .5);
                A.X3 = m0.x; A.Y3 = m0.y; A.X2 = m1.x; A.Y2 = m1.y; B.X0 = m0.x; B.Y0 = m0.y; B.X1 = m1.x; B.Y1 = m1.y;
            }
            Try(A, k, a, b, depth + 1); Try(B, k, a, b, depth + 1); return;
        }
        var ctr = q.Center; double t = Out(ctr.x, ctr.y);
        double dens = k is BK.Works or BK.Plant or BK.Timber or BK.Rock ? 1 : t < .9 ? 1 : Math.Max(0, 1 - (t - .9) / .48);
        if (t > .55 && t < .9) dens = 1 - (t - .55) * .35;
        if (rng.NextDouble() > dens) return;
        var core = new Quad(); (core.X0, core.Y0) = q.Map(.2, .2); (core.X1, core.Y1) = q.Map(.8, .2); (core.X2, core.Y2) = q.Map(.8, .8); (core.X3, core.Y3) = q.Map(.2, .8);
        MarkQuad(core, Taken);      // only the middle: neighbours share an edge
        Accept(q, k);
    }
}
