using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Firebomber;

/// <summary>Draws fighters, tracers and falling wrecks. Far out a fighter is a small coloured mark; close in, a sprite at the same scale as the bombers.</summary>
public partial class FighterView : Node2D
{
    public Main M = null!;
    static readonly Color WolfC = new(.55f, .9f, 1f), DeerC = new(1f, .42f, .35f), Watch = new(1f, .85f, .4f);
    readonly List<(Tracer t, float age)> tracers = new();
    public override void _Ready() => TextureFilter = TextureFilterEnum.Nearest;

    public override void _Process(double delta)
    {
        var sim = M.Sim;
        foreach (var t in sim.Tracers) tracers.Add((t, 0)); sim.Tracers.Clear();
        if (tracers.Count > 250) tracers.RemoveRange(0, tracers.Count - 250);
        for (int k = tracers.Count - 1; k >= 0; k--) { var q = tracers[k]; q.age += (float)delta; if (q.age > .22f) tracers.RemoveAt(k); else tracers[k] = q; }
        QueueRedraw();
    }
    public Vector2 Screen(Fighter f) { double fr = M.Clock.Paused ? 0 : M.Air.Frac; return M.W2S(f.X + Math.Cos(f.Th) * f.V * fr, f.Y + Math.Sin(f.Th) * f.V * fr); }

    void Flame(Vector2 c, float s, int k) => DrawTextureRectRegion(FireView.Flames, new Rect2((c - new Vector2(s / 2, s / 2)).Round(), new Vector2(s, s)), new Rect2((M.Clock.Paused ? k : (int)(Time.GetTicksMsec() / 100) + k) % 4 * 16, 0, 16, 16));

    public void TracersTo(CanvasItem ci, float bright)
    {
        var size = GetViewportRect().Size; var view = new Rect2(-60, -60, size.X + 120, size.Y + 120); double z = M.Zoom; bool sprites = z >= .05;
        foreach (var (t, age) in tracers)
        {
            var a = M.W2S(t.X1, t.Y1); var b = M.W2S(t.X2, t.Y2); if (!view.HasPoint(a)) continue;
            // a dash travelling along the line, not a beam
            float f = age / .22f; var s = a.Lerp(b, Math.Clamp(f - .35f, 0, 1)); var e = a.Lerp(b, Math.Clamp(f + .1f, 0, 1));
            ci.DrawLine(s, e, t.Wolf ? new Color(.75f, 1f, .75f, .95f) : new Color(1f, .75f, .25f, .95f), z > .2 ? 1.6f : 1);
            if (t.Hit && f > .7f && sprites) ci.DrawRect(new Rect2(b.Round() - new Vector2(1.5f, 1.5f), new Vector2(3, 3)), new Color(1f, .95f, .6f));
        }
    }

    /// <summary>Flak bursts: a flash, then a black puff that swells and thins out over a quarter of a minute. At night only the flash shows.</summary>
    public void FlakTo(CanvasItem ci, float dark)
    {
        var fl = M.Air.Flak; if (fl == null) return; double z = M.Zoom; if (z < .012) return;
        var size = GetViewportRect().Size; var view = new Rect2(-40, -40, size.X + 80, size.Y + 80); double T = M.Air.T;
        foreach (var b in fl.Bursts)
        {
            double age = T - b.T; if (age < 0 || age > 14) continue;
            var p = M.W2S(b.X + age * 3, b.Y + age * 1.5); if (!view.HasPoint(p)) continue;      // a little wind takes it
            float px = (float)Sprites.PxPerMetre(Craft.V16, z, b.Z), r = Math.Max(1.2f, (float)(6 + 10 * Math.Min(1, age / 3)) * px);
            if (dark < .5f)
            {
                float a = (float)(.8 * Math.Pow(1 - age / 14, 1.6)) * (1 - dark); var c = new Color(.1f, .09f, .08f, a);
                int h = (int)(b.X * 7 + b.Y * 13) & 7; var o = new Vector2((h & 3) - 1.5f, (h >> 2) - .5f) * r * .35f;
                ci.DrawCircle(p, r, c); ci.DrawCircle(p + o, r * .75f, c); ci.DrawCircle(p - o * .8f, r * .6f, new Color(.16f, .15f, .13f, a * .8f));
            }
            double flash = .3 + .9 * dark; if (age < flash) ci.DrawCircle(p, Math.Max(1.5f, r * (.55f + .3f * dark)), new Color(1f, .78f, .4f, (float)(1 - age / flash)));
        }
    }

    public override void _Draw()
    {
        var size = GetViewportRect().Size; var view = new Rect2(-60, -60, size.X + 120, size.Y + 120); double z = M.Zoom;
        bool sprites = z >= .05;
        FlakTo(this, (float)M.Air.Dark);
        var fly = M.Sim.All.Where(f => f.Airborne).ToList();
        if (z < .012)
            foreach (var f in fly) { var p = Screen(f); if (view.HasPoint(p)) DrawRect(new Rect2(p - new Vector2(1.5f, 1.5f), new Vector2(3, 3)), f.Wolf ? new Color(.1f, .35f, .55f) : new Color(.7f, .12f, .08f)); }
        else if (!sprites)
            foreach (var f in fly)
            {
                var p = Screen(f); if (!view.HasPoint(p)) continue;
                var d = new Vector2((float)Math.Cos(f.Th), -(float)Math.Sin(f.Th)); var r = new Vector2(-d.Y, d.X); var c = f.Wolf ? WolfC : DeerC;
                DrawColoredPolygon(new[] { p + d * 5, p - d * 3.5f + r * 3, p - d * 3.5f - r * 3 }, c);
            }
        else
        {
            var order = fly.Where(f => view.HasPoint(Screen(f))).OrderBy(f => f.Z).ToList();
            foreach (var f in order) Sprites.Shadow(this, f.Kind, Screen(f), f.Th, Sprites.PxPerMetre(f.Kind, z, f.Z), f.Z);
            foreach (var f in order) Sprites.Draw(this, f.Kind, Screen(f), f.Th, Sprites.PxPerMetre(f.Kind, z, f.Z), Hangar.DamageTint((float)f.Damage));
            Sprites.End(this);
            foreach (var f in order)
                if (f.H.Burning) { var p = Screen(f); float px = (float)Sprites.PxPerMetre(f.Kind, z, f.Z); Flame(p - new Vector2((float)Math.Cos(f.Th), -(float)Math.Sin(f.Th)) * px * 3, Math.Max(5, px * 3), f.Id); }
        }
        foreach (var d in M.Sim.Wrecks)
        {
            var p = M.W2S(d.X, d.Y); if (!view.HasPoint(p)) continue;
            double pxm = Sprites.PxPerMetre(d.Kind, z, d.Z); float fall = (float)Math.Clamp(d.Z / 6000, .35, 1);
            if (sprites) { Sprites.Draw(this, d.Kind, p, d.Th, pxm, new Color(.42f, .34f, .3f)); Sprites.End(this); }
            Flame(p, (float)Math.Max(6, Sprites.Specs[d.Kind].Span * pxm * .55 * fall), (int)d.Spin + 3);
        }
        TracersTo(this, 1);
        var w = M.Sim.Watch;
        if (w != null && w.Airborne)
        {
            var p = Screen(w); float r = (float)Math.Max(11, Sprites.Specs[w.Kind].Span * Sprites.PxPerMetre(w.Kind, z, w.Z) * .75), l = r * .45f;
            foreach (var (dx, dy) in new[] { (1, 1), (-1, 1), (1, -1), (-1, -1) }) { var q = p + new Vector2(dx * r, dy * r); DrawLine(q, q - new Vector2(dx * l, 0), Watch, 1.5f); DrawLine(q, q - new Vector2(0, dy * l), Watch, 1.5f); }
            Vector2? tgt = w.TF != null && w.TF.Airborne ? Screen(w.TF) : w.TB != null && w.TB.Alive ? M.View.Screen(w.TB) : null;
            if (tgt != null) DrawDashedLine(p, tgt.Value, new Color(Watch, .6f), 1, 6);
        }
    }
}
