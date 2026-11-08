using System;
using System.Collections.Generic;

namespace Firebomber;

/// <summary>The weather of the war, worked out from the campaign's seed alone, so it needs no saving and any moment can be asked about in advance.
/// Fronts come in from the west-south-west every few days and take two or three days to cross the map; each has its own strength, width, length and tilt, so one may rake the whole theatre and the next only clip a corner. Between them is fair weather with scattered cloud.
/// Only the core of a strong front brings heavy rain, so storms are real but not common. The ground dries over the week after rain has passed. Wind wanders round the prevailing west-south-westerly, and blows harder as a front goes through. All numbers are first suggestions.</summary>
public static class Weather
{
    sealed class Front { public double T0, V, Dx, Dy, Width, Str, Len, Q0, Life; public int K; }
    static readonly Dictionary<int, List<Front>> all = new();
    const double Day = 86400, Km = 1000, Start = -1400 * Km, Path = 2800 * Km;
    static readonly double Cx = World.WidthM / 2, Cy = World.HeightM / 2;

    /// <summary>How unsettled the season is: more so in spring and autumn.</summary>
    static double Season(double t) => .5 + .15 * Math.Cos((Campaign.Epoch.AddSeconds(t).DayOfYear - 100) / 365.0 * Math.Tau * 2);
    /// <summary>1 at midsummer, -1 at midwinter.</summary>
    static double Summer(double t) => Math.Cos((Campaign.Epoch.AddSeconds(t).DayOfYear - 190) / 365.0 * Math.Tau);

    static List<Front> Fronts(int seed, double until)
    {
        if (!all.TryGetValue(seed, out var list)) { all[seed] = list = new List<Front>(); list.Add(Make(seed, 0, -3 * Day)); }
        while (list[^1].T0 < until) { var last = list[^1]; double H = N.Hash(last.K, seed, 51), gap = (1.8 + 6 * Math.Pow(H, 1.5)) * (1.5 - Season(last.T0)) * Day; list.Add(Make(seed, last.K + 1, last.T0 + gap)); }
        return list;
    }
    static Front Make(int seed, int k, double t0)
    {
        double H(int s) => N.Hash(k, seed, s); double ang = (20 + 70 * (H(52) - .5)) * Math.PI / 180, v = 6 + 7 * H(53);
        return new Front { K = k, T0 = t0, V = v, Dx = Math.Cos(ang), Dy = Math.Sin(ang), Width = (190 + 200 * H(54)) * Km, Str = .3 + .7 * H(55) * H(56) * 1.6, Len = (500 + 700 * H(57)) * Km, Q0 = (H(58) - .5) * 700 * Km, Life = Path / v };
    }
    static IEnumerable<Front> Active(int seed, double t)
    {
        var l = Fronts(seed, t + Day); int lo = 0, hi = l.Count; double from = t - 6 * Day;
        while (lo < hi) { int m = (lo + hi) / 2; if (l[m].T0 < from) lo = m + 1; else hi = m; }
        for (int i = lo; i < l.Count && l[i].T0 <= t; i++) if (t <= l[i].T0 + l[i].Life) yield return l[i];
    }
    /// <summary>How much of a front is overhead here, 0-1.</summary>
    static double Over(Front f, double x, double y, double t, int seed)
    {
        double rx = x - Cx, ry = y - Cy, q = rx * -f.Dy + ry * f.Dx, s = rx * f.Dx + ry * f.Dy, at = Start + f.V * (t - f.T0);
        double wave = 70 * Km * N.S(q / (260 * Km), seed + f.K), lat = (q - f.Q0) / f.Len, d = (s - at + wave) / f.Width;
        return Math.Exp(-d * d) * Math.Exp(-lat * lat);
    }

    /// <summary>Cloud cover and rain here at this moment, each 0-1. Rain above about .6 is a storm.</summary>
    public static (double cloud, double rain) At(int seed, double x, double y, double t)
    {
        double b = .84 + .32 * N.V(x / (70 * Km) + t / (18 * 3600), y / (70 * Km), seed + 9);
        double cloud = .1 + .1 * Season(t) + .36 * N.V(x / (260 * Km) + t / (1.5 * Day), y / (260 * Km), seed + 7), rain = 0;
        foreach (var f in Active(seed, t))
        {
            double g = Over(f, x, y, t, seed); cloud = Math.Max(cloud, Math.Pow(g, .6) * (.62 + .48 * Math.Min(1, f.Str)) * b);
            double core = Math.Clamp((g - .5) / .42, 0, 1); rain = Math.Max(rain, core * core * (3 - 2 * core) * Math.Min(1, f.Str) * b);
        }
        return (Math.Clamp(cloud, 0, 1), Math.Clamp(rain, 0, 1));
    }

    /// <summary>The wind: how hard here, and where from in degrees.</summary>
    public static (double speed, double from) WindAt(int seed, double t, double x = double.NaN, double y = double.NaN)
    {
        if (double.IsNaN(x)) { x = Cx; y = Cy; }
        double from = (250 + 80 * N.S(t / (3 * Day), seed + 11) + 360) % 360, speed = 2 + 9 * Math.Pow(N.V(t / (2.2 * Day), .61, seed + 12), 1.5);
        foreach (var f in Active(seed, t)) speed += 18 * Math.Pow(Math.Min(1, f.Str), 1.5) * Over(f, x, y, t, seed);      // gales come with the strong fronts, as they pass
        return (Math.Min(speed, 28), from);
    }
    /// <summary>The wind as the fire model wants it: metres a second, blowing towards.</summary>
    public static (double x, double y) WindVec(int seed, double t) { var (s, from) = WindAt(seed, t); double a = from * Math.PI / 180; return (-Math.Sin(a) * s, -Math.Cos(a) * s); }

    /// <summary>How dry the ground is here, 0-1: the days since a front's rain went over it, and the season.</summary>
    public static double Dryness(int seed, double x, double y, double t)
    {
        var l = Fronts(seed, t + Day); double since = 9 * Day, rx = x - Cx, ry = y - Cy;
        for (int i = l.Count - 1; i >= 0 && l[i].T0 > t - 16 * Day; i--)
        {
            var f = l[i]; if (f.Str < .35) continue; double q = (rx * -f.Dy + ry * f.Dx - f.Q0) / f.Len; if (Math.Abs(q) > 1.1) continue;
            double passed = f.T0 + (rx * f.Dx + ry * f.Dy - Start) / f.V; if (passed <= t) since = Math.Min(since, t - passed); else if (passed - t < f.Width / f.V) since = 0;
        }
        return Math.Clamp(since / (8 * Day) + .14 * Summer(t), 0, 1);
    }
    public static string Rainy(double rain) => rain > .6 ? "storm" : rain > .3 ? "rain" : rain > .08 ? "light rain" : "dry";
}
