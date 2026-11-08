using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Firebomber;

public sealed class SandboxFlight { public Craft Kind; public int N; public int Field; }

/// <summary>What the player has asked for: a target, a time, some bombers, and how hard the other side pushes back. The load and the route come next, on the planning table.</summary>
public sealed class SandboxSetup
{
    public Place Target = null!; public bool AimWorks; public int AimKind; public (double x, double y)? AimSite; public double Hour = 8; public int Escorts = 4, Fighters = 24; public bool UcsRaid; public double Flak = 1;
    public readonly List<SandboxFlight> Flights = new();
    public (double x, double y) Aim => AimSite ?? (AimWorks ? CityPlan.Works(Target) : (Target.X, Target.Y));
}

/// <summary>The sandbox's opening screen: pick some bombers and send them where you like.</summary>
public partial class ConsoleUi
{
    const int MaxFlights = 10;

    void SetupPanel(Vector2 size)
    {
        var s = M.Sortie!; var world = M.World; var cities = world.Cities.Where(c => !c.Wolf).OrderByDescending(c => c.Size).ToList();
        const float x = 8, y = 42, w = 600, col = 118, rowH = 30, flightH = 42; int cityRows = (cities.Count + 3) / 4;
        var camp = M.Camp;
        float h = 36 + cityRows * 28 + 2 + (camp != null ? 3 : 6) * rowH + 28 + s.Flights.Count * flightH + 24 + 44 + (camp != null ? 22 : 0);
        var forces = M.Forces;
        // in the campaign a flight can only have what its field has ready, less what other flights from the same field have taken
        int Avail(SandboxFlight f) => forces == null ? 60 : Math.Min(forces.Of(world.Fields[f.Field]).S(f.Kind).Ready - s.Flights.Where(q => q != f && q.Field == f.Field && q.Kind == f.Kind).Sum(q => q.N),
                                                                 forces.Ind.BomberCrews[1] - s.Flights.Where(q => q != f).Sum(q => q.N));      // and only as many as there are crews for
        if (forces != null) foreach (var f in s.Flights) f.N = Math.Clamp(f.N, 0, Math.Max(0, Avail(f)));
        Panel(new Rect2(x, y, w, h), Amber);
        Text(x + 14, y + 24, camp != null ? "RAID ORDERS" : "SANDBOX MISSION", 15, Amber);
        Text(x + 170, y + 24, camp != null ? $"the bombers your fields have ready: {forces!.Ind.BomberCrews[1]} crews to fly them" : "pick some bombers and send them where you like", 12, Dim);
        if (camp != null) Button(new Rect2(x + w - 84, y + 6, 74, 22), "CANCEL", false, M.CancelSetup);
        float ry = y + 34;
        void Label(string t, float at) => Text(x + 14, at + 18, t, 13, Ink);
        // the target
        Label("TARGET", ry);
        for (int i = 0; i < cities.Count; i++)
        {
            var c = cities[i]; var r = new Rect2(x + col + (i % 4) * 118, ry + (i / 4) * 28, 113, 25);
            Button(r, c.Name, s.Target == c, () => M.PickTarget(c));
        }
        ry += cityRows * 28 + 2;
        // one row of mutually exclusive choices
        void Choice<T>(string label, (string t, T v)[] opts, Func<T, bool> on, Action<T> set)
        {
            Label(label, ry); float bx = x + col;
            foreach (var (t, v) in opts) { var vv = v; float bw = Math.Max(56, Width(t, 14) + 18); Button(new Rect2(bx, ry, bw, 25), t, on(v), () => set(vv)); bx += bw + 5; }
            ry += rowH;
        }
        if (forces == null) Choice("AIM AT", new[] { ("CITY CENTRE", false), ("THE WORKS", true) }, v => s.AimWorks == v, v => s.AimWorks = v);
        else
        {
            var sites = forces.Sites; var fld = sites.All.First(q => q.Kind == SiteKind.Field && q.Town == s.Target); var dep = sites.All.FirstOrDefault(q => q.Kind == SiteKind.Depot && q.Town == s.Target); var gun = sites.FlakOf(s.Target).Where(q => q.Guns > 0).OrderBy(q => Math.Abs(q.X - s.Target.X) + Math.Abs(q.Y - s.Target.Y)).FirstOrDefault();
            var opts = new System.Collections.Generic.List<(string, int)> { ("CITY CENTRE", 0), ("THE WORKS", 1), ("AIRFIELD", 2) }; if (dep != null) opts.Add(("DEPOT", 3)); if (gun != null) opts.Add(("FLAK", 4));
            void Aim(int v) { s.AimKind = v; s.AimWorks = v == 1; s.AimSite = v == 2 ? (fld.X, fld.Y) : v == 3 ? (dep!.X, dep.Y) : v == 4 ? (gun!.X, gun.Y) : null; }
            if (s.AimKind == 5 && s.AimSite == null) s.AimKind = 0;
            Choice("AIM AT", opts.ToArray(), v => s.AimKind == v, Aim);
        }
        Choice("TIME", new[] { ("DAY", 8.0), ("DUSK", 18.5), ("NIGHT", 22.0) }, v => s.Hour == v, v => s.Hour = v);
        if (camp != null)
        {
            double day0 = Math.Floor(camp.Now / Campaign.Day) * Campaign.Day, t = day0 + (s.Hour - 6 + 24) % 24 * 3600; while (t < camp.Now + 1800) t += Campaign.Day;
            Text(x + col + 260, ry - rowH + 18, "takes off " + camp.Stamp(t), 12, Dim);
            var (cl, rn) = Weather.At(camp.Seed, s.Target.X, s.Target.Y, t + 2 * 3600); var (ws, from) = Weather.WindAt(camp.Seed, t + 2 * 3600, s.Target.X, s.Target.Y); double dry = Weather.Dryness(camp.Seed, s.Target.X, s.Target.Y, t), moon = Campaign.WeatherAt(camp.Seed, t).moon;
            Text(x + 14, ry + 14, $"Forecast over {s.Target.Name}: cloud {cl * 100:0}%, {Weather.Rainy(rn)}, wind {ws:0} m/s from {from:000}, ground {(dry > .66 ? "very dry" : dry > .33 ? "dry" : "damp")}" + (s.Hour > 18 ? $", moon {moon * 100:0}%" : ""), 12, rn > .3 ? Deer : Ink); ry += 22;
        }
        Choice("F-5 PER FLIGHT", new[] { ("NONE", 0), ("2", 2), ("4", 4), ("8", 8) }, v => s.Escorts == v, v => s.Escorts = v);
        int escorts = s.Flights.Count(q => q.N > 0) * s.Escorts;
        if (forces != null)
        {
            // escorts come from the flights' own fields, and only what is ready there
            escorts = Math.Min(forces.Ind.Pilots[1], s.Flights.Where(q => q.N > 0).GroupBy(q => q.Field).Sum(g => Math.Min(g.Count() * s.Escorts, forces.Of(world.Fields[g.Key]).S(Craft.F5).Ready)));
            Text(x + col + 260, ry - rowH + 18, $"{escorts} F-5 ready to go with them", 12, escorts < s.Flights.Count(q => q.N > 0) * s.Escorts ? Deer : Dim);
        }
        if (camp == null)
        {
            Choice("I-8 PER FIELD", new[] { ("FEW", 12), ("MANY", 24), ("A SWARM", 40) }, v => s.Fighters == v, v => s.Fighters = v);
            Choice("UCS FLAK", new[] { ("NONE", 0.0), ("LIGHT", .5), ("NORMAL", 1.0), ("HEAVY", 2.0) }, v => s.Flak == v, v => s.Flak = v);
        }
        if (camp == null) Choice("UCS RAID", new[] { ("OFF", false), ("ON HJALMAR", true) }, v => s.UcsRaid == v, v => s.UcsRaid = v);
        // the bombers
        Label("BOMBERS", ry - 2);
        float ax = x + w - 214;
        if (s.Flights.Count < MaxFlights)
        {
            SandboxFlight New(Craft k)
            {
                var f = new SandboxFlight { Kind = k, N = k == Craft.V16 ? 36 : 12, Field = s.Flights.Count % world.Fields.Count };
                if (forces != null) { f.Field = Enumerable.Range(0, world.Fields.Count).OrderByDescending(i => { f.Field = i; return Avail(f); }).First(); f.N = Math.Min(k == Craft.V16 ? 24 : 12, Math.Max(0, Avail(f))); }
                return f;
            }
            Button(new Rect2(ax, ry - 2, 100, 25), "ADD V-16", false, () => s.Flights.Add(New(Craft.V16)));
            Button(new Rect2(ax + 105, ry - 2, 100, 25), "ADD U-4", false, () => s.Flights.Add(New(Craft.U4)));
        }
        ry += 28;
        var (aimX, aimY) = s.Aim;
        for (int i = 0; i < s.Flights.Count; i++)
        {
            var f = s.Flights[i]; var d = Design.Of[f.Kind]; var field = world.Fields[f.Field]; var row = new Rect2(x + 8, ry, w - 16, flightH - 4);
            DrawRect(row, new Color(.07f, .12f, .07f, .85f)); DrawRect(new Rect2(row.Position, new Vector2(4, row.Size.Y)), Wolf);
            Sprites.Card(this, f.Kind, row.Position + new Vector2(30, 19), f.Kind == Craft.V16 ? 34 : 28, Colors.White);
            Text(x + 62, ry + 16, $"{(char)('A' + i)}  {Sprites.Specs[f.Kind].Name}", 14, Ink);
            double trip = 2 * Math.Sqrt(Math.Pow(field.X - aimX, 2) + Math.Pow(field.Y - aimY, 2)) / 1000, reach = Loadouts.Of[f.Kind].Max(l => Weight.RangeKm(d, l.Kg));
            bool tooFar = trip * 1.05 > reach;
            Text(x + 62, ry + 32, tooFar ? $"{trip:N0} km there and back: TOO FAR" : forces != null ? $"{trip:N0} km, {Math.Max(0, Avail(f))} ready there" : $"{trip:N0} km there and back, {f.N * d.UsualKg / 1000:0} t usual", 12, tooFar || forces != null && f.N == 0 ? Deer : Dim);
            var ff = f; float cx = x + 300;
            Button(new Rect2(cx, ry + 7, 24, 24), "-", false, () => ff.N = Math.Max(forces != null ? 0 : 6, ff.N - 6));
            Text(cx + 40 - Width(f.N.ToString(), 14) / 2, ry + 24, f.N.ToString(), 14, Amber);
            Button(new Rect2(cx + 56, ry + 7, 24, 24), "+", false, () => ff.N = Math.Min(forces != null ? Math.Max(0, Avail(ff)) : 60, ff.N + 6));
            Button(new Rect2(cx + 86, ry + 7, 138, 24), field.Name.Replace(" field", "").ToUpperInvariant(), false, () =>
            {
                // the next field that has some of this type ready
                for (int k = 1; k <= world.Fields.Count; k++) { int was = ff.Field; ff.Field = (was + 1) % world.Fields.Count; if (forces == null || Avail(ff) > 0) { ff.N = forces == null ? ff.N : Math.Min(Math.Max(ff.N, 6), Avail(ff)); break; } }
            });
            if (s.Flights.Count > 1) Button(new Rect2(x + w - 40, ry + 7, 24, 24), "X", false, () => s.Flights.Remove(ff));
            // the way there, on the map
            DrawDashedLine(M.W2S(field.X, field.Y), M.W2S(aimX, aimY), tooFar ? new Color(Deer, .8f) : new Color(1f, .55f, .05f, .8f), 1.5f, 8);
            ry += flightH;
        }
        int bombers = s.Flights.Sum(f => f.N), esc = escorts;
        Text(x + 14, ry + 16, $"{bombers} bombers in {s.Flights.Count} flight{(s.Flights.Count > 1 ? "s" : "")}, {esc} F-5 escorts, about {s.Flights.Sum(f => f.N * Design.Of[f.Kind].UsualKg) / 1000:0} t on the usual loads", 13, Ink);
        Button(new Rect2(x + 14, ry + 24, w - 28, 32), "TO THE PLANNING TABLE", s.Flights.Any(q => q.N > 0), () => { if (s.Flights.Any(q => q.N > 0)) M.FinishSetup(); });
    }
}
