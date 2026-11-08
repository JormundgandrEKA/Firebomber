using System;
using System.Collections.Generic;
using System.Linq;

namespace Firebomber;

/// <summary>What has happened to one city over the war: raids, bombs, firestorms, the dead, injured and homeless, and buildings lost and rebuilt. Kept for every city, materialised or not.</summary>
public sealed class CityRecord
{
    public int Raids, Firestorms, Lost, Rebuilt; public double Tonnes, Dead, Injured, Homeless, LastHitT = -1e9, LastStormT = -1e9;
    /// <summary>The same tallies when the latest raid began, so a raid's own toll can be told.</summary>
    public double DeadAtRaid, HomelessAtRaid, TonnesAtRaid; public int LostAtRaid;
    public int Population;
}

/// <summary>The people in a city's buildings, what bombs and fire do to them, and how the city mends. All figures are first suggestions, to be checked against the Survey's anchors (see DESIGN_NOTES): a city houses one person for every 25 m² of floor (a crowded 1930s city), and a fifth of them work in the works.
/// Who is inside depends on the hour; who is in a shelter depends on warning (Vulfen radar gives more). Blast kills those above ground near it; an ordinary fire kills few, since people get out; a firestorm kills even those in the shelters.</summary>
public sealed partial class FireSim
{
    public readonly Dictionary<Place, CityRecord> Records = new();
    /// <summary>The hour of day where the bombs are falling; set by the game. Tests leave it at noon.</summary>
    public Func<double>? Hour;
    /// <summary>Each side's civil defence, 0 to 1, set by the game; tests leave it at the start level.</summary>
    public Func<bool, double>? Defence;
    /// <summary>How much faster a city's works are rebuilt than usual: the UCS puts its bombed works first.</summary>
    public Func<Place, double>? RepairBoost;
    /// <summary>How dry the ground is at a city, the wind speed, and the rain there; set by the campaign.</summary>
    public Func<Place, (double dry, double wind, double rain)>? Climate;
    /// <summary>What the day does to fire: the area and time a firestorm needs (lower is easier), and how fast fire spreads. An ordinary day comes to about one; dry and windy, firestorms come far more easily; rain damps everything.</summary>
    public static (float area, float hold, float heat) FireDay(double dry, double wind, double rain)
    {
        double dw = .55 * dry + .45 * Math.Min(1, wind / 18);
        return ((float)((1.2 - .55 * dw) * (1 + rain)), (float)((1.17 - .45 * dw) * (1 + .5 * rain)), (float)((.83 + .46 * dw) * (1 - .5 * rain)));
    }
    /// <summary>Flying embers: the chance each second that one burning building lights another well downwind. Nothing below a fresh breeze; in a gale over dry ground, fire jumps streets and firebreaks and gets away.</summary>
    public static float Embers(double dry, double wind, double rain) => (float)(.00018 * Math.Max(0, wind - 8) * (.25 + dry) * (1 - rain));
    /// <summary>A city's fire service at full strength, in fires a minute, with civil defence.</summary>
    public float FullBrigade(Place p) => (float)((5 + p.Size) * (.85 + (Defence?.Invoke(p.Wolf) ?? .15)));
    /// <summary>The share of people below ground when the bombs come: radar warning gets more Vulfen there; shelters and wardens more again.</summary>
    public static double Shelter(bool wolf, double defence) => (wolf ? .7 : .5) + .3 * (defence - .15);
    public CityRecord Rec(Place p) { if (!Records.TryGetValue(p, out var r)) Records[p] = r = new CityRecord { Population = PopulationOf(p) }; return r; }
    static readonly float[] Floors = { 1.6f, 3.5f, 1 }; const double M2PerHead = 25;
    static readonly System.Collections.Concurrent.ConcurrentDictionary<Place, int> pops = new();
    /// <summary>From the city's own buildings, so it is known before the city is ever bombed.</summary>
    public static int PopulationOf(Place p) => pops.GetOrAdd(p, q => q.Layout == null ? 9000 * q.Size * q.Size : (int)(q.Layout.Plots.Where(b => b.Mat != CityPlan.Factory).Sum(b => b.W * b.D * Floors[b.Mat]) / M2PerHead));

    /// <summary>Puts people into a newly made city: residents by floor area of the houses and flats, workers by floor area of the works.</summary>
    void Occupy(CityFire c)
    {
        double home = 0, work = 0;
        foreach (ref var b in c.B.AsSpan()) { double a = b.W * b.D * Floors[b.Mat]; if (b.Mat == CityPlan.Factory) work += a; else home += a; }
        int pop = PopulationOf(c.P); double perHome = home > 0 ? pop / home : 0, perWork = work > 0 ? pop * .2 / work : 0;      // the same total as PopulationOf, spread over this city's buildings
        foreach (ref var b in c.B.AsSpan()) { double a = b.W * b.D * Floors[b.Mat]; if (b.Mat == CityPlan.Factory) b.Work = (float)(a * perWork); else { b.Res = (float)(a * perHome); b.Work = b.Mat == CityPlan.Brick ? b.Res * .25f : 0; } }
    }

    /// <summary>A building is gutted or blown down: the people in it at this hour.</summary>
    void Harm(CityFire c, ref Bld b, byte state)
    {
        var r = Rec(c.P); r.Lost++;
        double h = Hour?.Invoke() ?? 12; bool night = h < 6 || h >= 21;
        double inside = b.Res * (night ? .92 : .45) + b.Work * (night ? .25 : .9);
        double shelter = Shelter(c.P.Wolf, Defence?.Invoke(c.P.Wolf) ?? .15);
        bool storm = b.Stormed;
        double kill, hurt;
        if (state == Blasted) { kill = inside * ((1 - shelter) * .625 + shelter * .088); hurt = inside * ((1 - shelter) * .32 + shelter * .12); }
        else if (storm) { kill = inside * (.44 + .26 * (1 - shelter)); hurt = inside * .15; }      // the shelters become ovens: in the worst districts of Hamburg a quarter and more of the people died
        else { kill = inside * ((1 - shelter) * .117 + shelter * .03) * (b.Mat == CityPlan.Wood ? 1.6 : 1); hurt = inside * .1; }
        r.Dead += kill; r.Injured += hurt; r.Homeless += b.Res * (1 - (inside > 0 ? kill / inside : 0));
    }

    /// <summary>A day of repair. Works are back in a few months; brick housing takes years, wooden less. A damaged works is patched up; blown roofs are covered. Nothing is rebuilt while it burns.</summary>
    public void Repair(double days)
    {
        foreach (var c in Cities)
        {
            if (c.Burning.Count > 0) continue; var r = Rec(c.P);
            for (int i = 0; i < c.B.Length; i++)
            {
                ref var b = ref c.B[i];
                if (b.State is Gutted or Blasted)
                {
                    double meanDays = b.Mat == CityPlan.Factory ? 80 / (RepairBoost?.Invoke(c.P) ?? 1) : b.Mat == CityPlan.Brick ? 900 : 500;
                    if (rng.NextDouble() < days / meanDays) { r.Homeless = Math.Max(0, r.Homeless - b.Res); r.Rebuilt++; b.Open = false; if (b.Mat == CityPlan.Factory) b.Hp = FactoryHp; Set(c, i, Intact); }
                }
                else if (b.State == Intact)
                {
                    if (b.Mat == CityPlan.Factory && b.Hp < FactoryHp && rng.NextDouble() < days / 20) b.Hp++;
                    if (b.Open && rng.NextDouble() < days / 30) b.Open = false;
                }
            }
        }
    }

    /// <summary>Bombs on a city: tonnes, and a new raid if none has fallen there for three hours.</summary>
    void Record(Place p, Impact h)
    {
        var r = Rec(p);
        if (t - r.LastHitT > 3 * 3600) { r.Raids++; r.DeadAtRaid = r.Dead; r.HomelessAtRaid = r.Homeless; r.TonnesAtRaid = r.Tonnes; r.LostAtRaid = r.Lost; }
        r.LastHitT = t; r.Tonnes += h.Kg / 1000.0;
    }

    /// <summary>How much of a city's works can work: factory floor still standing, a damaged works counting half. A city never bombed is whole. The panels ask every frame, so a recent answer is kept for a second of real time.</summary>
    public double Plant(Place p, bool cached = false)
    {
        long now = Environment.TickCount64; if (cached && plantSeen.TryGetValue(p, out var c) && now - c.at < 1000) return c.v;
        var cf = Cities.FirstOrDefault(q => q.P == p); double v = 1;
        if (cf != null) { double all = 0, ok = 0; foreach (ref var b in cf.B.AsSpan()) { if (b.Mat != CityPlan.Factory) continue; double a = b.W * b.D; all += a; if (b.State == Intact) ok += b.Hp >= FactoryHp ? a : a * .5; } v = all > 0 ? ok / all : 1; }
        plantSeen[p] = (now, v); return v;
    }
    readonly Dictionary<Place, (long at, double v)> plantSeen = new();

    // ---------------------------------------------------------------- saving
    public void RestoreTime(double time) => t = time;
    /// <summary>What is worth keeping of a city: every building not as it was built (index, state, roof, works damage). A building still burning is saved as gutted, since saving waits for the fires to be out or nearly.</summary>
    public IEnumerable<(int i, byte state, byte hp, bool open)> Changes(CityFire c)
    {
        for (int i = 0; i < c.B.Length; i++) { var b = c.B[i]; bool hpd = b.Mat == CityPlan.Factory && b.Hp < FactoryHp; if (b.State != Intact || b.Open || hpd) yield return (i, b.State == Burning ? Gutted : b.State, b.Hp, b.Open); }
    }
    public void Apply(CityFire c, int i, byte state, byte hp, bool open) { ref var b = ref c.B[i]; b.Hp = hp; b.Open = open; if (state != Intact) Set(c, i, state); }
}
