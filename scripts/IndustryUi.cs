using Godot;
using System;
using System.Linq;

namespace Firebomber;

/// <summary>The industry panel: every Vulfen city's works and what it makes, the dumps, and the crews and their schools. Changing what a works makes is two clicks, choose then RETOOL, because it costs weeks.</summary>
public partial class ConsoleUi
{
    public bool ShowIndustry;
    Works? works; Product pick;
    public void PickProduct(Product p) => pick = p;
    public void OpenWorks(Place city) { var q = M.Forces?.Ind.Of(city); if (q == null || !q.Wolf) return; ShowIndustry = true; ShowBases = ShowAircraft = false; works = q; pick = q.Making; }

    void IndustryPanel(Vector2 size)
    {
        var forces = M.Forces; if (!ShowIndustry || forces == null || M.Camp == null) return;
        var ind = forces.Ind; double now = M.Camp.Now; var fire = M.Fire;
        var list = ind.Works.Where(q => q.Wolf).OrderByDescending(q => q.Points).ToList();
        const float x = 8, y = 42, w = 700, rowH = 21; float h = 70 + list.Count * rowH + 196;
        Panel(new Rect2(x, y, w, h), Amber);
        Text(x + 14, y + 24, "INDUSTRY", 15, Amber); Text(x + 104, y + 24, "each city's works: click one to change what it makes", 12, Dim);
        Button(new Rect2(x + w - 74, y + 6, 64, 22), "CLOSE", false, () => ShowIndustry = false);
        string[] head = { "Works", "plant", "labour", "making", "a week", "" }; float[] cx = { x + 14, x + 120, x + 175, x + 240, x + 390, x + 470 };
        for (int i = 0; i < head.Length; i++) Text(cx[i], y + 50, head[i], 12, Dim);
        float ry = y + 58;
        foreach (var q in list)
        {
            var row = new Rect2(x + 8, ry, w - 16, rowH - 2); bool sel = works == q; double plant = fire.Plant(q.City, true), lab = forces.War.Labour(q.City, fire), per = Industry.PerWeek(q, plant * lab, now);
            DrawRect(row, sel ? new Color(.2f, .34f, .18f, .9f) : new Color(.07f, .12f, .07f, .85f));
            Text(cx[0], ry + 15, q.City.Name, 13, sel ? Amber : Ink);
            Text(cx[1], ry + 15, $"{plant * 100:0}%", 13, plant < .9 ? Deer : Ink);
            Text(cx[2], ry + 15, $"{lab * 100:0}%", 13, lab < .9 ? Deer : Ink);
            Text(cx[3], ry + 15, Industry.Name(q.Making), 13, Ink);
            Text(cx[4], ry + 15, q.Making == Product.Idle ? "-" : Industry.CraftOf(q.Making) != null || q.Making == Product.CivilDefence ? $"{per:0.0}" : $"{per:N0} t", 13, Ink);
            string state = now < q.Retooled ? $"retooling, {Math.Ceiling((q.Retooled - now) / Industry.Day):0} days left" : q.Factor(now) < 1 ? $"working up, {q.Factor(now) * 100:0}%" : plant < .9 ? "bomb damage" : "";
            if (state != "") Text(cx[5], ry + 15, state, 12, now < q.Retooled ? Amber : Dim);
            var qq = q; buttons.Add((row, () => { works = qq; pick = qq.Making; })); Named["works " + q.City.Name] = row;
            ry += rowH;
        }
        // changing a works over
        ry += 8;
        if (works == null) Text(x + 14, ry + 16, "Click a works to change what it makes.", 13, Dim);
        else
        {
            Text(x + 14, ry + 16, $"{works.City.Name}:", 13, Ink); float bx = x + 110;
            foreach (var p in Industry.WolfMakes.Append(Product.Idle))
            {
                var pp = p; string t = p == Product.Idle ? "STOP" : Industry.Short(p); float bw = Math.Max(48, Width(t, 14) + 18);
                Button(new Rect2(bx, ry, bw, 24), t, pick == p, () => pick = pp); Named["make " + t] = new Rect2(bx, ry, bw, 24); bx += bw + 5;
            }
            if (pick != works.Making)
            {
                double d = Industry.RetoolDays(works.Making, pick); var ww = works; var pk = pick;
                Button(new Rect2(bx + 8, ry, 90, 24), "RETOOL", true, () => { ind.Order(ww, pk, M.Camp.Now); M.Camp.Note(pk == Product.Idle ? $"The {ww.City.Name} works stops." : $"The {ww.City.Name} works retools for {Industry.Name(pk)}: {d:0} days."); });
                Named["RETOOL"] = new Rect2(bx + 8, ry, 90, 24);
                Text(x + 110, ry + 42, pick == Product.Idle ? "It will make nothing until set to something else." : $"{d:0} days making nothing, then two weeks working up to full output.", 12, Amber);
            }
        }
        // the dumps
        ry += 60;
        double Rate(Product p) => list.Where(q => q.Making == p).Sum(q => Industry.PerWeek(q, fire.Plant(q.City, true) * forces.War.Labour(q.City, fire), now));
        Text(x + 14, ry, "DUMPS", 13, Amber);
        Text(x + 110, ry, $"HE {ind.He[1]:N0} t (+{Rate(Product.HE):N0} a week)     incendiary {ind.Inc[1]:N0} t (+{Rate(Product.Incendiary):N0})     fuel {ind.Fuel[1]:N0} t (+{Industry.FuelWeek:N0})", 13, Ink);
        Text(x + 110, ry + 18, $"Aircraft a week: {Rate(Product.V16):0.0} V-16, {Rate(Product.U4):0.0} U-4, {Rate(Product.F5):0.0} F-5.   Civil defence {forces.War.Defence(true) * 100:0}% (+{Rate(Product.CivilDefence):0} points a week): {FireSim.Shelter(true, forces.War.Defence(true)) * 100:0}% in shelters", 12, Dim);
        // the crews
        ry += 46;
        var (bt, bn) = ind.InTraining(true, true); var (pt, pn) = ind.InTraining(true, false);
        Text(x + 14, ry, "CREWS", 13, Amber);
        Text(x + 110, ry, $"bomber crews {ind.BomberCrews[1]}  ({bt} at school, next out {(double.IsNaN(bn) ? "-" : M.Camp.Stamp(bn))})", 13, Ink);
        Text(x + 110, ry + 18, $"fighter pilots {ind.Pilots[1]}  ({pt} at school, next out {(double.IsNaN(pn) ? "-" : M.Camp.Stamp(pn))})", 13, Ink);
        int bombersNeed = Forces.WolfTypes.Where(k => !Design.Of[k].IsFighter).Sum(k => forces.All(true, k)), fightersNeed = forces.All(true, Craft.F5);
        Text(x + 470, ry, bombersNeed > ind.BomberCrews[1] ? $"{bombersNeed - ind.BomberCrews[1]} bombers without a crew" : "", 12, Deer);
        Text(x + 470, ry + 18, fightersNeed > ind.Pilots[1] ? $"{fightersNeed - ind.Pilots[1]} fighters without a pilot" : "", 12, Deer);
        ry += 30;
        Text(x + 110, ry + 17, $"Schools take {Industry.SchoolPlaces} a week:  bomber crews", 12, Dim); float sx = x + 110 + Width($"Schools take {Industry.SchoolPlaces} a week:  bomber crews", 12) + 10;
        Button(new Rect2(sx, ry, 24, 24), "-", false, () => ind.BomberIntake = Math.Max(0, ind.BomberIntake - 2)); Named["intake -"] = new Rect2(sx, ry, 24, 24);
        Text(sx + 30, ry + 17, ind.BomberIntake.ToString(), 14, Amber);
        Button(new Rect2(sx + 56, ry, 24, 24), "+", false, () => ind.BomberIntake = Math.Min(Industry.SchoolPlaces, ind.BomberIntake + 2)); Named["intake +"] = new Rect2(sx + 56, ry, 24, 24);
        Text(sx + 90, ry + 17, $"fighter pilots {ind.PilotIntake}   (courses {Industry.CourseWeeks(true, true)} and {Industry.CourseWeeks(true, false)} weeks)", 12, Dim);
        IndustryCharts(x + w + 8, y, h);
    }
}
