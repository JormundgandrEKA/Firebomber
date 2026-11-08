using System;
using System.Collections.Generic;
using System.Linq;

namespace Firebomber;

/// <summary>The buildings of one block, by type: what a street of the 1930s actually looked like from above. Also the large things laid down first: docks, rail yards, the citadel.</summary>
public sealed partial class CityGen
{
    public const byte GPaved = 0, GYard = 1, GGarden = 2, GPark = 3, GCinder = 4, GQuay = 5, GAllot = 6, GGraves = 7, GRock = 8;
    static byte GroundFor(BK k) => k switch { BK.Old => GPaved, BK.Tenement => GYard, BK.Mixed => GYard, BK.Rows => GGarden, BK.Barracks => GYard, BK.Cottage => GGarden, BK.Superblock => GYard, BK.Villa => GGarden, BK.Works => GCinder, BK.Park => GPark, BK.Cemetery => GGraves, BK.Allot => GAllot, BK.Square => GPaved, BK.Dock => GQuay, BK.Downtown => GPaved, BK.Frame => GGarden, BK.Walkup => GYard, BK.Slab => GPark, BK.WoodTown => GGarden, BK.Airy => GGarden, BK.Rock => GRock, BK.Plant => GCinder, BK.Timber => GCinder, _ => GYard };
    static byte StreetFor(BK k) => k == BK.Rock ? (byte)2 : k is BK.Cottage or BK.Barracks or BK.Allot ? (byte)1 : (byte)0;      // no streets across bare rock

    void AddPlot(double x, double y, double wd, double dp, double ang, byte mat, byte roof, BK zone)
    {
        var col = TerrainPaint.Roofs[roof & 31];
        plots.Add(new Plot { X = x, Y = y, W = wd, D = dp, Ang = ang, Mat = mat, Roof = roof, BlockRoof = roof });
        L.AddFar(x, y, (float)(wd * dp), col, zone);
    }
    void AddGround(Quad q, byte tone, BK k)
    {
        int col = TerrainPaint.GroundCols[tone];
        for (double y = q.MinY + 32; y < q.MaxY; y += 64) for (double x = q.MinX + 32; x < q.MaxX; x += 64) if (q.Has(x, y)) L.AddFar(x, y, 64 * 64 * .75f, col, k);
        var c = q.Center; if (q.Lu < 64 || q.Lv < 64) L.AddFar(c.x, c.y, (float)(q.Lu * q.Lv) * .75f, col, k);
    }
    byte RoofOf(int[] set) => (byte)set[rng.Next(set.Length)];

    void Accept(Quad outer, BK k)
    {
        double st = StreetW(k) / 2, fu = Math.Min(.3, st / outer.Lu), fv = Math.Min(.3, st / outer.Lv);
        var q = new Quad();
        (q.X0, q.Y0) = outer.Map(fu, fv); (q.X1, q.Y1) = outer.Map(1 - fu, fv); (q.X2, q.Y2) = outer.Map(1 - fu, 1 - fv); (q.X3, q.Y3) = outer.Map(fu, 1 - fv);
        byte g = GroundFor(k);
        blocks.Add(new CityBlock { Outer = outer, Inner = q, Ground = g, Street = StreetFor(k), Kind = k });
        AddGround(q, g, k);
        switch (k)
        {
            case BK.Old: FillOld(q); break;
            case BK.Tenement: FillTenement(q); break;
            case BK.Mixed: FillMixed(q); break;
            case BK.Rows: FillRows(q); break;
            case BK.Barracks: FillBarracks(q); break;
            case BK.Cottage: FillCottages(q); break;
            case BK.Superblock: FillSuper(q); break;
            case BK.Villa: FillVilla(q); break;
            case BK.Works: FillWorks(q); break;
            case BK.Allot: FillAllot(q); break;
            case BK.Square: FillSquare(q); break;
            case BK.Cemetery: FillChapel(q); break;
            case BK.Downtown: FillDowntown(q); break;
            case BK.Frame: FillFrame(q); break;
            case BK.Walkup: FillWalkup(q); break;
            case BK.Slab: FillSlab(q); break;
            case BK.WoodTown: FillWoodTown(q); break;
            case BK.Airy: FillAiry(q); break;
            case BK.Plant: FillPlant(q); break;
            case BK.Timber: FillTimber(q); break;
        }
    }
    /// <summary>Puts a building at a,b metres in from corner 0. Across means its length runs across the block.</summary>
    void Put(Quad q, double a, double b, double wd, double dp, bool across, double jit, byte mat, byte roof, BK zone)
    {
        var c = q.Map(a / q.Lu, b / q.Lv); double ang = q.Ang + (rng.NextDouble() - .5) * jit;
        if (across) AddPlot(c.x, c.y, dp, wd, ang, mat, roof, zone); else AddPlot(c.x, c.y, wd, dp, ang, mat, roof, zone);
    }

    // old town: crooked, gabled, almost no gaps, mostly timber
    void FillOld(Quad q)
    {
        double lu = q.Lu, lv = q.Lv; byte woodBlock = RoofOf(s.WoodRoofs), brickBlock = RoofOf(s.BrickRoofs);
        // a ring of narrow houses on the street, more behind them in the yards
        for (double b = 5; b < lv - 4; b += 9.5)
            for (double a = 4; a < lu - 3; a += 7.5 + rng.NextDouble() * 2.5)
            {
                if (rng.NextDouble() < .07) continue;
                byte mat = rng.NextDouble() < s.OldWood ? CityPlan.Wood : CityPlan.Brick;
                Put(q, a, b + (rng.NextDouble() - .5) * 2, 6.5 + rng.NextDouble() * 2, 7.5 + rng.NextDouble() * 1.5, false, .3, mat, rng.NextDouble() < .1 ? RoofOf(s.BrickRoofs) : mat == CityPlan.Wood ? woodBlock : brickBlock, BK.Old);
            }
    }
    // the closed city block: a wall of five storeys round a yard. Deep ones have wings and back houses, courtyard behind courtyard, as in Berlin
    void FillTenement(Quad q)
    {
        double lu = q.Lu, lv = q.Lv, D = 12; byte roof = RoofOf(s.BrickRoofs);
        void Run(bool along, double fixedAt, double from, double to)
        {
            for (double t = from; t < to - 8;)
            {
                double l = Math.Min(16 + rng.NextDouble() * 10, to - t);
                if (along) Put(q, t + l / 2, fixedAt, l - .5, D, false, .01, CityPlan.Brick, roof, BK.Tenement); else Put(q, fixedAt, t + l / 2, l - .5, D, true, .01, CityPlan.Brick, roof, BK.Tenement);
                if (s.Deep && rng.NextDouble() < .55)
                {
                    // a wing running back into the yard
                    double wing = 12 + rng.NextDouble() * 12; bool low = fixedAt < (along ? lv : lu) / 2; double back = fixedAt + (low ? 1 : -1) * (D / 2 + wing / 2);
                    if (along && wing < lv / 2 - D) Put(q, t + 3, back, 7, wing, false, 0, CityPlan.Brick, roof, BK.Tenement);
                    else if (!along && wing < lu / 2 - D) Put(q, back, t + 3, 7, wing, true, 0, CityPlan.Brick, roof, BK.Tenement);
                }
                t += l;
            }
        }
        Run(true, D / 2 + 1, 1, lu - 1); Run(true, lv - D / 2 - 1, 1, lu - 1); Run(false, D / 2 + 1, D + 1, lv - D - 1); Run(false, lu - D / 2 - 1, D + 1, lv - D - 1);
        if (s.Deep && lv > 110 && lu > 80)
            for (double a = lu * .28; a < lu * .8; a += 26) if (rng.NextDouble() < .75) Put(q, a, lv / 2, 20, 10, false, 0, CityPlan.Brick, roof, BK.Tenement);
        else if (rng.NextDouble() < .35) Put(q, lu / 2, lv / 2, Math.Min(26, lu * .3), Math.Min(14, lv * .2), false, 0, CityPlan.Factory, 6, BK.Tenement);
    }
    // houses and shops on the street, workshops and sheds behind
    void FillMixed(Quad q)
    {
        double lu = q.Lu, lv = q.Lv; byte roof = RoofOf(s.BrickRoofs);
        foreach (double b in new[] { 7.0, lv - 7 })
            for (double a = 5; a < lu - 5; a += 11 + rng.NextDouble() * 6)
            {
                if (rng.NextDouble() < .12) continue;
                byte mat = rng.NextDouble() < .3 ? CityPlan.Wood : CityPlan.Brick;
                Put(q, a, b, 9 + rng.NextDouble() * 4, 10, false, .03, mat, mat == CityPlan.Wood ? RoofOf(s.WoodRoofs) : roof, BK.Mixed);
            }
        for (int n = (int)(lv / 50); n > 0; n--) Put(q, lu * (.25 + .5 * rng.NextDouble()), 22 + rng.NextDouble() * Math.Max(1, lv - 44), 14 + rng.NextDouble() * 18, 9 + rng.NextDouble() * 8, rng.NextDouble() < .4, .02, rng.NextDouble() < .5 ? CityPlan.Factory : CityPlan.Wood, (byte)(rng.NextDouble() < .5 ? 5 : 9), BK.Mixed);
    }
    // terraces: rows of narrow houses back to back with a strip of yard between
    void FillRows(Quad q)
    {
        double lu = q.Lu, lv = q.Lv; byte roof = RoofOf(s.BrickRoofs);
        const double Gap = 19;
        bool byV = lv > lu; int rows = Math.Max(1, (int)((byV ? lu : lv) / Gap));
        for (int r = 0; r < rows; r++)
        {
            double cross = (r + .5) * (byV ? lu : lv) / rows, len = byV ? lv : lu;
            for (double t = 4; t < len - 4; t += 7.6)
            {
                if (rng.NextDouble() < .04) continue;
                byte mat = rng.NextDouble() < s.RowBrick ? CityPlan.Brick : CityPlan.Wood;
                if (byV) Put(q, cross, t, 9.5, 7.2, true, .005, mat, roof, BK.Rows); else Put(q, t, cross, 7.2, 9.5, false, .005, mat, roof, BK.Rows);
            }
        }
    }
    // workers' barracks: long wooden sheds in lines, tarred roofs, bare earth
    void FillBarracks(Quad q)
    {
        double lu = q.Lu, lv = q.Lv;
        for (double b = 12; b < lv - 8; b += 23)
            for (double a = 20; a < lu - 16; a += 40)
                if (rng.NextDouble() < .9) Put(q, a, b, 32, 8, false, .02, CityPlan.Wood, RoofOf(new[] { 9, 9, 10 }), BK.Barracks);
    }
    // wooden houses on long plots, each with its kitchen garden
    void FillCottages(Quad q)
    {
        double lu = q.Lu, lv = q.Lv; bool byV = lv > lu; double len = byV ? lv : lu, wid = byV ? lu : lv; byte street = RoofOf(s.WoodRoofs);
        foreach (double side in wid > 30 ? new[] { 8.0, wid - 8 } : new[] { wid / 2 })
            for (double t = 8; t < len - 6; t += 13 + rng.NextDouble() * 6)
            {
                if (rng.NextDouble() < .1) continue;
                byte mat = rng.NextDouble() < .88 ? CityPlan.Wood : CityPlan.Brick; byte roof = rng.NextDouble() < .2 ? RoofOf(s.WoodRoofs) : street;
                if (byV) Put(q, side, t, 9 + rng.NextDouble() * 3, 8, true, .12, mat, roof, BK.Cottage); else Put(q, t, side, 9 + rng.NextDouble() * 3, 8, false, .12, mat, roof, BK.Cottage);
            }
    }
    // the new Soviet kvartal: long five-storey slabs round a great yard with gaps at the corners, a school in the middle
    void FillSuper(Quad q)
    {
        double lu = q.Lu, lv = q.Lv, D = 15; byte roof = RoofOf(new[] { 13, 13, 8 });
        void Slabs(bool along, double at, double len)
        {
            int n = Math.Max(1, (int)(len / 75));
            for (int i = 0; i < n; i++) { double l = len / n - 12, c = 8 + (i + .5) * (len - 16) / n; if (along) Put(q, c, at, l, D, false, 0, CityPlan.Brick, roof, BK.Superblock); else Put(q, at, c, l, D, true, 0, CityPlan.Brick, roof, BK.Superblock); }
        }
        Slabs(true, D / 2 + 2, lu); Slabs(true, lv - D / 2 - 2, lu); if (lv > 120) { Slabs(false, D / 2 + 2, lv - 60); Slabs(false, lu - D / 2 - 2, lv - 60); }
        if (lu > 120 && lv > 150) Put(q, lu / 2, lv / 2, 48, 16, false, 0, CityPlan.Brick, 13, BK.Superblock);
    }
    void FillVilla(Quad q)
    {
        double lu = q.Lu, lv = q.Lv;
        for (double b = 16; b < lv - 8; b += 34) for (double a = 16; a < lu - 8; a += 34)
                if (rng.NextDouble() < .78) { byte mat = rng.NextDouble() < .7 ? CityPlan.Brick : CityPlan.Wood; Put(q, a + (rng.NextDouble() - .5) * 8, b + (rng.NextDouble() - .5) * 8, 11 + rng.NextDouble() * 5, 10 + rng.NextDouble() * 4, false, .5, mat, RoofOf(s.BrickRoofs), BK.Villa); }
    }
    // a works: great sheds with north-light roofs, a yard of cinders, an office, stacks
    void FillWorks(Quad q)
    {
        double lu = q.Lu, lv = q.Lv;
        for (double b = 8; b < lv - 12;)
        {
            double dp = 22 + rng.NextDouble() * 34;
            if (b + dp > lv - 4) dp = lv - 4 - b;
            if (dp < 12) break;
            for (double a = 6; a < lu - 12;)
            {
                double wd = 40 + rng.NextDouble() * 90; if (a + wd > lu - 4) wd = lu - 4 - a; if (wd < 14) break;
                if (rng.NextDouble() < .84) Put(q, a + wd / 2, b + dp / 2, wd, dp, false, 0, CityPlan.Factory, (byte)(5 + rng.Next(3)), BK.Works);
                a += wd + 8 + rng.NextDouble() * 12;
            }
            b += dp + 12 + rng.NextDouble() * 18;
        }
        for (int n = 1 + rng.Next(3); n > 0; n--) Put(q, 8 + rng.NextDouble() * (lu - 16), 6 + rng.NextDouble() * (lv - 12), 4, 4, false, 0, CityPlan.Factory, 9, BK.Works);
    }
    void FillAllot(Quad q) { for (int n = (int)(q.Lu * q.Lv / 1500); n > 0; n--) Put(q, 6 + rng.NextDouble() * (q.Lu - 12), 6 + rng.NextDouble() * (q.Lv - 12), 4, 3.5, false, .6, CityPlan.Wood, 10, BK.Allot); }
    void FillChapel(Quad q) => Put(q, q.Lu / 2, 18, 12, 18, false, 0, CityPlan.Brick, 14, BK.Cemetery);
    void FillSquare(Quad q) { if (rng.NextDouble() < .7) Put(q, q.Lu / 2, q.Lv / 2, Math.Min(46, q.Lu * .5), Math.Min(22, q.Lv * .4), false, 0, CityPlan.Brick, 11, BK.Square); }

    // downtown: tall steel and masonry filling small blocks to the lot line, flat roofs
    void FillDowntown(Quad q)
    {
        double lu = q.Lu, lv = q.Lv;
        for (double b = 0; b < lv - 8;)
        {
            double dp = Math.Min(lv - b - 1, 22 + rng.NextDouble() * 22);
            for (double a = 0; a < lu - 8;) { double wd = Math.Min(lu - a - 1, 18 + rng.NextDouble() * 22); if (rng.NextDouble() < .94) Put(q, a + wd / 2, b + dp / 2, wd - 1, dp - 1, false, 0, CityPlan.Brick, RoofOf(s.BrickRoofs), BK.Downtown); a += wd; }
            b += dp;
        }
    }
    // the American frame house: detached on a narrow lot, a sliver between neighbours, a garage on the back alley. Further out, empty lots
    void FillFrame(Quad q)
    {
        double lu = q.Lu, lv = q.Lv, t = Out(q.Center.x, q.Center.y), vacant = t > .7 ? .08 + (t - .7) * .8 : .05; bool byV = lv > lu; double len = byV ? lv : lu, wid = byV ? lu : lv;
        foreach (double side in new[] { 9.0, wid - 9 })
            for (double at = 7; at < len - 6; at += 11.5)
            {
                if (rng.NextDouble() < vacant) continue;
                byte mat = rng.NextDouble() < .93 ? CityPlan.Wood : CityPlan.Brick, roof = RoofOf(s.WoodRoofs);
                if (byV) Put(q, side, at, 8.2, 10.5, true, .004, mat, roof, BK.Frame); else Put(q, at, side, 8.2, 10.5, false, .004, mat, roof, BK.Frame);
                if (wid > 50 && rng.NextDouble() < .4) { double back = side < wid / 2 ? wid / 2 - 5 : wid / 2 + 5; if (byV) Put(q, back, at, 4.5, 5, true, 0, CityPlan.Wood, 22, BK.Frame); else Put(q, at, back, 4.5, 5, false, 0, CityPlan.Wood, 22, BK.Frame); }
            }
    }
    // brick walk-ups by the streetcar lines: three storeys, detached, with light courts
    void FillWalkup(Quad q)
    {
        double lu = q.Lu, lv = q.Lv; byte roof = RoofOf(s.BrickRoofs);
        foreach (double b in new[] { 11.0, lv - 11 })
            for (double a = 11; a < lu - 8; a += 21) if (rng.NextDouble() < .9) Put(q, a, b, 17, 18, false, 0, CityPlan.Brick, roof, BK.Walkup);
    }
    // functionalist slabs of the 1930s: all the same way round, in rows with light and grass between them
    void FillSlab(Quad q)
    {
        double lu = q.Lu, lv = q.Lv; byte roof = RoofOf(new[] { 20, 20, 13 });
        for (double b = 14; b < lv - 8; b += 36)
            for (double a = 34; a < lu - 26; a += 66) if (rng.NextDouble() < .92) Put(q, a, b, 56, 11, false, 0, CityPlan.Brick, roof, BK.Slab);
    }
    // painted wooden houses on regular plots round a yard, with wide streets kept open against fire
    void FillWoodTown(Quad q)
    {
        double lu = q.Lu, lv = q.Lv;
        foreach (double b in new[] { 7.0, lv - 7 })
            for (double a = 8; a < lu - 6; a += 13.5 + rng.NextDouble() * 3) if (rng.NextDouble() < .9) Put(q, a, b, 11, 8, false, .02, rng.NextDouble() < .95 ? CityPlan.Wood : CityPlan.Brick, RoofOf(s.WoodRoofs), BK.WoodTown);
        foreach (double a in new[] { 7.0, lu - 7 })
            for (double b = 22; b < lv - 20; b += 15) if (rng.NextDouble() < .7) Put(q, a, b, 11, 8, true, .02, CityPlan.Wood, RoofOf(s.WoodRoofs), BK.WoodTown);
    }
    // perimeter blocks with more air than Berlin's: lower, gaps at the corners, a garden in the middle
    void FillAiry(Quad q)
    {
        double lu = q.Lu, lv = q.Lv, D = 11; byte roof = RoofOf(s.BrickRoofs);
        void Run(bool along, double at, double from, double to)
        {
            for (double t = from; t < to - 10;)
            {
                double l = Math.Min(20 + rng.NextDouble() * 14, to - t);
                if (rng.NextDouble() < .84) { if (along) Put(q, t + l / 2, at, l - 1, D, false, .004, CityPlan.Brick, roof, BK.Airy); else Put(q, at, t + l / 2, l - 1, D, true, .004, CityPlan.Brick, roof, BK.Airy); }
                t += l;
            }
        }
        Run(true, D / 2 + 1, 12, lu - 12); Run(true, lv - D / 2 - 1, 12, lu - 12); Run(false, D / 2 + 1, D + 14, lv - D - 14); Run(false, lu - D / 2 - 1, D + 14, lv - D - 14);
    }
    // a vast single-storey plant: one or two enormous saw-tooth sheds in a great yard, a power house, stacks
    void FillPlant(Quad q)
    {
        double lu = q.Lu, lv = q.Lv; byte roof = (byte)(5 + rng.Next(3));
        if (lu < 120 || lv < 120) { FillWorks(q); return; }
        double split = rng.NextDouble() < .55 ? lv * (.5 + .15 * rng.NextDouble()) : lv * .8;
        Put(q, lu * .5, 18 + (split - 30) / 2, lu - 50, split - 30, false, 0, CityPlan.Factory, roof, BK.Plant);
        if (lv - split > 70) Put(q, lu * .42, split + (lv - split) / 2, lu * .6, lv - split - 34, false, 0, CityPlan.Factory, roof, BK.Plant);
        Put(q, lu - 34, lv - 34, 38, 26, false, 0, CityPlan.Factory, 9, BK.Plant);
        for (int n = 2 + rng.Next(3); n > 0; n--) Put(q, lu - 14, 14 + rng.NextDouble() * (lv - 28), 5, 5, false, 0, CityPlan.Factory, 9, BK.Plant);
    }
    // a timber yard: long stacks on bare ground, and a saw shed. It burns very well
    void FillTimber(Quad q)
    {
        double lu = q.Lu, lv = q.Lv;
        for (double b = 12; b < lv - 8; b += 15)
            for (double a = 16; a < lu - 12; a += 26) if (rng.NextDouble() < .78) Put(q, a, b, 20, 7, false, .01, CityPlan.Wood, 17, BK.Timber);
        Put(q, lu / 2, lv - 12, Math.Min(46, lu * .6), 14, false, 0, CityPlan.Factory, 9, BK.Timber);
    }

    // ---------------------------------------------------------------- laid down before anything else
    /// <summary>Arms of water branching off the river and narrowing away inland, so that water shapes the plan: peninsulas between them, coves along them, and here and there an island where an arm forks and closes again. Avenues bridge them where they can.</summary>
    void Inlets()
    {
        Line? river = null; double best = 1e12; int k0 = 0;
        foreach (var r in w.Rivers) for (int n = 0; n < r.P.Count; n++) { double dd = (r.P[n].x - p.X) * (r.P[n].x - p.X) + (r.P[n].y - p.Y) * (r.P[n].y - p.Y); if (dd < best) { best = dd; river = r; k0 = n; } }
        if (river == null) return;
        const double Step = 110;
        for (int i = 0; i < s.Arms; i++)
        {
            // where it leaves the river: spread along the banks either side of the middle of town
            double along = (i % 2 == 0 ? 1 : -1) * R * (.2 + .75 * ((i / 2 + rng.NextDouble() * .6) / Math.Max(1, (s.Arms + 1) / 2))), walked = 0; int k = k0, dir = along > 0 ? 1 : -1;
            while (k + dir > 0 && k + dir < river.P.Count - 1 && walked < Math.Abs(along)) { var pa = river.P[k]; var pb = river.P[k + dir]; walked += Math.Sqrt((pb.x - pa.x) * (pb.x - pa.x) + (pb.y - pa.y) * (pb.y - pa.y)); k += dir; }
            var at = river.P[k]; var nx1 = river.P[Math.Min(river.P.Count - 1, k + 4)]; var pv = river.P[Math.Max(0, k - 4)]; double tx = nx1.x - pv.x, ty = nx1.y - pv.y, tl = Math.Sqrt(tx * tx + ty * ty) + 1e-9; tx /= tl; ty /= tl;
            // out to one side or the other, away from the middle of town if it is near, leaning along the river
            double sx = -ty, sy = tx; if (rng.NextDouble() < .5) { sx = -sx; sy = -sy; } if ((at.x - ox) * sx + (at.y - oy) * sy < -R * .05) { sx = -sx; sy = -sy; }
            double lean = (rng.NextDouble() - .5) * .9, ca = Math.Cos(lean), sa = Math.Sin(lean), ux = sx * ca - sy * sa, uy = sx * sa + sy * ca, vx = -uy, vy = ux;
            double len = R * (.75 + .6 * rng.NextDouble()), ph1 = rng.NextDouble() * 6, ph2 = rng.NextDouble() * 6, amp = len * (.1 + .12 * rng.NextDouble()), forkAt = len * (.15 + .4 * rng.NextDouble()), forkL = 420 + rng.NextDouble() * 520; bool fork = rng.NextDouble() < .65; int nseed = p.Seed % 71 + i * 13;
            (double x, double y) Mid(double r) { double o = amp * Math.Sin(r / len * 3.4 + ph1) + amp * .4 * Math.Sin(r / len * 9 + ph2) + (N.V(r / 170, i * 3.7, nseed) - .5) * 60; o -= amp * Math.Sin(ph1) + amp * .4 * Math.Sin(ph2); return (at.x + ux * r + vx * o, at.y + uy * r + vy * o); }
            // wide where it meets the river, narrowing to nothing inland; coves come and go along it
            double Half(double r) { double f = Math.Clamp(r / len, 0, 1); return s.ArmW / 2 * Math.Pow(1 - f, .62) * (.5 + 1.15 * N.V(r / 210, i * 5.3, nseed + 1) + .3 * (N.V(r / 70, i * 2.1, nseed + 2) - .5)); }
            void Stretch((double x, double y) a, (double x, double y) b, double h0, double h1)
            {
                double dx = b.x - a.x, dy = b.y - a.y, l = Math.Sqrt(dx * dx + dy * dy) + 1e-9, nx0 = -dy / l, ny0 = dx / l, ex = dx / l * 8, ey = dy / l * 8; var q = new Quad();
                (q.X0, q.Y0) = (a.x - ex - nx0 * h0, a.y - ey - ny0 * h0); (q.X1, q.Y1) = (b.x + ex - nx0 * h1, b.y + ey - ny0 * h1); (q.X2, q.Y2) = (b.x + ex + nx0 * h1, b.y + ey + ny0 * h1); (q.X3, q.Y3) = (a.x - ex + nx0 * h0, a.y - ey + ny0 * h0);
                MarkQuad(q, Water); L.Basins.Add(q);
            }
            for (double r = 0; r < len; r += Step)
            {
                var a = Mid(r); var b = Mid(r + Step); double h0 = Half(r), h1 = Half(r + Step);
                if (h0 < 6 && h1 < 6) continue;
                double Fork(double q) => fork && q > forkAt && q < forkAt + forkL ? Math.Sin(Math.PI * (q - forkAt) / forkL) * (Half(q) * .6 + 120) : 0;
                double o0 = Fork(r), o1 = Fork(r + Step);
                if (o0 == 0 && o1 == 0) { Stretch(a, b, h0, h1); continue; }
                double dx = b.x - a.x, dy = b.y - a.y, l = Math.Sqrt(dx * dx + dy * dy) + 1e-9, nx0 = -dy / l, ny0 = dx / l;
                foreach (int side in new[] { -1, 1 }) Stretch((a.x + nx0 * o0 * side, a.y + ny0 * o0 * side), (b.x + nx0 * o1 * side, b.y + ny0 * o1 * side), h0 * .55, h1 * .55);
            }
        }
    }
    /// <summary>Dock basins cut into one bank of the river, with quays and long warehouses, along the stretch below the old town.</summary>
    void Docks()
    {
        Line? river = null; double best = 1e12;
        foreach (var r in w.Rivers) foreach (var q in r.P) { double d = (q.x - p.X) * (q.x - p.X) + (q.y - p.Y) * (q.y - p.Y); if (d < best) { best = d; river = r; } }
        if (river == null) return;
        double spacing = 300, from = R * .1, to = R * .95, walked = 0, next = from; int k = river.P.FindIndex(q => (q.x - p.X) * (q.x - p.X) + (q.y - p.Y) * (q.y - p.Y) == best);
        for (int i = k; i < river.P.Count - 2 && walked < to; i++)
        {
            var a = river.P[i]; var b = river.P[Math.Min(river.P.Count - 1, i + 8)]; double l = Math.Sqrt((a.x - river.P[i + 1].x) * (a.x - river.P[i + 1].x) + (a.y - river.P[i + 1].y) * (a.y - river.P[i + 1].y)); walked += l;
            if (walked < next) continue;
            next += spacing;
            double dx = b.x - a.x, dy = b.y - a.y, ln = Math.Sqrt(dx * dx + dy * dy) + 1e-9; dx /= ln; dy /= ln;
            double side = -1, bx = -dy * side, by = dx * side, bank = river.WidthAt(i) / 2 - 6, lenB = 300 + rng.NextDouble() * 220, wid = 66;
            (double x, double y) Pt(double along, double off) => (a.x + dx * along + bx * (bank + off), a.y + dy * along + by * (bank + off));
            var end = Pt(0, lenB + 90); if (w.Land(end.x, end.y, 64) < .3) continue;
            Quad Box(double a0, double a1, double o0, double o1) { var q = new Quad(); (q.X0, q.Y0) = Pt(a0, o0); (q.X1, q.Y1) = Pt(a1, o0); (q.X2, q.Y2) = Pt(a1, o1); (q.X3, q.Y3) = Pt(a0, o1); return q; }
            var basin = Box(-wid / 2, wid / 2, 0, lenB);
            var quay = Box(-spacing / 2, spacing / 2, 4, lenB + 70);
            MarkQuad(quay, Taken); MarkQuad(basin, Water); L.Basins.Add(basin);
            blocks.Add(new CityBlock { Outer = quay, Inner = quay, Ground = GQuay, Kind = BK.Dock }); AddGround(quay, GQuay, BK.Dock);
            double ang = Math.Atan2(by, bx);
            foreach (double off in new[] { -wid / 2 - 26, wid / 2 + 26, -spacing / 2 + 18, spacing / 2 - 18 })
                for (double t = 30; t < lenB + 30; t += 92)
                {
                    if (rng.NextDouble() > .9) continue;
                    var c = Pt(off, t); if (off == 0) continue;
                    AddPlot(c.x, c.y, 80, 24, ang, CityPlan.Factory, (byte)(rng.NextDouble() < .6 ? 15 : 5), BK.Dock);
                }
        }
    }
    /// <summary>The main station where the line comes in, and a marshalling yard further out on it.</summary>
    void Rail()
    {
        foreach (var l in w.Rails)
        {
            int atStart = (l.P[0].x == p.SX && l.P[0].y == p.SY) ? 1 : (l.P[^1].x == p.SX && l.P[^1].y == p.SY) ? -1 : 0; if (atStart == 0) continue;
            var pts = atStart == 1 ? l.P : Enumerable.Reverse(l.P).ToList();
            double reach = Math.Min(R * .6, 3200) + 400, d = 0; int k = 0; while (k < pts.Count - 2 && d < reach) { d += Math.Sqrt(Math.Pow(pts[k + 1].x - pts[k].x, 2) + Math.Pow(pts[k + 1].y - pts[k].y, 2)); k++; }
            var a = pts[k]; var b = pts[Math.Min(pts.Count - 1, k + 16)]; double dx = b.x - a.x, dy = b.y - a.y, ln = Math.Sqrt(dx * dx + dy * dy) + 1e-9; dx /= ln; dy /= ln;
            double len = Math.Min(1300, 500 + R * .12); int tracks = R > 3000 ? 11 : 5;
            (double x, double y) P2(double along, double off) => (a.x + dx * along - dy * off, a.y + dy * along + dx * off);
            bool wet = false;
            for (double u = 0; u <= len && !wet; u += 40) foreach (double off in new[] { -tracks * 8.0, 0, tracks * 8.0 }) { var c = P2(u, off); if (!Free2Water(c.x, c.y) || w.Land(c.x, c.y, 64) < .3) wet = true; }
            if (wet) continue;
            for (int t = 0; t < tracks; t++)
            {
                double off = (t - (tracks - 1) / 2.0) * 13; var line = new List<(double x, double y)>();
                for (double u = -6; u <= len + 6; u += 40) { double taper = Math.Min(1, Math.Min(u + 6, len + 6 - u) / 110); line.Add(P2(u, off * taper)); }
                L.Tracks.Add(new Line(line, 4, 4));
            }
            var q = new Quad(); (q.X0, q.Y0) = P2(0, -tracks * 8); (q.X1, q.Y1) = P2(len, -tracks * 8); (q.X2, q.Y2) = P2(len, tracks * 8); (q.X3, q.Y3) = P2(0, tracks * 8);
            MarkQuad(q, Way);
            blocks.Add(new CityBlock { Outer = q, Inner = q, Ground = GCinder, Kind = BK.Works }); AddGround(q, GCinder, BK.Works);
        }
        // the terminus itself
        double ang = p.RiverAng + .6; var st = new Quad();
        (double x, double y) S(double u, double v) => (p.SX + Math.Cos(ang) * u - Math.Sin(ang) * v, p.SY + Math.Sin(ang) * u + Math.Cos(ang) * v);
        (st.X0, st.Y0) = S(-85, -45); (st.X1, st.Y1) = S(85, -45); (st.X2, st.Y2) = S(85, 45); (st.X3, st.Y3) = S(-85, 45);
        MarkQuad(st, Taken); blocks.Add(new CityBlock { Outer = st, Inner = st, Ground = GPaved, Kind = BK.Square });
        var h = S(0, 0); AddPlot(h.x, h.y, 130, 42, ang, CityPlan.Factory, 11, BK.Square);
    }
    /// <summary>A walled citadel beside the river: palaces and cathedrals in a great paved yard, inside a wall of towers.</summary>
    void Citadel()
    {
        const double Rc = 340; double cx = ox, cy = oy;
        double Rad(double a) => Rc * (1 + .14 * Math.Sin(a * 3 + p.Seed));
        for (double y = cy - Rc * 1.2; y < cy + Rc * 1.2; y += 48)
            for (double x = cx - Rc * 1.2; x < cx + Rc * 1.2; x += 48)
            {
                double a = Math.Atan2(y - cy, x - cx); if (Math.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy)) > Rad(a) || !Free2Water(x, y)) continue;
                var c = new Quad(); (c.X0, c.Y0) = (x - 25, y - 25); (c.X1, c.Y1) = (x + 25, y - 25); (c.X2, c.Y2) = (x + 25, y + 25); (c.X3, c.Y3) = (x - 25, y + 25);
                byte tone = N.V(x / 110, y / 110, p.Seed % 89) < .42 ? GPark : GPaved; blocks.Add(new CityBlock { Outer = c, Inner = c, Ground = tone, Kind = BK.Square }); AddGround(c, tone, BK.Square);
            }
        for (double y = cy - Rc * 1.3; y <= cy + Rc * 1.3; y += MC) for (double x = cx - Rc * 1.3; x <= cx + Rc * 1.3; x += MC)
                if (Math.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy)) < Rad(Math.Atan2(y - cy, x - cx)) + 24 && Free2Water(x, y)) { int i = (int)((x - mX) / MC), j = (int)((y - mY) / MC); if (i >= 0 && j >= 0 && i < mN && j < mN) mask[j * mN + i] = Taken; }
        const int N2 = 30;
        for (int i = 0; i < N2; i++)
        {
            double a0 = i / (double)N2 * Math.Tau, a1 = (i + 1) / (double)N2 * Math.Tau, x0 = cx + Math.Cos(a0) * Rad(a0), y0 = cy + Math.Sin(a0) * Rad(a0), x1 = cx + Math.Cos(a1) * Rad(a1), y1 = cy + Math.Sin(a1) * Rad(a1);
            if (!Free2Water((x0 + x1) / 2, (y0 + y1) / 2)) continue;
            AddPlot((x0 + x1) / 2, (y0 + y1) / 2, Math.Sqrt((x1 - x0) * (x1 - x0) + (y1 - y0) * (y1 - y0)) + 1, 7, Math.Atan2(y1 - y0, x1 - x0), CityPlan.Brick, 15, BK.Square);
            if (i % 5 == 0) AddPlot(x0, y0, 14, 14, 0, CityPlan.Brick, 15, BK.Square);
        }
        foreach (var (a, b, wd, dp, roof) in new[] { (-140, -60, 90, 30, 11), (40, -150, 56, 40, 11), (130, 40, 110, 26, 13), (-60, 120, 70, 44, 11), (-10, -10, 36, 36, 11), (70, 120, 26, 26, 11) })
            if (Free2Water(cx + a, cy + b)) AddPlot(cx + a, cy + b, wd, dp, .1, CityPlan.Brick, (byte)roof, BK.Square);
        for (int i = 0; i < 70; i++)
        {
            double a = rng.NextDouble() * Math.Tau, r = Rad(a) * Math.Sqrt(rng.NextDouble()) * .88, x = cx + Math.Cos(a) * r, y = cy + Math.Sin(a) * r;
            if (Free2Water(x, y)) AddPlot(x, y, 12 + rng.NextDouble() * 34, 9 + rng.NextDouble() * 14, .1 + (rng.NextDouble() < .5 ? Math.PI / 2 : 0), CityPlan.Brick, (byte)(rng.NextDouble() < .5 ? 13 : rng.NextDouble() < .5 ? 0 : 11), BK.Square);
        }
    }
}
