using System;
using System.Collections.Generic;
using System.Linq;

namespace Firebomber;

/// <summary>What each pilot decides. Nobody gives a fighter a target. Every couple of seconds a pilot looks round, scores what can be seen, and takes the best: go for bombers, go for fighters, get away, or go home.
/// The score depends on who the pilot is (how bold, how much of a hunter), on what is in reach, and on what the pilot was already doing.</summary>
public sealed partial class FighterSim
{
    const double SeeBomber = 18000, SeeFighter = 9000, Keep = .3;
    public Fighter? Watch;

    void Think(Fighter f)
    {
        f.NextThink = T + 1.2 + rng.NextDouble() * 1.6;
        if (f.Act is Act.Zoom && T < f.Until) return;
        if (f.Act is Act.Extend && T < f.Until && !Tailed(f, out _)) return;

        if (f.Rear > 0 && T > f.Rear) { f.Rear = 0; GoHome(f, "rear guard done: leaving"); return; }
        if (f.Mood == Mood.Fleeing) return;
        if (f.Mood == Mood.Ramming)
        {
            if (f.TB == null || !f.TB.Alive)
            {
                f.TB = air.Formations.Where(q => q.Wolf != f.Wolf && q.Phase is not (Phase.Waiting or Phase.Landed)).SelectMany(q => q.Ac).OrderBy(b => Dist(b.X, b.Y, f.X, f.Y)).FirstOrDefault();
                if (f.TB == null || Dist(f.TB.X, f.TB.Y, f.X, f.Y) > 15000) { f.Mood = Mood.Fleeing; GoHome(f, "no bomber within reach: running"); return; }
            }
            f.Act = Act.AttackBomber; return;
        }
        // staying alive comes first. The Wolves have radar and discipline; a green UCS pilot may never look behind
        if (Tailed(f, out var chaser) && rng.NextDouble() < (f.Wolf ? 1 : .15 + .8 * f.Skill))
        {
            if (f.Act != Act.Evade)
            {
                f.Side = Wrap(Math.Atan2(chaser!.Y - f.Y, chaser.X - f.X) - f.Th) > 0 ? 1 : -1;
                // a Hund picks one line away from the chaser, no more than a firm turn off its nose, and holds it in a shallow dive: straight and fast is what nothing of theirs can follow
                double away = Math.Atan2(f.Y - chaser.Y, f.X - chaser.X), off = Math.Clamp(Wrap(away - f.Th), -1, 1), line = f.Th + off;
                f.GoX = f.X + Math.Cos(line) * 30000; f.GoY = f.Y + Math.Sin(line) * 30000; f.GoZ = Math.Max(800, f.Z - 500);
            }
            Set(f, Act.Evade, f.Wolf ? $"running: nothing they have can stay with me" : $"turning hard: {Sprites.Specs[chaser!.Kind].Name} on my tail");
            f.TF = chaser; f.NextThink = T + 1.2; return;
        }
        double home = Dist(f.X, f.Y, f.HomeX, f.HomeY), bingo = home / f.P.VCruise * 1.2 + 150;
        if (f.Fuel < bingo) { GoHome(f, "low on fuel"); return; }
        if (f.Ammo <= 0) { GoHome(f, "out of ammunition"); return; }
        if (f.H.Eng[0] < f.H.D.EngHp * .5) { GoHome(f, "engine running rough: nursing it home"); return; }
        if (f.H.Burning && f.Wolf) { GoHome(f, "on fire: going home while I can"); return; }
        if (f.H.CrewState[0] == 1 && (f.Wolf || f.W!.Morale < .5)) { GoHome(f, "wounded"); return; }
        if (f.Wolf)
        {
            if (f.Damage > (f.Defender ? .7 : .6)) { GoHome(f, "hit: no sense staying"); return; }      // an aircraft and a pilot are worth more than one more pass
        }
        else
        {
            // a winning squadron flies on with holes in it; one that is losing heart does not
            if (f.Damage > .5 + .45 * f.W!.Morale) { GoHome(f, f.W.Morale > .5 ? "too badly hit to go on" : "hit, and the fight is going badly"); return; }
            if (f.Leader != null && !f.Leader.Alive && f.W.Morale < .22) { GoHome(f, "lost my leader, and we are losing: breaking off"); return; }
        }
        if (f.Act == Act.Home) return;
        // outnumbered with no advantage: a Wolf pilot takes the speed away and comes back on better terms. Not a retreat, just sums
        if (f.Wolf && f.Rear == 0 && All.Count(e => !e.Wolf && e.Airborne && Dist(f.X, f.Y, e.X, e.Y) < 1500) >= 4 && f.Act != Act.Zoom) { ZoomAway(f, "too many at once: away, then back with height"); return; }

        var wg = f.W; bool sq = !f.Wolf && wg != null && wg.Box == null && wg.Phase != WPhase.Free && f.Mood == Mood.Normal && f.Charge == null;
        bool shotAt = All.Any(e => e.Wolf != f.Wolf && e.Airborne && e.TF == f && Dist(e.X, e.Y, f.X, f.Y) < 2000);
        if (sq && f.Role == Role.Bombers && !shotAt && (wg!.Phase is WPhase.Gather or WPhase.Reform || (wg.Phase == WPhase.Attack && f.LastPass > wg.PhaseT && f.Act != Act.AttackBomber)))
        { Set(f, Act.Form, wg.Lead == f ? (wg.Phase == WPhase.Gather ? "leading the squadron into position" : "leading the squadron round for another pass") : wg.Phase == WPhase.Gather ? "forming up on the leader to go in together" : "rallying on the leader for another pass"); f.IdleSince = T; f.TB = null; f.TF = null; return; }

        double best = .05, murk = air.MurkAt(f.X, f.Y); Bomber? bb = null; Fighter? bf = null; string why = "";
        // in thick cloud a pilot can lose sight of the bomber he was after
        bool blind = false; if (f.TB != null) { var skyTB = air.Wx(f.TB.X, f.TB.Y); blind = skyTB.cloud > .78 && N.Hash(f.Id, (int)(T / 40), 16) < (skyTB.cloud - .7) * .9 + skyTB.rain * .3; }
        // fighters in reach
        foreach (var e in All)
        {
            if (e.Wolf == f.Wolf || !e.Airborne) continue;
            double d = Dist(f.X, f.Y, e.X, e.Y); if (d > (f.Wolf ? SeeFighter * 1.6 * (1 - .4 * murk) : SeeFighter * (1 - .8 * murk))) continue;      // the Wolves have radar; at night everyone else is nearly blind
            // an escort's job is its bombers: it goes for what threatens them, not for whatever it can see
            if (f.Charge != null && f.Rear == 0 && e.TF != f && !(e.Act == Act.AttackBomber && e.TB?.F == f.Charge) && Dist(e.X, e.Y, f.Charge.Lead.X, f.Charge.Lead.Y) > (f.Role == Role.Sweep ? 30000 : f.W!.Pressing ? 14000 : 9000)) continue;
            double s = .45 + .45 * (1 - d / SeeFighter) + f.Hunter * (f.Wolf ? .4 : 1);
            if (!f.Wolf) s += (rng.NextDouble() - .5) * (1.3 - f.Skill) * .7;      // erratic: the less trained, the less sense their choices make
            if (f.Wolf && f.W!.Pressing) s += .2;
            double edge = ((f.Z + f.V * f.V / (2 * G)) - (e.Z + e.V * e.V / (2 * G))) / 1500; s += Math.Clamp(edge, -.3, .3);   // height and speed in hand
            string w1 = edge > .15 ? "I have the height" : edge < -.15 ? "it has the height, but it is close" : "even odds";
            if (e.TF == f) { s += .5; w1 = "it is after me"; }
            if (f.Leader != null && e.TF == f.Leader) { s += .6; w1 = "it is on my leader"; }
            if (f.Leader != null && f.Leader.TF == e) { s += .8; w1 = "backing my leader"; }
            if (f.Wolf)
            {
                if (e.Act == Act.AttackBomber) { s += .55; w1 = e.TB != null ? $"it is attacking box {e.TB.F.Name}" : w1; }
                if (f.Rear > 0 && f.Charge != null && Dist(e.X, e.Y, f.Charge.Lead.X, f.Charge.Lead.Y) < 6000) { s += .4; w1 = "holding them off the box"; }
                if (e.Act is Act.Stalk or Act.Hunt or Act.Climb or Act.Form && e.TF == null) { s += f.Role == Role.Sweep ? .55 : .25; w1 = "bouncing them while they form up"; }      // the best kill is the one that never saw you
                if (f.Charge != null) s -= (f.W!.Pressing ? .35 : 1) * (1 - f.Aggression) * Dist(f.X, f.Y, f.Charge.Lead.X, f.Charge.Lead.Y) / (f.Role == Role.Sweep ? 30000 : 9000);      // the sweep is out in front to find them: distance is its job      // discipline: do not stray from the bombers, unless the group is winning
            }
            else if (f.Charge != null) { if (e.Act == Act.AttackBomber && e.TB?.F == f.Charge) { s += .55; w1 = "it is on our bombers"; } else s -= .1; }
            else s -= .35;
            if (sq && f.Role == Role.Cover && e.Wolf) { s += .9; w1 = "told off to keep the escort busy"; }
            if (f.Kind == Craft.I7 && e.TF != f && !(f.Leader != null && e.TF == f.Leader)) s -= .4;      // the I-7 is for bombers: it fights a Hund only to save itself or its leader
            if (sq && f.Role == Role.Bombers && e.TF != f) s -= .35;
            if (!f.Wolf) { int on = 0; foreach (var m in All) if (m != f && !m.Wolf && m.TF == e && m.Act == Act.Fight) on++; if (on > 0) { s += Math.Min(.5, .18 * on); w1 = $"{on} of ours already on it: surround it"; } }      // numbers are the doctrine: mob it
            if (!f.Wolf && e.TF != null && e.TF.W == f.W && e.TF != f && d < 6000) { s += .55; w1 = "it is after one of ours"; }      // the swarm goes to its own: an escort that bounces one I-8 drags its squadron off the bombers       // doctrine: the bombers are the job, unless something forces the issue
            if (Math.Abs(e.Z - f.Z) > 1800) s -= .4;
            if (f.TF == e && f.Act == Act.Fight) s += Keep;
            if (s > best) { best = s; bf = e; bb = null; why = w1; }
        }
        // a squadron gathers before it goes in, off to the side and ahead, so that the attack comes all at once. The green and the impatient do not always wait
        if (!sq && !f.Wolf && f.Charge == null && f.Mood == Mood.Normal && f.Act != Act.AttackBomber && bf == null && (f.Skill >= .55 || N.Hash(f.Id, 5, 82) < .6))
        {
            var box = f.W?.Target; if (box != null && !(box.Phase == Phase.Outbound || box.Phase == Phase.Egress || box.Phase == Phase.Returning)) box = null;
            if (box != null && box.Lead.Z < f.P.Ceil + 40 && Dist(f.X, f.Y, box.Lead.X, box.Lead.Y) < SeeBomber * (1 - .85 * murk))
            {
                if (double.IsNaN(f.StalkSince)) f.StalkSince = T;
                int onIt = All.Count(e => !e.Wolf && e.Act == Act.AttackBomber && Dist(e.X, e.Y, box.Lead.X, box.Lead.Y) < 4000), mates = All.Count(e => !e.Wolf && e != f && e.Airborne && e.W == f.W && Dist(e.X, e.Y, f.X, f.Y) < 5000);
                bool front = (f.X - box.Lead.X) * Math.Cos(box.Lead.Th) + (f.Y - box.Lead.Y) * Math.Sin(box.Lead.Th) > 2000;
                if (onIt < 10 && mates < 6 && T - f.StalkSince < 75 && !(front && mates >= 3))
                {
                    double side = (f.Id % 2 == 0 ? 1 : -1) * 2500, lx = Math.Cos(box.Lead.Th), ly = Math.Sin(box.Lead.Th);
                    f.GoX = box.Lead.X + lx * 9000 + ly * side; f.GoY = box.Lead.Y + ly * 9000 - lx * side; f.GoZ = Math.Min(box.Lead.Z + 300, f.P.Ceil);
                    Set(f, Act.Stalk, $"forming up with the squadron to go for box {box.Name}"); f.IdleSince = T; return;
                }
            }
        }
        // bombers in reach. An escort stays with its own box unless the enemy bomber is close by
        {
            int guards = All.Count(e => e.Wolf != f.Wolf && e.Airborne && Dist(f.X, f.Y, e.X, e.Y) < 3500);
            foreach (var fm in air.Formations)
            {
                if (fm.Wolf == f.Wolf || fm.Phase is Phase.Waiting or Phase.Landed) continue;
                if (Dist(f.X, f.Y, fm.Lead.X, fm.Lead.Y) > SeeBomber + 2000) continue;
                foreach (var a in fm.Ac)
                {
                    double d = Dist(f.X, f.Y, a.X, a.Y); if (blind || d > SeeBomber * (1 - (f.Wolf ? .5 : .85) * murk) || a.Z > f.P.Ceil + 40) continue;
                    if (f.Charge != null && d > 5000) continue;
                    double s = .8 + .4 * (1 - d / SeeBomber) - f.Hunter * .5 - .13 * guards * (1.2 - f.Aggression) + (rng.NextDouble() - .5) * (1.3 - f.Skill) * .7;
                    string w1 = guards == 0 ? $"box {fm.Name} has no escort near" : $"box {fm.Name}, despite the escort";
                    if (f.Wolf) s += .25 * f.Skill;
                    if (f.Kind == Craft.I7) s += .3; else if (!f.Wolf && f.Charge == null) s -= .1;
                    if (a.Straggler) { s += .45; w1 = $"a straggler from box {fm.Name}"; } else if (a.Damage > .25) { s += .2; w1 = $"a damaged bomber in box {fm.Name}"; }
                    if (fm.Spread < .9) s -= .1;      // a tight box looks a harder nut
                    if (f.TB == a && f.Act == Act.AttackBomber) s += Keep + .22; s -= .22 * a.Pressed;
                    if (f.W?.Target == fm) s += sq && f.Role == Role.Bombers ? .8 : .3; else if (sq && f.Role == Role.Bombers) s -= .4;
                    if (sq && f.Role == Role.Cover) s -= .3;
                    if (fm.Phase != Phase.Outbound && !a.Straggler) { s -= fm.Aborted ? .45 : .25; w1 = $"box {fm.Name}, on its way home"; }      // its bombs are gone or it has given up: the ones still coming in matter more
                    s -= .05 * Math.Max(0, fm.Attackers - 5) * (f.TB?.F == fm ? .5 : 1);
                    double along0 = (f.X - a.X) * Math.Cos(a.Th) + (f.Y - a.Y) * Math.Sin(a.Th);
                    if (f.P.VMax - a.V < 35 && along0 > 0) s += .25;      // too slow to chase it down: take the ones still ahead
                    if (f.P.VMax - a.V < 10 && along0 < -1500 && !a.Straggler) s -= f.P.VMax < a.V ? 1.2 : .5;      // and one it can never catch at all is no target      // a tail chase that would take minutes under its gunners: look for one coming the other way
                    if (f.Leader != null && f.Leader.TB == a) { s += .6; w1 = "with my leader"; }
                    if (s > best) { best = s; bb = a; bf = null; why = w1; }
                }
            }
        }
        if (bb != null)
        {
            if (f.TB != bb || f.Stage < 0 || f.Act != Act.AttackBomber) { f.Pass = PassFor(f, bb); f.Stage = 0; }
            f.TB = bb; f.TF = null; f.StalkSince = double.NaN; Set(f, Act.AttackBomber, (f.Pass == Pass.HeadOn ? "head-on, " : f.Pass == Pass.Slash ? "diving through, " : "from astern, ") + why); f.IdleSince = T; return;
        }
        if (bf != null) { f.TF = bf; f.TB = null; Set(f, Act.Fight, $"fighting {Sprites.Specs[bf.Kind].Name}: {why}"); f.IdleSince = T; return; }
        f.TB = null; f.TF = null;

        // nothing in reach
        if (f.W != null && f.W.Wolf && f.W.Withdrawing && f.Rear == 0) { GoHome(f, "the group is withdrawing"); return; }
        if (f.Leader != null && f.Leader.Airborne && f.Leader.Act != Act.Home) { Set(f, Act.Follow, "staying with my leader"); return; }
        if (f.Charge != null || (f.Wolf && !f.Defender))
        {
            if (f.Charge == null || f.Charge.Destroyed || f.Charge.Phase == Phase.Landed)
            {
                f.Charge = air.Formations.Where(q => q.Wolf == f.Wolf && q.Phase is Phase.Outbound or Phase.Egress or Phase.Returning && !q.Destroyed).OrderBy(q => Dist(q.Lead.X, q.Lead.Y, f.X, f.Y)).FirstOrDefault();
                if (f.Charge == null) { GoHome(f, "no bombers left to cover"); return; }
            }
            Set(f, Act.Cover, "covering box " + f.Charge.Name); return;
        }
        // an interceptor is sent against a raid still coming in: the job is to stop the bombs, not to chase the bombers home. Half of each squadron goes to meet the raid on its observed track, where it can get to first; the other half climbs over the city the raid is making for, and waits for it there
        Formation? near = null; double nd = 1e12;
        foreach (var fm in air.Formations)
        {
            if (fm.Wolf == f.Wolf || fm.Phase is Phase.Waiting or Phase.Landed || fm.Lead.Z > f.P.Ceil + 40) continue;
            if (Dist(fm.Lead.X, fm.Lead.Y, f.HomeX, f.HomeY) > (f.Wolf ? 330000 : WarnRange * 1.4)) continue;
            double d = Dist(f.X, f.Y, fm.Lead.X, fm.Lead.Y) + (fm.Phase == Phase.Outbound ? 0 : 150000);      // one on its way home only if nothing is coming in
            if (fm == f.W?.Target && fm.Phase == Phase.Outbound) d -= 60000;      // the box the squadron was given: squadrons spread over the stream instead of all meeting the nearest box
            if (d < nd) { nd = d; near = fm; }
        }
        if (near != null)
        {
            var l = near.Lead; double vx = Math.Cos(l.Th) * l.V, vy = Math.Sin(l.Th) * l.V, noise = f.Wolf ? 1500 : 4000;      // radar knows the track well; observers less so
            // lost in cloud: for a minute or so this pilot cannot be told where the raid is, and flies on looking for it
            var skyThere = air.Wx(l.X, l.Y);
            if (skyThere.cloud > .72 && N.Hash(f.Id, (int)(T / 80), 15) < (skyThere.cloud - .6) * (f.Wolf ? .5 : 1) + skyThere.rain * .3) { f.GoX = f.X + Math.Cos(f.Th) * 4000; f.GoY = f.Y + Math.Sin(f.Th) * 4000; f.GoZ = f.Z; Set(f, Act.Hunt, "lost them in the cloud: searching"); return; }
            // the usual way: straight at the bombers, on a collision course damped so it does not swing. The wait-on-their-track method stays for the setting that stage 3 of the roadmap will give each group
            if (f.Mode == InterceptMode.Direct)
            {
                double dist = Dist(f.X, f.Y, l.X, l.Y); var (cx, cy) = LeadPoint(f, l.X, l.Y, vx, vy); double plot = dist * (f.Wolf ? .02 : .04) * (1 + 2 * skyThere.cloud);
                Steer(f, l.X + (cx - l.X) * .6 + (N.Hash(f.Id, 1, 13) - .5) * plot, l.Y + (cy - l.Y) * .6 + (N.Hash(f.Id, 1, 14) - .5) * plot, Math.Min(l.Z + 300, f.P.Ceil));
                Set(f, Act.Hunt, $"going straight for box {near.Name}, {dist / 1000:0} km"); f.IdleSince = T; return;
            }
            var aim = near.Phase == Phase.Outbound ? Making(near, !near.Wolf) : null;
            // close in, the UCS controller puts it straight onto the box from the observers' plots: at night an I-8 pilot sees a bomber at under 3 km, and without this flew to a guessed point and sat beside a raid it could not see. The Hunds do not need it: they see three times as far at night and follow the radar track already (vectoring them as well cost the TB-6s a third fewer losses)
            double close = f.Wolf ? 0 : 25000, cd = Dist(f.X, f.Y, l.X, l.Y);
            if (cd < close)
            {
                // part of the full lead, not all of it: aimed at the whole intercept point the fighter swung out ahead of the box and back as the solution moved (the designer: "overcorrecting"). The plotting error is the pilot's own and steady, shrinking as it closes
                var (ix, iy) = LeadPoint(f, l.X, l.Y, vx, vy); double err = cd * .04;
                Steer(f, l.X + (ix - l.X) * .6 + (N.Hash(f.Id, 1, 13) - .5) * err, l.Y + (iy - l.Y) * .6 + (N.Hash(f.Id, 1, 14) - .5) * err, Math.Min(l.Z + 150, f.P.Ceil));
                Set(f, Act.Hunt, $"vectored onto box {near.Name}, {cd / 1000:0} km"); f.IdleSince = T; return;
            }
            int idx = f.W?.Pilots.IndexOf(f) ?? f.Id; bool forward = aim == null || idx < (f.W?.Pilots.Count ?? 2) / 2;
            double gz = Math.Min(l.Z + 300, f.P.Ceil), reach = f.Wolf ? 190000 : 170000;      // forward, but over our own side: a fighter that meets a raid over the enemy's coast is home again, hit or dry, by the time the bombs fall
            // the first point on the bomber's track, within reach of this fighter's field, that it can get to before the bomber does, climbing on the way
            double tx = double.NaN, ty = 0;
            if (forward)
                for (double t = 60; t <= 3600; t += 30)
                {
                    double px = l.X + vx * t, py = l.Y + vy * t; if (Dist(px, py, f.HomeX, f.HomeY) > reach) continue;
                    double flyT = Math.Max(Dist(f.X, f.Y, px, py) / (f.P.VCruise * .95), Math.Max(0, gz - f.Z) / (f.P.Climb * .7));
                    if (flyT <= t) { tx = px; ty = py; break; }
                }
            if (forward && double.IsNaN(tx) && aim == null) { tx = l.X + vx * 600; ty = l.Y + vy * 600; }      // nothing better known: towards it
            if (!double.IsNaN(tx))
            {
                Steer(f, tx + (N.Hash(f.Id, 1, 11) - .5) * noise, ty + (N.Hash(f.Id, 1, 12) - .5) * noise, gz);
                Set(f, Act.Hunt, $"raid reported {Dist(f.X, f.Y, l.X, l.Y) / 1000:0} km away: going to meet it on its track"); f.IdleSince = T; return;
            }
            // in front of the city, out on the raid's track before the bomb run starts, at its height: from there the controller sends it at the bombers as soon as they are in range
            double back = Math.Max(0, Math.Min(40000, ((aim!.X - l.X) * vx + (aim.Y - l.Y) * vy) / l.V - close * .6)), ax = aim.X - vx / l.V * back, ay = aim.Y - vy / l.V * back, ang = T / 50 + f.Id;
            Steer(f, ax + Math.Cos(ang) * 1500, ay + Math.Sin(ang) * 1500, gz);
            Set(f, Act.Hunt, $"raid making for {aim.Name}: waiting {back / 1000:0} km out on its track"); f.IdleSince = T; return;
        }
        if (f.OnStation && air.Formations.Any(q => q.Wolf != f.Wolf && q.Phase is not (Phase.Waiting or Phase.Landed))) { Set(f, Act.Patrol, "on patrol: nothing in reach yet"); f.IdleSince = T; return; }
        if (T - f.IdleSince > 240) { GoHome(f, "no raid in reach"); return; }
        Set(f, Act.Patrol, "patrolling over the field");
    }
    void Set(Fighter f, Act a, string why) { f.Act = a; f.Why = why; }
    /// <summary>Where a hunting fighter is to fly, eased towards the new answer rather than jumping to it, so that a moving solution does not swing it to and fro.</summary>
    void Steer(Fighter f, double x, double y, double z)
    {
        if (f.Act == Act.Hunt && Dist(x, y, f.GoX, f.GoY) < 30000) { f.GoX += (x - f.GoX) * .35; f.GoY += (y - f.GoY) * .35; } else { f.GoX = x; f.GoY = y; }
        f.GoZ = z;
    }
    /// <summary>The city a raid seems to be making for: of the defenders' cities, the one its track passes closest to, ahead of it and within reach.</summary>
    Place? Making(Formation fm, bool wolfCities)
    {
        var l = fm.Lead; double hx = Math.Cos(l.Th), hy = Math.Sin(l.Th); Place? best = null; double bs = 1e18;
        foreach (var c in w.Cities)
        {
            if (c.Wolf != wolfCities) continue; double ax = c.X - l.X, ay = c.Y - l.Y, along = ax * hx + ay * hy, across = Math.Abs(ax * hy - ay * hx);
            if (along < 0 || along > 450000) continue; double s = across + along * .05;
            if (across < 30000 + c.R && s < bs) { bs = s; best = c; }
        }
        return best;
    }
    /// <summary>Straight on and up, trading nothing away: a fast fighter gets its height and speed back rather than turn with anyone.</summary>
    void ZoomAway(Fighter f, string why) { f.Act = Act.Zoom; f.Until = T + (f.Wolf ? 4 : 7) + rng.NextDouble() * (f.Wolf ? 3 : 5); f.GoZ = f.Z + 500; f.Why = why; f.Stage = -1; }
    /// <summary>The attack that suits the difference in speed.</summary>
    Pass PassFor(Fighter f, Bomber a)
    {
        if (f.Mood == Mood.Ramming) return Pass.Stern;
        double gain = f.P.VMax - a.V; if (gain >= 45) return Pass.Slash;
        if (!f.Wolf && f.Skill < .55 && N.Hash(f.Id, 4, 81) < .25) return Pass.Stern;      // the green just point at it
        double along = (f.X - a.X) * Math.Cos(a.Th) + (f.Y - a.Y) * Math.Sin(a.Th);
        return gain < 45 && along > 700 ? Pass.HeadOn : Pass.Stern;      // anywhere in front of it: wait and meet it nose to nose
    }
    public readonly Dictionary<string, int> LeftBecause = new();
    void GoHome(Fighter f, string why) { if (f.Act != Act.Home) { string k = Sprites.Specs[f.Kind].Name.Split(' ')[0] + ": " + why; LeftBecause.TryGetValue(k, out int n); LeftBecause[k] = n + 1; } f.Act = Act.Home; f.Why = why; f.TB = null; f.TF = null; }

    /// <summary>Is an enemy fighter behind me, close, and pointing at me?</summary>
    bool Tailed(Fighter f, out Fighter? by)
    {
        by = null; double bd = 750;
        foreach (var e in All)
        {
            if (e.Wolf == f.Wolf || !e.Airborne) continue;
            double d = Dist(f.X, f.Y, e.X, e.Y); if (d > bd || Math.Abs(e.Z - f.Z) > 300) continue;
            double toE = Math.Atan2(e.Y - f.Y, e.X - f.X), behind = Math.Abs(Wrap(toE - f.Th)), aim = Math.Abs(Wrap(Math.Atan2(f.Y - e.Y, f.X - e.X) - e.Th));
            if (behind > 1.9 && aim < .6) { bd = d; by = e; }
        }
        return by != null;
    }

    void Move(Fighter f)
    {
        var p = f.P;
        switch (f.Act)
        {
            case Act.Climb:
            {
                if (f.Charge != null) { var c = f.Charge.Lead; Fly(f, c.X + Math.Cos(c.Th) * 3000, c.Y + Math.Sin(c.Th) * 3000, c.Z + 700, 1); }
                else
                {
                    // scrambled: climb towards where the raid will be, well ahead of it, so the squadron meets it nose to nose at height rather than chasing it
                    Formation? near = null; double nd = 1e12;
                    foreach (var fm in air.Formations) { if (fm.Wolf == f.Wolf || fm.Phase is not (Phase.Outbound or Phase.Egress or Phase.Returning)) continue; double dd = Dist(fm.Lead.X, fm.Lead.Y, f.X, f.Y); if (dd < nd) { nd = dd; near = fm; } }
                    if (near != null && nd < 330000) { double ahead = Math.Min(400, nd / 250); Fly(f, near.Lead.X + Math.Cos(near.Lead.Th) * near.Lead.V * ahead, near.Lead.Y + Math.Sin(near.Lead.Th) * near.Lead.V * ahead, Math.Min(near.TargetAlt + 300, f.P.Ceil), 1); }
                    else Fly(f, f.X + Math.Cos(f.Th) * 3000, f.Y + Math.Sin(f.Th) * 3000, 5000, 1);
                }
                break;
            }
            case Act.Hunt: Fly(f, f.GoX, f.GoY, f.GoZ, .95); break;
            case Act.Form:
            {
                // a loose line abreast on the leader, a couple of hundred metres apart; the leader flies to where the squadron is to form
                var w = f.W!; var l = w.Lead;
                if (l == null || l == f || !l.Airborne || l.Act != Act.Form) { Fly(f, w.GoX, w.GoY, w.GoZ, w.EndRun || w.Phase == WPhase.Reform ? 1 : .95); break; }      // an end run is flown flat out
                int i = f.Slot; double lat = (i % 2 == 0 ? 1 : -1) * ((i + 1) / 2) * 230, lag = ((i + 1) / 2) * 70, fx = Math.Cos(l.Th), fy = Math.Sin(l.Th);
                double sx = l.X - fx * lag + fy * lat, sy = l.Y - fy * lag - fx * lat, dd = Dist(f.X, f.Y, sx, sy), ahead = (sx - f.X) * Math.Cos(f.Th) + (sy - f.Y) * Math.Sin(f.Th);
                double want = Math.Clamp(l.V + ahead * .08, p.VStall * 1.2, p.VMax);
                Fly(f, sx + fx * 500, sy + fy * 500, l.Z, Math.Min(1, (want / p.VMax) * (want / p.VMax) + (dd > 900 ? .3 : 0)));
                break;
            }
            case Act.Stalk: Fly(f, f.GoX, f.GoY, f.GoZ, .9); break;
            case Act.Patrol: if (f.OnStation) Fly(f, f.StationX + Math.Cos(T / 90 + f.Id) * 9000, f.StationY + Math.Sin(T / 90 + f.Id) * 9000, f.StationZ, Cruising(f)); else Fly(f, f.HomeX + Math.Cos(T / 60 + f.Id) * 5000, f.HomeY + Math.Sin(T / 60 + f.Id) * 5000, 5000, Cruising(f)); break;
            case Act.Cover:
            {
                // above the box and weaving round it, because a fighter cannot fly as slowly as a bomber in comfort
                var c = f.Charge!.Lead; double fx = Math.Cos(c.Th), fy = Math.Sin(c.Th), wob = Math.Sin(T / 18 + f.Id * 1.3) * 500, ahead = f.SlotA + 900;
                if (f.Wolf && f.Role == Role.Sweep) { ahead = 12000 + Math.Sin(T / 90 + f.Id) * 3000; wob = Math.Sin(T / 70 + f.Id * .7) * 5000; }      // the sweep: 9-15 km out in front and ranging 5 km either side, to meet the interceptors before they reach the box
                double sx = c.X + fx * ahead + fy * (f.SlotB + wob), sy = c.Y + fy * ahead - fx * (f.SlotB + wob), d = Dist(f.X, f.Y, sx, sy);
                double want = Math.Clamp(c.V + d * .03, f.Wolf ? p.VCruise * .92 : p.VStall * 1.3, p.VMax), th = (want / p.VMax) * (want / p.VMax);      // the Hund keeps its speed up and its height in hand, ready to come down
                Fly(f, sx, sy, Math.Min(c.Z + (f.Wolf ? 1200 : 400), p.Ceil), th);
                break;
            }
            case Act.Follow:
            {
                var l = f.Leader; if (l == null || !l.Airborne) { f.NextThink = T; Fly(f, f.X + Math.Cos(f.Th) * 900, f.Y + Math.Sin(f.Th) * 900, f.Z, Cruising(f)); break; }
                double fx = Math.Cos(l.Th), fy = Math.Sin(l.Th), sx = l.X - fx * 90 + fy * 110, sy = l.Y - fy * 90 - fx * 110, d = Dist(f.X, f.Y, sx, sy), ahead = (sx - f.X) * Math.Cos(f.Th) + (sy - f.Y) * Math.Sin(f.Th);
                double want = Math.Clamp(l.V + ahead * .12, p.VStall * 1.2, p.VMax);
                Fly(f, sx + fx * 400, sy + fy * 400, l.Z, (want / p.VMax) * (want / p.VMax) + (d > 600 ? .3 : 0));
                break;
            }
            case Act.AttackBomber:
            {
                var a = f.TB; if (a == null || !a.Alive) { f.NextThink = T; f.TB = null; Fly(f, f.X + Math.Cos(f.Th) * 900, f.Y + Math.Sin(f.Th) * 900, f.Z, 1); break; }
                double tvx = Math.Cos(a.Th) * a.V, tvy = Math.Sin(a.Th) * a.V; var (lx, ly) = LeadPoint(f, a.X, a.Y, tvx, tvy); double d = Dist(f.X, f.Y, a.X, a.Y);
                double hx = Math.Cos(a.Th), hy = Math.Sin(a.Th), along = (f.X - a.X) * hx + (f.Y - a.Y) * hy, off = (f.Id % 2 == 0 ? 1 : -1) * (500 + N.Hash(f.Id, 3, 80) * 1500);
                if (f.Pass == Pass.HeadOn && along < 300)
                {
                    if (f.P.VMax - a.V >= 30) f.Pass = Pass.Stern;      // it has gone by: after it
                    else { f.LastPass = T; f.Passes++; f.Side = f.Id % 2 == 0 ? 1 : -1; f.Act = Act.Extend; f.Stage = -1; f.Until = T + 4 + rng.NextDouble() * 4; f.Why = "through the box: round for the next one"; f.GoZ = a.Z + 200; f.NextThink = f.Until; break; }
                }
                if (f.Stage == 0 && f.Pass == Pass.Slash)
                {
                    // height first, off to one side; then down through the box at full speed
                    if ((f.Z > a.Z + 450 && d < 3800) || a.Z + 450 > f.P.Ceil) f.Stage = 1;
                    else { Fly(f, a.X + hx * 1500 + hy * off, a.Y + hy * 1500 - hx * off, Math.Min(a.Z + 900, f.P.Ceil), 1); break; }
                }
                // each pilot comes in on a line of their own, spreading out until close in
                double spread = f.Pass == Pass.Stern ? Math.Clamp((d - 700) / 3000, 0, 1) * 900 : 0, sa = N.Hash(f.Id, 0, 77) * Math.Tau;
                // and from above or below: nobody attacks a turret on the level if they can help it
                double high = f.Pass == Pass.HeadOn ? 25 : f.Pass == Pass.Slash ? 0 : (f.Skill > .85 || N.Hash(f.Id, 1, 78) < .55 ? 1 : -1) * (35 + 55 * N.Hash(f.Id, 2, 79));      // experts come from above and astern, where a V-16's tail blinds its top turrets
                Fly(f, lx + Math.Cos(sa) * spread, ly + Math.Sin(sa) * spread, a.Z + high, 1);
                Fire(f, a.H, a.X, a.Y, a.Z, lx, ly, tvx, tvy, (T < a.BreakUntil ? (a.H.D.Dash && a.Kg > 0 ? .8 : a.H.D.Dash ? .35 : .65) : 1) * (1.5 + Sprites.Specs[a.F.Kind].Span / 40 * 1.6) * (1 - a.H.D.DodgeNow(a.F.Company) * (1 - .9 * Math.Min(1, a.Kg / Math.Max(1, a.H.D.MaxKg))) * Math.Min(1, a.H.Power)), false, (gun, h) => { air.Hit(a, gun, h, f.Pass == Pass.HeadOn ? 2.5 : 1); if (!a.Alive) { f.KillsB++; BombersDownByFighters++; f.Skill = Math.Min(1.3, f.Skill + .05); Feel(f, .1); if (f.W != null) f.W.Kills++; } });
                if (f.Mood == Mood.Ramming && d < 40 && Math.Abs(f.Z - a.Z) < 60)
                {
                    Rams++; air.Ram(a); f.KillsB += a.Alive ? 0 : 1; if (!a.Alive) BombersDownByFighters++; f.Why = "rammed a bomber"; Kill(f, null); break;
                }
                // a slashing pass: through, away, and come round again. Nobody sits in a bomber's gunsights for long, unless they mean to die there
                if (f.Mood != Mood.Ramming && d < 130 + 170 * (1.2 - f.Aggression))
                {
                    f.LastPass = T; f.Passes++;
                    if (f.Wolf || f.Pass == Pass.Slash) ZoomAway(f, "through the box and up");
                    else { f.Side = rng.NextDouble() < .5 ? 1 : -1; f.Act = Act.Extend; f.Stage = -1; f.Until = T + 6 + rng.NextDouble() * 7; f.Why = "breaking off after a pass"; f.GoZ = f.Z - 250; }
                }
                break;
            }
            case Act.Fight:
            {
                var e = f.TF; if (e == null || !e.Airborne) { f.NextThink = T; f.TF = null; Fly(f, f.X + Math.Cos(f.Th) * 900, f.Y + Math.Sin(f.Th) * 900, f.Z, 1); break; }
                double tvx = Math.Cos(e.Th) * e.V, tvy = Math.Sin(e.Th) * e.V; var (lx, ly) = LeadPoint(f, e.X, e.Y, tvx, tvy); double d = Dist(f.X, f.Y, e.X, e.Y);
                if (f.Wolf)
                {
                    // boom and zoom: never turn with a slower, tighter aeroplane. Short of speed, or the target has turned inside: straight on and up, then come down again
                    double offNose = Math.Abs(Wrap(Math.Atan2(ly - f.Y, lx - f.X) - f.Th));
                    if (f.V < p.VCruise * .85) { ZoomAway(f, "short of speed: up and away to get it back"); break; }
                    if (d < 900 && offNose > .9) { ZoomAway(f, "it would have to be a turning fight: not with me"); break; }
                    if (d > 1400 && f.Z < e.Z + 300) { Fly(f, lx, ly, e.Z + 700, 1); break; }      // height before anything else
                    Fly(f, lx, ly, e.Z, 1);
                    Fire(f, e.H, e.X, e.Y, e.Z, lx, ly, tvx, tvy, .3, e.Act == Act.Evade, (gun, h) => HitFighter(e, gun, h, f));
                    if (d < 220) ZoomAway(f, "through and up");
                    break;
                }
                if (d > 900) { double sa = (N.Hash(f.Id, 6, 83) - .5) * 2.4, ox = Math.Cos(e.Th + Math.PI / 2) * sa * 600, oy = Math.Sin(e.Th + Math.PI / 2) * sa * 600; Fly(f, lx + ox, ly + oy, e.Z + 120, 1); }      // spread out round it, so whichever way it turns someone is there
                else Fly(f, lx, ly, e.Z, 1);
                Fire(f, e.H, e.X, e.Y, e.Z, lx, ly, tvx, tvy, .3, e.Act == Act.Evade, (gun, h) => HitFighter(e, gun, h, f));
                if (d < 55) { f.Act = Act.Extend; f.Until = T + 3; f.Why = "overshot, coming round"; f.GoZ = f.Z; }
                break;
            }
            case Act.Evade:
            {
                // a Hund does not out-turn anything: it opens the throttle and goes, shallow and fast. An I-8 turns inside whatever is behind it
                if (f.Wolf) { Fly(f, f.GoX, f.GoY, f.GoZ, 1); break; }
                double dir = f.Th + f.Side * 1.5;
                Fly(f, f.X + Math.Cos(dir) * 1500, f.Y + Math.Sin(dir) * 1500, f.Z, 1);
                break;
            }
            case Act.Extend:
            {
                double dir = f.Th + f.Side * .15;
                Fly(f, f.X + Math.Cos(dir) * 2000, f.Y + Math.Sin(dir) * 2000, f.GoZ, 1);
                if (T >= f.Until) f.NextThink = T;
                break;
            }
            case Act.Zoom:
                Fly(f, f.X + Math.Cos(f.Th) * 3000, f.Y + Math.Sin(f.Th) * 3000, f.GoZ, 1);
                if (T >= f.Until) f.NextThink = T;
                break;
            case Act.Home:
                Fly(f, f.HomeX, f.HomeY, 1200, Cruising(f));
                if (Dist(f.X, f.Y, f.HomeX, f.HomeY) < 2500) { f.Act = Act.Landed; f.Until = T + 240; f.Why = "landed at " + f.HomeName; }
                break;
        }
    }

    public Fighter? At(Func<Fighter, Godot.Vector2> screen, Godot.Vector2 p, double reach)
    {
        Fighter? best = null; double bd = reach;
        foreach (var f in All) { if (!f.Airborne) continue; double d = (screen(f) - p).Length(); if (d < bd) { bd = d; best = f; } }
        return best;
    }
}
