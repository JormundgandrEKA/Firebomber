using System;
using System.Collections.Generic;
using System.Linq;

namespace Firebomber;

/// <summary>One building. Res and Work: the people who live and work in it (see CityLife).</summary>
public struct Bld { public float X, Y, W, D, Ang, Heat, Burn, HeatT, Thr, Res, Work; public byte Mat, State, Hp; public bool Open, Away, Stormed, Weak, Volatile; }

/// <summary>One city's buildings and fires. Made the first time something falls on the city.</summary>
public sealed class CityFire
{
    public Place P = null!; public Bld[] B = { };
    // a grid over the city: each cell's chain of buildings, and how many there can still catch
    public int GX0, GY0, GW, GH; public int[] Head = { }, Next = { }, Live = { };
    // a coarser grid for how hot each part of the city is
    public int HX0, HY0, HW, HH, SeenMark = 1; public float[] Hot = { }; public int[] Seen = { }; public bool[] InStorm = { };
    public int HotIdx(float x, float y) => ((int)Math.Floor(y / 150) - HY0) * HW + (int)Math.Floor(x / 150) - HX0;
    public float SurveyT, Hold;
    public readonly HashSet<int> Burning = new();
    public readonly Dictionary<long, (float bank, float at)> Districts = new();     // what each district's crews have left
    public readonly int[,] Count = new int[3, 4];      // material, state
    public List<(double x, double y, float i)> Cells = new();
    public float Peak, Brigade, BrigadeAcc, StormSecs, Tinder = 1, WxArea = 1, WxHold = 1, WxHeat = 1, WxWind = 6, WxEmber = 0; public double AlarmT = -1e12, LastBurnT = -1e12; public int StormCells; public bool Storm;
}

/// <summary>Bombs on buildings, and fire among them. High explosive knocks buildings down and lights few fires. Incendiaries knock nothing down but start many, if the building will take.
/// A fire heats its neighbours; a neighbour catches when heat builds faster than it cools. So wood packed tight burns away street after street, while brick works and thin suburbs mostly do not spread.
/// Enough burning close together makes a fire that feeds itself: that is the firestorm.</summary>
public sealed partial class FireSim
{
    public const byte Intact = 0, Burning = 1, Gutted = 2, Blasted = 3;
    // per material: wood, brick, works
    static readonly float[] IgniteAt = { 1, 4, 9 }, BurnFor = { 1200, 1800, 2700 }, Output = { 1, .55f, .5f }, BlastR = { 18.9f, 14, 0 }, FireChanceHe = { .02f, .008f, .08f }, TakeInc = { .08f, .03f, .06f }, CrewCost = { 1, 1.5f, 5 };   // a burning works takes many crews
    /// <summary>The share of fires crews never get to: a roof space, no water to hand, a blocked street. They burn out whatever else happens, so no raid that lights fires leaves a city unmarked.</summary>
    public static double EscapeBomb = .35, EscapeSpread = .02; const double EscapeHe = .6, EscapeTinder = .25;      // a fire in a wrecked building, with the crews under cover
    /// <summary>Crews belong to a district and cannot be everywhere: no one district gets more than this share of the city's crews. A tight pattern of bombs swamps the districts it falls on while the rest of the city holds.</summary>
    public static float DistrictShare = .2f; const float DistrictM = 600, PinSecs = 900;
    static long DistrictOf(float x, float y) => ((long)Math.Floor(y / DistrictM) << 32) ^ (uint)(int)Math.Floor(x / DistrictM);
    const float YoungFire = 420, CellM = 32, HeatPerMin = .6f, CoolSecs = 600, BaseRad = 28;
    // A firestorm needs a great area burning at once, in one piece, and for some time. A single cell of 150 m is hot at HotCell; StormArea cells is about 1.2 square kilometres
    public const float HotCell = 38, StormDrive = 3, SurveyEvery = 30, StormHold = 600; public const int StormArea = 54;
    const float OpenR = 2.4f, OpenTake = 4, OpenHeat = .75f; const int FactoryHp = 2, Bomblets = 42; const double BombletSigma = 55, FireCell = 150;
    public double WindX = 5.6, WindY = 2.0;      // blowing towards, m/s
    public readonly List<CityFire> Cities = new();
    public readonly List<(CityFire c, int i)> Changed = new();
    public readonly List<(double x, double y, bool he)> Craters = new();
    readonly World w; readonly Random rng; double t;
    readonly Dictionary<Place, System.Threading.Tasks.Task<CityFire>> preparing = new();
    public FireSim(World world, int seed = 11) { w = world; rng = new Random(seed); }

    public string WindText { get { double from = (Math.Atan2(-WindX, -WindY) * 180 / Math.PI + 360) % 360; return $"WIND {from:000}° {Math.Sqrt(WindX * WindX + WindY * WindY):0} m/s"; } }

    /// <summary>Work out a city's buildings on another thread, ahead of the first bomb, so there is no pause when it falls.</summary>
    public void Prepare(Place p) { if (!preparing.ContainsKey(p) && Cities.All(c => c.P != p)) preparing[p] = System.Threading.Tasks.Task.Run(() => Make(p)); }
    public CityFire Materialise(Place p)
    {
        var have = Cities.FirstOrDefault(c => c.P == p); if (have != null) return have;
        Prepare(p); var made = preparing[p].Result; preparing.Remove(p); Cities.Add(made);
        return made;
    }
    CityFire Make(Place p)
    {
        var list = new List<Bld>(); double reach = p.R * 2.4;
        var places = w.PlacesNear(p.X - reach, p.Y - reach, p.X + reach, p.Y + reach);
        void One(Plot h)
        {
            list.Add(new Bld { Ang = (float)h.Ang, X = (float)(h.X - p.X), Y = (float)(h.Y - p.Y), W = (float)h.W, D = (float)h.D, Mat = h.Mat, Hp = (byte)(h.Mat == CityPlan.Factory ? FactoryHp : 1), Volatile = h.Mat == CityPlan.Factory && N.Hash((int)h.X, (int)h.Y, 333) < .15,
                // some buildings and some whole patches catch less easily, so a fire front is ragged and leaves survivors
                Thr = IgniteAt[h.Mat] * (float)((.65 + .8 * N.Hash((int)h.X, (int)h.Y, 321)) * (.65 + 1.5 * Math.Pow(N.V(h.X / 240, h.Y / 240, 322), 2))) });
        }
        if (p.Layout != null) foreach (var h in p.Layout.Plots) One(h);
        else CityPlan.Enumerate(w, p, places, p.X - reach, p.Y - reach, p.X + reach, p.Y + reach, h => { if (!w.IsWater(h.X, h.Y, 16)) One(h); }, null);
        var c = new CityFire { P = p, B = list.ToArray(), Next = new int[list.Count], Brigade = FullBrigade(p), Tinder = (float)(p.Style?.Tinder ?? 1) };
        float x0 = 0, y0 = 0, x1 = 0, y1 = 0; foreach (ref var b in c.B.AsSpan()) { x0 = Math.Min(x0, b.X); y0 = Math.Min(y0, b.Y); x1 = Math.Max(x1, b.X); y1 = Math.Max(y1, b.Y); }
        c.GX0 = (int)Math.Floor(x0 / CellM) - 1; c.GY0 = (int)Math.Floor(y0 / CellM) - 1; c.GW = (int)Math.Floor(x1 / CellM) - c.GX0 + 2; c.GH = (int)Math.Floor(y1 / CellM) - c.GY0 + 2;
        c.Head = new int[c.GW * c.GH]; Array.Fill(c.Head, -1); c.Live = new int[c.GW * c.GH];
        c.HX0 = (int)Math.Floor(x0 / FireCell) - 1; c.HY0 = (int)Math.Floor(y0 / FireCell) - 1; c.HW = (int)Math.Floor(x1 / FireCell) - c.HX0 + 2; c.HH = (int)Math.Floor(y1 / FireCell) - c.HY0 + 2;
        c.Hot = new float[c.HW * c.HH]; c.Seen = new int[c.HW * c.HH]; c.InStorm = new bool[c.HW * c.HH];
        for (int i = 0; i < c.B.Length; i++)
        {
            int g = GridOf(c, c.B[i].X, c.B[i].Y);
            c.Next[i] = c.Head[g]; c.Head[g] = i; c.Live[g]++;
            c.Count[c.B[i].Mat, Intact]++;
        }
        Occupy(c);
        return c;
    }
    static int GridOf(CityFire c, float x, float y) => ((int)Math.Floor(y / CellM) - c.GY0) * c.GW + (int)Math.Floor(x / CellM) - c.GX0;
    /// <summary>The chain of buildings in a grid cell, or none if it is off the grid.</summary>
    static int ChainAt(CityFire c, int i, int j) { i -= c.GX0; j -= c.GY0; return i < 0 || j < 0 || i >= c.GW || j >= c.GH ? -1 : c.Head[j * c.GW + i]; }

    void Set(CityFire c, int i, byte state)
    {
        ref var b = ref c.B[i];
        if (state is Gutted or Blasted && b.State is Intact or Burning) Harm(c, ref b, state);
        c.Count[b.Mat, b.State]--; c.Count[b.Mat, state]++;
        if (b.State == Burning) c.Burning.Remove(i);
        if (b.State == Intact) c.Live[GridOf(c, b.X, b.Y)]--; else if (state == Intact) c.Live[GridOf(c, b.X, b.Y)]++;
        b.State = state; Changed.Add((c, i));
        if (b.Volatile && state is Burning or Blasted) { b.Volatile = false; goingUp.Add((c, i)); }
        if (state == Burning) { b.Burn = 0; c.Burning.Add(i); }
    }

    readonly List<(CityFire c, int i)> goingUp = new();
    public void ClearSecondaries() => goingUp.Clear();
    /// <summary>Chemicals, fuel and munitions in the works: when the building goes, it goes with a bang, and takes what is near it. One can set off the next.</summary>
    void Secondaries()
    {
        for (int n = 0; n < goingUp.Count; n++)
        {
            var (c, i) = goingUp[n]; float x = c.B[i].X, y = c.B[i].Y; Craters.Add((x + c.P.X, y + c.P.Y, true));
            Around(c, x, y, 2, j =>
            {
                ref var b = ref c.B[j]; if (j == i || b.State >= Gutted) return; float dx = b.X - x, dy = b.Y - y; if (dx * dx + dy * dy > 42 * 42) return;
                if (b.Mat == CityPlan.Factory) { b.Open = true; b.Hp = (byte)Math.Max(0, b.Hp - 1); if (b.Hp == 0) { Set(c, j, Blasted); return; } if (b.State == Intact && rng.NextDouble() < .4) Light(c, j, EscapeHe, true); }
                else Set(c, j, Blasted);
            });
        }
        goingUp.Clear();
    }
    void Light(CityFire c, int i, double escape, bool weak = false) { Set(c, i, Burning); c.B[i].Away = rng.NextDouble() < escape; c.B[i].Weak = weak; }
    public void Hit(Impact h)
    {
        if (h.Water) return;
        Craters.Add((h.X, h.Y, !h.Incendiary));
        Place? near = null; foreach (var p in w.Cities) if (Math.Abs(p.X - h.X) < p.R * 2.4 && Math.Abs(p.Y - h.Y) < p.R * 2.4) near = p;
        if (near == null) return;
        Record(near, h);
        var c = Materialise(near); float x = (float)(h.X - near.X), y = (float)(h.Y - near.Y);
        double scale = Math.Cbrt(Math.Max(h.Kg, 50) / 250.0);      // blast grows with the cube root of the weight
        if (h.Incendiary)
            for (int k = 0; k < (h.Bomblets > 0 ? h.Bomblets : Bomblets) ; k++)
            {
                double u1 = 1 - rng.NextDouble(), r = BombletSigma * Math.Sqrt(Math.Max(h.Kg, 50) / 250.0) * Math.Sqrt(-2 * Math.Log(u1)), a = rng.NextDouble() * Math.Tau;
                float bx = x + (float)(r * Math.Cos(a)), by = y + (float)(r * Math.Sin(a));
                int best = Nearest(c, bx, by); if (best < 0) continue;
                ref var b = ref c.B[best];
                if (b.State == Intact && rng.NextDouble() < TakeInc[b.Mat] * (b.Open ? OpenTake : 1) * (h.Kg < 100 ? 3 : 1)) Light(c, best, EscapeBomb);
            }
        else
        {
            // high explosive sends the crews of that district to shelter for a quarter of an hour: fire that falls while they are under cover gets away
            { float rate = c.Brigade * DistrictShare / 60; c.Districts[DistrictOf(x, y)] = (-rate * PinSecs, (float)t); }
            c.Brigade = Math.Max(c.Brigade * (float)Math.Pow(.999, Math.Max(h.Kg, 100) / 250.0), .3f * FullBrigade(c.P));      // mains, roads and crews: high explosive wears down the people who would put the fires out, but never all of them
            Around(c, x, y, (int)Math.Ceiling(BlastR[0] * OpenR * scale / CellM) + 1, i =>
            {
                ref var b = ref c.B[i]; if (b.State >= Gutted) return;
                float dx = b.X - x, dy = b.Y - y, d = MathF.Sqrt(dx * dx + dy * dy);
                if (b.Mat == CityPlan.Factory)
                {
                    // works stand up to blast: a shed takes several direct hits
                    if (d < Math.Max(b.W, b.D) * .55f + 5.6f * (float)scale) { b.Open = true; int bite = h.Kg >= 250 ? (int)Math.Round(h.Kg / 250.0) : rng.NextDouble() < .4 ? 1 : 0; b.Hp = (byte)Math.Max(0, b.Hp - bite); if (b.Hp == 0) { Set(c, i, Blasted); return; } if (b.State == Intact && rng.NextDouble() < FireChanceHe[b.Mat]) Light(c, i, EscapeBomb, true); }
                    return;
                }
                if (d < BlastR[b.Mat] * scale) { Set(c, i, Blasted); return; }
                if (d < BlastR[b.Mat] * OpenR * scale)
                {
                    b.Open = true;      // roof and windows gone: this building now takes fire far more easily
                    if (b.State == Intact && rng.NextDouble() < FireChanceHe[b.Mat]) Light(c, i, EscapeHe, true);      // a fire started by blast burns where it is and spreads little: HE works by blast
                }
            });
        }
        Secondaries();
    }
    /// <summary>A wreck coming down: a crater, and if it lands on a building, probably a fire.</summary>
    public void Crash(double x, double y)
    {
        if (w.IsWater(x, y, 64)) return;
        Craters.Add((x, y, true));
        var p = w.CityNear(x, y); if (p == null) return;
        var c = Materialise(p); int b = Nearest(c, (float)(x - p.X), (float)(y - p.Y));
        if (b >= 0 && c.B[b].State == Intact && rng.NextDouble() < .7) Light(c, b, EscapeBomb);
    }
    static int Nearest(CityFire c, float x, float y)
    {
        int cx = (int)Math.Floor(x / CellM), cy = (int)Math.Floor(y / CellM), best = -1; float bd = 1e9f;
        for (int i = cx - 1; i <= cx + 1; i++) for (int j = cy - 1; j <= cy + 1; j++)
            {
                for (int n = ChainAt(c, i, j); n >= 0; n = c.Next[n])
                {
                    ref var b = ref c.B[n]; float dx = Math.Abs(b.X - x) - b.W / 2, dy = Math.Abs(b.Y - y) - b.D / 2;
                    if (dx > 2 || dy > 2) continue;
                    float d = dx + dy; if (d < bd) { bd = d; best = n; }
                }
            }
        return best;
    }
    static void Around(CityFire c, float x, float y, int cells, Action<int> act)
    {
        int cx = (int)Math.Floor(x / CellM), cy = (int)Math.Floor(y / CellM);
        for (int i = cx - cells; i <= cx + cells; i++) for (int j = cy - cells; j <= cy + cells; j++)
            for (int n = ChainAt(c, i, j); n >= 0; n = c.Next[n]) act(n);
    }

    /// <summary>Bring the fires up to this simulated time. With a budget, stops when its time is up and carries on next frame: at high speed the fire takes longer steps rather than slowing the game.</summary>
    /// <summary>The fire's own time, which may lag the clock at high speed; and whether anything is burning anywhere.</summary>
    public double T => t;
    public bool AnyBurning => Cities.Any(c => c.Burning.Count > 0);
    /// <summary>A city with a real fire, not the last few buildings smouldering. While one rages the campaign clock stays at operational speed.</summary>
    public bool Raging => Cities.Any(c => c.Burning.Count > 150);
    public void SyncTo(double T, double budgetMs = 1e9)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (true)
        {
            double step = Math.Clamp((T - t) / 3, 5, 30);
            if (t + step > T) return;
            t += step;
            foreach (var c in Cities) if (c.Burning.Count > 0) Step(c, (float)step);
            if (sw.Elapsed.TotalMilliseconds > budgetMs) return;
        }
    }

    static float Env(float f) => .3f + .7f * MathF.Sin(MathF.PI * Math.Min(1, f));

    /// <summary>Every half minute: how hot each part of the city is, and whether enough of it is burning at once, in one piece, for long enough to make a firestorm.</summary>
    void Survey(CityFire c)
    {
        Array.Clear(c.Hot);
        foreach (int i in c.Burning) { ref var b = ref c.B[i]; c.Hot[c.HotIdx(b.X, b.Y)] += Output[b.Mat] * Env(b.Burn / BurnFor[b.Mat]); }
        c.Cells.Clear(); c.Peak = 0;
        foreach (int i in c.Burning) { ref var b = ref c.B[i]; int k = c.HotIdx(b.X, b.Y); if (c.Seen[k] == c.SeenMark) continue; c.Seen[k] = c.SeenMark; c.Peak = Math.Max(c.Peak, c.Hot[k]); c.Cells.Add(((k % c.HW + .5) * FireCell + c.HX0 * FireCell + c.P.X, (k / c.HW + .5) * FireCell + c.HY0 * FireCell + c.P.Y, c.Hot[k])); }
        c.SeenMark++;
        // the largest single patch of hot ground
        int largest = 0; Array.Clear(c.InStorm); var stack = new Stack<int>(); var patch = new List<int>(); var all = new List<List<int>>();
        foreach (var (x, y, v) in c.Cells)
        {
            int k0 = c.HotIdx((float)(x - c.P.X), (float)(y - c.P.Y)); if (v < HotCell || c.Seen[k0] == c.SeenMark) continue;
            patch = new List<int>(); stack.Push(k0); c.Seen[k0] = c.SeenMark;
            while (stack.Count > 0)
            {
                int k = stack.Pop(); patch.Add(k); int kx = k % c.HW, ky = k / c.HW;
                for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = kx + dx, ny = ky + dy; if (nx < 0 || ny < 0 || nx >= c.HW || ny >= c.HH) continue;
                        int n = ny * c.HW + nx; if (c.Seen[n] != c.SeenMark && c.Hot[n] >= HotCell) { c.Seen[n] = c.SeenMark; stack.Push(n); }
                    }
            }
            all.Add(patch); largest = Math.Max(largest, patch.Count);
        }
        c.SeenMark++;
        if (Climate != null) { var (dry, wind, rain) = Climate(c.P); (c.WxArea, c.WxHold, c.WxHeat) = FireDay(dry, wind, rain); c.WxWind = (float)wind; c.WxEmber = Embers(dry, wind, rain); }
        c.Hold = largest >= StormArea * c.WxArea ? c.Hold + SurveyEvery : Math.Max(0, c.Hold - SurveyEvery);
        if (!c.Storm && c.Hold >= StormHold * c.WxHold) { c.Storm = true; var r = Rec(c.P); if (t - r.LastStormT > 6 * 3600) r.Firestorms++; r.LastStormT = t; }
        if (c.Storm && largest < StormArea / 2) { c.Storm = false; c.Hold = 0; }
        if (c.Storm)
        {
            c.StormSecs += SurveyEvery; c.StormCells = Math.Max(c.StormCells, largest);
            foreach (var p in all) if (p.Count >= StormArea / 2) foreach (int k in p) c.InStorm[k] = true;
            foreach (int i in c.Burning) { ref var b = ref c.B[i]; if (c.InStorm[c.HotIdx(b.X, b.Y)]) b.Stormed = true; }      // caught in it: whoever is inside dies as in a firestorm, however long the building then takes to burn out
        }
    }

    void Step(CityFire c, float dt)
    {
        float ws = (float)Math.Sqrt(WindX * WindX + WindY * WindY), wx = (float)WindX / ws, wy = (float)WindY / ws, windPush = .3f + .07f * c.WxWind;
        c.SurveyT -= dt; if (c.SurveyT <= 0) { c.SurveyT += SurveyEvery; Survey(c); }

        // crews put out what they can reach while it is still small. One fire is nothing; thousands at once are beyond them
        float full = FullBrigade(c.P); c.Brigade += (full - c.Brigade) * (1 - MathF.Exp(-dt / 1200)) * (c.Burning.Count > 400 ? 0 : 1);   // they regroup once the bombing stops, unless overwhelmed
        // the crews are at their stations when it starts and take twenty minutes and more to get into action; and there is only so much they can do: past a dozen minutes' work at once the water fails and they fall back
        if (t - c.LastBurnT > 3600) c.AlarmT = t;
        c.LastBurnT = t;
        float turnout = .12f + .88f * (1 - MathF.Exp(-(float)(t - c.AlarmT) / 1200)), most = full * 12, swamped = Math.Clamp(1 - (c.Burning.Count - most) / (3 * most), .1f, 1), crews = c.Brigade * turnout * swamped;
        c.BrigadeAcc += crews * dt / 60;
        if (c.BrigadeAcc >= 1)
        {
            var doused = new List<int>();
            float localRate = crews * DistrictShare / 60, nowT = (float)t;
            foreach (int i in c.Burning)
            {
                ref var s = ref c.B[i]; float cost = CrewCost[s.Mat]; if (c.BrigadeAcc < cost || s.Burn >= YoungFire || s.Away) continue;
                long k = DistrictOf(s.X, s.Y);
                if (!c.Districts.TryGetValue(k, out var dq)) dq = (localRate * 60, nowT);
                float bank = Math.Min(localRate * 60, dq.bank + localRate * (nowT - dq.at));
                if (bank < cost) { c.Districts[k] = (bank, nowT); continue; }
                c.Districts[k] = (bank - cost, nowT); doused.Add(i); c.BrigadeAcc -= cost;
            }
            foreach (int i in doused) { c.B[i].Heat = -2; c.B[i].HeatT = (float)t; Set(c, i, Intact); }
            c.BrigadeAcc = Math.Min(c.BrigadeAcc, 5);
        }
        var catching = new List<int>(); var done = new List<int>(); float now = (float)t;
        foreach (int i in c.Burning)
        {
            ref var s = ref c.B[i];
            s.Burn += dt;
            if (s.Burn >= BurnFor[s.Mat]) { done.Add(i); continue; }
            // an ordinary fire spreads by itself, house to house. Only a firestorm drives it much faster and further
            bool storm = c.InStorm[c.HotIdx(s.X, s.Y)];
            float mult = storm ? StormDrive : 1, rad = (storm ? BaseRad + 12 : BaseRad) * (s.Mat == CityPlan.Wood ? 1 + (c.Tinder - 1) * .6f : 1), rate = (s.Weak ? .3f : 1) * c.WxHeat * Output[s.Mat] * (s.Mat == CityPlan.Wood ? c.Tinder : 1) * Env(s.Burn / BurnFor[s.Mat]) * HeatPerMin * mult * dt / 60, sx = s.X, sy = s.Y, sr = Math.Max(s.W, s.D) / 2;
            if (c.WxEmber > 0 && s.Mat != CityPlan.Factory && rng.NextDouble() < c.WxEmber * dt)
            {
                float far = 50 + 220 * (float)rng.NextDouble(), side = 60 * ((float)rng.NextDouble() - .5f); int caught = Nearest(c, sx + wx * far - wy * side, sy + wy * far + wx * side);
                if (caught >= 0 && c.B[caught].State == Intact && c.B[caught].Heat > -1e5f && c.B[caught].Mat != CityPlan.Factory && (c.B[caught].Mat == CityPlan.Wood || rng.NextDouble() < .3)) { c.B[caught].Heat = -1e6f; catching.Add(caught); }
            }
            int reach = s.Mat == CityPlan.Factory || storm ? 3 : 2, cx = (int)Math.Floor(sx / CellM) - c.GX0, cy = (int)Math.Floor(sy / CellM) - c.GY0;
            for (int e = Math.Max(0, cy - reach); e <= Math.Min(c.GH - 1, cy + reach); e++)
                for (int a = Math.Max(0, cx - reach); a <= Math.Min(c.GW - 1, cx + reach); a++)
                {
                    int g = e * c.GW + a; if (c.Live[g] == 0) continue;     // nothing left here to catch: most of the ground behind a fire front
                    for (int n = c.Head[g]; n >= 0; n = c.Next[n])
                    {
                        ref var q = ref c.B[n]; if (q.State != Intact) continue;
                        float dx = q.X - sx, dy = q.Y - sy, d2 = dx * dx + dy * dy, lim = rad + sr + Math.Max(q.W, q.D) / 2 - 9;
                        if (d2 >= lim * lim) continue;
                        float d = MathF.Sqrt(d2), eff = Math.Max(0, d - (sr + Math.Max(q.W, q.D) / 2 - 9));
                        if (q.HeatT < now) { q.Heat *= MathF.Exp(-(now - q.HeatT) / CoolSecs); q.HeatT = now; }
                        float f = 1 - eff / rad, cosw = d > 1 ? (dx * wx + dy * wy) / d : 0;
                        q.Heat += rate * f * f * (1 + windPush * cosw);
                        if (q.Heat >= q.Thr * (q.Open ? OpenHeat : 1)) { q.Heat = -1e6f; catching.Add(n); }
                    }
                }
        }
        foreach (int i in done) Set(c, i, Gutted);
        foreach (int i in catching) Light(c, i, c.Tinder > 1 && c.B[i].Mat == CityPlan.Wood ? EscapeTinder : EscapeSpread);
        Secondaries();
    }
}
