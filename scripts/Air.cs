using System;
using System.Collections.Generic;
using System.Linq;

namespace Firebomber;

public enum Phase { Waiting, Outbound, Egress, Returning, Landed }

public sealed class Bomber
{
    public int Id; public Formation F = null!;
    public double Kg; public int CaptainId = -1, Sorties; public double X, Y, Z, Th, V, SlotLat, SlotLag, ZOff, DropAt = double.NaN, HomeX, HomeY, Nerve, NextThink;
    /// <summary>A strike bomber breaking against an attack: until when, which way, and whether it is turning in to bring its guns to bear.</summary>
    public double BreakUntil = -1e9; public int BreakSide = 1; public bool BreakInto;
    public int Pressed; public bool Released, Alive = true, Straggler, Press; public Hull H = null!; public string Captain = "", Why = "in formation", HomeName = "";
    public double Damage => H.Damage;
}

public sealed class Formation
{
    public string Name = "", TurnWhy = ""; public bool Wolf = true; public double Flown, Nerve = .5, NextReckon; public Craft Kind = Craft.V16; public double HomeX, HomeY; public string HomeName = "";
    public readonly List<Bomber> Ac = new(); public Bomber Lead = null!;
    public Phase Phase = Phase.Waiting; public double DepartT, EgressT;
    public double AimX, AimY, ErrX, ErrY, TargetAlt = 6000, Spread = 1;
    public bool Aborted, ForceRelease, Destroyed, CommanderTurned, SplitDone; public int Start = 24, BackEarly;
    /// <summary>Enemy fighters going for this box just now.</summary>
    public int Attackers;
    public double DashUntil, ErrDropX, ErrDropY, LoadKg, UnitX, UnitY, CrewAim = double.NaN; public Mission Mission = Mission.Half; public List<Bomb> Plan = new(); public Loadout? Preset;
    /// <summary>Minutes after the first flight that this one reaches the target: HE to open the roofs, then fire.</summary>
    public int ArriveMin;
    /// <summary>Bombers flying within a few minutes of this box, itself included. A big stream is heard from far off and its guns cover each other; a small group slips through, and has room to jink.</summary>
    public int Company;
    public int LoadHe => Plan.Count(b => b.He); public int LoadInc => Plan.Count(b => !b.He);
    public readonly List<(double x, double y)> Route = new();
}

public struct Impact { public double T, X, Y; public bool Incendiary, Water; public int Kg, Bomblets; }

/// <summary>Bomber formations in the one world: take off, climb, fly the route, release on the leader's signal, turn back and land. Metres, seconds, x east, y north, heading from +x counter-clockwise.
/// No drawing here. Fighters and gunnery are in FighterSim; this class only knows how a bomber is hurt, falls out of formation, and is lost.</summary>
public sealed partial class Air
{
    public const double Dt = .1, MinAlt = 1000, Span = 40;
    const double G = 9.81, DragFactor = 1.12, ForwardFactor = .85, ReleaseInterval = .1, BallisticSigma = .006, PlaneSigma = 80, AimSigma = 350, Jitter = .4;
    public readonly List<Formation> Formations = new();
    public readonly List<Impact> Hits = new();          // everything that has landed, for good
    public readonly List<Impact> Recent = new();        // landed since the view last looked
    readonly PriorityQueue<Impact, double> falling = new();
    /// <summary>Called for each bomb as it lands.</summary>
    public Action<Impact>? Landed;
    public Action? Stepped; public Action<Bomber>? Downed;
    /// <summary>The guns round the cities, if there are any.</summary>
    public FlakSim? Flak;
    public int BombersLost; public readonly List<Bomber> BackHome = new();
    readonly List<Bomber> burnedOut = new(), landedEarly = new(), ditching = new();
    readonly World w; readonly Random rng; double acc; int nextId;
    public double T { get; private set; }
    /// <summary>Time past the last step, so that aircraft can be drawn smoothly between steps.</summary>
    public double Frac => acc;

    public double Tonnes; public Roster Crews = new(); public double Hour0 = 8, StartT;
    public Air(World world, int seed = 1, double startT = 0, double hour = 8) { w = world; rng = new Random(seed); T = startT; StartT = startT; Hour0 = hour; }
    public double Hour => (Hour0 + (T - StartT) / 3600) % 24;
    /// <summary>0 in full daylight, 1 at dead of night.</summary>
        public double Dark { get { double h = Hour; return h < 5 || h >= 20.5 ? 1 : h < 6.5 ? (6.5 - h) / 1.5 : h >= 19 ? (h - 19) / 1.5 : 0; } }
    /// <summary>How hard it is to see, for the gunners and pilots: the night, lightened by the moon, and by day a little murk from cloud. The picture uses Dark; seeing uses this.</summary>
    public double Murk { get { double n = Dark; return Math.Clamp(n * (1 - .3 * Moon), 0, 1); } }
    /// <summary>The same, here: cloud hides the moon, and by day cloud and rain cut how far anyone can see.</summary>
    public double MurkAt(double x, double y) { double n = Dark; var (c, r) = Wx(x, y); return Math.Clamp(n * (1 - .3 * Moon * (1 - c)) + (1 - n) * (.3 * c + .45 * r), 0, 1); }
    /// <summary>The weather, set by the campaign: at a place and moment, and the wind speed. Unset (the sandbox and the tests), it is always clear with 6 m/s.</summary>
    public Func<double, double, double, (double cloud, double rain)>? WxAt; public Func<double, double, double, double>? WindAt;
    /// <summary>Whether a working radar station covers a point, set by the campaign. Unset, the whole Vulfen coast is watched.</summary>
    public Func<double, double, bool>? RadarCovers;
    double[]? wxC, wxR; double wxT = -1e12; const double WxCell = 24000; const int WxW = (int)(World.WidthM / WxCell) + 2, WxH = (int)(World.HeightM / WxCell) + 2;
    /// <summary>Cloud and rain here now, from a coarse grid made afresh every five minutes of the raid, since this is asked thousands of times a step.</summary>
    public (double cloud, double rain) Wx(double x, double y)
    {
        if (WxAt == null) return (0, 0);
        if (wxC == null || T - wxT > 300) { wxC ??= new double[WxW * WxH]; wxR ??= new double[WxW * WxH]; wxT = T; for (int j = 0; j < WxH; j++) for (int i = 0; i < WxW; i++) { var (c, r) = WxAt(i * WxCell, j * WxCell, T); wxC[j * WxW + i] = c; wxR[j * WxW + i] = r; } }
        int ix = Math.Clamp((int)(x / WxCell + .5), 0, WxW - 1), iy = Math.Clamp((int)(y / WxCell + .5), 0, WxH - 1); return (wxC[iy * WxW + ix], wxR![iy * WxW + ix]);
    }
    /// <summary>How scatter goes with height: as it is at 6,000 m, near nothing at 1,000 m.</summary>
    public static double HeightAim(double z) => Math.Pow(Math.Max(300, z) / 6000, 1.4);
    /// <summary>A crew's bombing: the Vulfen better trained; both improve over a dozen sorties.</summary>
    double CrewAim(Formation f) { if (double.IsNaN(f.CrewAim)) f.CrewAim = (f.Wolf ? .91 : 1.1) * (1.1 - .4 * Math.Min(1, f.Ac.Count == 0 ? 0 : f.Ac.Average(a => a.Sorties) / 12)); return f.CrewAim; }
    /// <summary>How far off the whole formation's aim is likely to be, in metres: height, crew, and the wind and cloud over the target. Calm air helps, cloud and rain hurt.</summary>
    public double AimScatter(Formation f)
    {
        var (c, r) = Wx(f.AimX, f.AimY); double z = f.Phase == Phase.Waiting ? f.TargetAlt : f.Lead.Z;
        return AimSigma * Design.Of[f.Kind].Aim * HeightAim(z) * CrewAim(f) * (.7 + (WindAt?.Invoke(f.AimX, f.AimY, T) ?? 6) / 20) * (1 + .8 * c + 1.2 * r);
    }
    /// <summary>The weather for this operation (campaign only): cloud cover and the moon, 0-1.</summary>
    public double Moon;

    double Gauss(double sd) { double u1 = 1 - rng.NextDouble(), u2 = rng.NextDouble(); return sd * Math.Sqrt(-2 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2); }
    static double Wrap(double a) => ((a + Math.PI) % (2 * Math.PI) + 2 * Math.PI) % (2 * Math.PI) - Math.PI;
    static double Dist(double ax, double ay, double bx, double by) => Math.Sqrt((ax - bx) * (ax - bx) + (ay - by) * (ay - by));

    /// <summary>The test raid: one box from each airfield, timed to meet at a point short of the target.</summary>
    public void TestRaid(Place target, int waves = 1, Craft[]? kinds = null, bool wolf = true, double delay = 0, double waveGap = 0, int box = 0)
    {
        delay += StartT;
        double cdx = 0, cdy = 0; var fields = wolf ? w.Fields.Take(3).ToList() : w.EnemyFields.OrderBy(q => Dist(q.X, q.Y, target.X, target.Y)).Take(3).ToList(); kinds ??= new[] { Craft.V16 };
        foreach (var f in fields) { double d = Dist(f.X, f.Y, target.X, target.Y); cdx += (f.X - target.X) / d; cdy += (f.Y - target.Y) / d; }
        double cl = Math.Sqrt(cdx * cdx + cdy * cdy); cdx /= cl; cdy /= cl;
        double rvX = target.X + cdx * 120000, rvY = target.Y + cdy * 120000;
        var legs = Enumerable.Range(0, waves * fields.Count).Select(n => (f: fields[n % fields.Count], i: n)).Select(q =>
        {
            var (f, i) = q;
            double lat = (i % 3 - 1) * 700 + (i / 3) * 250, along = i * 3000;
            return (f, x: rvX + cdx * along - cdy * lat, y: rvY + cdy * along + cdx * lat);
        }).ToList();
        double longest = 0; for (int i = 0; i < legs.Count; i++) { var ds0 = Design.Of[kinds[i % kinds.Length]]; longest = Math.Max(longest, Dist(legs[i].f.X, legs[i].f.Y, legs[i].x, legs[i].y) / Weight.Cruise(ds0, Weight.PlanKg(ds0, Dist(legs[i].f.X, legs[i].f.Y, target.X, target.Y) / 1000))); }
        for (int i = 0; i < legs.Count; i++)
        {
            var (f, x, y) = legs[i];
            var kind = kinds[i % kinds.Length]; var dsg = Design.Of[kind];
            var fm = MakeBox(wolf ? ((char)('A' + i)).ToString() : "U" + (Formations.Count(q => !q.Wolf) + 1), f, kind, box, wolf, x, y, target.X + (i % 3 - 1) * 300, target.Y + (i / 3 - (waves - 1) / 2.0) * 300);
            fm.DepartT = delay + (i / fields.Count) * waveGap + longest - Dist(f.X, f.Y, x, y) / Weight.Cruise(dsg, fm.LoadKg);
            if (!wolf && dsg.Strike)
            {
                double ang = Math.Atan2(cdy, cdx) + (i % 2 == 0 ? 1 : -1) * (.45 + .25 * (i / 2 % 2));
                fm.Route.Add((target.X + Math.Cos(ang) * 40000, target.Y + Math.Sin(ang) * 40000));
            }
        }
    }
    /// <summary>A raid from stock: each box from its own field, of its own size, meeting short of the target as the test raid does and going in together. SB-3 boxes fan out on the way in.</summary>
    public void Raid(Place target, IList<(Place field, Craft kind, int n)> boxes, bool wolf, double delay, bool works = false, (double x, double y)? at = null)
    {
        var (ax0, ay0) = at ?? (works ? CityPlan.Works(target) : (target.X, target.Y));
        delay += StartT; if (boxes.Count == 0) return;
        double cdx = 0, cdy = 0; foreach (var b in boxes) { double d = Dist(b.field.X, b.field.Y, target.X, target.Y) + 1; cdx += (b.field.X - target.X) / d; cdy += (b.field.Y - target.Y) / d; }
        double cl = Math.Sqrt(cdx * cdx + cdy * cdy) + 1e-6; cdx /= cl; cdy /= cl;
        double rvX = target.X + cdx * 120000, rvY = target.Y + cdy * 120000;
        var legs = boxes.Select((b, i) => (b, i, x: rvX + cdx * i * 3000 - cdy * (i % 3 - 1) * 700, y: rvY + cdy * i * 3000 + cdx * (i % 3 - 1) * 700)).ToList();
        double longest = legs.Max(q => { var ds = Design.Of[q.b.kind]; return Dist(q.b.field.X, q.b.field.Y, q.x, q.y) / Weight.Cruise(ds, Weight.PlanKg(ds, Dist(q.b.field.X, q.b.field.Y, target.X, target.Y) / 1000)); });
        foreach (var (b, i, x, y) in legs)
        {
            var dsg = Design.Of[b.kind];
            var fm = MakeBox(wolf ? ((char)('A' + Formations.Count(q => q.Wolf))).ToString() : "U" + (Formations.Count(q => !q.Wolf) + 1), b.field, b.kind, b.n, wolf, x, y, ax0 + (i % 3 - 1) * 300, ay0 + (i / 3 - 1) * 300);
            fm.DepartT = delay + longest - Dist(b.field.X, b.field.Y, x, y) / Weight.Cruise(dsg, fm.LoadKg);
            if (!wolf && dsg.Strike) { double ang = Math.Atan2(cdy, cdx) + (i % 2 == 0 ? 1 : -1) * (.45 + .25 * (i / 2 % 2)); fm.Route.Add((target.X + Math.Cos(ang) * 40000, target.Y + Math.Sin(ang) * 40000)); }
        }
    }
    /// <summary>The sandbox's raid: whatever flights the player asked for, from whichever fields, meeting short of the target and going in together.</summary>
    public void Flights(double aimX, double aimY, IList<(Place field, Craft kind, int n)> flights)
    {
        double cdx = 0, cdy = 0; foreach (var q in flights) { double d = Dist(q.field.X, q.field.Y, aimX, aimY) + 1; cdx += (q.field.X - aimX) / d; cdy += (q.field.Y - aimY) / d; }
        double cl = Math.Sqrt(cdx * cdx + cdy * cdy) + 1e-6; cdx /= cl; cdy /= cl;
        for (int i = 0; i < flights.Count; i++)
        {
            var (f, kind, n) = flights[i]; double lat = (i % 3 - 1) * 700 + (i / 3) * 250, along = i * 3000;
            MakeBox(((char)('A' + i)).ToString(), f, kind, n, true, aimX + cdx * (120000 + along) - cdy * lat, aimY + cdy * (120000 + along) + cdx * lat, aimX + (i % 3 - 1) * 500, aimY + (i / 3 - (flights.Count - 1) / 6.0) * 500);      // a big raid lays its boxes side by side and covers ground, rather than bombing the same streets over again
        }
        Launch();
    }
    /// <summary>One box on the ground at its field, with its route and its crews.</summary>
    Formation MakeBox(string name, Place f, Craft kind, int box, bool wolf, double x, double y, double aimX, double aimY)
    {
        var dsg = Design.Of[kind];
        var fm = NewFormation(name, f); fm.Wolf = wolf; fm.Kind = kind; fm.TargetAlt = dsg.Alt; fm.Start = box > 0 ? box : dsg.Box; fm.Company = fm.Start; fm.Nerve = rng.NextDouble(); fm.LoadKg = Weight.PlanKg(dsg, Dist(f.X, f.Y, aimX, aimY) / 1000); fm.Plan = Bombs.Load(dsg, fm.LoadKg, fm.Mission);
        fm.AimX = aimX; fm.AimY = aimY; fm.Route.Add((x, y));
        double th = Math.Atan2(y - f.Y, x - f.X);
        double sc = Math.Clamp(Sprites.Specs[kind].Span / 40, .55, 1), Col = 250 * sc, Rank = 220 * sc, WingLat = 90 * sc, WingLag = 70 * sc;
        for (int k = 0; k < fm.Start; k++)
        {
            int e = k / 3, m = k % 3, c = e % 2, r = e / 2; var cap = wolf ? WolfCaptains : DeerCaptains;
            var cq = wolf ? Crews.WolfCaptains : Crews.UcsCaptains; CaptainRec? rec = null; if (cq.Count > 0) { rec = cq[0]; cq.RemoveAt(0); }
            var a = new Bomber { Id = nextId++, F = fm, Th = th, V = Weight.Cruise(dsg, fm.LoadKg), ZOff = c == 1 ? 30 : -30, Z = 200, H = new Hull(dsg), HomeX = f.X, HomeY = f.Y, HomeName = f.Name, Captain = rec?.Name ?? cap[rng.Next(cap.Length)], Nerve = rec?.Nerve ?? rng.NextDouble(), Sorties = rec?.Sorties ?? 0, NextThink = StartT + rng.NextDouble() * 5, Kg = fm.Plan.Sum(b => b.Kg) };
            a.H.Fuel = Weight.FuelSeconds(dsg, a.Kg);
            a.SlotLat = (c == 0 ? -.5 : .5) * Col + (m == 0 ? 0 : m == 1 ? -WingLat : WingLat) + .5 * Col;
            a.SlotLag = r * Rank + c * Rank * .3 + (m == 0 ? 0 : WingLag);
            PutInSlot(a, f.X, f.Y, th);
            fm.Ac.Add(a);
        }
        fm.Lead = fm.Ac[0];
        Roll(fm);
        Formations.Add(fm);
        return fm;
    }
    Formation NewFormation(string name, Place home) => new() { Name = name, HomeX = home.X, HomeY = home.Y, HomeName = home.Name };
    static void PutInSlot(Bomber a, double lx, double ly, double th)
    {
        a.X = lx - a.SlotLag * Math.Cos(th) + a.SlotLat * Math.Sin(th);
        a.Y = ly - a.SlotLag * Math.Sin(th) - a.SlotLat * Math.Cos(th);
    }
    void Roll(Formation f) { f.UnitX = Gauss(1); f.UnitY = Gauss(1); }

    /// <summary>When the player's flights take off; until then they wait on the ground. Unset, they go at once.</summary>
    public double LaunchAt = double.NaN;
    /// <summary>Whether anything else (fighters) is in the air; set by the fighter simulation.</summary>
    public Func<bool>? Busy;
    /// <summary>Nothing flying, nothing falling, nobody about to take off: time can pass without being simulated.</summary>
    public bool Quiet => falling.Count == 0 && Formations.All(f => f.Phase == Phase.Landed || f.Phase == Phase.Waiting && f.DepartT > T + Dt) && (Busy == null || !Busy());
    /// <summary>The next take-off, or never.</summary>
    public double NextWake => Formations.Where(f => f.Phase == Phase.Waiting).Select(f => f.DepartT).DefaultIfEmpty(double.MaxValue).Min();
    /// <summary>Over quiet time in one go, for the campaign clock. Only when Quiet.</summary>
    public void JumpTo(double t) { if (t > T) { T = t; acc = 0; } }

    public void Advance(double seconds)
    {
        acc += seconds;
        while (acc >= Dt) { Step(); acc -= Dt; }
    }

    void Step()
    {
        foreach (var f in Formations) Fly(f);
        Flak?.Step(T); Hulls();
        foreach (var b in burnedOut) Lose(b); burnedOut.Clear();
        foreach (var b in ditching) Ditch(b); ditching.Clear();
        foreach (var b in landedEarly) { b.Alive = false; b.F.Ac.Remove(b); b.F.BackEarly++; BackHome.Add(b); if (b.F.Ac.Count == 0) b.F.Phase = Phase.Landed; else if (b == b.F.Lead) NewLeadExcept(b.F, b); } landedEarly.Clear();
        T += Dt;
        Stepped?.Invoke();
        while (falling.TryPeek(out var i, out double when) && when <= T)
        {
            falling.Dequeue();
            i.Water = w.IsWater(i.X, i.Y, 16);
            Hits.Add(i); Recent.Add(i); Landed?.Invoke(i);
        }
    }

    void Fly(Formation f)
    {
        if (f.Phase == Phase.Landed) return;
        if (f.Phase == Phase.Waiting) { if (T < f.DepartT) return; f.Phase = Phase.Outbound; }
        var lead = f.Lead; var dsg = Design.Of[f.Kind]; f.Flown += lead.V * Dt;
        switch (f.Phase)
        {
            case Phase.Outbound:
                { double aimS = AimScatter(f); f.ErrX = f.UnitX * aimS; f.ErrY = f.UnitY * aimS; }
                if (f.Route.Count > 0)
                {
                    Steer(lead, f.Route[0].x, f.Route[0].y);
                    if (Dist(lead.X, lead.Y, f.Route[0].x, f.Route[0].y) < 3000) f.Route.RemoveAt(0);
                }
                else
                {
                    var (rx, ry) = ReleasePoint(f, Math.Atan2(f.AimY + f.ErrY - lead.Y, f.AimX + f.ErrX - lead.X));
                    // too close to turn onto the run: fly out and come round again, rather than circle for ever
                    double off = Math.Abs(Wrap(Math.Atan2(ry - lead.Y, rx - lead.X) - lead.Th)), away = Dist(lead.X, lead.Y, rx, ry);
                    if (off > .45 && away < 13500 && !AtRelease(f))
                    {
                        double ox = lead.X - f.AimX, oy = lead.Y - f.AimY, ol = Math.Sqrt(ox * ox + oy * oy) + 1;
                        f.Route.Add((f.AimX + ox / ol * 22000, f.AimY + oy / ol * 22000));
                    }
                    Steer(lead, rx, ry);
                }
                if (f.ForceRelease || (f.Route.Count == 0 && AtRelease(f))) Release(f);
                break;
            case Phase.Egress:
                if (T >= f.EgressT) f.Phase = Phase.Returning;
                break;
            case Phase.Returning:
                Steer(lead, f.HomeX, f.HomeY);
                if (Dist(lead.X, lead.Y, f.HomeX, f.HomeY) < 7000 && f.Ac.All(a => !a.Straggler || a == lead)) { f.Phase = Phase.Landed; return; }
                break;
        }
        foreach (var a in f.Ac)
        {
            double cap = a.H.Power, roof = Weight.Roof(dsg, a.Kg) * a.H.RoofShare, sink = a.H.Sink, cruiseV = Weight.Cruise(dsg, a.Kg), climbV = Weight.Climb(dsg, a.Kg); bool dash = dsg.Dash && T < f.DashUntil;
            if (sink > 0) { a.Z -= sink * Dt; if (a.Z <= 25) { a.Why = "ditched"; ditching.Add(a); continue; } }
            else a.Z += Math.Clamp(Math.Min(roof, a.Straggler ? Math.Max(1500, f.TargetAlt - 1500) : f.TargetAlt) + a.ZOff - a.Z, -climbV * Dt, climbV * cap * Dt);
            if (a == lead) a.V = (dash ? Weight.Top(dsg, a.Kg) * .97 : cruiseV) * Math.Min(1, cap);
            else if (a.Straggler)
            {
                // on its own: its captain decides
                a.V = cruiseV * Math.Min(1, cap) * .92;
                if (a.Press && !a.Released)
                {
                    double ax = f.AimX - a.X, ay = f.AimY - a.Y, ad = Math.Sqrt(ax * ax + ay * ay) + 1, t = Throw(a);
                    Steer(a, f.AimX - ax / ad * t, f.AimY - ay / ad * t);
                    if (ad <= t * 1.05 && Math.Abs(Wrap(Math.Atan2(ay, ax) - a.Th)) < .25) { Drop(a); a.Press = false; a.Why = "bombed alone"; }
                    else if (ad < t * .5) { a.Press = false; }
                }
                else
                {
                    Steer(a, a.HomeX, a.HomeY);
                    if (Dist(a.X, a.Y, a.HomeX, a.HomeY) < 7000) { landedEarly.Add(a); continue; }
                }
            }
            else if (T < a.BreakUntil)
            {
                // a hard break: well off the formation's heading, as fast as it will go; an SB-3 dives as it turns
                double want = lead.Th + a.BreakSide * (a.BreakInto ? .9 : 1.25), m = a.H.D.TurnDeg * 4.5 * (dsg.Dash && a.Kg > 0 ? .3 : 1) * Math.PI / 180 * Dt;      // a U-4 with bombs slung outside can hardly turn
                a.Th = Wrap(a.Th + Math.Clamp(Wrap(want - a.Th), -m, m)); a.V = Weight.Top(dsg, a.Kg) * Math.Min(1, cap);
                if (!f.Wolf) a.Z = Math.Max(1500, a.Z - 7 * Dt);
            }
            else
            {
                double fx = Math.Cos(lead.Th), fy = Math.Sin(lead.Th), sx = lead.X - a.SlotLag * f.Spread * fx + a.SlotLat * f.Spread * fy, sy = lead.Y - a.SlotLag * f.Spread * fy - a.SlotLat * f.Spread * fx;
                double wx = fx * lead.V + .08 * (sx - a.X), wy = fy * lead.V + .08 * (sy - a.Y);
                double vTop = Math.Min(Weight.Top(dsg, a.Kg) * cap, 1.15 * lead.V); a.V = Math.Clamp(Math.Sqrt(wx * wx + wy * wy), Math.Min(.7 * lead.V, vTop), vTop);
                a.Th = Math.Atan2(wy, wx);
            }
            a.X += Math.Cos(a.Th) * a.V * Dt; a.Y += Math.Sin(a.Th) * a.V * Dt;
            if (!a.Released && !double.IsNaN(a.DropAt) && T >= a.DropAt) Drop(a);
        }
    }

    void Steer(Bomber a, double tx, double ty)
    {
        double d = Wrap(Math.Atan2(ty - a.Y, tx - a.X) - a.Th), m = a.H.D.TurnDeg * Math.PI / 180 * Dt;
        a.Th = Wrap(a.Th + Math.Clamp(d, -m, m));
    }
    static double Throw(Bomber a) => Math.Sqrt(2 * a.Z / G) * DragFactor * a.V * ForwardFactor;
    (double x, double y) ReleasePoint(Formation f, double th) { double t = Throw(f.Lead); return (f.AimX + f.ErrX - Math.Cos(th) * t, f.AimY + f.ErrY - Math.Sin(th) * t); }

    /// <summary>The leader has reached the release point, flying towards the aim. If it passed badly lined up it turns round and tries again.</summary>
    bool AtRelease(Formation f)
    {
        var l = f.Lead; double ax = f.AimX + f.ErrX - l.X, ay = f.AimY + f.ErrY - l.Y, fx = Math.Cos(l.Th), fy = Math.Sin(l.Th), t = Throw(l);
        double along = ax * fx + ay * fy, across = Math.Abs(ax * fy - ay * fx);
        return along <= t && along > 0 && across < 600;
    }
    void Release(Formation f)
    {
        f.Phase = Phase.Egress; f.EgressT = T + 25;
        // the sight: worse from higher up, far worse at night, and worse still if the lead's bomb aimer is gone
        double low = HeightAim(f.Lead.Z), sd = AimSigma * Design.Of[f.Kind].Aim * CrewAim(f) * (Math.Max(0, f.Lead.Z / 6000 - 1) + 1.6 * Dark * low);
        if (!f.Lead.H.Fit("Bomb aimer") && f.Lead.H.D.Crew.Contains("Bomb aimer")) sd += 500 * low;
        f.ErrDropX = Gauss(sd); f.ErrDropY = Gauss(sd);
        if (Design.Of[f.Kind].Dash) f.DashUntil = T + 420;      // bombs gone: run for home at full throttle
        foreach (var a in f.Ac) if (!a.Straggler) a.DropAt = T + (a == f.Lead ? 0 : Math.Abs(Gauss(Jitter * Math.Max(.15, low))));
    }
    void Drop(Bomber a)
    {
        a.Released = true;
        double tf = Math.Sqrt(2 * a.Z / G) * DragFactor, fx = Math.Cos(a.Th), fy = Math.Sin(a.Th), low = Math.Max(.1, HeightAim(a.Z)), ix = Gauss(PlaneSigma * low), iy = Gauss(PlaneSigma * low), bal = BallisticSigma * a.Z;
        // low down each crew aims at the point for itself rather than dropping on the leader: fully so by 1,000 m, not at all at 6,000 m. Bombs ordered away early still fall where the aircraft is
        double tight = a.Straggler || a.F.ForceRelease ? 0 : Math.Clamp((6000 - a.Z) / 5000, 0, 1) * .97;
        var plan = a.F.Plan; double ex = a.Straggler ? Gauss(400) : a.F.ErrDropX, ey = a.Straggler ? Gauss(400) : a.F.ErrDropY;
        for (int k = 0; k < plan.Count; k++)
        {
            double stick = a.V * (k - (1 - low) * (plan.Count - 1) / 2.0) * ReleaseInterval * Math.Max(.25, Math.Sqrt(low)), along = stick + a.V * tf * ForwardFactor;
            double hx = a.X + fx * along + ix + ex + Gauss(bal), hy = a.Y + fy * along + iy + ey + Gauss(bal), wantX = a.F.AimX + a.F.ErrX + ex + fx * stick + ix, wantY = a.F.AimY + a.F.ErrY + ey + fy * stick + iy;
            if (Dist(hx, hy, wantX, wantY) < 2500) { hx += (wantX - hx) * tight; hy += (wantY - hy) * tight; }
            var i = new Impact { T = T + k * ReleaseInterval + tf, X = hx, Y = hy, Incendiary = !plan[k].He, Kg = plan[k].Kg, Bomblets = plan[k].Bomblets };
            falling.Enqueue(i, i.T); Tonnes += plan[k].Kg / 1000.0;
        }
        a.Kg = 0;
    }

    // ---------------------------------------------------------------- orders
    static bool CanOrder(Formation f) => f.Phase == Phase.Waiting || f.Phase == Phase.Outbound;
    public void AddWaypoint(Formation f, double x, double y) { if (CanOrder(f)) f.Route.Add((x, y)); }
    public void SetAim(Formation f, double x, double y) { if (CanOrder(f)) { f.AimX = x; f.AimY = y; Roll(f); } }
    public static void ChangeAltitude(Formation f, double d) => f.TargetAlt = Math.Clamp(f.TargetAlt + d, MinAlt, Weight.Roof(Design.Of[f.Kind], f.LoadKg));
    public void ReAim(Formation f, double x, double y) => SetAim(f, x, y);
    /// <summary>Tests and command lines: below 0 demolition, 30 general HE, 15 half, 6 fire raid, 0 all incendiary.</summary>
    public static void SetLoad(Formation f, int he) => SetMission(f, he < 0 ? Mission.Demolition : he >= 30 ? Mission.General : he >= 15 ? Mission.Half : he > 0 ? Mission.Fire : Mission.AllIncendiary);
    public static void SetMission(Formation f, Mission m) { if (f.Phase != Phase.Waiting) return; f.Mission = m; f.Preset = null; Reload(f); }
    /// <summary>The load this flight goes with. Only on the ground.</summary>
    public static void SetPreset(Formation f, Loadout l) { if (f.Phase != Phase.Waiting) return; f.Preset = l; f.Plan = l.Plan; f.LoadKg = l.Kg; Refit(f); }
    /// <summary>The way out and straight back, in km: what the fuel must cover.</summary>
    public double TripKm(Formation f)
    {
        double km = 0, x = f.HomeX, y = f.HomeY; foreach (var r in f.Route) { km += Dist(x, y, r.x, r.y); x = r.x; y = r.y; }
        return (km + Dist(x, y, f.AimX, f.AimY) + Dist(f.AimX, f.AimY, f.HomeX, f.HomeY)) / 1000;
    }
    /// <summary>The raid goes. Each flight takes off when it must to reach the target at its time, whatever it now weighs and whichever way it now goes.</summary>
    public void Launch()
    {
        double Way(Formation f) { double m = 0, x = f.HomeX, y = f.HomeY; foreach (var r in f.Route) { m += Dist(x, y, r.x, r.y); x = r.x; y = r.y; } return (m + Dist(x, y, f.AimX, f.AimY)) / Weight.Cruise(Design.Of[f.Kind], f.LoadKg); }
        var own = Formations.Where(f => f.Wolf && f.Phase == Phase.Waiting).ToList(); if (own.Count == 0) return;
        double longest = own.Max(Way), t0 = double.IsNaN(LaunchAt) ? StartT : LaunchAt; foreach (var f in own) f.DepartT = t0 + longest - Way(f) + f.ArriveMin * 60;
    }
    /// <summary>Weight of bombs a plane. Only on the ground: fuel goes down as bombs go up.</summary>
    public static void ChangeKg(Formation f, double d) { if (f.Phase != Phase.Waiting) return; var ds = Design.Of[f.Kind]; f.LoadKg = Math.Clamp(f.LoadKg + d, 0, ds.MaxKg); Reload(f); }
    static void Reload(Formation f) { f.Plan = Bombs.Load(Design.Of[f.Kind], f.LoadKg, f.Mission); Refit(f); }
    /// <summary>Bombs and fuel to match the plan. Fuel goes down as bombs go up.</summary>
    static void Refit(Formation f) { var ds = Design.Of[f.Kind]; double kg = f.Plan.Sum(b => b.Kg); foreach (var a in f.Ac) if (!a.Released) { a.Kg = kg; a.H.Fuel = Weight.FuelSeconds(ds, kg); } }
    public static void SetSpacing(Formation f, double s) => f.Spread = s;
    public static void BombNow(Formation f) { if (f.Phase == Phase.Outbound) f.ForceRelease = true; }
    /// <summary>Sends a formation to another of its side's fields, turning it back if it was still going out.</summary>
    public static void Divert(Formation f, Place field)
    {
        f.HomeX = field.X; f.HomeY = field.Y; f.HomeName = field.Name; foreach (var a in f.Ac) { a.HomeX = field.X; a.HomeY = field.Y; a.HomeName = field.Name; }
        if (f.Phase == Phase.Outbound) { f.Aborted = true; f.Route.Clear(); f.Phase = Phase.Returning; }
    }
    /// <summary>How far this formation can still go, in km, on its leader's fuel.</summary>
    public static double FuelKm(Formation f) => f.Lead.H.Fuel * Weight.Cruise(Design.Of[f.Kind], f.Lead.Kg) / 1000;
    public static void Abort(Formation f) { if (CanOrder(f)) { f.Aborted = true; f.Route.Clear(); f.Phase = f.Phase == Phase.Waiting ? Phase.Landed : Phase.Returning; } }

    /// <summary>Detaches these aircraft into a new formation, led by the one furthest forward. Spacing in both formations is measured afresh, so nobody jumps.</summary>
    public Formation? Split(IEnumerable<Bomber> chosen, string? name = null)
    {
        var list = chosen.Where(a => a.Alive && !a.Straggler && a.F.Phase == Phase.Outbound).ToList(); if (list.Count == 0) return null;
        var src = list[0].F; list = list.Where(a => a.F == src).ToList();
        if (list.Count >= src.Ac.Count) return null;
        var nf = new Formation { Wolf = src.Wolf, Flown = src.Flown, Nerve = src.Nerve, Start = list.Count, Name = name ?? NextName(), Company = src.Company, DepartT = src.DepartT, Kind = src.Kind, HomeX = src.HomeX, HomeY = src.HomeY, HomeName = src.HomeName, Phase = src.Phase, Plan = src.Plan, Mission = src.Mission, LoadKg = src.LoadKg, AimX = src.AimX, AimY = src.AimY, TargetAlt = src.TargetAlt, Spread = src.Spread };
        nf.Route.AddRange(src.Route); Roll(nf);
        src.Start -= list.Count; foreach (var a in list) { src.Ac.Remove(a); nf.Ac.Add(a); a.F = nf; }
        nf.Lead = list.OrderByDescending(a => a.X * Math.Cos(a.Th) + a.Y * Math.Sin(a.Th)).First();
        if (list.Contains(src.Lead) || !src.Ac.Contains(src.Lead)) src.Lead = src.Ac.FirstOrDefault(q => !q.Straggler) ?? src.Ac[0];
        Reslot(src); Reslot(nf);
        Formations.Add(nf);
        return nf;
    }
    string NextName() { for (char c = 'A'; ; c++) if (Formations.All(f => f.Name != c.ToString())) return c.ToString(); }
    static void Reslot(Formation f)
    {
        var l = f.Lead; double fx = Math.Cos(l.Th), fy = Math.Sin(l.Th), rx = fy, ry = -fx, sp = Math.Max(.3, f.Spread);
        foreach (var a in f.Ac) { double dx = a.X - l.X, dy = a.Y - l.Y; a.SlotLat = (dx * rx + dy * ry) / sp; a.SlotLag = -(dx * fx + dy * fy) / sp; }
    }

    public string Status(Formation f)
    {
        var l = f.Lead;
        switch (f.Phase)
        {
            case Phase.Waiting: return $"on the ground, {Math.Ceiling((f.DepartT - T) / 60):0} min, {f.LoadKg / 1000:0.#} t each";
            case Phase.Outbound: return f.Route.Count > 0 ? $"on route, {f.Route.Count} waypoint{(f.Route.Count > 1 ? "s" : "")}" : $"bomb run, {Dist(l.X, l.Y, f.AimX, f.AimY) / 1000:0} km to aim";
            case Phase.Egress: return "bombs away";
            case Phase.Returning: return f.CommanderTurned ? f.TurnWhy : $"{(f.Aborted ? "aborted" : "returning")}, {Dist(l.X, l.Y, f.HomeX, f.HomeY) / 1000:0} km to {f.HomeName}";
            default: return f.Destroyed ? "all lost" : f.Aborted ? "stood down" : "home";
        }
    }
}
