using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Dungeons
{
    // Loot that fits the land the entrance stands in, as BuildCreatures.cs does for enemies (the game's Dvergr rooms handed out Mistlands
    // things in a Mountain dungeon). Every saved object the builder makes is looked at:
    // - a chest is emptied and filled again from the pack's "loot" for the biome (vault, rare or common), else from the table here;
    // - a pickable whose item is above the land's tier is swapped for a pickable that fits, or left out; a random one (the game's treasure
    //   piles) gets one of its own items that fits, saved as the game saves its choice; an item lying on the floor above the tier is left out;
    // - anything that gives items when broken or taken down (pots, rocks, the game rooms' pieces: black marble, Yggdrasil wood) keeps only
    //   what fits: a tier saved in its ZDO ("Dungeons_loot"), applied whenever the object is made (the ZNetView.Awake patch below), takes
    //   the rest out of its drop tables and its building costs.
    // The shells keep nothing that gives items (StripLoot). An item not in the table counts as above every tier.
    internal static partial class Builder
    {
        private struct Item
        {
            public int Tier;
            public int Min, Max;    // a stack in a chest (0: never put in chests by the table)
            public Item(int tier, int min = 0, int max = 0) { Tier = tier; Min = min; Max = max; }
        }

        private const int UnknownTier = 99;

        /// <summary>The game's items by the land they belong to (1 Meadows ... 7 Ashlands; nothing is Deep North's yet), and how many of each a
        /// chest may hold when the table fills it (none: it's only here so its tier is known).</summary>
        private static readonly Dictionary<string, Item> Items = new Dictionary<string, Item>(StringComparer.OrdinalIgnoreCase)
        {
            // Meadows
            ["Coins"] = new Item(1, 8, 25), ["Amber"] = new Item(1, 1, 2), ["Resin"] = new Item(1, 3, 8), ["Flint"] = new Item(1, 2, 5),
            ["LeatherScraps"] = new Item(1, 2, 5), ["DeerHide"] = new Item(1, 1, 3), ["Feathers"] = new Item(1, 3, 8), ["Honey"] = new Item(1, 1, 3),
            ["ArrowWood"] = new Item(1, 10, 20), ["Wood"] = new Item(1), ["Stone"] = new Item(1), ["Raspberry"] = new Item(1), ["Mushroom"] = new Item(1),
            ["Dandelion"] = new Item(1), ["RawMeat"] = new Item(1), ["NeckTail"] = new Item(1), ["HardAntler"] = new Item(1), ["Torch"] = new Item(1),
            ["QueenBee"] = new Item(1), ["BeechSeeds"] = new Item(1), ["FishRaw"] = new Item(1),
            // Black Forest
            ["AmberPearl"] = new Item(2, 1, 2), ["Ruby"] = new Item(2, 1, 1), ["BoneFragments"] = new Item(2, 2, 6), ["TinOre"] = new Item(2, 2, 4),
            ["CopperOre"] = new Item(2, 2, 4), ["GreydwarfEye"] = new Item(2, 2, 4), ["SurtlingCore"] = new Item(2, 1, 2), ["ArrowFlint"] = new Item(2, 10, 20),
            ["MushroomYellow"] = new Item(2, 1, 3), ["TrollHide"] = new Item(2, 1, 1), ["Copper"] = new Item(2), ["Tin"] = new Item(2),
            ["Bronze"] = new Item(2), ["BronzeNails"] = new Item(2), ["FineWood"] = new Item(2), ["RoundLog"] = new Item(2), ["Blueberries"] = new Item(2),
            ["Carrot"] = new Item(2), ["CarrotSeeds"] = new Item(2), ["Thistle"] = new Item(2), ["ArrowBronze"] = new Item(2), ["AncientSeed"] = new Item(2),
            ["PineCone"] = new Item(2), ["FirCone"] = new Item(2),
            // Swamp
            ["SilverNecklace"] = new Item(3, 1, 1), ["Chain"] = new Item(3, 1, 2), ["WitheredBone"] = new Item(3, 1, 3), ["IronScrap"] = new Item(3, 2, 5),
            ["Bloodbag"] = new Item(3, 1, 3), ["Entrails"] = new Item(3, 2, 4), ["Ooze"] = new Item(3, 1, 3), ["ArrowIron"] = new Item(3, 10, 20),
            ["Iron"] = new Item(3), ["IronNails"] = new Item(3), ["ElderBark"] = new Item(3), ["Guck"] = new Item(3), ["Turnip"] = new Item(3),
            ["TurnipSeeds"] = new Item(3), ["ArrowPoison"] = new Item(3), ["ArrowFire"] = new Item(3), ["Root"] = new Item(3),
            // Mountain
            ["SilverOre"] = new Item(4, 2, 4), ["Obsidian"] = new Item(4, 2, 5), ["WolfPelt"] = new Item(4, 1, 2), ["WolfFang"] = new Item(4, 1, 3),
            ["Crystal"] = new Item(4, 1, 3), ["FreezeGland"] = new Item(4, 1, 3), ["ArrowSilver"] = new Item(4, 10, 20), ["Silver"] = new Item(4),
            ["Onion"] = new Item(4), ["OnionSeeds"] = new Item(4), ["ArrowObsidian"] = new Item(4), ["ArrowFrost"] = new Item(4), ["JuteRed"] = new Item(4),
            ["WolfMeat"] = new Item(4),
            // Plains
            ["BlackMetalScrap"] = new Item(5, 2, 4), ["Barley"] = new Item(5, 2, 5), ["Flax"] = new Item(5, 2, 5), ["Needle"] = new Item(5, 2, 5),
            ["LoxPelt"] = new Item(5, 1, 2), ["Tar"] = new Item(5, 2, 4), ["ArrowNeedle"] = new Item(5, 10, 20), ["GoblinTotem"] = new Item(5, 1, 1),
            ["BlackMetal"] = new Item(5), ["LinenThread"] = new Item(5), ["Cloudberry"] = new Item(5), ["BarleyFlour"] = new Item(5),
            ["LoxMeat"] = new Item(5),
            // Mistlands
            ["Softtissue"] = new Item(6, 2, 4), ["Carapace"] = new Item(6, 2, 4), ["Sap"] = new Item(6, 1, 3), ["RoyalJelly"] = new Item(6, 2, 4),
            ["MushroomJotunPuffs"] = new Item(6, 2, 4), ["MushroomMagecap"] = new Item(6, 2, 4), ["Mandible"] = new Item(6, 1, 2),
            ["ScaleHide"] = new Item(6, 1, 2), ["BlackCore"] = new Item(6, 1, 1), ["DvergrKeyFragment"] = new Item(6, 1, 1), ["ArrowCarapace"] = new Item(6, 10, 20),
            ["BlackMarble"] = new Item(6), ["Eitr"] = new Item(6), ["YggdrasilWood"] = new Item(6), ["DvergrNeedle"] = new Item(6), ["Wisp"] = new Item(6),
            ["BugMeat"] = new Item(6), ["GiantBloodSack"] = new Item(6), ["Bilebag"] = new Item(6), ["JuteBlue"] = new Item(6), ["HareMeat"] = new Item(6),
            // Ashlands
            ["FlametalOreNew"] = new Item(7, 2, 4), ["AskHide"] = new Item(7, 1, 2), ["CharredBone"] = new Item(7, 2, 4), ["CelestialFeather"] = new Item(7, 1, 2),
            ["MoltenCore"] = new Item(7, 1, 1), ["ProustitePowder"] = new Item(7, 1, 3), ["SulfurStone"] = new Item(7, 2, 4), ["CharcoalResin"] = new Item(7, 2, 4),
            ["Bellfragment"] = new Item(7, 1, 1), ["MushroomSmokePuff"] = new Item(7, 2, 4), ["ArrowCharred"] = new Item(7, 10, 20),
            ["GemstoneBlue"] = new Item(7, 1, 1), ["GemstoneGreen"] = new Item(7, 1, 1), ["GemstoneRed"] = new Item(7, 1, 1),
            ["Grausten"] = new Item(7), ["FlametalOre"] = new Item(7), ["Flametal"] = new Item(7), ["FlametalNew"] = new Item(7), ["AskBladder"] = new Item(7),
            ["Blackwood"] = new Item(7), ["MorgenHeart"] = new Item(7), ["MorgenSinew"] = new Item(7), ["FiddleheadFern"] = new Item(7),
            ["Vineberry"] = new Item(7), ["Pukeberries"] = new Item(7),
        };

        /// <summary>Pickables a pickable above the tier may be swapped for (the game's own; those this game lacks are skipped).</summary>
        private static readonly string[] Pickables =
        {
            "Pickable_Mushroom", "Pickable_Flint", "Pickable_Stone", "Pickable_Branch", "Pickable_DolmenTreasure",
            "Pickable_Mushroom_yellow", "Pickable_ForestCryptRandom", "Pickable_ForestCryptRemains01", "Pickable_SurtlingCoreStand", "Pickable_Thistle",
            "Pickable_SunkenCryptRandom", "Pickable_MountainCaveCrystal", "Pickable_MountainCaveRandom", "Pickable_Obsidian",
            "Pickable_RoyalJelly", "Pickable_BlackCoreStand", "Pickable_DvergrMineTreasure", "Pickable_Mushroom_JotunPuffs", "Pickable_Mushroom_Magecap",
            "Pickable_Fiddlehead", "Pickable_SmokePuff",
        };

        private static readonly int LootKey = "Dungeons_loot".GetStableHashCode();

        /// <summary>An item's land tier: the table's; our own items (rewards, the packs' keys: "quaddungeon_...") fit every land; any other
        /// item the table doesn't know, above every land.</summary>
        private static int ItemTier(GameObject item)
        {
            if (item == null) return 0;
            string name = PrefabName(item);
            if (name.StartsWith("quaddungeon_", StringComparison.Ordinal)) return 0;
            return Items.TryGetValue(name, out Item i) ? i.Tier : UnknownTier;
        }

        /// <summary>Which chest list a room's chests are filled from: "boss" in a room tagged boss (the warden's trophy too), "vault" in a
        /// room tagged vault or end, "rare" for our rooms' own chests,
        /// "common" for the game rooms'.</summary>
        internal static string LootTierOf(PlacedRoom room)
        {
            List<string> tags = room?.Def?.Tags;
            if (tags != null && tags.Any(t => string.Equals(t, "boss", StringComparison.OrdinalIgnoreCase))) return "boss";
            if (tags != null && tags.Any(t => string.Equals(t, "vault", StringComparison.OrdinalIgnoreCase) || string.Equals(t, "end", StringComparison.OrdinalIgnoreCase)))
                return "vault";
            return string.IsNullOrEmpty(room?.Def?.GamePrefab) ? "rare" : "common";
        }

        // ---- what a thing gives

        /// <summary>Every item a thing can give: when picked, when broken (and what it leaves when broken), when taken down, its honey, what
        /// it spawns as loot. Not a chest's contents (those are filled here).</summary>
        private static IEnumerable<GameObject> Gives(GameObject go, int depth = 2)
        {
            if (go == null) yield break;
            ItemDrop self = go.GetComponent<ItemDrop>();
            if (self != null && go.GetComponent<Character>() == null) yield return go;
            foreach (Pickable p in go.GetComponentsInChildren<Pickable>(true))
            {
                if (p.m_itemPrefab != null) yield return p.m_itemPrefab;
                foreach (GameObject g in Drops(p.m_extraDrops)) yield return g;
            }
            foreach (PickableItem p in go.GetComponentsInChildren<PickableItem>(true))
            {
                if (p.m_itemPrefab != null) yield return p.m_itemPrefab.gameObject;
                foreach (PickableItem.RandomItem r in p.m_randomItemPrefabs ?? new PickableItem.RandomItem[0]) if (r.m_itemPrefab != null) yield return r.m_itemPrefab.gameObject;
            }
            foreach (DropOnDestroyed d in go.GetComponentsInChildren<DropOnDestroyed>(true)) foreach (GameObject g in Drops(d.m_dropWhenDestroyed)) yield return g;
            foreach (MineRock m in go.GetComponentsInChildren<MineRock>(true)) foreach (GameObject g in Drops(m.m_dropItems)) yield return g;
            foreach (MineRock5 m in go.GetComponentsInChildren<MineRock5>(true)) foreach (GameObject g in Drops(m.m_dropItems)) yield return g;
            foreach (LootSpawner l in go.GetComponentsInChildren<LootSpawner>(true)) foreach (GameObject g in Drops(l.m_items)) yield return g;
            foreach (Beehive b in go.GetComponentsInChildren<Beehive>(true)) if (b.m_honeyItem != null) yield return b.m_honeyItem.gameObject;
            foreach (Piece piece in go.GetComponentsInChildren<Piece>(true))
                foreach (Piece.Requirement r in piece.m_resources ?? new Piece.Requirement[0])
                    if (r?.m_resItem != null && r.m_recover) yield return r.m_resItem.gameObject;
            if (depth <= 0) yield break;
            foreach (Destructible d in go.GetComponentsInChildren<Destructible>(true))
            {
                foreach (GameObject g in Gives(d.m_spawnWhenDestroyed, depth - 1)) yield return g;
                foreach (GameObject g in Gives(d.m_spawnWhenDamaged, depth - 1)) yield return g;
            }
        }

        private static IEnumerable<GameObject> Drops(DropTable t) =>
            t?.m_drops == null ? Enumerable.Empty<GameObject>() : t.m_drops.Where(d => d.m_item != null).Select(d => d.m_item);

        private static int TopTier(IEnumerable<GameObject> items) => items.Select(ItemTier).DefaultIfEmpty(0).Max();

        private static string Above(IEnumerable<GameObject> items, int tier) =>
            string.Join(", ", items.Where(i => ItemTier(i) > tier).Select(PrefabName).Distinct().Take(4));

        // ---- before making it: pickables and items on the floor

        private enum LootCall { Keep, Swap, LeaveOut }

        /// <summary>A pickable or a lying item: whether it can be made as it is, must be swapped (swapTo) or left out. Anything else: Keep
        /// (its drops are dealt with after it's made).</summary>
        private static LootCall CheckPickable(GameObject source, CreatureFit fit, System.Random rng, string label, out GameObject swapTo)
        {
            swapTo = null;
            bool pickable = source.GetComponent<Pickable>() != null || source.GetComponent<PickableItem>() != null;
            bool lying = !pickable && source.GetComponent<ItemDrop>() != null && source.GetComponent<Character>() == null;
            if (!pickable && !lying) return LootCall.Keep;
            if (CanGiveFitting(source, fit.Tier)) return LootCall.Keep;
            string above = Above(Gives(source), fit.Tier);
            if (lying) { fit.LootNotes.Add($"{label} left out ({above})"); return LootCall.LeaveOut; }
            swapTo = PickPickable(fit, rng);
            fit.LootNotes.Add(swapTo != null ? $"{label} ({above}) -> {swapTo.name}" : $"{label} left out ({above})");
            return swapTo != null ? LootCall.Swap : LootCall.LeaveOut;
        }

        /// <summary>A pickable can give something that fits: a random one (PickableItem with choices) when one of its choices fits (it gets
        /// that one after it's made: FixPickableItem); any other when everything it gives fits.</summary>
        private static bool CanGiveFitting(GameObject source, int tier)
        {
            PickableItem pi = source.GetComponent<PickableItem>();
            if (pi != null && pi.m_itemPrefab == null && pi.m_randomItemPrefabs != null && pi.m_randomItemPrefabs.Length > 0)
                return pi.m_randomItemPrefabs.Any(r => r.m_itemPrefab != null && ItemTier(r.m_itemPrefab.gameObject) <= tier);
            return TopTier(Gives(source)) <= tier;
        }

        /// <summary>A pickable that fits the land: everything it gives within the tier, and best of the tier or the one below (else anything
        /// lower). Null when the game has none.</summary>
        private static GameObject PickPickable(CreatureFit fit, System.Random rng)
        {
            if (ZNetScene.instance == null) return null;
            var ok = new List<GameObject>();
            var best = new List<GameObject>();
            foreach (string name in Pickables)
            {
                GameObject p = ZNetScene.instance.GetPrefab(name);
                if (p == null) { WarnOnce($"Dungeon loot: this game has no pickable called {name}"); continue; }
                if (!CanGiveFitting(p, fit.Tier)) continue;
                ok.Add(p);
                int top = TopTier(Gives(p).Where(i => ItemTier(i) <= fit.Tier));
                if (top >= fit.Tier - 1) best.Add(p);
            }
            List<GameObject> from = best.Count > 0 ? best : ok;
            return from.Count > 0 ? from[rng.Next(from.Count)] : null;
        }

        // ---- after making it: chests, random pickables, drops

        /// <summary>Everything about loot that's done to a saved object just made: a chest filled, a random pickable's item chosen, and its
        /// drops kept to the land's tier (saved, so it lasts).</summary>
        private static void FitLoot(GameObject go, CreatureFit fit, string lootTier, System.Random rng, string label)
        {
            foreach (Container c in go.GetComponentsInChildren<Container>(true)) Refill(c, fit, lootTier, rng, label);
            PickableItem pi = go.GetComponent<PickableItem>();
            if (pi != null) FixPickableItem(go, pi, fit, rng, label);
            List<GameObject> gives = Gives(go).ToList();
            if (TopTier(gives) <= fit.Tier) return;
            ZDO zdo = go.GetComponent<ZNetView>()?.GetZDO();
            if (zdo == null) return;
            zdo.Set(LootKey, fit.Tier);
            ApplyLoot(go, fit.Tier);
            string what = $"{PrefabName(go)} ({Above(gives, fit.Tier)})";
            fit.Filtered[what] = fit.Filtered.TryGetValue(what, out int n) ? n + 1 : 1;
        }

        /// <summary>
        /// Empties a chest the game just filled (its default items: Container.Awake, on the owner, which this is) and fills it from the pack's
        /// loot for the biome and this chest's list (lootTier, then "common"), else from the table: as many stacks as its own default list
        /// would give (2 to 6), two more in a vault. Saved by the container itself (its inventory's change event).
        /// </summary>
        private static void Refill(Container chest, CreatureFit fit, string lootTier, System.Random rng, string label)
        {
            Inventory inv = chest.GetInventory();
            if (inv == null || ZNetScene.instance == null) return;
            bool boss = lootTier == "boss", vault = boss || lootTier == "vault";
            DropTable own = chest.m_defaultItems;
            int count = Mathf.Clamp(own != null ? rng.Next(own.m_dropMin, own.m_dropMax + 1) : 3, 2, 6) + (vault ? 2 : 0);
            count = Mathf.Min(count, inv.GetWidth() * inv.GetHeight());

            List<(GameObject item, int min, int max, float weight)> pool = PackLoot(fit, lootTier);
            bool fromPack = pool.Count > 0;
            if (!fromPack) pool = TableLoot(fit, vault);
            inv.RemoveAll();
            if (pool.Count == 0) { fit.LootNotes.Add($"{label}: chest left empty (no loot fits)"); StampLoot(chest); return; }

            var put = new List<string>();
            float total = pool.Sum(p => p.weight);
            for (int i = 0; i < count; i++)
            {
                double r = rng.NextDouble() * total;
                var pick = pool[pool.Count - 1];
                foreach (var p in pool) { r -= p.weight; if (r <= 0) { pick = p; break; } }
                int amount = rng.Next(pick.min, pick.max + 1);
                if (amount <= 0 || !inv.AddItem(pick.item, amount)) continue;
                put.Add($"{PrefabName(pick.item)} x{amount}");
            }
            // ours, only dungeons give them (BuildRewards.cs): a giant's cogs in every vault, the warden's trophy from a boss
            if (vault) Reward(inv, CogItem, rng.Next(1, 4), put);
            if (boss) Reward(inv, TrophyItem, 1, put);
            fit.LootNotes.Add($"{label}: chest ({lootTier}, {(fromPack ? "the pack's" : "the table's")}): {string.Join(", ", put)}");
            StampLoot(chest);
        }

        /// <summary>A filled chest marked with what it holds as filled (Keys.LootSig = LootSignature, from its saved items, which its own
        /// change event has just written): Runtime's Rebuild.EmptyOut gives out only what players have touched when the chest goes.</summary>
        private static void StampLoot(Container chest)
        {
            ZNetView view = chest.m_rootObjectOverride != null ? chest.m_rootObjectOverride : chest.GetComponent<ZNetView>();
            ZDO zdo = view != null ? view.GetZDO() : null;
            if (zdo != null) zdo.Set(Keys.LootSig, LootSignature(zdo));
        }

        /// <summary>A chest's saved items as one number (a hash of its ZDO's s_items bytes, which the game keeps as a byte array, not a string);
        /// never 0 (0: not stamped). Compare a chest's Keys.LootSig with this to tell untouched loot.</summary>
        internal static int LootSignature(ZDO zdo)
        {
            byte[] items = zdo?.GetByteArray(ZDOVars.s_items);
            if (items == null || items.Length == 0) return 1;
            unchecked
            {
                int h = (int)2166136261;
                foreach (byte b in items) h = (h ^ b) * 16777619;
                return h == 0 ? 1 : h;
            }
        }

        private static void Reward(Inventory inv, string item, int amount, List<string> put)
        {
            GameObject prefab = RewardItem(item);
            if (prefab == null) { WarnOnce($"Dungeon loot: the reward {item} isn't made (its game item is missing)"); return; }
            if (inv.AddItem(prefab, amount)) put.Add($"{item} x{amount}");
        }

        /// <summary>
        /// For Runtime's boss (an encounter's chest): fills a chest the owner has just made as a "boss" chest of this dungeon's land: the
        /// pack's "boss" loot for the biome (then "vault", then "common"), else the table's vault loot, plus 1-3 giant's cogs and the warden's
        /// trophy. Logged like the builder's own chests.
        /// </summary>
        internal static void FillRewardChest(Container chest, Built built, System.Random rng)
        {
            if (chest == null || built?.Layout == null || rng == null) return;
            CreatureFit fit = FitFor(built);
            Refill(chest, fit, "boss", rng, "boss chest");
            Plugin.Log.LogInfo($"Dungeon {built.Layout.Pack?.Name} {built.Layout.Seed}: {string.Join("; ", fit.LootNotes)}");
        }

        /// <summary>The pack's loot for the biome and list (then "common"): its entries "Prefab", "Prefab:n" or "Prefab:min-max", without
        /// items the table knows to be above the land's tier (an item the table doesn't know is the pack's word).</summary>
        private static List<(GameObject, int, int, float)> PackLoot(CreatureFit fit, string lootTier)
        {
            var pool = new List<(GameObject, int, int, float)>();
            foreach (string key in new[] { lootTier, lootTier == "boss" ? "vault" : "common", "common" }.Distinct())
            {
                foreach (string entry in PackReader.Loot(fit.Pack, fit.Biome.ToString(), key) ?? new List<string>())
                {
                    string[] f = (entry ?? "").Split(':');
                    GameObject item = ZNetScene.instance.GetPrefab(f[0].Trim());
                    if (item == null || item.GetComponent<ItemDrop>() == null) { WarnOnce($"Pack {fit.Pack?.Name}: loot \"{entry}\" isn't an item of this game"); continue; }
                    int tier = ItemTier(item);
                    if (tier != UnknownTier && tier > fit.Tier) continue;
                    int min = 1, max = 1;
                    if (f.Length > 1)
                    {
                        string[] range = f[1].Split('-');
                        int.TryParse(range[0], out min);
                        max = range.Length > 1 && int.TryParse(range[1], out int hi) ? hi : min;
                    }
                    else if (Items.TryGetValue(f[0].Trim(), out Item known) && known.Max > 0) { min = known.Min; max = known.Max; }
                    pool.Add((item, Mathf.Max(1, min), Mathf.Max(1, Mathf.Max(min, max)), 1f));
                }
                if (pool.Count > 0) break;
            }
            return pool;
        }

        /// <summary>The table's chest loot for the land: items of its tier weigh most, the tier below next, older ones little; coins always,
        /// more of them deeper in. A vault leaves out what's two tiers or more below.</summary>
        private static List<(GameObject, int, int, float)> TableLoot(CreatureFit fit, bool vault)
        {
            var pool = new List<(GameObject, int, int, float)>();
            foreach (KeyValuePair<string, Item> kv in Items.OrderBy(kv => kv.Key, StringComparer.Ordinal))
            {
                Item it = kv.Value;
                if (it.Max <= 0 || it.Tier > fit.Tier) continue;
                bool coins = kv.Key == "Coins";
                if (vault && !coins && it.Tier < fit.Tier - 1) continue;
                GameObject item = ZNetScene.instance.GetPrefab(kv.Key);
                if (item == null) { WarnOnce($"Dungeon loot: this game has no item called {kv.Key}"); continue; }
                float weight = coins ? 3f : it.Tier == fit.Tier ? (vault ? 6f : 4f) : it.Tier == fit.Tier - 1 ? 3f : 1f;
                int min = it.Min, max = it.Max;
                if (coins) { float more = 1f + 0.5f * (fit.Tier - 1) + (vault ? 1f : 0f); min = Mathf.RoundToInt(min * more); max = Mathf.RoundToInt(max * more); }
                pool.Add((item, min, max, weight));
            }
            return pool;
        }

        private static readonly System.Reflection.MethodInfo SetupItem = AccessTools.Method(typeof(PickableItem), "SetupItem");

        /// <summary>A random pickable (the game's treasure piles) chose its item as it woke (on the owner: this game) with the game's random:
        /// one above the tier is replaced by one of its own choices that fits, saved where the game keeps its choice.</summary>
        private static void FixPickableItem(GameObject go, PickableItem pi, CreatureFit fit, System.Random rng, string label)
        {
            if (pi.m_itemPrefab == null || ItemTier(pi.m_itemPrefab.gameObject) <= fit.Tier) return;
            List<PickableItem.RandomItem> ok = (pi.m_randomItemPrefabs ?? new PickableItem.RandomItem[0])
                .Where(r => r.m_itemPrefab != null && ItemTier(r.m_itemPrefab.gameObject) <= fit.Tier).ToList();
            ZDO zdo = go.GetComponent<ZNetView>()?.GetZDO();
            if (ok.Count == 0 || zdo == null || ObjectDB.instance == null) return;   // (CanGiveFitting said one fits)
            PickableItem.RandomItem r = ok[rng.Next(ok.Count)];
            int stack = Mathf.Max(1, rng.Next(r.m_stackMin, r.m_stackMax + 1));
            string was = pi.m_itemPrefab.name;
            zdo.Set(ZDOVars.s_itemPrefab, ObjectDB.instance.GetPrefabHash(r.m_itemPrefab.gameObject));
            zdo.Set(ZDOVars.s_itemStack, stack);
            pi.m_itemPrefab = r.m_itemPrefab;
            pi.m_stack = stack;
            try { SetupItem?.Invoke(pi, new object[] { false }); SetupItem?.Invoke(pi, new object[] { true }); }   // (what it shows)
            catch (Exception e) { Plugin.Log.LogWarning($"{label}: showing its new item: {e.Message}"); }
            fit.LootNotes.Add($"{label}: {was} -> {r.m_itemPrefab.name} x{stack}");
        }

        /// <summary>
        /// Keeps a saved object's drops to a tier: drop tables (when broken, mined, picked as extra, spawned as loot) lose the items above it,
        /// a building piece's refund too, and what it leaves behind when broken goes if that would give more. Called when the builder makes
        /// it and whenever the game makes it again (the patch below): the fields come from the prefab each time.
        /// </summary>
        internal static void ApplyLoot(GameObject root, int tier)
        {
            foreach (DropOnDestroyed d in root.GetComponentsInChildren<DropOnDestroyed>(true)) d.m_dropWhenDestroyed = Filter(d.m_dropWhenDestroyed, tier);
            foreach (MineRock m in root.GetComponentsInChildren<MineRock>(true)) m.m_dropItems = Filter(m.m_dropItems, tier);
            foreach (MineRock5 m in root.GetComponentsInChildren<MineRock5>(true)) m.m_dropItems = Filter(m.m_dropItems, tier);
            foreach (LootSpawner l in root.GetComponentsInChildren<LootSpawner>(true)) l.m_items = Filter(l.m_items, tier);
            foreach (Pickable p in root.GetComponentsInChildren<Pickable>(true)) p.m_extraDrops = Filter(p.m_extraDrops, tier);
            foreach (Piece piece in root.GetComponentsInChildren<Piece>(true))
                if (piece.m_resources != null)
                    piece.m_resources = piece.m_resources.Where(r => r?.m_resItem == null || !r.m_recover || ItemTier(r.m_resItem.gameObject) <= tier).ToArray();
            foreach (Destructible d in root.GetComponentsInChildren<Destructible>(true))
            {
                if (d.m_spawnWhenDestroyed != null && TopTier(Gives(d.m_spawnWhenDestroyed)) > tier) d.m_spawnWhenDestroyed = null;
                if (d.m_spawnWhenDamaged != null && TopTier(Gives(d.m_spawnWhenDamaged)) > tier) d.m_spawnWhenDamaged = null;
            }
        }

        private static DropTable Filter(DropTable t, int tier)
        {
            if (t?.m_drops == null || t.m_drops.All(d => ItemTier(d.m_item) <= tier)) return t;
            DropTable copy = t.Clone();
            copy.m_drops = t.m_drops.Where(d => d.m_item != null && ItemTier(d.m_item) <= tier).ToList();
            return copy;
        }

        /// <summary>A game room's shell keeps nothing that gives items (they're all the networked parts' business; anything without a ZNetView
        /// that could still give one is taken off). How many were.</summary>
        private static int StripLoot(GameObject shell)
        {
            int n = 0;
            foreach (MonoBehaviour mb in shell.GetComponentsInChildren<MonoBehaviour>(false))
            {
                bool gives = mb is Pickable || mb is PickableItem || mb is ItemDrop || mb is Container || mb is Destructible || mb is MineRock
                             || mb is MineRock5 || mb is DropOnDestroyed || mb is LootSpawner || mb is Beehive;
                if (!gives) continue;
                mb.enabled = false;
                Object.Destroy(mb);
                n++;
            }
            return n;
        }
    }

    // A saved object the builder kept to a land's tier (Dungeons_loot in its ZDO) is made from its prefab again after every load: keep its
    // drops to that tier again. Only objects with the key are touched.
    [HarmonyPatch(typeof(ZNetView), "Awake")]
    internal static class Dungeons_ZNetView_Awake_Loot
    {
        private static readonly int Key = "Dungeons_loot".GetStableHashCode();

        private static void Postfix(ZNetView __instance)
        {
            ZDO zdo = __instance.GetZDO();
            if (zdo == null) return;
            int tier = zdo.GetInt(Key, 0);
            if (tier <= 0) return;
            try { Builder.ApplyLoot(__instance.gameObject, tier); }
            catch (Exception e) { Plugin.Log.LogWarning($"Keeping {__instance.name}'s drops to its dungeon's land: {e.Message}"); }
        }
    }
}
