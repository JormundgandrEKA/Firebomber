using System;
using System.Collections.Generic;
using System.Linq;

namespace Firebomber;

public sealed class PilotRec { public string Name = ""; public double Skill, Aggression, Hunter; public int Career, Sorties; }
public sealed class CaptainRec { public string Name = ""; public double Nerve; public int Sorties; }

/// <summary>The people, who outlast any one raid. Aircraft are repaired or replaced between raids; crews carry over. What happened to each crew member who went down is counted here.</summary>
public sealed class Roster
{
    public readonly int[] Dead = new int[2], Recovered = new int[2], Prisoners = new int[2], LostAtSea = new int[2];      // index 1 is Vulfen
    public readonly List<PilotRec> WolfPilots = new(), UcsPilots = new(); public readonly List<CaptainRec> WolfCaptains = new(), UcsCaptains = new();
    public int Raid = 1;
    public void Clear() { Array.Clear(Dead); Array.Clear(Recovered); Array.Clear(Prisoners); Array.Clear(LostAtSea); }
    public int CaptainsLost;

    /// <summary>What becomes of the crew of an aircraft that has gone down. Returns whether the first crew member (pilot or captain) is back with their side.</summary>
    public bool Down(Hull h, World w, Random r, double x, double y, double z, bool wolf, string cause)
    {
        int side = wolf ? 1 : 0; bool first = false;
        bool sea = w.Land(x, y, 256) < 0 || w.InLake(x, y, 256), ownSoil = World.North(x, y) == wolf;
        for (int i = 0; i < h.CrewState.Length; i++)
        {
            if (h.CrewState[i] == 2) { Dead[side]++; continue; }
            double getOut = cause == "ram" ? .1 : cause == "ditched" ? .85 : z < 300 ? .2 : h.D.IsFighter ? .7 : .6;
            if (h.CrewState[i] == 1) getOut -= .2;
            if (r.NextDouble() > getOut) { Dead[side]++; continue; }
            if (sea) { if (r.NextDouble() < .45) { Recovered[side]++; if (i == 0) first = true; } else LostAtSea[side]++; }
            else if (ownSoil) { Recovered[side]++; if (i == 0) first = true; }
            else if (r.NextDouble() < .9) Prisoners[side]++;
            else { Recovered[side]++; if (i == 0) first = true; }       // a few walk home
        }
        return first;
    }
}
