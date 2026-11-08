using System;
using System.Collections.Generic;
using System.Linq;

namespace Firebomber;

/// <summary>One type of aircraft at one field: how many are ready, and how many are coming (back from servicing or repair, or on their way from another field), each with the moment they will be ready.</summary>
public sealed class Stock
{
    public int Ready; public readonly List<(double T, int N)> Coming = new();
    public int ComingN => Coming.Sum(c => c.N);
}

/// <summary>Fighters told off to fly a patrol at a place. Hours: 0 by day, 1 by night, 2 at all hours. A timetable, not a flight: they become aircraft in the air only when a raid comes near.</summary>
public sealed class Patrol
{
    public Craft Kind; public int N; public double X, Y; public int Hours;
    public bool Up(double hour) => Hours == 2 || (Hours == 0) == (hour >= 7 && hour < 19);
    public string HoursText => Hours == 0 ? "DAY" : Hours == 1 ? "NIGHT" : "ALL";
    /// <summary>Tonnes of fuel for a day of it: fighters relieve each other, three sorties a shift.</summary>
    public double FuelADay => N * Industry.FuelFor(Kind) * (Hours == 2 ? 6 : 3);
}
/// <summary>Aircraft on their way from one field to another, for the map. They arrive through the field's stock; this only says where they are.</summary>
public sealed class FerryFlight { public Place From = null!, To = null!; public Craft Kind; public int N; public double T0, Land; }

/// <summary>An airfield and what is on it. Its capacity is set by the city it serves.</summary>
public sealed class Base
{
    public Place Field = null!, Town = null!; public bool Wolf; public int Capacity;
    /// <summary>The field's standing orders: whether its fighters go up when a raid is reported, how they go about it, and its patrols.</summary>
    public bool StandDown; public InterceptMode Intercept = InterceptMode.Direct; public readonly List<Patrol> Patrols = new();
    public readonly Dictionary<Craft, Stock> Of = new();
    public Stock S(Craft k) { if (!Of.TryGetValue(k, out var s)) Of[k] = s = new Stock(); return s; }
    public int Total => Of.Values.Sum(s => s.Ready + s.ComingN);
    public string Name => Town.Name;
}

/// <summary>Every aircraft both sides have, field by field. Raids and escorts are taken out of a field's ready stock when they go; survivors come back through servicing (a day), or repair (days to weeks by damage); the lost are written off.
/// Defenders and interceptors stay on the stock while they wait: only those that fly go through servicing. Aircraft can be ferried between fields, which takes the flight time and half a day.
/// New aircraft come from the works (Industry.cs). All numbers are first suggestions.</summary>
public sealed class Forces
{
    public readonly List<Base> Bases = new();
    /// <summary>The works, the stocks and the crews (part 3).</summary>
    public Industry Ind = null!;
    /// <summary>War support, labour and civil defence (part 4).</summary>
    public War War = new();
    /// <summary>The UCS's planner and what it believes (part 5).</summary>
    public UcsPlanner Planner = new();
    /// <summary>The war over time, for the graphs.</summary>
    public History Hist = new();
    /// <summary>Ferry flights still in the air.</summary>
    public readonly List<FerryFlight> Flights = new();
    /// <summary>The fields' parts, the depots, the guns and the radar chain, as things bombs can break.</summary>
    public Sites Sites = null!;
    public static readonly Craft[] WolfTypes = { Craft.V16, Craft.U4, Craft.F5 }, UcsTypes = { Craft.TB6, Craft.SB3, Craft.I7, Craft.I8 };
    public const double Day = 86400;
    /// <summary>A count as the enemy's would be estimated: to the nearest ten, or five when small.</summary>
    public static int About(int n) => n < 30 ? (int)Math.Round(n / 5.0) * 5 : (int)Math.Round(n / 10.0) * 10;

    public Forces(World w)
    {
        foreach (var f in w.Fields) Bases.Add(new Base { Field = f, Town = f.Town!, Wolf = true, Capacity = 60 + 25 * f.Town!.Size });
        foreach (var f in w.EnemyFields) Bases.Add(new Base { Field = f, Town = f.Town!, Wolf = false, Capacity = 80 + 40 * f.Town!.Size });
        Sites = new Sites(w);
    }
    public Base Of(Place field) => Bases.First(b => b.Field == field);
    public Base? Named(string fieldName) => Bases.FirstOrDefault(b => b.Field.Name == fieldName);
    public IEnumerable<Base> Side(bool wolf) => Bases.Where(b => b.Wolf == wolf);
    public int Ready(bool wolf, Craft k) => Side(wolf).Sum(b => b.S(k).Ready);
    public int All(bool wolf, Craft k) => Side(wolf).Sum(b => b.S(k).Ready + b.S(k).ComingN);

    /// <summary>The order of battle on the first day. The Vulfen: few, good and costly, the bombers at the three bomber bases, fighters spread over every city. The UCS: many, the bombers at the cities nearest the sea, fighters by city size.</summary>
    public static Forces Start(World w)
    {
        var f = new Forces(w);
        void Put(string town, Craft k, int n) { var b = f.Bases.First(q => q.Town.Name == town); b.S(k).Ready += n; }
        // doubled 8 Nov (the designer): 288 V-16 and 96 U-4, over five fields so none is crammed
        Put("Ostrand", Craft.V16, 120); Put("Hjalmar", Craft.V16, 96); Put("Skarn", Craft.V16, 36); Put("Vess", Craft.V16, 36);
        Put("Hjalmar", Craft.U4, 40); Put("Skarn", Craft.U4, 28); Put("Kaldvik", Craft.U4, 28);
        foreach (var b in f.Side(true)) b.S(Craft.F5).Ready += b.Town.Name is "Ostrand" or "Hjalmar" or "Skarn" ? 18 : 4 + 2 * b.Town.Size;
        foreach (var b in f.Side(false)) { b.S(Craft.I8).Ready += 9 * b.Town.Size; b.S(Craft.I7).Ready += b.Town.Size; }
        // UCS bomber bases: the cities nearest the Vulfen
        var front = f.Side(false).OrderByDescending(b => b.Town.Y).Take(6).ToList();
        for (int i = 0; i < front.Count; i++) { front[i].S(Craft.TB6).Ready += i < 3 ? 36 : 18; front[i].S(Craft.SB3).Ready += i < 3 ? 72 : 36; }
        f.Ind = Industry.Start(w);
        return f;
    }

    /// <summary>The daily tick: whatever is due is ready.</summary>
    public void Tick(double now)
    {
        Flights.RemoveAll(q => q.Land <= now);
        foreach (var b in Side(true)) foreach (var p in b.Patrols) Ind.Fuel[1] = Math.Max(0, Ind.Fuel[1] - p.FuelADay);
        foreach (var b in Bases) foreach (var s in b.Of.Values)
            for (int i = s.Coming.Count - 1; i >= 0; i--) if (s.Coming[i].T <= now) { s.Ready += s.Coming[i].N; s.Coming.RemoveAt(i); }
    }

    /// <summary>New aircraft from the works: to the field that holds that type and has fewest of it for its size, or failing that the one with most room. Returns the field, or null if every field is full.</summary>
    public Base? Deliver(bool wolf, Craft k, int n, double at)
    {
        var b = Side(wolf).Where(q => q.S(k).Ready + q.S(k).ComingN > 0 && q.Total + n <= q.Capacity).OrderBy(q => (q.S(k).Ready + q.S(k).ComingN) / (double)q.Town.Size).FirstOrDefault()
             ?? Side(wolf).Where(q => q.Total + n <= q.Capacity).OrderByDescending(q => q.Capacity - q.Total).FirstOrDefault()
             ?? Side(wolf).OrderByDescending(q => q.Capacity - q.Total).First();      // every field full: they are parked somewhere all the same
        b.S(k).Coming.Add((at, n)); return b;
    }

    /// <summary>Moves aircraft between two of one side's fields. Returns why not, or null if they are on their way.</summary>
    public string? Ferry(Base from, Base to, Craft k, int n, double now)
    {
        if (from == to) return "they are already there";
        if (from.Wolf != to.Wolf) return "not to the other side's field";
        if (Sites.Closed(from.Field)) return $"{from.Name}'s runway is closed";
        if (Sites.Closed(to.Field)) return $"{to.Name}'s runway is closed";
        if (n <= 0 || from.S(k).Ready < n) return $"only {from.S(k).Ready} {Sprites.Specs[k].Name.Split(' ')[0]} ready at {from.Name}";
        if (to.Total + n > to.Capacity) return $"{to.Name} has room for {Math.Max(0, to.Capacity - to.Total)} more";
        double km = Math.Sqrt(Math.Pow(from.Field.X - to.Field.X, 2) + Math.Pow(from.Field.Y - to.Field.Y, 2)), secs = km / Design.Of[k].Cruise + 2 * 3600;      // the flight, and two hours to see them in and turned round
        from.S(k).Ready -= n; to.S(k).Coming.Add((now + secs, n));
        Flights.Add(new FerryFlight { From = from.Field, To = to.Field, Kind = k, N = n, T0 = now, Land = now + km / Design.Of[k].Cruise });
        return null;
    }

    /// <summary>The UCS moves its own aircraft every morning. Fighters go where they are needed, by city size and by how often each city has been bombed lately; bombers are kept at the forward bases and taken off fields that are too full.
    /// A few moves a day, by whole flights. The player does not see them, only their effect (part 5's planner will make these decisions with more cunning).</summary>
    public int UcsMoves(double now, Func<Place, double> recentRaids)
    {
        var ucs = Side(false).ToList(); int moved = 0;
        foreach (var k in new[] { Craft.I8, Craft.I7 })
        {
            double W(Base b) => b.Town.Size + 3 * recentRaids(b.Town) + (b.Town.Y > ucs.Average(q => q.Town.Y) ? 1.5 : 0);      // size, raids, and the side nearest the Vulfen
            int total = ucs.Sum(b => b.S(k).Ready + b.S(k).ComingN); double sw = ucs.Sum(W); int step = k == Craft.I8 ? 8 : 4;
            var want = ucs.ToDictionary(b => b, b => total * W(b) / sw);
            for (int n = 0; n < 6; n++)
            {
                var over = ucs.Where(b => b.S(k).Ready >= step).OrderByDescending(b => b.S(k).Ready + b.S(k).ComingN - want[b]).First();
                var under = ucs.Where(b => b.Total + step <= b.Capacity).OrderBy(b => b.S(k).Ready + b.S(k).ComingN - want[b]).First();
                if (over == under || over.S(k).Ready + over.S(k).ComingN - want[over] < step || want[under] - under.S(k).Ready - under.S(k).ComingN < step) break;
                if (Ferry(over, under, k, step, now) == null) moved += step; else break;
            }
        }
        // bombers: off any field more than nine-tenths full, to the forward bomber base with most room
        var forward = ucs.OrderByDescending(b => b.Town.Y).Take(6).ToList();
        foreach (var b in ucs.Where(q => q.Total > q.Capacity * .9))
            foreach (var k in new[] { Craft.SB3, Craft.TB6 })
            {
                int n = Math.Min(b.S(k).Ready, Design.Of[k].Box); if (n < 6) continue;
                var to = forward.Where(q => q != b && q.Total + n <= q.Capacity * .8).OrderByDescending(q => q.Capacity - q.Total).FirstOrDefault();
                if (to != null && Ferry(b, to, k, n, now) == null) moved += n;
            }
        return moved;
    }
    /// <summary>How long an aircraft is out of the line after a sortie: a day's servicing and the crew's rest, or repair by how badly it is hit.</summary>
    public static double Out(double damage) => damage > .5 ? 21 * Day : damage > .25 ? 8 * Day : damage > .08 ? 3 * Day : Day;
}
