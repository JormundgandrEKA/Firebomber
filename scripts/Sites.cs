using System;
using System.Collections.Generic;
using System.Linq;

namespace Firebomber;

public enum SiteKind { Field, Depot, Flak, Radar }
public enum PartKind { Runway, Hangar, Tank, Bombs, Gun, Mast, Shed }

/// <summary>One thing at a site that a bomb can break.</summary>
public sealed class Part { public PartKind Kind; public double X, Y, BackAt; public float W, D; public bool Down; }

/// <summary>A military target on the ground: an airfield, a depot, a flak battery or a radar station, each made of parts laid out on clear land.</summary>
public sealed class Site
{
    public SiteKind Kind; public bool Wolf; public string Name = ""; public Place Town = null!; public Place? Field; public double X, Y, Ang; public readonly List<Part> Parts = new(); public int Index;
    public int RunwayDown => Parts.Count(p => p.Kind == PartKind.Runway && p.Down);
    public bool Closed => Kind == SiteKind.Field && RunwayDown >= 2;
    public int Guns => Parts.Count(p => p.Kind == PartKind.Gun && !p.Down);
    public bool Working => Kind != SiteKind.Radar || Parts.All(p => !p.Down);
    public int Lost => Parts.Count(p => p.Down);
    public double Condition => 1 - Lost / (double)Math.Max(1, Parts.Count);
    public double ReadyAt => Parts.Where(p => p.Down).Select(p => p.BackAt).DefaultIfEmpty(0).Max();
}

/// <summary>Everything bombs can break that is not a city (roadmap two, stage 4).
/// A cratered runway closes a field until it is filled in. Bombs among the dispersals destroy aircraft on the ground. Fuel and bombs explode, and take their neighbours with them.
/// Each side's stocks are held in its depots in equal shares, so a depot destroyed is that share gone. Flak batteries keep their places between raids and lose guns. The Vulfen radar chain is real stations: lose one and part of the coast goes unwatched. All numbers are first suggestions.</summary>
public sealed class Sites
{
    public readonly List<Site> All = new();
    readonly Dictionary<Place, Site> fieldSite = new(); readonly Dictionary<Place, List<Site>> flakAt = new();
    public const double RadarKm = 220, Day = 86400;
    /// <summary>What the bombs of this operation did, for the diary.</summary>
    public readonly Dictionary<Site, (int parts, int craters, int aircraft, double tonnes)> Report = new();
    public bool FlakChanged;

    public Site Of(Place field) => fieldSite[field];
    public bool Closed(Place field) => fieldSite.TryGetValue(field, out var s) && s.Closed;
    public Site? Named(string name) => All.FirstOrDefault(s => s.Name == name);
    public IEnumerable<Site> FlakOf(Place city) => flakAt.TryGetValue(city, out var l) ? l : Enumerable.Empty<Site>();
    public int DepotCount(bool wolf) => Math.Max(1, All.Count(s => s.Kind == SiteKind.Depot && s.Wolf == wolf));
    public bool RadarCovers(double x, double y) => All.Any(s => s.Kind == SiteKind.Radar && s.Working && Math.Abs(s.X - x) < RadarKm * 1000 && (s.X - x) * (s.X - x) + (s.Y - y) * (s.Y - y) < RadarKm * RadarKm * 1e6);

    public Sites(World w)
    {
        void Add(Site s) { s.Index = All.Count; All.Add(s); }
        (double x, double y) Local(Site s, double along, double side) { double c = Math.Cos(s.Ang), n = Math.Sin(s.Ang); return (s.X + along * c - side * n, s.Y + along * n + side * c); }
        void P(Site s, PartKind k, double along, double side, float wd, float dp) { var (x, y) = Local(s, along, side); s.Parts.Add(new Part { Kind = k, X = x, Y = y, W = wd, D = dp }); }
        foreach (var f in w.Fields.Concat(w.EnemyFields))
        {
            var s = new Site { Kind = SiteKind.Field, Wolf = f.Wolf, Name = f.Name, Town = f.Town!, Field = f, X = f.X, Y = f.Y, Ang = World.RunwayAng };
            for (int i = 0; i < 8; i++) P(s, PartKind.Runway, -700 + 200 * i, 0, 200, 60);
            for (int i = 0; i < 4; i++) P(s, PartKind.Hangar, -450 + 300 * i, 330, 90, 70);
            P(s, PartKind.Tank, -520, -320, 40, 40); P(s, PartKind.Tank, -450, -320, 40, 40); P(s, PartKind.Bombs, 480, -360, 50, 30); P(s, PartKind.Bombs, 560, -360, 50, 30);
            Add(s); fieldSite[f] = s;
        }
        var cities = w.Cities.ToList();
        // depots: for each side, outside its bigger cities
        foreach (bool wolf in new[] { true, false })
            foreach (var c in cities.Where(q => q.Wolf == wolf).OrderByDescending(q => q.Size).Take(wolf ? 5 : 8))
            {
                double a = N.Hash(c.Seed, 11, 502) * Math.Tau; var (x, y) = w.ClearSpot(c.X, c.Y, a, c.R, 45000, 500);
                var s = new Site { Kind = SiteKind.Depot, Wolf = wolf, Name = c.Name + " depot", Town = c, X = x, Y = y, Ang = N.Hash(c.Seed, 12, 502) * Math.PI };
                for (int i = 0; i < 6; i++) P(s, i < 3 ? PartKind.Bombs : PartKind.Tank, -145 + 145 * (i % 3), i < 3 ? -130 : 130, 110, 50);
                Add(s);
            }
        // the guns: twice as many sites as a city has batteries to begin with, so that guns moved to it have somewhere to go
        foreach (var c in cities)
        {
            int n = Math.Max(1, (int)Math.Round(c.Size * c.Size / (c.Wolf ? 10.0 : 4.0))) * 2, guns = c.Wolf ? 4 : 6; var list = new List<Site>(); var rng = new Random(c.Seed + 77);
            for (int k = 0; k < n; k++)
            {
                double x = c.X, y = c.Y;
                for (int tries = 0; tries < 10; tries++)
                {
                    double ang = Math.Tau * (k + .6 * rng.NextDouble()) / n + tries * .37, r = c.R * (.4 + .8 * rng.NextDouble()) + 3000 * rng.NextDouble();
                    x = c.X + Math.Cos(ang) * r; y = c.Y + Math.Sin(ang) * r; if (!w.IsWater(x, y, 40)) break;
                }
                var s = new Site { Kind = SiteKind.Flak, Wolf = c.Wolf, Name = $"{c.Name} flak {k + 1}", Town = c, X = x, Y = y, Ang = rng.NextDouble() * Math.PI };
                for (int g = 0; g < guns; g++) { double a = g * Math.Tau / guns; P(s, PartKind.Gun, Math.Cos(a) * 45, Math.Sin(a) * 45, 12, 12); }
                Add(s); list.Add(s);
            }
            flakAt[c] = list;
        }
        // the radar chain: the Vulfen coast, on clear ground on the seaward side of each town
        foreach (var name in new[] { "Orsvik", "Fjorgard", "Kaldvik", "Skarn", "Hjalmar", "Vess", "Pohjaa" })
        {
            var c = w.City(name); var (x, y) = w.ClearSpot(c.X, c.Y, -Math.PI / 2, c.R, 40000, 150);
            var s = new Site { Kind = SiteKind.Radar, Wolf = true, Name = c.Name + " radar", Town = c, X = x, Y = y, Ang = 0 };
            P(s, PartKind.Mast, -40, 0, 14, 14); P(s, PartKind.Mast, 40, 0, 14, 14); P(s, PartKind.Shed, 0, -60, 30, 16); Add(s);
        }
    }

    (int parts, int craters, int aircraft, double tonnes) Rep(Site s) => Report.TryGetValue(s, out var r) ? r : default;

    /// <summary>A part breaks: when it comes back, what goes with it, and what it does to the stocks.</summary>
    void Break(Site s, Part p, double now, Forces f, Random rng)
    {
        if (p.Down) return; p.Down = true; var r = Rep(s); r.parts++;
        p.BackAt = now + Day * p.Kind switch { PartKind.Runway => .7 + .8 * rng.NextDouble(), PartKind.Hangar => 18 + 14 * rng.NextDouble(), PartKind.Gun => 8 + 10 * rng.NextDouble(), PartKind.Mast or PartKind.Shed => 14 + 10 * rng.NextDouble(), _ => 25 + 20 * rng.NextDouble() };
        if (p.Kind == PartKind.Runway) r.craters++;
        if (p.Kind is PartKind.Gun) FlakChanged = true;
        int side = s.Wolf ? 1 : 0; var ind = f.Ind;
        if (p.Kind is PartKind.Tank or PartKind.Bombs)
        {
            // a depot holds its share of the nation's stock in its sheds; a field only what it has in hand
            double frac = s.Kind == SiteKind.Depot ? 1.0 / f.Sites.DepotCount(s.Wolf) / 3 : 0;
            if (p.Kind == PartKind.Tank) { double t = s.Kind == SiteKind.Depot ? ind.Fuel[side] * frac : Math.Min(40, ind.Fuel[side]); ind.Fuel[side] -= t; r.tonnes += t; }
            else { double he = s.Kind == SiteKind.Depot ? ind.He[side] * frac : Math.Min(20, ind.He[side]), inc = s.Kind == SiteKind.Depot ? ind.Inc[side] * frac : Math.Min(10, ind.Inc[side]); ind.He[side] -= he; ind.Inc[side] -= inc; r.tonnes += he + inc; }
        }
        Report[s] = r;
        // fuel and bombs go up, and take what is near
        if (p.Kind is PartKind.Tank or PartKind.Bombs)
        {
            double reach = p.Kind == PartKind.Bombs ? 170 : 110;
            foreach (var q in s.Parts) if (!q.Down && q != p && q.Kind != PartKind.Runway && Math.Abs(q.X - p.X) < reach && Math.Abs(q.Y - p.Y) < reach && (q.X - p.X) * (q.X - p.X) + (q.Y - p.Y) * (q.Y - p.Y) < reach * reach) Break(s, q, now, f, rng);
        }
    }

    /// <summary>A bomb lands. Quick for those nowhere near anything.</summary>
    public void Hit(Impact h, double now, Forces f, Random rng)
    {
        if (h.Water) return; double scale = Math.Cbrt(Math.Max(h.Kg, 50) / 250.0); bool he = !h.Incendiary;
        foreach (var s in All)
        {
            double dx0 = h.X - s.X, dy0 = h.Y - s.Y; if (Math.Abs(dx0) > 1400 || Math.Abs(dy0) > 1400) continue;
            foreach (var p in s.Parts)
            {
                if (p.Down) continue; double d = Math.Sqrt((h.X - p.X) * (h.X - p.X) + (h.Y - p.Y) * (h.Y - p.Y)), half = Math.Max(p.W, p.D) / 2;
                double reach = he ? half + 14 * scale : half + 40;      // a cluster of fire bombs spreads wider and does less
                if (d > reach) continue;
                double chance = he ? (p.Kind == PartKind.Runway ? .85 : p.Kind == PartKind.Gun ? .75 : .9) : p.Kind is PartKind.Tank or PartKind.Bombs or PartKind.Hangar ? .28 : p.Kind == PartKind.Shed ? .2 : 0;
                if (rng.NextDouble() < chance) Break(s, p, now, f, rng);
            }
            if (s.Kind == SiteKind.Field && dx0 * dx0 + dy0 * dy0 < 520 * 520 && rng.NextDouble() < (he ? .15 : .05)) Parked(s, f, now, rng, 1);
        }
    }
    /// <summary>Aircraft standing on the field caught by a bomb: one destroyed, by weight of what is there.</summary>
    void Parked(Site s, Forces f, double now, Random rng, int n)
    {
        var b = f.Of(s.Field!); var kinds = b.Of.Where(kv => kv.Value.Ready > 0).ToList(); if (kinds.Count == 0) return;
        int total = kinds.Sum(kv => kv.Value.Ready), pick = rng.Next(total);
        foreach (var kv in kinds) { pick -= kv.Value.Ready; if (pick < 0) { kv.Value.Ready -= n; var r = Rep(s); r.aircraft += n; Report[s] = r; return; } }
    }

    /// <summary>The daily tick: what is due is mended.</summary>
    public void Daily(double now) { foreach (var s in All) foreach (var p in s.Parts) if (p.Down && p.BackAt <= now) p.Down = false; FlakChanged = true; }

    /// <summary>The diary's account of this operation, ours and theirs, and clears it.</summary>
    public List<(string text, bool alarm)> Account(double now)
    {
        var o = new List<(string, bool)>();
        foreach (var wolf in new[] { true, false })
        {
            var hit = Report.Where(kv => kv.Key.Wolf == wolf && kv.Value.parts > 0).OrderBy(kv => kv.Key.Kind).ToList(); if (hit.Count == 0) continue;
            string What(Site s, (int parts, int craters, int aircraft, double tonnes) r)
            {
                var bits = new List<string>();
                if (s.Closed) { double days = Math.Ceiling((s.Parts.Where(p => p.Kind == PartKind.Runway && p.Down).Max(p => p.BackAt) - now) / Day); bits.Add($"runway closed, {days:0} day{(days > 1 ? "s" : "")} to fill"); }
                else if (r.craters > 0) bits.Add("runway cratered");
                if (r.aircraft > 0) bits.Add($"{r.aircraft} aircraft destroyed");
                if (r.tonnes > 1) bits.Add($"{r.tonnes:N0} t of stores gone");
                if (s.Kind == SiteKind.Flak) bits.Add($"{s.Guns} guns left");
                if (s.Kind == SiteKind.Radar && !s.Working) bits.Add("off the air");
                if (bits.Count == 0) bits.Add($"{r.parts} hit");
                return $"{s.Name}: {string.Join(", ", bits)}";
            }
            var named = hit.Where(kv => kv.Key.Kind != SiteKind.Flak).Select(kv => What(kv.Key, kv.Value)).ToList(); int flak = hit.Where(kv => kv.Key.Kind == SiteKind.Flak).Sum(kv => kv.Value.parts);
            if (flak > 0) named.Add($"{flak} flak guns destroyed");
            o.Add(((wolf ? "Bomb damage: " : "Our bombs: ") + string.Join("\n", named), wolf));
        }
        Report.Clear(); return o;
    }
}
