using System;
using System.Linq;
using System.Text;

namespace Firebomber;

/// <summary>Run with --dumpsave file: everything in each save in user://saves, written out as text. For looking at what happened in a campaign.</summary>
public partial class Main
{
    void DumpSaves(string path)
    {
        var o = new StringBuilder();
        foreach (var p in new[] { SaveGame.Auto, SaveGame.Manual })
        {
            o.AppendLine($"==== {p}"); if (!System.IO.File.Exists(p)) { o.AppendLine("(none)"); continue; }
            o.AppendLine($"written {System.IO.File.GetLastWriteTime(p)}, {new System.IO.FileInfo(p).Length / 1024} KB");
            try
            {
                var st = SaveGame.Read(p, World, true); var c = st.Camp;
                o.AppendLine($"campaign date {c.DateText}, day {c.DayNo}, seed {c.Seed}");
                o.AppendLine("Due: " + string.Join("; ", c.Pending.Select(e => $"{c.Stamp(e.T)} {e.Kind}{(e.Target != null ? " on " + e.Target.Name + $" x{e.Waves}" : "")}")));
                o.AppendLine("Cities:"); foreach (var (pl, r) in st.Fire.Records.OrderBy(kv => kv.Key.Wolf)) o.AppendLine($"  {pl.Name,-10} {(pl.Wolf ? "Vulfen" : "UCS"),-6} raids {r.Raids}, {r.Tonnes:0} t, storms {r.Firestorms}, lost {r.Lost}, rebuilt {r.Rebuilt}, dead {r.Dead:0}, injured {r.Injured:0}, homeless {r.Homeless:0}");
                o.AppendLine($"Crews: raid {st.Crews.Raid}, Hund pilots {st.Crews.WolfPilots.Count}, UCS pilots {st.Crews.UcsPilots.Count}, captains {st.Crews.WolfCaptains.Count}/{st.Crews.UcsCaptains.Count}, captains lost {st.Crews.CaptainsLost}");
                if (st.Forces != null)
                {
                    o.AppendLine("Fields:");
                    foreach (var b in st.Forces.Bases) o.AppendLine($"  {(b.Wolf ? "V" : "U")} {b.Name,-10} {b.Total,4}/{b.Capacity,-4} " + string.Join("  ", b.Of.Where(kv => kv.Value.Ready + kv.Value.ComingN > 0).Select(kv => $"{Sprites.Specs[kv.Key].Name.Split(' ')[0]} {kv.Value.Ready}+{kv.Value.ComingN}")));
                    var ind = st.Forces.Ind; o.AppendLine("Works:");
                    foreach (var q in ind.Works) o.AppendLine($"  {(q.Wolf ? "V" : "U")} {q.City.Name,-10} {q.Points,5:0.0} pts  plant {st.Fire.Plant(q.City) * 100,3:0}%  {Industry.Name(q.Making)}{(q.Retooled > c.Now ? $" (retooling until {c.Stamp(q.Retooled)})" : "")}  {Industry.PerWeek(q, st.Fire.Plant(q.City), c.Now):0.0} a week");
                    o.AppendLine($"Dumps: HE {ind.He[1]:0} t, incendiary {ind.Inc[1]:0} t, fuel {ind.Fuel[1]:0} t.  Crews: Vulfen {ind.BomberCrews[1]} bomber, {ind.Pilots[1]} fighter; UCS {ind.BomberCrews[0]} bomber, {ind.Pilots[0]} fighter. Schools {ind.Schools.Count} classes, bomber intake {ind.BomberIntake}");
                }
                o.AppendLine("Diary:"); foreach (var l in c.Log) o.AppendLine($"  {c.Stamp(l.T),-18} {(l.Alarm ? "!" : " ")} {l.Text}");
            }
            catch (Exception ex) { o.AppendLine("could not read: " + ex.Message); }
        }
        System.IO.File.WriteAllText(path, o.ToString()); GetTree().Quit();
    }
}
