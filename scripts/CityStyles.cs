using System.Collections.Generic;

namespace Firebomber;

public enum BK : byte { None, Old, Tenement, Mixed, Rows, Barracks, Cottage, Superblock, Villa, Works, Park, Cemetery, Allot, Square, Dock, Downtown, Frame, Walkup, Slab, WoodTown, Airy, Rock, Plant, Timber }
public enum CityShape { Radial, Grid, Strip }

/// <summary>What kind of city this is, as data. The generator has no special cases: every difference between cities lives here. The UCS cities are European of about 1935-40; the Vulfen cities are Nordic and American.</summary>
public sealed class CityStyle
{
    public string Like = ""; public CityShape Shape; public double Km = 3, Angle, RiverW = 90, Warp = 26, Industry = .3, Avenue = 26, OldWood = .7, RowBrick = .9;
    public double[] Rings = { .15, .4, .7 }; public int Radials = 7, Sats = 2;
    public double CoreT = .12, InnerT = .38, MidT = .68;       // bands, as a fraction of the built radius
    public (BK k, double w)[][] Mix = { };                      // for bands: inner, mid, outer, fringe
    public int[] BrickRoofs = { 0, 3, 15 }, WoodRoofs = { 10, 9, 1 };
    public bool Citadel, Port, Bridges = true, Deep;            // Deep: tenement blocks with wings and back houses
    public double StripWidthKm = 2.2; public double AxisKm = 2.2;
    /// <summary>What stands at the centre, and what industry looks like inland and on the water.</summary>
    public BK CoreKind = BK.Old, WorksKind = BK.Works, WaterWorks = BK.Works;
    /// <summary>Strict: an American grid, every block square to it, running out along its arterials. Arms: fjord inlets cutting the city into districts. Rock: how much bare rock and pine is left standing inside the city.</summary>
    public bool Strict; public int Arms; public double ArmW = 300, Rock;
    /// <summary>Above 1, its wooden houses stand apart with no party walls and catch from each other fast, before crews can get there. Only a wide road, rock or water stops a fire in them.</summary>
    public double Tinder = 1;

    static (BK, double)[] M(params (BK, double)[] x) => x;

    // Moscow: rings and radials round a walled citadel; a brick centre, then big factories and mile after mile of wooden houses; new Soviet blocks on the avenues
    static readonly CityStyle Moscow = new() { Tinder = 1.5, Like = "Moscow", Shape = CityShape.Radial, Rings = new[] { .1, .24, .46, .74 }, Radials = 11, Citadel = true, RiverW = 110, Avenue = 40, Industry = .42, OldWood = .55, Warp = 30, Sats = 5, InnerT = .5, MidT = .82,
        BrickRoofs = new[] { 15, 8, 13, 3 }, WoodRoofs = new[] { 10, 9, 10, 1 },
        Mix = new[] { M((BK.Tenement, .6), (BK.Superblock, .2), (BK.Mixed, .2)), M((BK.Superblock, .25), (BK.Barracks, .2), (BK.Mixed, .3), (BK.Cottage, .25)), M((BK.Cottage, .6), (BK.Barracks, .3), (BK.Superblock, .1)), M((BK.Cottage, .85), (BK.Allot, .15)) } };
    // Berlin: long straight streets on a rectilinear plan, deep tenement blocks with courtyards behind courtyards, canals, allotment belts, villas in the south-west
    static readonly CityStyle Berlin = new() { Like = "Berlin", Shape = CityShape.Grid, Angle = .32, AxisKm = 2.1, RiverW = 90, Deep = true, Avenue = 34, Industry = .34, OldWood = .3, Warp = 9, CoreT = .07, InnerT = .5, MidT = .78, Sats = 4,
        BrickRoofs = new[] { 15, 0, 14 }, WoodRoofs = new[] { 0, 14, 9 },
        Mix = new[] { M((BK.Tenement, .9), (BK.Mixed, .1)), M((BK.Tenement, .55), (BK.Mixed, .25), (BK.Rows, .2)), M((BK.Allot, .3), (BK.Villa, .25), (BK.Rows, .3), (BK.Tenement, .15)), M((BK.Allot, .45), (BK.Villa, .4), (BK.Cottage, .15)) } };
    // Hamburg: a timber old town on the water, ringed by brick; dock basins and warehouses all along the river
    static readonly CityStyle Hamburg = new() { Like = "Hamburg", Shape = CityShape.Radial, Rings = new[] { .16, .42, .75 }, Radials = 8, Port = true, RiverW = 300, Avenue = 26, Industry = .3, OldWood = .85, Warp = 24, CoreT = .17, InnerT = .45, Sats = 3,
        BrickRoofs = new[] { 15, 0, 3 }, WoodRoofs = new[] { 0, 3, 15 },
        Mix = new[] { M((BK.Tenement, .75), (BK.Mixed, .25)), M((BK.Tenement, .35), (BK.Rows, .4), (BK.Mixed, .25)), M((BK.Rows, .5), (BK.Villa, .25), (BK.Allot, .25)), M((BK.Villa, .4), (BK.Cottage, .3), (BK.Allot, .3)) } };
    // Paris and Warsaw: grand avenues cut across the city from the centre, stone and zinc-roofed blocks, a river with many bridges
    static readonly CityStyle Paris = new() { Like = "Paris, Warsaw", Shape = CityShape.Radial, Rings = new[] { .2, .55, .85 }, Radials = 13, RiverW = 170, Avenue = 44, Industry = .22, OldWood = .3, Warp = 16, CoreT = .18, InnerT = .55, MidT = .82, Sats = 3,
        BrickRoofs = new[] { 8, 8, 14, 8 }, WoodRoofs = new[] { 14, 8, 3 },
        Mix = new[] { M((BK.Tenement, .95), (BK.Square, .05)), M((BK.Tenement, .6), (BK.Mixed, .4)), M((BK.Rows, .35), (BK.Mixed, .3), (BK.Villa, .2), (BK.Cottage, .15)), M((BK.Cottage, .5), (BK.Allot, .3), (BK.Villa, .2)) } };
    // Stalingrad: a strip tens of kilometres long on one bank of a very wide river, no bridges; great tractor and steel works at one end, workers' settlements of wooden houses behind
    static readonly CityStyle Stalingrad = new() { Tinder = 1.5, Like = "Stalingrad", Shape = CityShape.Strip, RiverW = 620, Bridges = false, StripWidthKm = 2.6, Avenue = 30, Industry = .4, OldWood = .5, Warp = 18, CoreT = .14, InnerT = .3, MidT = .7, Sats = 2,
        BrickRoofs = new[] { 13, 8, 15 }, WoodRoofs = new[] { 10, 9 },
        Mix = new[] { M((BK.Superblock, .5), (BK.Tenement, .3), (BK.Mixed, .2)), M((BK.Cottage, .5), (BK.Barracks, .3), (BK.Superblock, .2)), M((BK.Cottage, .7), (BK.Barracks, .3)), M((BK.Cottage, .9), (BK.Allot, .1)) } };
    // Liege: a river valley crowded with steelworks and coal, terraces of brick climbing behind them
    static readonly CityStyle Liege = new() { Like = "Liege", Shape = CityShape.Strip, RiverW = 120, StripWidthKm = 1.5, Avenue = 20, Industry = .62, OldWood = .35, Warp = 22, CoreT = .22, InnerT = .45, MidT = .75, Sats = 2,
        BrickRoofs = new[] { 14, 15, 3 }, WoodRoofs = new[] { 14, 15 },
        Mix = new[] { M((BK.Rows, .6), (BK.Mixed, .4)), M((BK.Rows, .8), (BK.Mixed, .2)), M((BK.Rows, .7), (BK.Cottage, .3)), M((BK.Cottage, .5), (BK.Rows, .3), (BK.Allot, .2)) } };
    // London: spreading terraces, street after street of them, with a little walled old centre
    static readonly CityStyle London = new() { Like = "London", Shape = CityShape.Radial, Rings = new[] { .12, .4, .75 }, Radials = 9, RiverW = 170, Avenue = 20, Industry = .22, OldWood = .25, Warp = 34, CoreT = .1, InnerT = .3, MidT = .7, Sats = 3,
        BrickRoofs = new[] { 14, 15, 3 }, WoodRoofs = new[] { 14, 3 },
        Mix = new[] { M((BK.Rows, .6), (BK.Tenement, .25), (BK.Mixed, .15)), M((BK.Rows, .9), (BK.Mixed, .1)), M((BK.Rows, .7), (BK.Villa, .3)), M((BK.Villa, .6), (BK.Cottage, .25), (BK.Allot, .15)) } };
    // A provincial town of the east: a market square, a church, wooden houses on long plots
    static readonly CityStyle Provincial = new() { Tinder = 1.2, Like = "provincial town", Shape = CityShape.Radial, Rings = new[] { .25, .65 }, Radials = 5, RiverW = 60, Avenue = 16, Industry = .12, OldWood = .9, Warp = 26, CoreT = .2, InnerT = .35, MidT = .65, Sats = 1,
        BrickRoofs = new[] { 15, 3 }, WoodRoofs = new[] { 10, 12, 9 },
        Mix = new[] { M((BK.Mixed, .5), (BK.Cottage, .5)), M((BK.Cottage, .9), (BK.Mixed, .1)), M((BK.Cottage, .85), (BK.Allot, .15)), M((BK.Cottage, .8), (BK.Allot, .2)) } };
    static readonly CityStyle HarbourTown = new() { Like = "Hamburg, on a small scale", Shape = CityShape.Radial, Rings = new[] { .25, .65 }, Radials = 6, Port = true, RiverW = 130, Avenue = 18, Industry = .2, OldWood = .85, Warp = 26, CoreT = .22, InnerT = .45, Sats = 1,
        BrickRoofs = new[] { 15, 0, 3 }, WoodRoofs = new[] { 0, 3 },
        Mix = new[] { M((BK.Mixed, .5), (BK.Rows, .5)), M((BK.Rows, .6), (BK.Cottage, .4)), M((BK.Cottage, .7), (BK.Allot, .3)), M((BK.Cottage, .8), (BK.Allot, .2)) } };

    // ---------------------------------------------------------------- Vulfen. Roofs: 14 slate, 0 and 15 red tile, 9 tar, 16-18 painted wood, 19 turf, 20 flat slab; 21-22 shingle, 23 flat tar, 24 masonry, 25 brick
    // Stockholm, Gothenburg: districts on islands and peninsulas joined by bridges, airy perimeter blocks in the middle, then slabs and painted wooden houses among rock and pine
    static readonly CityStyle Stockholm = new() { Tinder = 1.8, Like = "Stockholm, Gothenburg", Shape = CityShape.Radial, Rings = new[] { .16, .5, .85 }, Radials = 7, Port = true, RiverW = 170, Arms = 5, ArmW = 340, Rock = .3, Avenue = 30, Industry = .2, OldWood = .35, Warp = 34, CoreT = .09, InnerT = .42, MidT = .72, Sats = 3, WaterWorks = BK.Timber,
        BrickRoofs = new[] { 14, 14, 0, 15, 20 }, WoodRoofs = new[] { 16, 17, 18, 9 },
        Mix = new[] { M((BK.Airy, .75), (BK.Mixed, .1), (BK.Slab, .15)), M((BK.Airy, .3), (BK.Slab, .35), (BK.WoodTown, .35)), M((BK.WoodTown, .55), (BK.Slab, .2), (BK.Villa, .15), (BK.Allot, .1)), M((BK.WoodTown, .6), (BK.Villa, .2), (BK.Allot, .2)) } };
    // Bergen: a timber wharf quarter on the water, then wooden town climbing the rock
    static readonly CityStyle Bergen = new() { Tinder = 2, Like = "Bergen, Oslo", Shape = CityShape.Radial, Rings = new[] { .2, .6 }, Radials = 6, Port = true, RiverW = 150, Arms = 4, ArmW = 280, Rock = .36, Avenue = 28, Industry = .16, OldWood = .85, Warp = 36, CoreT = .12, InnerT = .3, MidT = .75, Sats = 2, WaterWorks = BK.Timber,
        BrickRoofs = new[] { 14, 0, 15 }, WoodRoofs = new[] { 16, 17, 18, 18, 9, 19 },
        Mix = new[] { M((BK.Airy, .5), (BK.WoodTown, .4), (BK.Mixed, .1)), M((BK.WoodTown, .8), (BK.Slab, .1), (BK.Villa, .1)), M((BK.WoodTown, .75), (BK.Villa, .15), (BK.Allot, .1)), M((BK.WoodTown, .6), (BK.Allot, .4)) } };
    // Helsinki: granite and new functionalist slabs, built in rows with light between them
    static readonly CityStyle Helsinki = new() { Tinder = 1.8, Like = "Helsinki", Shape = CityShape.Radial, Rings = new[] { .18, .55 }, Radials = 6, Port = true, RiverW = 150, Arms = 3, ArmW = 300, Rock = .34, Avenue = 34, Industry = .18, OldWood = .1, Warp = 26, CoreT = .1, InnerT = .35, MidT = .75, Sats = 2, WaterWorks = BK.Timber,
        BrickRoofs = new[] { 20, 20, 14, 24 }, WoodRoofs = new[] { 17, 16, 9 },
        Mix = new[] { M((BK.Airy, .6), (BK.Slab, .4)), M((BK.Slab, .65), (BK.WoodTown, .25), (BK.Airy, .1)), M((BK.Slab, .35), (BK.WoodTown, .5), (BK.Allot, .15)), M((BK.WoodTown, .7), (BK.Allot, .3)) } };
    // Trondheim: a wooden town cut by wide fire avenues on a regular plan, after the fires of the past
    static readonly CityStyle Trondheim = new() { Tinder = 1.5, Like = "Trondheim", Shape = CityShape.Grid, Angle = .18, AxisKm = .8, RiverW = 110, Avenue = 46, Rock = .12, Industry = .14, OldWood = .55, Warp = 7, CoreT = .1, InnerT = .35, MidT = .7, Sats = 1, WaterWorks = BK.Timber,
        BrickRoofs = new[] { 14, 0 }, WoodRoofs = new[] { 17, 16, 18 },
        Mix = new[] { M((BK.WoodTown, .85), (BK.Mixed, .15)), M((BK.WoodTown, .9), (BK.Villa, .1)), M((BK.WoodTown, .8), (BK.Allot, .2)), M((BK.WoodTown, .6), (BK.Allot, .4)) } };
    // a small wooden harbour among skerries
    static readonly CityStyle Skerry = new() { Tinder = 2, Like = "a Nordic harbour town", Shape = CityShape.Radial, Rings = new[] { .3, .7 }, Radials = 5, Port = true, RiverW = 90, Arms = 2, ArmW = 110, Rock = .34, Avenue = 20, Industry = .14, OldWood = .9, Warp = 30, CoreT = .14, InnerT = .3, MidT = .7, Sats = 1, WaterWorks = BK.Timber,
        BrickRoofs = new[] { 14, 0 }, WoodRoofs = new[] { 16, 17, 18, 18 },
        Mix = new[] { M((BK.WoodTown, .85), (BK.Mixed, .15)), M((BK.WoodTown, .9), (BK.Allot, .1)), M((BK.WoodTown, .75), (BK.Allot, .25)), M((BK.WoodTown, .6), (BK.Allot, .4)) } };
    // a mill town up a valley: timber yards and paper mills on the river
    static readonly CityStyle MillTown = new() { Tinder = 2, Like = "a Nordic mill town", Shape = CityShape.Strip, RiverW = 90, StripWidthKm = 1.1, Rock = .24, Avenue = 18, Industry = .42, OldWood = .9, Warp = 22, CoreT = .18, InnerT = .35, MidT = .7, Sats = 1, WaterWorks = BK.Timber, WorksKind = BK.Timber,
        BrickRoofs = new[] { 14, 15 }, WoodRoofs = new[] { 18, 16, 9 },
        Mix = new[] { M((BK.WoodTown, .8), (BK.Mixed, .2)), M((BK.WoodTown, .9), (BK.Allot, .1)), M((BK.WoodTown, .8), (BK.Allot, .2)), M((BK.WoodTown, .6), (BK.Allot, .4)) } };
    // Detroit, Chicago: a strict grid with long arterials, a tight downtown of tall masonry, belts of wooden frame houses without end, vast plants set apart along rail and water
    static readonly CityStyle Detroit = new() { Tinder = 2, Like = "Detroit, Chicago", Shape = CityShape.Grid, Strict = true, Angle = .06, AxisKm = 1.6, Port = true, RiverW = 170, Avenue = 40, Industry = .38, Warp = 2, CoreT = .075, InnerT = .3, MidT = .75, Sats = 4, CoreKind = BK.Downtown, WorksKind = BK.Plant, WaterWorks = BK.Plant,
        BrickRoofs = new[] { 23, 23, 24, 25 }, WoodRoofs = new[] { 21, 22 },
        Mix = new[] { M((BK.Walkup, .5), (BK.Frame, .35), (BK.Mixed, .15)), M((BK.Frame, .85), (BK.Walkup, .15)), M((BK.Frame, .8), (BK.Villa, .1), (BK.Allot, .1)), M((BK.Frame, .7), (BK.Allot, .3)) } };
    // Seattle: the same grid laid across hills and water, with timber and shipping on the front
    static readonly CityStyle Seattle = new() { Tinder = 2, Like = "Seattle", Shape = CityShape.Grid, Strict = true, Angle = .42, AxisKm = 1.2, Port = true, RiverW = 170, Avenue = 32, Rock = .14, Industry = .24, Warp = 3, CoreT = .09, InnerT = .3, MidT = .75, Sats = 2, CoreKind = BK.Downtown, WorksKind = BK.Plant, WaterWorks = BK.Timber,
        BrickRoofs = new[] { 23, 24, 25 }, WoodRoofs = new[] { 21, 22, 22 },
        Mix = new[] { M((BK.Walkup, .45), (BK.Frame, .45), (BK.Mixed, .1)), M((BK.Frame, .9), (BK.Walkup, .1)), M((BK.Frame, .8), (BK.Allot, .2)), M((BK.Frame, .65), (BK.Allot, .35)) } };
    // Pittsburgh: a river valley of steel, with frame houses crowding up the sides
    static readonly CityStyle Pittsburgh = new() { Tinder = 2, Like = "Pittsburgh", Shape = CityShape.Strip, RiverW = 150, StripWidthKm = 1.5, Avenue = 24, Industry = .6, Warp = 8, CoreT = .14, InnerT = .35, MidT = .7, Sats = 2, CoreKind = BK.Downtown, WorksKind = BK.Plant, WaterWorks = BK.Plant,
        BrickRoofs = new[] { 23, 24, 25 }, WoodRoofs = new[] { 21, 22 },
        Mix = new[] { M((BK.Frame, .6), (BK.Walkup, .4)), M((BK.Frame, .85), (BK.Walkup, .15)), M((BK.Frame, .85), (BK.Allot, .15)), M((BK.Frame, .6), (BK.Allot, .4)) } };

    static CityStyle Sized(CityStyle s, double km)
    {
        var c = (CityStyle)s.MemberwiseClone(); c.Km = km; return c;
    }
    /// <summary>Which city is modelled on what.</summary>
    public static CityStyle? For(string city) => city switch
    {
        "Tamarsk" => Sized(Moscow, 7.5), "Verchny" => Sized(Berlin, 6), "Kolvograd" => Sized(Hamburg, 4.8), "Pelkaja" => Sized(Paris, 4), "Lieben" => Sized(Stalingrad, 7), "Karst" => Sized(Liege, 3),
        "Borrow" => Sized(London, 2.8), "Dravik" => Sized(HarbourTown, 2), "Gorsk" => Sized(Provincial, 1.9), "Marren" => Sized(Provincial, 1.2), "Sund" => Sized(HarbourTown, 1.1), "Ludna" => Sized(Provincial, 1.1),
        "Ostrand" => Sized(Detroit, 6), "Hjalmar" => Sized(Stockholm, 5), "Fjorgard" => Sized(Bergen, 4.8), "Nordhavn" => Sized(Seattle, 4), "Pohjaa" => Sized(Helsinki, 3.4), "Kaldvik" => Sized(Pittsburgh, 3),
        "Tarvik" => Sized(Trondheim, 2.2), "Skarn" => Sized(Skerry, 1.3), "Vess" => Sized(Skerry, 1.2), "Orsvik" => Sized(Skerry, 1.1), "Hogvik" => Sized(MillTown, 1.3),
        _ => null,
    };
}
