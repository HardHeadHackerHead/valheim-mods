using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace AICompanion
{
    /// <summary>
    /// Living at home while nobody is there. Valheim only runs the world near players, so a companion left at home is frozen while you are
    /// away. Instead of keeping its area running (deep in the game's world code, costly, and where lost-item bugs come from), it catches up
    /// when someone comes back and its area loads: it works out how long it was away (world time) and does that much work at once, on the
    /// real world around it, then tells you what it did:
    ///   * felling trees (a stump is left), mining rocks and ore, picking wild berries and mushrooms, at a player's pace with its tools, as far
    ///     as what is within its radius, its tools' wear, and room in its chests allow; the game's own drop tables decide what it gets;
    ///   * its food burns down over that time and it eats from its bag, as a player does;
    ///   * with encounters on (Companion, WhileAway: Mild), it meets the creatures of its biome now and then, more at night and in harder
    ///     places, fights them off and keeps what they drop (Mild: it never falls while you are away).
    /// One game does this (the one that runs the companion), once per return.
    /// </summary>
    internal static class CatchUp
    {
        public const string LastKey = "dhc_lastsim";
        private const float MinAway = 120f, MaxAway = 3f * 1800f; // at most three in-game days of work at once

        private static readonly Dictionary<Heightmap.Biome, string[]> Creatures = new Dictionary<Heightmap.Biome, string[]>
        {
            [Heightmap.Biome.Meadows] = new[] { "Boar", "Neck", "Greyling" },
            [Heightmap.Biome.BlackForest] = new[] { "Greydwarf", "Greydwarf", "Greydwarf_Elite", "Skeleton" },
            [Heightmap.Biome.Swamp] = new[] { "Draugr", "Leech", "Skeleton_Poison", "Blob" },
            [Heightmap.Biome.Mountain] = new[] { "Wolf", "Fenring", "Hatchling" },
            [Heightmap.Biome.Plains] = new[] { "Goblin", "Deathsquito", "Lox" },
            [Heightmap.Biome.Mistlands] = new[] { "Seeker", "Tick" },
            [Heightmap.Biome.AshLands] = new[] { "Charred_Melee", "Asksvin" },
        };

        private static readonly Dictionary<Heightmap.Biome, float> FightsPerHour = new Dictionary<Heightmap.Biome, float>
        {
            [Heightmap.Biome.Meadows] = 0.4f, [Heightmap.Biome.BlackForest] = 1.2f, [Heightmap.Biome.Swamp] = 2f, [Heightmap.Biome.Mountain] = 1.5f,
            [Heightmap.Biome.Plains] = 2f, [Heightmap.Biome.Mistlands] = 2f, [Heightmap.Biome.AshLands] = 3f,
        };

        /// <summary>Every few seconds while it lives at home on this game: "it was here and working at this world time".</summary>
        public static void Stamp(Humanoid c) => Companion.Zdo(c)?.Set(LastKey, (long)ZNet.instance.GetTimeSeconds());

        /// <summary>The first time this game runs it after it was away (its area just loaded): catch up, then report.</summary>
        public static void OnArrive(BrainState st)
        {
            Humanoid me = st.Body;
            st.CaughtUp = true;
            ZDO z = Companion.Zdo(me);
            long last = z.GetLong(LastKey, 0L);
            Stamp(me);
            if (last <= 0L || Companion.OrderOf(me) != Order.Gather) return;
            float away = (float)(ZNet.instance.GetTimeSeconds() - last);
            if (away < MinAway) return;
            Plugin.Instance.StartCoroutine(Run(st, Mathf.Min(away, MaxAway), away));
        }

        private static IEnumerator Run(BrainState st, float seconds, float away)
        {
            yield return new WaitForSeconds(3f); // let the trees, rocks and chests around it finish loading
            Humanoid me = st.Body;
            if (me == null || me.IsDead()) yield break;
            var got = new Dictionary<string, int>();
            var notes = new List<string>();
            float budget = seconds;
            int felled = 0, mined = 0, picked = 0, fights = 0;

            // Encounters first take some of the time (Mild: it always comes through).
            if (Plugin.WhileAway.Value != AwayMode.Off)
            {
                Heightmap.Biome biome = WorldGenerator.instance != null ? WorldGenerator.instance.GetBiome(me.transform.position) : Heightmap.Biome.Meadows;
                float rate = FightsPerHour.TryGetValue(biome, out float r) ? r : 1f;
                float hours = seconds / (EnvMan.instance != null ? EnvMan.instance.m_dayLengthSec / 24f : 75f); // in-game hours
                int expected = Mathf.Min(8, Mathf.FloorToInt(hours / 24f * rate * 3f + UnityEngine.Random.value)); // a few a day, more in harder places
                for (int i = 0; i < expected; i++)
                {
                    string prefab = Creatures.TryGetValue(biome, out string[] list) ? list[UnityEngine.Random.Range(0, list.Length)] : null;
                    GameObject go = prefab != null ? ZNetScene.instance.GetPrefab(prefab) : null;
                    if (go == null) continue;
                    fights++;
                    budget -= 60f;
                    CharacterDrop drops = go.GetComponent<CharacterDrop>();
                    if (drops != null)
                        foreach (CharacterDrop.Drop d in drops.m_drops)
                            if (d.m_prefab != null && UnityEngine.Random.value <= d.m_chance) Add(got, d.m_prefab, UnityEngine.Random.Range(d.m_amountMin, d.m_amountMax + 1));
                    foreach (ItemDrop.ItemData worn in Companion.Worn(me).Where(w => w.m_shared.m_useDurability))
                        worn.m_durability = Mathf.Max(worn.GetMaxDurability() * 0.1f, worn.m_durability - worn.GetMaxDurability() * 0.03f); // wear, never broken
                    ItemDrop.ItemData weapon = me.GetCurrentWeapon();
                    if (weapon != null) Skill.Raise(me, weapon.m_shared.m_skillType, 3f);
                    string name = Localization.instance.Localize(go.GetComponent<Character>()?.m_name ?? prefab);
                    if (!notes.Contains(name)) notes.Add(name);
                }
            }

            // Then the work, at a player's pace, on what is really there.
            Job jobs = Work.JobsOf(me);
            if (jobs == Job.None) jobs = Work.AutoJobs(me);
            Vector3 center = Work.Center(me);
            float radius = Work.RadiusOf(me);
            ItemDrop.ItemData axe = Work.Axe(me), pick = Work.Pickaxe(me);
            var seen = new HashSet<GameObject>();
            var targets = new List<Component>();
            foreach (Collider col in Physics.OverlapSphere(center, radius, ~0, QueryTriggerInteraction.Collide))
            {
                GameObject go = col.attachedRigidbody != null ? col.attachedRigidbody.gameObject : col.transform.root.gameObject;
                if (!seen.Add(go) || go.GetComponent<Piece>() != null || go.GetComponent<Character>() != null) continue;
                Component t = (Component)go.GetComponent<TreeLog>() ?? (Component)go.GetComponent<TreeBase>() ?? (Component)go.GetComponent<MineRock5>() ?? (Component)go.GetComponent<MineRock>()
                              ?? (Component)go.GetComponent<Destructible>() ?? go.GetComponent<Pickable>();
                if (t != null) targets.Add(t);
            }
            targets = targets.OrderBy(t => Vector3.Distance(t.transform.position, center)).ToList();

            foreach (Component t in targets)
            {
                if (budget <= 0f) break;
                if (Free(me) <= 1) { notes.Add("its bag and chests are full"); break; }
                switch (t)
                {
                    case Pickable p when (jobs & Job.Forage) != 0 && p.m_respawnTimeMinutes > 0f && p.m_itemPrefab != null && !(Companion.Zdo(p)?.GetBool(ZDOVars.s_picked, false) ?? true):
                        Add(got, p.m_itemPrefab, p.m_amount);
                        foreach (GameObject extra in p.m_extraDrops.GetDropList()) Add(got, extra, 1);
                        p.GetComponent<ZNetView>()?.InvokeRPC(ZNetView.Everybody, "RPC_SetPicked", true);
                        budget -= 15f; picked++;
                        break;
                    case TreeLog log when (jobs & Job.Wood) != 0 && axe != null && axe.m_shared.m_toolTier >= log.m_minToolTier:
                        if (!Chop(me, axe, log.m_health, ref budget)) break;
                        AddTable(got, log.m_dropWhenDestroyed); AddSubLogs(got, log.m_subLogPrefab, 3);
                        Remove(log.gameObject); felled++;
                        break;
                    case TreeBase tree when (jobs & Job.Wood) != 0 && axe != null && axe.m_shared.m_toolTier >= tree.m_minToolTier:
                        if (!Chop(me, axe, tree.m_health, ref budget)) break;
                        AddTable(got, tree.m_dropWhenDestroyed);
                        TreeLog fallen = tree.m_logPrefab != null ? tree.m_logPrefab.GetComponent<TreeLog>() : null;
                        if (fallen != null) { AddTable(got, fallen.m_dropWhenDestroyed); AddSubLogs(got, fallen.m_subLogPrefab, 3); budget -= 40f; }
                        if (tree.m_stubPrefab != null) UnityEngine.Object.Instantiate(tree.m_stubPrefab, tree.transform.position, tree.transform.rotation); // the stump it leaves
                        Remove(tree.gameObject); felled++;
                        break;
                    case MineRock5 rock5 when pick != null && pick.m_shared.m_toolTier >= rock5.m_minToolTier && (jobs & (Work.IsOre(rock5.m_dropItems) ? Job.Ore : Job.Stone)) != 0:
                        int areas = (AccessTools.Field(typeof(MineRock5), "m_hitAreas").GetValue(rock5) as IList)?.Count ?? 4;
                        if (!Chop(me, pick, rock5.m_health * areas, ref budget)) break;
                        for (int i = 0; i < areas; i++) AddTable(got, rock5.m_dropItems);
                        Remove(rock5.gameObject); mined++;
                        break;
                    case MineRock rock when pick != null && pick.m_shared.m_toolTier >= rock.m_minToolTier && (jobs & (Work.IsOre(rock.m_dropItems) ? Job.Ore : Job.Stone)) != 0:
                        if (!Chop(me, pick, rock.m_health, ref budget)) break;
                        AddTable(got, rock.m_dropItems);
                        Remove(rock.gameObject); mined++;
                        break;
                    case Destructible des:
                        DropTable table = des.GetComponent<DropOnDestroyed>()?.m_dropWhenDestroyed;
                        if (table == null || table.m_drops.Count == 0) break;
                        bool wood = des.m_destructibleType == DestructibleType.Tree;
                        ItemDrop.ItemData tool = wood ? axe : pick;
                        if (tool == null || tool.m_shared.m_toolTier < des.m_minToolTier) break;
                        if ((jobs & (wood ? Job.Wood : Work.IsOre(table) ? Job.Ore : Job.Stone)) == 0) break;
                        if (!Chop(me, tool, des.m_health, ref budget)) break;
                        AddTable(got, table);
                        Remove(des.gameObject);
                        if (wood) felled++; else mined++;
                        break;
                }
            }

            // Its food burned meanwhile; it ate from its bag.
            Food.PassTime(me, st, seconds);

            int stored = Store(me, got);
            Companion.SaveBag(me); // its tools' and armour's wear
            if (felled + mined + picked + fights == 0) yield break;
            var did = new List<string>();
            if (felled > 0) did.Add($"felled {felled} tree{(felled == 1 ? "" : "s")}");
            if (mined > 0) did.Add($"mined {mined} rock{(mined == 1 ? "" : "s")}");
            if (picked > 0) did.Add($"picked {picked} bush{(picked == 1 ? "" : "es")}");
            if (fights > 0) did.Add($"fought off {fights} creature{(fights == 1 ? "" : "s")} ({string.Join(", ", notes.Where(n => !n.Contains("full")).Take(3))})");
            string items = string.Join(", ", got.OrderByDescending(kv => kv.Value).Take(8).Select(kv => $"{kv.Value} {kv.Key}"));
            string line = $"While you were away ({Mathf.RoundToInt(away / 60f)} min) {Companion.NameOf(me)} {string.Join(", ", did)}" + (items.Length > 0 ? $": {items}" : "") +
                          (stored < got.Values.Sum() ? " (kept the rest in their bag)" : "") + (notes.Any(n => n.Contains("full")) ? ". Their chests are full." : ".");
            st.Remember(line);
            Plugin.Instance?.Note(line);
            DebugLog.Add(new DecisionRecord { When = DateTime.Now, Companion = Companion.NameOf(me), Outcome = "— " + line + " —" });
            if (Companion.Master(me) == Player.m_localPlayer) Player.m_localPlayer.Message(MessageHud.MessageType.Center, line);
        }

        /// <summary>The time and wear of working one tree or rock with this tool (as a player swings it); false when the tool would break.</summary>
        private static bool Chop(Humanoid me, ItemDrop.ItemData tool, float health, ref float budget)
        {
            float perHit = Mathf.Max(1f, (tool.m_shared.m_damages.m_chop + tool.m_shared.m_damages.m_pickaxe) * Mathf.Lerp(0.4f, 1f, Skill.Get(me, tool.m_shared.m_skillType) / 100f));
            int hits = Mathf.CeilToInt(health / perHit);
            float wear = hits * tool.m_shared.m_useDurabilityDrain;
            if (tool.m_shared.m_useDurability && tool.m_durability - wear <= 0f) return false;
            if (tool.m_shared.m_useDurability) tool.m_durability -= wear;
            budget -= hits * 1.3f + 20f; // swings, plus walking and picking up
            for (int i = 0; i < hits; i++) Skill.Raise(me, tool.m_shared.m_skillType, 1f);
            return true;
        }

        private static void Add(Dictionary<string, int> got, GameObject prefab, int amount)
        {
            if (prefab == null || amount <= 0 || prefab.GetComponent<ItemDrop>() == null) return;
            string key = prefab.name;
            got[key] = (got.TryGetValue(key, out int had) ? had : 0) + amount;
        }

        private static void AddTable(Dictionary<string, int> got, DropTable table)
        {
            if (table == null) return;
            foreach (GameObject go in table.GetDropList()) Add(got, go, 1);
        }

        private static void AddSubLogs(Dictionary<string, int> got, GameObject sub, int depth)
        {
            if (sub == null || depth <= 0) return;
            TreeLog log = sub.GetComponent<TreeLog>();
            if (log == null) return;
            AddTable(got, log.m_dropWhenDestroyed);
            AddSubLogs(got, log.m_subLogPrefab, depth - 1);
        }

        private static void Remove(GameObject go)
        {
            ZNetView v = go.GetComponent<ZNetView>();
            if (v == null || !v.IsValid()) return;
            if (!v.IsOwner()) v.ClaimOwnership();
            ZNetScene.instance.Destroy(go);
        }

        private static int Free(Humanoid me) => me.GetInventory().GetEmptySlots() + Home.Chests(me).Sum(c => c.GetInventory().GetEmptySlots());

        /// <summary>Into its chests first (stacking onto what is there), then its bag. Returns how many went into chests. Translated names in the report.</summary>
        private static int Store(Humanoid me, Dictionary<string, int> got)
        {
            int stored = 0;
            var named = new Dictionary<string, int>();
            foreach (var kv in got)
            {
                GameObject prefab = ObjectDB.instance.GetItemPrefab(kv.Key);
                ItemDrop drop = prefab != null ? prefab.GetComponent<ItemDrop>() : null;
                if (drop == null) continue;
                int left = kv.Value;
                foreach (Container chest in Home.Chests(me).Where(c => !c.IsInUse()))
                {
                    if (left <= 0) break;
                    ZNetView v = chest.GetComponent<ZNetView>();
                    if (v != null && !v.IsOwner()) v.ClaimOwnership();
                    int before = chest.GetInventory().CountItems(drop.m_itemData.m_shared.m_name);
                    chest.GetInventory().AddItem(prefab, left);
                    int added = chest.GetInventory().CountItems(drop.m_itemData.m_shared.m_name) - before;
                    left -= added; stored += added;
                }
                if (left > 0) me.GetInventory().AddItem(prefab, left);
                named[Localization.instance.Localize(drop.m_itemData.m_shared.m_name)] = kv.Value;
            }
            got.Clear();
            foreach (var kv in named) got[kv.Key] = kv.Value;
            return stored;
        }
    }

    public enum AwayMode { Off, Mild }
}
