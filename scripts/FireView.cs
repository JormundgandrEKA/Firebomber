using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Firebomber;

/// <summary>Everything bombs and fire leave on the map, in the same pixels as the terrain: burnt and blasted buildings and craters on a 4 m grid, pixel flames, smoke drifting downwind, explosions.</summary>
public partial class FireView : Node2D
{
    public Main M = null!;
    const int ChunkPx = 256, Mpp = 4, Frames = 4, FrameSize = 16;
    sealed class Chunk { public byte[] Px = new byte[ChunkPx * ChunkPx * 4]; public ImageTexture? Tex; public bool Dirty; }
    readonly Queue<Chunk> dirty = new();
    readonly Dictionary<long, Chunk> chunks = new();
    struct Boom { public double X, Y; public float Age, Size; public bool He, Water; }
    struct Puff { public double X, Y, Born, Life, Grow, Base; public float Tone; public int Kind; }
    readonly List<Boom> booms = new(); readonly List<Puff> smoke = new();
    public int BoomCount => booms.Count;
    /// <summary>At night what burns is what you see: flames and flashes, drawn again above the darkness, with a glow on the ground.</summary>
    public void GlowTo(CanvasItem ci, float dark)
    {
        var size = GetViewportRect().Size; var view = new Rect2(-80, -80, size.X + 160, size.Y + 160); double z = M.Zoom; int frame = (int)(anim * 9); var mod = new Color(1, 1, 1, dark);
        foreach (var c in M.Fire.Cities)
        {
            foreach (var (x, y, v) in c.Cells)
            {
                var p = M.W2S(x, y); if (!view.HasPoint(p)) continue;
                ci.DrawCircle(p, (float)Math.Max(6, 240 * z), new Color(1f, .42f, .1f, dark * Math.Min(.16f, v / 200f)));
                if (z < .08) { float s = (float)Math.Max(3, (30 + Math.Min(v, 60) * 3.2) * z); ci.DrawTextureRectRegion(Flames, new Rect2((p - new Vector2(s / 2, s / 2)).Round(), new Vector2(s, s)), new Rect2((frame + (int)x) % Frames * FrameSize, 0, FrameSize, FrameSize), mod); }
            }
            if (z >= .08)
                foreach (int i in c.Burning)
                {
                    if (i % 3 == 0) continue;
                    ref var b = ref c.B[i]; var p = M.W2S(c.P.X + b.X, c.P.Y + b.Y); if (!view.HasPoint(p)) continue;
                    float s = (float)Math.Max(5, Math.Min(26, Math.Max(b.W, b.D)) * 1.2 * z);
                    ci.DrawTextureRectRegion(Flames, new Rect2((p - new Vector2(s / 2, s * .7f)).Round(), new Vector2(s, s)), new Rect2((frame + i) % Frames * FrameSize, 0, FrameSize, FrameSize), mod);
                }
        }
        foreach (var b in booms)
        {
            var p = M.W2S(b.X, b.Y); if (!view.HasPoint(p)) continue;
            int fr = Math.Min(5, (int)(b.Age / .15f)); float s = (float)Math.Max(b.He ? 5 : 3, (b.Water ? 40 : b.He ? 60 : 16) * b.Size * z);
            ci.DrawTextureRectRegion(blast, new Rect2((p - new Vector2(s / 2, s / 2)).Round(), new Vector2(s, s)), new Rect2(fr * FrameSize, 0, FrameSize, FrameSize), mod);
        }
    }
    public static ImageTexture Flames = null!; ImageTexture blast = null!, puffs = null!;
    readonly Random rng = new(5); double lastT; float anim;

    static ImageTexture Atlas(int frames, Func<int, double, double, int, (int r, int g, int b, int a)> f)
    {
        var d = new byte[frames * FrameSize * FrameSize * 4];
        for (int fr = 0; fr < frames; fr++)
            for (int y = 0; y < FrameSize; y++) for (int x = 0; x < FrameSize; x++)
                {
                    double u = (x + .5 - 8) / 8, v = (y + .5 - 8) / 8; var c = f(fr, u, v, x * 37 + y * 101 + fr * 7);
                    int o = (y * frames * FrameSize + x + fr * FrameSize) * 4; d[o] = (byte)c.r; d[o + 1] = (byte)c.g; d[o + 2] = (byte)c.b; d[o + 3] = (byte)c.a;
                }
        return ImageTexture.CreateFromImage(Image.CreateFromData(frames * FrameSize, FrameSize, false, Image.Format.Rgba8, d));
    }

    public override void _Ready()
    {
        TextureFilter = TextureFilterEnum.NearestWithMipmaps;
        Flames = Atlas(Frames, (fr, u, v, s) =>
        {
            // a licking flame: wide at the foot, wobbling at the tip, hot in the middle
            double wob = .22 * Math.Sin(v * 4 + fr * 1.6), w = (1 - (v + 1) / 2) * 0 + .35 + .5 * (v + 1) / 2, r = Math.Abs(u + wob * (1 - v) * .5) / w + N.Hash(s, fr, 3) * .3;
            if (r > 1 || v < -.95 + .2 * N.Hash(s % 7, fr, 4)) return (0, 0, 0, 0);
            return r < .35 && v > -.1 ? (255, 238, 170, 255) : r < .65 ? (255, 176, 52, 255) : (216, 74, 24, 235);
        });
        blast = Atlas(6, (fr, u, v, s) =>
        {
            double t = fr / 5.0, q = Math.Sqrt(u * u + v * v) + N.Hash(s, fr, 5) * .22, R = .35 + .65 * t;
            if (q > R) return (0, 0, 0, 0);
            if (fr <= 1) return q < R * .6 ? (255, 250, 220, 255) : (255, 190, 80, 255);
            if (fr <= 3) return q < R * .4 ? (255, 150, 40, 255) : q < R * .75 ? (120, 72, 40, 235) : (58, 50, 44, 210);
            return N.Hash(s, fr, 6) < 1.1 - t ? (64, 58, 52, 170) : (0, 0, 0, 0);
        });
        puffs = Atlas(3, (fr, u, v, s) =>
        {
            double q = Math.Sqrt(u * u + v * v), lump = N.V(u * 2.3 + fr * 3, v * 2.3 + 9, 41) * .55;
            if (q + lump > 1.1) return (0, 0, 0, 0);
            int b = N.Hash(s, fr, 7) < .28 ? 118 : 92;      // a little grain, so smoke stays pixels
            return (b, b - 2, b - 4, q + lump > .85 ? 140 : 255);
        });
    }

    // ---------------------------------------------------------------- the burnt ground
    Chunk ChunkAt(int i, int j) { long k = ((long)i << 32) ^ (uint)j; if (!chunks.TryGetValue(k, out var c)) chunks[k] = c = new Chunk(); return c; }
    void Pixel(double wx, double wy, (int r, int g, int b) col, int a = 255)
    {
        int gx = (int)Math.Floor(wx / Mpp), gy = (int)Math.Floor(wy / Mpp), i = (int)Math.Floor((double)gx / ChunkPx), j = (int)Math.Floor((double)gy / ChunkPx);
        var c = ChunkAt(i, j); int px = gx - i * ChunkPx, py = ChunkPx - 1 - (gy - j * ChunkPx), o = (py * ChunkPx + px) * 4;
        c.Px[o] = (byte)col.r; c.Px[o + 1] = (byte)col.g; c.Px[o + 2] = (byte)col.b; c.Px[o + 3] = (byte)a; if (!c.Dirty) { c.Dirty = true; dirty.Enqueue(c); }
    }
    void PaintBuilding(CityFire f, int i)
    {
        ref var b = ref f.B[i]; double ca = Math.Cos(b.Ang), sa = Math.Sin(b.Ang), r = Math.Sqrt(b.W * b.W + b.D * b.D) / 2 + Mpp, cx = f.P.X + b.X, cy = f.P.Y + b.Y;
        byte state = b.State, mat = b.Mat; int n = 0;
        for (double y = Math.Floor((cy - r) / Mpp) * Mpp + Mpp / 2.0; y < cy + r; y += Mpp)
            for (double x = Math.Floor((cx - r) / Mpp) * Mpp + Mpp / 2.0; x < cx + r; x += Mpp)
            {
                double ox = x - cx, oy = y - cy, lx = ox * ca + oy * sa, ly = -ox * sa + oy * ca;
                if (Math.Abs(lx) > b.W / 2 + 1 || Math.Abs(ly) > b.D / 2 + 1) continue;
                double h = N.Hash((int)(x / Mpp), (int)(y / Mpp), 44); n++;
                bool rim = Math.Abs(lx) > b.W / 2 - 3 || Math.Abs(ly) > b.D / 2 - 3;
                if (state == FireSim.Intact) { Pixel(x, y, (0, 0, 0), h < .1 ? 150 : 0); continue; }   // saved by the crews: a few scorch marks
                (int r, int g, int b) c = state switch
                {
                    FireSim.Burning => h < .45 ? (150, 50, 20) : h < .8 ? (70, 34, 22) : (30, 24, 20),
                    // gutted: wood leaves ash and charcoal, brick leaves roofless walls standing round a black floor
                    FireSim.Gutted => mat == CityPlan.Wood ? (h < .3 ? (58, 54, 48) : (26, 23, 21)) : rim ? (h < .5 ? (92, 64, 54) : (70, 52, 46)) : (h < .25 ? (46, 40, 36) : (22, 20, 19)),
                    _ => h < .4 ? (88, 80, 68) : h < .75 ? (58, 52, 46) : (34, 31, 28),   // blasted: rubble
                };
                Pixel(x, y, c);
            }
        if (n == 0) Pixel(cx, cy, state == FireSim.Burning ? (150, 50, 20) : (30, 26, 24));
    }
    void PaintCrater(double x, double y, bool he)
    {
        if (!he) { if (rng.NextDouble() < .35) Pixel(x, y, (40, 30, 24), 200); return; }
        foreach (var (a, b, c) in new[] { (0, 0, (14, 12, 11)), (Mpp, 0, (40, 36, 32)), (-Mpp, 0, (40, 36, 32)), (0, Mpp, (40, 36, 32)), (0, -Mpp, (40, 36, 32)) }) Pixel(x + a, y + b, c);
    }

    // ---------------------------------------------------------------- keeping up with the simulation
    public override void _Process(double delta)
    {
        var pf = System.Diagnostics.Stopwatch.StartNew();
        var fire = M.Fire;
        foreach (var (c, i) in fire.Changed) PaintBuilding(c, i); fire.Changed.Clear();
        foreach (var (x, y, he) in fire.Craters) PaintCrater(x, y, he); fire.Craters.Clear();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (dirty.Count > 0 && sw.Elapsed.TotalMilliseconds < 4)
        {
            var c = dirty.Dequeue();
            var im = Image.CreateFromData(ChunkPx, ChunkPx, false, Image.Format.Rgba8, c.Px); im.GenerateMipmaps();
            if (c.Tex == null) c.Tex = ImageTexture.CreateFromImage(im); else c.Tex.Update(im);
            c.Dirty = false;
        }
        foreach (var i in M.Air.Recent)
        {
            if (booms.Count > 900 || (i.Incendiary && rng.NextDouble() < .5)) continue;
            booms.Add(new Boom { X = i.X, Y = i.Y, He = !i.Incendiary, Water = i.Water, Size = (float)Math.Cbrt(Math.Max(i.Kg, 50) / 250.0) });
        }
        M.Air.Recent.Clear();
        for (int k = booms.Count - 1; k >= 0; k--) { var b = booms[k]; b.Age += (float)delta; if (b.Age > .9f) booms.RemoveAt(k); else booms[k] = b; }

        double T = M.Air.T, dt = Math.Max(0, T - lastT); lastT = T;
        if (!M.Clock.Paused) anim += (float)delta;
        Emit(Math.Min(dt, 60));
        foreach (var f in M.Air.Formations)
            if (f.Phase != Phase.Waiting && f.Phase != Phase.Landed)
                foreach (var a in f.Ac) for (double n = a.H.Burning ? dt * 3 : a.H.Working < a.H.D.Engines ? dt : 0; n > 0; n -= 1) if (rng.NextDouble() < n) smoke.Add(new Puff { X = a.X, Y = a.Y, Born = T, Life = 35, Grow = 60, Base = 14, Tone = .2f, Kind = rng.Next(3) });
        foreach (var d in M.Sim.Wrecks) for (double n = dt * (d.Big ? 4 : 2); n > 0; n -= 1) if (rng.NextDouble() < n) smoke.Add(new Puff { X = d.X, Y = d.Y, Born = T, Life = 45, Grow = d.Big ? 120 : 50, Base = d.Big ? 20 : 8, Tone = .18f, Kind = rng.Next(3) });
        foreach (var f in M.Sim.All) if (f.Airborne && (f.H.Burning || f.Damage > .4)) for (double n = dt * 1.5; n > 0; n -= 1) if (rng.NextDouble() < n) smoke.Add(new Puff { X = f.X, Y = f.Y, Born = T, Life = 25, Grow = 30, Base = 6, Tone = .3f, Kind = rng.Next(3) });
        smoke.RemoveAll(p => T - p.Born > p.Life);
        if (smoke.Count > 1800) smoke.RemoveRange(0, smoke.Count - 1800);
        QueueRedraw(); Prof.Add("paint", pf.Elapsed.TotalMilliseconds);
    }
    void Emit(double dt)
    {
        foreach (var c in M.Fire.Cities)
            foreach (var (x, y, v) in c.Cells)
            {
                double n = Math.Min(v, 30) * .012 * dt;
                while (n > 0) { if (rng.NextDouble() < n) smoke.Add(NewPuff(x, y, M.Air.T)); n -= 1; }
            }
    }
    Puff NewPuff(double x, double y, double born) => new() { X = x + (rng.NextDouble() - .5) * 150, Y = y + (rng.NextDouble() - .5) * 150, Born = born, Life = 700 + rng.NextDouble() * 500, Base = 70, Grow = 500 + rng.NextDouble() * 300, Tone = .32f + (float)rng.NextDouble() * .2f, Kind = rng.Next(3) };
    /// <summary>For views jumped to by fast-forward: fill in the plume that would have built up by now.</summary>
    public void Prewarm()
    {
        foreach (var c in M.Fire.Cities) foreach (var (x, y, v) in c.Cells)
                for (int k = 0; k < (int)(Math.Min(v, 30) * .012 * 900); k++) smoke.Add(NewPuff(x, y, M.Air.T - rng.NextDouble() * 900));
        lastT = M.Air.T;
    }

    public override void _Draw()
    {
        var pf = System.Diagnostics.Stopwatch.StartNew(); DrawAll(); Prof.Add("flames", pf.Elapsed.TotalMilliseconds);
    }
    void DrawAll()
    {
        var size = GetViewportRect().Size; var view = new Rect2(-80, -80, size.X + 160, size.Y + 160); double z = M.Zoom, T = M.Air.T;
        double cs = ChunkPx * (double)Mpp;
        foreach (var (k, c) in chunks)
        {
            if (c.Tex == null) continue;
            int i = (int)(k >> 32), j = (int)(uint)k;
            var a = M.W2S(i * cs, (j + 1) * cs).Round(); var b = M.W2S((i + 1) * cs, j * cs).Round(); var r = new Rect2(a, b - a);
            if (r.Intersects(view)) DrawTextureRect(c.Tex, r, false);
        }
        int frame = (int)(anim * 9);
        foreach (var c in M.Fire.Cities)
        {
            if (z >= .08)
            {
                int stride = 1 + c.Burning.Count / 9000, n = 0;
                foreach (int i in c.Burning)
                {
                    if (++n % stride != 0 || i % 3 == 0) continue;     // not every burning building shows a flame, so the glowing roofs and gaps read
                    ref var b = ref c.B[i]; var p = M.W2S(c.P.X + b.X, c.P.Y + b.Y); if (!view.HasPoint(p)) continue;
                    float s = (float)Math.Max(5, Math.Min(26, Math.Max(b.W, b.D)) * (b.Mat == CityPlan.Factory ? 1.1 : 1.25) * z);
                    DrawTextureRectRegion(Flames, new Rect2((p - new Vector2(s / 2, s * .7f)).Round(), new Vector2(s, s)), new Rect2((frame + i) % Frames * FrameSize, 0, FrameSize, FrameSize));
                }
            }
            else
                foreach (var (x, y, v) in c.Cells)
                {
                    var p = M.W2S(x, y); if (!view.HasPoint(p)) continue;
                    float s = (float)Math.Max(3, (30 + Math.Min(v, 60) * 3.2) * z);
                    DrawTextureRectRegion(Flames, new Rect2((p - new Vector2(s / 2, s / 2)).Round(), new Vector2(s, s)), new Rect2((frame + (int)x) % Frames * FrameSize, 0, FrameSize, FrameSize));
                }
        }
        foreach (var b in booms)
        {
            var p = M.W2S(b.X, b.Y); if (!view.HasPoint(p)) continue;
            int fr = Math.Min(5, (int)(b.Age / .15f));
            float s = (float)Math.Max(b.He ? 5 : 3, (b.Water ? 40 : b.He ? 60 : 16) * b.Size * z);
            var mod = b.Water ? new Color(.8f, .9f, 1f, .8f) : Colors.White;
            DrawTextureRectRegion(blast, new Rect2((p - new Vector2(s / 2, s / 2)).Round(), new Vector2(s, s)), new Rect2(fr * FrameSize, 0, FrameSize, FrameSize), mod);
        }
        // on the big map a fire is far too small at true size: show it as a mark, growing with the fire, with smoke trailing downwind
        if (z < .012)
            foreach (var c in M.Fire.Cities)
            {
                if (c.Burning.Count == 0) continue;
                var p = M.W2S(c.P.X, c.P.Y); float s = Math.Clamp(5 + MathF.Sqrt(c.Burning.Count) * .22f, 6, 24), ws = (float)Math.Sqrt(M.Fire.WindX * M.Fire.WindX + M.Fire.WindY * M.Fire.WindY);
                var dir = new Vector2((float)M.Fire.WindX / ws, -(float)M.Fire.WindY / ws);
                for (int k = 5; k >= 1; k--) { float ps = s * (.7f + k * .28f); DrawTextureRectRegion(puffs, new Rect2((p + dir * k * s * .75f - new Vector2(ps, ps) / 2).Round(), new Vector2(ps, ps)), new Rect2(k % 3 * FrameSize, 0, FrameSize, FrameSize), new Color(.3f, .29f, .28f, .75f - k * .1f)); }
                DrawTextureRectRegion(Flames, new Rect2((p - new Vector2(s, s) / 2).Round(), new Vector2(s, s)), new Rect2(frame % Frames * FrameSize, 0, FrameSize, FrameSize));
            }
        foreach (var q in smoke)
        {
            double age = T - q.Born, f = age / q.Life, x = q.X + M.Fire.WindX * age, y = q.Y + M.Fire.WindY * age;
            var p = M.W2S(x, y); if (!view.HasPoint(p)) continue;
            float s = (float)Math.Max(3, (q.Base + q.Grow * Math.Sqrt(f)) * z);
            var col = new Color(q.Tone, q.Tone * .97f, q.Tone * .94f, (float)(Math.Min(1, f * 8) * (1 - f) * .46));
            DrawTextureRectRegion(puffs, new Rect2((p - new Vector2(s / 2, s / 2)).Round(), new Vector2(s, s)), new Rect2(q.Kind * FrameSize, 0, FrameSize, FrameSize), col);
        }
    }
}
