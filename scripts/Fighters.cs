using System;
using System.Collections.Generic;
using System.Linq;

namespace Firebomber;

public enum Act { Ground, Climb, Hunt, Stalk, Patrol, Cover, Follow, AttackBomber, Fight, Evade, Extend, Zoom, Home, Ditch, Landed, Down, Form }
/// <summary>How to go at a bomber. Too slow to catch it: meet it head-on. Much faster than it: dive through from above. Otherwise from astern.</summary>
public enum Pass { Stern, HeadOn, Slash }

public sealed class Perf
{
    public double VMax, VCruise, VStall, NMax, Climb, Accel, Ceil, Range;
    public static readonly Perf Hund = new() { VMax = 185, VCruise = 135, VStall = 66, NMax = 3.4, Climb = 16, Accel = 3.8, Ceil = 12000, Range = 550 };   // fast, heavy guns, poor in a turning fight, long legs on drop tanks
    /// <summary>After the Ki-45 and Pe-3: fast for the UCS (enough to run down a U-4 with its bombs aboard, not one without), high and long-legged, heavy in the nose, but it turns like the twin it is.</summary>
    public static readonly Perf I7 = new() { VMax = 158, VCruise = 118, VStall = 60, NMax = 3.6, Climb = 10, Accel = 3.0, Ceil = 10000, Range = 520 };
    public static readonly Perf I8 = new() { VMax = 138, VCruise = 112, VStall = 48, NMax = 6, Climb = 12, Accel = 3.2, Ceil = 8500, Range = 450 };          // slow, tough, turns tightly, weak guns, short legs
}

public sealed class Fighter
{
    public int Sorties; public int Id, KillsB, KillsF; public bool Wolf, Alive = true; public Craft Kind; public string Name = ""; public Perf P = null!;
    public bool Back; public Hull H = null!; public Fighter? LastBy; public bool Defender; public double Fuel { get => H.Fuel; set => H.Fuel = value; } public double Ammo { get => H.Ammo; set => H.Ammo = value; }
    public double TurnAvg, X, Y, Z, Th, V, HomeX, HomeY, Skill, Aggression, Hunter;
    public Act Act = Act.Ground; public string Why = ""; public double NextThink, ScrambleAt = double.NaN, Until, GoX, GoY, GoZ, LastHit = -1e9, LastGun = -1e9, IdleSince;
    public int BurstId; public double BurstT; public string ActAtDeath = "";
    /// <summary>The squadron's use for this pilot, its place in the line, and when it last finished a pass at a bomber.</summary>
    public Role Role; public int Slot, Passes; public double LastPass = -1e9;
    public Pass Pass; public int Stage; public double StalkSince = double.NaN; public Bomber? TB; public Fighter? TF, Leader; public Formation? Charge; public int Side = 1; public double SlotA, SlotB;
    public string HomeName = ""; public Wing? W; public Mood Mood; public double Rear, Skill0; public int Career;
    public bool Ace => Career + KillsB + KillsF >= 5;
    /// <summary>A patrol's station, if this fighter is on one, and how it goes about interception.</summary>
    public double StationX = double.NaN, StationY, StationZ; public InterceptMode Mode;
    public bool OnStation => !double.IsNaN(StationX);
    public double Damage => H.Damage;
    public bool Airborne => Alive && Act != Act.Ground && Act != Act.Landed && Act != Act.Down;
}
/// <summary>Fighters to put into an operation: so many of a type at a field, on alert there, or escorting a box.</summary>
public sealed record Garrison(Place Field, Craft Kind, int N, bool Wolf, Formation? Escort = null, Patrol? On = null, InterceptMode Mode = InterceptMode.Direct);
public sealed class Wreck { public double X, Y, Z, Th, V, Spin; public Craft Kind; public bool Big; }
public struct Tracer { public double X1, Y1, X2, Y2; public bool Wolf, Hit; }

/// <summary>Fighters as individual pilots. This half is the aircraft: flight that trades speed for height, gunnery that depends on range and deflection, damage, gunners on the bombers, wrecks, scrambles.
/// The other half (FighterAI.cs) is what each pilot decides to do. No drawing here.</summary>
public sealed partial class FighterSim
{
    const double Dt = Air.Dt, G = 9.81;
    public readonly List<Fighter> All = new(); public readonly List<Wreck> Wrecks = new(); public readonly List<Tracer> Tracers = new();
    public Action<double, double>? Crashed;
    public int UcsLost, WolfLost, BombersDownByFighters, EscortCount, UcsCount, GunnerKills;
    public double SecsOnBombers, SecsOnFighters;
    public double AlertT = double.NaN;
    World w; Air air; Random rng; double gunT, hullT;
    public int Ditched;
    /// <summary>For the combat table. Hitting bursts each aircraft took before fighters brought it down, by type; and per shooter type: bursts fired, bursts that hit, hits, seconds firing.</summary>
    public readonly Dictionary<Craft, List<int>> KillBursts = new(); public readonly Dictionary<Craft, double[]> Gunnery = new(); int burstSeq;
    double[] Gun(Craft k) { if (!Gunnery.TryGetValue(k, out var g)) Gunnery[k] = g = new double[4]; return g; }
    void Killed(Craft k, Hull h) { if (h.Bursts == 0) return; if (!KillBursts.TryGetValue(k, out var l)) KillBursts[k] = l = new(); l.Add(h.Bursts); }
    double T => air.T;
    static double Wrap(double a) => ((a + Math.PI) % (2 * Math.PI) + 2 * Math.PI) % (2 * Math.PI) - Math.PI;
    static double Dist(double ax, double ay, double bx, double by) => Math.Sqrt((ax - bx) * (ax - bx) + (ay - by) * (ay - by));
    static readonly string[] WolfNames = { "Holm", "Arvid", "Lund", "Berg", "Falk", "Ulven", "Strand", "Hauk", "Dahl", "Rask", "Vik", "Ek", "Norr", "Sten", "Hagen", "Moe" }, DeerNames = { "Petrov", "Orlov", "Kuzmin", "Sokol", "Volkov", "Belov", "Lebed", "Morozov", "Zaitsev", "Rogov", "Oleni", "Gusev", "Sedov", "Titov", "Yarov", "Kirov" };

    public FighterSim(World world, Air airSim, int escortsPerBox, int perField, int seed = 3, int defendersPerField = 0, int ucsEscortsPerBox = 0)
    {
        w = world; air = airSim; rng = new Random(seed); air.Stepped = Step; air.Downed = BomberDown; air.Busy = () => All.Any(f => f.Airborne);
        foreach (var fm in air.Formations.Where(q => q.Wolf))
            for (int k = 0; k < escortsPerBox; k++)
            {
                var f = Make(true, Craft.F5, Perf.Hund, fm.HomeX + (rng.NextDouble() - .5) * 1500, fm.HomeY + (rng.NextDouble() - .5) * 1500, fm.HomeName);
                f.Charge = fm; f.Th = fm.Lead.Th; double a = k / (double)Math.Max(1, escortsPerBox) * Math.Tau; f.SlotA = Math.Cos(a) * 1300; f.SlotB = Math.Sin(a) * 900;
                if (k % 2 == 1) f.Leader = All[^2];
                EscortCount++; Enrol(f, fm.Name, true, fm);
            }
        // the UCS sends few escorts, and short-legged ones: drop tanks get them most of the way
        foreach (var fm in air.Formations.Where(q => !q.Wolf))
            for (int k = 0; k < ucsEscortsPerBox; k++)
            {
                bool heavy = k % 4 != 3;      // the I-7 has the legs for escort: three places in four
                var f = Make(false, heavy ? Craft.I7 : Craft.I8, heavy ? Perf.I7 : Perf.I8, fm.HomeX + (rng.NextDouble() - .5) * 1500, fm.HomeY + (rng.NextDouble() - .5) * 1500, fm.HomeName);
                f.Charge = fm; f.Th = fm.Lead.Th; if (!heavy) f.H.Fuel *= 1.6; double a = k / (double)Math.Max(1, ucsEscortsPerBox) * Math.Tau; f.SlotA = Math.Cos(a) * 900; f.SlotB = Math.Sin(a) * 700;
                if (k % 2 == 1) f.Leader = All[^2];
                UcsCount++; Enrol(f, fm.Name, false, fm);
            }
        foreach (var fld in w.Fields.Take(3))      // the tests' defenders: the three bomber bases, as before there were more fields
            for (int k = 0; k < defendersPerField; k++)
            {
                var f = Make(true, Craft.F5, Perf.Hund, fld.X + (rng.NextDouble() - .5) * 900, fld.Y + (rng.NextDouble() - .5) * 200, fld.Name);
                f.Defender = true; f.Th = rng.NextDouble() * Math.Tau; f.SlotA = 110; f.SlotB = -70; if (k % 2 == 1) f.Leader = All[^2];
                Enrol(f, fld.Name + " defence", true, null);
            }
        foreach (var fld in w.EnemyFields)
        {
            int n = perField * (1 + (fld.Town?.Size ?? 4) / 4);      // the tests' interceptors: as many as when the big cities had several fields
            int heavies = n / 4 / 2 * 2;      // about a quarter of each field is a squadron of I-7s, for the bombers
            for (int k = 0; k < n; k++)
            {
                bool heavy = k < heavies;
                var f = Make(false, heavy ? Craft.I7 : Craft.I8, heavy ? Perf.I7 : Perf.I8, fld.X + (rng.NextDouble() - .5) * 900, fld.Y + (rng.NextDouble() - .5) * 200, fld.Name);
                f.Th = rng.NextDouble() * Math.Tau; f.SlotA = (k % 2 == 1 ? -1 : 1) * 110; f.SlotB = -70;
                if (k % 2 == 1) f.Leader = All[^2];
                UcsCount++; Enrol(f, heavy ? fld.Name + " heavies" : fld.Name, false, null);
            }
        }
    }
    public static Perf PerfOf(Craft k) => k == Craft.F5 ? Perf.Hund : k == Craft.I7 ? Perf.I7 : Perf.I8;
    /// <summary>The campaign's operation: exactly the fighters the fields hold, as escorts to named boxes and on alert at their fields.</summary>
    public FighterSim(World world, Air airSim, IList<Garrison> garrison, int seed)
    {
        w = world; air = airSim; rng = new Random(seed); air.Stepped = Step; air.Downed = BomberDown; air.Busy = () => All.Any(f => f.Airborne);
        foreach (var g in garrison.Where(q => q.Escort != null))
        {
            var fm = g.Escort!;
            for (int k = 0; k < g.N; k++)
            {
                var f = Make(g.Wolf, g.Kind, PerfOf(g.Kind), fm.HomeX + (rng.NextDouble() - .5) * 1500, fm.HomeY + (rng.NextDouble() - .5) * 1500, g.Field.Name);
                f.HomeX = g.Field.X + (f.X - fm.HomeX); f.HomeY = g.Field.Y + (f.Y - fm.HomeY);
                f.Charge = fm; f.Th = fm.Lead.Th; if (g.Kind == Craft.I8) f.H.Fuel *= 1.6; double a = k / (double)Math.Max(1, g.N) * Math.Tau; f.SlotA = Math.Cos(a) * (g.Wolf ? 1300 : 900); f.SlotB = Math.Sin(a) * (g.Wolf ? 900 : 700);
                if (k % 2 == 1) f.Leader = All[^2];
                if (g.Wolf) EscortCount++; else UcsCount++; Enrol(f, fm.Name, g.Wolf, fm);
            }
        }
        foreach (var g in garrison.Where(q => q.Escort == null))
            for (int k = 0; k < g.N; k++)
            {
                var f = Make(g.Wolf, g.Kind, PerfOf(g.Kind), g.Field.X + (rng.NextDouble() - .5) * 900, g.Field.Y + (rng.NextDouble() - .5) * 200, g.Field.Name);
                f.Th = rng.NextDouble() * Math.Tau; f.SlotA = (k % 2 == 1 ? -1 : 1) * 110; f.SlotB = -70; if (k % 2 == 1) f.Leader = All[^2];
                f.Mode = g.Mode;
                if (g.On != null) { f.StationX = g.On.X + (rng.NextDouble() - .5) * 6000; f.StationY = g.On.Y + (rng.NextDouble() - .5) * 6000; f.StationZ = g.Wolf ? 6500 : 5000; if (g.Wolf) f.Defender = true; if (!g.Wolf) UcsCount++; Enrol(f, g.Field.Name + " patrol", g.Wolf, null); continue; }
                if (g.Wolf) { f.Defender = true; Enrol(f, g.Field.Name + " defence", true, null); }
                else { UcsCount++; Enrol(f, g.Kind == Craft.I7 ? g.Field.Name + " heavies" : g.Field.Name, false, null); }
            }
    }
    Fighter Make(bool wolf, Craft kind, Perf p, double x, double y, string home)
    {
        var names = wolf ? WolfNames : DeerNames; var queue = wolf ? air.Crews.WolfPilots : air.Crews.UcsPilots;
        var f = new Fighter { Id = All.Count, Wolf = wolf, Kind = kind, P = p, X = x, Y = y, HomeX = x, HomeY = y, HomeName = home, H = new Hull(Design.Of[kind]), V = p.VCruise, Name = names[rng.Next(names.Length)],
            Hunter = (rng.NextDouble() - .5) * .9, NextThink = air.T + rng.NextDouble() * 2 };
        if (wolf)
        {
            // few, put together and pragmatic: everyone is good, and nobody is reckless
            f.Skill = .95 + .3 * rng.NextDouble(); f.Aggression = .35 + .3 * rng.NextDouble(); f.Career = rng.NextDouble() < .35 ? rng.Next(1, 7) : 0;
        }
        else
        {
            // a mixed bag: a mass of green pilots, some competent, and the few who have lived long enough to be dangerous. All gung-ho.
            double r = rng.NextDouble(); f.Aggression = .5 + .5 * rng.NextDouble();
            if (r < .7) f.Skill = .25 + .3 * rng.NextDouble();
            else if (r < .95) { f.Skill = .6 + .25 * rng.NextDouble(); f.Career = rng.NextDouble() < .3 ? rng.Next(1, 4) : 0; }
            else { f.Skill = 1 + .3 * rng.NextDouble(); f.Career = rng.Next(5, 22); }
        }
        if (queue.Count > 0) { var q = queue[0]; queue.RemoveAt(0); f.Name = q.Name; f.Skill = q.Skill; f.Aggression = q.Aggression; f.Hunter = q.Hunter; f.Career = q.Career; f.Sorties = q.Sorties; }
        f.Skill0 = f.Skill; All.Add(f); return f;
    }

    void Enrol(Fighter f, string name, bool wolf, Formation? box)
    {
        var w = Wings.FirstOrDefault(q => q.Name == name && q.Wolf == wolf);
        if (w == null) { w = new Wing { Name = name, Wolf = wolf, Box = box }; Wings.Add(w); }
        w.Pilots.Add(f); f.W = w;
    }
    void Step()
    {
        if (air.Formations.Count == 0) return;
        Alert(); Moralestep(); Tactics();
        hullT -= Dt; bool hullTick = hullT <= 0; if (hullTick) hullT = 1;
        foreach (var f in All)
        {
            if (!f.Alive) continue;
            if (f.Act == Act.Ground) { Ground(f); continue; }
            if (f.Act == Act.Landed) { Turnaround(f); continue; }
            if (hullTick)
            {
                f.H.Second(rng, f.Act is Act.Fight or Act.AttackBomber or Act.Extend or Act.Evade or Act.Zoom ? 1.5 : 1);
                if (f.H.Dead) { Kill(f, null); continue; }
                if (f.H.Working == 0 && f.Act != Act.Ditch) { f.Act = Act.Ditch; f.TB = null; f.TF = null; f.Why = f.H.Dry ? "out of fuel: ditching" : "engine dead: ditching"; }
            }
            if (f.Act == Act.Ditch)
            {
                f.Z -= 9 * Dt; f.V = Math.Max(f.P.VStall, f.V - 2 * Dt); f.X += Math.Cos(f.Th) * f.V * Dt; f.Y += Math.Sin(f.Th) * f.V * Dt;
                if (f.Z < 30) { Ditched++; Kill(f, null, false); }
                continue;
            }
            if (T >= f.NextThink) Think(f);
            Move(f);
            if (f.Act == Act.AttackBomber) SecsOnBombers += Dt; else if (f.Act == Act.Fight && !f.Wolf) SecsOnFighters += Dt;
        }
        gunT -= Dt;
        if (gunT <= 0)
        {
            gunT = .5; Gunners(); RearGunners();
            // how many are already on each bomber: nobody wants to queue behind five others
            foreach (var fm in air.Formations) { fm.Attackers = 0; foreach (var a in fm.Ac) a.Pressed = 0; }
            foreach (var f in All) if (f.Act == Act.AttackBomber && f.TB != null) { f.TB.Pressed++; f.TB.F.Attackers++; }
            Breaks2();
        }
        for (int i = Wrecks.Count - 1; i >= 0; i--)
        {
            var d = Wrecks[i]; d.Z -= (d.Big ? 55 : 70) * Dt; d.Th += d.Spin * Dt; d.V = Math.Max(30, d.V - 3 * Dt);
            d.X += Math.Cos(d.Th) * d.V * Dt; d.Y += Math.Sin(d.Th) * d.V * Dt;
            if (d.Z <= 0) { Crashed?.Invoke(d.X, d.Y); Wrecks.RemoveAt(i); }
        }
        if (Tracers.Count > 400) Tracers.RemoveRange(0, Tracers.Count - 400);
    }

    // ---------------------------------------------------------------- the air: speed, height and turning are one budget
    /// <summary>One step towards a point. Tight turns need speed and cost speed; climbing costs speed; diving gains it. So a fast aircraft can leave a fight and a slow one cannot.</summary>
    void Fly(Fighter f, double tx, double ty, double tz, double throttle)
    {
        var p = f.P;
        double n = Math.Min(p.NMax * Math.Clamp(.6 + .4 * f.Skill, .6, 1.1), Math.Max(1.02, (f.V / p.VStall) * (f.V / p.VStall))), omega = G * Math.Sqrt(n * n - 1) / f.V;
        double err = Wrap(Math.Atan2(ty - f.Y, tx - f.X) - f.Th), m = omega * Dt, turned = Math.Clamp(err, -m, m), used = m > 0 ? Math.Abs(turned) / m : 0;
        f.Th = Wrap(f.Th + turned);
        // drag comes from turning that is sustained: small corrections to the aim from step to step cost nothing
        f.TurnAvg += (turned - f.TurnAvg) * .12; used = m > 0 ? Math.Min(1, Math.Abs(f.TurnAvg) / m) : 0;
        double climb = p.Climb * Math.Clamp(f.V / p.VCruise, .4, 1.25) * (1 - .6 * Math.Clamp(f.Z / p.Ceil, 0, 1) * Math.Clamp(f.Z / p.Ceil, 0, 1)), vz = Math.Clamp((Math.Min(tz, p.Ceil) - f.Z) * .25, -70, climb);
        double nu = 1 + (n - 1) * used, r = f.V / p.VMax;
        double a = p.Accel * (throttle - r * r) - G * vz / f.V - .55 * (nu * nu - 1) * r;
        f.V = Math.Clamp(f.V + a * Dt, p.VStall * .95, p.VMax * 1.3);
        f.Z = Math.Max(30, f.Z + vz * Dt);
        f.X += Math.Cos(f.Th) * f.V * Dt; f.Y += Math.Sin(f.Th) * f.V * Dt;
    }
    double Cruising(Fighter f) => f.P.VCruise / f.P.VMax * (f.P.VCruise / f.P.VMax);

    // ---------------------------------------------------------------- guns
    /// <summary>Where to point to meet the target. It is also what makes a long approach a head-on or a beam attack rather than a tail chase.</summary>
    (double x, double y) LeadPoint(Fighter f, double tx, double ty, double tvx, double tvy)
    {
        double rx = tx - f.X, ry = ty - f.Y, d = Math.Sqrt(rx * rx + ry * ry);
        if (d < 700) { double tg = d / 650; return (tx + tvx * tg, ty + tvy * tg); }      // close in: the guns' own lead
        // further out: where the two tracks meet, the shortest time t at which |r + v t| = s t. Head-on that is far sooner than a tail chase, so a fighter meets a box instead of circling a point ahead of it
        double s = Math.Max(f.V, f.P.VCruise), qa = tvx * tvx + tvy * tvy - s * s, qb = 2 * (rx * tvx + ry * tvy), qc = d * d, t = d / s, disc = qb * qb - 4 * qa * qc;
        if (Math.Abs(qa) > 1e-6 && disc >= 0) { double r1 = (-qb - Math.Sqrt(disc)) / (2 * qa), r2 = (-qb + Math.Sqrt(disc)) / (2 * qa); double tt = r1 > 0 && r2 > 0 ? Math.Min(r1, r2) : Math.Max(r1, r2); if (tt > 0) t = tt; }
        t = Math.Min(t, 45);
        return (tx + tvx * t, ty + tvy * t);
    }
    void Fire(Fighter f, Hull th, double tx, double ty, double tz, double lx, double ly, double tvx, double tvy, double size, bool evading, Action<Weapon, int> onHit)
    {
        var gun = f.H.D.Fixed!; if (f.Ammo <= 0) return;
        double d = Dist(f.X, f.Y, tx, ty), dz = Math.Abs(f.Z - tz);
        // a trained pilot waits until the range is right; a green one opens fire far too soon and wastes it
        double opens = f.Wolf ? 1 : Math.Clamp(1.35 - .55 * f.Skill, .75, 1.25);
        bool big = size >= 1;      // a bomber, not a fighter: a big target, worth a burst from further out and a little further off the nose
        if (d > f.P.Range * opens * (big ? 1.2 : 1) || dz > 45 + d * .1) return;
        double off = Math.Abs(Wrap(Math.Atan2(ly - f.Y, lx - f.X) - f.Th)); if (off > (big ? .16 : .12)) return;      // near enough on the nose to be worth a burst; whether it hits is below
        var gs = Gun(f.Kind); if (T - f.LastGun > 1 || T - f.BurstT > 1.5) { f.BurstId = ++burstSeq; f.BurstT = T; gs[0]++; } gs[3] += Dt;      // a burst: up to a second and a half on the trigger
        double rounds = Math.Min(f.Ammo, f.H.D.FixedN * gun.Rpm / 60 * Dt); f.Ammo -= rounds; f.LastGun = T;
        // the faster the target crosses the line of fire, the harder it is to hit
        double mvx = Math.Cos(f.Th) * f.V, mvy = Math.Sin(f.Th) * f.V, rvx = tvx - mvx, rvy = tvy - mvy, ux = (tx - f.X) / (d + 1), uy = (ty - f.Y) / (d + 1), cross = Math.Abs(rvx * uy - rvy * ux);
        double defl = 1 / (1 + cross / (55 + 40 * f.Skill)), p = HitChance * gun.Acc * (size < 1 ? gun.VsSmall : 1) * (1 - .5 * air.MurkAt(f.X, f.Y)) * f.Skill * size * Math.Max(.05, 1.05 - d / f.P.Range) * defl * (evading ? .45 : 1) * (f.H.CrewState[0] == 1 ? .6 : 1);
        double hits = rounds * Math.Max(0, p); int h = (int)hits + (rng.NextDouble() < hits - (int)hits ? 1 : 0);
        if (rng.NextDouble() < .35) Tracers.Add(new Tracer { X1 = f.X, Y1 = f.Y, X2 = tx + (rng.NextDouble() - .5) * (h > 0 ? 6 : 40), Y2 = ty + (rng.NextDouble() - .5) * (h > 0 ? 6 : 40), Wolf = f.Wolf, Hit = h > 0 });
        if (h > 0) { if (th.BurstKey != f.BurstId) { th.BurstKey = f.BurstId; th.Bursts++; gs[1]++; } gs[2] += h; onHit(gun, h); }
    }
    const double HitChance = .3;
    void HitFighter(Fighter f, Weapon w, int hits, Fighter? by)
    {
        if (!f.Alive) return;
        f.H.Take(w, hits, rng, T); f.LastHit = T; if (by != null) f.LastBy = by;
        if (f.H.Dead) Kill(f, by);
    }
    void Kill(Fighter f, Fighter? by, bool wreck = true)
    {
        if (!f.Alive) return;
        f.Alive = false; f.ActAtDeath = f.Act + (f.Act == Act.Cover && f.Charge != null && f.Charge.DashUntil > T ? " (box dashing)" : ""); f.Act = Act.Down; if (f.Wolf) WolfLost++; else UcsLost++;
        by ??= f.LastBy; if (by != null) Killed(f.Kind, f.H);
        if (by != null && by.Wolf != f.Wolf) { by.KillsF++; by.Skill = Math.Min(1.3, by.Skill + .05); if (by.W != null) { by.W.Kills++; if (!by.Wolf) Feel(by, .06); } } else GunnerKills++;
        if (f.W != null) { f.W.Lost++; if (!f.Wolf) { Feel(f, -.12); foreach (var o in Wings) if (!o.Wolf && o != f.W && !o.Broken && o.Pilots.Any(p => p.Airborne && Dist(p.X, p.Y, f.X, f.Y) < 8000)) o.Morale -= .02; } }
        f.Back = air.Crews.Down(f.H, w, rng, f.X, f.Y, f.Z, f.Wolf, f.H.CauseOr);
        if (wreck) Wrecks.Add(new Wreck { X = f.X, Y = f.Y, Z = f.Z, Th = f.Th, V = f.V * .8, Spin = (rng.NextDouble() < .5 ? -1 : 1) * (1 + rng.NextDouble() * 2), Kind = f.Kind });
    }
    /// <summary>A strike bomber's crew sees a fighter coming in and throws the aircraft about. A U-4 turns into an attack from ahead, to bring its cannon to bear, and runs from one astern;
    /// an SB-3 turns hard towards the attacker and dives. Either way the attacker's aim is spoiled. A Vulfen crew sees it coming more often.</summary>
    void Breaks2()
    {
        foreach (var f in All)
        {
            if (f.Act != Act.AttackBomber || f.TB == null || !f.TB.Alive) continue;
            var a = f.TB; var dsg = a.H.D; if (!dsg.Strike || a.Straggler || T < a.BreakUntil + 2.5 || a.F.Phase is not (Phase.Outbound or Phase.Egress or Phase.Returning)) continue;
            double d = Dist(f.X, f.Y, a.X, a.Y); if (d > 1100 || d < 150) continue;
            if (rng.NextDouble() > (a.F.Wolf ? .45 : .4) * (1 - .5 * air.MurkAt(a.X, a.Y))) continue;
            double rel = Wrap(Math.Atan2(f.Y - a.Y, f.X - a.X) - a.Th);
            a.BreakSide = rel > 0 ? 1 : -1; bool laden = dsg.Dash && a.Kg > 0; a.BreakInto = dsg.Dash && !laden && Math.Abs(rel) < .8; a.BreakUntil = T + 5 + 4 * rng.NextDouble();
            if (dsg.Dash) a.F.DashUntil = Math.Max(a.F.DashUntil, T + 60);
            // caught at the right moment, the break spoils the pass: the fighter overshoots and has to come round again
            if (rng.NextDouble() < (laden ? .25 : dsg.Dash ? .65 : .4))
            {
                if (f.Wolf) ZoomAway(f, "it broke away as I came in: round again");
                else { f.Side = rng.NextDouble() < .5 ? 1 : -1; f.Act = Act.Extend; f.Stage = -1; f.Until = T + 5 + rng.NextDouble() * 5; f.Why = "it broke away: coming round again"; f.GoZ = f.Z; }
            }
        }
    }
    /// <summary>A two-seat fighter's gunner fires at an enemy fighter sitting behind it, in its rear arc and within reach.</summary>
    void RearGunners()
    {
        foreach (var f in All)
        {
            if (!f.Airborne || f.H.Turrets.Count == 0) continue;
            var t = f.H.Turrets[0]; if (t.Rounds <= 0 || (t.Gunner >= 0 && f.H.CrewState[t.Gunner] == 2)) continue;
            Fighter? tgt = null; double bd = 450;
            foreach (var e in All)
            {
                if (e.Wolf == f.Wolf || !e.Airborne || Math.Abs(e.Z - f.Z) > 200) continue; double dd = Dist(f.X, f.Y, e.X, e.Y); if (dd >= bd) continue;
                double rel = Math.Abs(Wrap(Math.Atan2(e.Y - f.Y, e.X - f.X) - f.Th)) * 180 / Math.PI; if (rel < 180 - t.S.Half) continue;
                bd = dd; tgt = e;
            }
            if (tgt == null || rng.NextDouble() > .5) continue;
            double rounds = Math.Min(t.Rounds, t.S.Guns * t.S.W.Rpm / 60 * .5); t.Rounds -= rounds;
            double p = GunnerHit * 1.4 * (1 - .5 * air.MurkAt(tgt.X, tgt.Y)) * (1.05 - bd / 450) * (1.4 - .7 * tgt.Skill), hits = rounds * p; int h = (int)hits + (rng.NextDouble() < hits - (int)hits ? 1 : 0);
            if (rng.NextDouble() < .3) Tracers.Add(new Tracer { X1 = f.X, Y1 = f.Y, X2 = tgt.X + (rng.NextDouble() - .5) * 50, Y2 = tgt.Y + (rng.NextDouble() - .5) * 50, Wolf = f.Wolf, Hit = h > 0 });
            if (h > 0) HitFighter(tgt, t.S.W, h, f);
        }
    }
    void BomberDown(Bomber a)
    {
        if (a.H.By == "fighters") Killed(a.F.Kind, a.H);
        if (a.Z > 60) Wrecks.Add(new Wreck { X = a.X, Y = a.Y, Z = a.Z, Th = a.Th, V = a.V * .75, Spin = (rng.NextDouble() < .5 ? -1 : 1) * (.15 + rng.NextDouble() * .3), Kind = a.F.Kind, Big = true });
    }

    /// <summary>Each bomber's gunners fire at the nearest enemy fighter in reach. A turret can only fire if its gunner is fit, it has rounds, and the target is on its side of the aircraft: above for the top turrets, below for the lower ones. A tight box brings more guns to bear; a lone straggler only its own.</summary>
    void Gunners()
    {
        const double Reach = 480, Interval = .5;
        foreach (var fm in air.Formations)
        {
            if (fm.Phase is Phase.Waiting or Phase.Landed) continue;
            var near = All.Where(f => f.Wolf != fm.Wolf && f.Airborne && Dist(fm.Lead.X, fm.Lead.Y, f.X, f.Y) < 4500).ToList(); if (near.Count == 0) continue;
            foreach (var a in fm.Ac)
            {
                Fighter? tgt = null; double bd = Reach;
                foreach (var f in near) { if (!f.Alive) continue; double dd = Dist(a.X, a.Y, f.X, f.Y); if (dd < bd && Math.Abs(a.Z - f.Z) < 300) { bd = dd; tgt = f; } }
                if (tgt == null) continue;
                double dz = tgt.Z - a.Z, rel = Wrap(Math.Atan2(tgt.Y - a.Y, tgt.X - a.X) - a.Th) * 180 / Math.PI; int total = 0; Weapon? gun = null;
                double night = 1 - .6 * air.MurkAt(a.X, a.Y);
                // fixed forward guns: only if the fighter happens to be dead ahead
                if (a.H.D.Fixed != null && a.H.Ammo > 0 && Math.Abs(rel) < (T < a.BreakUntil && a.BreakInto ? 16 : 10))
                {
                    double rf = Math.Min(a.H.Ammo, a.H.D.FixedN * a.H.D.Fixed.Rpm / 60 * Interval); a.H.Ammo -= rf; gun = a.H.D.Fixed; double hf = rf * (a.H.D.Dash ? .006 : .025) * gun.Acc * night * (1.05 - bd / Reach); total += (int)hf + (rng.NextDouble() < hf - (int)hf ? 1 : 0);
                }
                foreach (var t in a.H.Turrets)
                {
                    if (t.Rounds <= 0 || (t.Gunner >= 0 && a.H.CrewState[t.Gunner] == 2)) continue;
                    if (dz > 25 && !t.S.Top || dz < -25 && t.S.Top) continue;       // the wrong side of the aircraft
                    if (Math.Abs(Wrap((rel - t.S.Center) * Math.PI / 180)) * 180 / Math.PI > t.S.Half) continue;      // masked by the airframe
                    if (rng.NextDouble() > .35) { gun ??= t.S.W; continue; }      // gunners fire in bursts
                    double rounds = Math.Min(t.Rounds, t.S.Guns * t.S.W.Rpm / 60 * Interval); t.Rounds -= rounds; gun = t.S.W;
                    double p = GunnerHit * Math.Clamp(MassFloor + fm.Company / 120.0, MassFloor, 1.2) * (1 - .7 * Math.Clamp((tgt.V - 120) / 60, 0, 1)) * night * a.H.D.GunSkill * (1.05 - bd / Reach) * (1.4 - .7 * tgt.Skill) * (tgt.H.D.Struct < 40 ? .8 : 1), hits = rounds * p;
                    total += (int)hits + (rng.NextDouble() < hits - (int)hits ? 1 : 0);
                }
                if (gun == null) continue;
                if (rng.NextDouble() < .25) Tracers.Add(new Tracer { X1 = a.X, Y1 = a.Y, X2 = tgt.X + (rng.NextDouble() - .5) * 60, Y2 = tgt.Y + (rng.NextDouble() - .5) * 60, Wolf = fm.Wolf, Hit = total > 0 });
                if (total > 0) HitFighter(tgt, gun, total, null);
            }
        }
    }
    const double GunnerHit = .014, MassFloor = 1, WarnFloor = .85;

    // ---------------------------------------------------------------- the ground: warning and scramble
    /// <summary>The UCS has no radar. Observers report a raid once it is over their side; every airfield in reach scrambles a few minutes later, a few aircraft at a time. Quantity is the doctrine.</summary>
    void Alert()
    {
        if ((int)(T * 10) % 150 != 0) return;     // every quarter of a minute
        foreach (var f in All)
        {
            if (!f.OnStation || f.Act != Act.Ground || !double.IsNaN(f.ScrambleAt)) continue;
            if (air.Formations.Any(fm => fm.Wolf != f.Wolf && fm.Phase is not (Phase.Waiting or Phase.Landed) && Dist(fm.Lead.X, fm.Lead.Y, f.StationX, f.StationY) < 250000)) f.ScrambleAt = T;
        }
        foreach (var fm in air.Formations)
        {
            if (fm.Phase is Phase.Waiting or Phase.Landed) continue;
            // the UCS has observers on its own side only: nothing is reported until a raid crosses the middle of the sea. The Vulfen have radar, and scramble only what is needed
            if (fm.Wolf && World.NorthF(fm.Lead.X, fm.Lead.Y) < 166) continue;
            bool seen = fm.Wolf || (air.RadarCovers?.Invoke(fm.Lead.X, fm.Lead.Y) ?? true);      // a raid coming in where the radar is down is only known to the observers, late      // picket boats and the observers on the island in the Fjordsee see a raid some 80 km before it crosses the middle of the sea
            foreach (var f in All)
            {
                if (f.Wolf == fm.Wolf || f.Act != Act.Ground || !double.IsNaN(f.ScrambleAt) || f.Charge != null || f.OnStation || (f.Wolf && !f.Defender)) continue;
                if (Dist(fm.Lead.X, fm.Lead.Y, f.HomeX, f.HomeY) < (f.Wolf ? (seen ? 300000 : 70000) : WarnRange * Math.Clamp(Math.Sqrt(fm.Company / 48.0), WarnFloor, 1))) { f.ScrambleAt = T + 20 + rng.NextDouble() * 70; if (double.IsNaN(AlertT)) AlertT = T; }      // the order goes out at once: crews running to their aircraft
            }
        }
    }
    /// <summary>Over its own ground a defender lands, rearms and refuels, and goes up again while the raid is still about, if the aircraft is fit to.</summary>
    void Turnaround(Fighter f)
    {
        if (!f.Defender || T < f.Until || f.Damage > .35 || f.H.Working < f.H.D.Engines) return;
        if (!air.Formations.Any(q => q.Wolf != f.Wolf && q.Phase is Phase.Outbound or Phase.Egress && Dist(q.Lead.X, q.Lead.Y, f.HomeX, f.HomeY) < 300000)) return;
        f.H.Ammo = f.H.D.FixedN * f.H.D.FixedRounds; f.H.Fuel = f.H.D.Fuel; f.H.Leak = 0; f.H.Dry = false; f.Act = Act.Climb; f.Z = 100; f.Why = "rearmed: going back up"; f.IdleSince = T; f.NextThink = T; f.Rear = 0; f.TB = null; f.TF = null;
    }
    const double WarnRange = 230000;     // every field in reach goes up: the UCS answers a raid with numbers. A big stream is heard at this range, a small group not until far closer
    void Ground(Fighter f)
    {
        // on patrol: already up when the raid comes near, somewhere on its beat, with some of its fuel gone
        if (f.OnStation && !double.IsNaN(f.ScrambleAt) && T >= f.ScrambleAt) { double a = rng.NextDouble() * Math.Tau; f.X = f.StationX + Math.Cos(a) * 8000; f.Y = f.StationY + Math.Sin(a) * 8000; f.Z = f.StationZ; f.Th = a + 1.5; f.V = f.P.VCruise; f.H.Fuel *= .75; f.Act = Act.Patrol; f.Why = "on patrol"; f.IdleSince = T; f.NextThink = T; return; }
        if (f.Charge != null) { if (f.Charge.Phase != Phase.Waiting) { f.Act = Act.Climb; f.Z = 100; f.Why = "taking off with box " + f.Charge.Name; f.NextThink = T; } }
        else if (f.Wolf) { if (f.Defender && !double.IsNaN(f.ScrambleAt) && T >= f.ScrambleAt) { f.Act = Act.Climb; f.Z = 100; f.Why = "scrambled by radar"; f.IdleSince = T; f.NextThink = T; } }
        else if (!double.IsNaN(f.ScrambleAt) && T >= f.ScrambleAt) { f.Act = Act.Climb; f.Z = 100; f.Why = "scrambled: raid reported"; f.IdleSince = T; f.NextThink = T; }
    }
}
