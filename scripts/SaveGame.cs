using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace Firebomber;

/// <summary>A campaign on disk, at a quiet moment: nothing flying, no fire raging. Written compressed; only what differs from a freshly made world is kept, since the world, its cities and their buildings are made again the same from their seeds.
/// Kept: the clock, the event queue and the diary; every city's record; every damaged building in each city that has been bombed; the fire's clock and wind; the crews' roster.</summary>
public static class SaveGame
{
    const string Magic = "FIREBOMBER-SAVE"; const int Version = 8;      // 8: what is broken at the fields, depots, guns and radar; raids aimed at sites;      // 7: fields' standing orders and patrols, ferries in the air;      // 6: the history behind the graphs, and raid warnings;      // 5: the UCS planner, works sizes, raids aimed at works;      // 3: the works, dumps, crews and schools; 4: war support
    public static string Dir { get { var d = System.Environment.GetEnvironmentVariable("FIREBOMBER_SAVES") ?? Godot.ProjectSettings.GlobalizePath("user://saves"); Directory.CreateDirectory(d); return d; } }
    public static string Manual => Path.Combine(Dir, "campaign.fbsave");
    public static string Auto => Path.Combine(Dir, "autosave.fbsave");
    /// <summary>The newer of the two saves, if there is one.</summary>
    public static string? Latest => new[] { Manual, Auto }.Where(File.Exists).OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();

    public sealed class State { public Campaign Camp = null!; public FireSim Fire = null!; public Roster Crews = null!; public Forces Forces = null!; }

    public static void Write(string path, World w, Campaign camp, FireSim fire, Roster crews, Forces forces)
    {
        var tmp = path + ".tmp";
        using (var fs = File.Create(tmp)) using (var gz = new GZipStream(fs, CompressionLevel.Optimal)) using (var o = new BinaryWriter(gz))
        {
            o.Write(Magic); o.Write(Version);
            // the campaign
            o.Write(camp.Seed); o.Write(camp.Now);
            var ev = camp.Pending.ToList(); o.Write(ev.Count);
            foreach (var e in ev) { o.Write(e.T); o.Write((int)e.Kind); o.Write(e.Target?.Name ?? ""); o.Write(e.Waves); o.Write(e.Works); o.Write(e.Site); }
            o.Write(camp.Log.Count); foreach (var l in camp.Log) { o.Write(l.T); o.Write(l.Text); o.Write(l.Alarm); }
            // the cities
            o.Write(fire.T); o.Write(fire.WindX); o.Write(fire.WindY);
            o.Write(fire.Records.Count);
            foreach (var (p, r) in fire.Records)
            {
                o.Write(p.Name); o.Write(r.Raids); o.Write(r.Firestorms); o.Write(r.Lost); o.Write(r.Rebuilt); o.Write(r.Tonnes); o.Write(r.Dead); o.Write(r.Injured); o.Write(r.Homeless);
                o.Write(r.LastHitT); o.Write(r.LastStormT); o.Write(r.DeadAtRaid); o.Write(r.HomelessAtRaid); o.Write(r.TonnesAtRaid); o.Write(r.LostAtRaid);
            }
            o.Write(fire.Cities.Count);
            foreach (var c in fire.Cities)
            {
                o.Write(c.P.Name); o.Write(c.B.Length); o.Write(c.Brigade);
                var ch = fire.Changes(c).ToList(); o.Write(ch.Count);
                int last = 0; foreach (var (i, st, hp, open) in ch) { o.Write7(i - last); last = i; o.Write((byte)(st | (open ? 0x80 : 0))); o.Write(hp); }
            }
            // the crews
            for (int s = 0; s < 2; s++) { o.Write(crews.Dead[s]); o.Write(crews.Recovered[s]); o.Write(crews.Prisoners[s]); o.Write(crews.LostAtSea[s]); }
            o.Write(crews.Raid); o.Write(crews.CaptainsLost);
            foreach (var list in new[] { crews.WolfPilots, crews.UcsPilots }) { o.Write(list.Count); foreach (var q in list) { o.Write(q.Name); o.Write(q.Skill); o.Write(q.Aggression); o.Write(q.Hunter); o.Write(q.Career); o.Write(q.Sorties); } }
            foreach (var list in new[] { crews.WolfCaptains, crews.UcsCaptains }) { o.Write(list.Count); foreach (var q in list) { o.Write(q.Name); o.Write(q.Nerve); o.Write(q.Sorties); } }
            // the aircraft, field by field
            o.Write(forces.Bases.Count);
            foreach (var b in forces.Bases)
            {
                o.Write(b.Field.Name); o.Write(b.Of.Count);
                foreach (var (k, s) in b.Of) { o.Write((int)k); o.Write(s.Ready); o.Write(s.Coming.Count); foreach (var (t, n) in s.Coming) { o.Write(t); o.Write(n); } }
            }
            // the works, the dumps, the crews and the schools
            var ind = forces.Ind;
            o.Write(ind.Works.Count); foreach (var q in ind.Works) { o.Write(q.City.Name); o.Write((int)q.Making); o.Write(q.Progress); o.Write(q.Retooled); }
            for (int s = 0; s < 2; s++) { o.Write(ind.Fuel[s]); o.Write(ind.He[s]); o.Write(ind.Inc[s]); o.Write(ind.BomberCrews[s]); o.Write(ind.Pilots[s]); }
            o.Write(ind.BomberIntake); o.Write(ind.PassedOut);
            o.Write(ind.Made.Count); foreach (var (p, n) in ind.Made) { o.Write((int)p); o.Write(n); }
            o.Write(ind.Schools.Count); foreach (var c in ind.Schools) { o.Write(c.T); o.Write(c.Wolf); o.Write(c.Bomber); o.Write(c.N); }
            // war support
            var war = forces.War;
            for (int s = 0; s < 2; s++) { o.Write(war.Support[s]); o.Write(war.Habit[s]); o.Write(war.LastShock[s]); o.Write(war.LastGain[s]); o.Write(war.WeeksBroken[s]); o.Write(war.DeadSeen[s]); o.Write(war.HomelessSeen[s]); o.Write(war.StormsSeen[s]); o.Write(war.BombersDowned[s]); o.Write(war.CrewsLost[s]); }
            o.Write(war.DefencePoints); o.Write(war.Loser); o.Write(war.UcsEstimate); o.Write(war.EstimateSpread);
            o.Write(war.Destroyed.Count); foreach (var n in war.Destroyed) o.Write(n);
            // the UCS planner, and the works' sizes (it disperses them)
            var pl = forces.Planner;
            o.Write((int)pl.Doctrine); o.Write(pl.DoctrineSince); o.Write(pl.LastBig); o.Write(pl.StandDownUntil); o.Write(pl.LastMixChange); o.Write(pl.DayLoss); o.Write(pl.NightLoss); o.Write(pl.Raids); o.Write(pl.BigRaids); o.Write(pl.EscortsPerBox);
            foreach (var dict in new[] { pl.LossAt, pl.PlayerFrom, pl.Flak }) { o.Write(dict.Count); foreach (var (k, v) in dict) { o.Write(k); o.Write(v); } }
            o.Write(pl.RaidsOn.Count); foreach (var (k, v) in pl.RaidsOn) { o.Write(k); o.Write(v); }
            o.Write(ind.Works.Count); foreach (var q in ind.Works) { o.Write(q.City.Name); o.Write(q.Points); }
            // the history, the raid warning and the last raid's attrition
            var hs = forces.Hist;
            foreach (var side in hs.Side) { o.Write(side.Count); foreach (var q in side) { o.Write(q.T); o.Write(q.Support); o.Write(q.Output); o.Write(q.He); o.Write(q.Inc); o.Write(q.Fuel); o.Write(q.Bombers); o.Write(q.Fighters); o.Write(q.BomberCrews); o.Write(q.Pilots); } }
            o.Write(hs.City.Count); foreach (var (name, samples) in hs.City) { o.Write(name); o.Write(samples.Count); foreach (var q in samples) { o.Write(q.T); o.Write(q.Alive); o.Write(q.Standing); o.Write(q.Works); } }
            o.Write(pl.WarnTarget); o.Write(pl.WarnUntil); o.Write(pl.WarnN); o.Write(war.LastAttrition);
            foreach (var ba in forces.Bases) { o.Write(ba.StandDown); o.Write((int)ba.Intercept); o.Write(ba.Patrols.Count); foreach (var p in ba.Patrols) { o.Write((int)p.Kind); o.Write(p.N); o.Write(p.X); o.Write(p.Y); o.Write(p.Hours); } }
            o.Write(forces.Flights.Count); foreach (var q in forces.Flights) { o.Write(q.From.Name); o.Write(q.To.Name); o.Write((int)q.Kind); o.Write(q.N); o.Write(q.T0); o.Write(q.Land); }
            var broke = forces.Sites.All.SelectMany(si => si.Parts.Select((p, n) => (si.Index, n, p))).Where(q => q.p.Down).ToList(); o.Write(broke.Count); foreach (var (si, n, p) in broke) { o.Write(si); o.Write(n); o.Write(p.BackAt); }
        }
        File.Move(tmp, path, true);      // never leave a half-written save in place of a good one
    }

    /// <param name="old">Also read a version 1 save (before aircraft stocks), for looking at; it has no forces.</param>
    public static State Read(string path, World w, bool old = false)
    {
        using var fs = File.OpenRead(path); using var gz = new GZipStream(fs, CompressionMode.Decompress); using var i = new BinaryReader(gz);
        if (i.ReadString() != Magic) throw new InvalidDataException("not a Firebomber save");
        int ver = i.ReadInt32(); if (ver != Version && ver != 2 && ver != 3 && ver != 4 && ver != 5 && ver != 6 && ver != 7 && !(old && ver == 1)) throw new InvalidDataException($"save version {ver}, this game reads {Version}");
        Place City(string n) => w.Cities.First(c => c.Name == n);
        int seed = i.ReadInt32(); double now = i.ReadDouble();
        var ev = new List<CampaignEvent>(); int ne = i.ReadInt32();
        for (int k = 0; k < ne; k++) { double t = i.ReadDouble(); var kind = (Ev)i.ReadInt32(); string tn = i.ReadString(); int waves = i.ReadInt32(); bool works = ver >= 5 && i.ReadBoolean(); string site = ver >= 8 ? i.ReadString() : ""; ev.Add(new CampaignEvent { T = t, Kind = kind, Target = tn == "" ? null : City(tn), Waves = waves, Works = works, Site = site }); }
        var log = new List<LogLine>(); int nl = i.ReadInt32(); for (int k = 0; k < nl; k++) log.Add(new LogLine(i.ReadDouble(), i.ReadString(), i.ReadBoolean()));
        var camp = Campaign.Restore(w, seed, now, ev, log);

        var fire = new FireSim(w); fire.RestoreTime(i.ReadDouble()); fire.WindX = i.ReadDouble(); fire.WindY = i.ReadDouble();
        int nr = i.ReadInt32();
        for (int k = 0; k < nr; k++)
        {
            var r = fire.Rec(City(i.ReadString()));
            r.Raids = i.ReadInt32(); r.Firestorms = i.ReadInt32(); r.Lost = i.ReadInt32(); r.Rebuilt = i.ReadInt32(); r.Tonnes = i.ReadDouble(); r.Dead = i.ReadDouble(); r.Injured = i.ReadDouble(); r.Homeless = i.ReadDouble();
            r.LastHitT = i.ReadDouble(); r.LastStormT = i.ReadDouble(); r.DeadAtRaid = i.ReadDouble(); r.HomelessAtRaid = i.ReadDouble(); r.TonnesAtRaid = i.ReadDouble(); r.LostAtRaid = i.ReadInt32();
        }
        int nc = i.ReadInt32();
        for (int k = 0; k < nc; k++)
        {
            var p = City(i.ReadString()); int n = i.ReadInt32(); float brigade = i.ReadSingle(); var c = fire.Materialise(p);
            if (c.B.Length != n) throw new InvalidDataException($"{p.Name} has {c.B.Length} buildings, the save {n}: the world has changed since it was saved");
            c.Brigade = brigade; int nch = i.ReadInt32(), at = 0;
            var rec = fire.Rec(p); var keep = (rec.Dead, rec.Injured, rec.Homeless, rec.Lost);      // applying the damage again must not count its dead twice
            for (int q = 0; q < nch; q++) { at += i.Read7(); byte sb = i.ReadByte(), hp = i.ReadByte(); fire.Apply(c, at, (byte)(sb & 0x7f), hp, (sb & 0x80) != 0); }
            (rec.Dead, rec.Injured, rec.Homeless, rec.Lost) = keep;      // the changes stay queued: the fire view paints the damage from them
        }
        var crews = new Roster();
        for (int s = 0; s < 2; s++) { crews.Dead[s] = i.ReadInt32(); crews.Recovered[s] = i.ReadInt32(); crews.Prisoners[s] = i.ReadInt32(); crews.LostAtSea[s] = i.ReadInt32(); }
        crews.Raid = i.ReadInt32(); crews.CaptainsLost = i.ReadInt32();
        foreach (var list in new[] { crews.WolfPilots, crews.UcsPilots }) { int n = i.ReadInt32(); for (int k = 0; k < n; k++) list.Add(new PilotRec { Name = i.ReadString(), Skill = i.ReadDouble(), Aggression = i.ReadDouble(), Hunter = i.ReadDouble(), Career = i.ReadInt32(), Sorties = i.ReadInt32() }); }
        foreach (var list in new[] { crews.WolfCaptains, crews.UcsCaptains }) { int n = i.ReadInt32(); for (int k = 0; k < n; k++) list.Add(new CaptainRec { Name = i.ReadString(), Nerve = i.ReadDouble(), Sorties = i.ReadInt32() }); }
        if (ver == 1) return new State { Camp = camp, Fire = fire, Crews = crews, Forces = null! };
        var forces = new Forces(w); int nb = i.ReadInt32();
        for (int k = 0; k < nb; k++)
        {
            var b = forces.Named(i.ReadString()) ?? throw new InvalidDataException("an airfield in the save is not in this world"); int nk = i.ReadInt32();
            for (int q = 0; q < nk; q++) { var s = b.S((Craft)i.ReadInt32()); s.Ready = i.ReadInt32(); int nc2 = i.ReadInt32(); for (int z = 0; z < nc2; z++) s.Coming.Add((i.ReadDouble(), i.ReadInt32())); }
        }
        forces.Ind = Industry.Start(w, now);      // a version 2 save has none: it starts as on the first day, from the save's date
        if (ver >= 3)
        {
            var ind = forces.Ind; int nw = i.ReadInt32();
            for (int k = 0; k < nw; k++) { var q = ind.Of(City(i.ReadString())) ?? throw new InvalidDataException("a works in the save is not in this world"); q.Making = (Product)i.ReadInt32(); q.Progress = i.ReadDouble(); q.Retooled = i.ReadDouble(); }
            for (int s = 0; s < 2; s++) { ind.Fuel[s] = i.ReadDouble(); ind.He[s] = i.ReadDouble(); ind.Inc[s] = i.ReadDouble(); ind.BomberCrews[s] = i.ReadInt32(); ind.Pilots[s] = i.ReadInt32(); }
            ind.BomberIntake = i.ReadInt32(); ind.PassedOut = i.ReadInt32();
            int nm = i.ReadInt32(); for (int k = 0; k < nm; k++) ind.Made[(Product)i.ReadInt32()] = i.ReadDouble();
            ind.Schools.Clear(); int ns = i.ReadInt32(); for (int k = 0; k < ns; k++) ind.Schools.Add(new Course { T = i.ReadDouble(), Wolf = i.ReadBoolean(), Bomber = i.ReadBoolean(), N = i.ReadInt32() });
        }
        var war = forces.War;
        if (ver >= 4)
        {
            for (int s = 0; s < 2; s++) { war.Support[s] = i.ReadDouble(); war.Habit[s] = i.ReadDouble(); war.LastShock[s] = i.ReadDouble(); war.LastGain[s] = i.ReadDouble(); war.WeeksBroken[s] = i.ReadInt32(); war.DeadSeen[s] = i.ReadDouble(); war.HomelessSeen[s] = i.ReadDouble(); war.StormsSeen[s] = i.ReadInt32(); war.BombersDowned[s] = i.ReadInt32(); war.CrewsLost[s] = i.ReadInt32(); }
            war.DefencePoints = i.ReadDouble(); war.Loser = i.ReadInt32(); war.UcsEstimate = i.ReadDouble(); war.EstimateSpread = i.ReadDouble();
            int nd = i.ReadInt32(); for (int k = 0; k < nd; k++) war.Destroyed.Add(i.ReadString());
        }
        if (ver >= 5)
        {
            var pl = forces.Planner;
            pl.Doctrine = (Doctrine)i.ReadInt32(); pl.DoctrineSince = i.ReadDouble(); pl.LastBig = i.ReadDouble(); pl.StandDownUntil = i.ReadDouble(); pl.LastMixChange = i.ReadDouble(); pl.DayLoss = i.ReadDouble(); pl.NightLoss = i.ReadDouble(); pl.Raids = i.ReadInt32(); pl.BigRaids = i.ReadInt32(); pl.EscortsPerBox = i.ReadInt32();
            foreach (var dict in new[] { pl.LossAt, pl.PlayerFrom, pl.Flak }) { int n = i.ReadInt32(); for (int k = 0; k < n; k++) dict[i.ReadString()] = i.ReadDouble(); }
            { int n = i.ReadInt32(); for (int k = 0; k < n; k++) pl.RaidsOn[i.ReadString()] = i.ReadInt32(); }
            { int n = i.ReadInt32(); for (int k = 0; k < n; k++) { var q = forces.Ind.Of(City(i.ReadString())); double p = i.ReadDouble(); if (q != null) q.Points = p; } }
        }
        else forces.Planner.LastBig = now - 9 * 86400;      // an older save: the planner starts fresh, ready for a big raid soon
        if (ver >= 6)
        {
            var hs = forces.Hist;
            foreach (var side in hs.Side) { int n = i.ReadInt32(); for (int k = 0; k < n; k++) side.Add(new SideSample { T = i.ReadDouble(), Support = i.ReadSingle(), Output = i.ReadSingle(), He = i.ReadSingle(), Inc = i.ReadSingle(), Fuel = i.ReadSingle(), Bombers = i.ReadInt32(), Fighters = i.ReadInt32(), BomberCrews = i.ReadInt32(), Pilots = i.ReadInt32() }); }
            int nh = i.ReadInt32();
            for (int k = 0; k < nh; k++) { string name = i.ReadString(); int n = i.ReadInt32(); var list = new List<CitySample>(n); for (int z = 0; z < n; z++) list.Add(new CitySample { T = i.ReadDouble(), Alive = i.ReadSingle(), Standing = i.ReadSingle(), Works = i.ReadSingle() }); hs.City[name] = list; }
            forces.Planner.WarnTarget = i.ReadString(); forces.Planner.WarnUntil = i.ReadDouble(); forces.Planner.WarnN = i.ReadInt32(); war.LastAttrition = i.ReadDouble();
        }
        if (ver >= 7)
        {
            foreach (var ba in forces.Bases) { ba.StandDown = i.ReadBoolean(); ba.Intercept = (InterceptMode)i.ReadInt32(); int n = i.ReadInt32(); for (int k = 0; k < n; k++) ba.Patrols.Add(new Patrol { Kind = (Craft)i.ReadInt32(), N = i.ReadInt32(), X = i.ReadDouble(), Y = i.ReadDouble(), Hours = i.ReadInt32() }); }
            int nf = i.ReadInt32(); for (int k = 0; k < nf; k++) forces.Flights.Add(new FerryFlight { From = forces.Named(i.ReadString())!.Field, To = forces.Named(i.ReadString())!.Field, Kind = (Craft)i.ReadInt32(), N = i.ReadInt32(), T0 = i.ReadDouble(), Land = i.ReadDouble() });
        }
        if (ver >= 8) { int nb2 = i.ReadInt32(); for (int k = 0; k < nb2; k++) { var site = forces.Sites.All[i.ReadInt32()]; var p = site.Parts[i.ReadInt32()]; p.Down = true; p.BackAt = i.ReadDouble(); } }
        fire.ClearSecondaries();
        if (ver < 4)      // an older save: support starts at its baseline, and what was done before the save is not booked again as this week's shock
            for (int s = 0; s < 2; s++) { var recs = fire.Records.Where(kv => kv.Key.Wolf == (s == 1)).Select(kv => kv.Value).ToList(); war.DeadSeen[s] = recs.Sum(r => r.Dead); war.HomelessSeen[s] = recs.Sum(r => r.Homeless); war.StormsSeen[s] = recs.Sum(r => r.Firestorms); }
        if (ver < 4) foreach (var (p, r) in fire.Records) if (p.Layout != null && r.Lost - r.Rebuilt >= .4 * p.Layout.Plots.Length) war.Destroyed.Add(p.Name);
        return new State { Camp = camp, Fire = fire, Crews = crews, Forces = forces };
    }

    static void Write7(this BinaryWriter o, int v) => o.Write7BitEncodedInt(v);
    static int Read7(this BinaryReader i) => i.Read7BitEncodedInt();
}
