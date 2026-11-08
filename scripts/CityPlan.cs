using System;
using System.Collections.Generic;

namespace Firebomber;

public struct Plot { public double X, Y, W, D, Ang; public byte Mat, Roof, BlockRoof; }

/// <summary>What stands in a city, building by building, with what it is made of. The terrain painter and the fire simulation both read this, so what burns is what you see.
/// Still the plain test layout on a street lattice: an old wooden core, brick around it, thinner suburbs, one industrial quarter. The real city comes in milestone 7.</summary>
public static class CityPlan
{
    public const byte Wood = 0, Brick = 1, Factory = 2;
    const double BW = 84, BH = 62, St = 8;

    public static (double x, double y) Works(Place p) { if (p.Layout != null) return (p.Layout.WorksX, p.Layout.WorksY); double a = N.Hash(p.Seed, 0, 301) * Math.Tau; return (p.X + Math.Cos(a) * p.R * 1.15, p.Y + Math.Sin(a) * p.R * 1.15); }

    public static void Enumerate(World w, Place p, List<Place> places, double x0, double y0, double x1, double y1, Action<Plot> house, Action<double, double, double, double, double>? street)
    {
        double ca = Math.Cos(p.Ang), sa = Math.Sin(p.Ang), reach = p.R * 2.2; var works = Works(p);
        int ni = (int)(reach / (BW + St)), nj = (int)(reach / (BH + St));
        for (int i = -ni; i <= ni; i++)
            for (int j = -nj; j <= nj; j++)
            {
                double lx = i * (BW + St), ly = j * (BH + St), wx = p.X + lx * ca - ly * sa, wy = p.Y + lx * sa + ly * ca;
                if (wx < x0 || wx > x1 || wy < y0 || wy > y1) continue;
                double u = World.Urb(wx, wy, places, 16);
                if (N.Hash(i + p.Seed, j, 77) > u * 1.5 || w.Land(wx, wy, 64) < .03 || w.InLake(wx, wy, 64) || w.Forest(wx, wy, 64) - u * .7 > .55) continue;
                if (u > .4 && street != null)
                    foreach (var e in new[] { (0.0, BH / 2 + St / 2, BW + St, St), (0.0, -BH / 2 - St / 2, BW + St, St), (BW / 2 + St / 2, 0.0, St, BH + St), (-BW / 2 - St / 2, 0.0, St, BH + St) })
                        street(wx + e.Item1 * ca - e.Item2 * sa, wy + e.Item1 * sa + e.Item2 * ca, e.Item3, e.Item4, p.Ang);

                double warp = (N.V(wx / 700, wy / 700, 305) - .5) * p.R * .35, dCore = Math.Sqrt((wx - p.X) * (wx - p.X) + (wy - p.Y) * (wy - p.Y)) + warp, dWorks = Math.Sqrt((wx - works.x) * (wx - works.x) + (wy - works.y) * (wy - works.y)) + warp;
                bool old = u > .6 && dCore < p.R * .5, ind = !old && dWorks < p.R * .55 && N.Hash(i * 53 + j, p.Seed, 306) > .1;
                byte blockRoof = ind ? (byte)(5 + N.Hash(i, j, 79) * 1.99) : old ? (byte)(N.Hash(i, j, 79) < .5 ? 1 : 4) : (byte)(N.Hash(i, j, 79) < .75 ? (N.Hash(i, j, 80) < .5 ? 0 : 3) : 2);
                Plot Make(double a, double b, double wd, double dp, byte mat, byte roof) => new() { X = wx + a * ca - b * sa, Y = wy + a * sa + b * ca, W = wd, D = dp, Ang = p.Ang, Mat = mat, Roof = roof, BlockRoof = blockRoof };
                if (ind)
                {
                    // brick works: one or two big sheds filling the block
                    if (N.Hash(i, j, 81) < .55) house(Make(0, 0, BW - 8, BH - 10, Factory, blockRoof));
                    else { house(Make(-BW / 4, 0, BW / 2 - 7, BH - 10, Factory, blockRoof)); house(Make(BW / 4, 0, BW / 2 - 7, BH - 16, Factory, blockRoof)); }
                    continue;
                }
                double step = old ? 9.6 : u > .55 ? 12 : 16, fill = old ? .97 : Math.Clamp(u * 1.5, .25, 1), wd2 = old ? 8.4 : 9, dp2 = old ? 7.4 : 8; int h = 0;
                for (double a = -BW / 2 + 6; a < BW / 2 - 4; a += step)
                    for (double b = -BH / 2 + 6; b < BH / 2 - 4; b += step)
                    {
                        h++; if (N.Hash(i * 53 + j, h, p.Seed) > fill) continue;
                        double q = N.Hash(i, j * 7 + h, 78);
                        byte mat = old ? (q < .9 ? Wood : Brick) : u > .55 ? (q < .75 ? Brick : Wood) : (q < .5 ? Brick : Wood);
                        byte roof = mat == Wood ? (byte)(N.Hash(i, h, 82) < .5 ? 1 : 4) : (byte)(N.Hash(i, h, 82) < .45 ? 0 : N.Hash(i, h, 82) < .85 ? 3 : 2);
                        house(Make(a, b, wd2, dp2, mat, roof));
                    }
            }
    }
}
