using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Firebomber;

/// <summary>Draws formations, routes and bomb hits on the map, and holds what the player has picked. Orders go from here to Air.</summary>
public partial class AirView : Node2D
{
    public Main M = null!;
    public readonly HashSet<Formation> Chosen = new();
    public Formation? Primary;
    public readonly HashSet<Bomber> Picked = new();
    public int Mode;                 // 0 none, 1 route, 2 aim
    public bool Boxing; public Vector2 BoxA, BoxB;
    public Formation? Following;
    /// <summary>Screen rectangles of the formation tags, so map labels keep clear of them.</summary>
    public readonly List<Rect2> Tags = new();
    Font font = null!;

    static readonly Color Foe = new(.85f, .42f, .32f), Body = new(.72f, .78f, .84f), Spent = new(.62f, .68f, .74f), InkC = new(.05f, .07f, .1f), Gold = new(1f, .85f, .4f), Route = new(1f, .55f, .05f), Pick = new(.4f, .95f, 1f);
    public override void _Ready() { TextureFilter = TextureFilterEnum.Nearest; font = ThemeDB.FallbackFont; }

    public override void _Process(double delta)
    {
        Chosen.RemoveWhere(f => !M.Air.Formations.Contains(f) || f.Destroyed);
        Picked.RemoveWhere(a => !a.Alive);
        if (Primary == null || !Chosen.Contains(Primary)) Primary = Chosen.FirstOrDefault();
        QueueRedraw();
    }

    public (double x, double y) Pos(Bomber a) { double fr = a.F.Phase == Phase.Waiting || M.Clock.Paused ? 0 : M.Air.Frac; return (a.X + Math.Cos(a.Th) * a.V * fr, a.Y + Math.Sin(a.Th) * a.V * fr); }
    public Vector2 Screen(Bomber a) { var (x, y) = Pos(a); return M.W2S(x, y); }
    static bool Flying(Formation f) => f.Phase != Phase.Waiting && f.Phase != Phase.Landed;
    static float AltScale(double z) => (float)Math.Max(.75, Sprites.AltScale(z));   // icons never shrink out of sight, even low down
    double Pxm(Bomber a) => Sprites.PxPerMetre(a.F.Kind, M.Zoom, a.Z);

    public override void _Draw()
    {
        var size = GetViewportRect().Size; var view = new Rect2(-60, -60, size.X + 120, size.Y + 120);
        Tags.Clear();
        Fields(view); Batteries(view); GunFlashes(this); Routes();
        foreach (var f in M.Air.Formations) if (Flying(f)) DrawFormation(f, view);
        foreach (var a in Picked) if (Flying(a.F)) DrawArc(Screen(a), (float)Math.Max(9, Sprites.Specs[a.F.Kind].Span * Pxm(a) * .62), 0, Mathf.Tau, 24, new Color(Pick, .95f), 2);
        var wb = M.Air.Watch;
        if (wb != null && wb.Alive && Flying(wb.F))
        {
            var p = Screen(wb); float r0 = (float)Math.Max(12, Sprites.Specs[wb.F.Kind].Span * Pxm(wb) * .7), l = r0 * .45f;
            foreach (var (dx, dy) in new[] { (1, 1), (-1, 1), (1, -1), (-1, -1) }) { var q = p + new Vector2(dx * r0, dy * r0); DrawLine(q, q - new Vector2(dx * l, 0), Gold, 1.5f); DrawLine(q, q - new Vector2(0, dy * l), Gold, 1.5f); }
        }
        if (Boxing) { var r = new Rect2(BoxA, BoxB - BoxA).Abs(); DrawRect(r, new Color(Pick, .12f)); DrawRect(r, new Color(Pick, .9f), false, 1.5f); }
    }

    void Fields(Rect2 view)
    {
        var sites = M.Forces?.Sites; double z = M.Zoom;
        if (sites != null)
            foreach (var st in sites.All)
            {
                if (st.Kind == SiteKind.Flak) continue;
                var c = M.W2S(st.X, st.Y); if (!view.Grow(200).HasPoint(c)) continue;
                var along = new Vector2(Mathf.Cos((float)st.Ang), -Mathf.Sin((float)st.Ang)); var across = new Vector2(-along.Y, along.X);
                if (z >= .006)
                {
                    float gw = (st.Kind == SiteKind.Field ? 1000 : st.Kind == SiteKind.Depot ? 340 : 110) * (float)z, gd = (st.Kind == SiteKind.Field ? 520 : st.Kind == SiteKind.Depot ? 230 : 90) * (float)z;
                    DrawColoredPolygon(new[] { c - along * gw - across * gd, c + along * gw - across * gd, c + along * gw + across * gd, c - along * gw + across * gd }, new Color(.46f, .47f, .34f));
                }
                foreach (var p in st.Parts)
                {
                    if (p.Kind != PartKind.Runway && z < .012) continue;
                    var q = M.W2S(p.X, p.Y); float w = Math.Max(p.Kind == PartKind.Runway ? 1.2f : 1, p.W * (float)z) / 2, d = Math.Max(1, p.D * (float)z) / 2;
                    var col = p.Down ? new Color(.1f, .08f, .07f) : p.Kind switch { PartKind.Runway => new Color(.34f, .33f, .29f), PartKind.Hangar => new Color(.5f, .5f, .46f), PartKind.Tank => new Color(.62f, .6f, .35f), PartKind.Bombs => new Color(.42f, .36f, .24f), PartKind.Mast => new Color(.75f, .78f, .8f), _ => new Color(.45f, .45f, .42f) };
                    DrawColoredPolygon(new[] { q - along * w - across * d, q + along * w - across * d, q + along * w + across * d, q - along * w + across * d }, col);
                }
                if (z >= .03) continue;
                var edge = st.Wolf ? new Color(.12f, .2f, .38f, .95f) : new Color(.45f, .1f, .07f, .95f); var fill = st.Wolf ? new Color(.37f, .62f, .95f) : new Color(.85f, .3f, .22f);
                if (st.Kind == SiteKind.Field)
                {
                    DrawRect(new Rect2(c - new Vector2(5, 5), new Vector2(10, 10)), edge, false, 2); DrawRect(new Rect2(c - new Vector2(2, 2), new Vector2(4, 4)), st.Closed ? new Color(.1f, .08f, .07f) : fill);
                    if (st.Closed) { DrawLine(c - new Vector2(7, 7), c + new Vector2(7, 7), new Color(1, .45f, .3f), 2); DrawLine(c + new Vector2(-7, 7), c + new Vector2(7, -7), new Color(1, .45f, .3f), 2); }
                }
                else if (st.Kind == SiteKind.Depot) { var a = c + new Vector2(0, -6); DrawColoredPolygon(new[] { a, c + new Vector2(6, 0), c + new Vector2(0, 6), c + new Vector2(-6, 0) }, st.Lost > 2 ? new Color(.2f, .17f, .15f) : fill); DrawPolyline(new[] { a, c + new Vector2(6, 0), c + new Vector2(0, 6), c + new Vector2(-6, 0), a }, edge, 1.5f); }
                else { var col = st.Working ? fill : new Color(.2f, .17f, .15f); DrawLine(c + new Vector2(0, 6), c + new Vector2(0, -5), col, 2); DrawArc(c + new Vector2(0, -3), 6, -2.6f, -.5f, 10, col, 2); }
            }
        else
            foreach (var f in M.World.Fields.Concat(M.World.EnemyFields))
            {
                var p = M.W2S(f.X, f.Y); if (!view.HasPoint(p)) continue;
                float len = (float)Math.Max(3, 1600 * M.Zoom), wd = (float)Math.Max(1, 60 * M.Zoom);
                var d = new Vector2(.82f, -.57f) * len / 2; var n = new Vector2(.57f, .82f) * wd / 2;
                DrawColoredPolygon(new[] { p - d - n, p + d - n, p + d + n, p - d + n }, new Color(.34f, .33f, .29f));
                if (M.Zoom >= .03) continue;
                var edge = f.Wolf ? new Color(.12f, .2f, .38f, .95f) : new Color(.45f, .1f, .07f, .95f); var fill = f.Wolf ? new Color(.37f, .62f, .95f) : new Color(.85f, .3f, .22f);
                DrawRect(new Rect2(p - new Vector2(5, 5), new Vector2(10, 10)), edge, false, 2); DrawRect(new Rect2(p - new Vector2(2, 2), new Vector2(4, 4)), fill);
            }
    }
    /// <summary>What is on each airfield, beside it: ours exactly, theirs as an estimate. Drawn above the night, like the city names, so it can be read at any hour.</summary>
    public void FieldLabels(CanvasItem ci)
    {
        var forces = M.Forces; if (forces == null || M.Zoom < .0025 || M.Zoom >= .03) return;
        var size = GetViewportRect().Size; var view = new Rect2(-40, -40, size.X + 80, size.Y + 80);
        foreach (var f in M.World.Fields.Concat(M.World.EnemyFields))
        {
            var p = M.W2S(f.X, f.Y); if (!view.HasPoint(p)) continue;
            var b = forces.Of(f); string s;
            if (f.Wolf) s = string.Join("  ", Forces.WolfTypes.Where(k => b.S(k).Ready > 0).Select(k => $"{Sprites.Specs[k].Name.Split(' ')[0]} {b.S(k).Ready}"));
            else { int fi = b.S(Craft.I8).Ready + b.S(Craft.I7).Ready, bo = b.S(Craft.TB6).Ready + b.S(Craft.SB3).Ready; s = (fi > 0 ? $"~{Forces.About(fi)} fighters" : "") + (bo > 0 ? $"  ~{Forces.About(bo)} bombers" : ""); }
            if (s == "") continue;
            var at = (p + new Vector2(9, 16)).Round();
            ci.DrawStringOutline(font, at, s, HorizontalAlignment.Left, -1, 12, 3, new Color(.04f, .05f, .03f, .85f));
            ci.DrawString(font, at, s, HorizontalAlignment.Left, -1, 12, f.Wolf ? new Color(.75f, .88f, 1f) : new Color(1f, .66f, .56f));
        }
    }

    /// <summary>The gun pits round a city, close in; and, on the planning chart, how far out its guns reach at bombing height.</summary>
    void Batteries(Rect2 view)
    {
        var fl = M.Air.Flak; if (fl == null) return; double z = M.Zoom;
        if (M.Holding && z < .02)
            foreach (var g in fl.Batteries.Where(q => !q.Wolf).GroupBy(q => q.City))
            {
                var c = g.Key; double r = g.Max(q => Math.Sqrt((q.X - c.X) * (q.X - c.X) + (q.Y - c.Y) * (q.Y - c.Y))) + 8600; var p = M.W2S(c.X, c.Y); float rp = (float)(r * z);
                if (p.X + rp < 0 || p.X - rp > view.End.X || p.Y + rp < 0 || p.Y - rp > view.End.Y) continue;
                var col = new Color(.85f, .2f, .15f, .45f); int n = Math.Max(24, (int)(rp / 3));
                for (int k = 0; k < n; k += 2) DrawArc(p, rp, Mathf.Tau * k / n, Mathf.Tau * (k + 1) / n, 3, col, 1.2f);
                Outlined(p + new Vector2(-12, -rp - 4), $"FLAK {g.Sum(q => q.Guns)}", 10, new Color(.95f, .35f, .3f, .9f));
            }
        if (z < .03) return;
        foreach (var b in fl.Batteries)
        {
            var p = M.W2S(b.X, b.Y); if (!view.HasPoint(p)) continue;
            float ring = (float)Math.Max(4, 32 * z), pit = (float)Math.Max(1.2, 5 * z);
            DrawCircle(p, (float)Math.Max(1, 4 * z), new Color(.2f, .2f, .18f));
            for (int k = 0; k < b.Guns; k++)
            {
                var q = p + new Vector2(ring, 0).Rotated(Mathf.Tau * k / b.Guns + 0.3f);
                DrawCircle(q, pit * 1.7f, new Color(.48f, .44f, .34f)); DrawCircle(q, pit, new Color(.14f, .14f, .12f));
            }
        }
    }
    /// <summary>Above the night: guns flash as they fire.</summary>
    public void GunFlashes(CanvasItem ci)
    {
        var fl = M.Air.Flak; if (fl == null || M.Zoom < .012) return; var size = GetViewportRect().Size; var view = new Rect2(0, 0, size.X, size.Y); double T = M.Air.T, z = M.Zoom;
        foreach (var b in fl.Batteries)
        {
            double life = .35 + .5 * M.Air.Dark, age = T - b.LastFire; if (age > life || age < 0) continue; var p = M.W2S(b.X, b.Y); if (!view.HasPoint(p)) continue;
            float ring = (float)Math.Max(4, 32 * z), a = (float)(1 - age / life);
            for (int k = 0; k < b.Guns; k++) ci.DrawCircle(p + new Vector2(ring, 0).Rotated(Mathf.Tau * k / b.Guns + 0.3f), (float)Math.Max(1.5, 9 * z), new Color(1f, .85f, .45f, a));
        }
    }

    void Dashed(Vector2 a, Vector2 b, Color c, float w, float dash)
    {
        var d = b - a; float len = d.Length(); if (len < 1 || len > 8000) return; var n = d / len;
        for (float t = 0; t < len; t += dash * 2) DrawLine(a + n * t, a + n * Math.Min(t + dash, len), c, w);
    }
    void Routes()
    {
        if (M.Setup) return;
        foreach (var f in M.Air.Formations)
        {
            bool chosen = Chosen.Contains(f); var col = chosen ? Route : new Color(.75f, .45f, .1f, .55f);
            if (!f.Wolf && f.Phase == Phase.Landed) continue;
            var (lx, ly) = Pos(f.Lead); var from = M.W2S(lx, ly);
            if (f.Phase == Phase.Returning) { if (chosen) Dashed(from, M.W2S(f.HomeX, f.HomeY), new Color(col, .6f), 1.2f, 6); continue; }
            if (f.Phase != Phase.Waiting && f.Phase != Phase.Outbound) continue;
            if (!f.Wolf) continue;      // the enemy's plans are not on our chart
            int n = 1;
            foreach (var r in f.Route)
            {
                var q = M.W2S(r.x, r.y); Dashed(from, q, col, chosen ? 1.5f : 1, 8);
                DrawRect(new Rect2(q - new Vector2(4, 4), new Vector2(8, 8)), col, false, chosen ? 2 : 1);
                if (chosen) Outlined(q + new Vector2(8, -5), n.ToString(), 12, Route);
                from = q; n++;
            }
            var aim = M.W2S(f.AimX, f.AimY); Dashed(from, aim, col, chosen ? 1.5f : 1, 8);
            DrawArc(aim, 8, 0, Mathf.Tau, 20, col, chosen ? 2 : 1); DrawLine(aim - new Vector2(13, 0), aim + new Vector2(13, 0), col, 1); DrawLine(aim - new Vector2(0, 13), aim + new Vector2(0, 13), col, 1);
        }
    }
    bool OnChart => M.Zoom < .009;
    void Outlined(Vector2 at, string t, int fs, Color c)
    {
        at = at.Round();
        DrawStringOutline(font, at, t, HorizontalAlignment.Left, -1, fs, 3, OnChart ? new Color(.86f, .82f, .64f, .85f) : new Color(.04f, .05f, .03f, .8f));
        DrawString(font, at, t, HorizontalAlignment.Left, -1, fs, c);
    }

    void DrawFormation(Formation f, Rect2 view)
    {
        bool chosen = Chosen.Contains(f); var lead = Screen(f.Lead);
        double extent = 900 * f.Spread * M.Zoom;
        if (extent < 50)
        {
            // far out: one icon for the whole box
            if (!view.HasPoint(lead)) return;
            Triangle(lead, f.Lead.Th, 9 * AltScale(f.Lead.Z), !f.Wolf ? Foe : chosen ? Gold : Body);
        }
        else
        {
            var spec = Sprites.Specs[f.Kind];
            bool triangles = spec.Span * Sprites.PxPerMetre(f.Kind, M.Zoom, 6000) < 16;   // decided for the whole box, so a box never mixes the two
            if (triangles)
                foreach (var a in f.Ac) { var p = Screen(a); if (view.HasPoint(p)) Triangle(p, a.Th, (a == f.Lead ? 8 : 6) * AltScale(a.Z), !f.Wolf ? Foe : a == f.Lead ? Gold : a.Damage > .3 ? new Color(.85f, .45f, .35f) : a.Released ? Spent : Body); }
            else
            {
                // all shadows first, so that no shadow falls on a neighbour; then low aircraft under high ones
                var order = f.Ac.Where(a => view.HasPoint(Screen(a))).OrderBy(a => a.Z).ToList();
                foreach (var a in order) Sprites.Shadow(this, f.Kind, Screen(a), a.Th, Pxm(a), a.Z);
                foreach (var a in order) Sprites.Draw(this, f.Kind, Screen(a), a.Th, Pxm(a), Hangar.DamageTint((float)a.Damage));
                Sprites.End(this);
                // a badly hit bomber trails fire from an engine
                foreach (var a in order)
                    if (a.H.Burning)
                    {
                        var e = Screen(a) + new Vector2((float)(Math.Sin(a.Th) * 7 * Pxm(a)), (float)(Math.Cos(a.Th) * 7 * Pxm(a))); var back = new Vector2(-(float)Math.Cos(a.Th), (float)Math.Sin(a.Th));
                        for (int k = 2; k >= 0; k--)
                        {
                            float s = (float)Math.Max(6, 7 * Pxm(a)) * (1 - k * .22f);
                            var c = e + back * k * s * .8f;
                            DrawTextureRectRegion(FireView.Flames, new Rect2((c - new Vector2(s / 2, s / 2)).Round(), new Vector2(s, s)), new Rect2((M.Clock.Paused ? k : (int)(Time.GetTicksMsec() / 100) + a.Id + k) % 4 * 16, 0, 16, 16));
                        }
                    }
                if (view.HasPoint(lead)) DrawArc(lead, (float)(spec.Span * Pxm(f.Lead) * .7 + 3), 0, Mathf.Tau, 28, new Color(Gold, chosen ? .8f : .35f), 1);
            }
        }
        if (M.Zoom < .06 && view.HasPoint(lead))
        {
            string t = $"{f.Name} {f.Ac.Count}"; float w = font.GetStringSize(t, HorizontalAlignment.Left, -1, 12).X;
            // the tag takes the first free side of its icon, so tags of close formations do not run together
            var at = lead + new Vector2(13, 4);
            foreach (var o in new[] { new Vector2(13, 4), new Vector2(-13 - w, 4), new Vector2(-w / 2, -13), new Vector2(-w / 2, 24), new Vector2(13, -11), new Vector2(13, 19), new Vector2(-13 - w, -11), new Vector2(-13 - w, 19) })
            {
                var r = new Rect2(lead + o + new Vector2(0, -12), new Vector2(w, 15));
                if (!Tags.Any(g => g.Intersects(r))) { at = lead + o; break; }
            }
            Outlined(at, t, 12, !f.Wolf ? Foe : OnChart ? (chosen ? new Color(.55f, .25f, 0) : new Color(.14f, .16f, .25f)) : (chosen ? Gold : Body));
            Tags.Add(new Rect2(at + new Vector2(0, -12), new Vector2(w, 15)));
        }
    }
    void Triangle(Vector2 p, double th, float s, Color c)
    {
        var f = new Vector2((float)Math.Cos(th), -(float)Math.Sin(th)); var r = new Vector2(-f.Y, f.X);
        var t = new[] { p + f * s, p - f * s * .8f + r * s * .7f, p - f * s * .8f - r * s * .7f };
        DrawColoredPolygon(t, c); DrawPolyline(new[] { t[0], t[1], t[2], t[0] }, InkC, 1);
    }
    // ---------------------------------------------------------------- picking and orders
    public void Each(Action<Formation> act) { foreach (var f in Chosen) act(f); }
    public void ChooseOnly(Formation f) { Chosen.Clear(); Chosen.Add(f); Primary = f; Mode = 0; }
    public void Toggle(Formation f) { if (!Chosen.Remove(f)) { Chosen.Add(f); Primary = f; } Mode = 0; }
    public void ChooseAll() { foreach (var f in M.Air.Formations) if (f.Wolf) Chosen.Add(f); }

    public Bomber? BomberAt(Vector2 p)
    {
        Bomber? best = null; double bd = Math.Max(14, Air.Span * M.Zoom * .6);
        foreach (var f in M.Air.Formations) { if (!Flying(f)) continue; foreach (var a in f.Ac) { double d = (Screen(a) - p).Length(); if (d < bd) { bd = d; best = a; } } }
        return best;
    }
    /// <summary>A plain left click on the map. Returns true if it was used.</summary>
    public bool MapClick(Vector2 p)
    {
        if (M.Ui.Pick != PickMode.None) return M.Ui.MapPicked(p);
        if (Mode == 3 && Primary != null)
        {
            foreach (var fld in M.World.Fields) if ((M.W2S(fld.X, fld.Y) - p).Length() < 14 && Math.Sqrt(Math.Pow(fld.X - Primary.Lead.X, 2) + Math.Pow(fld.Y - Primary.Lead.Y, 2)) / 1000 <= Air.FuelKm(Primary)) { foreach (var f in Chosen) Air.Divert(f, fld); Mode = 0; break; }
            return true;
        }
        // an airfield: open it in the bases panel
        if (Mode == 0 && M.Forces != null && M.Camp != null && !M.Holding)
            foreach (var f in M.World.Fields.Concat(M.World.EnemyFields)) if ((M.W2S(f.X, f.Y) - p).Length() < 9) { M.Ui.OpenField(M.Forces.Of(f)); return true; }
        if (Mode == 0 && M.Forces != null && M.Camp != null && !M.Holding)
            foreach (var st in M.Forces.Sites.All.Where(q => q.Kind is SiteKind.Depot or SiteKind.Radar)) if ((M.W2S(st.X, st.Y) - p).Length() < 9) { M.Ui.OpenSite(st); return true; }
        if (Mode != 0 && Primary != null)
        {
            double wx = M.S2Wx(p.X), wy = M.S2Wy(p.Y), px = Mode == 1 ? Primary.Lead.X : Primary.AimX, py = Mode == 1 ? Primary.Lead.Y : Primary.AimY;
            foreach (var f in Chosen)
            {
                // several formations ordered together keep their places relative to each other
                double ox = (Mode == 1 ? f.Lead.X : f.AimX) - px, oy = (Mode == 1 ? f.Lead.Y : f.AimY) - py;
                if (Mode == 1) M.Air.AddWaypoint(f, wx + ox, wy + oy); else M.Air.SetAim(f, wx + ox, wy + oy);
            }
            if (Mode == 2) Mode = 0;
            return true;
        }
        var pilot = M.Sim.At(M.FView.Screen, p, Math.Max(12, 12 * M.Zoom * 2));
        if (pilot != null) { M.Sim.Watch = pilot; M.Air.Watch = null; return true; }
        var b = BomberAt(p); if (b == null) return false;
        M.Air.Watch = b; M.Sim.Watch = null; if (b.F.Wolf) ChooseOnly(b.F); return true;
    }
    public void RightClick(Vector2 p)
    {
        if (Mode == 1) { Mode = 0; return; }
        foreach (var f in Chosen) M.Air.SetAim(f, M.S2Wx(p.X), M.S2Wy(p.Y));
        Mode = 0;
    }
    public void FinishBox(Vector2 p)
    {
        BoxB = p; Boxing = false; var r = new Rect2(BoxA, BoxB - BoxA).Abs();
        if (r.Size.Length() < 6) { var b = BomberAt(p); if (b != null && !Picked.Remove(b)) Picked.Add(b); return; }
        foreach (var f in M.Air.Formations) if (f.Wolf && Flying(f)) foreach (var a in f.Ac) if (r.HasPoint(Screen(a))) Picked.Add(a);
    }
    public void NewFormation()
    {
        var nf = M.Air.Split(Picked); Picked.Clear();
        if (nf != null) ChooseOnly(nf);
    }
}
