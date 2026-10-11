using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Dungeons
{
    // Enemies that fit the land the entrance stands in. The game's rooms bring their own creatures (the Dvergr rooms' Mistlands ticks and
    // seekers killed two players in Mountain gear), so every saved object the builder makes is looked at before it's made:
    // - a creature itself that doesn't fit is swapped for a seeded pick of the same role (melee, ranged, flying) that does; a boss is left out;
    // - a spawner (CreatureSpawner, SpawnArea, TriggerSpawner) keeps its object, and gets a plan saved in its ZDO ("Dungeons_fit"): which
    //   creature each spawns instead and its levels, capped to the tier. The patches below apply it whenever it spawns (the game makes the
    //   spawner from its prefab again after every load, so a field set once wouldn't last). Without the mod, the game's own creatures return;
    // - things that make creatures some other way (eggs that hatch, pots or props that spawn something when broken, offering bowls) are
    //   left out when what they make is stronger than the land (they can't be told otherwise).
    // "Fits": a creature of the land's tier or the one below it, not a boss. The pack's "creatures" lists for the biome come first (PackReader
    // .Creatures, by role), then the table here.
    internal static partial class Builder
    {
        private enum Role { Melee, Ranged, Flying }

        private struct Kind
        {
            public int Tier;
            public Role Role;
            public bool Pick;       // may be picked to replace another (big ones, passive ones, water ones never are)
            public Kind(int tier, Role role, bool pick = true) { Tier = tier; Role = role; Pick = pick; }
        }

        /// <summary>The game's creatures by the land they belong to (1 Meadows, 2 Black Forest, 3 Swamp, 4 Mountain, 5 Plains, 6 Mistlands,
        /// 7 Ashlands, 8 Deep North) and their role. Names this game doesn't have are skipped (logged once); a creature not here gets a tier
        /// from its health and a role from what it carries (TierOfCreature, RoleOf).</summary>
        private static readonly Dictionary<string, Kind> Creatures = new Dictionary<string, Kind>(StringComparer.OrdinalIgnoreCase)
        {
            // Meadows
            ["Greyling"] = new Kind(1, Role.Melee), ["Boar"] = new Kind(1, Role.Melee), ["Neck"] = new Kind(1, Role.Melee),
            ["Skeleton_Meadows_noarcher"] = new Kind(1, Role.Melee), ["Skeleton_Meadows"] = new Kind(1, Role.Ranged),
            ["Deer"] = new Kind(1, Role.Melee, false),
            // Black Forest
            ["Greydwarf"] = new Kind(2, Role.Melee), ["Greydwarf_Elite"] = new Kind(2, Role.Melee), ["Greydwarf_Shaman"] = new Kind(2, Role.Ranged),
            ["Skeleton_NoArcher"] = new Kind(2, Role.Melee), ["Skeleton"] = new Kind(2, Role.Ranged), ["Skeleton_Poison"] = new Kind(2, Role.Melee),
            ["Ghost"] = new Kind(2, Role.Flying), ["Troll"] = new Kind(2, Role.Melee, false), ["Bjorn"] = new Kind(2, Role.Melee, false),
            // Swamp
            ["Draugr"] = new Kind(3, Role.Melee), ["Draugr_Elite"] = new Kind(3, Role.Melee), ["Draugr_Ranged"] = new Kind(3, Role.Ranged),
            ["Skeleton_Swamps_noarcher"] = new Kind(3, Role.Melee), ["Skeleton_Swamps"] = new Kind(3, Role.Ranged),
            ["Blob"] = new Kind(3, Role.Melee), ["BlobElite"] = new Kind(3, Role.Melee), ["Wraith"] = new Kind(3, Role.Flying),
            ["Bat_Swamp"] = new Kind(3, Role.Flying), ["Leech"] = new Kind(3, Role.Melee, false), ["Surtling"] = new Kind(3, Role.Ranged, false),
            ["Abomination"] = new Kind(3, Role.Melee, false),
            // Mountain
            ["Wolf"] = new Kind(4, Role.Melee), ["Ulv"] = new Kind(4, Role.Melee), ["Fenring"] = new Kind(4, Role.Melee),
            ["Skeleton_Mountains_noarcher"] = new Kind(4, Role.Melee), ["Skeleton_Mountains"] = new Kind(4, Role.Ranged),
            ["Fenring_Cultist"] = new Kind(4, Role.Ranged), ["Bat"] = new Kind(4, Role.Flying), ["Hatchling"] = new Kind(4, Role.Flying),
            ["StoneGolem"] = new Kind(4, Role.Melee, false),
            // Plains
            ["Goblin"] = new Kind(5, Role.Melee), ["GoblinBrute"] = new Kind(5, Role.Melee), ["GoblinArcher"] = new Kind(5, Role.Ranged),
            ["GoblinShaman"] = new Kind(5, Role.Ranged), ["Deathsquito"] = new Kind(5, Role.Flying), ["Lox"] = new Kind(5, Role.Melee, false),
            ["Unbjorn"] = new Kind(5, Role.Melee, false),
            // Mistlands
            ["Seeker"] = new Kind(6, Role.Melee), ["SeekerBrute"] = new Kind(6, Role.Melee), ["SeekerBrood"] = new Kind(6, Role.Melee),
            ["Tick"] = new Kind(6, Role.Melee), ["Gjall"] = new Kind(6, Role.Flying, false), ["Hare"] = new Kind(6, Role.Melee, false),
            ["Dverger"] = new Kind(6, Role.Melee, false), ["DvergerMage"] = new Kind(6, Role.Ranged, false),
            // Ashlands
            ["Charred_Melee"] = new Kind(7, Role.Melee), ["Charred_Twitcher"] = new Kind(7, Role.Melee), ["Charred_Archer"] = new Kind(7, Role.Ranged),
            ["Charred_Mage"] = new Kind(7, Role.Ranged), ["Asksvin"] = new Kind(7, Role.Melee), ["BlobLava"] = new Kind(7, Role.Melee),
            ["Volture"] = new Kind(7, Role.Flying), ["Morgen"] = new Kind(7, Role.Melee, false), ["FallenValkyrie"] = new Kind(7, Role.Flying, false),
            // Deep North (the pack lists name more; until the game has them, the Ashlands' stand in)
            ["Skeleton_DeepNorth"] = new Kind(8, Role.Melee), ["GoblinDeepNorth"] = new Kind(8, Role.Melee), ["Greydwarf_Frozen"] = new Kind(8, Role.Melee),
            ["Greydwarf_Shaman_Frozen"] = new Kind(8, Role.Ranged),
        };

        /// <summary>The land's tier: Meadows 1 ... Deep North 8 (the sea counts as the Black Forest's: nothing builds there).</summary>
        internal static int TierOf(Heightmap.Biome biome)
        {
            switch (biome)
            {
                case Heightmap.Biome.Meadows: return 1;
                case Heightmap.Biome.BlackForest: return 2;
                case Heightmap.Biome.Ocean: return 2;
                case Heightmap.Biome.Swamp: return 3;
                case Heightmap.Biome.Mountain: return 4;
                case Heightmap.Biome.Plains: return 5;
                case Heightmap.Biome.Mistlands: return 6;
                case Heightmap.Biome.AshLands: return 7;
                case Heightmap.Biome.DeepNorth: return 8;
                default: return 1;
            }
        }

        /// <summary>The highest level a dungeon's creatures may have in a land of this tier (the game's 1 = no star): one star up to the
        /// Black Forest, two after.</summary>
        private static int LevelCap(int tier) => tier <= 2 ? 2 : 3;

        /// <summary>One SpawnNetworked call's view of the land: the biome under the entrance, its tier, the pack, a seed, and what was changed
        /// (for the log).</summary>
        internal class CreatureFit
        {
            public Heightmap.Biome Biome;
            public int Tier;
            public Pack Pack;
            public int Seed;
            public readonly List<string> Notes = new List<string>();
            public readonly List<string> LootNotes = new List<string>();                          // (BuildLoot.cs)
            public readonly Dictionary<string, int> Filtered = new Dictionary<string, int>();    // objects whose drops were kept to the tier
        }

        private static CreatureFit FitFor(Built built)
        {
            Heightmap.Biome biome = WorldGenerator.instance != null ? WorldGenerator.instance.GetBiome(built.Origin.x, built.Origin.z) : Heightmap.Biome.Meadows;
            return new CreatureFit { Biome = biome, Tier = TierOf(biome), Pack = built.Layout.Pack, Seed = built.Layout.Seed };
        }

        // ---- making one saved object, fitted

        /// <summary>
        /// Instantiates source (a game room's networked child, or a Saved part's prefab) at the place as a saved object, fitted to the land
        /// (its creatures here, its loot in BuildLoot.cs; lootTier: which chest list fills a chest in it): null when it's left out.
        /// replaced: the object made isn't a copy of source (a creature or a pickable swapped for another).
        /// </summary>
        private static GameObject MakeFitting(GameObject source, Vector3 at, Quaternion turn, CreatureFit fit, int room, int ordinal, string lootTier,
                                              out bool replaced)
        {
            replaced = false;
            string name = PrefabName(source);
            var rng = new System.Random(unchecked(fit.Seed * 486187739 + room * 7919 + ordinal * 104729 + 17));

            // a creature standing there
            if (IsCreature(source, out Character self))
            {
                if (Fits(source, fit) && !PackPrefersOther(source, fit)) return Make(source, at, turn);   // (what it drops when killed is its own land's)
                if (self.m_boss) { fit.Notes.Add($"{room}: {name} left out (a boss)"); return null; }
                GameObject pick = Pick(RoleOf(source), fit, rng);
                if (pick == null) { fit.Notes.Add($"{room}: {name} left out (nothing of its kind fits)"); return null; }
                fit.Notes.Add($"{room}: {name} -> {pick.name}");
                replaced = true;
                return Make(pick, at, turn);
            }

            // things that make creatures in their own way, which can't be told to make another
            foreach (GameObject made in Hatches(source))
                foreach (GameObject creature in CreaturesFrom(made, 2))
                    if (TooStrong(creature, fit))
                    {
                        fit.Notes.Add($"{room}: {name} left out (it makes {creature.name}, {TierOfCreature(creature)} > {fit.Tier})");
                        return null;
                    }

            // a pickable or an item on the floor above the land's tier: swapped or left out
            switch (CheckPickable(source, fit, rng, $"{room}: {name}", out GameObject swapTo))
            {
                case LootCall.LeaveOut: return null;
                case LootCall.Swap:
                    replaced = true;
                    GameObject other = Make(swapTo, at, turn);
                    FitLoot(other, fit, lootTier, rng, $"{room}: {swapTo.name}");
                    return other;
            }

            // spawners: a plan worked out on the source, saved on the copy
            string plan = SpawnerPlan(source, fit, rng, $"{room}: {name}", out bool dead);
            if (dead) { fit.Notes.Add($"{room}: {name} left out (nothing it spawns fits)"); return null; }
            GameObject go = Make(source, at, turn);
            if (plan != null)
            {
                go.GetComponent<ZNetView>()?.GetZDO()?.Set(FitKey, plan);
                foreach (Component c in Spawners(go)) ApplyFit(c);   // (its Awake has run: the fields now, the ZDO for every load after)
            }
            FitLoot(go, fit, lootTier, rng, $"{room}: {name}");   // (a chest filled, its drops kept to the tier)
            return go;
        }

        private static GameObject Make(GameObject prefab, Vector3 at, Quaternion turn)
        {
            GameObject go = Object.Instantiate(prefab, at, turn);
            go.transform.SetPositionAndRotation(at, turn);
            return go;
        }

        // ---- what a thing is

        internal static string PrefabName(GameObject go)
        {
            if (go == null) return "";
            string n = go.name;
            int cut = n.IndexOf('(');
            return (cut > 0 ? n.Substring(0, cut) : n).Trim();
        }

        private static bool IsCreature(GameObject go, out Character c)
        {
            c = go != null ? go.GetComponent<Character>() : null;
            return c != null && go.GetComponent<BaseAI>() != null && !(c is Player);
        }

        private static bool IsCreature(GameObject go) => IsCreature(go, out _);

        /// <summary>A creature's tier: the table's, else from its health (a creature of another mod).</summary>
        private static int TierOfCreature(GameObject prefab)
        {
            if (Creatures.TryGetValue(PrefabName(prefab), out Kind k)) return k.Tier;
            float hp = prefab.GetComponent<Character>()?.m_health ?? 0f;
            return hp <= 25f ? 1 : hp <= 60f ? 2 : hp <= 120f ? 3 : hp <= 220f ? 4 : hp <= 350f ? 5 : hp <= 600f ? 6 : hp <= 1000f ? 7 : 8;
        }

        /// <summary>A creature's role: the table's, else flying if it flies, ranged if anything it can carry shoots, melee otherwise.</summary>
        private static Role RoleOf(GameObject prefab)
        {
            if (Creatures.TryGetValue(PrefabName(prefab), out Kind k)) return k.Role;
            Character c = prefab.GetComponent<Character>();
            if (c != null && c.m_flying) return Role.Flying;
            if (prefab.GetComponent<Humanoid>() is Humanoid h)
            {
                IEnumerable<GameObject> items = (h.m_defaultItems ?? new GameObject[0]).Concat(h.m_randomWeapon ?? new GameObject[0])
                    .Concat((h.m_randomSets ?? new Humanoid.ItemSet[0]).SelectMany(s => s?.m_items ?? new GameObject[0]));
                foreach (GameObject item in items)
                {
                    Attack attack = item != null ? item.GetComponent<ItemDrop>()?.m_itemData?.m_shared?.m_attack : null;
                    if (attack != null && (attack.m_attackType == Attack.AttackType.Projectile || attack.m_attackType == Attack.AttackType.TriggerProjectile))
                        return Role.Ranged;
                }
            }
            return Role.Melee;
        }

        private static string RoleName(Role role) => role == Role.Ranged ? "ranged" : role == Role.Flying ? "flying" : "melee";

        private static bool Boss(GameObject creature) => creature.GetComponent<Character>()?.m_boss ?? false;

        /// <summary>A creature fits the land: not a boss, of its tier or the one below.</summary>
        private static bool Fits(GameObject creature, CreatureFit fit)
        {
            if (Boss(creature)) return false;
            int t = TierOfCreature(creature);
            return t <= fit.Tier && t >= fit.Tier - 1;
        }

        /// <summary>The pack names its own creatures for this biome and the creature's role, and this one isn't among them: the pack's are
        /// used instead, even for one that would fit (a pack's draugr spawner in a Mountain dungeon becomes the pack's Mountain pick).</summary>
        private static bool PackPrefersOther(GameObject creature, CreatureFit fit)
        {
            List<string> listed = PackReader.Creatures(fit.Pack, fit.Biome.ToString(), RoleName(RoleOf(creature)));
            if (listed == null || listed.Count == 0) return false;
            string name = PrefabName(creature);
            if (listed.Any(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase))) return false;
            // only when one of them exists in this game and isn't too strong (else the creature stays as the fit it is)
            return listed.Any(n => ZNetScene.instance != null && ZNetScene.instance.GetPrefab(n) is GameObject p && IsCreature(p) && !TooStrong(p, fit));
        }

        private static bool TooStrong(GameObject creature, CreatureFit fit) => Boss(creature) || TierOfCreature(creature) > fit.Tier;

        /// <summary>A replacement of this role for the land, picked with rng: from the pack's list for the biome and role (then its "common"
        /// list), keeping only those not stronger than the land; else the table's of the land's tier, or the nearest tier below with one of
        /// the role; else a melee one the same way. Null when the game has none of them.</summary>
        private static GameObject Pick(Role role, CreatureFit fit, System.Random rng)
        {
            if (ZNetScene.instance == null) return null;
            foreach (string key in new[] { RoleName(role), "common" })
            {
                List<GameObject> listed = PackReader.Creatures(fit.Pack, fit.Biome.ToString(), key)
                    .Select(Prefab).Where(p => p != null && IsCreature(p) && !TooStrong(p, fit)).ToList();
                if (listed.Count > 0) return listed[rng.Next(listed.Count)];
            }
            foreach (Role r in role == Role.Melee ? new[] { Role.Melee } : new[] { role, Role.Melee })
                for (int tier = fit.Tier; tier >= 1; tier--)
                {
                    List<GameObject> all = Creatures.Where(kv => kv.Value.Pick && kv.Value.Tier == tier && kv.Value.Role == r)
                        .OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => Prefab(kv.Key)).Where(p => p != null && IsCreature(p)).ToList();
                    if (all.Count > 0) return all[rng.Next(all.Count)];
                }
            return null;
        }

        // ---- for Runtime's encounters (fight rooms that spawn waves)

        /// <summary>
        /// A creature for an encounter in this dungeon, fitted to its land as the spawners' are (Pick): role "melee", "ranged" or "flying"
        /// from the pack's list for the biome and role, then its "common" list, then the table. "elite": the pack's "elite" list for the
        /// biome (elitePicked true); with none, a melee pick (elitePicked false: the caller gives it an elite's level). Never a boss, never
        /// stronger than the land. Null when there's none.
        /// </summary>
        internal static GameObject EncounterCreature(Built built, string role, System.Random rng, out bool elitePicked)
        {
            elitePicked = false;
            if (built?.Layout == null || rng == null) return null;
            CreatureFit fit = FitFor(built);
            if (string.Equals(role, "elite", StringComparison.OrdinalIgnoreCase))
            {
                List<GameObject> elites = PackReader.Creatures(fit.Pack, fit.Biome.ToString(), "elite")
                    .Select(Prefab).Where(p => p != null && IsCreature(p) && !TooStrong(p, fit)).ToList();
                if (elites.Count > 0) { elitePicked = true; return elites[rng.Next(elites.Count)]; }
                return Pick(Role.Melee, fit, rng);
            }
            Role r = string.Equals(role, "ranged", StringComparison.OrdinalIgnoreCase) ? Role.Ranged
                   : string.Equals(role, "flying", StringComparison.OrdinalIgnoreCase) ? Role.Flying : Role.Melee;
            return Pick(r, fit, rng);
        }

        /// <summary>The highest level (the game's: 1 = no star) a creature in this dungeon may have, from its land.</summary>
        internal static int LevelCapOf(Built built) => built?.Layout == null ? LevelCap(1) : LevelCap(FitFor(built).Tier);

        private static GameObject Prefab(string name)
        {
            if (string.IsNullOrEmpty(name) || ZNetScene.instance == null) return null;
            GameObject p = ZNetScene.instance.GetPrefab(name);
            if (p == null) WarnOnce($"Dungeon creatures: this game has no creature called {name}");
            return p;
        }

        /// <summary>What a thing makes in ways that can't be told to make something else: an egg's hatchling, what a prop spawns when hit or
        /// broken, a one-off spawn, an offering bowl's boss, what a young one grows into.</summary>
        private static IEnumerable<GameObject> Hatches(GameObject go)
        {
            foreach (EggHatch e in go.GetComponentsInChildren<EggHatch>(true)) if (e.m_spawnPrefab != null) yield return e.m_spawnPrefab;
            foreach (Destructible d in go.GetComponentsInChildren<Destructible>(true))
            {
                if (d.m_spawnWhenDestroyed != null) yield return d.m_spawnWhenDestroyed;
                if (d.m_spawnWhenDamaged != null) yield return d.m_spawnWhenDamaged;
            }
            foreach (SpawnOnDamaged s in go.GetComponentsInChildren<SpawnOnDamaged>(true)) if (s.m_spawnOnDamage != null) yield return s.m_spawnOnDamage;
            foreach (SpawnPrefab s in go.GetComponentsInChildren<SpawnPrefab>(true)) if (s.m_prefab != null) yield return s.m_prefab;
            foreach (OfferingBowl b in go.GetComponentsInChildren<OfferingBowl>(true)) if (b.m_bossPrefab != null) yield return b.m_bossPrefab;
            foreach (Growup g in go.GetComponentsInChildren<Growup>(true))
            {
                if (g.m_grownPrefab != null) yield return g.m_grownPrefab;
                if (g.m_altGrownPrefabs != null) foreach (Growup.GrownEntry a in g.m_altGrownPrefabs) if (a?.m_prefab != null) yield return a.m_prefab;
            }
        }

        /// <summary>The creatures a prefab is or makes, looking through what it makes (a spawner, an egg) a few steps deep.</summary>
        private static IEnumerable<GameObject> CreaturesFrom(GameObject prefab, int depth)
        {
            if (prefab == null) yield break;
            if (IsCreature(prefab)) { yield return prefab; yield break; }
            if (depth <= 0) yield break;
            var next = new List<GameObject>(Hatches(prefab));
            foreach (CreatureSpawner s in prefab.GetComponentsInChildren<CreatureSpawner>(true)) next.Add(s.m_creaturePrefab);
            foreach (SpawnArea a in prefab.GetComponentsInChildren<SpawnArea>(true)) next.AddRange(a.m_prefabs.Select(p => p?.m_prefab));
            foreach (TriggerSpawner t in prefab.GetComponentsInChildren<TriggerSpawner>(true)) next.AddRange(t.m_creaturePrefabs ?? new GameObject[0]);
            foreach (GameObject n in next)
                foreach (GameObject c in CreaturesFrom(n, depth - 1)) yield return c;
        }

        // ---- spawners: the plan, and applying it

        private static readonly int FitKey = "Dungeons_fit".GetStableHashCode();

        /// <summary>A thing's spawners, in the order its plan lists them: its CreatureSpawners, then SpawnAreas, then TriggerSpawners.</summary>
        private static List<Component> Spawners(GameObject root)
        {
            var list = new List<Component>();
            list.AddRange(root.GetComponentsInChildren<CreatureSpawner>(true));
            list.AddRange(root.GetComponentsInChildren<SpawnArea>(true));
            list.AddRange(root.GetComponentsInChildren<TriggerSpawner>(true));
            return list;
        }

        /// <summary>
        /// The plan for a thing's spawners, or null if it has none: one entry per spawner (Spawners' order) joined by '|'; an entry is
        /// what it spawns, comma separated, each "prefab/minLevel/maxLevel/extra" (extra: a CreatureSpawner's wake-up animation 1/0, a
        /// SpawnArea's weight), "" for a spawner of things that aren't creatures (left as it is), "-" for one that spawns nothing.
        /// Creatures that fit stay; the others are swapped (bosses and those with no pick are dropped). Levels are capped to the land.
        /// dead: every spawner it has ends up spawning nothing (the thing is better left out).
        /// </summary>
        private static string SpawnerPlan(GameObject source, CreatureFit fit, System.Random rng, string label, out bool dead)
        {
            dead = false;
            List<Component> spawners = Spawners(source);
            if (spawners.Count == 0) return null;
            int cap = LevelCap(fit.Tier), live = 0;
            var entries = new List<string>();
            foreach (Component c in spawners)
            {
                var parts = new List<string>();
                bool creatures = false;
                if (c is CreatureSpawner cs)
                {
                    creatures = IsCreature(cs.m_creaturePrefab);
                    GameObject to = creatures ? Fitted(cs.m_creaturePrefab, fit, rng, label) : null;
                    if (to != null)
                    {
                        bool wake = cs.m_wakeUpAnimation && to == cs.m_creaturePrefab;   // (another creature hasn't that animation)
                        parts.Add(Entry(to, cs.m_minLevel, cs.m_maxLevel, cap, wake ? "1" : "0"));
                    }
                }
                else if (c is SpawnArea sa)
                {
                    foreach (SpawnArea.SpawnData d in sa.m_prefabs)
                    {
                        if (d?.m_prefab == null || !IsCreature(d.m_prefab)) continue;
                        creatures = true;
                        GameObject to = Fitted(d.m_prefab, fit, rng, label);
                        if (to != null) parts.Add(Entry(to, d.m_minLevel, d.m_maxLevel, cap, d.m_weight.ToString(CultureInfo.InvariantCulture)));
                    }
                }
                else if (c is TriggerSpawner ts)
                {
                    foreach (GameObject p in ts.m_creaturePrefabs ?? new GameObject[0])
                    {
                        if (!IsCreature(p)) continue;
                        creatures = true;
                        GameObject to = Fitted(p, fit, rng, label);
                        if (to != null) parts.Add(Entry(to, ts.m_minLevel, ts.m_maxLevel, cap, "0"));
                    }
                }
                if (!creatures) { entries.Add(""); live++; continue; }
                if (parts.Count == 0) { entries.Add("-"); continue; }
                entries.Add(string.Join(",", parts));
                live++;
            }
            dead = live == 0;
            return string.Join("|", entries);
        }

        private static string Entry(GameObject prefab, int min, int max, int cap, string extra)
        {
            int hi = Mathf.Clamp(max, 1, cap), lo = Mathf.Clamp(min, 1, hi);
            return $"{PrefabName(prefab)}/{lo}/{hi}/{extra}";
        }

        /// <summary>The creature a spawner should make here: itself when it fits, a pick of its role when it doesn't, null for none.</summary>
        private static GameObject Fitted(GameObject creature, CreatureFit fit, System.Random rng, string label)
        {
            if (Fits(creature, fit) && !PackPrefersOther(creature, fit)) return creature;
            if (Boss(creature)) { fit.Notes.Add($"{label}: {creature.name} dropped (a boss)"); return null; }
            GameObject pick = Pick(RoleOf(creature), fit, rng);
            fit.Notes.Add(pick != null ? $"{label}: {creature.name} -> {pick.name}" : $"{label}: {creature.name} dropped (nothing of its kind fits)");
            return pick;
        }

        /// <summary>
        /// The plan saved on a spawner's object (Dungeons_fit), applied to one of its spawners, just before it spawns (and once when the
        /// builder makes it). False: it must spawn nothing (the patch skips the game's spawn). Spawners without a plan (the game's own
        /// everywhere else) are left alone.
        /// </summary>
        internal static bool ApplyFit(Component spawner)
        {
            if (spawner == null) return true;
            ZNetView view = spawner.GetComponentInParent<ZNetView>();
            ZDO zdo = view != null ? view.GetZDO() : null;
            string plan = zdo?.GetString(FitKey, "");
            if (string.IsNullOrEmpty(plan)) return true;
            int index = Spawners(view.gameObject).IndexOf(spawner);
            string[] entries = plan.Split('|');
            if (index < 0 || index >= entries.Length || entries[index].Length == 0) return true;
            if (entries[index] == "-") return false;

            var made = new List<(GameObject prefab, int min, int max, string extra)>();
            foreach (string e in entries[index].Split(','))
            {
                string[] f = e.Split('/');
                if (f.Length < 4 || ZNetScene.instance == null) continue;
                GameObject prefab = ZNetScene.instance.GetPrefab(f[0]);
                if (prefab == null) continue;
                int.TryParse(f[1], out int min);
                int.TryParse(f[2], out int max);
                made.Add((prefab, Mathf.Max(1, min), Mathf.Max(1, max), f[3]));
            }
            if (made.Count == 0) return false;   // (never fall back to what it spawned before)

            switch (spawner)
            {
                case CreatureSpawner cs:
                    cs.m_creaturePrefab = made[0].prefab;
                    cs.m_minLevel = made[0].min;
                    cs.m_maxLevel = made[0].max;
                    if (made[0].extra == "0") cs.m_wakeUpAnimation = false;
                    break;
                case SpawnArea sa:
                    sa.m_prefabs = made.Select(m => new SpawnArea.SpawnData
                    {
                        m_prefab = m.prefab,
                        m_minLevel = m.min,
                        m_maxLevel = m.max,
                        m_weight = float.TryParse(m.extra, NumberStyles.Float, CultureInfo.InvariantCulture, out float w) ? w : 1f,
                    }).ToList();
                    break;
                case TriggerSpawner ts:
                    ts.m_creaturePrefabs = made.Select(m => m.prefab).ToArray();
                    ts.m_minLevel = made.Min(m => m.min);
                    ts.m_maxLevel = made.Max(m => m.max);
                    break;
            }
            return true;
        }
    }

    // The fitted spawners' plans, applied whenever they spawn (Builder.ApplyFit): only objects with a "Dungeons_fit" plan are changed, and
    // only a plan that says "nothing" skips the game's spawn.
    [HarmonyPatch(typeof(CreatureSpawner), "Spawn")]
    internal static class Dungeons_CreatureSpawner_Spawn
    {
        private static bool Prefix(CreatureSpawner __instance) => Builder.ApplyFit(__instance);
    }

    [HarmonyPatch(typeof(SpawnArea), "SpawnOne")]
    internal static class Dungeons_SpawnArea_SpawnOne
    {
        private static bool Prefix(SpawnArea __instance) => Builder.ApplyFit(__instance);
    }

    [HarmonyPatch(typeof(TriggerSpawner), "Spawn")]
    internal static class Dungeons_TriggerSpawner_Spawn
    {
        private static bool Prefix(TriggerSpawner __instance) => Builder.ApplyFit(__instance);
    }
}
