using Godot;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace Firebomber;

/// <summary>Terrain tiles at five levels, made on demand on worker threads. The coarsest level covers the whole theatre and is ready before the first frame,
/// so whatever else is still being made, a coarser tile shows through and there is never a void.</summary>
public sealed class TerrainLayer
{
    public static readonly int[] Mpp = { 4, 16, 64, 256, 1024 };
    const int Top = 4, InkMin = 3, MaxTiles = 900, MaxInFlight = 20;
    readonly World w;
    sealed class Tile { public ImageTexture Tex = null!; public int Used; }
    readonly Dictionary<long, Tile> tiles = new();
    readonly ConcurrentQueue<(long key, byte[] pix)> done = new();
    readonly HashSet<long> inFlight = new();
    readonly List<(long key, int lvl, int ix, int iy, bool ink, double d)> wanted = new();
    int frame;

    public bool Idle => wanted.Count == 0 && inFlight.Count == 0 && done.IsEmpty;
    public int Count => tiles.Count;

    static long Key(bool ink, int lvl, int ix, int iy) => ((long)(ink ? 1 : 0) << 62) | ((long)lvl << 56) | ((long)(uint)ix << 28) | (uint)iy;
    static int Cols(int lvl) => (int)Math.Ceiling(World.WidthM / (TerrainPaint.T * (double)Mpp[lvl]));
    static int Rows(int lvl) => (int)Math.Ceiling(World.HeightM / (TerrainPaint.T * (double)Mpp[lvl]));

    public TerrainLayer(World world)
    {
        w = world;
        var jobs = new List<(bool ink, int x, int y)>();
        for (int x = 0; x < Cols(Top); x++) for (int y = 0; y < Rows(Top); y++) { jobs.Add((false, x, y)); jobs.Add((true, x, y)); }
        var made = new ConcurrentBag<(long, byte[])>();
        Parallel.ForEach(jobs, j => made.Add((Key(j.ink, Top, j.x, j.y), TerrainPaint.Render(w, Mpp[Top], j.x, j.y, j.ink))));
        foreach (var (k, p) in made) Upload(k, p);
    }

    void Upload(long key, byte[] pix)
    {
        var im = Image.CreateFromData(TerrainPaint.T, TerrainPaint.T, false, Image.Format.Rgba8, pix);
        im.GenerateMipmaps();
        tiles[key] = new Tile { Tex = ImageTexture.CreateFromImage(im), Used = frame };
    }

    /// <summary>The finest level whose pixels are no more than 2 screen pixels at this zoom.</summary>
    public static int LevelFor(double zoom)
    {
        for (int i = Top; i >= 0; i--) if (Mpp[i] * zoom <= 2) return i;
        return 0;
    }

    public void Draw(CanvasItem ci, Func<double, double, Vector2> w2s, double zoom, double camX, double camY, Vector2 size)
    {
        frame++; wanted.Clear();
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed.TotalMilliseconds < 5 && done.TryDequeue(out var r)) { inFlight.Remove(r.key); Upload(r.key, r.pix); }
        double vx0 = camX - size.X / 2 / zoom, vx1 = camX + size.X / 2 / zoom, vy0 = camY - size.Y / 2 / zoom, vy1 = camY + size.Y / 2 / zoom;
        int t = LevelFor(zoom);
        double inkA = Math.Clamp((.012 - zoom) / .006, 0, 1);
        if (inkA < 1)
            for (int l = Top; l >= t; l--) Level(ci, w2s, false, l, l <= t + 1, vx0, vy0, vx1, vy1, camX, camY, 1f);
        if (inkA > 0)
        {
            int il = Math.Max(t, InkMin);
            if (il != Top) Level(ci, w2s, true, Top, false, vx0, vy0, vx1, vy1, camX, camY, (float)inkA);
            Level(ci, w2s, true, il, true, vx0, vy0, vx1, vy1, camX, camY, (float)inkA);
        }
        foreach (var q in wanted.OrderBy(q => q.lvl == t ? 1 : 0).ThenBy(q => q.d))
        {
            if (inFlight.Count >= MaxInFlight) break;
            if (!inFlight.Add(q.key)) continue;
            var job = q; Task.Run(() => done.Enqueue((job.key, TerrainPaint.Render(w, Mpp[job.lvl], job.ix, job.iy, job.ink))));
        }
        if (tiles.Count > MaxTiles && frame % 30 == 0)
            foreach (var kv in tiles.Where(kv => kv.Value.Used < frame - 2 && (int)((kv.Key >> 56) & 63) != Top).OrderBy(kv => kv.Value.Used).Take(tiles.Count - MaxTiles + 50).ToList())
            { kv.Value.Tex.Dispose(); tiles.Remove(kv.Key); }
    }

    void Level(CanvasItem ci, Func<double, double, Vector2> w2s, bool ink, int l, bool request, double vx0, double vy0, double vx1, double vy1, double camX, double camY, float alpha)
    {
        double ts = TerrainPaint.T * (double)Mpp[l];
        int ix0 = Math.Max(0, (int)Math.Floor(vx0 / ts)), ix1 = Math.Min(Cols(l) - 1, (int)Math.Floor(vx1 / ts));
        int iy0 = Math.Max(0, (int)Math.Floor(vy0 / ts)), iy1 = Math.Min(Rows(l) - 1, (int)Math.Floor(vy1 / ts));
        var mod = new Color(1, 1, 1, alpha);
        for (int ix = ix0; ix <= ix1; ix++)
            for (int iy = iy0; iy <= iy1; iy++)
            {
                long k = Key(ink, l, ix, iy);
                if (tiles.TryGetValue(k, out var tile))
                {
                    tile.Used = frame;
                    var a = w2s(ix * ts, (iy + 1) * ts).Round(); var b = w2s((ix + 1) * ts, iy * ts).Round();   // whole screen pixels, so no gaps open between tiles
                    ci.DrawTextureRect(tile.Tex, new Rect2(a, b - a), false, mod);
                }
                else if (request)
                {
                    double dx = (ix + .5) * ts - camX, dy = (iy + .5) * ts - camY;
                    wanted.Add((k, l, ix, iy, ink, dx * dx + dy * dy));
                }
            }
    }
}
