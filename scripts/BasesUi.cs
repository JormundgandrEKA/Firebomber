using Godot;
using System;
using System.Linq;

namespace Firebomber;

/// <summary>The bases panel: every Vulfen field, what is ready on it and what is coming back to it, and ferrying aircraft from one field to another.</summary>
public partial class ConsoleUi
{
    public bool ShowBases;
    Base? from, seen; Craft ferryKind = Craft.F5; int ferryN = 6, ferryTo; string ferryMsg = ""; bool theirs;
    /// <summary>Opens the panel on a field clicked on the map.</summary>
    public void OpenBase(Base b) { ShowBases = true; ShowIndustry = ShowAircraft = false; theirs = !b.Wolf; if (b.Wolf) { from = b; ferryMsg = ""; } else seen = b; }

    /// <summary>The UCS fields, as intelligence has them: rough numbers. (Truth for now, rounded; reconnaissance will make them old and uncertain.)</summary>
    void TheirBases(Forces forces)
    {
        var bases = forces.Side(false).OrderByDescending(b => b.Town.Y).ToList();
        const float x = 8, y = 42, w = 640, rowH = 21; float h = 70 + bases.Count * rowH + 14;
        Panel(new Rect2(x, y, w, h), Deer);
        Text(x + 14, y + 24, "UCS FIELDS", 15, Deer); Text(x + 120, y + 24, "estimates, nearest first", 12, Dim);
        Button(new Rect2(x + w - 74, y + 6, 64, 22), "CLOSE", false, () => ShowBases = false);
        Button(new Rect2(x + w - 158, y + 6, 78, 22), "OURS", false, () => theirs = false);
        string[] head = { "Field", "fighters", "of them I-7", "bombers", "of them SB-3" }; float[] cx = { x + 14, x + 170, x + 270, x + 390, x + 490 };
        for (int i = 0; i < head.Length; i++) Text(cx[i], y + 50, head[i], 12, Dim);
        float ry = y + 58;
        foreach (var b in bases)
        {
            var row = new Rect2(x + 8, ry, w - 16, rowH - 2); bool sel = seen == b;
            DrawRect(row, sel ? new Color(.34f, .16f, .12f, .9f) : new Color(.12f, .07f, .06f, .85f));
            int fi = b.S(Craft.I8).Ready + b.S(Craft.I7).Ready, bo = b.S(Craft.TB6).Ready + b.S(Craft.SB3).Ready;
            string E(int n) => n == 0 ? "-" : $"~{Forces.About(n)}";
            Text(cx[0], ry + 15, b.Name, 13, sel ? Amber : Ink); Text(cx[1], ry + 15, E(fi), 13, Ink); Text(cx[2], ry + 15, E(b.S(Craft.I7).Ready), 12, Dim); Text(cx[3], ry + 15, E(bo), 13, Ink); Text(cx[4], ry + 15, E(b.S(Craft.SB3).Ready), 12, Dim);
            var bb = b; buttons.Add((row, () => { seen = bb; M.SelectCity(bb.Town, true); })); ry += rowH;
        }
    }

    void BasesPanel(Vector2 size)
    {
        var forces = M.Forces; if (!ShowBases || forces == null || M.Camp == null) return;
        if (theirs) { TheirBases(forces); return; }
        var bases = forces.Side(true).ToList(); var types = Forces.WolfTypes;
        const float x = 8, y = 42, w = 640, rowH = 21; float h = 70 + bases.Count * rowH + 70;
        Panel(new Rect2(x, y, w, h), Amber);
        Text(x + 14, y + 24, "BASES", 15, Amber); Text(x + 80, y + 24, "ready, and (in brackets) back from servicing, repair or ferrying", 12, Dim);
        Button(new Rect2(x + w - 74, y + 6, 64, 22), "CLOSE", false, () => ShowBases = false);
        Button(new Rect2(x + w - 158, y + 6, 78, 22), "UCS", false, () => theirs = true);
        float cx0 = x + 150, cw = 110; Text(x + 14, y + 50, "Field", 12, Dim);
        for (int t = 0; t < types.Length; t++) { Text(cx0 + t * cw, y + 50, Sprites.Specs[types[t]].Name, 12, Dim); HoverIf(new Rect2(cx0 + t * cw, y + 38, cw, 16), types[t]); }
        Text(cx0 + types.Length * cw, y + 50, "room", 12, Dim);
        float ry = y + 58;
        foreach (var b in bases)
        {
            var row = new Rect2(x + 8, ry, w - 16, rowH - 2); bool sel = from == b;
            DrawRect(row, sel ? new Color(.2f, .34f, .18f, .9f) : new Color(.07f, .12f, .07f, .85f));
            Text(x + 14, ry + 15, b.Name, 13, sel ? Amber : Ink);
            for (int t = 0; t < types.Length; t++)
            {
                var s = b.S(types[t]); int c = s.ComingN; if (s.Ready == 0 && c == 0) { Text(cx0 + t * cw, ry + 15, "-", 13, Dim); continue; }
                Text(cx0 + t * cw, ry + 15, s.Ready.ToString(), 13, Ink); if (c > 0) Text(cx0 + t * cw + Width(s.Ready.ToString(), 13) + 6, ry + 15, $"({c})", 12, Dim);
            }
            Text(cx0 + types.Length * cw, ry + 15, $"{b.Capacity - b.Total}", 12, b.Capacity - b.Total < 12 ? Deer : Dim);
            var bb = b; buttons.Add((row, () => { from = bb; ferryMsg = ""; })); Named["base " + b.Name] = row;
            ry += rowH;
        }
        // ferrying: from the chosen field, so many of a type, to another field
        ry += 10; Text(x + 14, ry + 16, from == null ? "Click a field to send aircraft from it." : $"From {from.Name}:", 13, from == null ? Dim : Ink);
        if (from != null)
        {
            var dests = bases.Where(q => q != from).ToList(); ferryTo = Math.Clamp(ferryTo, 0, dests.Count - 1); var to = dests[ferryTo];
            float bx = x + 150;
            Button(new Rect2(bx, ry, 96, 24), Sprites.Specs[ferryKind].Name.Split(' ')[0], false, () => ferryKind = types[(Array.IndexOf(types, ferryKind) + 1) % types.Length]); bx += 102;
            Button(new Rect2(bx, ry, 24, 24), "-", false, () => ferryN = Math.Max(1, ferryN - 6)); Text(bx + 30, ry + 17, ferryN.ToString(), 14, Amber); bx += 56;
            Button(new Rect2(bx, ry, 24, 24), "+", false, () => ferryN += 6); bx += 34;
            Button(new Rect2(bx, ry, 150, 24), "TO " + to.Name.ToUpperInvariant(), false, () => ferryTo = (ferryTo + 1) % dests.Count); bx += 156;
            Button(new Rect2(bx, ry, 70, 24), "SEND", true, () =>
            {
                var why = M.Forces!.Ferry(from, to, ferryKind, ferryN, M.Camp!.Now);
                ferryMsg = why ?? $"{ferryN} {Sprites.Specs[ferryKind].Name.Split(' ')[0]} on their way to {to.Name}";
                if (why == null) M.Camp.Note($"Ferrying {ferryN} {Sprites.Specs[ferryKind].Name.Split(' ')[0]} from {from.Name} to {to.Name}.");
            });
            if (ferryMsg != "") Text(x + 14, ry + 44, ferryMsg, 12, ferryMsg.Contains("way") ? Green : Deer);
        }
    }
}
