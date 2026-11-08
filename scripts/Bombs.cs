using System;
using System.Collections.Generic;
using System.Linq;

namespace Firebomber;

public sealed record Bomb(string Name, int Kg, bool He, int Bomblets = 0);
public enum Mission { Demolition, General, Half, Fire, AllIncendiary }

/// <summary>The common bombs of the day, and how a load is made up: by weight first, then by what the raid is for.</summary>
public static class Bombs
{
    public static readonly Bomb HE100 = new("100 kg HE", 100, true), HE250 = new("250 kg HE", 250, true), HE500 = new("500 kg HE", 500, true), HE1000 = new("1000 kg HE", 1000, true);
    public static readonly Bomb Oil50 = new("50 kg oil bomb", 50, false, 3), Cluster250 = new("250 kg incendiary cluster", 250, false, 42), Cluster500 = new("500 kg incendiary cluster", 500, false, 84);   // tonne for tonne, a cluster and oil bombs start about as many fires: oil is lighter on the racks' lift, clusters make better use of them
    public static readonly string[] MissionNames = { "demolition", "general HE", "half and half", "fire raid", "all incendiary" };

    static Bomb Biggest(Design d, double atMost)
    {
        foreach (var b in new[] { HE1000, HE500, HE250, HE100 }) if (b.Kg <= d.MaxBomb && b.Kg <= atMost) return b;
        return HE100;
    }
    /// <summary>Makes up a load of about this weight from the bombs this aircraft can carry, in the slots it has.</summary>
    public static List<Bomb> Load(Design d, double kg, Mission m)
    {
        var o = new List<Bomb>(); double left = kg;
        void Add(Bomb b, int n) { for (int i = 0; i < n && left >= b.Kg && o.Count < d.Slots; i++) { o.Add(b); left -= b.Kg; } }
        void Small() { if (d.MaxBomb >= 250) Add(HE250, (int)(left / 250)); Add(HE100, (int)(left / 100)); }
        void Fill() { Add(d.MaxBomb >= 500 && left >= 500 && kg >= 3000 ? Cluster500 : Cluster250, 1); }
        switch (m)
        {
            case Mission.Demolition:
            {
                // a few big bombs and a couple of light ones
                var big = Biggest(d, kg * .5); Add(big, (int)(kg * .8 / big.Kg)); Small(); break;
            }
            case Mission.General: if (d.Slots * 250 < kg && d.MaxBomb >= 500) Add(HE500, (int)((kg - d.Slots * 125) / 500)); Small(); break;
            case Mission.Half:
            {
                double he = kg / 2; Add(HE250, (int)(he / 250)); Add(HE100, (int)((he - o.Sum(b => b.Kg)) / 100));
                while (left >= 250 && o.Count < d.Slots) Fill();
                Add(Oil50, (int)(left / 50)); break;
            }
            case Mission.Fire:
            {
                // fewer and more varied HE, to break roofs and keep the crews' heads down; everything else burns
                double he = kg * .2; if (d.MaxBomb >= 500 && he >= 500) Add(HE500, 1); else if (he >= 250) Add(HE250, 1);
                Add(HE100, (int)Math.Max(0, (he - o.Sum(b => b.Kg)) / 100));
                while (left >= 250 && o.Count < d.Slots) Fill();
                Add(Oil50, (int)(left / 50)); break;
            }
            default:
                while (left >= 250 && o.Count < d.Slots) Fill();
                Add(Oil50, (int)(left / 50)); break;
        }
        return o;
    }
    public static double HeShare(List<Bomb> l) { double all = l.Sum(b => b.Kg); return all <= 0 ? 0 : l.Where(b => b.He).Sum(b => b.Kg) / all; }
    public static string Describe(List<Bomb> l) => string.Join(", ", l.GroupBy(b => b.Name).OrderByDescending(g => g.First().Kg).Select(g => $"{g.Count()} x {g.Key}"));
}

/// <summary>A fixed load an aircraft type can be sent with: the planner picks one for each flight. Weight comes off the fuel, so the heavier the load the shorter the reach.</summary>
public sealed record Loadout(string Name, params (Bomb b, int n)[] Racks)
{
    public List<Bomb> Plan => Racks.SelectMany(r => Enumerable.Repeat(r.b, r.n)).ToList();
    public double Kg => Racks.Sum(r => r.b.Kg * r.n);
}
public static class Loadouts
{
    public static readonly Dictionary<Craft, Loadout[]> Of = new()
    {
        [Craft.V16] = new Loadout[]
        {
            new("Half and half", (Bombs.HE250, 10), (Bombs.Cluster250, 10)),
            new("Fire raid", (Bombs.HE250, 4), (Bombs.Cluster250, 16)),
            new("All incendiary", (Bombs.Cluster250, 20)),
            new("Break and burn", (Bombs.HE1000, 2), (Bombs.Cluster250, 8)),
            new("General HE", (Bombs.HE250, 20)),
            new("Demolition", (Bombs.HE1000, 4), (Bombs.HE250, 4)),
            new("Long reach, fire", (Bombs.Cluster250, 12)),
            new("Maximum fire", (Bombs.Cluster500, 13)),
        },
        [Craft.U4] = new Loadout[]
        {
            new("Mixed", (Bombs.HE250, 2), (Bombs.Cluster250, 2)),
            new("Strike", (Bombs.HE250, 4)),
            new("Fire marker", (Bombs.Cluster250, 4)),
            new("Oil, light and far", (Bombs.Oil50, 6)),
            new("Long reach", (Bombs.HE250, 2)),
            new("Works breaker", (Bombs.HE1000, 2)),
        },
    };
    public static Loadout Default(Craft k) => Of[k][0];
}

/// <summary>What weight does to an aeroplane. These are read per aircraft from what it is carrying now, so a bomber that has dropped is lighter, faster and can climb higher.</summary>
public static class Weight
{
    static double F(Design d, double kg) => d.MaxKg <= 0 ? 0 : Math.Clamp(kg / d.MaxKg, 0, 1.1);
    public static double Cruise(Design d, double kg) => d.Cruise * (1 - d.SpeedPen * F(d, kg));
    public static double Top(Design d, double kg) => d.Max * (1 - d.SpeedPen * F(d, kg));
    public static double Roof(Design d, double kg) => d.Ceil * (1 - d.CeilPen * F(d, kg));
    public static double Climb(Design d, double kg) => d.Climb * (1 - .5 * F(d, kg));
    /// <summary>Range in km with this load: the bigger the load, the less fuel it can lift.</summary>
    public static double RangeKm(Design d, double kg)
    {
        var r = d.RangeAt; if (r.Length == 0) return d.Fuel * d.Cruise / 1000;
        if (kg <= r[0].kg) return r[0].km;
        for (int i = 1; i < r.Length; i++) if (kg <= r[i].kg) return r[i - 1].km + (r[i].km - r[i - 1].km) * (kg - r[i - 1].kg) / (r[i].kg - r[i - 1].kg);
        return r[^1].km;
    }
    public static double FuelSeconds(Design d, double kg) => RangeKm(d, kg) * 1000 / Cruise(d, kg);
    /// <summary>The load a planner would send on a trip of this length: the usual load, cut down if the round trip needs it.</summary>
    public static double PlanKg(Design d, double oneWayKm)
    {
        double need = oneWayKm * 2 * 1.15, kg = d.UsualKg;
        while (kg > d.MaxKg * .2 && RangeKm(d, kg) < need) kg -= 250;
        return kg;
    }
}
