using System;
using System.Collections.Generic;
using System.Linq;

namespace Firebomber;

/// <summary>What a works group can be set to make.</summary>
public enum Product { Idle, V16, U4, F5, HE, Incendiary, TB6, SB3, I8, I7, CivilDefence }

/// <summary>One city's works: how much it can make in a week with its plant whole, what it is making, how far along the next one is, and when its retooling is done.</summary>
public sealed class Works
{
    public Place City = null!; public bool Wolf; public double Points;
    public Product Making; public double Progress, Retooled = -1e18;
    /// <summary>How much of its capacity it is giving: none while retooling, then rising over a fortnight while the lines learn the new type.</summary>
    public double Factor(double now) => now < Retooled ? 0 : now < Retooled + Industry.Ramp ? .3 + .7 * (now - Retooled) / Industry.Ramp : 1;
}

/// <summary>A class at a flying school: how many, which kind, and when they pass out.</summary>
public sealed class Course { public double T; public bool Wolf, Bomber; public int N; }

/// <summary>Production and people (roadmap part 3).
/// Every city has a works group. Its size is the city's factory floor; bomb damage to that floor cuts it until repaired. The player sets what each Vulfen group makes; changing type means weeks of retooling. The UCS has more capacity, makes cruder aircraft in a fixed mix, and does not count its bombs or fuel yet (that waits for its planner, part 5).
/// Crews: a pool of bomber crews and of fighter pilots for each side. A raid or a scramble can only fly as many as there are crews; losses come out of the pool, less those who get back; the schools turn out new ones weeks after they go in.
/// Stocks: the Vulfen hold fuel, HE and incendiaries. A raid takes its bombs and fuel when it goes, and cannot go short.
/// All numbers are first suggestions.</summary>
public sealed class Industry
{
    public const double Day = 86400, Week = 7 * Day, Ramp = 14 * Day;
    /// <summary>Capacity in points a week, whole plant. The Vulfen make few and costly; the UCS more and cheaper.</summary>
    public const double WolfPoints = 180, UcsPoints = 450, FuelWeek = 1500, FuelCap = 24000;
    public const int SchoolPlaces = 30;
    public readonly List<Works> Works = new();
    /// <summary>Tonnes, index 1 the Vulfen (as in the roster). The UCS's are not kept yet.</summary>
    public readonly double[] Fuel = new double[2], He = new double[2], Inc = new double[2];
    public readonly int[] BomberCrews = new int[2], Pilots = new int[2];
    /// <summary>The Vulfen schools' weekly intake, the player's to split. The UCS's are fixed.</summary>
    public int BomberIntake = 14;
    public int PilotIntake => SchoolPlaces - BomberIntake;
    public readonly List<Course> Schools = new();
    /// <summary>This week's Vulfen output, for the diary.</summary>
    public readonly Dictionary<Product, double> Made = new(); public int PassedOut;
    /// <summary>Everything made since the game was started or loaded, both sides (for the tests).</summary>
    public readonly Dictionary<Product, double> Ever = new();

    public static readonly Product[] WolfMakes = { Product.V16, Product.U4, Product.F5, Product.HE, Product.Incendiary, Product.CivilDefence }, UcsMakes = { Product.TB6, Product.SB3, Product.I8, Product.I7 };
    /// <summary>Points for one aircraft, or for one tonne of bombs.</summary>
    public static double Cost(Product p) => p switch { Product.V16 => 12, Product.U4 => 5, Product.F5 => 3, Product.HE => .1, Product.Incendiary => .16, Product.TB6 => 6, Product.SB3 => 3, Product.I8 => 1.5, Product.I7 => 3, Product.CivilDefence => .5, _ => 0 };
    public static Craft? CraftOf(Product p) => p switch { Product.V16 => Craft.V16, Product.U4 => Craft.U4, Product.F5 => Craft.F5, Product.TB6 => Craft.TB6, Product.SB3 => Craft.SB3, Product.I8 => Craft.I8, Product.I7 => Craft.I7, _ => null };
    public static string Name(Product p) => p switch { Product.Idle => "nothing", Product.HE => "HE bombs", Product.Incendiary => "incendiaries", Product.CivilDefence => "civil defence", _ => Sprites.Specs[CraftOf(p)!.Value].Name };
    public static string Short(Product p) => p switch { Product.Idle => "-", Product.HE => "HE", Product.Incendiary => "INC", Product.CivilDefence => "CIV DEF", _ => Sprites.Specs[CraftOf(p)!.Value].Name.Split(' ')[0] };
    /// <summary>Days out of production to change from one thing to another: bombs to bombs a week, aircraft to aircraft four.</summary>
    public static double RetoolDays(Product from, Product to) => from == to ? 0 : to == Product.Idle ? 0 : CraftOf(from) == null && CraftOf(to) == null && from != Product.Idle ? 7 : CraftOf(from) != null && CraftOf(to) != null ? 28 : 21;
    /// <summary>Fuel for one sortie, tonnes.</summary>
    public static double FuelFor(Craft k) => k switch { Craft.V16 => 6, Craft.U4 => 2, Craft.F5 => .7, Craft.TB6 => 4, Craft.SB3 => 1.5, Craft.I7 => .8, _ => .4 };
    /// <summary>Weeks at school: the Vulfen train longer, and it shows.</summary>
    public static int CourseWeeks(bool wolf, bool bomber) => wolf ? (bomber ? 16 : 12) : (bomber ? 8 : 6);
    int Intake(bool wolf, bool bomber) => wolf ? (bomber ? BomberIntake : PilotIntake) : (bomber ? 60 : 70);

    /// <summary>A city's factory floor, m², from its own buildings.</summary>
    /// <summary>A works' share of its side's capacity: by factory floor, but less than in proportion, so a small town's works still count and the biggest city is not the whole war.</summary>
    static double Weight(Place p) => Math.Pow(Floor(p), .6);
    public static double Floor(Place p) => p.Layout == null ? 20000.0 * p.Size : p.Layout.Plots.Where(b => b.Mat == CityPlan.Factory).Sum(b => (double)b.W * b.D);

    /// <summary>The first day of the war: works by factory floor, set to a mix like the old weekly deliveries; crews a little over the aircraft; classes already at the schools.</summary>
    public static Industry Start(World w, double now = 0)
    {
        var ind = new Industry();
        foreach (bool wolf in new[] { true, false })
        {
            var cities = w.Cities.Where(c => c.Wolf == wolf).ToList(); double floor = cities.Sum(Weight), total = wolf ? WolfPoints : UcsPoints;
            var mine = cities.Select(c => new Works { City = c, Wolf = wolf, Points = total * Weight(c) / floor }).OrderByDescending(q => q.Points).ToList();
            // the biggest works first, each to whatever is furthest short of its share
            var share = wolf ? new Dictionary<Product, double> { [Product.V16] = .45, [Product.U4] = .12, [Product.F5] = .18, [Product.HE] = .15, [Product.Incendiary] = .10 }
                             : new Dictionary<Product, double> { [Product.TB6] = .33, [Product.SB3] = .42, [Product.I8] = .2, [Product.I7] = .05 };
            var got = share.Keys.ToDictionary(k => k, _ => 0.0);
            foreach (var q in mine)
            {
                // a works that would take months over one aircraft makes bombs instead, or the cheapest type
                var can = share.Keys.Where(p => CraftOf(p) == null || q.Points / Cost(p) >= 1).ToList(); if (can.Count == 0) can = share.Keys.OrderBy(Cost).Take(1).ToList();
                var k = can.OrderByDescending(p => share[p] * total - got[p]).First(); q.Making = k; got[k] += q.Points;
            }
            ind.Works.AddRange(mine);
        }
        ind.Fuel[1] = 12000; ind.He[1] = 5000; ind.Inc[1] = 2000;      // doubled with the bombers (8 Nov)
        ind.Fuel[0] = 18000; ind.He[0] = 9000; ind.Inc[0] = 3500;
        ind.BomberCrews[1] = 440; ind.Pilots[1] = 185; ind.BomberCrews[0] = 520; ind.Pilots[0] = 700;
        foreach (bool wolf in new[] { true, false }) foreach (bool bomber in new[] { true, false })
            for (int k = 1; k <= CourseWeeks(wolf, bomber); k++) ind.Schools.Add(new Course { T = now + k * Week, Wolf = wolf, Bomber = bomber, N = ind.Intake(wolf, bomber) });
        return ind;
    }
    public Works? Of(Place city) => Works.FirstOrDefault(q => q.City == city);
    /// <summary>Sets a works to make something else: it stops for the retooling, then works up to full output.</summary>
    public void Order(Works q, Product p, double now)
    {
        if (q.Making == p) return; double d = RetoolDays(q.Making, p);
        q.Making = p; q.Progress = 0; q.Retooled = now + d * Day;
    }
    /// <summary>What a works is turning out now, a week: aircraft, or tonnes of bombs.</summary>
    public static double PerWeek(Works q, double plant, double now) => q.Making == Product.Idle ? 0 : q.Points * plant * q.Factor(now) / Cost(q.Making);

    /// <summary>The daily tick: each works adds a day's output, whole aircraft go to the fields, bombs to the dumps; fuel comes in; classes that are done pass out.</summary>
    public void Daily(double now, FireSim fire, Forces forces)
    {
        foreach (var q in Works)
        {
            if (q.Making == Product.Idle) continue;
            q.Progress += q.Points * fire.Plant(q.City) * forces.War.Labour(q.City, fire) * q.Factor(now) / 7; double c = Cost(q.Making);      // plant, and the people to work it
 int n = (int)(q.Progress / c); if (n <= 0) continue;
            q.Progress -= n * c; int s = q.Wolf ? 1 : 0;
            var k = CraftOf(q.Making);
            if (k != null) forces.Deliver(q.Wolf, k.Value, n, now + Day / 4);
            else if (q.Making == Product.HE) He[s] += n; else if (q.Making == Product.CivilDefence) forces.War.DefencePoints += n; else Inc[s] += n;
            if (q.Wolf) Made[q.Making] = Made.GetValueOrDefault(q.Making) + n;
            Ever[q.Making] = Ever.GetValueOrDefault(q.Making) + n;
        }
        Fuel[1] = Math.Min(FuelCap, Fuel[1] + FuelWeek / 7);
        Fuel[0] = Math.Min(FuelCap * 2, Fuel[0] + 3500.0 / 7); He[0] += 1200.0 / 7; Inc[0] += 600.0 / 7;      // the UCS's bombs and fuel: a steady supply, for now, into depots that can be hit
        for (int i = Schools.Count - 1; i >= 0; i--)
        {
            var c = Schools[i]; if (c.T > now) continue; int s = c.Wolf ? 1 : 0;
            if (c.Bomber) BomberCrews[s] += c.N; else Pilots[s] += c.N; if (c.Wolf) PassedOut += c.N; Schools.RemoveAt(i);
        }
    }
    /// <summary>The weekly tick: a new class goes into each school; the diary has what the works made.</summary>
    public string Weekly(double now)
    {
        foreach (bool wolf in new[] { true, false }) foreach (bool bomber in new[] { true, false })
            { int n = Intake(wolf, bomber); if (n > 0) Schools.Add(new Course { T = now + CourseWeeks(wolf, bomber) * Week, Wolf = wolf, Bomber = bomber, N = n }); }
        string Line(Func<Product, bool> which, Func<Product, double, string> say) => string.Join(" // ", Made.Where(kv => kv.Value > 0 && which(kv.Key)).OrderBy(kv => kv.Key).Select(kv => say(kv.Key, kv.Value)));
        string planes = Line(p => CraftOf(p) != null, (p, v) => $"{v:0} {Short(p)}");
        string stores = Line(p => CraftOf(p) == null, (p, v) => p == Product.HE ? $"+{v:N0} t HE ({He[1]:N0} t)" : p == Product.Incendiary ? $"+{v:N0} t incendiary ({Inc[1]:N0} t)" : $"+{v:0} civil defence");
        string s = "Manufactured: " + (planes != "" ? planes : "no aircraft") + (stores != "" ? "\n" + stores : "") + (PassedOut > 0 ? $"\n{PassedOut} crews passed out" : "");
        Made.Clear(); PassedOut = 0; return s;
    }
    /// <summary>Graduates still to come for one side and kind, and when the next class is out.</summary>
    public (int n, double next) InTraining(bool wolf, bool bomber) { var c = Schools.Where(q => q.Wolf == wolf && q.Bomber == bomber).ToList(); return (c.Sum(q => q.N), c.Count > 0 ? c.Min(q => q.T) : double.NaN); }
}
