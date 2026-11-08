using System;
using System.Collections.Generic;
using System.Linq;

namespace Firebomber;

/// <summary>One day's figures for a side. For the UCS, Support is what intelligence estimated, not the truth.</summary>
public struct SideSample { public double T; public float Support, Output, He, Inc, Fuel; public int Bombers, Fighters, BomberCrews, Pilots; }
/// <summary>A city's figures on a day they changed: people alive, share of buildings standing, share of works working.</summary>
public struct CitySample { public double T; public float Alive, Standing, Works; }

/// <summary>The war over time, for the graphs. Each side is sampled every morning; a city only when something about it has changed, so a city never bombed costs one entry.</summary>
public sealed class History
{
    public readonly List<SideSample>[] Side = { new(), new() };
    public readonly Dictionary<string, List<CitySample>> City = new();
    public bool Empty => Side[0].Count == 0;

    public void Sample(double now, World w, Forces f, FireSim fire)
    {
        var ind = f.Ind; var war = f.War;
        for (int s = 0; s < 2; s++)
        {
            bool wolf = s == 1; int Sum(params Craft[] k) => k.Sum(q => f.All(wolf, q));
            Side[s].Add(new SideSample
            {
                T = now, Support = (float)(wolf ? war.Support[1] : war.UcsEstimate),
                Output = (float)ind.Works.Where(q => q.Wolf == wolf && q.Making != Product.Idle).Sum(q => q.Points * fire.Plant(q.City) * war.Labour(q.City, fire) * q.Factor(now)),
                He = (float)ind.He[s], Inc = (float)ind.Inc[s], Fuel = (float)ind.Fuel[s],
                Bombers = wolf ? Sum(Craft.V16, Craft.U4) : Sum(Craft.TB6, Craft.SB3), Fighters = wolf ? Sum(Craft.F5) : Sum(Craft.I8, Craft.I7),
                BomberCrews = ind.BomberCrews[s], Pilots = ind.Pilots[s],
            });
        }
        foreach (var c in w.Cities)
        {
            var (alive, standing, works) = Now(c, fire);
            if (!City.TryGetValue(c.Name, out var list)) City[c.Name] = list = new List<CitySample>();
            if (list.Count > 0) { var l = list[^1]; if (Math.Abs(l.Alive - alive) < 1 && Math.Abs(l.Standing - standing) < .0005f && Math.Abs(l.Works - works) < .0005f) continue; }
            list.Add(new CitySample { T = now, Alive = alive, Standing = standing, Works = works });
        }
    }

    /// <summary>A city as it stands: people alive, share of its buildings standing, share of its works.</summary>
    public static (float alive, float standing, float works) Now(Place c, FireSim fire)
    {
        fire.Records.TryGetValue(c, out var r); int plots = c.Layout?.Plots.Length ?? 0;
        float alive = (float)(FireSim.PopulationOf(c) - (r?.Dead ?? 0)), standing = plots == 0 || r == null ? 1 : Math.Clamp(1 - (r.Lost - r.Rebuilt) / (float)plots, 0, 1);
        return (alive, standing, (float)fire.Plant(c, true));
    }
}
