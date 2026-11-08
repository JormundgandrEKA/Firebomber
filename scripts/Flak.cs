using System;
using System.Collections.Generic;
using System.Linq;

namespace Firebomber;

/// <summary>A heavy battery: a ring of guns and one predictor that lays them all.</summary>
public sealed class Battery
{
    public Site? Site; public double X, Y, NextSalvo, LastFire = -1e9, TrackSince; public int Guns; public bool Wolf; public Place City = null!; public Formation? On; public int Shells;
}
/// <summary>A shell bursting, for the view: where, and when.</summary>
public struct Burst { public double X, Y, Z, T; public bool Wolf; }

/// <summary>Heavy flak round the cities. Each battery's predictor tracks one box and fires the whole battery at where it expects a bomber to be when the shells arrive, ten or fifteen seconds later.
/// The guess gets better the longer it holds the same box, and worse with height, speed and darkness. The shells burst where they were sent, whatever the bomber did meanwhile, and their splinters
/// go through the same hull as cannon shells. Thousands of shells for each bomber brought down, but many more holed, and a holed bomber is a straggler for the fighters.
/// UCS guns are many and crude, laid by eye and searchlight at night; Vulfen guns are fewer, reach higher, and are laid by radar. All numbers are first suggestions.</summary>
public sealed class FlakSim
{
    public readonly List<Battery> Batteries = new(); public readonly List<Burst> Bursts = new();
    /// <summary>Shells fired, splinters that hit, and bombers holed at least once, by side of the bombers (0 UCS, 1 Vulfen).</summary>
    public readonly int[] Shells = new int[2], Splinters = new int[2]; public readonly HashSet<Bomber>[] Holed = { new(), new() };
    readonly Air air; readonly Random rng; readonly PriorityQueue<(double x, double y, double z, bool wolf), double> flying = new();

    const double SalvoSecs = 4, ShellV = 600, SlantMax = 10500, Splinter = 25, SplinterN = 40, GunSpread = 35;
    public static readonly Weapon Shell = new("heavy flak splinter", 88, 0, 8, .2, 14);
    public static double Ceiling(bool wolf) => wolf ? 9000 : 8000;

    /// <param name="ucs">How many guns the UCS has round its cities, against the usual: 0 none, 2 twice as many.</param>
    /// <summary>Batteries that have lost guns to bombs fire with what they have left.</summary>
    public void Sync() { foreach (var b in Batteries) if (b.Site != null) b.Guns = b.Site.Guns; }
    public FlakSim(World w, Air a, double ucs = 1, double wolf = 1, int seed = 11, Func<Place, double>? moved = null, Sites? sites = null)
    {
        air = a; rng = new Random(seed);
        foreach (var c in w.Cities)
        {
            // guns go where there is something to defend: a big city gets far more than a town
            double strength = c.Wolf ? wolf : ucs * (moved?.Invoke(c) ?? 1); int n = (int)Math.Round(strength * c.Size * c.Size / (c.Wolf ? 10.0 : 4.0)); if (strength > 0) n = Math.Max(1, n);
            if (sites != null) { foreach (var s in sites.FlakOf(c).Take(n)) if (s.Guns > 0) Batteries.Add(new Battery { X = s.X, Y = s.Y, Guns = s.Guns, Wolf = c.Wolf, City = c, Site = s, NextSalvo = rng.NextDouble() * SalvoSecs }); continue; }
            for (int k = 0; k < n; k++)
            {
                double x = c.X, y = c.Y;
                for (int tries = 0; tries < 10; tries++)
                {
                    double ang = Math.Tau * (k + .6 * rng.NextDouble()) / n + tries * .37, r = c.R * (.4 + .8 * rng.NextDouble()) + 3000 * rng.NextDouble();
                    x = c.X + Math.Cos(ang) * r; y = c.Y + Math.Sin(ang) * r; if (!w.IsWater(x, y, 40)) break;
                }
                Batteries.Add(new Battery { X = x, Y = y, Guns = c.Wolf ? 4 : 6, Wolf = c.Wolf, City = c, NextSalvo = rng.NextDouble() * SalvoSecs });
            }
        }
    }

    static double Dist(double ax, double ay, double bx, double by) => Math.Sqrt((ax - bx) * (ax - bx) + (ay - by) * (ay - by));
    double Gauss(double sd) { double u1 = 1 - rng.NextDouble(), u2 = rng.NextDouble(); return sd * Math.Sqrt(-2 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2); }
    int Poisson(double mean) { if (mean <= 0) return 0; double l = Math.Exp(-Math.Min(mean, 60)), p = 1; int k = 0; do { k++; p *= rng.NextDouble(); } while (p > l); return k - 1; }
    static bool Up(Formation f) => f.Phase is Phase.Outbound or Phase.Egress or Phase.Returning && f.Ac.Count > 0;

    /// <summary>One step of the air: batteries that are ready fire, and shells that have arrived burst.</summary>
    public void Step(double T)
    {
        foreach (var b in Batteries) if (T >= b.NextSalvo) Fire(b, T);
        while (flying.TryPeek(out var s, out double when) && when <= T) { flying.Dequeue(); Burst(s.x, s.y, s.z, s.wolf, T); }
        int old = 0; while (old < Bursts.Count && Bursts[old].T < T - 15) old++; if (old > 0) Bursts.RemoveRange(0, old);
    }

    void Fire(Battery b, double T)
    {
        b.NextSalvo = T + SalvoSecs * (.9 + .2 * rng.NextDouble());
        double roof = Ceiling(b.Wolf);
        bool InReach(Bomber a) => a.Alive && a.Z < roof && a.Z > 300 && Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y) + a.Z * a.Z) < SlantMax;
        // hold the box it has been tracking while it can; otherwise the nearest one in reach
        if (b.On == null || !Up(b.On) || !b.On.Ac.Any(InReach))
        {
            b.On = null; double best = SlantMax + 4000;
            foreach (var f in air.Formations)
            {
                if (f.Wolf == b.Wolf || !Up(f)) continue; double d = Dist(f.Lead.X, f.Lead.Y, b.X, b.Y); if (d >= best) continue;
                if (f.Ac.Any(InReach)) { best = d; b.On = f; }
            }
            if (b.On == null) return;
            b.TrackSince = T;
        }
        var choice = b.On.Ac.Where(InReach).ToList(); var t = choice[rng.Next(choice.Count)];
        double slant = Math.Sqrt((t.X - b.X) * (t.X - b.X) + (t.Y - b.Y) * (t.Y - b.Y) + t.Z * t.Z), tof = slant / ShellV * (1 + slant / 40000);
        // the predictor's guess: straight on at the speed it has measured. Its error is shared by every gun in the salvo
        double crude = b.Wolf ? 1 : 1.3, dark = air.MurkAt(t.X, t.Y) * (b.Wolf ? .5 : 1.5), high = 1 + 3 * Math.Pow(Math.Max(0, t.Z / roof - .6), 2) * 2.5, track = 1 + 1.5 * Math.Exp(-(T - b.TrackSince) / 20);
        if (b.Guns <= 0) { b.NextSalvo = T + SalvoSecs; return; }
        bool cued = b.Wolf && (air.RadarCovers?.Invoke(b.X, b.Y) ?? true);
        var skyGun = air.Wx(t.X, t.Y); double blindGuns = 1 + (cued ? .4 : 3.5) * skyGun.cloud * skyGun.cloud + (cued ? .3 : 2) * skyGun.rain;      // a gun that must see its target loses most of its aim in cloud; the Vulfen predictors are cued by radar
        double sd = ((20 + .012 * slant) * crude * track + t.V * tof * .015) * (1 + dark) * high * blindGuns;      // near its ceiling a gun's shell is slow and its fuse a guess
        double px = t.X + Math.Cos(t.Th) * t.V * tof + Gauss(sd), py = t.Y + Math.Sin(t.Th) * t.V * tof + Gauss(sd), pz = t.Z + Gauss(sd * 1.6);
        for (int g = 0; g < b.Guns; g++) flying.Enqueue((px + Gauss(GunSpread), py + Gauss(GunSpread), pz + Gauss(GunSpread * 1.4), b.Wolf), T + tof + rng.NextDouble() * .6);
        b.LastFire = T; b.Shells += b.Guns; Shells[b.Wolf ? 0 : 1] += b.Guns;
    }

    void Burst(double x, double y, double z, bool gunsWolf, double T)
    {
        Bursts.Add(new Burst { X = x, Y = y, Z = z, T = T, Wolf = gunsWolf });
        const double Reach = Splinter * 3;
        foreach (var f in air.Formations)
        {
            if (f.Wolf == gunsWolf || !Up(f) || Dist(f.Lead.X, f.Lead.Y, x, y) > 4000) continue;
            double size = Math.Pow(Sprites.Specs[f.Kind].Span / 40, 1.2);      // a small aircraft catches fewer splinters
            foreach (var a in f.Ac.ToList())
            {
                double dx = a.X - x, dy = a.Y - y, dz = a.Z - z; if (Math.Abs(dx) > Reach || Math.Abs(dy) > Reach || Math.Abs(dz) > Reach) continue;
                double d = Math.Sqrt(dx * dx + dy * dy + dz * dz); if (d > Reach) continue;
                int hits = Poisson(SplinterN * size * Math.Exp(-(d / Splinter) * (d / Splinter))) + (d < 5 ? 40 : 0);
                if (hits == 0) continue;
                int side = f.Wolf ? 1 : 0; Splinters[side] += hits; Holed[side].Add(a);
                air.Hit(a, Shell, hits, 1, "flak");
            }
        }
    }
}
