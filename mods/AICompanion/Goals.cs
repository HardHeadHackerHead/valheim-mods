using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace AICompanion
{
    /// <summary>
    /// What it is working toward: its next piece of better gear (an upgrade of something it wears, or something new worth making at a station
    /// near home), what that still needs, and where those things come from. It gathers toward it first (Work prefers the trees, rocks,
    /// plants and animals that drop what is missing), makes the in-between materials itself (bronze from copper and tin, at the forge) and
    /// asks you for what it cannot get (a troll hide, an ore it has no pickaxe for). Without a goal it gathers what it can, as before.
    /// </summary>
    internal class Goal
    {
        public string What = "";                    // "bronze helmet", "bronze axe (level 2)"
        public ItemDrop.ItemData Item;              // upgrading this
        public Recipe Recipe;                       // or making this
        public CraftingStation Station;
        public readonly Dictionary<string, int> Raw = new Dictionary<string, int>();       // item prefab name -> how many to gather
        public readonly Dictionary<string, string> Names = new Dictionary<string, string>(); // item prefab name -> its name in the game
        public readonly List<string> Ask = new List<string>();                           // "2 troll hide" it cannot get itself
        public readonly List<KeyValuePair<Recipe, CraftingStation>> Steps = new List<KeyValuePair<Recipe, CraftingStation>>(); // materials to make on the way
        public readonly HashSet<string> Smelt = new HashSet<string>();                   // bars that need a smelter ("copper")
        public int Cost;                                                                 // how much is missing (to pick the nearest goal)

        public bool Wants(string prefab) => prefab != null && Raw.ContainsKey(prefab);

        /// <summary>"4 copper ore, 2 tin ore"</summary>
        public string RawText() => string.Join(", ", Raw.Select(kv => $"{kv.Value} {Names[kv.Key]}"));
    }

    internal static class Goals
    {
        private class Source { public Job Job; public int Tier; public string Creature; }

        private static Dictionary<string, List<Source>> _sources;   // item prefab name -> what drops it
        private static Dictionary<string, ItemDrop> _smeltedFrom;   // bar prefab name -> the ore a smelter makes it from
        private static Dictionary<string, float> _unfindable;      // the companion being planned for: what is not near its home

        private static readonly HashSet<string> Prey = new HashSet<string> { "Deer", "Boar", "Neck", "Hare" };

        public static void Forget() { _sources = null; _smeltedFrom = null; }

        private static void Learn()
        {
            if (_sources != null || ZNetScene.instance == null) return;
            _sources = new Dictionary<string, List<Source>>();
            _smeltedFrom = new Dictionary<string, ItemDrop>();
            foreach (GameObject prefab in ZNetScene.instance.m_prefabs)
            {
                if (prefab == null) continue;
                TreeBase tree = prefab.GetComponent<TreeBase>();
                if (tree != null) Add(tree.m_dropWhenDestroyed, Job.Wood, tree.m_minToolTier);
                TreeLog log = prefab.GetComponent<TreeLog>();
                if (log != null) Add(log.m_dropWhenDestroyed, Job.Wood, log.m_minToolTier);
                Destructible des = prefab.GetComponent<Destructible>();
                DropTable drops = prefab.GetComponent<DropOnDestroyed>()?.m_dropWhenDestroyed;
                if (des != null && drops != null && prefab.GetComponent<Piece>() == null && prefab.GetComponent<Character>() == null)
                {
                    bool wood = des.m_destructibleType == DestructibleType.Tree || des.m_damages.m_chop != HitData.DamageModifier.Immune && des.m_damages.m_pickaxe == HitData.DamageModifier.Immune;
                    if (wood) Add(drops, Job.Wood, des.m_minToolTier);
                    else if (des.m_damages.m_pickaxe != HitData.DamageModifier.Immune) Add(drops, Work.IsOre(drops) ? Job.Ore : Job.Stone, des.m_minToolTier);
                }
                MineRock rock = prefab.GetComponent<MineRock>();
                if (rock != null) Add(rock.m_dropItems, Work.IsOre(rock.m_dropItems) ? Job.Ore : Job.Stone, rock.m_minToolTier);
                MineRock5 rock5 = prefab.GetComponent<MineRock5>();
                if (rock5 != null) Add(rock5.m_dropItems, Work.IsOre(rock5.m_dropItems) ? Job.Ore : Job.Stone, rock5.m_minToolTier);
                Pickable pick = prefab.GetComponent<Pickable>();
                if (pick != null && pick.m_respawnTimeMinutes > 0f && pick.m_itemPrefab != null) AddOne(pick.m_itemPrefab.name, new Source { Job = Job.Forage });
                CharacterDrop cd = prefab.GetComponent<CharacterDrop>();
                if (cd != null && Prey.Contains(prefab.name))
                    foreach (CharacterDrop.Drop d in cd.m_drops) if (d?.m_prefab != null) AddOne(d.m_prefab.name, new Source { Job = Job.Hunt, Creature = prefab.name });
                Smelter smelter = prefab.GetComponent<Smelter>();
                if (smelter != null)
                    foreach (Smelter.ItemConversion conv in smelter.m_conversion)
                        if (conv?.m_from != null && conv.m_to != null && !_smeltedFrom.ContainsKey(conv.m_to.gameObject.name)) _smeltedFrom[conv.m_to.gameObject.name] = conv.m_from;
            }
        }

        private static void Add(DropTable table, Job job, int tier)
        {
            if (table?.m_drops == null) return;
            foreach (DropTable.DropData d in table.m_drops) if (d.m_item != null) AddOne(d.m_item.name, new Source { Job = job, Tier = tier });
        }

        private static void AddOne(string item, Source s)
        {
            if (!_sources.TryGetValue(item, out List<Source> list)) _sources[item] = list = new List<Source>();
            list.Add(s);
        }

        /// <summary>Can it get this itself, with the tools it has? (an axe or pickaxe of the tier the source needs, a weapon for hunting)</summary>
        private static bool CanGather(Humanoid me, string item)
        {
            if (!_sources.TryGetValue(item, out List<Source> list)) return false;
            if (_unfindable != null && _unfindable.TryGetValue(item, out float until) && until > Time.time) return false; // not near home
            int axe = Work.Axe(me)?.m_shared.m_toolTier ?? -1, pick = Work.Pickaxe(me)?.m_shared.m_toolTier ?? -1;
            bool armed = Companion.BestMelee(me) != null || Companion.BestRanged(me) != null;
            return list.Any(s => s.Job == Job.Forage || (s.Job == Job.Hunt && armed) || (s.Job == Job.Wood && axe >= s.Tier) || ((s.Job == Job.Stone || s.Job == Job.Ore) && pick >= s.Tier));
        }

        /// <summary>The jobs that get what the goal is missing, and the creatures to hunt for it.</summary>
        public static Job JobsFor(Goal g, out HashSet<string> creatures)
        {
            creatures = new HashSet<string>();
            Job jobs = Job.None;
            if (g == null || _sources == null) return jobs;
            foreach (string item in g.Raw.Keys)
                if (_sources.TryGetValue(item, out List<Source> list))
                    foreach (Source s in list) { jobs |= s.Job; if (s.Creature != null) creatures.Add(s.Creature); }
            return jobs;
        }

        private static int Have(Humanoid me, List<Container> chests, ItemDrop item)
        {
            string name = item.m_itemData.m_shared.m_name;
            return me.GetInventory().CountItems(name) + chests.Sum(c => c != null ? c.GetInventory().CountItems(name) : 0);
        }

        /// <summary>
        /// Its next goal, or null when there is nothing to work toward (nothing better to make or upgrade at the stations near home). Goals it
        /// can gather everything for come first, then the cheapest; with only goals it needs help with, the one needing least from you.
        /// </summary>
        public static Goal Pick(Humanoid me, Vector3 center, float radius, BrainState st = null)
        {
            Learn();
            if (_sources == null || ObjectDB.instance == null) return null;
            _unfindable = st?.Unfindable;
            List<CraftingStation> stations = Upgrades.StationsNear(center, radius + 10f);
            List<Container> chests = Home.Chests(me);
            var goals = new List<Goal>();

            foreach (ItemDrop.ItemData item in me.GetInventory().GetAllItems().Where(i => Upgrades.Upgradable(i)))
            {
                Recipe r = ObjectDB.instance.GetRecipe(item);
                if (r == null || r.m_craftingStation == null) continue;
                int next = item.m_quality + 1;
                CraftingStation at = stations.FirstOrDefault(s => s.m_name == r.m_craftingStation.m_name && s.GetLevel() >= r.GetRequiredStationLevel(next));
                if (at == null) continue;
                var g = new Goal { Item = item, Station = at, What = $"{Loc(item.m_shared.m_name)} (level {next})" };
                if (Build(me, chests, stations, g, r, next)) { if (me.IsItemEquiped(item)) g.Cost -= 1; goals.Add(g); }
            }
            foreach (Recipe r in ObjectDB.instance.m_recipes)
            {
                if (r == null || !r.m_enabled || r.m_item == null || !Upgrades.WorthMaking(me, r.m_item.m_itemData)) continue;
                if (r.m_item.m_itemData.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Ammo) continue; // arrows are made when it has the things (Upgrades), never a goal
                CraftingStation at = r.m_craftingStation == null ? null : stations.FirstOrDefault(s => s.m_name == r.m_craftingStation.m_name && s.GetLevel() >= Mathf.Max(1, r.m_minStationLevel));
                if (at == null && r.m_craftingStation != null) continue; // (no station needed: made anywhere)
                var g = new Goal { Recipe = r, Station = at, What = Loc(r.m_item.m_itemData.m_shared.m_name) };
                if (Build(me, chests, stations, g, r, 1)) goals.Add(g);
            }
            return goals.OrderBy(g => g.Ask.Count > 0 ? 1 : 0).ThenBy(g => g.Ask.Count).ThenBy(g => g.Cost).FirstOrDefault();
        }

        /// <summary>Fills in what the goal is missing; false when nothing is (then Upgrades makes it at once) .</summary>
        private static bool Build(Humanoid me, List<Container> chests, List<CraftingStation> stations, Goal g, Recipe r, int quality)
        {
            bool missing = false;
            foreach (Piece.Requirement q in Upgrades.Needs(r))
            {
                if (q.m_resItem == null) continue;
                int need = q.GetAmount(quality) - Have(me, chests, q.m_resItem);
                if (need <= 0) continue;
                missing = true;
                Expand(me, chests, stations, g, q.m_resItem, need, 0);
            }
            return missing;
        }

        /// <summary>
        /// Where "need" of an item comes from: gathered (a tree, a rock, a plant, an animal it can deal with), smelted from an ore (it gathers
        /// the ore; a smelter makes the bars), or made from other materials at a station near home (bronze); else it has to ask you.
        /// </summary>
        private static void Expand(Humanoid me, List<Container> chests, List<CraftingStation> stations, Goal g, ItemDrop item, int need, int depth)
        {
            string prefab = item.gameObject.name;
            if (depth > 0) need -= Have(me, chests, item);
            if (need <= 0) return;
            g.Cost += need;
            if (CanGather(me, prefab))
            {
                g.Raw[prefab] = (g.Raw.TryGetValue(prefab, out int had) ? had : 0) + need;
                g.Names[prefab] = Loc(item.m_itemData.m_shared.m_name);
                return;
            }
            if (depth < 3 && _smeltedFrom.TryGetValue(prefab, out ItemDrop ore))
            {
                g.Smelt.Add(Loc(item.m_itemData.m_shared.m_name));
                Expand(me, chests, stations, g, ore, need, depth + 1);
                return;
            }
            Recipe made = depth < 3 ? ObjectDB.instance.GetRecipe(item.m_itemData) : null;
            CraftingStation at = made?.m_craftingStation != null ? stations.FirstOrDefault(s => s.m_name == made.m_craftingStation.m_name && s.GetLevel() >= Mathf.Max(1, made.m_minStationLevel)) : null;
            if (made != null && (at != null || made.m_craftingStation == null) && Upgrades.Needs(made).Any())
            {
                int batches = Mathf.CeilToInt(need / (float)Mathf.Max(1, made.m_amount));
                foreach (Piece.Requirement q in Upgrades.Needs(made)) if (q.m_resItem != null) Expand(me, chests, stations, g, q.m_resItem, q.GetAmount(1) * batches, depth + 1);
                if (!g.Steps.Any(s => s.Key == made)) g.Steps.Add(new KeyValuePair<Recipe, CraftingStation>(made, at));
                return;
            }
            g.Ask.Add($"{need} {Loc(item.m_itemData.m_shared.m_name)}");
            g.Cost += 100;
        }

        /// <summary>An in-between material for its goal it can make right now (bronze, when it has the copper and tin), and where.</summary>
        public static KeyValuePair<Recipe, CraftingStation>? StepReady(Humanoid me, Goal g)
        {
            if (g == null) return null;
            foreach (var step in g.Steps)
            {
                List<Container> chests = step.Value == null ? new List<Container>() // made on the spot, from its bag
                    : Home.Chests(me).Where(c => c != null && Vector3.Distance(c.transform.position, step.Value.transform.position) < 25f).ToList(); // as Upgrades.Craft pays
                if (Upgrades.Needs(step.Key).All(q => q.m_resItem == null || Have(me, chests, q.m_resItem) >= q.GetAmount(1))) return step;
            }
            return null;
        }

        /// <summary>
        /// Tell its player about a new goal, and ask for what it cannot get itself (each at most every so often). Called when it picks one.
        /// </summary>
        public static void Announce(BrainState st, Goal g)
        {
            Humanoid me = st.Body;
            if (g == null) return;
            if (st.GoalSaid != g.What)
            {
                st.GoalSaid = g.What;
                string need = g.Raw.Count > 0 ? $" I still need {g.RawText()}." : "";
                string smelt = g.Smelt.Count > 0 ? $" ({string.Join(" and ", g.Smelt)} need{(g.Smelt.Count == 1 ? "s" : "")} a smelter: I'll put the ore in my chest.)" : "";
                Talk.Tell(me, $"I'm working toward a {g.What}.{need}{smelt}", "goal:" + g.What, 30f);
                st.Remember($"working toward a {g.What}");
            }
            if (g.Ask.Count > 0)
                Talk.Tell(me, $"For my {g.What} I need {string.Join(" and ", g.Ask)}, and I can't get that myself. Do you have any? Put it in my chest or give it to me.", "ask:" + g.What, 20f);
        }

        private static string Loc(string s) => Localization.instance.Localize(s).ToLowerInvariant();
    }
}
