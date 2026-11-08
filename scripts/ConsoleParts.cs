using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Firebomber;

/// <summary>What the next click on the map is for, when a field is being given an order.</summary>
public enum PickMode { None, Patrol, FerryTo, BombAt }

/// <summary>The console's parts: recessed screens, engraved plates, screws, bar meters, the chart drawer, envelope icons, and pop-ups that sit where you clicked.</summary>
public partial class ConsoleUi
{
    Vector2 Mouse => M.MouseForReadout;
    /// <summary>The exact figures under the mouse, drawn last, above everything.</summary>
    readonly List<(string t, Color c)> tip = new();
    static string Exact(double v) => Math.Abs(v) >= 1000 ? $"{v:N0}" : $"{v:0.#}";
    void DrawTip(Vector2 size)
    {
        if (tip.Count == 0) return; float w = tip.Max(q => Width(q.t, 13)) + 18, h = tip.Count * 17 + 10, x = Mouse.X + 16, y = Mouse.Y - h - 8;
        if (x + w > size.X - 6) x = Mouse.X - w - 12; if (y < 38) y = Mouse.Y + 18;
        var r = new Rect2(x, y, w, h); DrawRect(r.Grow(1), new Color(.01f, .02f, .01f, .95f)); DrawRect(r, new Color(.08f, .13f, .08f, .97f)); DrawRect(r, new Color(Amber, .8f), false, 1);
        for (int i = 0; i < tip.Count; i++) Text(x + 9, y + 18 + i * 17, tip[i].t, 13, tip[i].c);
    }

    /// <summary>A recessed screen: dark, shadowed along the top and left, lit along the bottom and right.</summary>
    void Bezel(Rect2 r)
    {
        DrawRect(r, new Color(.015f, .03f, .02f, .96f));
        DrawLine(r.Position, new Vector2(r.End.X, r.Position.Y), new Color(0, 0, 0, .9f), 2);
        DrawLine(r.Position, new Vector2(r.Position.X, r.End.Y), new Color(0, 0, 0, .9f), 2);
        DrawLine(new Vector2(r.Position.X, r.End.Y), r.End, new Color(.6f, .7f, .55f, .3f), 1);
        DrawLine(new Vector2(r.End.X, r.Position.Y), r.End, new Color(.6f, .7f, .55f, .3f), 1);
    }

    /// <summary>An engraved title plate. Returns its width.</summary>
    float Plate(float x, float y, string t, Color accent, int fs = 14)
    {
        float w = Width(t, fs) + 16, h = fs + 9; var r = new Rect2(x, y, w, h);
        DrawRect(r, new Color(accent, .92f));
        DrawLine(r.Position, new Vector2(r.End.X, y), new Color(1, 1, 1, .45f), 1);
        DrawLine(new Vector2(x, r.End.Y), r.End, new Color(0, 0, 0, .55f), 1);
        Text(x + 8, y + fs + 2, t, fs, new Color(.03f, .05f, .03f));
        return w;
    }

    void Screw(Vector2 p)
    {
        DrawCircle(p, 2.6f, new Color(.42f, .46f, .4f)); DrawCircle(p + new Vector2(-.5f, -.5f), 1.6f, new Color(.62f, .66f, .58f));
        DrawLine(p + new Vector2(-2, 1), p + new Vector2(2, -1), new Color(.1f, .12f, .1f), 1);
    }

    /// <summary>A horizontal meter with its label on the left and its reading on the right.</summary>
    void Bar(Rect2 r, double share, Color c, string label, string reading, string? exact = null)
    {
        if (r.HasPoint(Mouse)) tip.Add(($"{label}: {exact ?? $"{share * 100:0.0}%"}", Ink));
        Text(r.Position.X, r.Position.Y + 13, label, 12, Dim); float bx = r.Position.X + 72, bw = r.Size.X - 72 - 62;
        var well = new Rect2(bx, r.Position.Y + 2, bw, 12); Bezel(well); DrawRect(new Rect2(bx + 1, r.Position.Y + 3, (bw - 2) * (float)Math.Clamp(share, 0, 1), 10), c);
        for (int i = 1; i < 4; i++) DrawLine(new Vector2(bx + bw * i / 4, r.Position.Y + 2), new Vector2(bx + bw * i / 4, r.Position.Y + 14), new Color(0, 0, 0, .45f), 1);
        Text(r.End.X - Width(reading, 12), r.Position.Y + 13, reading, 12, Ink);
    }

    /// <summary>A small oscilloscope chart. X is days of the war, Y the value; the axis is marked in weeks. Each series is a line of its own colour; several get a legend.</summary>
    void Chart(Rect2 r, string title, Color titleCol, double? fixedMax, params (IReadOnlyList<Vector2> pts, Color c, string name)[] series)
    {
        Bezel(r); float x0 = r.Position.X + 32, x1 = r.End.X - 6, y0 = r.Position.Y + 24, y1 = r.End.Y - 16;
        Text(r.Position.X + 5, r.Position.Y + 12, title, 11, titleCol);
        float now = M.Camp != null ? (float)(M.Camp.Now / Campaign.Day) : 0, tMin = series.Where(s => s.pts.Count > 0).Select(s => s.pts[0].X).DefaultIfEmpty(0).Min(), tMax = Math.Max(now, tMin + 14);
        double vMax = fixedMax ?? Nice(Math.Max(1, series.Where(s => s.pts.Count > 0).Select(s => (double)s.pts.Max(p => p.Y)).DefaultIfEmpty(1).Max() * 1.05));
        for (int i = 0; i <= 2; i++)
        {
            float y = y1 + (y0 - y1) * i / 2; DrawLine(new Vector2(x0, y), new Vector2(x1, y), new Color(Green, .16f), 1);
            double v = vMax * i / 2; string lab = v >= 10000 ? $"{v / 1000:0.#}k" : $"{v:0}"; Text(r.Position.X + 29 - Width(lab, 10), y + 4, lab, 10, Dim);
        }
        int firstW = (int)(tMin / 7), lastW = (int)(tMax / 7), step = Math.Max(1, (lastW - firstW) / 5);
        for (int w = firstW; w <= lastW; w += step)
        {
            float x = x0 + (x1 - x0) * (w * 7 - tMin) / (tMax - tMin); if (x < x0 - 1 || x > x1 + 1) continue;
            DrawLine(new Vector2(x, y0), new Vector2(x, y1), new Color(Green, .1f), 1); Text(x + 2, r.End.Y - 3, $"W{w + 1}", 10, Dim);
        }
        Vector2 At(Vector2 p) => new(x0 + (x1 - x0) * (p.X - tMin) / (tMax - tMin), y1 + (y0 - y1) * (float)Math.Clamp(p.Y / vMax, 0, 1));
        foreach (var (pts, c, _) in series)
        {
            if (pts.Count == 0) continue; int thin = Math.Max(1, pts.Count / (int)Math.Max(2, x1 - x0)); var line = new List<Vector2>();
            for (int i = 0; i < pts.Count; i += thin) line.Add(At(pts[i]));
            line.Add(At(pts[^1])); line.Add(At(new Vector2(now, pts[^1].Y)));
            var arr = line.ToArray(); if (arr.Length < 2) continue;
            DrawPolyline(arr, new Color(c, .25f), 3.5f); DrawPolyline(arr, c, 1.4f);
        }
        if (r.HasPoint(Mouse) && tMax > tMin)
        {
            float mx = Math.Clamp(Mouse.X, x0, x1), day = tMin + (mx - x0) / (x1 - x0) * (tMax - tMin);
            DrawLine(new Vector2(mx, y0), new Vector2(mx, y1), new Color(Amber, .75f), 1);
            tip.Add(($"Week {(int)(day / 7) + 1}, day {(int)day + 1}", Dim));
            foreach (var (pts, c, name) in series)
            {
                if (pts.Count == 0 || day < pts[0].X) continue; int lo = 0, hi = pts.Count - 1;
                while (lo < hi) { int m = (lo + hi + 1) / 2; if (pts[m].X <= day) lo = m; else hi = m - 1; }
                var p = pts[lo]; DrawCircle(At(p with { X = mx > x0 ? day : p.X }), 2.5f, c);
                tip.Add(($"{(name == "" ? title : name)}: {Exact(p.Y)}", c));
            }
        }
        if (series.Length > 1) { float lx = r.End.X - 4; foreach (var (_, c, name) in series.Reverse()) { lx -= Width(name, 10) + 6; Text(lx, r.Position.Y + 12, name, 10, c); } }
    }

    /// <summary>A round number at or above this one, for the top of a scale.</summary>
    static double Nice(double v) { double p = Math.Pow(10, Math.Floor(Math.Log10(v))); foreach (var m in new[] { 1, 2, 2.5, 4, 5, 10 }) if (m * p >= v) return m * p; return 10 * p; }

    /// <summary>An envelope; open, it has a sheet of paper standing out of it.</summary>
    void Envelope(Rect2 r, bool open)
    {
        var body = new Rect2(r.Position.X + 2, r.Position.Y + r.Size.Y * .42f, r.Size.X - 4, r.Size.Y * .54f);
        if (open) DrawRect(new Rect2(r.Position.X + 6, r.Position.Y + 1, r.Size.X - 12, r.Size.Y * .7f), new Color(.82f, .72f, .55f));
        DrawRect(body, new Color(.9f, .9f, .82f)); DrawRect(body, new Color(.1f, .12f, .1f), false, 1);
        DrawLine(body.Position, body.Position + new Vector2(body.Size.X / 2, body.Size.Y * .6f), new Color(.1f, .12f, .1f), 1);
        DrawLine(new Vector2(body.End.X, body.Position.Y), body.Position + new Vector2(body.Size.X / 2, body.Size.Y * .6f), new Color(.1f, .12f, .1f), 1);
    }

    /// <summary>Where a pop-up goes: up and to the right of what it belongs to, or wherever else it will fit on the screen.</summary>
    Rect2 PopRect(Vector2 anchor, Vector2 size)
    {
        var view = GetViewportRect().Size; var p = anchor + new Vector2(30, -size.Y - 20);
        if (p.X + size.X > view.X - 6) p.X = anchor.X - size.X - 30;
        if (p.Y < 42) p.Y = anchor.Y + 24;
        p.X = Math.Clamp(p.X, 6, Math.Max(6, view.X - size.X - 6)); p.Y = Math.Clamp(p.Y, 42, Math.Max(42, view.Y - size.Y - 36));
        return new Rect2(p, size);
    }
    /// <summary>The line from a pop-up to what it belongs to, with a ring round the thing itself.</summary>
    void Leader(Vector2 anchor, Rect2 r, Color c)
    {
        if (!GetViewportRect().HasPoint(anchor)) return;
        var to = new Vector2(Math.Clamp(anchor.X, r.Position.X, r.End.X), Math.Clamp(anchor.Y, r.Position.Y, r.End.Y));
        DrawLine(anchor, to, new Color(c, .85f), 1.5f); DrawArc(anchor, 7, 0, Mathf.Tau, 20, new Color(c, .95f), 1.5f);
    }

    /// <summary>Text broken into lines that fit, keeping the line breaks already in it.</summary>
    List<string> Wrap(string t, int fs, float width)
    {
        var o = new List<string>();
        foreach (var para in t.Split('\n'))
        {
            string line = "";
            foreach (var word in para.Split(' '))
            {
                string tryLine = line == "" ? word : line + " " + word;
                if (line != "" && Width(tryLine, fs) > width) { o.Add(line); line = word; } else line = tryLine;
            }
            o.Add(line);
        }
        return o;
    }
}
