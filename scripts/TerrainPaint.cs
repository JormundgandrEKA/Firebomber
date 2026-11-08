using System;
using System.Collections.Generic;
using System.Linq;

namespace Firebomber;

/// <summary>Paints one 256-pixel terrain tile from the world. Two looks of the same data: recon pixel (near) and inked chart (far). No Godot types, so tiles can be made on worker threads.</summary>
public static class TerrainPaint
{
    public const int T = 256; const int M = 2;
    const byte Land = 0, Sea = 1, Inland = 2, Road = 3, Rail = 4, Bridge = 5, Street = 6, Roof = 7, Lane = 8, Ground = 9, Urban = 10;
    static readonly int[,] B4 = { { 0, 8, 2, 10 }, { 12, 4, 14, 6 }, { 3, 11, 1, 9 }, { 15, 7, 13, 5 } };
    static double Bay(long x, long y) => (B4[y & 3, x & 3] + .5) / 16;
    static (double r, double g, double b) C(int h) => (h >> 16 & 255, h >> 8 & 255, h & 255);
    static (double r, double g, double b) Mul((double r, double g, double b) c, double k) => (c.r * k, c.g * k, c.b * k);

    public static byte[] Render(World w, int mpp, int ix, int iy, bool ink)
    {
        int S = T + 2 * M, n = S * S; double size = T * (double)mpp, x0 = ix * size - M * mpp, yTop = (iy + 1) * size + M * mpp;
        var kind = new byte[n]; var aux = new byte[n]; var dir = new byte[n]; var fld = new float[n]; var hgt = new float[n]; var fst = new float[n]; var urb = new float[n]; var parcel = new int[n]; var far = new int[n];
        double X(int px) => x0 + (px + .5) * mpp; double Y(int py) => yTop - (py + .5) * mpp;
        var places = w.PlacesNear(x0, yTop - S * mpp, x0 + S * mpp, yTop);
        var towns = places.Where(p => p.Layout != null).Select(p => p.Layout!).ToList();
        for (int py = 0; py < S; py++)
            for (int px = 0; px < S; px++)
            {
                int i = py * S + px; double wx = X(px), wy = Y(py);
                double f = w.Land(wx, wy, mpp); fld[i] = (float)f;
                kind[i] = f < 0 ? Sea : w.InLake(wx, wy, mpp) ? Inland : Land;
                hgt[i] = (float)w.Elev(wx, wy, mpp);
                if (kind[i] != Land) continue;
                double u = World.Urb(wx, wy, places, mpp); foreach (var t in towns) u = Math.Max(u, t.Soft(wx, wy)); urb[i] = (float)u;
                fst[i] = (float)(w.Forest(wx, wy, mpp) - u * .7);
                if (mpp <= 64)
                {
                    double ax = wx + (N.V(wx / 3400, wy / 3400, 25) - .5) * 460 + (N.Oct(wx, wy, 21, 800, 2, mpp) - .5) * 220, ay = wy + (N.V(wx / 3400, wy / 3400, 26) - .5) * 460 + (N.Oct(wx, wy, 23, 800, 2, mpp) - .5) * 220;
                    int ci = (int)Math.Floor(ax / 340), cj = (int)Math.Floor(ay / 250), m = 1 + (int)(N.Hash(ci, cj, 5) * 3.99); bool d = N.Hash(ci, cj, 6) < .5;
                    int sub = (int)((d ? ax / 340 - ci : ay / 250 - cj) * m);
                    parcel[i] = (ci * 7919 + cj) * 5 + sub; dir[i] = (byte)(d ? 1 : 0);
                }
                else parcel[i] = (int)Math.Floor(wx / 1500) * 7919 + (int)Math.Floor(wy / 1200);
            }

        // --- stamps, in world positions, so the same line falls in the same place in every tile
        void Stamp(double wx, double wy, double rPx, byte k)
        {
            int cx = (int)Math.Floor((wx - x0) / mpp), cy = (int)Math.Floor((yTop - wy) / mpp), r = (int)Math.Floor(rPx);
            for (int yy = cy - r; yy <= cy + r; yy++) for (int xx = cx - r; xx <= cx + r; xx++)
                {
                    if (xx < 0 || yy < 0 || xx >= S || yy >= S) continue;
                    int i = yy * S + xx; byte old = kind[i];
                    if (k == Inland) { if (old == Land) kind[i] = Inland; continue; }
                    if (old == Sea) continue;
                    kind[i] = old == Inland || old == Bridge ? Bridge : k;
                }
        }
        void Lines(List<Line> ls, byte k)
        {
            foreach (var l in ls)
                foreach (int s in l.Near(x0, yTop - S * mpp, x0 + S * mpp, yTop))
                {
                    var a = l.P[s]; var b = l.P[s + 1]; double mg = 400 + l.WidthAt(s) / 2; if (Math.Max(a.x, b.x) < x0 - mg || Math.Min(a.x, b.x) > x0 + S * mpp + mg || Math.Max(a.y, b.y) < yTop - S * mpp - mg || Math.Min(a.y, b.y) > yTop + mg) continue;
                    double dx = b.x - a.x, dy = b.y - a.y, len = Math.Sqrt(dx * dx + dy * dy), r = Math.Max(0, l.WidthAt(s) / 2 / mpp - .5);
                    int steps = Math.Max(1, (int)(len / Math.Max(mpp * .6, r * mpp * .5)));
                    for (int t = 0; t <= steps; t++) Stamp(a.x + dx * t / steps, a.y + dy * t / steps, r, k);
                }
        }
        Lines(w.Rivers, Inland);
        void Fill(Quad q, byte k, byte ax, bool keepRoads)
        {
            int px0 = Math.Max(0, (int)Math.Floor((q.MinX - x0) / mpp)), px1 = Math.Min(S - 1, (int)Math.Ceiling((q.MaxX - x0) / mpp)), py0 = Math.Max(0, (int)Math.Floor((yTop - q.MaxY) / mpp)), py1 = Math.Min(S - 1, (int)Math.Ceiling((yTop - q.MinY) / mpp));
            for (int yy = py0; yy <= py1; yy++) for (int xx = px0; xx <= px1; xx++)
                {
                    int i = yy * S + xx; byte old = kind[i]; if (old == Sea || (k != Inland && (old == Inland || old == Bridge))) continue;
                    if (q.Has(x0 + (xx + .5) * mpp, yTop - (yy + .5) * mpp)) { kind[i] = k; aux[i] = ax; }
                }
        }
        foreach (var t in towns) foreach (var b in t.Basins) Fill(b, Inland, 0, false);
        if (mpp <= 16) Lanes(w, x0, yTop, S, mpp, Stamp);
        if (mpp <= 16) Farms(w, x0, yTop, S, mpp, kind, aux, fst);
        double tx0 = x0, tx1 = x0 + S * mpp, ty0 = yTop - S * mpp, ty1 = yTop;
        if (mpp <= 16)
            foreach (var t in towns)
            {
                if (t.X1 < tx0 || t.X0 > tx1 || t.Y1 < ty0 || t.Y0 > ty1) continue;
                foreach (int bi in t.BlocksIn(tx0, ty0, tx1, ty1))
                {
                    ref var b = ref t.Blocks[bi];
                    if (b.Outer.MaxX < tx0 || b.Outer.MinX > tx1 || b.Outer.MaxY < ty0 || b.Outer.MinY > ty1) continue;
                    if (mpp <= 4) { Fill(b.Outer, Street, b.Street, false); Fill(b.Inner, Ground, b.Ground, false); }
                    else
                    {
                        // far enough out, streets are thin: each block keeps a one-pixel margin so that the plan still reads
                        Fill(b.Outer, Street, b.Street, false); var o = b.Outer; double fu = Math.Min(.3, 7.5 / Math.Max(1, o.Lu)), fv = Math.Min(.3, 7.5 / Math.Max(1, o.Lv)); var q = new Quad();
                        (q.X0, q.Y0) = o.Map(fu, fv); (q.X1, q.Y1) = o.Map(1 - fu, fv); (q.X2, q.Y2) = o.Map(1 - fu, 1 - fv); (q.X3, q.Y3) = o.Map(fu, 1 - fv);
                        Fill(q, Ground, b.Ground, false);
                    }
                }
            }
        if (mpp <= 256) Lines(w.Roads, Road);
        if (mpp <= 256) Lines(w.Rails, Rail);
        if (mpp <= 64) foreach (var t in towns) { Lines(t.Roads, Road); if (mpp <= 16) Lines(t.Tracks, Rail); }
        if (mpp <= 16)
            foreach (var t in towns)
            {
                if (t.X1 < tx0 || t.X0 > tx1 || t.Y1 < ty0 || t.Y0 > ty1) continue;
                foreach (int pi in t.PlotsIn(tx0, ty0, tx1, ty1)) { ref var h = ref t.Plots[pi]; PlotRect(h, x0, yTop, S, mpp, kind, aux); }
            }
        if (mpp <= 16 && towns.Count > 0)
            for (int i = 0; i < n; i++) if (kind[i] == Land && urb[i] > .1 && (urb[i] - .1) * 5 > N.Hash(parcel[i], 0, 31)) { kind[i] = Ground; aux[i] = 2; }     // field by field, so there is no line where the city's ground begins
        if (mpp >= 64)
            foreach (var t in towns)
            {
                if (t.X1 < tx0 || t.X0 > tx1 || t.Y1 < ty0 || t.Y0 > ty1) continue;
                for (int i = 0; i < n; i++)
                {
                    if (kind[i] != Land && kind[i] != Road) continue;
                    var (cv, cl) = t.Far(x0 + (i % S + .5) * mpp, yTop - (i / S + .5) * mpp, mpp);
                    if (cv > 0 && N.Hash(i % S + ix * T, i / S + iy * T, 43) < cv * 1.15) { kind[i] = Urban; far[i] = cl; }
                }
            }
        if (mpp >= 64)
        {
            // too far to draw houses: roofs as a stipple that follows the same built-up shape as the houses do when closer
            for (int i = 0; i < n; i++)
                if (kind[i] == Land && urb[i] > .03 && N.Hash(i % S + ix * T, i / S + iy * T, 41) < Math.Min(.8, Math.Pow(urb[i], 1.3) * 1.2)) { kind[i] = Roof; aux[i] = (byte)(N.Hash(i % S + ix * T, i / S + iy * T, 42) * 3.99); }
            foreach (var p in places)
            {
                if (mpp >= 1024 && !p.City || mpp >= 256 && !p.City && p.R < 250) continue;
                int xx = (int)((p.X - x0) / mpp), yy = (int)((yTop - p.Y) / mpp);
                if (xx >= 0 && yy >= 0 && xx < S && yy < S && kind[yy * S + xx] == Land) { kind[yy * S + xx] = Roof; aux[yy * S + xx] = 0; }
            }
        }
        else foreach (var p in places) Settlement(w, p, places, x0, yTop, S, mpp, kind, aux, urb);

        // --- paint
        var pix = new byte[T * T * 4];
        long gx0 = (long)Math.Floor(x0 / mpp), gy0 = (long)Math.Floor(-yTop / mpp);
        double slopeK = 60 * Math.Pow(mpp / 4.0, .18);
        for (int py = 0; py < T; py++)
            for (int px = 0; px < T; px++)
            {
                int ax = px + M, ay = py + M, i = ay * S + ax; long gx = gx0 + ax, gy = gy0 + ay;
                bool shoreN = (kind[i] == Sea || kind[i] == Inland) != (kind[i - 1] == Sea || kind[i - 1] == Inland) || (kind[i] == Sea || kind[i] == Inland) != (kind[i + 1] == Sea || kind[i + 1] == Inland)
                           || (kind[i] == Sea || kind[i] == Inland) != (kind[i - S] == Sea || kind[i - S] == Inland) || (kind[i] == Sea || kind[i] == Inland) != (kind[i + S] == Sea || kind[i + S] == Inland);
                double slope = (hgt[i - S - 1] - hgt[i + S + 1]) / (2.83 * mpp) * slopeK;
                bool edge = parcel[i] != parcel[i + 1] || parcel[i] != parcel[i + S];
                bool shadow = kind[i - S - 1] == Roof;
                var ctx = new Ctx { K = kind[i], Aux = aux[i], Dir = dir[i], F = fld[i], H = hgt[i], Fo = fst[i], U = urb[i], Parcel = parcel[i], Gx = gx, Gy = gy, Mpp = mpp, Shore = shoreN, Slope = slope, Edge = edge, Shadow = shadow, North = World.NorthF(X(ax), Y(ay)) + 14 * ((mpp <= 64 ? N.Hash(parcel[i], 0, 9) : Bay(gx, gy)) - .5) < 196, Wx = X(ax), Wy = Y(ay), RoofEdge = kind[i - S] != Roof || kind[i - 1] != Roof, Far = far[i] };
                var c = ink ? Inked(ctx) : Recon(ctx);
                int o = (py * T + px) * 4;
                pix[o] = (byte)Math.Clamp(c.r, 0, 255); pix[o + 1] = (byte)Math.Clamp(c.g, 0, 255); pix[o + 2] = (byte)Math.Clamp(c.b, 0, 255); pix[o + 3] = 255;
            }
        return pix;
    }

    struct Ctx { public int Far; public byte K, Aux, Dir; public double F, H, Fo, U, Slope, Wx, Wy; public int Parcel, Mpp; public long Gx, Gy; public bool Shore, Edge, Shadow, North, RoofEdge; }

    // ---------------------------------------------------------------- what stands on the land
    static void Lanes(World w, double x0, double yTop, int S, int mpp, Action<double, double, double, byte> stamp)
    {
        const double Cell = 9000;
        for (int i = (int)Math.Floor(x0 / Cell) - 2; i <= (int)Math.Floor((x0 + S * mpp) / Cell) + 1; i++)
            for (int j = (int)Math.Floor((yTop - S * mpp) / Cell) - 2; j <= (int)Math.Floor(yTop / Cell) + 1; j++)
            {
                var a = w.VillageAt(i, j); if (a == null) continue;
                foreach (var (di, dj, s) in new[] { (1, 0, 901), (0, 1, 902) })
                {
                    var b = w.VillageAt(i + di, j + dj); if (b == null || N.Hash(i, j, s) > .7) continue;
                    double dx = b.X - a.X, dy = b.Y - a.Y, len = Math.Sqrt(dx * dx + dy * dy), nx = -dy / len, ny = dx / len;
                    if (Math.Max(a.X, b.X) < x0 - 500 || Math.Min(a.X, b.X) > x0 + S * mpp + 500 || Math.Max(a.Y, b.Y) < yTop - S * mpp - 500 || Math.Min(a.Y, b.Y) > yTop + 500) continue;
                    for (double t = 0; t <= len; t += mpp * .6)
                    {
                        double f = t / len, wob = 260 * Math.Sin(f * Math.PI) * N.S(f * 5, i * 31 + j);
                        stamp(a.X + dx * f + nx * wob, a.Y + dy * f + ny * wob, 0, Lane);
                    }
                }
            }
    }

    /// <summary>Lone farms scattered over the country, a few pixels of roof with their own position in the world.</summary>
    static void Farms(World w, double x0, double yTop, int S, int mpp, byte[] kind, byte[] aux, float[] fst)
    {
        const double Cell = 1300;
        for (int i = (int)Math.Floor(x0 / Cell); i <= (int)Math.Floor((x0 + S * mpp) / Cell); i++)
            for (int j = (int)Math.Floor((yTop - S * mpp) / Cell); j <= (int)Math.Floor(yTop / Cell); j++)
            {
                if (N.Hash(i, j, 911) > .34) continue;
                int count = 1 + (int)(N.Hash(i, j, 912) * 3.4); double fx = (i + N.Hash(i, j, 913)) * Cell, fy = (j + N.Hash(i, j, 914)) * Cell;
                for (int k = 0; k < count; k++)
                {
                    double hx = fx + (N.Hash(i + k, j, 915) - .5) * 44, hy = fy + (N.Hash(i, j + k, 916) - .5) * 44; int wide = mpp <= 4 ? 3 : 1, deep = mpp <= 4 ? 2 : 1;
                    int cx = (int)Math.Floor((hx - x0) / mpp), cy = (int)Math.Floor((yTop - hy) / mpp);
                    for (int yy = cy; yy < cy + deep; yy++) for (int xx = cx; xx < cx + wide; xx++)
                        if (xx >= 0 && yy >= 0 && xx < S && yy < S && kind[yy * S + xx] == Land && fst[yy * S + xx] < .55) { kind[yy * S + xx] = Roof; aux[yy * S + xx] = (byte)((i + j + k) & 3); }
                }
            }
    }

    static void PlotRect(in Plot h, double x0, double yTop, int S, int mpp, byte[] kind, byte[] aux)
    {
        double c = Math.Cos(h.Ang), s = Math.Sin(h.Ang), r = Math.Sqrt(h.W * h.W + h.D * h.D) / 2 + mpp; bool shaped = mpp <= 4 || h.W > mpp * 1.5 || h.D > mpp * 1.5;
        int px0 = Math.Max(0, (int)((h.X - r - x0) / mpp)), px1 = Math.Min(S - 1, (int)((h.X + r - x0) / mpp)), py0 = Math.Max(0, (int)((yTop - h.Y - r) / mpp)), py1 = Math.Min(S - 1, (int)((yTop - h.Y + r) / mpp));
        for (int yy = py0; yy <= py1; yy++)
            for (int xx = px0; xx <= px1; xx++)
            {
                double ox = x0 + (xx + .5) * mpp - h.X, oy = yTop - (yy + .5) * mpp - h.Y, lx = ox * c + oy * s, ly = -ox * s + oy * c;
                if (shaped && (Math.Abs(lx) > h.W / 2 || Math.Abs(ly) > h.D / 2)) continue;
                if (!shaped && (Math.Abs(ox) > mpp * .5 || Math.Abs(oy) > mpp * .5)) continue;
                int i = yy * S + xx; byte old = kind[i]; if (old == Sea || old == Inland || old == Bridge || old == Rail || old == Road) continue;
                kind[i] = Roof; aux[i] = h.Roof;
            }
    }

    static void Settlement(World w, Place p, List<Place> places, double x0, double yTop, int S, int mpp, byte[] kind, byte[] aux, float[] urb)
    {
        void Rect(double cx, double cy, double wd, double dp, double ang, byte k, byte ax)
        {
            double c = Math.Cos(ang), s = Math.Sin(ang), r = Math.Sqrt(wd * wd + dp * dp) / 2 + mpp;
            int px0 = (int)((cx - r - x0) / mpp), px1 = (int)((cx + r - x0) / mpp), py0 = (int)((yTop - cy - r) / mpp), py1 = (int)((yTop - cy + r) / mpp);
            for (int yy = Math.Max(0, py0); yy <= Math.Min(S - 1, py1); yy++)
                for (int xx = Math.Max(0, px0); xx <= Math.Min(S - 1, px1); xx++)
                {
                    double ox = x0 + (xx + .5) * mpp - cx, oy = yTop - (yy + .5) * mpp - cy, lx = ox * c + oy * s, ly = -ox * s + oy * c;
                    bool shaped = mpp <= 4 || wd > mpp * 1.5;
                    if (shaped && (Math.Abs(lx) > wd / 2 || Math.Abs(ly) > dp / 2)) continue;
                    if (!shaped && (Math.Abs(ox) > mpp * .5 || Math.Abs(oy) > mpp * .5)) continue;     // too small to have a shape: one pixel
                    int i = yy * S + xx; byte old = kind[i];
                    if (old == Sea || old == Inland || old == Bridge || old == Rail || old == Road) continue;
                    if (k == Street && old == Roof) continue;
                    kind[i] = k; aux[i] = ax;
                }
        }
        double tx0 = x0, tx1 = x0 + S * mpp, ty0 = yTop - S * mpp, ty1 = yTop;
        if (p.X + 3 * p.R < tx0 || p.X - 3 * p.R > tx1 || p.Y + 3 * p.R < ty0 || p.Y - 3 * p.R > ty1) return;
        if (!p.City)
        {
            int cnt = 30 + (int)(p.R / 5);
            for (int k = 0; k < cnt; k++)
            {
                double a = N.Hash(p.Seed, k, 43) * 6.28, q = p.R * Math.Sqrt(N.Hash(p.Seed, k, 44)), hx = p.X + q * Math.Cos(a), hy = p.Y + q * Math.Sin(a);
                if (hx < tx0 || hx > tx1 || hy < ty0 || hy > ty1) continue;
                Rect(hx, hy, 8 + 6 * N.Hash(p.Seed, k, 45), 7 + 4 * N.Hash(p.Seed, k, 46), p.Ang, Roof, (byte)(k & 3));
            }
            return;
        }
        CityPlan.Enumerate(w, p, places, tx0 - 100, ty0 - 100, tx1 + 100, ty1 + 100, h => Rect(h.X, h.Y, h.W, h.D, h.Ang, Roof, mpp <= 4 ? h.Roof : h.BlockRoof),
            mpp <= 4 ? (x, y, wd, dp, ang) => Rect(x, y, wd, dp, ang, Street, 0) : null);
    }

    static (double dx, double dy, double d) Crown(long x, long y, int cell)
    {
        long cx = x >= 0 ? x / cell : (x - cell + 1) / cell, cy = y >= 0 ? y / cell : (y - cell + 1) / cell; double best = 1e9, bx = 0, by = 0;
        for (int j = -1; j <= 1; j++) for (int i = -1; i <= 1; i++)
            {
                double tx = (cx + i + N.Hash((int)(cx + i), (int)(cy + j), 61)) * cell, ty = (cy + j + N.Hash((int)(cx + i), (int)(cy + j), 62)) * cell, dd = (x - tx) * (x - tx) + (y - ty) * (y - ty);
                if (dd < best) { best = dd; bx = x - tx; by = y - ty; }
            }
        return (bx, by, Math.Sqrt(best));
    }

    // ---------------------------------------------------------------- recon pixel (near)
    static readonly int[] SouthCrops = { 0x564632, 0x4E5C36, 0x7C7042, 0x686242, 0x5C683A, 0x484C34 }, NorthCrops = { 0x44583A, 0x4E5C3C, 0x5C5A40, 0x3E5036, 0x62603E, 0x485438 };
    /// <summary>0-7 as the placeholder town uses them (5-7 are north-light factory roofs); then zinc, tar, weathered wood, copper, thatch, pale concrete, slate, dark tile; from 16 the Vulfen roofs.</summary>
    public static readonly int[] Roofs = { 0x7E4A3E, 0x8C6C48, 0x5C6268, 0x965C46, 0x7A5E44, 0x565C62, 0x4C5258, 0x565C62, 0x6C7276, 0x3E3A38, 0x6C6050, 0x5E8A78, 0x7A7454, 0x7E7A6E, 0x4A4E58, 0x6A423A,
        0xA4623C, 0xC9B98A, 0x8E3E30, 0x55603C, 0xB8AE96, 0x6E6A64, 0x5E4E40, 0x2E2C2A, 0x8A8478, 0x7A4A3C };      // Vulfen: painted wood (ochre-red, pale, falu), turf, flat slab; shingle grey and brown, flat tar, masonry, brick
    /// <summary>Paved, yard, garden, park, cinders, quay, allotments, graves, rock.</summary>
    public static readonly int[] GroundCols = { 0x4E4A44, 0x54503E, 0x4C5A36, 0x3A4E30, 0x48443E, 0x68645A, 0x505C3C, 0x44523A, 0x5C5E58 };
    static (double r, double g, double b) Recon(Ctx c)
    {
        double bay = Bay(c.Gx, c.Gy); int mpp = c.Mpp;
        if (c.K == Sea)
        {
            if (c.Shore && mpp <= 64) return C(0x78969A);
            if (c.F + .05 * bay > -.05 * (mpp / 4.0 > 40 ? 40 : mpp / 4.0)) return C(0x345C68);
            if (c.F - .6 * bay > -.9) return C(0x284C5C);
            return N.V(c.Wx / 90000, c.Wy / 90000, 97) + .05 * bay > .52 ? C(0x223F52) : C(0x1E3C4E);
        }
        if (c.K == Inland) return mpp <= 16 && c.Shore ? C(0x4C7482) : C(0x284C5C);
        if (c.K == Road) return mpp >= 64 ? C(0x6E6A58) : C(0x706C5E); if (c.K == Lane) return C(0x6C6650); if (c.K == Urban) return Mul(C(c.Far), .92 + N.Hash((int)c.Gx, (int)c.Gy, 44) * .16);
        if (c.K == Street) { var st = c.Aux == 2 ? C(GroundCols[8]) : c.Aux == 1 ? C(0x5C5040) : C(0x605C52); return c.Shadow && mpp <= 4 ? Mul(st, .62) : st; }
        if (c.K == Ground) { var g = C(GroundCols[c.Aux & 15]); if (c.Aux == 8) { double h = N.Hash((int)(c.Gx / 3), (int)(c.Gy / 3), 47), lump = N.V(c.Wx / 70, c.Wy / 70, 48); g = lump > .55 && h < .7 ? C(h < .3 ? 0x20301E : 0x2C3E26) : h < .25 ? C(0x4A4C48) : h < .4 ? C(0x6C6E66) : g; } else if (c.Aux == 6) { double h = N.Hash((int)(c.Gx / 3), (int)(c.Gy / 3), 45); g = h < .3 ? C(0x6E6A40) : h < .6 ? C(0x4C6A3A) : g; } else if (c.Aux == 7 && (c.Gx + c.Gy * 3) % 5 == 0) g = C(0x707468); else if (c.Aux == 3 && mpp <= 4 && N.Hash((int)(c.Gx / 3), (int)(c.Gy / 3), 46) < .14) g = C(0x24361F); return c.Shadow && mpp <= 4 ? Mul(g, .58) : g; } if (c.K == Bridge) return C(0x968E7C);
        if (c.K == Rail) return mpp >= 64 ? C(0x4A4438) : (c.Gx + c.Gy) % 4 < 2 ? C(0x282624) : C(0x54493C);
        if (c.K == Roof) { var r = C(Roofs[c.Aux & 31]); if (mpp <= 4 && (c.Aux & 31) >= 5 && (c.Aux & 31) <= 7 && c.Gy % 3 == 0) r = Mul(r, .72); if (mpp >= 16) { var d = C(0x6A5C50); double t = mpp >= 64 ? .67 : .55; r = (r.r * (1 - t) + d.r * t, r.g * (1 - t) + d.g * t, r.b * (1 - t) + d.b * t); } return mpp <= 4 && c.RoofEdge ? Mul(r, 1.18) : r; }
        if (c.Shore && (mpp <= 16 || c.F < .3)) return C(0x8C7C5C);
        double lit = Math.Floor(Math.Clamp(c.Slope, -2, 2) * 5 + bay) / 5 * .16 + 1;
        if (c.H - 14 * bay > 990) return Mul(C(0xBCC6C8), lit);
        if (c.H - 6 * bay > 830) return Mul(C(0x5C5E56), lit);
        if (c.Fo > .56 + .02 * bay)
        {
            if (mpp >= 64) return Mul(N.Hash((int)c.Gx, (int)c.Gy, 63) < .5 ? C(0x24361F + 0x030503) : C(0x1E2E20), lit);
            if (mpp == 16) { double q = N.Hash((int)c.Gx, (int)c.Gy, 63); return Mul(q < .3 ? C(0x3A5032) : q < .75 ? C(0x283C28) : C(0x18221A), lit); }
            var t = Crown(c.Gx, c.Gy, 5);
            if (t.d > 2.6) return Mul(C(0x18221A), lit);
            return Mul(t.dx + t.dy < -.5 ? C(0x3A5032) : t.dx + t.dy > 1 ? C(0x202E20) : C(0x283C28), lit);
        }
        var crops = c.North ? NorthCrops : SouthCrops;
        int ci = (int)(N.Hash(c.Parcel, 0, 7) * 5.99);
        var col = C(crops[ci]);
        if (mpp >= 16) { var m = c.North ? C(0x4A5A3A) : C(0x5E5C3A); double t = mpp >= 256 ? .5 : mpp >= 64 ? .68 : .3; col = (col.r * (1 - t) + m.r * t, col.g * (1 - t) + m.g * t, col.b * (1 - t) + m.b * t); }   // far away, fields merge into one tone
        if (mpp <= 4 && (ci == 0 || ci == 2 || ci == 3) && ((c.Dir == 1 ? c.Gx : c.Gy) / 2) % 2 == 0) col = Mul(col, .9);
        if (c.U > .03) { var g = C(0x4E5838); double t = Math.Min(.75, c.U * 1.6); col = (col.r * (1 - t) + g.r * t, col.g * (1 - t) + g.g * t, col.b * (1 - t) + g.b * t); }
        if (c.Edge && mpp <= 16) return mpp <= 4 && N.Hash((int)(c.Gx / 3), (int)(c.Gy / 3), 14) < .3 ? C(0x202E1E) : Mul(col, mpp <= 4 ? .68 : .84);
        if (c.Shadow && mpp <= 4) col = Mul(col, .55);
        return Mul(col, lit);
    }

    // ---------------------------------------------------------------- inked chart (far): the same land as the old big map's style
    static readonly int[] PaperFields = { 0xD4CB96, 0xC9C28C, 0xCFC48E, 0xBDBF88 };
    static (double r, double g, double b) Inked(Ctx c)
    {
        double bay = Bay(c.Gx, c.Gy);
        bool grat = Math.Abs(((c.Wx % 216000) + 216000) % 216000 - 108000) > 108000 - c.Mpp * .5 || Math.Abs(((c.Wy % 162000) + 162000) % 162000 - 81000) > 81000 - c.Mpp * .5;
        (double r, double g, double b) col;
        if (c.K == Sea)
        {
            if (c.Shore) return C(0x4C5E5C);
            col = c.F > -2.5 ? C(0xA6BEC2) : C(0xB2C6CA);
        }
        else if (c.K == Inland) return C(0x5F8CA0);
        else if (c.K == Road) return C(0x9A8A60);
        else if (c.K == Rail || c.K == Bridge) return (c.Gx + c.Gy) % 3 < 2 ? C(0x3C3226) : C(0xCFC48E);
        else if (c.K == Roof || c.K == Urban) return C(0x5C4638);
        else if (c.Shore) return C(0x4C5E5C);
        else
        {
            if (c.H - 14 * bay > 990) col = C(0xECECE4);
            else if (c.H - 6 * bay > 830) col = C(0xC8C4A8);
            else if (c.Fo > .56 + .02 * bay) col = c.North ? C(0x96A87E) : C(0xA0B082);
            else col = c.North ? (N.Hash(c.Parcel, 0, 7) < .5 ? C(0xAAB68E) : C(0xB4BE94)) : C(PaperFields[(int)(N.Hash(c.Parcel, 0, 7) * 3.99)]);
            if (c.Slope < -1.1 && (c.Gx + c.Gy) % 3 == 0) col = Mul(col, .84);
        }
        return grat ? Mul(col, .9) : col;
    }
}
