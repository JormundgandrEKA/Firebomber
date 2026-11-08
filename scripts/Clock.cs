using System;

namespace Firebomber;

/// <summary>Simulation time. The one place that knows about pause and speed; everything that simulates will take its time step from here.</summary>
public sealed class Clock
{
    /// <summary>The first five are for operations. The last two pass whole hours and are only for the campaign, when nothing is flying or burning.</summary>
    public static readonly int[] Speeds = { 1, 4, 16, 64, 256, 3600, 21600 };
    public const int OpMax = 4;
    public static string Label(int i) => Speeds[i] >= 3600 ? $"{Speeds[i] / 3600}H/S" : $"x{Speeds[i]}";
    public double T { get; private set; }
    public bool Paused = true;
    public int SpeedIdx;

    public double Advance(double realDelta)
    {
        if (Paused) return 0;
        double step = realDelta * Speeds[SpeedIdx];
        T += step;
        return step;
    }
    public void Skip(double seconds) => T += seconds;
    public void SetSpeed(int i) { SpeedIdx = Math.Clamp(i, 0, Speeds.Length - 1); Paused = false; }
    public string Text { get { int s = (int)T; return s >= 3600 ? $"T+{s / 3600}:{s / 60 % 60:00}:{s % 60:00}" : $"T+{s / 60:00}:{s % 60:00}"; } }
}
