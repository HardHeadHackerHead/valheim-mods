using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Dungeons
{
    /// <summary>
    /// A boss fight, on the entrance's owner (the toolkit's data: BossModel.cs). MechEncounter's boss hooks call in here: BossStart when
    /// the fight starts, BossTick a few times a second while it goes on (and after its boss fell, through its death), BossReset when it
    /// goes back to idle. The owner decides everything and saves it in the entrance's ZDO (BossRecord, BossKey): the intro's clock and
    /// the boss waking, the phases passed, a shield, enrage, the ability being cast and its targets, a leap or a dash, its death. Every
    /// game draws and hears it from there (BossShow.cs); the game that owns the boss creature carries out its moves (BossBody.cs).
    ///
    /// The fight, in order:
    /// - started: a new record. The intro's beats fire their wires as their times come; at its "wake" the boss is spawned (the land's
    ///   elite or its role, at the level cap, at the first point; its health times "health", more per extra player); at "leapAt" it
    ///   leaps to "leap"; until the intro's "seconds" it can't be hurt and stands still. No intro: it comes at once, as before.
    /// - each phase as its health passes "at": wires, adds (at the fight's points but its first), its abilities from now, a leap, enrage,
    ///   a shield (the conditions' mechanisms put back first: lighting the braziers before doesn't count).
    /// - a shield breaks when its conditions all hold, when a weak hit lands, or after its seconds: a stagger and a heavy blow.
    /// - weakTo: a mechanism arriving at its state (a hammer coming down) with the boss inside its area: a share of its health, a
    ///   stagger, its shield broken and its armour off for a while.
    /// - abilities on their cooldowns, one at a time, not while it leaps or channels a shield (but rain, summon and wires do then).
    /// - stuck: not moved 1 m in "unstick" seconds while away from the nearest player in the fight: it leaps to them.
    /// - its death: the death's beats; after the death's seconds the trophy where it fell and the fight cleared (its wires: the gates open,
    ///   the vault moment and each player's vault roll, MechEncounter).
    /// </summary>
    internal partial class MechSet
    {
        public const string BossKey = "quaddungeon_bossfight";
        internal static readonly int BossKeyHash = BossKey.GetStableHashCode();   // (cleared by a reset and a remake: Dungeon.Reset, Rebuild.Do)
        public static readonly int BossScaleHash = "quaddungeon_bossscale".GetStableHashCode();   // float on the boss's ZDO: its size
        public static readonly int BossFightHash = "quaddungeon_bossfightid".GetStableHashCode();   // long on the boss's ZDO: its fight (Stamp)
        public const short WeakMark = 7311;      // HitData.m_toolTier of the toolkit's own blows on the boss: through its shield and armour
        private static readonly int GroundMask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "terrain", "vehicle");

        internal static int BossTagHash => BossTag;            // (MechEncounter's keys, for BossBody)
        internal static int EncounterTagHash => TagHash;
        internal static void WarnOnceBoss(string message) => WarnOnce(message);

        private string _bossText;
        private Dictionary<string, BossRecord> _bosses = new Dictionary<string, BossRecord>();
        private readonly Dictionary<string, KeyValuePair<Vector3, float>> _bossStill = new Dictionary<string, KeyValuePair<Vector3, float>>();

        private void ReadBosses()
        {
            ZDO z = Site.Zdo;
            if (z == null) return;
            string text = z.GetString(BossKeyHash) ?? "";
            if (text == _bossText) return;
            _bossText = text;
            _bosses = BossRecord.Parse(text);
        }

        /// <summary>The fight's record as saved (a copy to change), or null.</summary>
        public BossRecord BossRecordOf(Mech m)
        {
            ReadBosses();
            return _bosses.TryGetValue(m.Key, out BossRecord r) ? r.Copy() : null;
        }

        private void WriteBoss(Mech m, BossRecord r, string why)
        {
            if (!Site.IsOwner) return;
            ReadBosses();
            var all = new Dictionary<string, BossRecord>(_bosses);
            if (r == null) all.Remove(m.Key); else all[m.Key] = r;
            string text = BossRecord.Format(all);
            if (text == _bossText) return;
            Site.Zdo.Set(BossKeyHash, text);
            _bossText = text;
            _bosses = all;
            if (why != null) Plugin.Log.LogInfo($"Dungeon {Site.Id}: boss {m.Key}: {why}");
        }

        // ---- the hooks

        /// <summary>The fight starts (StartEncounter): a new record; the boss now, or at its intro's wake.</summary>
        private void BossStart(Mech m)
        {
            BossDef d = m.Def.Encounter.Boss;
            var b = new BossRecord { Start = MechClock.Now };
            _bossStill.Remove(m.Key);
            // (the game builds creatures' navmesh only up to 5500 m: Pathfinding's tiles, 2500 +- 3000; above it nothing walks)
            if (RoomPoint(m, m.Def.Encounter.Points.FirstOrDefault()).y > 5490f)
                WarnOnce($"Dungeon {Site.Id}: boss {m.Key} stands above 5490 m, where the game has no navmesh: its creatures can't walk");
            RemoveStaleBosses(m, b);   // (a boss left from an earlier fight, a reset or a remake: never this fight's)
            if ((d.Intro?.Wake ?? 0f) <= 0f) b.Woke = SpawnTheBoss(m, b) != null;
            WriteBoss(m, b, d.Intro != null ? $"the intro begins ({d.Intro.Seconds:0.#} s, it wakes at {d.Intro.Wake:0.#} s)" : "the fight begins");
        }

        /// <summary>Back to idle (a wipe, a command): its record goes.</summary>
        private void BossReset(Mech m)
        {
            _bossStill.Remove(m.Key);
            WriteBoss(m, null, "its record cleared");
        }

        /// <summary>The owner, a few times a second while the fight goes on.</summary>
        private void BossTick(Mech m, EncounterRecord r, List<Character> alive)
        {
            BossDef d = m.Def.Encounter.Boss;
            double now = MechClock.Now;
            BossRecord b = BossRecordOf(m);
            if (b == null)
            {
                // a fight from before the toolkit (or its record lost): past its intro
                b = new BossRecord { Start = now - 1000.0, Woke = r.Kills > 0 || alive.Any(IsBoss), Phase = r.Wave };
                if (d.Intro != null) b.Beats = d.Intro.Beats.Count;
            }
            BossRecord was = b.Copy();
            string why = null;
            double t = now - b.Start;

            // the intro: its beats' wires, the boss waking, its leap
            if (d.Intro != null)
            {
                List<BossBeat> beats = d.Intro.Beats.OrderBy(x => x.At).ToList();
                while (b.Beats < beats.Count && beats[b.Beats].At <= t) { FireWires(m, beats[b.Beats].Wires, $"its intro at {beats[b.Beats].At:0.#} s"); b.Beats++; }
            }
            // this fight's boss only (one left from an earlier fight, a reset or a remake is taken away)
            RemoveStaleBosses(m, b, alive);
            Character boss = alive.FirstOrDefault(c => IsBoss(c) && OfFight(c, b));
            if (!b.Woke)
            {
                if (t < (d.Intro?.Wake ?? 0f)) { Save(m, was, b, why); return; }
                GameObject go = SpawnTheBoss(m, b);
                b.Woke = go != null;
                boss = go != null ? go.GetComponent<Character>() : null;
                why = "it wakes";
                if (!b.Woke) { Save(m, was, b, "it can't be made here"); return; }
            }
            if (d.Intro?.Leap != null && t >= d.Intro.LeapAt && b.LeapAt < b.Start + d.Intro.LeapAt - 0.01 && boss != null)
                Leap(b, boss, RoomPoint(m, d.Intro.Leap.Value), now, "the intro", big: true);

            // dead?
            if (boss == null)
            {
                if (b.DeadAt <= 0.0)
                {
                    Vector3 last = m.BossLast != Vector3.zero ? m.BossLast : RoomPoint(m, m.Def.Encounter.Points[0]);
                    if (!SomeoneNear(last)) { Save(m, was, b, why); return; }   // (not loaded here: it may well be alive)
                    b.DeadAt = now;
                    b.Cast = -1;
                    b.Shield = -1;
                    why = "it falls";
                }
                BossDeath(m, r, b, now);
                Save(m, was, b, why);
                return;
            }
            m.BossLast = boss.transform.position;
            if (b.Next.Count < d.Abilities.Count) b.Next.AddRange(Enumerable.Repeat(0.0, d.Abilities.Count - b.Next.Count));
            if (b.WeakSeen.Count < d.WeakTo.Count) b.WeakSeen.AddRange(Enumerable.Repeat(0.0, d.WeakTo.Count - b.WeakSeen.Count));
            bool intro = d.Intro != null && t < d.Intro.Seconds;

            // phases, as its health falls (none during the intro: it can't be hurt then)
            float health = boss.GetHealthPercentage();
            while (!intro && b.Phase < d.Phases.Count && health <= d.Phases[b.Phase].At)
            {
                EnterPhase(m, b, boss, b.Phase, now);
                b.Phase++;
                r.Wave = b.Phase;
                r.Time = now;
                WriteEncounter(m, r, $"its boss at {health:P0}: phase {b.Phase}");
                why = why ?? $"phase {b.Phase}";
                if (d.Phases[b.Phase - 1].HealTo > 0f) break;   // (a second wind: its health is read again at the next look)
            }

            // weak hits: a mechanism arriving at its state with the boss under it
            for (int i = 0; i < d.WeakTo.Count; i++)
            {
                BossWeak w = d.WeakTo[i];
                Mech wm = Sibling(m, w.Mech);
                if (wm == null) continue;
                MechMotion mo = Motion(wm, now);
                double arrived = mo.Start + mo.Seconds;
                // on its way down with the boss under it: chains snap round its legs and hold it there until it lands (a well-timed
                // pull always lands; one with the boss off the target just drops on the floor)
                if (mo.To == w.State && mo.Moving(now) && b.ClampUntil < arrived && InWeakArea(wm, w, boss.transform.position))
                {
                    b.ClampUntil = arrived + 0.15;
                    b.ClampWeak = i;
                    why = $"{w.Mech} is coming down on it: held till it lands";
                }
                if (mo.To != w.State || now < arrived || double.IsInfinity(arrived) || now - arrived > 1.5 || arrived <= b.WeakSeen[i] + 0.01) continue;
                b.WeakSeen[i] = arrived;
                bool clamped = Math.Abs(b.ClampUntil - (arrived + 0.15)) < 0.02;
                if (!clamped && !InWeakArea(wm, w, boss.transform.position)) continue;
                WeakHit(m, b, boss, w, now);
                ThrowOut(b, boss, wm, w, now);
                why = $"{w.Mech} came down on it";
            }

            // the shield: broken by its conditions, or failing after its seconds
            if (b.Shield >= 0 && b.Shield < d.Phases.Count && d.Phases[b.Shield].Shield != null)
            {
                BossShield s = d.Phases[b.Shield].Shield;
                bool met = s.Until.Count > 0 && s.Until.All(c => { Mech cm = Sibling(m, c.Mech); return cm != null && StateOf(cm) == c.State; });
                if (met && b.ShieldCount + 1 < Math.Max(1, s.Times))
                {
                    // not the last time: it flickers, and what broke it is put back (the braziers go out): again
                    b.ShieldCount++;
                    b.FlickerAt = now;
                    var back = s.Until.Select(c => Sibling(m, c.Mech)).Where(cm => cm != null).Select(cm => new KeyValuePair<Mech, int>(cm, cm.Def.Start)).ToList();
                    if (back.Count > 0) Change(back, $"encounter {m.Key}: its shield flickers ({b.ShieldCount} of {s.Times})");
                    why = $"its shield flickers ({b.ShieldCount} of {s.Times})";
                }
                else if (met || now - b.ShieldAt >= s.Seconds) { BreakShield(m, b, boss, now, met ? "the room broke its shield" : $"its shield failed after {s.Seconds:0} s"); why = "its shield breaks"; }
            }
            else b.Shield = -1;

            // abilities: the one being cast lands; or the next one due
            if (b.Cast >= 0)
            {
                BossAbility a = b.Cast < d.Abilities.Count ? d.Abilities[b.Cast] : null;
                if (a == null) b.Cast = -1;
                else if (now >= b.CastAt + a.Warn)
                {
                    Land(m, b, a, boss, now);
                    b.Next[b.Cast] = now + a.Every * (0.85 + 0.3 * Rng(m, now).NextDouble());
                    b.Cast = -1;
                }
            }
            else if (!intro && !Leaping(b, now))
            {
                List<int> usable = Usable(d, b.Phase);
                for (int i = 0; i < d.Abilities.Count; i++)
                {
                    if (!usable.Contains(i)) { b.Next[i] = 0.0; continue; }
                    if (b.Next[i] <= 0.0) b.Next[i] = now + (d.Abilities[i].First >= 0f ? d.Abilities[i].First : d.Abilities[i].Every * 0.6f);
                }
                bool held = b.Shield >= 0 && d.Phases[b.Shield].Shield?.Hold == true;
                int pick = -1;
                foreach (int i in usable)
                {
                    BossAbility a = d.Abilities[i];
                    if (b.Next[i] > now || (held && (a.Kind == "slam" || a.Kind == "charge"))) continue;
                    if (pick < 0 || b.Next[i] < b.Next[pick]) pick = i;
                }
                if (pick >= 0)
                {
                    if (Cast(m, b, d.Abilities[pick], boss, now)) { b.Cast = pick; b.CastAt = now; why = $"casts {d.Abilities[pick].Id}"; }
                    else b.Next[pick] = now + 1.5;   // (nothing to aim at: a little later, and the others meanwhile)
                }
            }

            // stuck far from the players: it leaps to the nearest
            if (!intro) Unstick(m, b, boss, now);
            Save(m, was, b, why);
        }

        /// <summary>Writes the record if this tick changed it.</summary>
        private void Save(Mech m, BossRecord was, BossRecord b, string why)
        {
            if (BossRecord.Format(new Dictionary<string, BossRecord> { ["x"] = was }) != BossRecord.Format(new Dictionary<string, BossRecord> { ["x"] = b }))
                WriteBoss(m, b, why);
        }

        // ---- spawning it

        /// <summary>The boss: the land's elite (or its role) at the level cap, at the first point, made a boss on every game (Character.m_boss:
        /// the game's boss bar with its name, for everyone near), its size and health from the pack (BossToughness).</summary>
        private GameObject SpawnTheBoss(Mech m, BossRecord fight)
        {
            BossDef b = m.Def.Encounter.Boss;
            if (Site.Built == null || m.Def.Encounter.Points.Count == 0) { WarnOnce($"Encounter {m.Key}: a boss with no points"); return null; }
            var rng = new System.Random(unchecked(Site.Seed * 31 + m.Key.GetStableHashCode()));
            GameObject prefab = Builder.EncounterCreature(Site.Built, string.IsNullOrEmpty(b.Role) ? "elite" : b.Role, rng, out _);
            if (prefab == null) { WarnOnce($"Encounter {m.Key}: no creature fits its boss here"); return null; }
            Vector3 at = RoomPoint(m, m.Def.Encounter.Points[0]) + Vector3.up * 0.2f;
            GameObject go = Spawn(m, prefab, at, Builder.LevelCapOf(Site.Built));
            if (go == null) return null;
            int players = PlayersInside();
            Character made = go.GetComponent<Character>();
            float baseX = b.Fight > 0f && made != null ? Mathf.Max(1f, PlayerDps(Site) * b.Fight / Mathf.Max(1f, made.GetMaxHealth())) : Mathf.Max(1f, b.Health);
            float tough = baseX * (1f + Mathf.Max(0f, b.PerPlayer) * (players - 1));
            ZDO z = go.GetComponent<ZNetView>()?.GetZDO();
            z?.Set(BossTag, string.IsNullOrEmpty(b.Name) ? prefab.GetComponent<Character>()?.m_name ?? "" : b.Name);
            z?.Set(BossX, tough);
            z?.Set(BossScaleHash, Mathf.Clamp(b.Scale, 0.5f, 3f));
            z?.Set(BossFightHash, Stamp(fight));
            Character c = go.GetComponent<Character>();
            MakeBoss(c, z);
            BossBody.Apply(c, z);
            if (Player.GetAllPlayers().FirstOrDefault(p => p != null && !p.IsDead() && InArea(m, p.transform.position)) is Player near)
                go.transform.rotation = Quaternion.LookRotation(Vector3.ProjectOnPlane(near.transform.position - at, Vector3.up).normalized + Vector3.forward * 1e-4f);
            Plugin.Log.LogInfo($"Dungeon {Site.Id}: encounter {m.Key}: its boss is {prefab.name} ({b.Name}, x{tough:0.#} health for {players} player(s), size {b.Scale:0.##})");
            return go;
        }

        /// <summary>What one player of the land's tier does to a boss a second, hitting it all the time (a fair weapon of that land, its
        /// misses and stamina counted): what a boss's "fight" seconds are measured in. Meadows 9 ... Deep North 80.</summary>
        public static float PlayerDps(Site site)
        {
            float[] dps = { 9f, 9f, 14f, 20f, 28f, 38f, 50f, 65f, 80f };
            int tier = WorldGenerator.instance != null && site != null ? Builder.TierOf(WorldGenerator.instance.GetBiome(site.EntrancePos)) : 1;
            return dps[Mathf.Clamp(tier, 1, 8)];
        }

        /// <summary>A living player, this game's or (on the server) any peer's, within 60 m: the boss there is loaded if it's alive.</summary>
        private static bool SomeoneNear(Vector3 p)
        {
            Player me = Player.m_localPlayer;
            if (me != null && Vector3.Distance(me.transform.position, p) < 60f) return true;
            return ZNet.instance != null && ZNet.instance.IsServer() && ZNet.instance.GetPeers().Any(peer => peer != null && Vector3.Distance(peer.m_refPos, p) < 60f);
        }

        // ---- what makes it a boss (moved here from MechEncounter.cs as they were)

        private static readonly int BossTag = "quaddungeon_boss".GetStableHashCode();     // string on the boss's ZDO: its name (a $ key)
        private static readonly int BossX = "quaddungeon_bossx".GetStableHashCode();      // float: it takes 1/this of each hit

        /// <summary>The owner: the boss's trophy (Builder's reward item) where it fell (or at its point after a relog), a saved item.</summary>
        private void DropTrophy(Mech m)
        {
            BossDef d = m.Def.Encounter.Boss;
            string name = d.Trophy ?? (d.Mini ? "" : Builder.TrophyItem);
            if (string.IsNullOrEmpty(name)) return;   // (a mini boss drops none: the vault's rewards are the prize)
            // ours by name, else whatever ZNetScene has under the name (an item another copy of the mod registered before a reload, or
            // a game item a pack names)
            GameObject trophy = Builder.RewardItem(name) ?? (ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(name) : null);
            if (trophy == null || trophy.GetComponent<ItemDrop>() == null) { WarnOnce($"The boss's trophy item {name} isn't registered: no trophy"); return; }
            Vector3 at = m.BossLast != Vector3.zero ? m.BossLast : RoomPoint(m, m.Def.Encounter.Points.Count > 0 ? m.Def.Encounter.Points[0] : Vector3.zero);
            try { Object.Instantiate(trophy, at + Vector3.up * 1f, Quaternion.identity); }
            catch (Exception e) { WarnOnce("Dropping the boss's trophy: " + e.Message); }
        }

        /// <summary>A character whose ZDO says it's one of our bosses: the game's boss (its bar) with our name. On every game, when it wakes.</summary>
        public static void MakeBoss(Character c, ZDO z)
        {
            if (c == null || z == null) return;
            string name = z.GetString(BossTag);
            if (string.IsNullOrEmpty(name)) return;
            c.m_boss = true;
            c.m_name = name;
        }

        /// <summary>How much harder our boss is to kill (1: an ordinary creature).</summary>
        public static float BossToughness(ZDO z) => z == null ? 1f : Mathf.Max(1f, z.GetFloat(BossX, 1f));

        /// <summary>A fight's stamp: its start, to a hundredth of a second (as its record keeps it), on its boss's ZDO.</summary>
        public static long Stamp(BossRecord b) => b == null ? 0L : (long)Math.Round(b.Start * 100.0);

        /// <summary>The boss belongs to this fight (a fight from before the stamp, its record made up: any boss of the encounter).</summary>
        private static bool OfFight(Character c, BossRecord b)
        {
            ZDO z = c.GetComponent<ZNetView>()?.GetZDO();
            if (z == null) return false;
            long stamp = z.GetLong(BossFightHash, 0L);
            return stamp == 0L ? b.Start < MechClock.Now - 900.0 : stamp == Stamp(b);
        }

        /// <summary>The owner: bosses of this encounter loaded here that aren't this fight's (left alive by a reset, a remake or an earlier
        /// fight) are taken away, so a new fight always starts with its own boss at full health.</summary>
        private void RemoveStaleBosses(Mech m, BossRecord b, List<Character> alive = null)
        {
            foreach (Character c in (alive ?? Spawned(m)).Where(c => IsBoss(c) && !OfFight(c, b)).ToList())
            {
                ZNetView v = c.GetComponent<ZNetView>();
                if (v == null || !v.IsValid()) continue;
                Plugin.Log.LogInfo($"Dungeon {Site.Id}: boss {m.Key}: a boss from an earlier fight ({c.GetHealthPercentage():P0} health) taken away");
                v.ClaimOwnership();
                ZNetScene.instance.Destroy(c.gameObject);
                alive?.Remove(c);
            }
        }

        private bool IsBoss(Character c)
        {
            ZDO z = c.GetComponent<ZNetView>()?.GetZDO();
            return z != null && !string.IsNullOrEmpty(z.GetString(BossTag));
        }

        // ---- phases, shields, weak hits

        private void EnterPhase(Mech m, BossRecord b, Character boss, int i, double now)
        {
            BossPhase ph = m.Def.Encounter.Boss.Phases[i];
            var pending = new Dictionary<Mech, int>();
            int Cur(Mech x) => pending.TryGetValue(x, out int s) ? s : StateOf(x);
            if (ph.Wires.Count > 0) ApplyWires(m, pending, Cur, false, true, ph.Wires);
            if (ph.Shield != null)
            {
                // the shield: what breaks it put back first (braziers lit before it came up don't count)
                foreach (BossCond c in ph.Shield.Until)
                {
                    Mech cm = Sibling(m, c.Mech);
                    if (cm != null && c.State != cm.Def.Start) pending[cm] = cm.Def.Start;   // (its start: a brazier out, a push block in its start cell)
                }
                b.Shield = i;
                b.ShieldAt = now;
                b.Cast = -1;
            }
            if (pending.Count > 0) Change(pending.ToList(), $"encounter {m.Key}: its boss's phase {i + 1}");
            if (ph.Adds.Count > 0) SpawnAt(m, ph.Adds, AddPoints(m, null), $"boss phase {i + 1}");
            if (ph.Enrage > 1f) b.Enrage = ph.Enrage;
            if (ph.HealTo > 0f && boss != null && !boss.IsDead())
            {
                // a second wind: back up to its share of health, for everyone to see (BossShow: the surge)
                float lack = ph.HealTo - boss.GetHealthPercentage();
                if (lack > 0f) boss.Heal(lack * boss.GetMaxHealth(), true);
                b.HealAt = now;
                Plugin.Log.LogInfo($"Dungeon {Site.Id}: boss {m.Key}: a second wind, back to {ph.HealTo:P0} (phase {i + 1})");
            }
            if (ph.Leap.HasValue) Leap(b, boss, RoomPoint(m, ph.Leap.Value), now, $"phase {i + 1}", big: true);
        }

        private void BreakShield(Mech m, BossRecord b, Character boss, double now, string why)
        {
            BossShield s = b.Shield >= 0 && b.Shield < m.Def.Encounter.Boss.Phases.Count ? m.Def.Encounter.Boss.Phases[b.Shield].Shield : null;
            b.Shield = -1;
            b.ShieldCount = 0;
            b.BrokeAt = now;
            Plugin.Log.LogInfo($"Dungeon {Site.Id}: boss {m.Key}: {why}");
            if (s == null) return;
            FireWires(m, s.BreakWires, "its shield broke");
            Blow(boss, s.BreakShare, true);
        }

        private bool InWeakArea(Mech wm, BossWeak w, Vector3 world)
        {
            if (wm.Base == null) return false;
            Vector3 local = wm.Base.InverseTransformPoint(world) - w.AreaAt;
            Vector3 half = w.Area * 0.5f;
            return Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.y) <= half.y && Mathf.Abs(local.z) <= half.z;
        }

        private void WeakHit(Mech m, BossRecord b, Character boss, BossWeak w, double now)
        {
            b.HitAt = now;
            if (w.Breaks)
            {
                b.ArmourOff = now + m.Def.Encounter.Boss.ArmourOff;
                if (b.Shield >= 0) BreakShield(m, b, boss, now, $"{w.Mech} broke its shield");
            }
            Blow(boss, w.Share, w.Stagger);
            Plugin.Log.LogInfo($"Dungeon {Site.Id}: boss {m.Key}: {w.Mech} came down on it ({w.Share:P0} of its health{(w.Breaks ? ", its guard broken" : "")})");
        }

        /// <summary>A share of the boss's whole health (its toughness counted), through its shield and armour (WeakMark), and a stagger.</summary>
        private static void Blow(Character boss, float share, bool stagger)
        {
            if (boss == null || boss.IsDead() || share <= 0f && !stagger) return;
            ZDO z = boss.GetComponent<ZNetView>()?.GetZDO();
            if (share > 0f)
            {
                var hit = new HitData { m_point = boss.GetCenterPoint(), m_dir = Vector3.down, m_toolTier = WeakMark, m_hitType = HitData.HitType.Undefined };
                hit.m_damage.m_damage = share * boss.GetMaxHealth() * BossToughness(z);
                boss.Damage(hit);
            }
            if (stagger) boss.Stagger(-boss.transform.forward);
        }

        // ---- abilities

        /// <summary>The abilities (indices) used in this phase: each one's own "phases", else the last phase's "use" up to it, else all.</summary>
        private static List<int> Usable(BossDef d, int phase)
        {
            List<string> use = null;
            for (int p = Math.Min(phase, d.Phases.Count) - 1; p >= 0 && use == null; p--) use = d.Phases[p].Use;
            var list = new List<int>();
            for (int i = 0; i < d.Abilities.Count; i++)
            {
                BossAbility a = d.Abilities[i];
                if (a.Phases != null ? a.Phases.Contains(phase) : use == null || use.Contains(a.Id)) list.Add(i);
            }
            return list;
        }

        /// <summary>Starts one: its targets into the record. False when there's nothing to aim at.</summary>
        private bool Cast(Mech m, BossRecord b, BossAbility a, Character boss, double now)
        {
            b.CastPts.Clear();
            List<Player> players = Player.GetAllPlayers().Where(p => p != null && !p.IsDead() && InArea(m, p.transform.position)).ToList();
            Vector3 at = boss.transform.position;
            System.Random rng = Rng(m, now);
            switch (a.Kind)
            {
                case "slam":
                    if (!players.Any(p => Vector3.Distance(p.transform.position, at) < a.Radius + 4f)) return false;   // (nobody near: no slam)
                    b.CastPts.Add(Floor(at) ?? at);
                    return true;
                case "rain":
                    foreach (Player p in players) b.CastPts.Add(Floor(p.transform.position) ?? p.transform.position);
                    for (int i = 0; i < a.Count; i++)
                    {
                        Vector3 r = at + Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f) * Vector3.forward * (3f + (float)rng.NextDouble() * 6f);
                        if (Floor(r) is Vector3 f && InArea(m, f)) b.CastPts.Add(f);
                    }
                    return b.CastPts.Count > 0;
                case "charge":
                {
                    if (players.Count == 0) return false;
                    Player target = players[rng.Next(players.Count)];
                    Vector3 dir = Vector3.ProjectOnPlane(target.transform.position - at, Vector3.up);
                    if (dir.magnitude < 3f) return false;
                    dir.Normalize();
                    float len = Mathf.Min(a.Length, Vector3.Distance(at, target.transform.position) + 4f);
                    // stops short of anything solid at its chest's height
                    Vector3 chest = at + Vector3.up * 1.2f;
                    if (Physics.SphereCast(chest, 0.6f, dir, out RaycastHit hit, len, GroundMask)) len = Mathf.Max(0f, hit.distance - 1f);
                    if (len < 3f) return false;
                    Vector3 end = Floor(at + dir * len) ?? at + dir * len;
                    b.CastPts.Add(at);
                    b.CastPts.Add(end);
                    return true;
                }
                case "summon":
                    b.CastPts.AddRange(AddPoints(m, a));
                    return b.CastPts.Count > 0;
                case "wires":
                    b.CastPts.Add(at);
                    return true;
            }
            return false;
        }

        /// <summary>An ability lands (the owner's part: the damage is each player's own game's, BossShow).</summary>
        private void Land(Mech m, BossRecord b, BossAbility a, Character boss, double now)
        {
            switch (a.Kind)
            {
                case "summon":
                    SpawnAt(m, a.Adds, b.CastPts, $"its {a.Id}");
                    break;
                case "wires":
                    FireWires(m, a.Wires, $"its {a.Id}");
                    break;
                case "charge":
                    if (b.CastPts.Count >= 2 && boss != null)
                    {
                        float len = Vector3.Distance(b.CastPts[0], b.CastPts[1]);
                        b.LeapAt = now;
                        b.LeapFrom = boss.transform.position;
                        b.LeapTo = b.CastPts[1];
                        b.LeapDur = Mathf.Clamp(len / 16f, 0.35f, 1.6f);
                        b.LeapArc = 0f;
                    }
                    break;
            }
            if (a.Wires.Count > 0 && a.Kind != "wires") FireWires(m, a.Wires, $"its {a.Id}");
        }

        // ---- moving it: leaps and dashes (carried out by the game that owns the creature: BossBody)

        public const float LeapWarn = 1.0f;     // a leap's landing ring shows this long before it jumps (it crouches, held, meanwhile)

        /// <summary>A leap: big (the intro's and a phase's: slow and high, to be seen from across the room), or not (a hop to the players
        /// when stuck). Its landing ring shows LeapWarn seconds before it jumps.</summary>
        private void Leap(BossRecord b, Character boss, Vector3 to, double now, string why, bool big = false)
        {
            if (boss == null) return;
            Vector3 land = Floor(to) ?? to;
            if (!Site.Contains(land)) return;
            float d = Vector3.Distance(boss.transform.position, land);
            b.LeapAt = now + LeapWarn;
            b.LeapFrom = boss.transform.position;
            b.LeapTo = land;
            b.LeapDur = big ? Mathf.Clamp(1.1f + d * 0.05f, 1.4f, 2.2f) : Mathf.Clamp(0.7f + d * 0.04f, 0.8f, 1.6f);
            b.LeapArc = big ? Mathf.Clamp(3.5f + d * 0.2f, 4.5f, 8f) : Mathf.Clamp(2f + d * 0.15f, 2.5f, 6f);
            b.Cast = -1;
            Plugin.Log.LogInfo($"Dungeon {Site.Id}: its boss leaps {d:0.0} m in {LeapWarn:0.#} s ({why})");
        }

        /// <summary>Thrown out from under a weak hit's mechanism (a hammer that came down on it): a short hop to just outside its area, at
        /// once, so it never stands inside the hammer's block.</summary>
        private void ThrowOut(BossRecord b, Character boss, Mech wm, BossWeak w, double now)
        {
            if (boss == null || wm.Base == null) return;
            Vector3 centre = wm.Base.TransformPoint(new Vector3(w.AreaAt.x, 0f, w.AreaAt.z));
            Vector3 away = Vector3.ProjectOnPlane(boss.transform.position - centre, Vector3.up);
            if (away.sqrMagnitude < 0.01f) away = -boss.transform.forward;
            Vector3 to = centre + away.normalized * (Mathf.Max(w.Area.x, w.Area.z) * 0.5f + 1.5f);
            Vector3 land = Floor(to) ?? to;
            if (!Site.Contains(land)) return;
            b.LeapAt = now;
            b.LeapFrom = boss.transform.position;
            b.LeapTo = land;
            b.LeapDur = 0.6f;
            b.LeapArc = 1.2f;
            b.Cast = -1;
        }

        /// <summary>Held for a leap: from its order (the ring shows, it crouches) to a moment after it lands.</summary>
        public static bool Leaping(BossRecord b, double now) => b.LeapDur > 0f && now >= b.LeapAt - LeapWarn - 0.05 && now < b.LeapAt + b.LeapDur + 0.2;

        /// <summary>The owner: a boss that hasn't moved 1 m in its "unstick" seconds while away from every player in the fight (a dais it
        /// can't step off, a ledge, a corner the navmesh doesn't reach) leaps to the nearest of them.</summary>
        private void Unstick(Mech m, BossRecord b, Character boss, double now)
        {
            BossDef d = m.Def.Encounter.Boss;
            if (d.Unstick <= 0f || Leaping(b, now) || b.Cast >= 0 || (b.Shield >= 0 && d.Phases[b.Shield].Shield?.Hold == true))
            {
                _bossStill.Remove(m.Key);
                return;
            }
            Vector3 at = boss.transform.position;
            if (!_bossStill.TryGetValue(m.Key, out KeyValuePair<Vector3, float> still) || Vector3.Distance(still.Key, at) > 1f)
            {
                _bossStill[m.Key] = new KeyValuePair<Vector3, float>(at, Time.time);
                return;
            }
            if (Time.time - still.Value < d.Unstick) return;
            Player near = Player.GetAllPlayers().Where(p => p != null && !p.IsDead() && InArea(m, p.transform.position))
                                .OrderBy(p => Vector3.Distance(p.transform.position, at)).FirstOrDefault();
            _bossStill[m.Key] = new KeyValuePair<Vector3, float>(at, Time.time);
            if (near == null) return;
            float reach = 3f + boss.GetRadius() * Mathf.Max(1f, d.Scale);
            float dist = Vector3.Distance(near.transform.position, at);
            if (dist < reach + 1.5f) return;
            Vector3 to = near.transform.position + Vector3.ProjectOnPlane(at - near.transform.position, Vector3.up).normalized * Mathf.Min(reach, dist * 0.5f);
            Leap(b, boss, to, now, $"stuck {d.Unstick:0} s, {dist:0} m from {near.GetPlayerName()}");
        }

        // ---- death

        /// <summary>The owner, from its fall: the death's beats' wires as their times come; after its seconds the trophy and the clear.</summary>
        private void BossDeath(Mech m, EncounterRecord r, BossRecord b, double now)
        {
            BossTimeline death = m.Def.Encounter.Boss.Death;
            double t = now - b.DeadAt;
            if (death != null)
            {
                List<BossBeat> beats = death.Beats.OrderBy(x => x.At).ToList();
                while (b.DeathBeats < beats.Count && beats[b.DeathBeats].At <= t) { FireWires(m, beats[b.DeathBeats].Wires, $"its death at {beats[b.DeathBeats].At:0.#} s"); b.DeathBeats++; }
            }
            if (t < (death?.Seconds ?? 0f)) return;
            DropTrophy(m);
            ClearEncounter(m, "its boss is dead");
        }

        // ---- helpers

        private void FireWires(Mech m, List<Wire> wires, string why)
        {
            if (wires == null || wires.Count == 0) return;
            var pending = new Dictionary<Mech, int>();
            int Cur(Mech x) => pending.TryGetValue(x, out int s) ? s : StateOf(x);
            ApplyWires(m, pending, Cur, false, true, wires);
            if (pending.Count > 0) Change(pending.ToList(), $"encounter {m.Key}: {why}");
        }

        /// <summary>Where adds come: an ability's points, else the fight's points but the boss's own (its dais).</summary>
        private List<Vector3> AddPoints(Mech m, BossAbility a)
        {
            List<Vector3> pts = a != null && a.Points.Count > 0 ? a.Points : m.Def.Encounter.Points.Skip(m.Def.Encounter.Points.Count > 1 ? 1 : 0).ToList();
            return pts.Select(p => RoomPoint(m, p)).ToList();
        }

        /// <summary>Creatures of these roles at these points (in turn), fitted to the land, as a fight's waves.</summary>
        private void SpawnAt(Mech m, Dictionary<string, int> roles, List<Vector3> points, string what)
        {
            EncounterDef e = m.Def.Encounter;
            if (points.Count == 0 || Site.Built == null) return;
            var rng = new System.Random(unchecked(Site.Seed * 31 + m.Key.GetStableHashCode() * 17 + what.GetStableHashCode() + (int)(MechClock.Now * 10)));
            int cap = Builder.LevelCapOf(Site.Built), at = rng.Next(points.Count);
            var list = new List<string>();
            foreach (KeyValuePair<string, int> kv in roles)
                for (int i = 0; i < kv.Value; i++)
                {
                    GameObject prefab = Builder.EncounterCreature(Site.Built, kv.Key, rng, out _);
                    if (prefab == null) continue;
                    int level = Mathf.Clamp(kv.Key == "elite" ? e.EliteLevel : e.Level, 1, cap);
                    if (Spawn(m, prefab, points[at++ % points.Count] + Vector3.up * 0.2f, level) != null) list.Add(prefab.name + (level > 1 ? $"*{level - 1}" : ""));
                }
            Plugin.Log.LogInfo($"Dungeon {Site.Id}: encounter {m.Key} {what}: {string.Join(", ", list)}");
        }

        /// <summary>The floor under a point (from 3 m above it, 12 m down), or null.</summary>
        private static Vector3? Floor(Vector3 p) =>
            Physics.Raycast(p + Vector3.up * 3f, Vector3.down, out RaycastHit hit, 12f, GroundMask, QueryTriggerInteraction.Ignore) ? hit.point : (Vector3?)null;

        private System.Random Rng(Mech m, double now) => new System.Random(unchecked(Site.Seed * 31 + m.Key.GetStableHashCode() + (int)(now * 10)));
    }
}
