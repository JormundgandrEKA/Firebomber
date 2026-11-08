using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Firebomber;

public enum Craft { V16, U4, F5, TB6, SB3, I8, I7 }

/// <summary>Pixel-art aircraft, painted pixel by pixel from each design's real dimensions (metres). Every aircraft is painted at several resolutions, the same way, and the game
/// draws whichever puts one art pixel on one or a little more screen pixels. So a sprite is crisp pixel art at any zoom, and sizes between designs are always true.</summary>
public static class Sprites
{
    internal record struct Plan(double URoot, double LeR, double TeR, double UTip, double LeT, double TeT);
    internal record struct Fin(double U, double V0, double V1, double W);
    internal record struct Eng(double U, double V0, double V1, double W, double Pr = 0, bool Gun = false, bool Boom = false, double Nose = .9);
    internal record struct Glass(double V0, double V1, double F, double U0 = 0);
    internal record struct Ball(double U, double V, double R);
    public sealed class Spec
    {
        public string Name = ""; public double Span, Len, NoseDark, BandV0, BandV1; public bool Metal, Wolf; public int Accent;
        internal (double v, double h)[] Fus = { }; internal Plan Wing, Tail; internal Fin[] Fins = { }; internal Eng[] Engines = { }; internal Glass[] Glazing = { };
        internal Ball[] Turrets = { }, Props = { }; internal Ball? Roundel;
    }
    public static readonly Dictionary<Craft, Spec> Specs = new()
    {
        [Craft.V16] = new() { Name = "V-16 Silverhair", Span = 40, Len = 34, Metal = true, Wolf = true, Accent = 0x3F6FB5, NoseDark = 2.6, BandV0 = 25, BandV1 = 26,
            Fus = new[] { (0, .25), (1, .7), (3, 1.2), (6, 1.3), (9, .85), (12.5, 1.5), (16, 1.85), (19, 1.75), (24, 1.0), (31, .45), (33.7, .2) },
            Wing = new(1.8, 12.6, 18.9, 20, 13.4, 16), Tail = new(0, 30.9, 33.7, 4.6, 30.9, 33.7), Fins = new Fin[] { new(0, 30.2, 33.7, .45) },
            Engines = new Eng[] { new(-9.5, 10.9, 14.4, 1.5, 1.5), new(-5.7, 9.6, 14.4, 1.5, 1.5), new(5.7, 9.6, 14.4, 1.5, 1.5), new(9.5, 10.9, 14.4, 1.5, 1.5) },
            Glazing = new Glass[] { new(.7, 4.8, .8), new(5.4, 6.6, .7) }, Turrets = new Ball[] { new(0, 8.6, .7), new(0, 22.1, .8) }, Roundel = new(14.5, 14.9, 1) },
        [Craft.U4] = new() { Name = "U-4 Doppel", Span = 17, Len = 12.4, Metal = true, Wolf = true, Accent = 0x3F6FB5, NoseDark = 1.2,
            Fus = new[] { (0, .0), (1, .55), (2.1, .9), (3.6, .75), (5.5, .4), (8, .12) },
            Wing = new(.5, 3.7, 8, 8.5, 4.3, 5.4), Tail = new(0, 11.5, 12.4, 4.3, 11.5, 12.4), Fins = new Fin[] { new(3.4, 10.6, 12.4, .4), new(-3.4, 10.6, 12.4, .4) },
            Engines = new Eng[] { new(-3.4, 2.6, 5.8, 1.3, 1.5), new(3.4, 2.6, 5.8, 1.3, 1.5), new(-3.4, 5.5, 12.2, 1, Boom: true), new(3.4, 5.5, 12.2, 1, Boom: true),
                new(-.5, -.5, 1.6, .24, Gun: true), new(-.17, -.5, 1.6, .24, Gun: true), new(.17, -.5, 1.6, .24, Gun: true), new(.5, -.5, 1.6, .24, Gun: true) },
            Glazing = new Glass[] { new(1.4, 4.2, .5) }, Roundel = new(6.2, 5.1, .6) },
        [Craft.F5] = new() { Name = "F-5 Hund", Span = 9, Len = 8.6, Metal = true, Wolf = true, Accent = 0x3F6FB5, NoseDark = 1.6,
            Fus = new[] { (0, .28), (.6, .5), (2, .62), (4.2, .65), (6.2, .42), (7.9, .22), (8.6, .1) },
            Wing = new(.6, 2.8, 5.1, 4.5, 3, 3.9), Tail = new(0, 7.7, 8.5, 1.3, 7.8, 8.4), Fins = new Fin[] { new(0, 6.9, 8.6, .25) }, Props = new Ball[] { new(0, .15, 1.5) },
            Engines = new Eng[] { new(-1.2, 2.3, 3.2, .22, Gun: true), new(1.2, 2.3, 3.2, .22, Gun: true), new(-1.9, 2.5, 3.3, .22, Gun: true), new(1.9, 2.5, 3.3, .22, Gun: true) },
            Glazing = new Glass[] { new(2.9, 4.5, .5) }, Roundel = new(2.6, 3.9, .4) },
        [Craft.TB6] = new() { Name = "TB-6", Span = 34, Len = 22.4, Accent = 0xB02A22,
            Fus = new[] { (0, .3), (1, .8), (3, 1.5), (6, 1.65), (14, 1.65), (18, 1.2), (21, .65), (22.4, .3) },
            Wing = new(1.7, 7, 10.7, 17, 7.3, 10.2), Tail = new(0, 19.8, 22.2, 5.5, 19.9, 22.2), Fins = new Fin[] { new(0, 19.4, 22.2, .5) },
            Engines = new Eng[] { new(-9.2, 5, 9.3, 1.9, 1.8), new(-5.2, 4.7, 9.8, 2, 1.9), new(5.2, 4.7, 9.8, 2, 1.9), new(9.2, 5, 9.3, 1.9, 1.8) },
            Glazing = new Glass[] { new(.8, 3, .55, -.4), new(3.9, 5.2, .35, .7), new(8.5, 10, .3, -.5) }, Turrets = new Ball[] { new(0, 22.8, .5) }, Roundel = new(12.5, 8.9, 1.4) },
        // redesigned toward the Soviet SB-2: long slim fuselage with a glazed nose, single fin, tapered wing, two long pointed engine nacelles reaching past the trailing edge
        [Craft.SB3] = new() { Name = "SB-3", Span = 20.3, Len = 14.6, Accent = 0xB02A22,
            Fus = new[] { (0, .12), (.5, .4), (1.6, .66), (3.2, .78), (6, .82), (9, .62), (12, .34), (14.3, .14) },
            Wing = new(.8, 4.3, 8.9, 10.15, 5.6, 7.3), Tail = new(0, 12.1, 13.9, 3.5, 12.6, 13.6), Fins = new Fin[] { new(0, 11.4, 14.4, .28) },
            Engines = new Eng[] { new(-3.5, 2.3, 9.9, 1.15, 1.65, Nose: 1.7), new(3.5, 2.3, 9.9, 1.15, 1.65, Nose: 1.7) },
            Glazing = new Glass[] { new(.15, 2, .86), new(3.6, 4.9, .62), new(7.9, 8.8, .5) }, Turrets = new Ball[] { new(0, 9.4, .42) }, Roundel = new(7, 6.3, .85) },
        [Craft.I8] = new() { Name = "I-8", Span = 9.6, Len = 7.2, Accent = 0xB02A22,
            Fus = new[] { (0, .55), (.5, .9), (1.6, .95), (3.2, .85), (5.2, .5), (6.6, .25), (7.2, .12) },
            Wing = new(.9, 2.2, 4.9, 4.8, 2.6, 4.1), Tail = new(0, 5.9, 6.9, 1.9, 6, 6.8), Fins = new Fin[] { new(0, 5.7, 7.2, .25) }, Props = new Ball[] { new(0, .2, 1.5) },
            Glazing = new Glass[] { new(2.6, 3.8, .5) }, Roundel = new(3.1, 3.6, .55) },
        // after the Ki-45 (with something of the Pe-3): a slim two-seat twin, a long glasshouse over pilot and gunner, short radial nacelles, a straight tapered wing, one tall fin, and the barrel of a big cannon in the nose
        [Craft.I7] = new() { Name = "I-7", Span = 15, Len = 11, Accent = 0xB02A22,
            Fus = new[] { (0, .2), (.5, .45), (1.6, .62), (4, .66), (6.5, .52), (9, .3), (11, .12) },
            Wing = new(.8, 3.4, 5.9, 7.5, 4.3, 5.6), Tail = new(0, 9.6, 10.8, 2.6, 9.8, 10.6), Fins = new Fin[] { new(0, 9.1, 11, .28) },
            Engines = new Eng[] { new(-2.6, 2.6, 6.6, 1.35, 1.45, Nose: .7), new(2.6, 2.6, 6.6, 1.35, 1.45, Nose: .7), new(0, -1.1, .8, .2, Gun: true), new(-.3, .1, 1.3, .14, Gun: true), new(.3, .1, 1.3, .14, Gun: true) },
            Glazing = new Glass[] { new(2.9, 6.4, .62) }, Roundel = new(5.1, 4.9, .6) },
    };

    static readonly double[] Levels = { .12, .18, .27, .4, .6, .9, 1.35, 2 };
    static readonly Dictionary<(Craft, int), ImageTexture> Tex = new();

    public static void Build()
    {
        var jobs = Specs.Keys.SelectMany(k => Enumerable.Range(0, Levels.Length).Select(l => (k, l))).ToList();
        var done = new (Craft k, int l, byte[] p, int w, int h)[jobs.Count];
        Parallel.For(0, jobs.Count, i => { var (k, l) = jobs[i]; var p = Render(Specs[k], Levels[l], out int w, out int h); done[i] = (k, l, p, w, h); });
        foreach (var d in done) Tex[(d.k, d.l)] = ImageTexture.CreateFromImage(Image.CreateFromData(d.w, d.h, false, Image.Format.Rgba8, d.p));
    }

    /// <summary>Screen pixels per metre for an aircraft. Slightly oversize when far out, the same for every design so proportions hold; true to the map when close. A fighter never gets too small to read.
    /// Altitude: doubling it makes the sprite 40% larger (6,000 m is true size).</summary>
    public static double PxPerMetre(Craft k, double zoom, double z)
    {
        double scale = Math.Max(zoom * Math.Clamp(26 / (40 * zoom), 1, 3), (Specs[k].Span < 12 ? 17 : 14) / Specs[k].Span);
        return scale * AltScale(z);
    }
    public static double AltScale(double z) => Math.Max(.5, Math.Pow(Math.Max(z, 1) / 6000, Math.Log2(1.4)));

    public static void Draw(CanvasItem ci, Craft k, Vector2 at, double th, double pxm, Color mod)
    {
        int l = 0; while (l < Levels.Length - 1 && Levels[l] * pxm < 1) l++;
        var t = Tex[(k, l)]; float s = (float)(Levels[l] * pxm);
        ci.DrawSetTransform(at.Round(), (float)(Math.PI / 2 - th), new Vector2(s, s));
        ci.DrawTexture(t, -new Vector2(t.GetWidth() / 2, t.GetHeight() / 2), mod);
    }
    public static void Shadow(CanvasItem ci, Craft k, Vector2 at, double th, double pxm, double z)
    {
        float off = (float)(Specs[k].Span * pxm * .2 * Math.Clamp(z / 6000, 0, 1.5));
        Draw(ci, k, at + new Vector2(off, off * .7f), th, pxm, new Color(0, 0, 0, .3f));
    }
    public static void End(CanvasItem ci) => ci.DrawSetTransform(Vector2.Zero, 0, Vector2.One);
    /// <summary>A sprite nose to the right at exactly one art pixel per screen pixel, for panels.</summary>
    public static void Card(CanvasItem ci, Craft k, Vector2 at, double maxSpanPx, Color mod)
    {
        int l = 0; while (l < Levels.Length - 1 && Specs[k].Span / Levels[l] > maxSpanPx) l++;
        Draw(ci, k, at, 0, 1 / Levels[l], mod); End(ci);
    }

    // ---------------------------------------------------------------- the painter
    static double Fbm(double x, double y, int s) => (N.V(x, y, s) * 4 + N.V(x * 2, y * 2, s + 1) * 2 + N.V(x * 4, y * 4, s + 2)) / 7;
    static (double r, double g, double b) C(int h) => (h >> 16 & 255, h >> 8 & 255, h & 255);
    static (double r, double g, double b) Mul((double r, double g, double b) c, double k) => (c.r * k, c.g * k, c.b * k);
    static (double r, double g, double b) Mix((double r, double g, double b) a, (double r, double g, double b) b, double t) => (a.r + (b.r - a.r) * t, a.g + (b.g - a.g) * t, a.b + (b.b - a.b) * t);
    static double HalfWidth((double v, double h)[] f, double v)
    {
        if (v < f[0].v || v > f[^1].v) return -1;
        for (int i = 0; i < f.Length - 1; i++) if (v <= f[i + 1].v) return f[i].h + (f[i + 1].h - f[i].h) * (v - f[i].v) / (f[i + 1].v - f[i].v);
        return -1;
    }
    static (double le, double te)? Planform(Plan w, double au)
    {
        if (au > w.UTip) return null;
        double t = au <= w.URoot ? 0 : (au - w.URoot) / (w.UTip - w.URoot), le = w.LeR + (w.LeT - w.LeR) * t, te = w.TeR + (w.TeT - w.TeR) * t;
        const double Rs = .88;
        if (t > Rs) { double k = Math.Sqrt(Math.Max(0, 1 - Math.Pow((t - Rs) / (1 - Rs), 2))), m = (le + te) / 2; le = m - (m - le) * k; te = m + (te - m) * k; }
        return (le, te);
    }

    static byte[] Render(Spec S, double mpp, out int W, out int H)
    {
        int pad = (int)Math.Ceiling(1.2 / mpp) + 2, seed = S.Name.Length * 31; W = (int)Math.Ceiling(S.Span / mpp) + 2 * pad; H = (int)Math.Ceiling(S.Len / mpp) + 2 * pad;
        int w = W, h = H; var d = new byte[w * h * 4];
        bool fine = mpp < .5; double fat = mpp >= .6 ? mpp * .3 : 0;   // at coarse sizes thin parts are fattened a little, or they would break up
        var acc = C(S.Accent);
        var props = S.Engines.Where(e => e.Pr > 0).Select(e => new Ball(e.U, e.V0, e.Pr)).Concat(S.Props).ToList();
        (double r, double g, double b) Skin(double u, double v)
        {
            if (S.Metal)
            {
                var c = (170.0, 176.0, 182.0);
                double pu = Math.Abs(((u + 100) % 2.2) - 1.1), pv = Math.Abs(((v + 100) % 2.7) - 1.35);
                return fine && (pu > 1.06 || pv > 1.3) ? Mul(c, .86) : c;
            }
            double n = Fbm(u * .26 + 3, v * .26 + 9, 61);
            return n > .58 ? (60, 68, 41) : n < .38 ? (108, 92, 60) : (90, 98, 61);
        }
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                double u = (x + .5 - w / 2.0) * mpp, v = (y + .5 - h / 2.0) * mpp + S.Len / 2, au = Math.Abs(u), hs = N.Hash(x, y, 5 + seed);
                (double r, double g, double b)? col = null; double al = 255;
                double hw = HalfWidth(S.Fus, v); if (hw >= 0) hw += fat;
                bool inF = hw > 0 && au <= hw;
                var wy = Planform(S.Wing, au); var ty = Planform(S.Tail, au);
                bool inW = wy != null && v >= wy.Value.le - fat && v <= wy.Value.te + fat, inT = ty != null && v >= ty.Value.le - fat && v <= ty.Value.te + fat;
                Eng? eg = null; double er = 0;
                foreach (var e in S.Engines)
                {
                    double ue = (u - e.U) / (e.W / 2 + fat);
                    if (Math.Abs(ue) > 1 || v < e.V0 || v > e.V1) continue;
                    double nose = e.V0 + e.Nose, tl = e.V1 - 1.6, f = 1;
                    if (v < nose) f = Math.Sqrt(Math.Max(0, 1 - Math.Pow((nose - v) / e.Nose, 2))); else if (v > tl) f = 1 - .7 * (v - tl) / 1.6;
                    if (Math.Abs(ue) <= f) { eg = e; er = ue / f; break; }
                }
                bool inFin = false; double finU = 0; foreach (var f in S.Fins) if (Math.Abs(u - f.U) <= f.W / 2 + fat && v >= f.V0 && v <= f.V1) { inFin = true; finU = f.U; }
                Ball? tur = null; double tdd = 0; foreach (var t in S.Turrets) { double dd = Math.Sqrt((u - t.U) * (u - t.U) + (v - t.V) * (v - t.V)); if (dd <= t.R) { tur = t; tdd = dd; } }
                Glass? gl = null; if (inF) foreach (var q in S.Glazing) if (v >= q.V0 && v <= q.V1 && Math.Abs(u - q.U0) <= hw * q.F) { gl = q; break; }

                if (tur != null)
                {
                    var t = tur.Value; double k = tdd / t.R;
                    col = k > .8 ? (30, 34, 38) : Mix((84, 112, 124), (176, 206, 216), Math.Max(0, 1 - Math.Sqrt(Math.Pow(u - t.U + t.R * .4, 2) + Math.Pow(v - t.V + t.R * .4, 2)) / (t.R * .75)));
                }
                else if (gl != null)
                {
                    var g = gl.Value; col = (72, 98, 108);
                    if (fine && (((v - g.V0) % .85 < mpp * .95) || Math.Abs(u - g.U0) > hw * g.F - mpp * .9 || v - g.V0 < mpp)) col = (26, 32, 36);
                    else if (u < 0 && (v - g.V0) < (g.V1 - g.V0) * .45) col = (120, 150, 160);
                }
                else if (eg != null && eg.Value.Gun) col = Mul((46, 50, 54), 1.12 - .4 * er * er);
                else if (eg != null && eg.Value.Boom) col = Mul(Skin(u, v), (1.12 - .4 * er * er - .08 * er) * .95);
                else if (eg != null)
                {
                    var e = eg.Value; double k = 1.12 - .4 * er * er - .08 * er;
                    if (v < e.V0 + e.Nose) col = Mul(v < e.V0 + .28 ? acc : (58, 62, 66), k);
                    else if (fine && v > e.V0 + 1.4 + e.Nose - .9 && v < e.V0 + 2.7 + e.Nose - .9 && Math.Abs(er) > .74) col = (38, 34, 32);
                    else col = Mul(Skin(u, v), k * (S.Metal ? .95 : .92));
                }
                else if (inF)
                {
                    double t = au / hw, k = 1.12 - .38 * t * t - .08 * (u / hw); var b = Skin(u, v);
                    if (S.BandV1 > 0 && v >= S.BandV0 && v <= S.BandV1) b = acc;
                    if (S.NoseDark > 0 && v < S.NoseDark) b = Mix(b, (52, 60, 66), .75);
                    if (fine && Math.Abs(v % 2.4 - 1.2) > 1.12) b = Mul(b, .9);
                    col = Mul(b, k);
                }
                else if (inFin) col = Mul(Skin(u, v), u > finU ? .82 : 1.1);
                else if (inW)
                {
                    double le = wy!.Value.le, te = wy.Value.te, fr = (v - le) / (te - le), k = 1, tip = S.Wing.UTip; var b = Skin(u, v);
                    if (S.Roundel != null)
                    {
                        var rd = S.Roundel.Value; double q = Math.Sqrt((au - rd.U) * (au - rd.U) + (v - rd.V) * (v - rd.V));
                        if (q <= rd.R) { double f = q / rd.R; b = S.Wolf ? C(f < .34 ? 0x2B4A82 : f < .7 ? 0xDBE3EA : 0x2B4A82) : C(f < .78 ? 0xA82F27 : 0xD2CAB0); }
                    }
                    if (fr < .1) k = 1.16; else if (fr > .88) k = .86;
                    if (fine)
                    {
                        double ch = le + .72 * (te - le);
                        if (au > .62 * tip && au < .97 * tip && Math.Abs(v - ch) < mpp * .55) k = .68;
                        if (au > S.Wing.URoot + 1.4 && au <= .62 * tip && Math.Abs(v - ch) < mpp * .55) k = .72;
                        if (Math.Abs(au - .62 * tip) < mpp * .5 && fr > .72) k = .7;
                    }
                    foreach (var e in S.Engines) if (e.Pr > 0 && Math.Abs(u - e.U) < e.W * .55 && v > e.V1 - 3 && v < e.V1 + 2.8) k *= 1 - .38 * (1 - Math.Min(1, (v - (e.V1 - 3)) / 5.8));   // exhaust stain
                    col = Mul(b, k);
                }
                else if (inT)
                {
                    double fr = (v - ty!.Value.le) / (ty.Value.te - ty.Value.le), k = fr < .1 ? 1.12 : fr > .9 ? .88 : 1;
                    if (fine && Math.Abs(fr - .58) < mpp * .5 / (ty.Value.te - ty.Value.le)) k = .72;
                    if (au < .12) k = .8;
                    col = Mul(Skin(u, v), k);
                }
                if (col != null) { col = Mul(col.Value, (1 - .17 * Fbm(u * .5 + 7, v * .5 + 3, 77)) * (1 + (hs - .5) * .07)); if (fine && hs < .02) col = Mul(col.Value, .72); }
                if (mpp < 1.3)
                    foreach (var p in props)
                    {
                        double q = Math.Sqrt((u - p.U) * (u - p.U) + (v - p.V) * (v - p.V)), rim = Math.Max(.22, mpp);
                        if (q > p.R) continue;
                        if (col != null) { col = Mix(col.Value, (205, 210, 214), q > p.R - rim ? .1 : .3); if (q > p.R - rim) col = Mix(col.Value, (80, 82, 86), .35); }
                        else { col = q > p.R - rim ? (110, 114, 120) : (205, 210, 214); al = q > p.R - rim ? 130 : 70; }
                    }
                if (col == null) continue;
                int o = (y * w + x) * 4; var c = col.Value;
                d[o] = (byte)Math.Clamp(c.r, 0, 255); d[o + 1] = (byte)Math.Clamp(c.g, 0, 255); d[o + 2] = (byte)Math.Clamp(c.b, 0, 255); d[o + 3] = (byte)al;
            }
        // a one-pixel dark outline, so a sprite holds up on any ground
        var ink = new List<int>();
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                if (d[(y * w + x) * 4 + 3] >= 200) continue;
                foreach (var (ox, oy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (-1, -1) })
                {
                    int nx = x + ox, ny = y + oy;
                    if (nx >= 0 && ny >= 0 && nx < w && ny < h && d[(ny * w + nx) * 4 + 3] >= 200) { ink.Add((y * w + x) * 4); break; }
                }
            }
        foreach (int i in ink) { d[i] = 14; d[i + 1] = 18; d[i + 2] = 26; d[i + 3] = 255; }
        return d;
    }
}

/// <summary>Press H: every design on one page at true relative size, then at three altitudes, damaged and picked. For looking at the art.</summary>
public partial class Hangar : Node2D
{
    public override void _Ready() { TextureFilter = TextureFilterEnum.Nearest; Visible = false; }
    public override void _Draw()
    {
        var size = GetViewportRect().Size; var font = ThemeDB.FallbackFont;
        DrawRect(new Rect2(Vector2.Zero, size), new Color(.3f, .35f, .22f));
        void Text(float x, float y, string t, int fs, Color c) { DrawStringOutline(font, new Vector2(x, y), t, HorizontalAlignment.Left, -1, fs, 3, new Color(0, 0, 0, .7f)); DrawString(font, new Vector2(x, y), t, HorizontalAlignment.Left, -1, fs, c); }
        Text(24, 34, "HANGAR   all aircraft at the same scale   H to close", 18, ConsoleUi.Amber);
        const double Pxm = 8; float x = 24;
        foreach (var k in new[] { Craft.V16, Craft.TB6, Craft.SB3, Craft.U4, Craft.I7, Craft.I8, Craft.F5 })
        {
            var q = Sprites.Specs[k]; float w = (float)(q.Span * Pxm) + 50; var at = new Vector2(x + w / 2 - 10, 195);
            Sprites.Shadow(this, k, at, Math.PI / 2, Pxm, 6000); Sprites.Draw(this, k, at, Math.PI / 2, Pxm, Colors.White); Sprites.End(this);
            Text(x + 4, 352, q.Name, 14, Colors.White); Text(x + 4, 369, $"{q.Span:0.#} x {q.Len:0.#} m", 11, new Color(.85f, .9f, .75f));
            x += w;
        }
        Text(30, 405, "Altitude: 3,000 m, 6,000 m, 12,000 m (each doubling is 40% larger)", 14, ConsoleUi.Amber);
        x = 40;
        foreach (var k in new[] { Craft.V16, Craft.SB3, Craft.F5 })
            foreach (double z in new[] { 3000.0, 6000, 12000 })
            {
                double pxm = 2.2 * Sprites.AltScale(z); var at = new Vector2(x + (float)(Sprites.Specs[k].Span * pxm / 2), 500);
                Sprites.Shadow(this, k, at, Math.PI / 2 - .35, pxm, z); Sprites.Draw(this, k, at, Math.PI / 2 - .35, pxm, Colors.White); Sprites.End(this);
                x += (float)(Sprites.Specs[k].Span * pxm) + 30;
            }
        Text(1010, 405, "Damage tint", 14, ConsoleUi.Amber);
        x = 1000;
        foreach (float d in new[] { 0f, .35f, .7f, 1f })
        {
            var at = new Vector2(x + 50, 500);
            Sprites.Shadow(this, Craft.V16, at, Math.PI / 2, 2.2, 6000); Sprites.Draw(this, Craft.V16, at, Math.PI / 2, 2.2, DamageTint(d)); Sprites.End(this);
            x += 130;
        }
        Text(30, 625, "Zooming out, the same aircraft: redrawn at each size, not shrunk", 14, ConsoleUi.Amber);
        x = 40;
        foreach (double pxm in new[] { 3.0, 2, 1.4, 1, .75, .55 })
            foreach (var k in new[] { Craft.V16, Craft.SB3, Craft.F5 })
            {
                var at = new Vector2(x + (float)(Sprites.Specs[k].Span * pxm / 2), 730); double h = Math.PI / 2 + .6;
                Sprites.Shadow(this, k, at, h, pxm, 6000); Sprites.Draw(this, k, at, h, pxm, Colors.White); Sprites.End(this);
                x += (float)(Sprites.Specs[k].Span * pxm) + 18;
            }
    }
    public static Color DamageTint(float d) => Colors.White.Lerp(new Color(.82f, .4f, .3f), Math.Min(1, d * 1.1f)).Darkened(d * .25f);
}
