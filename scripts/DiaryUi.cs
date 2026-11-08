using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Firebomber;

/// <summary>The war diary: minimised to its header, showing the latest entries, or opened out and scrollable. War support pulls out above it.</summary>
public partial class ConsoleUi
{
    /// <summary>0 header only, 1 the latest entries, 2 opened out.</summary>
    public int DiaryMode = 1;
    public bool ShowSupport; int diaryScroll; Rect2 diaryRect;

    /// <summary>The wheel over the opened-out diary scrolls it. Returns whether it was used.</summary>
    public bool Wheel(Vector2 p, bool up)
    {
        if (DiaryMode != 2 || !diaryRect.HasPoint(p) || M.Camp == null) return false;
        diaryScroll = Math.Clamp(diaryScroll + (up ? 2 : -2), 0, Math.Max(0, M.Camp.Log.Count - 1)); return true;
    }
    void IconButton(Rect2 r, string name, bool on, Action act, Action draw)
    {
        DrawRect(r, on ? new Color(Green, .35f) : new Color(.08f, .14f, .08f, .9f)); DrawRect(r, new Color(Green, .7f), false, 1); draw(); buttons.Add((r, act)); Named[name] = r;
    }

    void DiaryPanel(Vector2 size)
    {
        var camp = M.Camp; if (camp == null || M.Planning) return;
        const float w = 540, lh = 16, head = 34; float body = DiaryMode == 0 ? 0 : DiaryMode == 1 ? 150 : Math.Min(size.Y * .62f, 560), h = head + body, x = 4, y = size.Y - 30 - 4 - h;
        diaryRect = new Rect2(x, y, w, h); Panel(diaryRect, Green);
        float px = x + 8; px += Plate(px, y + 7, "WAR DIARY", Amber, 13) + 6; Text(px, y + 21, $"DAY {camp.DayNo}  //  WEEK {camp.WeekNo}", 12, Ink);
        float bx = x + w - 34;
        IconButton(new Rect2(bx, y + 6, 26, 22), "diary size", DiaryMode == 2, () => { DiaryMode = DiaryMode == 2 ? 1 : 2; diaryScroll = 0; }, () => { });
        Envelope(new Rect2(bx + 2, y + 7, 22, 20), DiaryMode == 2); bx -= 30;
        Button(new Rect2(bx, y + 6, 26, 22), "_", DiaryMode == 0, () => DiaryMode = DiaryMode == 0 ? 1 : 0); bx -= 112;
        if (M.Forces != null)
        {
            var sup = new Rect2(bx, y + 6, 108, 22);
            IconButton(sup, "War Support", ShowSupport, () => ShowSupport = !ShowSupport, () => { }); Envelope(new Rect2(bx + 2, y + 7, 20, 20), ShowSupport); Text(bx + 26, y + 22, "War Support", 12, Green);
            var war = M.Forces.War; string s = $"{war.Support[1]:0} / UCS ~{Math.Round(war.UcsEstimate / 5) * 5:0}"; Text(bx - Width(s, 12) - 8, y + 22, s, 12, war.Support[1] < 40 ? Deer : Amber);
        }
        if (DiaryMode > 0)
        {
            float bottom = y + h - 6, top = y + head + 2, ly = bottom; int idx = camp.Log.Count - 1 - diaryScroll;
            for (; idx >= 0; idx--)
            {
                var e = camp.Log[idx]; var lines = Wrap(e.Text, 12, w - 148); float eh = lines.Count * lh + 3; if (ly - eh < top) break;
                float ey = ly - eh; DrawLine(new Vector2(x + 8, ly), new Vector2(x + w - 8, ly), new Color(Green, .14f), 1);
                Text(x + 10, ey + 13, camp.Stamp(e.T), 11, Dim);
                for (int i = 0; i < lines.Count; i++) Text(x + 134, ey + 13 + i * lh, lines[i], 12, e.Alarm ? Deer : Ink);
                ly = ey;
            }
            DrawLine(new Vector2(x + 128, top), new Vector2(x + 128, bottom), new Color(Green, .3f), 1);
            if (DiaryMode == 2 && camp.Log.Count > 1)
            {
                float track = bottom - top, frac = diaryScroll / (float)Math.Max(1, camp.Log.Count - 1); DrawRect(new Rect2(x + w - 6, top, 3, track), new Color(Green, .15f)); DrawRect(new Rect2(x + w - 6, bottom - 24 - (track - 24) * frac, 3, 24), new Color(Green, .75f));
            }
        }
        if (ShowSupport && M.Forces != null)
        {
            const float ph = 128, cw = 256; var hist = M.Forces.Hist; float Day(double t) => (float)(t / Campaign.Day);
            var r = new Rect2(x, y - ph - 4, cw * 2 + 28, ph); Panel(r, Green);
            Chart(new Rect2(x + 10, r.Position.Y + 8, cw, ph - 16), "VULFEN WAR SUPPORT", Wolf, 100, (hist.Side[1].Select(s => new Vector2(Day(s.T), s.Support)).ToList(), Wolf, "support"));
            Chart(new Rect2(x + 18 + cw, r.Position.Y + 8, cw, ph - 16), "UCS WAR SUPPORT (ESTIMATED)", Deer, 100, (hist.Side[0].Select(s => new Vector2(Day(s.T), s.Support)).ToList(), Deer, "support"));
        }
    }

    /// <summary>The industry panel's graphs, on a plate of their own beside it.</summary>
    void IndustryCharts(float x, float y, float h)
    {
        var hist = M.Forces!.Hist.Side[1]; const float w = 360; Panel(new Rect2(x, y, w, h), Amber); Plate(x + 10, y + 8, "THE WAR ECONOMY", Amber, 13);
        float ch = (h - 40) / 4 - 6, cy = y + 36; float Day(double t) => (float)(t / Campaign.Day);
        List<Vector2> S(Func<SideSample, float> f) => hist.Select(s => new Vector2(Day(s.T), f(s))).ToList();
        void C(string title, params (IReadOnlyList<Vector2>, Color, string)[] series) { Chart(new Rect2(x + 10, cy, w - 20, ch), title, Dim, null, series); cy += ch + 6; }
        C("OUTPUT, POINTS A WEEK", (S(s => s.Output), Green, "output"));
        C("BOMBS IN THE DUMPS, T", (S(s => s.He), new Color(.75f, .78f, .82f), "HE"), (S(s => s.Inc), new Color(1f, .5f, .15f), "fire"));
        C("FUEL, T", (S(s => s.Fuel), Amber, "fuel"));
        C("AIRCRAFT AND CREWS", (S(s => s.Bombers), Wolf, "bombers"), (S(s => s.Fighters), Green, "fighters"), (S(s => s.BomberCrews), new Color(.8f, .7f, .45f), "crews"));
    }
}
