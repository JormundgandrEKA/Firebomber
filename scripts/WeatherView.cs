using Godot;
using System;

namespace Firebomber;

/// <summary>The weather on the map, as pixel art: a few flat grey tones with hard stepped edges, never more than half opaque, drawn under the aircraft (the designer's chosen style, docs/reference/clouds).
/// The weather is worked out on a coarse grid, smoothed up to the art pixels, roughened by a fixed tile of small billows that drifts with the clouds, and then cut at set levels into tones. Light cloud is pale; the heavier and wetter, the darker.
/// It is remade a few times a second at most, and only when the clock has moved. It thins out when zoomed in over a city, so the fires can still be seen.</summary>
public partial class WeatherView : Node2D
{
    public Main M = null!;
    const double Art = 2000; const int Step = 3, W = (int)(World.WidthM / Art), H = (int)(World.HeightM / Art), CW = W / Step + 2, CH = H / Step + 2, Tile = 512;
    readonly byte[] px = new byte[W * H * 4]; readonly float[] coarse = new float[CW * CH], relief = new float[Tile * Tile], lit = new float[Tile * Tile];
    Image? img; ImageTexture? tex; double lastT = -1e12; long lastReal;
    // the tones, from the darkest: grey, and opacity out of 255, never over half
    static readonly (byte r, byte g, byte b, byte a)[] Tones = { (58, 62, 70, 127), (88, 93, 102, 127), (124, 129, 138, 124), (166, 170, 178, 116), (205, 208, 214, 106), (241, 243, 246, 96) };
    // how heavy the weather must be for each tone, lightest first
    static readonly (float at, int tone)[] Cuts = { (.15f, 5), (.26f, 4), (.46f, 3), (.68f, 2), (.92f, 1), (1.14f, 0) };

    public override void _Ready() { TextureFilter = TextureFilterEnum.Nearest; MakeBillows(); }

    /// <summary>A tiling field of round domes at three sizes: added to the smooth weather before it is cut, it breaks the tone edges into small steps.</summary>
    void MakeBillows()
    {
        var r = new Random(4711); var b = new float[Tile * Tile];
        foreach (var (cell, weight) in new[] { (15, .55f), (8, .3f), (4, .15f) })
        {
            var one = new float[Tile * Tile]; int n = Tile / cell;
            for (int gy = 0; gy < n; gy++)
                for (int gx = 0; gx < n; gx++)
                {
                    double cx = (gx + r.NextDouble()) * cell, cy = (gy + r.NextDouble()) * cell, rad = cell * (.8 + .55 * r.NextDouble()); int R = (int)rad + 1;
                    for (int y = (int)cy - R; y <= (int)cy + R; y++)
                        for (int x = (int)cx - R; x <= (int)cx + R; x++)
                        {
                            double q = 1 - ((x - cx) * (x - cx) + (y - cy) * (y - cy)) / (rad * rad); if (q <= 0) continue;
                            int k = (y & (Tile - 1)) * Tile + (x & (Tile - 1)); one[k] = Math.Max(one[k], (float)Math.Sqrt(q));
                        }
                }
            for (int k = 0; k < b.Length; k++) b[k] += weight * one[k];
        }
        for (int y = 0; y < Tile; y++)
            for (int x = 0; x < Tile; x++)
            {
                int k = y * Tile + x; relief[k] = b[k] - .55f;
                lit[k] = Math.Clamp(b[k] - b[((y + 2) & (Tile - 1)) * Tile + ((x + 2) & (Tile - 1))], -.5f, .5f);
            }
    }

    public override void _Process(double delta)
    {
        var camp = M.Camp; if (camp == null) return;
        long now = System.Environment.TickCount64;
        if (tex == null || Math.Abs(camp.Now - lastT) > 600 && now - lastReal > 250) { lastT = camp.Now; lastReal = now; Remake(camp); }
        QueueRedraw();
    }

    void Remake(Campaign camp)
    {
        for (int j = 0; j < CH; j++)
            for (int i = 0; i < CW; i++)
            {
                var (c, r) = Weather.At(camp.Seed, i * Step * Art, World.HeightM - j * Step * Art, camp.Now);
                coarse[j * CW + i] = (float)((c - .32) * 1.1 + r * .55);
            }
        int drift = (int)(camp.Now * 8 / Art);
        for (int j = 0; j < H; j++)
        {
            int cj = j / Step; float fj = (j % Step) / (float)Step;
            for (int i = 0; i < W; i++)
            {
                int ci = i / Step; float fi = (i % Step) / (float)Step, a = coarse[cj * CW + ci], b = coarse[cj * CW + ci + 1], c = coarse[(cj + 1) * CW + ci], d = coarse[(cj + 1) * CW + ci + 1];
                float h = a + (b - a) * fi + (c - a) * fj + (a - b - c + d) * fi * fj;
                int k = (j * W + i) * 4, tile = (j & (Tile - 1)) * Tile + ((i - drift) & (Tile - 1)), tone = -1; float t = h + .4f * relief[tile] + .1f * lit[tile];
                foreach (var (at, v) in Cuts) if (t >= at) tone = v;
                if (tone < 0) { px[k + 3] = 0; continue; }
                var q = Tones[tone]; px[k] = q.r; px[k + 1] = q.g; px[k + 2] = q.b; px[k + 3] = q.a;
            }
        }
        if (tex == null) { img = Image.CreateFromData(W, H, false, Image.Format.Rgba8, px); tex = ImageTexture.CreateFromImage(img); }
        else { img!.SetData(W, H, false, Image.Format.Rgba8, px); tex.Update(img); }
    }

    public override void _Draw()
    {
        if (tex == null) return; var a = M.W2S(0, World.HeightM); var b = M.W2S(World.WidthM, 0);
        float fade = (float)Math.Clamp(1 - (M.Zoom - .01) / .05, .22, 1);
        DrawTextureRect(tex, new Rect2(a, b - a), false, new Color(1, 1, 1, fade));
    }
}
