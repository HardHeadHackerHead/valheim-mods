using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using UnityEngine;
using Object = UnityEngine.Object;
using static DHack.Shared.Translations;

namespace Dungeons
{
    /// <summary>An encounter's saved state: idle, fighting (which wave, when it began, kills so far for a siege, its locks' states from
    /// before) or cleared.</summary>
    internal struct EncounterRecord
    {
        public const int Idle = 0, Fighting = 1, Cleared = 2;
        public int State, Wave, Kills;
        public double Time;             // server time of the last change (the wave's start, the fight's start, the clear)
        public string Before;           // the lock mechanisms' states when it started, one digit each, in Lock's order

        public static Dictionary<string, EncounterRecord> Parse(string text)
        {
            var all = new Dictionary<string, EncounterRecord>();
            if (string.IsNullOrEmpty(text)) return all;
            foreach (string entry in text.Split(';'))
            {
                int eq = entry.IndexOf('=');
                if (eq <= 0) continue;
                string[] f = entry.Substring(eq + 1).Split(',');
                if (f.Length < 4) continue;
                int.TryParse(f[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int state);
                int.TryParse(f[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int wave);
                int.TryParse(f[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int kills);
                double.TryParse(f[3], NumberStyles.Float, CultureInfo.InvariantCulture, out double time);
                all[entry.Substring(0, eq)] = new EncounterRecord { State = state, Wave = wave, Kills = kills, Time = time, Before = f.Length > 4 ? f[4] : "" };
            }
            return all;
        }

        public static string Format(Dictionary<string, EncounterRecord> all)
        {
            var sb = new StringBuilder();
            foreach (KeyValuePair<string, EncounterRecord> kv in all)
            {
                if (sb.Length > 0) sb.Append(';');
                EncounterRecord r = kv.Value;
                sb.Append(kv.Key).Append('=').Append(r.State.ToString(CultureInfo.InvariantCulture)).Append(',').Append(r.Wave.ToString(CultureInfo.InvariantCulture))
                  .Append(',').Append(r.Kills.ToString(CultureInfo.InvariantCulture)).Append(',').Append(r.Time.ToString("0.###", CultureInfo.InvariantCulture))
                  .Append(',').Append(r.Before ?? "");
            }
            return sb.ToString();
        }
    }

    /// <summary>
    /// Fight rooms: mechanisms of kind "encounter" (EncounterDef). Started by a living player stepping into its area ("enter"), a lever's
    /// or plate's wire "start" ("wire"), or the chest at its Chest opened ("open"). Its locks (any mechanisms of the room) go to state 0,
    /// then its waves come at its points: the land's creatures, fitted by the builder (Builder.EncounterCreature), one more melee a wave
    /// for each extra player inside the dungeon. The next wave when the last is dead (after Pause), or after Timeout anyway. Cleared when
    /// the last wave is dead: the unlock sound, its wires fire (or, with none, its locks go back as they were). A siege: waves come only
    /// while its plate (HoldBy) is pressed, until Siege are killed. Guard mode (no waves): cleared when no creature is alive in its area.
    /// A wipe (no living player in the area for 60 s) puts it back to idle: its creatures go, its locks go back.
    ///
    /// The state is in the entrance's ZDO (Keys.Encounters), written by the owner only, who also spawns. The creatures are never saved by
    /// ZDOID (it changes every load): each is tagged (EncounterTag: the dungeon's id and the encounter's key) and counted by its tag among
    /// the objects loaded on the owner's game, so a fight goes on after a relog or a restart (none alive: the wave is done).
    /// </summary>
    internal partial class MechSet
    {
        public const string EncounterTag = "quaddungeon_enc";
        private static readonly int TagHash = EncounterTag.GetStableHashCode();
        private const float WipeAfter = 60f;
        private const float EncounterEvery = 0.25f;

        private string _encText;
        private Dictionary<string, EncounterRecord> _encounters = new Dictionary<string, EncounterRecord>();
        private float _nextEncounter;

        private void ReadEncounters()
        {
            ZDO z = Site.Zdo;
            if (z == null) return;
            string text = z.GetString(Keys.Encounters) ?? "";
            if (text == _encText) return;
            _encText = text;
            _encounters = EncounterRecord.Parse(text);
        }

        public EncounterRecord EncounterOf(Mech m)
        {
            ReadEncounters();
            return _encounters.TryGetValue(m.Key, out EncounterRecord r) ? r : new EncounterRecord { State = EncounterRecord.Idle, Before = "" };
        }

        private void WriteEncounter(Mech m, EncounterRecord r, string why)
        {
            if (!Site.IsOwner) return;
            ReadEncounters();
            var all = new Dictionary<string, EncounterRecord>(_encounters) { [m.Key] = r };
            string text = EncounterRecord.Format(all);
            Site.Zdo.Set(Keys.Encounters, text);
            _encText = text;
            _encounters = all;
            Plugin.Log.LogInfo($"Dungeon {Site.Id}: encounter {m.Key}: {why}");
        }

        // ---- where things are

        private Mech Sibling(Mech m, string id) => string.IsNullOrEmpty(id) ? null : ByKey.TryGetValue(m.Room + "." + id, out Mech s) ? s : null;

        /// <summary>A point of the room's frame in the world.</summary>
        private Vector3 RoomPoint(Mech m, Vector3 p)
        {
            PlacedRoom room = Layout.Rooms[m.Room];
            return Builder.WorldPos(Site.Built, room) + Builder.WorldRot(room) * p;
        }

        private bool InArea(Mech m, Vector3 world)
        {
            if (m.Base == null) return false;
            Vector3 local = m.Base.InverseTransformPoint(world);
            Vector3 half = m.Def.Encounter.Area * 0.5f;
            return Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.y) <= half.y && Mathf.Abs(local.z) <= half.z;
        }

        /// <summary>Living players in its area: loaded here, or (another game's player out of this game's sight) said so in the last 2.5 s.</summary>
        private bool PlayersIn(Mech m) =>
            Player.GetAllPlayers().Any(p => p != null && !p.IsDead() && InArea(m, p.transform.position))
            || m.RemotePress.Values.Any(until => until > Time.time);

        private string TagOf(Mech m) => Site.Id + "|" + m.Key;

        /// <summary>The creatures it spawned that are alive, among the objects loaded here.</summary>
        private List<Character> Spawned(Mech m)
        {
            string tag = TagOf(m);
            return Character.GetAllCharacters().Where(c => c != null && !(c is Player) && !c.IsDead() && c.GetComponent<ZNetView>() is ZNetView v
                                                           && v.IsValid() && v.GetZDO().GetString(TagHash) == tag).ToList();
        }

        // ---- starting, ending

        /// <summary>The owner: start it (a wire "start", a trigger, the use command). False when it isn't idle.</summary>
        public bool StartEncounter(Mech m, string why)
        {
            if (!Site.IsOwner || m.Def.Encounter == null) return false;
            EncounterRecord r = EncounterOf(m);
            if (r.State != EncounterRecord.Idle) return false;
            EncounterDef e = m.Def.Encounter;
            var before = new StringBuilder();
            var shut = new List<KeyValuePair<Mech, int>>();
            foreach (string id in e.Lock)
            {
                Mech lockM = Sibling(m, id);
                int was = lockM != null ? StateOf(lockM) : 0;
                before.Append(Mathf.Clamp(was, 0, 9));
                if (lockM != null) shut.Add(new KeyValuePair<Mech, int>(lockM, 0));
            }
            if (shut.Count > 0) Change(shut, $"encounter {m.Key} locks its room");
            m.EncDeadAt = -1f;
            m.EncEmptySince = -1f;
            m.EncAlive = 0;
            m.EncNextSpawn = 0f;
            WriteEncounter(m, new EncounterRecord { State = EncounterRecord.Fighting, Wave = 0, Kills = e.Boss != null ? 1 : 0, Time = MechClock.Now, Before = before.ToString() }, "started (" + why + ")");
            if (e.Boss != null) SpawnBoss(m);
            else if (e.Siege <= 0 && e.Waves.Count > 0) SpawnWave(m, 0);
            return true;
        }

        private void ClearEncounter(Mech m, string why)
        {
            EncounterRecord r = EncounterOf(m);
            r.State = EncounterRecord.Cleared;
            r.Time = MechClock.Now;
            WriteEncounter(m, r, "cleared (" + why + ")");
            var pending = new Dictionary<Mech, int>();
            int Cur(Mech x) => pending.TryGetValue(x, out int s) ? s : StateOf(x);
            if (m.Def.Wires.Count > 0) ApplyWires(m, pending, Cur, false, true);
            else RestoreLocks(m, r, pending);
            if (pending.Count > 0) Change(pending.ToList(), $"encounter {m.Key} cleared");
        }

        private void RestoreLocks(Mech m, EncounterRecord r, Dictionary<Mech, int> pending)
        {
            List<string> locks = m.Def.Encounter.Lock;
            for (int i = 0; i < locks.Count; i++)
            {
                Mech lockM = Sibling(m, locks[i]);
                if (lockM == null) continue;
                int was = r.Before != null && i < r.Before.Length ? r.Before[i] - '0' : 1;
                pending[lockM] = was;
            }
        }

        /// <summary>Back to idle (a wipe, or the set command): its creatures go, its locks go back to how they were.</summary>
        public void ResetEncounter(Mech m, string why)
        {
            if (!Site.IsOwner) return;
            EncounterRecord r = EncounterOf(m);
            foreach (Character c in Spawned(m))
            {
                ZNetView v = c.GetComponent<ZNetView>();
                if (v == null || !v.IsValid()) continue;
                v.ClaimOwnership();
                ZNetScene.instance.Destroy(c.gameObject);
            }
            if (r.State == EncounterRecord.Fighting)
            {
                var pending = new Dictionary<Mech, int>();
                RestoreLocks(m, r, pending);
                if (pending.Count > 0) Change(pending.ToList(), $"encounter {m.Key} reset");
            }
            WriteEncounter(m, new EncounterRecord { State = EncounterRecord.Idle, Time = MechClock.Now, Before = "" }, "back to idle (" + why + ")");
        }

        /// <summary>The set command: 0 idle, 1 start, 2 cleared.</summary>
        public bool SetEncounter(Mech m, int state, out string why)
        {
            why = null;
            switch (state)
            {
                case EncounterRecord.Idle: ResetEncounter(m, "the set command"); return true;
                case EncounterRecord.Fighting:
                    if (!StartEncounter(m, "the set command")) { why = $"{m.Key} isn't idle (set it to 0 first)"; return false; }
                    return true;
                case EncounterRecord.Cleared:
                    foreach (Character c in Spawned(m)) { c.GetComponent<ZNetView>()?.ClaimOwnership(); ZNetScene.instance.Destroy(c.gameObject); }
                    if (EncounterOf(m).State != EncounterRecord.Fighting) WriteEncounter(m, new EncounterRecord { State = EncounterRecord.Fighting, Time = MechClock.Now, Before = "" }, "set");
                    ClearEncounter(m, "the set command");
                    return true;
                default: why = "an encounter's states: 0 idle, 1 fighting, 2 cleared"; return false;
            }
        }

        // ---- spawning

        /// <summary>The players inside the dungeon (one more melee a wave for each past the first).</summary>
        private int PlayersInside()
        {
            int n = Player.GetAllPlayers().Count(p => p != null && !p.IsDead() && Site.Contains(p.transform.position));
            if (ZNet.instance != null && ZNet.instance.IsServer())
                n = Math.Max(n, ZNet.instance.GetPeers().Count(peer => peer != null && Site.Contains(peer.m_refPos)) + (Player.m_localPlayer != null && Site.Contains(Player.m_localPlayer.transform.position) ? 1 : 0));
            return Math.Max(1, n);
        }

        private void SpawnWave(Mech m, int wave)
        {
            EncounterDef e = m.Def.Encounter;
            if (e.Waves.Count == 0 || e.Points.Count == 0 || Site.Built == null) return;
            Dictionary<string, int> roles = e.Waves[Mathf.Clamp(wave, 0, e.Waves.Count - 1)];
            m.EncAlive = SpawnRoles(m, roles, PlayersInside() - 1, $"wave {wave + 1}");
            m.EncDeadAt = -1f;
        }

        /// <summary>Creatures of these roles at the encounter's points (extra: that many more melee ones); how many came.</summary>
        private int SpawnRoles(Mech m, Dictionary<string, int> roles, int extra, string what)
        {
            EncounterDef e = m.Def.Encounter;
            if (e.Points.Count == 0 || Site.Built == null) return 0;
            var rng = new System.Random(unchecked(Site.Seed * 31 + m.Key.GetStableHashCode() * 17 + what.GetStableHashCode() + (int)(MechClock.Now * 10)));
            int cap = Builder.LevelCapOf(Site.Built), made = 0, point = rng.Next(e.Points.Count);
            var list = new List<string>();
            foreach (KeyValuePair<string, int> kv in roles.Concat(new[] { new KeyValuePair<string, int>("melee", Math.Max(0, extra)) }))
            {
                for (int i = 0; i < kv.Value; i++)
                {
                    bool elite = kv.Key == "elite";
                    GameObject prefab = Builder.EncounterCreature(Site.Built, kv.Key, rng, out bool elitePicked);
                    if (prefab == null) continue;
                    int level = Mathf.Clamp(elite ? e.EliteLevel : e.Level, 1, cap);
                    Vector3 at = RoomPoint(m, e.Points[point++ % e.Points.Count]) + Vector3.up * 0.2f;
                    if (Spawn(m, prefab, at, level) != null) { made++; list.Add(prefab.name + (level > 1 ? $"*{level - 1}" : "")); }
                }
            }
            Plugin.Log.LogInfo($"Dungeon {Site.Id}: encounter {m.Key} {what}: {string.Join(", ", list)}");
            return made;
        }

        // ---- the boss

        private static readonly int BossTag = "quaddungeon_boss".GetStableHashCode();     // string on the boss's ZDO: its name (a $ key)
        private static readonly int BossX = "quaddungeon_bossx".GetStableHashCode();      // float: it takes 1/this of each hit

        /// <summary>The boss: the land's elite (or its role) at the level cap, at the first point, made a boss on every game (Character.m_boss:
        /// the game's boss bar with its name, for everyone near) and harder to kill (Dungeons_BossDamage).</summary>
        private void SpawnBoss(Mech m)
        {
            BossDef b = m.Def.Encounter.Boss;
            if (Site.Built == null || m.Def.Encounter.Points.Count == 0) { WarnOnce($"Encounter {m.Key}: a boss with no points"); return; }
            var rng = new System.Random(unchecked(Site.Seed * 31 + m.Key.GetStableHashCode()));
            GameObject prefab = Builder.EncounterCreature(Site.Built, string.IsNullOrEmpty(b.Role) ? "elite" : b.Role, rng, out _);
            if (prefab == null) { WarnOnce($"Encounter {m.Key}: no creature fits its boss here"); return; }
            Vector3 at = RoomPoint(m, m.Def.Encounter.Points[0]) + Vector3.up * 0.2f;
            GameObject go = Spawn(m, prefab, at, Builder.LevelCapOf(Site.Built));
            if (go == null) return;
            ZDO z = go.GetComponent<ZNetView>()?.GetZDO();
            z?.Set(BossTag, string.IsNullOrEmpty(b.Name) ? prefab.GetComponent<Character>()?.m_name ?? "" : b.Name);
            z?.Set(BossX, Mathf.Max(1f, b.Health));
            MakeBoss(go.GetComponent<Character>(), z);
            Plugin.Log.LogInfo($"Dungeon {Site.Id}: encounter {m.Key}: its boss is {prefab.name} ({b.Name}, x{b.Health:0.#} health)");
        }

        /// <summary>The owner: the boss's trophy (Builder's reward item) where it fell (or at its point after a relog), a saved item.</summary>
        private void DropTrophy(Mech m)
        {
            GameObject trophy = Builder.RewardItem(Builder.TrophyItem);
            if (trophy == null) { WarnOnce("The boss's trophy item isn't registered: no trophy"); return; }
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

        private bool IsBoss(Character c)
        {
            ZDO z = c.GetComponent<ZNetView>()?.GetZDO();
            return z != null && !string.IsNullOrEmpty(z.GetString(BossTag));
        }

        /// <summary>The boss's fight, each tick: its phases as its health falls, cleared when it's dead.</summary>
        private void TickBoss(Mech m, EncounterRecord r, List<Character> alive)
        {
            BossDef b = m.Def.Encounter.Boss;
            Character boss = alive.FirstOrDefault(IsBoss);
            if (boss == null)
            {
                if (r.Kills <= 0) return;
                DropTrophy(m);
                ClearEncounter(m, "its boss is dead");
                return;
            }
            m.BossLast = boss.transform.position;
            float health = boss.GetHealthPercentage();
            while (r.Wave < b.Phases.Count && health <= b.Phases[r.Wave].At)
            {
                BossPhase phase = b.Phases[r.Wave];
                var pending = new Dictionary<Mech, int>();
                int Cur(Mech x) => pending.TryGetValue(x, out int s) ? s : StateOf(x);
                if (phase.Wires.Count > 0) ApplyWires(m, pending, Cur, false, true, phase.Wires);
                if (pending.Count > 0) Change(pending.ToList(), $"encounter {m.Key}: its boss's phase {r.Wave + 1}");
                if (phase.Adds.Count > 0) SpawnRoles(m, phase.Adds, 0, $"boss phase {r.Wave + 1}");
                r.Wave++;
                r.Time = MechClock.Now;
                WriteEncounter(m, r, $"its boss at {health:P0}: phase {r.Wave}");
            }
        }

        private GameObject Spawn(Mech m, GameObject prefab, Vector3 at, int level)
        {
            try
            {
                Quaternion turn = Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f);
                GameObject go = Object.Instantiate(prefab, at, turn);
                ZNetView v = go.GetComponent<ZNetView>();
                v?.GetZDO()?.Set(TagHash, TagOf(m));
                Character c = go.GetComponent<Character>();
                if (c != null && level > 1) c.SetLevel(level);
                go.GetComponent<BaseAI>()?.SetHuntPlayer(true);
                PlayAt("vfx_spawn", at);
                return go;
            }
            catch (Exception e) { WarnOnce($"Encounter {m.Key}: spawning {prefab.name}: {e.Message}"); return null; }
        }

        // ---- the owner, every quarter second

        private void EncounterTick()
        {
            if (Time.time < _nextEncounter) return;
            _nextEncounter = Time.time + EncounterEvery;
            foreach (Mech m in All)
            {
                if (m.Def.Encounter == null || m.Base == null) continue;
                try { TickOne(m); }
                catch (Exception e) { WarnOnce($"Encounter {m.Key}: {e.Message}"); }
            }
        }

        private void TickOne(Mech m)
        {
            EncounterDef e = m.Def.Encounter;
            EncounterRecord r = EncounterOf(m);
            if (r.State == EncounterRecord.Cleared) return;
            if (r.State == EncounterRecord.Idle)
            {
                if (e.StartOn == "enter" && PlayersIn(m)) StartEncounter(m, "a player came in");
                else if (e.StartOn == "open" && ChestOpened(m)) StartEncounter(m, "its chest was opened");
                return;
            }

            // fighting: a wipe?
            bool someone = PlayersIn(m);
            if (someone) m.EncEmptySince = -1f;
            else if (m.EncEmptySince < 0f) m.EncEmptySince = Time.time;
            else if (Time.time - m.EncEmptySince > WipeAfter) { ResetEncounter(m, $"nobody in it for {WipeAfter:0} s"); return; }

            if (e.Boss != null) { TickBoss(m, r, Spawned(m)); return; }

            // guard mode: cleared when nothing is alive in the area
            if (e.Waves.Count == 0)
            {
                bool any = Character.GetAllCharacters().Any(c => c != null && !(c is Player) && !c.IsDead() && !c.IsTamed() && InArea(m, c.transform.position));
                if (!any) ClearEncounter(m, "nothing left alive in it");
                return;
            }

            List<Character> alive = Spawned(m);
            if (e.Siege > 0)
            {
                // kills: what was alive and isn't any more
                if (alive.Count < m.EncAlive) { r.Kills += m.EncAlive - alive.Count; WriteEncounter(m, r, $"{r.Kills} of {e.Siege} killed"); }
                m.EncAlive = alive.Count;
                if (r.Kills >= e.Siege) { foreach (Character c in alive) { c.GetComponent<ZNetView>()?.ClaimOwnership(); ZNetScene.instance.Destroy(c.gameObject); } ClearEncounter(m, $"{r.Kills} killed"); return; }
                Mech plate = Sibling(m, e.HoldBy);
                bool held = plate == null || StateOf(plate) > 0;
                int perWave = e.Waves.Count > 0 ? e.Waves[0].Values.Sum() : 3;
                if (held && alive.Count < perWave && Time.time >= m.EncNextSpawn)
                {
                    m.EncNextSpawn = Time.time + Mathf.Max(e.Pause, 2f);
                    SpawnWave(m, r.Wave % Math.Max(1, e.Waves.Count));
                    m.EncAlive += alive.Count;
                    r.Wave++;
                    WriteEncounter(m, r, $"siege wave {r.Wave}");
                }
                return;
            }

            // waves: the next when this one is dead (after Pause), or after Timeout anyway; cleared after the last
            double now = MechClock.Now;
            bool dead = alive.Count == 0;
            if (dead && m.EncDeadAt < 0f) m.EncDeadAt = Time.time;
            bool next = (dead && Time.time - m.EncDeadAt >= e.Pause) || now - r.Time >= e.Timeout;
            if (!next) return;
            if (r.Wave + 1 >= e.Waves.Count)
            {
                if (dead) ClearEncounter(m, "its last wave is dead");
                return;   // (the last wave isn't timed out: it must die)
            }
            r.Wave++;
            r.Time = now;
            WriteEncounter(m, r, $"wave {r.Wave + 1} of {e.Waves.Count}");
            SpawnWave(m, r.Wave);
        }

        private bool ChestOpened(Mech m)
        {
            Vector3 at = RoomPoint(m, m.Def.Encounter.Chest);
            foreach (Container c in Object.FindObjectsByType<Container>(FindObjectsSortMode.None))
            {
                if (c == null || (c.transform.position - at).sqrMagnitude > 4f) continue;
                ZNetView v = c.GetComponent<ZNetView>();
                if (v != null && v.IsValid() && v.GetZDO().GetInt(ZDOVars.s_inUse) == 1) return true;
            }
            return false;
        }

        // ---- every game: what the players see and hear

        /// <summary>A change of an encounter's state, seen on every game from the saved state (the first look, and anything older than 2 s,
        /// is silent): the locks slamming ("dungeons_fight_start"), each wave ("dungeons_fight_wave"), and the clear (the unlock sound at
        /// it and its locks, "dungeons_fight_clear"), told to this game's player when they're in its area.</summary>
        private void EncounterNotices()
        {
            ReadEncounters();
            Player p = Player.m_localPlayer;
            foreach (Mech m in All)
            {
                if (m.Def.Encounter == null) continue;
                EncounterRecord r = EncounterOf(m);
                int seen = r.State * 1000 + r.Wave;
                if (m.EncSeen == seen) continue;
                bool first = m.EncSeen < 0;
                m.EncSeen = seen;
                if (first || MechClock.Now - r.Time > 2.0) continue;
                bool here = p != null && InArea(m, p.transform.position);
                BossDef boss = m.Def.Encounter.Boss;
                string bossName = boss != null && Localization.instance != null ? Localization.instance.Localize(boss.Name ?? "") : "";
                if (r.State == EncounterRecord.Fighting)
                {
                    int waves = Math.Max(1, m.Def.Encounter.Waves.Count);
                    if (boss != null) { if (here && r.Wave == 0) p.Message(MessageHud.MessageType.Center, T("dungeons_boss_awakens", bossName)); }
                    else if (here) p.Message(MessageHud.MessageType.Center, r.Wave == 0 ? T("dungeons_fight_start") : T("dungeons_fight_wave", r.Wave + 1, waves));
                }
                else if (r.State == EncounterRecord.Cleared)
                {
                    PlayAt(UnlockClunk, m.WorldPos);
                    PlayAt(UnlockChime, m.WorldPos);
                    foreach (string id in m.Def.Encounter.Lock) { Mech l = Sibling(m, id); if (l != null) PlayAt(UnlockChime, l.WorldPos); }
                    if (boss != null)
                    {
                        // the vault moment, for everyone inside the dungeon
                        if (p != null && Site.Contains(p.transform.position))
                        {
                            PlayAt(VaultSolved, p.transform.position + Vector3.up);
                            p.Message(MessageHud.MessageType.Center, T("dungeons_boss_defeated", bossName));
                            VaultReward.Give(Site);
                        }
                    }
                    else if (here) p.Message(MessageHud.MessageType.Center, T("dungeons_fight_clear"));
                }
            }
        }

        /// <summary>Not the owner: this game's player is in an idle "enter" encounter's area, or a fighting one's (so a wipe isn't seen
        /// where the owner can't see them).</summary>
        private void TellEncounters(Player p)
        {
            foreach (Mech m in All)
            {
                if (m.Def.Encounter == null || !InArea(m, p.transform.position)) continue;
                int state = EncounterOf(m).State;
                if (state == EncounterRecord.Cleared) continue;
                Site.Act("area:" + m.Key);
            }
        }

        /// <summary>The owner: a player's game says they're in an encounter's area.</summary>
        public void InAreaFrom(long sender, string key)
        {
            if (!Site.IsOwner || !ByKey.TryGetValue(key ?? "", out Mech m) || m.Def.Encounter == null) return;
            m.RemotePress[sender] = Time.time + 2.5f;
        }

        /// <summary>A lever an uncleared encounter keeps locked (its LockLever).</summary>
        public bool LockedLever(Mech lever)
        {
            foreach (Mech m in All)
                if (m.Def.Encounter != null && m.Room == lever.Room && m.Def.Encounter.LockLever == lever.Def.Id && EncounterOf(m).State != EncounterRecord.Cleared)
                    return true;
            return false;
        }
    }
}
