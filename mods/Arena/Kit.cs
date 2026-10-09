using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace Arena
{
    /// <summary>
    /// The arena's armoury and kitchen, for the contests fought with the arena's own steel (the Long Road, today's trial). Walking into the ring
    /// you hand everything you carry to the Arena Master: it is written into your character (its custom data, saved with it) and your bag is
    /// emptied. The armourer then dresses you for the land you fight (the weapon of your choice, its shield, the land's armour, two healing
    /// meads) and the kitchen feeds you three meals. All of it is lent (each item is marked) and taken back when the contest ends.
    ///
    /// Your own things come back when the contest ends: at once if you walk out, or when you rise again if you fell (your tombstone then holds
    /// only what the crowd threw you; the lent things are taken before it is made). Anything that went wrong in between (the game closed
    /// mid-fight, a crash) is put right the next time your character is in the world: lent things go, your own come back.
    /// </summary>
    internal static class Kit
    {
        private const string StowKey = "dh_arena_stowed", LoanKey = "dh_arena_loan";

        internal static readonly string[] Styles = { "Sword", "Axe", "Mace", "Spear", "Bow" };

        // weapon by style and land (Meadows first); ":n" is the quality it is lent at (2 when not given)
        private static readonly string[][] Weapons =
        {
            new[] { "KnifeFlint:3", "SwordBronze", "SwordIron", "SwordSilver", "SwordBlackmetal", "SwordMistwalker", "SwordNiedhogg" },
            new[] { "AxeFlint", "AxeBronze", "AxeIron", "AxeIron:4", "AxeBlackMetal", "AxeJotunBane", "AxeBerzerkr" },
            new[] { "Club:3", "MaceBronze", "MaceIron", "MaceSilver", "MaceNeedle", "MaceNeedle:4", "MaceEldner" },
            new[] { "SpearFlint", "SpearBronze", "SpearElderbark", "SpearWolfFang", "SpearWolfFang:4", "SpearCarapace", "SpearSplitner" },
            new[] { "Bow:3", "BowFineWood", "BowHuntsman", "BowDraugrFang", "BowDraugrFang:4", "BowSpineSnap", "BowAshlands" },
        };
        private static readonly string[] Arrows = { "ArrowFlint", "ArrowBronze", "ArrowIron", "ArrowSilver", "ArrowNeedle", "ArrowCarapace", "ArrowCharred" };
        private static readonly string[] Knives = { "KnifeFlint", "KnifeCopper", "KnifeCopper:4", "KnifeSilver", "KnifeBlackMetal", "KnifeBlackMetal:4", "KnifeSkollAndHati" };
        private static readonly string[] Shields = { "ShieldWood", "ShieldBronzeBuckler", "ShieldBanded", "ShieldSilver", "ShieldBlackmetal", "ShieldCarapace", "ShieldFlametal" };
        private static readonly string[][] Armour =
        {
            new[] { "HelmetLeather", "ArmorLeatherChest", "ArmorLeatherLegs" },
            new[] { "HelmetBronze", "ArmorBronzeChest", "ArmorBronzeLegs" },
            new[] { "HelmetIron", "ArmorIronChest", "ArmorIronLegs" },
            new[] { "HelmetDrake", "ArmorWolfChest", "ArmorWolfLegs" },
            new[] { "HelmetPadded", "ArmorPaddedCuirass", "ArmorPaddedGreaves" },
            new[] { "HelmetCarapace", "ArmorCarapaceChest", "ArmorCarapaceLegs" },
            new[] { "HelmetFlametal", "ArmorFlametalChest", "ArmorFlametalLegs" },
        };
        private static readonly string[] Meads = { "MeadHealthMinor", "MeadHealthMinor", "MeadHealthMinor", "MeadHealthMedium", "MeadHealthMedium", "MeadHealthMajor", "MeadHealthMajor" };

        // ---- the kitchen -------------------------------------------------------------------------------------------------------

        /// <summary>What a meal is for: health, stamina, or both alike.</summary>
        internal enum Role { Health, Stamina, Both }

        // each land's kitchen, best first in each role
        private static readonly (string Prefab, Role Role)[][] Kitchen =
        {
            new[] { ("CookedDeerMeat", Role.Health), ("CookedMeat", Role.Health), ("NeckTailGrilled", Role.Health), ("Honey", Role.Stamina), ("Raspberry", Role.Stamina), ("Mushroom", Role.Both) },
            new[] { ("DeerStew", Role.Health), ("MinceMeatSauce", Role.Health), ("CarrotSoup", Role.Stamina), ("QueensJam", Role.Stamina), ("BoarJerky", Role.Both) },
            new[] { ("Sausages", Role.Health), ("BlackSoup", Role.Health), ("TurnipStew", Role.Stamina), ("ShocklateSmoothie", Role.Stamina), ("BoarJerky", Role.Both) },
            new[] { ("WolfMeatSkewer", Role.Health), ("CookedWolfMeat", Role.Health), ("Eyescream", Role.Stamina), ("OnionSoup", Role.Stamina), ("WolfJerky", Role.Both) },
            new[] { ("LoxPie", Role.Health), ("FishWraps", Role.Health), ("BloodPudding", Role.Stamina), ("Bread", Role.Stamina), ("WolfJerky", Role.Both) },
            new[] { ("MisthareSupreme", Role.Health), ("MeatPlatter", Role.Health), ("HoneyGlazedChicken", Role.Health), ("MushroomOmelette", Role.Stamina), ("Salad", Role.Stamina), ("WolfJerky", Role.Both) },
            new[] { ("PiquantPie", Role.Health), ("MashedMeat", Role.Health), ("FierySvinstew", Role.Health), ("RoastedCrustPie", Role.Stamina), ("ScorchingMedley", Role.Stamina), ("Vineberry", Role.Both) },
        };

        /// <summary>The meals a land's kitchen serves (those the game has).</summary>
        internal static List<(string Prefab, Role Role)> Menu(int land) => Kitchen[Mathf.Clamp(land, 0, Kitchen.Length - 1)].Where(m => Roster.Has(m.Prefab)).ToList();

        /// <summary>A plate of three from a land's menu: as many of each role as the chosen plate (from the first land) had, best first.</summary>
        internal static List<string> Plate(int land, IList<Role> roles)
        {
            List<(string Prefab, Role Role)> menu = Menu(land);
            var plate = new List<string>();
            foreach (Role role in roles)
            {
                string pick = menu.FirstOrDefault(m => m.Role == role && !plate.Contains(m.Prefab)).Prefab
                              ?? menu.FirstOrDefault(m => !plate.Contains(m.Prefab)).Prefab;
                if (pick != null) plate.Add(pick);
            }
            return plate;
        }

        internal static Role RoleOf(int land, string prefab) => Menu(land).FirstOrDefault(m => m.Prefab == prefab).Role;

        /// <summary>The usual plate: two for health, one for stamina.</summary>
        internal static readonly Role[] DefaultRoles = { Role.Health, Role.Health, Role.Stamina };

        // ---- lending ---------------------------------------------------------------------------------------------------------------

        internal static bool IsLoan(ItemDrop.ItemData item) => item != null && item.m_customData != null && item.m_customData.ContainsKey(LoanKey);
        internal static bool Stowed(Player p) => p != null && p.m_customData.ContainsKey(StowKey);

        /// <summary>Hands everything the player carries to the Arena Master. False (and nothing is touched) when something is already held.</summary>
        internal static bool Stow(Player player)
        {
            if (player == null || Stowed(player)) return false;
            Inventory inv = player.GetInventory();
            var pkg = new ZPackage();
            inv.Save(pkg);   // (what was worn is saved as worn: it is put on again when it comes back)
            string saved = pkg.GetBase64();
            player.m_customData[StowKey] = saved;
            Backup(player, saved);
            int count = inv.NrOfItems();
            player.UnequipAllItems();
            inv.RemoveAll();
            Plugin.Log.LogInfo($"Stowed {count} items for the contest");
            return true;
        }

        // A copy of what was stowed, on disk beside the game (the last few kept), should anything ever go wrong with a character's save.
        private static void Backup(Player player, string saved)
        {
            try
            {
                string dir = System.IO.Path.Combine(BepInEx.Paths.CachePath, "Arena", "stowed");
                System.IO.Directory.CreateDirectory(dir);
                string name = string.Concat(player.GetPlayerName().Where(char.IsLetterOrDigit));
                System.IO.File.WriteAllText(System.IO.Path.Combine(dir, $"{name}-{System.DateTime.Now:yyyyMMdd-HHmmss}.txt"), saved);
                foreach (string old in System.IO.Directory.GetFiles(dir, name + "-*.txt").OrderByDescending(f => f).Skip(10)) System.IO.File.Delete(old);
            }
            catch (System.Exception e) { Plugin.Log.LogWarning("Could not write the stowed things' backup: " + e.Message); }
        }

        /// <summary>Checks that a bag survives being stowed and given back, on a copy (the player's own bag is not touched).</summary>
        internal static string SelfTest(Player player)
        {
            Inventory real = player.GetInventory();
            var copy = new Inventory("arena test", null, real.GetWidth(), real.GetHeight());
            foreach (ItemDrop.ItemData item in real.GetAllItems()) copy.AddItem(item.Clone(), item.m_gridPos);
            var pkg = new ZPackage();
            copy.Save(pkg);
            var back = new Inventory("arena test back", null, real.GetWidth(), real.GetHeight());
            back.Load(new ZPackage(pkg.GetBase64()));
            string Key(ItemDrop.ItemData i) => $"{i.m_dropPrefab?.name}x{i.m_stack} q{i.m_quality} v{i.m_variant} at {i.m_gridPos} {(i.m_equipped ? "worn" : "")} {i.m_durability:0} {string.Join(",", i.m_customData.Select(kv => kv.Key + "=" + kv.Value))}";
            var a = real.GetAllItems().Select(Key).OrderBy(k => k).ToList();
            var b = back.GetAllItems().Select(Key).OrderBy(k => k).ToList();
            var lost = a.Except(b).ToList();
            var added = b.Except(a).ToList();
            return $"{a.Count} items carried, {b.Count} after the round trip" + (lost.Count + added.Count == 0 ? ": all the same" : $"; differ: before [{string.Join("; ", lost)}] after [{string.Join("; ", added)}]");
        }

        /// <summary>Dresses the player in the arena's things for a land (taking back what was lent for the last one).</summary>
        internal static void Arm(Player player, int land, int style, bool fists)
        {
            TakeBack(player);
            land = Mathf.Clamp(land, 0, Roster.TierNames.Length - 1);
            style = Mathf.Clamp(style, 0, Styles.Length - 1);
            foreach (string piece in Armour[land]) Lend(player, piece, 1, true);
            if (!fists)
            {
                ItemDrop.ItemData weapon = Lend(player, Weapons[style][land], 1, true);
                bool twoHands = weapon != null && (weapon.m_shared.m_itemType == ItemDrop.ItemData.ItemType.TwoHandedWeapon || weapon.m_shared.m_itemType == ItemDrop.ItemData.ItemType.TwoHandedWeaponLeft
                                                   || weapon.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Bow);
                if (!twoHands) Lend(player, Shields[land], 1, true);
                if (style == 4)
                {
                    Lend(player, Arrows[land], 150, true);
                    Lend(player, Knives[land], 1, false);   // (for when they get close)
                }
            }
            Lend(player, Meads[land], 2, false);
        }

        private static ItemDrop.ItemData Lend(Player player, string entry, int amount, bool wear)
        {
            string prefab = entry;
            int quality = 2;
            int colon = entry.IndexOf(':');
            if (colon > 0) { prefab = entry.Substring(0, colon); int.TryParse(entry.Substring(colon + 1), out quality); }
            GameObject go = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(prefab) : null;
            ItemDrop drop = go != null ? go.GetComponent<ItemDrop>() : null;
            if (drop == null) return null;
            quality = Mathf.Clamp(quality, 1, drop.m_itemData.m_shared.m_maxQuality);
            ItemDrop.ItemData item = player.GetInventory().AddItem(prefab, Mathf.Min(amount, drop.m_itemData.m_shared.m_maxStackSize), quality, 0, 0L, "The Arena", false);
            if (item == null) return null;
            item.m_customData[LoanKey] = "1";
            item.m_durability = item.GetMaxDurability();
            if (wear) player.EquipItem(item, false);
            return item;
        }

        /// <summary>Feeds the player a plate (what they had eaten before is forgotten: the arena's meals are what they fight on).</summary>
        internal static void Feed(Player player, IEnumerable<string> plate)
        {
            player.ClearFood();
            var names = new List<string>();
            foreach (string prefab in plate)
            {
                ItemDrop drop = ZNetScene.instance.GetPrefab(prefab)?.GetComponent<ItemDrop>();
                if (drop == null) continue;
                ItemDrop.ItemData meal = drop.m_itemData.Clone();
                meal.m_dropPrefab = drop.gameObject;
                if (player.EatFood(meal)) names.Add(Localization.instance.Localize(meal.m_shared.m_name));
            }
            if (names.Count > 0) player.Message(MessageHud.MessageType.TopLeft, "The kitchen serves: " + string.Join(", ", names));
        }

        /// <summary>Takes back what the arena lent (everything marked).</summary>
        internal static void TakeBack(Player player)
        {
            if (player == null) return;
            Inventory inv = player.GetInventory();
            foreach (ItemDrop.ItemData item in inv.GetAllItems().Where(IsLoan).ToList())
            {
                if (item.m_equipped) player.UnequipItem(item, false);
                inv.RemoveItem(item);
            }
        }

        /// <summary>
        /// Gives the player their own things back: the lent ones go, what they won or were thrown in the ring is kept (put back in the bag after
        /// their own things, or dropped at their feet if the bag is full), and what they wore is put on again.
        /// </summary>
        internal static void Return(Player player)
        {
            if (player == null || !Stowed(player)) return;
            Inventory inv = player.GetInventory();
            TakeBack(player);
            List<ItemDrop.ItemData> won = inv.GetAllItems().Select(i => i.Clone()).ToList();
            player.UnequipAllItems();
            try
            {
                inv.Load(new ZPackage(player.m_customData[StowKey]));
            }
            catch (System.Exception e)
            {
                // never lose the stowed things: keep them held, and say so
                Plugin.Log.LogError("Could not give back the stowed things (they are still held, and will be tried again): " + e);
                foreach (ItemDrop.ItemData item in won) inv.AddItem(item);
                return;
            }
            player.m_customData.Remove(StowKey);
            foreach (ItemDrop.ItemData item in inv.GetEquippedItems().ToList())
            {
                item.m_equipped = false;   // (EquipItem would take it off again otherwise)
                player.EquipItem(item, false);
            }
            int dropped = 0;
            foreach (ItemDrop.ItemData item in won)
            {
                if (inv.CanAddItem(item) && inv.AddItem(item)) continue;
                ItemDrop.DropItem(item, item.m_stack, player.transform.position + Vector3.up + player.transform.forward * 0.6f, Quaternion.identity);
                dropped++;
            }
            Plugin.Log.LogInfo($"Gave back the stowed things ({inv.NrOfItems()} items now carried{(dropped > 0 ? ", " + dropped + " dropped for want of room" : "")})");
            if (dropped > 0) player.Message(MessageHud.MessageType.Center, "Your bag is full: what you won lies at your feet");
        }

        /// <summary>Puts things right once the player is in the world and no contest is on: lent things go, stowed things come back.</summary>
        internal static void Tick()
        {
            if (Time.time < _nextCheck) return;
            _nextCheck = Time.time + 1f;
            Player p = Player.m_localPlayer;
            if (p == null || p.IsDead() || p.IsTeleporting() || Contest.Active) return;
            if (Stowed(p)) { Return(p); p.Message(MessageHud.MessageType.Center, "The Arena Master gives you back your things"); }
            else if (p.GetInventory().GetAllItems().Any(IsLoan)) TakeBack(p);
        }

        private static float _nextCheck;
    }

    /// <summary>The arena's things stay with you while you fight (dropped in the ring, they would be lost to the next fight anyway).</summary>
    [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.DropItem))]
    internal static class Humanoid_DropItem_Loan
    {
        private static bool Prefix(Humanoid __instance, ItemDrop.ItemData item, ref bool __result)
        {
            if (!Kit.IsLoan(item)) return true;
            __result = false;
            if (__instance == Player.m_localPlayer) Hud.Say("That is the arena's: it stays with you until the fight is over.");
            return false;
        }
    }
}
