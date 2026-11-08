using System;
using System.Collections.Generic;
using System.Linq;

namespace Firebomber;

public enum Ev { Day, Week, UcsRaid }

/// <summary>Something due at a moment of the war. UCS raids sit in the queue unseen: the player learns of them from radar.</summary>
public sealed class CampaignEvent { public double T; public Ev Kind; public Place? Target; public int Waves = 2; public bool Works; public string Site = ""; }

/// <summary>One line of the war diary.</summary>
public readonly record struct LogLine(double T, string Text, bool Alarm);

/// <summary>The war's one running clock. Seconds since the campaign began; the calendar is worked out from it.
/// Two layers: operations (raids, fighters, fires) run second by second while anything is in the air or burning;
/// persistent things (cities, deliveries, stocks, plans) change only on the daily tick at 06:00 and the weekly tick each Monday.
/// This class only keeps time, the queue and the diary; Main decides what an event does to the world.</summary>
public sealed class Campaign
{
    /// <summary>A placeholder start: a Monday morning in 1939. The weekly tick falls every seventh day from here.</summary>
    public static readonly DateTime Epoch = new(1939, 4, 3, 6, 0, 0);
    public const double Day = 86400, Week = 7 * Day;
    public double Now { get; private set; }
    public DateTime Date => Epoch.AddSeconds(Now);
    public int DayNo => (int)(Now / Day) + 1;
    public int WeekNo => (int)(Now / Week) + 1;
    public double Hour => HourAt(Now);
    public static double HourAt(double t) => (6 + t / 3600) % 24;
    public readonly List<LogLine> Log = new();
    readonly PriorityQueue<CampaignEvent, (double, int)> queue = new(); int order;
    readonly World w; Random rng; public readonly int Seed;
    /// <summary>Called for each event as it falls due, with the clock standing at its moment.</summary>
    public Action<CampaignEvent>? Due;

    public Campaign(World world, int seed = 5)
    {
        w = world; Seed = seed; rng = new Random(seed);
        Schedule(new CampaignEvent { T = Day, Kind = Ev.Day }); Schedule(new CampaignEvent { T = Week, Kind = Ev.Week });
        Note("The war diary opens.");
    }
    /// <summary>The UCS planner, set by the game. Without one (old tests) the placeholder rolls dice.</summary>
    public Action? PlanDaily;
    /// <summary>The weather at the middle of the map, and the moon's light 0-1 (new to full and back every 29.5 days).</summary>
    public static (double cloud, double moon) WeatherAt(int seed, double t)
    {
        double phase = ((t / Day + 11) % 29.53) / 29.53, moon = (1 - Math.Cos(phase * Math.Tau)) / 2;
        return (Weather.At(seed, World.WidthM / 2, World.HeightM / 2, t).cloud, moon);
    }

    /// <summary>A campaign read back from a save: the clock, the queue and the diary as they were. The planner's dice are reseeded from the day, so they need not be saved.</summary>
    public static Campaign Restore(World world, int seed, double now, IEnumerable<CampaignEvent> events, IEnumerable<LogLine> log)
    {
        var c = new Campaign(world, seed); c.queue.Clear(); c.Log.Clear(); c.Now = now; c.rng = new Random(seed + c.DayNo * 7919);
        foreach (var e in events) c.Schedule(e); c.Log.AddRange(log); return c;
    }
    /// <summary>Everything still to come, for saving.</summary>
    public IEnumerable<CampaignEvent> Pending => queue.UnorderedItems.Select(q => q.Element).OrderBy(e => e.T);

    public void Schedule(CampaignEvent e) => queue.Enqueue(e, (e.T, order++));
    public void Note(string text, bool alarm = false) => Log.Add(new LogLine(Now, text, alarm));
    public string Stamp(double t) { var d = Epoch.AddSeconds(t); return d.ToString("ddd d MMM, HH:mm", System.Globalization.CultureInfo.InvariantCulture); }
    public string DateText => Date.ToString("ddd d MMMM yyyy, HH:mm", System.Globalization.CultureInfo.InvariantCulture).ToUpperInvariant();

    /// <summary>When the next thing the player may know of falls due: a daily or weekly tick. Unseen UCS raids are not told.</summary>
    public double NextKnown => queue.UnorderedItems.Where(q => q.Element.Kind != Ev.UcsRaid).Select(q => q.Element.T).DefaultIfEmpty(double.MaxValue).Min();

    /// <summary>Moves the clock towards a moment, firing whatever falls due on the way, in order. Stops early, at the event, when <paramref name="stop"/> says the world has become busy, so an operation never starts in the middle of a long jump.</summary>
    public void RunTo(double t, Func<bool>? stop = null)
    {
        while (queue.TryPeek(out var e, out var at) && at.Item1 <= t)
        {
            queue.Dequeue(); Now = Math.Max(Now, e.T);
            switch (e.Kind)
            {
                case Ev.Day: Schedule(new CampaignEvent { T = e.T + Day, Kind = Ev.Day }); PlanUcs(); break;
                case Ev.Week: Schedule(new CampaignEvent { T = e.T + Week, Kind = Ev.Week }); Note($"Week {WeekNo} begins."); break;
            }
            Due?.Invoke(e);
            if (stop != null && stop()) return;
        }
        Now = Math.Max(Now, t);
    }

    /// <summary>UCS raids that fall due before this moment, taken out of the queue so one operation can carry them.</summary>
    public List<CampaignEvent> TakeUcsRaids(double until)
    {
        var all = new List<(CampaignEvent e, (double, int) p)>(); var take = new List<CampaignEvent>();
        while (queue.TryDequeue(out var e, out var p)) all.Add((e, p));
        foreach (var (e, p) in all) if (e.Kind == Ev.UcsRaid && e.T <= until) take.Add(e); else queue.Enqueue(e, p);
        return take;
    }

    /// <summary>A stand-in for the UCS planner, until the real one is built (roadmap step 3): on some days it sends a raid, by night more often than by day, at a Vulfen city chosen by size.</summary>
    void PlanUcs()
    {
        if (PlanDaily != null) { PlanDaily(); return; }
        if (rng.NextDouble() > .3) return;
        var ours = w.Cities.Where(c => c.Wolf).ToList(); if (ours.Count == 0) return;
        double total = ours.Sum(c => c.Size * c.Size), pick = rng.NextDouble() * total; var target = ours[^1];
        foreach (var c in ours) { pick -= c.Size * c.Size; if (pick <= 0) { target = c; break; } }
        bool night = rng.NextDouble() < .65; double hour = night ? 22 + rng.NextDouble() * 3 : 10 + rng.NextDouble() * 3;      // hours after 06:00 of this day
        double dayStart = Math.Floor(Now / Day) * Day, t = dayStart + (hour - 6) * 3600;
        if (t <= Now + 600) t += Day;
        Schedule(new CampaignEvent { T = t, Kind = Ev.UcsRaid, Target = target, Waves = 2 + rng.Next(3) });
    }
}
