using System;
using System.Collections.Generic;
using System.Linq;

namespace Firebomber;

/// <summary>War support and what it does (roadmap part 4). Index 1 is the Vulfen, 0 the UCS, as in the roster.
/// Support (0-100) is booked weekly. It falls with shocks: the week's dead and dehoused as a share of the nation, firestorms, a city destroyed (once, by its share of the nation), and aircrew lost.
/// A people get used to steady bombing: a shock the size of the recent run hurts as much as it is, a smaller one less, a sudden new scale up to half again more.
/// It drifts back towards a baseline, and rises with the side's own successes: enemy bombers brought down, and the enemy's own shocks.
/// Below 60 people stay away from work; below 20 for four weeks running, a side asks for terms.
/// Civil defence (shelters, wardens, warning) is made by the works like anything else, and gets more people below ground in time. All numbers are first suggestions.</summary>
public sealed class War
{
    public readonly double[] Support = { 70, 75 }, Baseline = { 70, 75 }, Habit = new double[2], LastShock = new double[2], LastGain = new double[2];
    public readonly int[] WeeksBroken = new int[2];
    /// <summary>What had been booked at the last weekly tick, so a week's toll can be told.</summary>
    public readonly double[] DeadSeen = new double[2], HomelessSeen = new double[2]; public readonly int[] StormsSeen = new int[2];
    public readonly HashSet<string> Destroyed = new();
    /// <summary>This week: each side's bombers brought down, and its aircrew lost.</summary>
    public readonly int[] BombersDowned = new int[2], CrewsLost = new int[2];
    /// <summary>Civil defence points built up by the Vulfen works; the UCS has a fixed, lower level.</summary>
    public double DefencePoints;
    /// <summary>The share of our bombers lost on our last raid, or below zero if there has been none.</summary>
    public double LastAttrition = -1;
    /// <summary>The side that asked for terms, or -1.</summary>
    public int Loser = -1;
    /// <summary>What intelligence last said the UCS's support was: an estimate, not the truth.</summary>
    public double UcsEstimate = 70, EstimateSpread = 12;

    /// <summary>Civil defence, 0 to 1: the share of what can be done.</summary>
    public double Defence(bool wolf) => wolf ? .15 + .6 * DefencePoints / (DefencePoints + 300) : .15;
    /// <summary>The share of workers who turn up, by support.</summary>
    public double Turnout(bool wolf) { double s = Support[wolf ? 1 : 0]; return s >= 60 ? 1 : .6 + .4 * s / 60; }
    /// <summary>A city's labour: less its dead and its homeless (who have other things to do than work), times the turnout.</summary>
    public double Labour(Place p, FireSim fire)
    {
        if (!fire.Records.TryGetValue(p, out var r) || r.Population <= 0) return Turnout(p.Wolf);
        return Math.Clamp(1 - .8 * r.Homeless / r.Population - 3 * r.Dead / r.Population, .35, 1) * Turnout(p.Wolf);
    }

    static double Pop(World w, bool wolf) => w.Cities.Where(c => c.Wolf == wolf).Sum(c => (double)FireSim.PopulationOf(c));

    /// <summary>The weekly booking. Returns the diary lines: ours in full, theirs as intelligence has it.</summary>
    public List<(string text, bool alarm)> Weekly(World w, FireSim fire, Industry ind, Random rng)
    {
        var raw = new double[2]; var why = new List<string>[] { new(), new() };
        for (int s = 0; s < 2; s++)
        {
            bool wolf = s == 1; double pop = Pop(w, wolf); var recs = fire.Records.Where(kv => kv.Key.Wolf == wolf).Select(kv => kv.Value).ToList();
            double dead = recs.Sum(r => r.Dead), homeless = recs.Sum(r => r.Homeless); int storms = recs.Sum(r => r.Firestorms);
            double dDead = Math.Max(0, dead - DeadSeen[s]), dHome = Math.Max(0, homeless - HomelessSeen[s]); int dStorm = storms - StormsSeen[s];
            double x = 800 * dDead / pop + 250 * dHome / pop + 2 * dStorm;      // a big raid a few points: the war is to last years
            if (dDead >= 50) why[s].Add($"{Main.Round(dDead)} dead"); if (dHome >= 500) why[s].Add($"{Main.Round(dHome)} homeless"); if (dStorm > 0) why[s].Add(dStorm > 1 ? $"{dStorm} firestorms" : "a firestorm");
            foreach (var (p, r) in fire.Records.Where(kv => kv.Key.Wolf == wolf))
            {
                int n = p.Layout?.Plots.Length ?? 0; if (n == 0 || Destroyed.Contains(p.Name) || r.Lost - r.Rebuilt < .4 * n) continue;
                Destroyed.Add(p.Name); x += 3 + 25 * FireSim.PopulationOf(p) / pop; why[s].Add($"{p.Name} destroyed");
            }
            int crews = ind.BomberCrews[s] + ind.Pilots[s] + CrewsLost[s];
            if (CrewsLost[s] > 0) { x += 15.0 * CrewsLost[s] / Math.Max(1, crews); why[s].Add($"{CrewsLost[s]} aircrew lost"); }
            raw[s] = x; DeadSeen[s] = dead; HomelessSeen[s] = homeless; StormsSeen[s] = storms;
        }
        var lines = new List<(string, bool)>();
        for (int s = 0; s < 2; s++)
        {
            int o = 1 - s; double x = raw[s];
            double hurt = x > 0 ? x * (.5 + x / (Habit[s] + x)) : 0;      // steady bombing x1, lighter than usual less, a new scale up to x1.5
            Habit[s] = .75 * Habit[s] + .25 * x;
            double gain = Math.Min(4, .015 * BombersDowned[o] + .4 * raw[o]), back = .12 * (Baseline[s] - Support[s]);
            Support[s] = Math.Clamp(Support[s] - hurt + gain + back, 0, 100); LastShock[s] = hurt; LastGain[s] = gain;
            WeeksBroken[s] = Support[s] < 20 ? WeeksBroken[s] + 1 : 0;
            if (WeeksBroken[s] >= 4 && Loser < 0) Loser = s;
        }
        // ours, in full
        string Change(int s) => $"-{LastShock[s]:0.0}" + (LastGain[s] > .05 ? $", +{LastGain[s]:0.0} for successes" : "");
        lines.Add(($"War support {Support[1]:0} ({Change(1)}{(why[1].Count > 0 ? ": " + string.Join(", ", why[1]) : "")}). Turnout at work {Turnout(true) * 100:0}%.", Support[1] < 40));
        // theirs, as intelligence has it: the error narrows a little each week the UCS is watched, and the estimate lags
        EstimateSpread = Math.Max(6, EstimateSpread * .95); UcsEstimate = Math.Clamp(Support[0] + (rng.NextDouble() - .5) * EstimateSpread * 2, 0, 100);
        int lo = (int)Math.Max(0, Math.Round((UcsEstimate - EstimateSpread) / 5) * 5), hi = (int)Math.Min(100, Math.Round((UcsEstimate + EstimateSpread) / 5) * 5);
        double works = ind.Works.Where(q => !q.Wolf).Sum(q => q.Points * fire.Plant(q.City)) / ind.Works.Where(q => !q.Wolf).Sum(q => q.Points);
        lines.Add(($"Intelligence: UCS war support {lo}-{hi}{(why[0].Count > 0 ? " after " + string.Join(", ", why[0]) : "")}; its works at about {Math.Round(works * 20) * 5:0}% of full.", false));
        if (Loser == 0) lines.Add(("The UCS asks for terms. The war is won.", true));
        else if (Loser == 1) lines.Add(("The Directorate asks for terms. The war is lost.", true));
        Array.Clear(BombersDowned); Array.Clear(CrewsLost);
        return lines;
    }
}
