using System;
using System.Collections.Generic;
using System.Linq;

namespace Firebomber;

/// <summary>A gun. A hit normally only tears the airframe; a critical hit reaches something that matters. The T-15's critical chance and damage are about five times the KA-31's, its basic accuracy comes from the pilot.</summary>
/// <summary>VsSmall: how much more often it hits a fighter-sized target. KillSmall: how much more each hit does to a light airframe (120 or less).</summary>
public sealed record Weapon(string Name, double Mm, double Rpm, double Dmg, double Crit, double CritDmg, double Acc = 1, double VsLarge = 1, double VsSmall = 1, double KillSmall = 1)
{
    // fast-firing wing guns spread their fire: poor for working on one big aeroplane. Against a light fighter the high-explosive shell is the point: the Hund hits less often than the swarm shoots, but what it hits mostly goes down
    public static readonly Weapon T15 = new("T-15 15 mm revolver cannon", 15, 1200, 3.3, .15, 9, .28, 1.4, 1.7, 4);
    public static readonly Weapon KA31 = new("KA-31 13 mm", 13, 600, 3.2, .06, 6, 1, .75, 3);
    public static readonly Weapon T12 = new("T-12 12 mm", 12, 800, 2.2, .06, 7);
    /// <summary>The I-7's nose: a 37 mm cannon and two heavy machine guns, counted as three barrels of one battery. Made for bombers.</summary>
    public static readonly Weapon ShV = new("ShV 37 mm and two 13 mm", 37, 600, 3.3, .1, 12, 1, 1.15, 1.2);      // was 4.5 and 1.35: the designer found the I-7 overpowered (8 Nov)
}
/// <summary>Arc: Center and Half in degrees from the nose (0 ahead, 180 astern). Top turrets fire at targets above, the others below, and all at targets level.</summary>
public sealed record TurretSpec(string Name, int Guns, bool Top, int RoundsEach, Weapon W, double Center = 0, double Half = 180);

/// <summary>Everything that makes one type of aircraft what it is. All numbers except those the designer set are first suggestions. Fuel is in seconds of cruise.</summary>
public sealed class Design
{
    public string Like = ""; public string Name = ""; public bool Wolf, IsFighter; public double Cruise, Max, Ceil, Climb, TurnDeg, Alt, Struct, EngHp, PilotArmor, FireOut, Fuel, Aim = 1, GunSkill = .85, Redundancy = 1, EngShare = .3;
    public int Engines, Box = 24, Bombs; public bool SelfSeal; public string[] Crew = { }; public TurretSpec[] Turrets = { }; public Weapon? Fixed; public int FixedN, FixedRounds; public string Note = "";

    public double MaxKg, UsualKg, SpeedPen, CeilPen = .35; public int MaxBomb = 1000, Slots = 30; public (double kg, double km)[] RangeAt = { }; public bool Dash;
    /// <summary>How much of an attacker's fire a nimble aircraft throws off by jinking, clean and sound. Bombs and a dead engine take it away.</summary>
    public double Dodge;
    /// <summary>A strike bomber: fast or nimble, lightly built, and meant to slip fighters rather than soak up their fire. It breaks against attacks.</summary>
    public bool Strike;
    /// <summary>A jink needs room: alone or in a small group the whole of Dodge, in a big stream less.</summary>
    public double DodgeNow(int company) => Dodge * Math.Clamp(1.15 - company / 120.0, .55, 1);
    // the V-16's tail masks the top turrets' rear arc; the lower turrets see all round
    static TurretSpec[] V16T() => new[] { new TurretSpec("top forward", 2, true, 800, Weapon.T12, 0, 155), new TurretSpec("top rear", 2, true, 800, Weapon.T12, 0, 155), new TurretSpec("lower forward", 2, false, 800, Weapon.T12), new TurretSpec("lower rear", 2, false, 800, Weapon.T12) };
    public static readonly Dictionary<Craft, Design> Of = new()
    {
        [Craft.V16] = new() { Name = "V-16 Silverhair", Wolf = true, Cruise = 125, Max = 145, Ceil = 9500, Climb = 5.5, TurnDeg = 1, Alt = 6000, Struct = 170, Engines = 4, EngHp = 22, PilotArmor = .2, SelfSeal = true, FireOut = .035, Fuel = 37500, Box = 24, GunSkill = .85,
            MaxKg = 6500, UsualKg = 5000, SpeedPen = .12, MaxBomb = 1000, Slots = 28, RangeAt = new[] { (0.0, 5000.0), (3000.0, 4200.0), (5000.0, 2600.0), (6500.0, 1400.0) },
            Crew = new[] { "Pilot", "Co-pilot", "Bomb aimer", "Navigator", "Engineer", "Gunner", "Gunner", "Gunner", "Gunner" }, Turrets = V16T(), Note = "Self-sealing tanks. Built light for speed and height. The heavy lifter." },
        [Craft.U4] = new() { Name = "U-4 Doppel", Wolf = true, Cruise = 133, Max = 178, Ceil = 7500, Climb = 7, TurnDeg = 3, Alt = 5000, Struct = 70, Engines = 2, EngHp = 18, PilotArmor = .3, SelfSeal = true, FireOut = .04, Fuel = 16000, Box = 12, Aim = .6, GunSkill = .85, EngShare = .55, Dash = true, Dodge = .75, Strike = true,
            MaxKg = 2000, UsualKg = 1000, SpeedPen = .45, MaxBomb = 1000, Slots = 6, RangeAt = new[] { (0.0, 2800.0), (500.0, 2400.0), (1000.0, 1600.0), (2000.0, 700.0) },
            Crew = new[] { "Pilot", "Navigator" }, Fixed = Weapon.T15, FixedN = 6, FixedRounds = 400, Note = "Fast, and hard to hit once its bombs are gone; loaded, it can barely manoeuvre. Big engines that take most of the hits. Forward cannon for self-defence, no turrets. At its best in small groups." },
        [Craft.F5] = new() { Name = "F-5 Hund", Wolf = true, IsFighter = true, Cruise = 135, Max = 185, Ceil = 12000, Climb = 16, Struct = 28, Engines = 1, EngHp = 26, PilotArmor = .6, SelfSeal = true, FireOut = .03, Fuel = 12600, Redundancy = .6, EngShare = .25,
            Crew = new[] { "Pilot" }, Fixed = Weapon.T15, FixedN = 4, FixedRounds = 300, Note = "Light, but with redundant systems, pilot armour and self-sealing tanks. Meant not to get hit." },
        [Craft.TB6] = new() { Name = "TB-6", Cruise = 92, Max = 108, Ceil = 7000, Climb = 3, TurnDeg = .8, Alt = 5000, Struct = 320, Engines = 4, EngHp = 30, PilotArmor = .1, SelfSeal = false, FireOut = .02, Fuel = 30000, Box = 18, GunSkill = .85, Aim = 1.5,
            MaxKg = 4000, UsualKg = 3000, SpeedPen = .1, CeilPen = .3, MaxBomb = 1000, Slots = 26, RangeAt = new[] { (0.0, 3400.0), (2000.0, 2800.0), (3000.0, 2000.0), (4000.0, 1000.0) },
            Crew = new[] { "Pilot", "Co-pilot", "Bomb aimer", "Navigator", "Engineer", "Gunner", "Gunner", "Gunner", "Gunner", "Gunner" },
            Turrets = new[] { new TurretSpec("nose", 1, true, 500, Weapon.KA31, 0, 70), new TurretSpec("top", 2, true, 500, Weapon.KA31, 0, 150), new TurretSpec("belly", 1, false, 500, Weapon.KA31, 180, 100), new TurretSpec("waist", 1, false, 500, Weapon.KA31, 180, 130), new TurretSpec("tail", 2, true, 500, Weapon.KA31, 180, 60) }, Note = "No self-sealing tanks. Solid, but slow and low, and it burns. Many ways of carrying bombs." },
        [Craft.SB3] = new() { Name = "SB-3", Cruise = 110, Max = 130, Ceil = 7800, Climb = 4.5, TurnDeg = 1.8, Alt = 5500, Struct = 120, Engines = 2, EngHp = 16, PilotArmor = .1, SelfSeal = false, FireOut = .025, Fuel = 17000, Box = 36, GunSkill = .7, Aim = 1.3, EngShare = .35, Dodge = .3, Strike = true,
            MaxKg = 1800, UsualKg = 1250, SpeedPen = .1, CeilPen = .3, MaxBomb = 250, Slots = 16, RangeAt = new[] { (0.0, 2200.0), (1250.0, 1700.0), (1800.0, 900.0) },
            Crew = new[] { "Pilot", "Navigator", "Gunner" }, Turrets = new[] { new TurretSpec("nose", 1, true, 400, Weapon.KA31, 0, 50), new TurretSpec("top", 1, true, 400, Weapon.KA31, 180, 130), new TurretSpec("belly", 1, false, 400, Weapon.KA31, 180, 70) }, Note = "Light and cheap, built in numbers. No self-sealing tanks. It breaks away from attacks and splits up when a box is being cut down. A small bomb bay: nothing over 250 kg." },
        [Craft.I8] = new() { Name = "I-8", IsFighter = true, Cruise = 112, Max = 138, Ceil = 8500, Climb = 12, Struct = 40, Engines = 1, EngHp = 22, PilotArmor = .1, SelfSeal = false, FireOut = .015, Fuel = 5400, EngShare = .3,
            Crew = new[] { "Pilot" }, Fixed = Weapon.KA31, FixedN = 2, FixedRounds = 400, Note = "The interceptor: simple and cheap, built in great numbers, and tight in a turn. Light guns that do little to a heavy bomber, but they last. Its job is the escort." },
        [Craft.I7] = new() { Name = "I-7", IsFighter = true, Cruise = 118, Max = 158, Ceil = 10000, Climb = 10, Struct = 100, Engines = 2, EngHp = 22, PilotArmor = .25, SelfSeal = true, FireOut = .03, Fuel = 11000, EngShare = .35,
            Crew = new[] { "Pilot", "Gunner" }, Turrets = new[] { new TurretSpec("rear", 1, true, 300, Weapon.KA31, 180, 70) }, Fixed = Weapon.ShV, FixedN = 3, FixedRounds = 250,
            Note = "A two-seat heavy fighter after the Ki-45 and Pe-3: fast for the UCS, high, long-legged, a heavy nose battery and one gunner looking back. It turns poorly. It escorts the bombers, and goes for ours." },
    };
}

public sealed class Turret { public TurretSpec S = null!; public double Rounds; public int Gunner = -1; }

/// <summary>One aircraft's body: airframe, engines, tanks, fire, crew, guns. The same for every type; the numbers come from its Design.</summary>
public sealed class Hull
{
    public readonly Design D; public double Struct, Fuel, Leak, Ammo, LastHit = -1e9; public readonly double[] Eng; public readonly int[] CrewState; public readonly List<Turret> Turrets = new();
    public bool Burning, BurnTank, Dry; public string Note = ""; public string Cause = "";
    /// <summary>What hit it last: "fighters" or "flak". A bomber holed by flak and finished by a fighter counts to the fighter.</summary>
    public string By = "";
    /// <summary>For the combat table: how many separate fighter bursts have hit it, and which burst hit it last.</summary>
    public int Bursts, BurstKey = -1;
    public string CauseOr => Cause == "" ? "airframe" : Cause;
    public Hull(Design d)
    {
        D = d; Struct = d.Struct; Fuel = d.Fuel; Ammo = d.FixedN * d.FixedRounds; Eng = Enumerable.Repeat(d.EngHp, d.Engines).ToArray(); CrewState = new int[d.Crew.Length];
        var gunners = Enumerable.Range(0, d.Crew.Length).Where(i => d.Crew[i] == "Gunner").ToList(); int k = 0;
        foreach (var t in d.Turrets) Turrets.Add(new Turret { S = t, Rounds = t.Guns * t.RoundsEach, Gunner = gunners.Count == 0 ? -1 : gunners[k++ % gunners.Count] });
    }
    public int Working => Dry ? 0 : Eng.Count(e => e > 0);
    public double Damage => Math.Clamp(1 - Struct / D.Struct, 0, 1);
    public bool Fit(string role) { for (int i = 0; i < CrewState.Length; i++) if (D.Crew[i] == role && CrewState[i] < 2) return true; return false; }
    public bool Flyable => Fit("Pilot") || Fit("Co-pilot") || (D.Engines > 1 && Fit("Navigator"));      // on a bomber someone else can take the controls
    public bool Dead => Struct <= 0 || !Flyable;
    public int CrewDead => CrewState.Count(s => s == 2); public int CrewHurt => CrewState.Count(s => s == 1);
    /// <summary>Share of full power. Four engines fly on two but slowly; two engines barely limp on one; a fighter needs its one.</summary>
    public double Power { get { int n = D.Engines, w = Working; return n == 4 ? new[] { 0, .45, .72, .9, 1 }[w] : n == 2 ? new[] { 0, .62, 1 }[w] : w; } }
    /// <summary>Metres a second it must lose, however hard it is flown.</summary>
    public double Sink { get { int n = D.Engines, w = Working; return w == 0 ? 8 : n == 4 && w == 1 ? 1.2 : n == 2 && w == 1 ? .25 : 0; } }
    public double RoofShare { get { int n = D.Engines, w = Working; return n == 4 && w == 2 ? .6 : 1; } }

    public void Take(Weapon w, int hits, Random r, double T, double critMul = 1)
    {
        LastHit = T;
        for (int h = 0; h < hits && Struct > 0; h++)
        {
            double big = D.Struct > 120 ? w.VsLarge : 1, small = D.Struct <= 120 ? w.KillSmall : 1;      // a light airframe (120 or less) is what a high-explosive shell does most to
            if (r.NextDouble() >= w.Crit * big * critMul) { Struct -= w.Dmg * big * small * (.6 + .8 * r.NextDouble()); continue; }
            double q = r.NextDouble(), eng = D.EngShare, tank = eng + .2, crew = tank + .22;
            if (q < eng) HitEngine(w, r, small);
            else if (q < tank) HitTank(r);
            else if (q < crew) HitCrew(r);
            else Struct -= w.CritDmg * D.Redundancy * small;
        }
        if (Struct <= 0) Cause = "airframe";
    }
    void HitEngine(Weapon w, Random r, double small = 1)
    {
        var live = Enumerable.Range(0, Eng.Length).Where(i => Eng[i] > 0).ToList(); if (live.Count == 0) return;
        int i = live[r.Next(live.Count)]; Eng[i] -= w.CritDmg * small * (D.Engines == 1 ? D.Redundancy : 1);
        if (Eng[i] <= 0) Note = D.Engines == 1 ? "engine dead" : $"engine {i + 1} out";
        if (!Burning && r.NextDouble() < .1) { Burning = true; BurnTank = false; Note = $"engine fire"; }
    }
    void HitTank(Random r)
    {
        if (D.SelfSeal)
        {
            if (r.NextDouble() < .2) { Leak += .4; Note = "tank holed, sealing"; }
            if (!Burning && r.NextDouble() < .015) { Burning = true; BurnTank = true; Note = "fuel fire"; }
        }
        else
        {
            Leak += 1.5 + r.NextDouble() * 3; Note = "tank holed, losing fuel";
            if (!Burning && r.NextDouble() < .16) { Burning = true; BurnTank = true; Note = "fuel fire"; }
        }
    }
    void HitCrew(Random r)
    {
        var live = Enumerable.Range(0, CrewState.Length).Where(i => CrewState[i] < 2).ToList(); if (live.Count == 0) return;
        int i = live[r.Next(live.Count)]; string role = D.Crew[i];
        if ((role == "Pilot" || role == "Co-pilot") && r.NextDouble() < D.PilotArmor) { Note = "armour stopped a hit on the pilot"; return; }
        if (CrewState[i] == 1 || r.NextDouble() < .35) { CrewState[i] = 2; Note = role.ToLowerInvariant() + " killed"; }
        else { CrewState[i] = 1; Note = role.ToLowerInvariant() + " wounded"; }
        if (!Flyable) Cause = "crew";
    }
    /// <summary>One second passes. A fire eats the airframe until the crew or the design gets it out; a holed tank drains; a self-sealing one closes up.</summary>
    public void Second(Random r, double burn)
    {
        if (Burning)
        {
            double unsealed = BurnTank && !D.SelfSeal ? 1.6 : 1;
            Struct -= D.Struct / 80 * unsealed;
            if (!BurnTank) { var i = Array.FindIndex(Eng, e => e > 0); if (i >= 0) Eng[i] -= D.EngHp / 40; }
            double outChance = D.FireOut * (D.Engines > 1 && !Fit("Engineer") && D.Crew.Contains("Engineer") ? .5 : 1) / unsealed;
            if (r.NextDouble() < outChance) { Burning = false; Note = "fire out"; }
            if (Struct <= 0) Cause = "fire";
        }
        Fuel -= burn + Leak; if (D.SelfSeal) Leak *= .97;
        if (Fuel <= 0 && !Dry) { Dry = true; Fuel = 0; Note = "out of fuel"; }
    }
}
