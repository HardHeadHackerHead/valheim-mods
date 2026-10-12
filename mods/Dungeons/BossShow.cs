using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static DHack.Shared.Translations;
using Object = UnityEngine.Object;

namespace Dungeons
{
    /// <summary>
    /// A boss fight as every game sees and hears it, from its record (BossFight.cs) and the server's clock, every frame: the intro's and
    /// the death's beats (sounds, effects, the camera's shake, the banner with the boss's name and title, messages), each phase's
    /// moment, the shield coming up and breaking, a weak hit landing, the telegraphs of what it's casting and where it will land, and the
    /// boss's own look and moves (BossBody). Each game hurts only its own player when a blow lands (a slam's ring, a charge's lane, a leap's
    /// landing, a weakTo hammer): the same rule as traps, the damage fitted to the land. The first look at a fight (a player arriving,
    /// a relog, a reload) plays nothing older than a moment.
    /// </summary>
    internal partial class MechSet
    {
        private class BossView
        {
            public bool Seen;
            public double IntroT, DeathT;
            public int Phase, Shield;
            public double BrokeAt, HitAt, LeapAt, CastAt, FlickerAt, HealAt, ClampUntil;
            public int Cast = -1;
            public List<Vector3> Pts = new List<Vector3>();
            public bool Landed, Hurt, LeapLanded, Banner;
            public int Laned;
            public readonly List<GameObject> Marks = new List<GameObject>();
            public readonly List<GameObject> Fills = new List<GameObject>();
            public readonly List<GameObject> LeapMarks = new List<GameObject>();
            public readonly List<double> WeakSeen = new List<double>();
            public readonly List<double> WeakFalling = new List<double>();   // each weakTo: the motion its ring was made for
            public readonly List<GameObject> WeakRings = new List<GameObject>();
            public readonly List<GameObject> MarkRings = new List<GameObject>();      // weakTo marks (amber), made once a fight
        }

        private readonly Dictionary<string, BossView> _bossViews = new Dictionary<string, BossView>();
        private float _nextBossFind;

        // the game's sounds and effects the fights use when the pack names none (checked by `dungeon check` through SoundPrefabs)
        public static readonly string[] BossPrefabs =
        {
            "fx_StaffShield_Hit", "fx_StaffShield_Break", "fx_shield_start", "vfx_StaffShield", "vfx_RockDestroyed_large", "sfx_rock_destroyed",
            "vfx_troll_groundslam", "sfx_gdking_stomp", "fx_eikthyr_stomp", "vfx_prespawn", "sfx_prespawn", "sfx_metalbars_break", "guard_stone",
            "sfx_gdking_alert", "sfx_rock_hit", "sfx_gdking_scream", "vfx_HealthUpgrade", "fx_guardstone_activate", "sfx_dverger_heal_finish",
            "fx_frozenking_chain_ground_impact_1", "sfx_frozenking_chainflurry_chain",
        };

        /// <summary>Every frame, every game (Update): each boss fight shown.</summary>
        private void BossShow()
        {
            if (Time.time >= _nextBossFind) { _nextBossFind = Time.time + 1f; BossBody.Find(); }
            ReadBosses();
            foreach (Mech m in All)
            {
                if (m.Def.Encounter?.Boss == null || m.Base == null) continue;
                try { ShowBoss(m); }
                catch (Exception e) { WarnOnce($"Boss {m.Key}: {e.Message}"); }
            }
        }

        private void ShowBoss(Mech m)
        {
            BossDef d = m.Def.Encounter.Boss;
            if (!_bossViews.TryGetValue(m.Key, out BossView v)) _bossViews[m.Key] = v = new BossView();
            EncounterRecord er = EncounterOf(m);
            _bosses.TryGetValue(m.Key, out BossRecord b);
            double now = MechClock.Now;
            bool fighting = er.State == EncounterRecord.Fighting && b != null;
            bool dying = b != null && b.DeadAt > 0.0 && er.State != EncounterRecord.Idle && now - b.DeadAt < (d.Death?.Seconds ?? 0f) + 6f;
            if (!fighting && !dying)
            {
                if (v.Seen)
                {
                    BossMarks.Clear(v.Marks);
                    BossMarks.Clear(v.Fills);
                    BossMarks.Clear(v.LeapMarks);
                    BossMarks.Clear(v.WeakRings);
                    BossMarks.Clear(v.MarkRings);
                    _bossViews[m.Key] = new BossView();
                }
                return;
            }
            Player p = Player.m_localPlayer;
            bool here = p != null && !p.IsDead() && InArea(m, p.transform.position);
            string tag = TagOf(m);
            // this fight's boss (one left from an earlier fight isn't spoken for: an ordinary creature until the owner takes it away)
            long stamp = Stamp(b);
            BossBody body = BossBody.All.FirstOrDefault(x => x.C != null && x.Tag == tag && !x.C.IsDead() && (x.Fight == stamp || x.Fight == 0L && b.Start < now - 900.0));
            Vector3 bossAt = body != null ? body.C.transform.position : RoomPoint(m, m.Def.Encounter.Points[0]);
            bool first = !v.Seen;
            if (first)
            {
                v.Seen = true;
                // (a fight seen from its start, or a fall seen as it happens, plays from its first beat: the record and the fight's state
                // reach every game together)
                v.IntroT = now - b.Start < 1.5 ? -0.001 : now - b.Start;
                v.DeathT = b.DeadAt > 0.0 && now - b.DeadAt >= 1.5 ? now - b.DeadAt : -1.0;
                v.Phase = b.Phase;
                v.Shield = b.Shield;
                v.BrokeAt = b.BrokeAt;
                v.FlickerAt = b.FlickerAt;
                v.HealAt = b.HealAt;
                v.ClampUntil = b.ClampUntil;
                v.HitAt = b.HitAt;
                v.LeapAt = b.LeapAt;
                v.LeapLanded = true;
                v.CastAt = b.CastAt;
                v.Cast = -1;
                v.Banner = true;
            }

            // the intro's beats
            if (d.Intro != null)
            {
                double t = now - b.Start;
                foreach (BossBeat beat in d.Intro.Beats)
                    if (beat.At > v.IntroT && beat.At <= t && t - beat.At < 2.0) Beat(m, d, beat, here, bossAt);
                v.IntroT = Math.Max(v.IntroT, t);
            }
            // the death's
            if (b.DeadAt > 0.0)
            {
                double t = now - b.DeadAt;
                if (v.DeathT < 0.0)
                {
                    // the fall itself: everyone inside hears and feels it
                    v.DeathT = 0.0;
                    if (t < 2.0)
                    {
                        PlayAt("vfx_RockDestroyed_large", bossAt);
                        Shake(bossAt, 0.4f);
                        if (d.Death == null || !d.Death.Beats.Any(x => x.Banner)) Banner(d, here, T("dungeons_boss_fallen", Localize(d.Name)));
                    }
                }
                if (d.Death != null)
                    foreach (BossBeat beat in d.Death.Beats)
                        if (beat.At >= v.DeathT && beat.At <= t && t - beat.At < 2.0 && !(beat.At == 0f && v.DeathT > 0.0)) Beat(m, d, beat, here, bossAt, dead: true);
                v.DeathT = Math.Max(v.DeathT, t + 1e-4);
            }

            // each phase's moment
            if (v.Phase != b.Phase)
            {
                if (now - er.Time < 3.0)
                    for (int i = Math.Max(0, v.Phase); i < b.Phase && i < d.Phases.Count; i++)
                    {
                        BossPhase ph = d.Phases[i];
                        if (!string.IsNullOrEmpty(ph.Sound)) PlayAt(ph.Sound, bossAt);
                        if (ph.Shake > 0f) Shake(bossAt, ph.Shake);
                        if (here && !string.IsNullOrEmpty(ph.Banner)) MessageHud.instance?.ShowBiomeFoundMsg(Localize(ph.Banner), false);
                        if (here && !string.IsNullOrEmpty(ph.Message)) p.Message(MessageHud.MessageType.Center, Localize(ph.Message, Localize(d.Name)));
                        if (here && ph.Enrage > 1f && string.IsNullOrEmpty(ph.Message)) p.Message(MessageHud.MessageType.Center, T("dungeons_boss_enraged", Localize(d.Name)));
                    }
                v.Phase = b.Phase;
            }

            // the shield
            if (v.Shield != b.Shield)
            {
                if (b.Shield >= 0 && b.Shield < d.Phases.Count && now - b.ShieldAt < 3.0)
                {
                    PlayAt("fx_shield_start", bossAt);
                    string hint = d.Phases[b.Shield].Shield?.Hint;
                    if (here) p.Message(MessageHud.MessageType.Center, string.IsNullOrEmpty(hint) ? T("dungeons_boss_shielded", Localize(d.Name)) : Localize(hint, Localize(d.Name)));
                }
                v.Shield = b.Shield;
            }
            if (v.BrokeAt != b.BrokeAt)
            {
                if (now - b.BrokeAt < 3.0)
                {
                    PlayAt("fx_StaffShield_Break", bossAt);
                    PlayAt("sfx_metalbars_break", bossAt);
                    Shake(bossAt, 0.4f);
                    if (here) p.Message(MessageHud.MessageType.Center, T("dungeons_boss_shield_broken", Localize(d.Name)));
                }
                v.BrokeAt = b.BrokeAt;
            }
            // the shield flickering (its conditions held, not for the last time)
            if (v.FlickerAt != b.FlickerAt)
            {
                if (now - b.FlickerAt < 3.0)
                {
                    PlayAt("fx_StaffShield_Hit", bossAt);
                    PlayAt("sfx_gdking_scream", bossAt);
                    if (here) p.Message(MessageHud.MessageType.Center, T("dungeons_boss_shield_flicker", Localize(d.Name)));
                }
                v.FlickerAt = b.FlickerAt;
            }
            // a second wind: a surge of light and sound as it recovers
            if (v.HealAt != b.HealAt)
            {
                if (now - b.HealAt < 3.0)
                {
                    PlayAt("vfx_HealthUpgrade", bossAt);
                    PlayAt("fx_guardstone_activate", bossAt);
                    PlayAt("sfx_dverger_heal_finish", bossAt);
                    Shake(bossAt, 0.3f);
                    if (body != null) body.Surge = Time.time + 2.5f;
                    if (here) p.Message(MessageHud.MessageType.TopLeft, T("dungeons_boss_recovers", Localize(d.Name)));
                }
                v.HealAt = b.HealAt;
            }
            // held by a weakTo mechanism coming down on it: chains snap round it
            if (v.ClampUntil != b.ClampUntil)
            {
                if (now < b.ClampUntil)
                {
                    PlayAt("fx_frozenking_chain_ground_impact_1", bossAt);
                    PlayAt("sfx_frozenking_chainflurry_chain", bossAt);
                    if (here) p.Message(MessageHud.MessageType.TopLeft, T("dungeons_boss_held", Localize(d.Name)));
                }
                v.ClampUntil = b.ClampUntil;
            }
            // a weak hit
            if (v.HitAt != b.HitAt)
            {
                if (now - b.HitAt < 3.0)
                {
                    PlayAt("vfx_RockDestroyed_large", bossAt);
                    PlayAt("sfx_rock_destroyed", bossAt);
                    if (here) p.Message(MessageHud.MessageType.TopLeft, T("dungeons_boss_staggered", Localize(d.Name)));
                }
                v.HitAt = b.HitAt;
            }

            ShowCast(m, d, b, v, p, here, body, now);
            ShowLeap(m, b, v, p, body, now);
            ShowWeak(m, d, v, p, now, first);

            // the boss itself: what the record says about it now
            if (body != null)
            {
                bool introNow = d.Intro != null && now - b.Start < d.Intro.Seconds;
                BossAbility casting = b.Cast >= 0 && b.Cast < d.Abilities.Count ? d.Abilities[b.Cast] : null;
                BossShield shield = b.Shield >= 0 && b.Shield < d.Phases.Count ? d.Phases[b.Shield].Shield : null;
                body.Look = d.Look;
                body.Said = Time.time;
                body.Shielded = shield != null && shield.Take <= 0f;
                body.Held = introNow || b.DeadAt > 0.0 || Leaping(b, now) || (shield != null && shield.Hold) || now < b.ClampUntil
                            || (casting != null && (casting.Kind == "slam" || casting.Kind == "charge") && now < b.CastAt + casting.Warn + 0.3);
                body.Guard = introNow ? 0f : shield != null ? shield.Take : now < b.ArmourOff ? 1f : Mathf.Clamp(d.Armour, 0.05f, 1f);
                body.Enrage = b.Enrage;
                body.Enraged = b.Enrage > 1f;
                body.LeapAt = b.LeapAt;
                body.LeapFrom = b.LeapFrom;
                body.LeapTo = b.LeapTo;
                body.LeapDur = b.LeapDur;
                body.LeapArc = b.LeapArc;
                body.Show();
                // weakTo mechanisms: it passes through them while they move or while it's under one that's down (thrown out after a hit)
                foreach (BossWeak w in d.WeakTo)
                {
                    Mech wm = Sibling(m, w.Mech);
                    if (wm?.Base == null) continue;
                    MechMotion wmo = Motion(wm, now);
                    body.PassThrough(wm.Colliders, wmo.Moving(now) || (wmo.To == w.State && InWeakArea(wm, w, body.C.transform.position)));
                }
            }
        }

        // ---- casts: telegraphs, then the blow

        private void ShowCast(Mech m, BossDef d, BossRecord b, BossView v, Player p, bool here, BossBody body, double now)
        {
            if (b.Cast >= 0 && b.CastAt != v.CastAt && b.Cast < d.Abilities.Count)
            {
                BossMarks.Clear(v.Marks);
                BossMarks.Clear(v.Fills);
                v.CastAt = b.CastAt;
                v.Cast = b.Cast;
                v.Pts = new List<Vector3>(b.CastPts);
                v.Landed = v.Hurt = false;
                v.Laned = 0;
                BossAbility a = d.Abilities[b.Cast];
                // every telegraph has its sound as well as its marks
                string cue = a.Cue ?? (a.Kind == "slam" || a.Kind == "charge" ? "sfx_gdking_alert" : a.Kind == "rain" ? "sfx_rock_hit"
                                       : a.Kind == "summon" ? "sfx_prespawn" : "");
                if (now < b.CastAt + a.Warn && !string.IsNullOrEmpty(cue))
                    foreach (Vector3 at in a.Kind == "rain" || a.Kind == "summon" ? v.Pts : v.Pts.Take(1)) PlayAt(cue, at);
                if (now < b.CastAt + a.Warn && _root != null)
                    switch (a.Kind)
                    {
                        case "slam":
                        case "rain":
                            foreach (Vector3 at in v.Pts)
                            {
                                v.Marks.Add(BossMarks.Ring(_root.transform, at, a.Radius));
                                v.Fills.Add(BossMarks.Ring(_root.transform, at, 0.2f));
                            }
                            break;
                        case "charge":
                            if (v.Pts.Count >= 2) v.Marks.Add(BossMarks.Lane(_root.transform, v.Pts[0], v.Pts[1], a.Width));
                            break;
                        case "summon":
                            foreach (Vector3 at in v.Pts) PlayAt("vfx_prespawn", at);
                            break;
                    }
            }
            if (v.Cast < 0 || v.Cast >= d.Abilities.Count) return;
            BossAbility c = d.Abilities[v.Cast];
            double land = v.CastAt + c.Warn;
            if (now < land)
            {
                float k = (float)((now - v.CastAt) / Math.Max(0.1f, c.Warn));
                foreach (GameObject f in v.Fills) BossMarks.Grow(f, c.Radius * k);
                if (c.Kind == "charge" && v.Pts.Count >= 2 && v.Marks.Count > 0) BossMarks.FillLane(v.Marks[0], v.Pts[0], v.Pts[1], k, ref v.Laned);
                return;
            }
            if (!v.Landed)
            {
                v.Landed = true;
                BossMarks.Clear(v.Marks);
                BossMarks.Clear(v.Fills);
                if (now - land < 1.0)
                    switch (c.Kind)
                    {
                        case "slam":
                        case "rain":
                            foreach (Vector3 at in v.Pts)
                            {
                                PlayAt(c.Fx ?? (c.Kind == "slam" ? "fx_eikthyr_stomp" : "vfx_RockDestroyed_large"), at);
                                PlayAt(c.Sound ?? (c.Kind == "slam" ? "sfx_gdking_stomp" : "sfx_rock_destroyed"), at);
                                if (p != null && !p.IsDead() && Flat(p.transform.position, at) <= c.Radius && Mathf.Abs(p.transform.position.y - at.y) < 3f)
                                    HurtMe(p, at, c.Damage, c.Scale, c.Push, body?.C, true);
                            }
                            break;
                        case "charge":
                            if (v.Pts.Count >= 1) { PlayAt(c.Sound ?? "sfx_gdking_alert", v.Pts[0]); if (!string.IsNullOrEmpty(c.Fx)) PlayAt(c.Fx, v.Pts[0]); }
                            break;
                        default:
                            if (v.Pts.Count >= 1)
                            {
                                if (!string.IsNullOrEmpty(c.Sound)) PlayAt(c.Sound, v.Pts[0]);
                                if (!string.IsNullOrEmpty(c.Fx)) PlayAt(c.Fx, v.Pts[0]);
                            }
                            break;
                    }
            }
            // a charge: what it runs through on its way
            if (c.Kind == "charge" && !v.Hurt && v.Pts.Count >= 2 && p != null && !p.IsDead() && body?.C != null && now < land + 2.0)
            {
                Vector3 at = body.C.transform.position;
                if (Flat(p.transform.position, at) <= c.Width * 0.5f + 0.6f && Mathf.Abs(p.transform.position.y - at.y) < 2.5f && BossFight_Dashing(b, now))
                {
                    v.Hurt = true;
                    HurtMe(p, at, c.Damage, c.Scale, c.Push, body.C, true);
                }
            }
            if (now > land + 3.0) v.Cast = -1;
        }

        private static bool BossFight_Dashing(BossRecord b, double now) => b.LeapArc <= 0f && Leaping(b, now);

        // ---- leaps: where it will land, and the landing

        private void ShowLeap(Mech m, BossRecord b, BossView v, Player p, BossBody body, double now)
        {
            if (b.LeapAt != v.LeapAt)
            {
                BossMarks.Clear(v.LeapMarks);
                v.LeapAt = b.LeapAt;
                v.LeapLanded = false;
                if (b.LeapArc > 1.5f && now < b.LeapAt + b.LeapDur && _root != null)
                {
                    // where it will land, shown before it jumps, with its roar
                    v.LeapMarks.Add(BossMarks.Ring(_root.transform, b.LeapTo, 3f));
                    v.LeapMarks.Add(BossMarks.Ring(_root.transform, b.LeapTo, 1.2f));
                    PlayAt("sfx_gdking_alert", b.LeapFrom);
                }
            }
            if (v.LeapLanded || now < b.LeapAt + b.LeapDur) return;
            v.LeapLanded = true;
            BossMarks.Clear(v.LeapMarks);
            if (b.LeapArc <= 1.5f || now - (b.LeapAt + b.LeapDur) > 1.0) return;
            // the landing: a burst anyone in the room sees
            PlayAt("vfx_troll_groundslam", b.LeapTo);
            PlayAt("fx_eikthyr_stomp", b.LeapTo);
            PlayAt("vfx_RockDestroyed_large", b.LeapTo);
            PlayAt("sfx_gdking_stomp", b.LeapTo);
            if (b.LeapArc >= 4.5f) Shake(b.LeapTo, 0.4f);   // (a big leap, one-off; a hop to the players shakes nothing)
            if (p != null && !p.IsDead() && Flat(p.transform.position, b.LeapTo) <= 3f && Mathf.Abs(p.transform.position.y - b.LeapTo.y) < 3f)
                HurtMe(p, b.LeapTo, "blunt", 1.5f, 40f, body?.C, true);
        }

        // ---- weakTo: a hammer coming down (its dust, its shake, and whoever stands under it)

        private void ShowWeak(Mech m, BossDef d, BossView v, Player p, double now, bool first)
        {
            while (v.WeakSeen.Count < d.WeakTo.Count) v.WeakSeen.Add(0.0);
            while (v.WeakFalling.Count < d.WeakTo.Count) { v.WeakFalling.Add(0.0); v.WeakRings.Add(null); }
            if (v.MarkRings.Count == 0 && _root != null)
                foreach (BossWeak w in d.WeakTo.Where(x => x.Mark))
                {
                    Mech wm = Sibling(m, w.Mech);
                    if (wm?.Base == null) continue;
                    // the target, in amber, for the whole fight: lure it here
                    Vector3 c = wm.Base.TransformPoint(new Vector3(w.AreaAt.x, 0f, w.AreaAt.z));
                    float r = Mathf.Max(w.Area.x, w.Area.z) * 0.5f;
                    v.MarkRings.Add(BossMarks.Ring(_root.transform, c, r, BossMarks.Amber));
                    v.MarkRings.Add(BossMarks.Ring(_root.transform, c, r * 0.55f, BossMarks.Amber));
                }
            for (int i = 0; i < d.WeakTo.Count; i++)
            {
                BossWeak w = d.WeakTo[i];
                Mech wm = Sibling(m, w.Mech);
                if (wm?.Base == null) continue;
                MechMotion mo = Motion(wm, now);
                double arrived = mo.Start + mo.Seconds;
                // on its way down: a red ring where it will land, for as long as it falls (the mechanism's own sound goes with it)
                bool falling = mo.To == w.State && mo.Moving(now);
                if (falling && v.WeakFalling[i] != mo.Start && _root != null)
                {
                    v.WeakFalling[i] = mo.Start;
                    if (v.WeakRings[i] != null) Object.Destroy(v.WeakRings[i]);
                    Vector3 c = wm.Base.TransformPoint(new Vector3(w.AreaAt.x, 0f, w.AreaAt.z));
                    v.WeakRings[i] = BossMarks.Ring(_root.transform, c, Mathf.Max(w.Area.x, w.Area.z) * 0.5f);
                }
                else if (!falling && v.WeakRings[i] != null) { Object.Destroy(v.WeakRings[i]); v.WeakRings[i] = null; }
                if (mo.To != w.State || double.IsInfinity(arrived) || now < arrived || arrived <= v.WeakSeen[i] + 0.01) continue;
                v.WeakSeen[i] = arrived;
                if (first || now - arrived > 0.6) continue;
                Vector3 at = wm.Base.TransformPoint(w.AreaAt);
                PlayAt("vfx_RockDestroyed_large", at);
                PlayAt("sfx_gdking_stomp", at);
                if (w.Hurt > 0f && p != null && !p.IsDead() && InWeakArea(wm, w, p.transform.position + Vector3.up * 0.5f))
                    HurtMe(p, at, "blunt", w.Hurt, 30f, null, false);
            }
        }

        // ---- every game: moments

        private void Beat(Mech m, BossDef d, BossBeat beat, bool here, Vector3 bossAt, bool dead = false)
        {
            Vector3 at = beat.Where.HasValue ? RoomPoint(m, beat.Where.Value) : bossAt;
            if (!string.IsNullOrEmpty(beat.Sound)) PlayAt(beat.Sound, at);
            if (!string.IsNullOrEmpty(beat.Fx)) PlayAt(beat.Fx, at);
            if (beat.Shake > 0f) Shake(at, beat.Shake);
            Player p = Player.m_localPlayer;
            bool inside = p != null && Site.Contains(p.transform.position);
            if (beat.Banner && (here || dead && inside))
                Banner(d, true, dead ? T("dungeons_boss_fallen", Localize(d.Name)) : null);
            if (!string.IsNullOrEmpty(beat.Message) && (here || dead && inside))
            {
                // "the doors slam shut" only when the fight's start shut a lock that was open (MechEncounter's StartLine)
                string line = beat.Message == "$dungeons_fight_start" ? StartLine(m, EncounterOf(m)) : Localize(beat.Message, Localize(d.Name));
                if (!string.IsNullOrEmpty(line)) p?.Message(MessageHud.MessageType.Center, line);
            }
        }

        /// <summary>The game's big banner (as for a land found): the boss's name and title, or a line of text.</summary>
        private static void Banner(BossDef d, bool show, string text)
        {
            if (!show || MessageHud.instance == null) return;
            if (text == null)
            {
                text = Localize(d.Name);
                if (!string.IsNullOrEmpty(d.Title)) text += "\n<size=55%>" + Localize(d.Title) + "</size>";
            }
            MessageHud.instance.ShowBiomeFoundMsg(text, !d.Mini);
        }

        private static float _shakeUntil;

        /// <summary>The camera's shake, for big one-off moments only (an intro's landing, a shield breaking, a death; a pack's beat or
        /// phase "shake"): at most 0.4 (the game's shake lasts its strength in seconds), never while another of ours goes on, times the
        /// player's "Screen shake" setting (0: none). Repeating things (each slam, stone, hammer, spawn) never shake.</summary>
        private static void Shake(Vector3 at, float strength)
        {
            float k = BossSettings.ScreenShake;
            if (GameCamera.instance == null || strength <= 0f || k <= 0f || Time.time < _shakeUntil) return;
            float s = Mathf.Min(0.4f, strength) * k;
            _shakeUntil = Time.time + s + 0.1f;
            GameCamera.instance.AddShake(at, 40f, s, false);
        }

        private static float Flat(Vector3 a, Vector3 b) => Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));

        private static string Localize(string text, params object[] args)
        {
            if (string.IsNullOrEmpty(text)) return "";
            string s = Localization.instance != null ? Localization.instance.Localize(text) : text;
            if (args.Length == 0 || !s.Contains("{0}")) return s;
            try { return string.Format(s, args); } catch (FormatException) { return s; }
        }

        /// <summary>This game's player hit by a boss's blow: the land's trap damage (Meadows 8 ... Deep North 95) times scale, never more
        /// than a share of their health (a third up to the Black Forest, half to the Mountains, 70% after), pushed away from where it
        /// landed. Dodge-rolling through it avoids it; a block takes it (and a parry staggers the boss).</summary>
        private void HurtMe(Player p, Vector3 from, string type, float scale, float push, Character attacker, bool blockable)
        {
            int tier = WorldGenerator.instance != null ? Builder.TierOf(WorldGenerator.instance.GetBiome(Site.EntrancePos)) : 1;
            float[] table = { 8f, 8f, 15f, 25f, 35f, 50f, 65f, 80f, 95f };
            float damage = table[Mathf.Clamp(tier, 1, 8)] * Mathf.Clamp(scale, 0f, 5f);
            float share = tier <= 2 ? 0.34f : tier <= 4 ? 0.5f : 0.7f;
            damage = Mathf.Min(damage, p.GetMaxHealth() * share);
            if (damage <= 0f) return;
            Vector3 dir = Vector3.ProjectOnPlane(p.transform.position - from, Vector3.up);
            dir = (dir.sqrMagnitude > 0.01f ? dir.normalized : p.transform.forward * -1f) + Vector3.up * 0.25f;
            var hit = new HitData
            {
                m_point = p.GetCenterPoint(), m_dir = dir.normalized, m_pushForce = push, m_dodgeable = true, m_blockable = blockable,
                m_hitType = HitData.HitType.EnemyHit,
            };
            switch (type)
            {
                case "slash": hit.m_damage.m_slash = damage; break;
                case "pierce": hit.m_damage.m_pierce = damage; break;
                case "fire": hit.m_damage.m_fire = damage; break;
                case "frost": hit.m_damage.m_frost = damage; break;
                case "lightning": hit.m_damage.m_lightning = damage; break;
                case "poison": hit.m_damage.m_poison = damage; break;
                case "spirit": hit.m_damage.m_spirit = damage; break;
                default: hit.m_damage.m_blunt = damage; break;
            }
            if (attacker != null && !attacker.IsDead()) hit.SetAttacker(attacker);
            p.Damage(hit);
        }

        // ---- MechEncounter's and the music's hooks

        /// <summary>A boss fight's track: none until it wakes (the room's own plays through the intro's build-up), then its music.</summary>
        private string BossMusic(Mech m)
        {
            BossDef d = m.Def.Encounter.Boss;
            ReadBosses();
            if (d.Intro != null && _bosses.TryGetValue(m.Key, out BossRecord b) && MechClock.Now - b.Start < d.Intro.Wake) return null;
            return string.IsNullOrEmpty(m.Def.Encounter.Music) ? DungeonMusic.BossDefault : m.Def.Encounter.Music;
        }

        /// <summary>EncounterNotices for a boss fight's change of state: its start (without an intro: "awakens"; with one the banner says
        /// it), and its clear: the vault moment for everyone inside (the deep "solved", the message, each player's vault roll).</summary>
        private void BossNotices(Mech m, EncounterRecord r, bool here, Player p)
        {
            BossDef boss = m.Def.Encounter.Boss;
            string name = Localize(boss.Name);
            if (r.State == EncounterRecord.Fighting)
            {
                if (here && r.Wave == 0 && boss.Intro == null) p.Message(MessageHud.MessageType.Center, T("dungeons_boss_awakens", name));
                return;
            }
            if (r.State != EncounterRecord.Cleared || p == null || !Site.Contains(p.transform.position)) return;
            PlayAt(VaultSolved, p.transform.position + Vector3.up);
            p.Message(MessageHud.MessageType.Center, T("dungeons_boss_defeated", name));
            VaultReward.Give(Site);
        }
    }
}
