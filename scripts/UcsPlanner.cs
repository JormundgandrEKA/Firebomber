using System;
using System.Collections.Generic;
using System.Linq;

namespace Firebomber;

/// <summary>How the UCS means to win this month.</summary>
public enum Doctrine { Burn, Works, Pressure, CounterAir }

/// <summary>The UCS's operational planner (roadmap part 5). It replaces the placeholder that raided on three days in ten.
/// It plans every morning from what it believes: its own losses by day and by night and at each target, the Vulfen cities it has hurt, which of its own cities were bombed lately, and which fields the Vulfen raids come from.
/// It holds a doctrine for weeks (burn the cities by night; hit the works by day; or keep up pressure with small raids on the nearer towns), drops it when it is costing too much, but not always.
/// It keeps a rhythm like the player's: build up, a big raid, small ones between, a stand-down after a bad night. It over-reacts: a heavy Vulfen raid on a UCS city brings a raid back the next night if anything is ready.
/// Targets are chosen by weighted chance from scores, so it is sensible most of the time and never certain.
/// At home it moves flak towards the cities that were hit, rebuilds bombed works first, disperses a badly hit works to small towns, and shifts its factories between bombers and fighters as the war goes.
/// What it does is not told to the player; radar and intelligence are. All numbers are first suggestions.</summary>
public sealed class UcsPlanner
{
    public Doctrine Doctrine = Doctrine.Burn; public double DoctrineSince, LastBig = -9 * 86400.0, StandDownUntil, LastMixChange = -1e9;
    /// <summary>Loss rate it remembers, by day and by night (running means), and by target city.</summary>
    public double DayLoss = .08, NightLoss = .06;
    public readonly Dictionary<string, double> LossAt = new();
    /// <summary>What it has done to each Vulfen city, by its own count: raids sent.</summary>
    public readonly Dictionary<string, int> RaidsOn = new();
    /// <summary>Where the Vulfen bombers have been seen to come from: bombers sent from each field's town, decaying.</summary>
    public readonly Dictionary<string, double> PlayerFrom = new();
    /// <summary>Flak moved between UCS cities: a multiplier on each city's usual guns. The total stays about the same.</summary>
    public readonly Dictionary<string, double> Flak = new();
    /// <summary>For the diary and tests: what it did and why, newest last.</summary>
    public readonly List<string> Notes = new();
    public int Raids, BigRaids;
    /// <summary>Fighters sent with each bomber box, from the box's own field: more when raids are being cut up, fewer when they get through.</summary>
    public int EscortsPerBox = 2;
    /// <summary>The latest raid warning from intelligence: the city it names, how long it stands, how many bombers it speaks of. It may be wrong, or there may be no raid at all.</summary>
    public string WarnTarget = ""; public double WarnUntil; public int WarnN; public bool NewWarning;
    bool scheduledToday;

    const double Day = 86400;
    public double FlakOf(Place c) => Flak.TryGetValue(c.Name, out var m) ? m : 1;
    /// <summary>Works in UCS cities that were bombed are rebuilt first.</summary>
    public double RepairBoost(Place c, FireSim fire) => !c.Wolf && fire.Plant(c) < .95 ? 1.6 : 1;

    /// <summary>How much the UCS cares about a city of its own just now: its size, how often it was bombed lately, and the last one hit most of all.</summary>
    public double Alarm(Place c, FireSim fire, double now)
    {
        if (!fire.Records.TryGetValue(c, out var r) || now - r.LastHitT > 21 * Day) return 0;
        double last = fire.Records.Where(kv => !kv.Key.Wolf).Max(kv => kv.Value.LastHitT);
        return r.Raids * Math.Exp(-(now - r.LastHitT) / (10 * Day)) + (r.LastHitT == last ? 2 : 0);      // over-reaction: the latest is the one everyone talks about
    }

    /// <summary>The morning plan. May put a raid in the queue; never more than one a day.</summary>
    public void Daily(double now, Campaign camp, World w, Forces forces, FireSim fire, Random rng)
    {
        // patrols: over the cities it is most worried about, on the side the bombers come from
        foreach (var b in forces.Side(false)) b.Patrols.Clear();
        foreach (var b in forces.Side(false).Where(q => Alarm(q.Town, fire, now) > 0).OrderByDescending(q => Alarm(q.Town, fire, now)).Take(3))
        { int n = Math.Min(24, b.S(Craft.I8).Ready / 4); if (n >= 4) b.Patrols.Add(new Patrol { Kind = Craft.I8, N = n, X = b.Town.X, Y = b.Town.Y + 40000, Hours = 2 }); }
        scheduledToday = false; Plan(now, camp, w, forces, fire, rng);
        // now and then intelligence is alarmed about nothing
        if (!scheduledToday && now >= WarnUntil && rng.NextDouble() < .05)
        {
            var mine = w.Cities.Where(c => c.Wolf).OrderByDescending(c => c.Size).Take(5).ToList(); double day0 = Math.Floor(now / Day) * Day;
            Say(camp, mine[rng.Next(mine.Count)], day0 + 22 * 3600, 100 + 40 * rng.Next(6), true);
        }
    }
    void Say(Campaign camp, Place named, double t, int n, bool night)
    {
        WarnTarget = named.Name; WarnUntil = t + 6 * 3600; WarnN = n; NewWarning = true;
        camp.Note($"Intelligence: about {n} UCS bombers may strike {(night ? "tonight" : "today")}, suspected target is {named.Name}.", true);
    }
    void Plan(double now, Campaign camp, World w, Forces forces, FireSim fire, Random rng)
    {
        var ind = forces.Ind; var ucs = forces.Side(false).ToList();
        int ready = Math.Min(ucs.Sum(b => b.S(Craft.TB6).Ready + b.S(Craft.SB3).Ready), ind.BomberCrews[0]), fleet = ucs.Sum(b => b.S(Craft.TB6).Ready + b.S(Craft.TB6).ComingN + b.S(Craft.SB3).Ready + b.S(Craft.SB3).ComingN);
        double readiness = fleet == 0 ? 0 : ready / (double)fleet;
        foreach (var k in PlayerFrom.Keys.ToList()) PlayerFrom[k] *= .97;

        // doctrine: held for two weeks at least; dropped when it is costing, but stubbornly
        double cost = Doctrine is Doctrine.Works or Doctrine.CounterAir ? DayLoss : NightLoss;
        if (now - DoctrineSince > 14 * Day && (cost > .14 && rng.NextDouble() < .35 || rng.NextDouble() < .04))
        {
            var score = new Dictionary<Doctrine, double> { [Doctrine.Burn] = 1 + 6 * (.12 - NightLoss), [Doctrine.Works] = 1 + 6 * (.12 - DayLoss), [Doctrine.Pressure] = .8, [Doctrine.CounterAir] = .75 + .02 * PlayerFrom.Values.Sum() / 10 };
            score[Doctrine] *= .5;      // a change is wanted: not the same again, though it may come back
            var next = Pick(score.Keys.ToList(), d => Math.Max(.05, score[d]), rng);
            if (next != Doctrine) { Note(camp, $"doctrine {Doctrine} to {next} (losses by day {DayLoss:P0}, by night {NightLoss:P0})"); Doctrine = next; DoctrineSince = now; }
        }
        if (now < StandDownUntil || readiness < .4) return;

        // over-reaction: a heavy raid on one of its cities in the last two days brings one back tonight
        var hitHard = fire.Records.Where(kv => !kv.Key.Wolf && now - kv.Value.LastHitT < 2 * Day && kv.Value.Lost - kv.Value.LostAtRaid > 800).Select(kv => kv.Key).FirstOrDefault();
        bool revenge = hitHard != null && !camp.Pending.Any(e => e.Kind == Ev.UcsRaid) && rng.NextDouble() < .6;
        bool big = !revenge && now - LastBig > (9 + 6 * rng.NextDouble()) * Day && readiness > .6;
        bool small = !revenge && !big && rng.NextDouble() < (Doctrine == Doctrine.Pressure ? .35 : .12);
        if (!revenge && !big && !small) return;
        if (camp.Pending.Any(e => e.Kind == Ev.UcsRaid)) return;

        // day or night: by doctrine, by what it has learned, and by tonight's moon and today's cloud
        var day0 = Math.Floor(now / Day) * Day; var (cloud, moon) = Campaign.WeatherAt(camp.Seed, day0 + 6 * 3600);
        double pNight = Doctrine switch { Doctrine.Burn => .85, Doctrine.Works => .3, Doctrine.CounterAir => .25, _ => .6 } + Math.Clamp((DayLoss - NightLoss) * 3, -.3, .3) + (moon > .7 ? -.15 : moon < .3 ? .1 : 0) + (cloud > .6 ? .2 : 0);
        bool night = revenge || rng.NextDouble() < Math.Clamp(pNight, .1, .95);
        double hour = night ? 22 + 2.5 * rng.NextDouble() : 10 + 3 * rng.NextDouble();
        double t = day0 + (hour - 6) * 3600; if (t <= now + 600) t += Day;

        // where: scored, then a weighted draw
        var cities = w.Cities.Where(c => c.Wolf && Reach(c, ucs)).ToList(); if (cities.Count == 0) return;
        double popMax = cities.Max(c => (double)FireSim.PopulationOf(c)), worksMax = ind.Works.Where(q => q.Wolf).Max(q => q.Points);
        double Score(Place c)
        {
            fire.Records.TryGetValue(c, out var r); double ruin = r == null || c.Layout == null ? 0 : Math.Min(1, (r.Lost - r.Rebuilt) / (double)c.Layout.Plots.Length / .4);
            double s = Doctrine switch
            {
                Doctrine.Burn => Math.Sqrt(FireSim.PopulationOf(c) / popMax) * (1 - .7 * ruin),
                Doctrine.Works => (ind.Of(c)?.Points ?? 0) / worksMax * fire.Plant(c) + .4 * Math.Min(1, PlayerFrom.GetValueOrDefault(c.Name) / 100),      // the works, and the towns whose fields the bombers come from
                Doctrine.CounterAir => .3 + Math.Min(1, PlayerFrom.GetValueOrDefault(c.Name) / 80),      // the towns whose fields the bombers fly from
                _ => .3 + .7 * (1 - NearestField(c, ucs) / 500000),
            };
            if (revenge) s += .3 * Math.Sqrt(FireSim.PopulationOf(c) / popMax);
            s -= 2.5 * Math.Max(0, LossAt.GetValueOrDefault(c.Name, .08) - .08);      // where it was hurt before
            return Math.Max(.02, s + .15 * (rng.NextDouble() - .5));
        }
        var target = Pick(cities, Score, rng, .25);
        int waves = revenge ? 2 + rng.Next(2) : big ? 4 + rng.Next(3) : 1 + rng.Next(2);
        bool works = Doctrine == Doctrine.Works && !night;
        // the forecast, which is not perfect: no use sending a big raid into a storm, or going for the works when they cannot be seen
        {
            var fld = ucs.Where(b => b.S(Craft.SB3).Ready + b.S(Craft.TB6).Ready > 0).OrderBy(b => Dist(b.Field, target)).First(); double err = (rng.NextDouble() - .5) * .24, flight = Dist(fld.Field, target) / 110;
            double routeRain = RouteRain(camp.Seed, fld.Field, target, t) + err, targetCloud = Weather.At(camp.Seed, target.X, target.Y, t + flight).cloud + err;
            if (routeRain > (big ? .35 : .55) || works && targetCloud > .65) { Note(camp, $"holding back a {(big ? "big" : "small")} raid on {target.Name}: forecast rain {Math.Clamp(routeRain, 0, 1):P0} on the way, cloud {Math.Clamp(targetCloud, 0, 1):P0} there"); return; }
        }
        // counter-air: the field at that town, or now and then the radar nearest it
        string siteName = "";
        if (Doctrine == Doctrine.CounterAir && !revenge)
        {
            var radar = forces.Sites.All.Where(s => s.Kind == SiteKind.Radar && s.Working).OrderBy(s => Math.Abs(s.X - target.X) + Math.Abs(s.Y - target.Y)).FirstOrDefault();
            var field = forces.Sites.All.First(s => s.Kind == SiteKind.Field && s.Town == target); siteName = radar != null && rng.NextDouble() < .25 ? radar.Name : field.Name;
        }
        camp.Schedule(new CampaignEvent { T = t, Kind = Ev.UcsRaid, Target = target, Waves = waves, Works = works, Site = siteName });
        Raids++; if (big) { BigRaids++; LastBig = now; }
        scheduledToday = true;
        // what intelligence gets to hear of it: a big raid usually, a small one seldom; the numbers are rough, and the city is wrong one time in three
        if (rng.NextDouble() < (big ? .85 : revenge ? .6 : .35))
        {
            int roughly = (int)(Math.Round(waves * 81 * (.75 + .5 * rng.NextDouble()) / 10) * 10);
            Say(camp, rng.NextDouble() < .65 ? target : cities[rng.Next(cities.Count)], t, roughly, night);
        }
        RaidsOn[target.Name] = RaidsOn.GetValueOrDefault(target.Name) + 1;
        Note(camp, $"{(revenge ? "revenge for " + hitHard!.Name + ": " : big ? "big raid: " : "small raid: ")}{waves} waves on {(works ? "the works of " : "")}{target.Name}, {camp.Stamp(t)} ({Doctrine}, ready {readiness:P0}, cloud {cloud:P0}, moon {moon:P0})");
    }

    /// <summary>The worst rain a raid would meet on its way from a field to a target, leaving at this moment.</summary>
    public static double RouteRain(int seed, Place from, Place to, double t)
    {
        double worst = 0, d = Dist(from, to);
        for (int i = 0; i <= 8; i++) { double k = i / 8.0; worst = Math.Max(worst, Weather.At(seed, from.X + (to.X - from.X) * k, from.Y + (to.Y - from.Y) * k, t + d * k / 110).rain); }
        return worst;
    }
    /// <summary>At take-off, with the true weather: a raid heading into a storm stays on the ground. Returns true if it is called off.</summary>
    public bool Scrub(CampaignEvent e, Campaign camp, Forces forces)
    {
        if (e.Target == null) return false; var fld = forces.Side(false).Where(b => b.S(Craft.SB3).Ready + b.S(Craft.TB6).Ready > 0).OrderBy(b => Dist(b.Field, e.Target)).FirstOrDefault(); if (fld == null) return false;
        double rain = RouteRain(camp.Seed, fld.Field, e.Target, camp.Now); if (rain < (e.Waves >= 4 ? .55 : .75)) return false;
        Note(camp, $"called off the raid on {e.Target.Name}: rain {rain:P0} on the way");
        if (camp.Now < WarnUntil) { WarnUntil = 0; WarnTarget = ""; camp.Note("Intelligence: the expected raid seems to have been called off for the weather."); }
        return true;
    }

    /// <summary>After an operation: what its raids cost, and where. Heavy losses bring a stand-down, unless it is stubborn.</summary>
    public void Report(Campaign camp, Place? target, bool night, int sent, int lost, Random rng)
    {
        if (sent <= 0) return; double rate = lost / (double)sent;
        if (night) NightLoss = .7 * NightLoss + .3 * rate; else DayLoss = .7 * DayLoss + .3 * rate;
        if (target != null) LossAt[target.Name] = .6 * LossAt.GetValueOrDefault(target.Name, .08) + .4 * rate;
        // escorts: up when cut up, down when it gets through
        int was = EscortsPerBox; if (rate > .2) EscortsPerBox = Math.Min(10, EscortsPerBox + 2); else if (rate < .08) EscortsPerBox = Math.Max(2, EscortsPerBox - 1);
        if (EscortsPerBox != was) Note(camp, $"escorts {was} to {EscortsPerBox} a box after losing {rate:P0}");
        // a stand-down only after a raid far worse than it has come to expect, and not always even then
        double usual = (DayLoss + NightLoss) / 2;
        if (rate > Math.Max(.3, 1.6 * usual) && rng.NextDouble() < .6) { StandDownUntil = camp.Now + (3 + 4 * rng.NextDouble()) * Day; Note(camp, $"stand-down after losing {rate:P0} on {target?.Name}"); }
    }

    /// <summary>The weekly look at home: flak towards the cities hit, a badly hit works partly dispersed, and the factories' mix.</summary>
    public void Weekly(double now, Campaign camp, World w, Forces forces, FireSim fire, War war, Random rng)
    {
        var mine = w.Cities.Where(c => !c.Wolf).ToList(); var alarm = mine.ToDictionary(c => c, c => Alarm(c, fire, now)); double total = alarm.Values.Sum();
        // flak: a tenth of the way each week towards guns in proportion to alarm, never below half or above twice the usual
        foreach (var c in mine)
        {
            double want = total <= 0 ? 1 : Math.Clamp(.7 + 2.4 * alarm[c] / total, .5, 2), now0 = FlakOf(c);
            Flak[c.Name] = now0 + (want - now0) * .25;
        }
        double mean = mine.Average(FlakOf); foreach (var c in mine) Flak[c.Name] = FlakOf(c) / mean;      // guns are moved, not made
        // dispersal: a works more than a quarter wrecked may move a slice to the two smallest towns, losing a tenth of it on the way
        var ind = forces.Ind;
        foreach (var q in ind.Works.Where(q => !q.Wolf && fire.Plant(q.City) < .75 && q.Points > 8).ToList())
        {
            if (rng.NextDouble() > .5) continue; double move = q.Points * .15; q.Points -= move;
            foreach (var dest in ind.Works.Where(z => !z.Wolf && z != q).OrderBy(z => z.Points).Take(2)) dest.Points += move * .45;
            Note(camp, $"dispersing a sixth of the {q.City.Name} works");
        }
        // the factories: more fighters when the Vulfen are hurting it, more bombers when its own fleet is thin. One change in three weeks at most
        if (now - LastMixChange < 21 * Day) return;
        var ucs = forces.Side(false).ToList(); int bombers = ucs.Sum(b => b.S(Craft.TB6).Ready + b.S(Craft.TB6).ComingN + b.S(Craft.SB3).Ready + b.S(Craft.SB3).ComingN);
        Works? change = null; Product to = Product.Idle;
        if (war.LastShock[0] > 2.5 && rng.NextDouble() < .5) { change = ind.Works.Where(q => !q.Wolf && q.Making is Product.TB6 or Product.SB3).OrderBy(q => q.Points).FirstOrDefault(); to = Product.I8; }
        else if ((bombers < 300 || ucs.Sum(b => b.S(Craft.I8).Ready + b.S(Craft.I8).ComingN) > 900) && rng.NextDouble() < .5) { change = ind.Works.Where(q => !q.Wolf && q.Making == Product.I8).OrderBy(q => q.Points).FirstOrDefault(); to = bombers < 300 ? Product.SB3 : Product.TB6; }      // bombers thin, or fighters piling up with nothing to do
        if (change != null) { ind.Order(change, to, now); LastMixChange = now; Note(camp, $"the {change.City.Name} works retools for {Industry.Short(to)}"); }
    }

    /// <summary>Whether any of its bomber fields can reach a city and get home with a usual load.</summary>
    static bool Reach(Place c, List<Base> ucs) => ucs.Any(b => b.S(Craft.SB3).Ready + b.S(Craft.TB6).Ready > 0 && Weight.RangeKm(Design.Of[Craft.SB3], Design.Of[Craft.SB3].UsualKg) >= 2.2 * Dist(b.Field, c) / 1000);
    static double NearestField(Place c, List<Base> ucs) => ucs.Where(b => b.S(Craft.SB3).Ready + b.S(Craft.TB6).Ready > 0).Select(b => Dist(b.Field, c)).DefaultIfEmpty(1e9).Min();
    static double Dist(Place a, Place b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
    /// <summary>A weighted draw. A low temperature sharpens it towards the best; scores are first scaled to the best one.</summary>
    static T Pick<T>(IList<T> items, Func<T, double> score, Random rng, double temp = 1)
    {
        var s = items.Select(score).ToList(); double top = s.Max(); var wts = s.Select(v => Math.Exp((v - top) / Math.Max(.01, temp * Math.Max(.05, top)))).ToList();
        double r = rng.NextDouble() * wts.Sum(); for (int i = 0; i < items.Count; i++) { r -= wts[i]; if (r <= 0) return items[i]; }
        return items[^1];
    }
    void Note(Campaign camp, string s) { Notes.Add($"{camp.Stamp(camp.Now)}  {s}"); if (Notes.Count > 200) Notes.RemoveAt(0); }
}
