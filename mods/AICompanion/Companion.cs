using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace AICompanion
{
    public enum Order { Follow, Stay, Guard, Gather }
    public enum Style { Aggressive, Balanced, Defensive, Passive, Auto }

    /// <summary>
    /// A companion's saved settings, in its ZDO so they travel with it and every player sees the same. Only the ZDO's owner writes them
    /// (the menu claims ownership first), as the game itself does.
    /// </summary>
    internal static class Keys
    {
        public const string Master = "dhc_master", MasterName = "dhc_mastername", Name = "dhc_name", Order = "dhc_order", Post = "dhc_post",
            Style = "dhc_style", Retreat = "dhc_retreat", Potions = "dhc_potions", Protect = "dhc_protect", UseJev = "dhc_usejev", Status = "dhc_status", Id = "dhc_id", Kills = "dhc_kills",
            Jobs = "dhc_jobs", Radius = "dhc_radius", HasBed = "dhc_hasbed", BedPos = "dhc_bedpos", Friends = "dhc_friends";
    }

    internal static class Companion
    {
        private static readonly System.Reflection.FieldInfo HumanoidInventory = AccessTools.Field(typeof(Humanoid), "m_inventory");
        private static readonly AccessTools.FieldRef<Humanoid, ItemDrop.ItemData> Right = AccessTools.FieldRefAccess<Humanoid, ItemDrop.ItemData>("m_rightItem"),
            Left = AccessTools.FieldRefAccess<Humanoid, ItemDrop.ItemData>("m_leftItem"), Helmet = AccessTools.FieldRefAccess<Humanoid, ItemDrop.ItemData>("m_helmetItem"),
            Chest = AccessTools.FieldRefAccess<Humanoid, ItemDrop.ItemData>("m_chestItem"), Legs = AccessTools.FieldRefAccess<Humanoid, ItemDrop.ItemData>("m_legItem"),
            Shoulder = AccessTools.FieldRefAccess<Humanoid, ItemDrop.ItemData>("m_shoulderItem"), Utility = AccessTools.FieldRefAccess<Humanoid, ItemDrop.ItemData>("m_utilityItem"),
            Ammo = AccessTools.FieldRefAccess<Humanoid, ItemDrop.ItemData>("m_ammoItem");

        public static bool Is(Component c)
        {
            if (c == null) return false;
            ZNetView view = c.GetComponent<ZNetView>();
            return view != null && view.IsValid() && view.GetZDO().GetPrefab() == Prefab.Hash;
        }

        public static ZDO Zdo(Component c) { ZNetView v = c != null ? c.GetComponent<ZNetView>() : null; return v != null && v.IsValid() ? v.GetZDO() : null; }

        public static IEnumerable<Humanoid> All() => Character.GetAllCharacters().OfType<Humanoid>().Where(Is);

        /// <summary>Its own lasting id (made by its owner the first time it is asked for).</summary>
        public static long IdOf(Component c)
        {
            ZDO zdo = Zdo(c);
            if (zdo == null) return 0L;
            long id = zdo.GetLong(Keys.Id, 0L);
            if (id == 0L && c.GetComponent<ZNetView>().IsOwner())
            {
                id = ((long)Random.Range(1, int.MaxValue) << 31) | (long)Random.Range(1, int.MaxValue);
                zdo.Set(Keys.Id, id);
            }
            return id != 0L ? id : zdo.m_uid.ID;
        }

        public static long MasterId(Component c) => Zdo(c)?.GetLong(Keys.Master, 0L) ?? 0L;
        public static bool IsMine(Component c, Player p) => p != null && MasterId(c) == p.GetPlayerID();
        public static string NameOf(Component c) { string n = Zdo(c)?.GetString(Keys.Name, ""); return string.IsNullOrEmpty(n) ? "Companion" : n; }
        public static Order OrderOf(Component c) => (Order)(Zdo(c)?.GetInt(Keys.Order, 0) ?? 0);
        /// <summary>The style it fights with right now (what "let it decide" worked out, or the one you picked).</summary>
        public static Style StyleOf(Component c) => c is Humanoid h ? Following.Effective(h) : Chosen(c);
        /// <summary>The style you picked (Auto: let it decide).</summary>
        public static Style Chosen(Component c) => (Style)(Zdo(c)?.GetInt(Keys.Style, (int)Style.Auto) ?? (int)Style.Auto);
        public static int RetreatOf(Component c) => Zdo(c)?.GetInt(Keys.Retreat, 30) ?? 30;
        public static bool Potions(Component c) => Zdo(c)?.GetBool(Keys.Potions, true) ?? true;
        public static bool Protect(Component c) => Zdo(c)?.GetBool(Keys.Protect, true) ?? true;
        public static string StatusOf(Component c) => Zdo(c)?.GetString(Keys.Status, "") ?? "";

        /// <summary>The player it serves, if that player is in the world right now.</summary>
        public static Player Master(Component c)
        {
            long id = MasterId(c);
            if (id == 0L) return null;
            foreach (Player p in Player.GetAllPlayers()) if (p != null && p.GetPlayerID() == id) return p;
            return null;
        }

        /// <summary>The nearest of your companions within range, or null.</summary>
        public static Humanoid MineNear(Player p, float range) =>
            All().Where(c => IsMine(c, p) && Vector3.Distance(c.transform.position, p.transform.position) < range)
                 .OrderBy(c => Vector3.Distance(c.transform.position, p.transform.position)).FirstOrDefault();

        /// <summary>Its owner, or anyone when its owner lets friends give it orders (Orders tab).</summary>
        public static bool CanCommand(Component c, Player p) => IsMine(c, p) || (Zdo(c)?.GetBool(Keys.Friends, false) ?? false);

        /// <summary>Change a setting: take the companion over first (only an owner's writes stick). False if someone has its gear open.</summary>
        public static bool Write(Humanoid c, System.Action<ZDO> change)
        {
            ZNetView view = c != null ? c.GetComponent<ZNetView>() : null;
            if (view == null || !view.IsValid()) return false;
            Container gear = c.GetComponent<Container>();
            if (!view.IsOwner())
            {
                if (gear != null) { if (!Containers.Take(gear)) return false; } // (its bag loaded fresh as it is taken over, before anything is moved)
                else view.ClaimOwnership();
            }
            change(view.GetZDO());
            return true;
        }

        private static float _nextClaim;

        /// <summary>Your companions run on your game while you are near them (their brain, their bag). Never while its gear is open.</summary>
        public static void KeepOwnership(Player p)
        {
            if (Time.time < _nextClaim) return;
            _nextClaim = Time.time + 1f;
            foreach (Humanoid c in All())
            {
                if (!IsMine(c, p)) continue;
                ZNetView view = c.GetComponent<ZNetView>();
                if (view.IsOwner()) continue;
                // Near you it runs on your game. Further away too, when nobody runs it (the game that did has left): else it stood frozen
                // wherever it was (once in the sea, where it had fled).
                bool orphan = !Running(view.GetZDO().GetOwner());
                if (!orphan && Vector3.Distance(c.transform.position, p.transform.position) > 64f) continue;
                Container gear = c.GetComponent<Container>();
                if (gear != null) Containers.Take(gear, orphan); // (not while its gear is open; its bag loaded fresh as it is taken over)
                else view.ClaimOwnership();
            }
        }

        /// <summary>
        /// A game that is in the world right now (it runs what it owns). The host knows every player's game (GetPeer); another player's game
        /// knows only the host's, so it looks for the owner among the players in the world (each one's character carries their game's id).
        /// </summary>
        private static bool Running(long owner)
        {
            if (owner == 0L || ZNet.instance == null) return false;
            if (owner == ZDOMan.GetSessionID() || ZNet.instance.GetPeer(owner) != null) return true;
            foreach (ZNet.PlayerInfo info in ZNet.instance.GetPlayerList()) if (info.m_characterID.UserID == owner) return true;
            return false;
        }

        // ---- summoning -----------------------------------------------------------------------------------

        public static Humanoid Summon(Player p, string name)
        {
            GameObject prefab = Prefab.Get();
            if (prefab == null) { Plugin.Tell("The companion could not be made (see the log)"); return null; }
            Vector3 pos = p.transform.position - p.transform.forward * 2f + p.transform.right * 1f;
            if (ZoneSystem.instance != null && ZoneSystem.instance.GetSolidHeight(pos, out float height)) pos.y = Mathf.Max(pos.y, height);
            GameObject go = Object.Instantiate(prefab, pos, Quaternion.LookRotation(p.transform.forward));
            Humanoid c = go.GetComponent<Humanoid>();
            ZDO zdo = Zdo(c);
            zdo.Set(Keys.Master, p.GetPlayerID());
            zdo.Set(Keys.MasterName, p.GetPlayerName());
            zdo.Set(Keys.Name, string.IsNullOrEmpty(name) ? "Rádvar" : name);
            zdo.Set(Keys.Order, (int)Order.Follow);
            zdo.Set(Keys.Style, (int)Style.Auto);
            zdo.Set(Following.MigratedKey, true);
            zdo.Set(Keys.Retreat, 30);
            zdo.Set(Keys.Potions, true);
            zdo.Set(Keys.Protect, true);
            Dress(c);
            Journal.Started(c);
            Journal.Add(c, $"Joined {p.GetPlayerName()}.");
            p.m_customData["dhc_name"] = NameOf(c);
            Profile.Save(p, Profile.Of(c)); // who it is, so it can wake in its bed after falling
            Plugin.Instance?.Note($"{p.GetPlayerName()} summoned the companion {NameOf(c)} at {pos:F0}");
            return c;
        }

        /// <summary>A look of its own: man or woman, skin and hair colour (the game picks the hair and beard for player-like NPCs).</summary>
        private static void Dress(Humanoid c) => Looks.Randomise(c, true);

        /// <summary>Send it home: only with its gear taken out first, so nothing can be lost.</summary>
        public static bool Dismiss(Humanoid c, out string why)
        {
            Container gear = c.GetComponent<Container>();
            ZNetView view = c.GetComponent<ZNetView>();
            if (gear != null && !Containers.Take(gear)) { why = "Someone has its gear open."; return false; } // (what is really in its bag, before it is checked)
            if (gear != null && gear.GetInventory().NrOfItems() > 0) { why = "Take its gear first (Open its inventory)"; return false; }
            if (!view.IsOwner()) view.ClaimOwnership();
            Plugin.Instance?.Note($"The companion {NameOf(c)} was sent home");
            if (Player.m_localPlayer != null) Profile.Forget(Player.m_localPlayer, IdOf(c));
            view.Destroy();
            why = null;
            return true;
        }

        /// <summary>
        /// Save its bag now. The bag (its gear chest) saves itself when items come and go, but not when an item itself changes: an upgrade, a
        /// repair, wear. Without this those were lost whenever the companion was made again from its save (its area reloading, a new body).
        /// </summary>
        public static void SaveBag(Humanoid c)
        {
            if (c != null && c.GetComponent<ZNetView>() is ZNetView v && v.IsValid() && v.IsOwner()) c.GetInventory().m_onChanged?.Invoke();
        }

        /// <summary>From before 0.22.0: the gear it wore when it fell, kept on its save and its player's character (Profile.Kept).</summary>
        public const string KeptKey = "dhc_kept";

        /// <summary>
        /// It fell (or is sent away for good): everything it has, the gear in its gear slots too, goes into a tombstone where it is, as a
        /// player's does, each item in the cell it had (its gear in the two gear rows). The tombstone is part of the world, so nothing depends
        /// on who is online or on a save that has not happened yet: before 0.22.0 its worn gear was kept on its own save (gone with its body)
        /// and on its player's character (saved only every half hour, and not at all when that player was offline), and was lost when it fell
        /// on another player's game. When it wakes, its player's game takes its gear rows back out of the tombstone (Grave.WearAgain); a
        /// tombstone too far to reach then, it goes back to. Returns how many stacks went into the tombstone.
        /// </summary>
        public static int DropGear(Humanoid c)
        {
            Container gear = c.GetComponent<Container>();
            Inventory inv = gear != null ? gear.GetInventory() : null;
            if (inv == null || inv.NrOfItems() == 0) return 0;
            int had = inv.NrOfItems(), worn = inv.GetAllItems().Count(Gear.InSlot);
            foreach (ItemDrop.ItemData item in inv.GetAllItems()) item.m_equipped = false; // (the game's move leaves equipped items out)

            // A tombstone like a player's: its player can take everything back with one E. The game's own move (MoveInventoryToGrave) makes
            // the tombstone as big as the bag it empties: adding items one by one only fitted the tombstone's own 4 slots, and the rest was lost.
            GameObject scenePlayer = ZNetScene.instance.GetPrefab("Player");
            Player playerPrefab = scenePlayer != null ? scenePlayer.GetComponent<Player>() : null;
            GameObject tombPrefab = playerPrefab != null && playerPrefab.m_tombstone != null ? playerPrefab.m_tombstone : ZNetScene.instance.GetPrefab("CargoCrate");
            GameObject tomb = tombPrefab != null ? Object.Instantiate(tombPrefab, c.GetCenterPoint(), c.transform.rotation) : null;
            Container grave = tomb != null ? tomb.GetComponent<Container>() : null;
            if (grave != null)
            {
                grave.GetInventory().MoveInventoryToGrave(inv);
                tomb.GetComponent<TombStone>()?.Setup(NameOf(c), MasterId(c));
                grave.GetComponent<ZNetView>().GetZDO().Set(Net.CrateKey, NameOf(c));
                grave.GetComponent<ZNetView>().GetZDO().Set(Grave.OfKey, IdOf(c));
            }

            // Anything still in the bag (no grave could be made): on the ground beside it, never lost.
            int dropped = 0;
            foreach (ItemDrop.ItemData item in inv.GetAllItems().ToList())
            {
                ItemDrop.DropItem(item, item.m_stack, c.transform.position + Vector3.up + Random.insideUnitSphere * 0.5f, Quaternion.identity);
                inv.RemoveItem(item);
                dropped++;
            }
            int saved = grave != null ? grave.GetInventory().NrOfItems() : 0;
            Plugin.Instance?.Note($"{NameOf(c)} fell at {c.transform.position:F0}: {saved} of {had} item stacks in their tombstone ({worn} of them its gear)" + (dropped > 0 ? $", {dropped} dropped beside it" : ""));
            if (saved + dropped < had) Plugin.Instance?.Warn($"{had - saved - dropped} item stacks of {NameOf(c)} could not be placed");
            return saved;
        }

        /// <summary>Items in the game's own save format (as a chest keeps them), as text.</summary>
        public static string Pack(IEnumerable<ItemDrop.ItemData> items)
        {
            var temp = new Inventory("kept", null, 8, 4);
            foreach (ItemDrop.ItemData i in items.Take(32)) { ItemDrop.ItemData copy = i.Clone(); copy.m_equipped = false; temp.AddItem(copy); }
            if (temp.NrOfItems() == 0) return "";
            var pkg = new ZPackage();
            temp.Save(pkg);
            return System.Convert.ToBase64String(pkg.GetArray());
        }

        /// <summary>Put packed items back into a companion's bag (it wears them again by itself). How many stacks.</summary>
        public static int Unpack(Humanoid c, string packed)
        {
            if (string.IsNullOrEmpty(packed)) return 0;
            try
            {
                var temp = new Inventory("kept", null, 8, 4);
                temp.Load(new ZPackage(System.Convert.FromBase64String(packed)));
                int n = 0;
                foreach (ItemDrop.ItemData item in temp.GetAllItems().ToList())
                {
                    item.m_equipped = false;
                    if (c.GetInventory().AddItem(item)) n++;
                    else ItemDrop.DropItem(item, item.m_stack, c.transform.position + Vector3.up, Quaternion.identity); // never lost
                }
                SaveBag(c);
                return n;
            }
            catch (System.Exception e) { Plugin.Instance?.Warn("Could not give the companion its gear back: " + e.Message); return 0; }
        }

        // ---- gear ----------------------------------------------------------------------------------------

        /// <summary>The humanoid's own inventory is the gear chest's (called when the chest wakes up), so it wears and wields what you put in.</summary>
        public static void ShareInventory(Container gear)
        {
            Humanoid h = gear.GetComponent<Humanoid>();
            Gear.Grow(gear.GetInventory()); // (one from before its gear slots: two rows more)
            if (h != null && HumanoidInventory != null) HumanoidInventory.SetValue(h, gear.GetInventory());
        }

        /// <summary>Its equipment slots, for the Inventory tab (an empty slot has a null item).</summary>
        public static KeyValuePair<string, ItemDrop.ItemData>[] Slots(Humanoid h) => new[]
        {
            new KeyValuePair<string, ItemDrop.ItemData>("Weapon", Right(h)), new KeyValuePair<string, ItemDrop.ItemData>("Shield", Left(h)),
            new KeyValuePair<string, ItemDrop.ItemData>("Helmet", Helmet(h)), new KeyValuePair<string, ItemDrop.ItemData>("Chest", Chest(h)),
            new KeyValuePair<string, ItemDrop.ItemData>("Legs", Legs(h)), new KeyValuePair<string, ItemDrop.ItemData>("Cape", Shoulder(h)),
            new KeyValuePair<string, ItemDrop.ItemData>("Ammo", Ammo(h)),
        };

        /// <summary>Things worth giving a companion: weapons, armour, shields, arrows and bolts, healing potions.</summary>
        public static bool Useful(Humanoid h, ItemDrop.ItemData i)
        {
            var t = i.m_shared.m_itemType;
            return IsMelee(i) || IsRanged(i) || t == ItemDrop.ItemData.ItemType.Shield || t == ItemDrop.ItemData.ItemType.Helmet || t == ItemDrop.ItemData.ItemType.Chest
                || t == ItemDrop.ItemData.ItemType.Legs || t == ItemDrop.ItemData.ItemType.Shoulder || t == ItemDrop.ItemData.ItemType.Ammo
                || (t == ItemDrop.ItemData.ItemType.Consumable && i.m_shared.m_consumeStatusEffect is SE_Stats se && (se.m_healthOverTime > 0f || se.m_healthUpFront > 0f) && i.m_shared.m_food <= 0f)
                || Food.IsFood(i);
        }

        /// <summary>Move an item from the companion to the player (true if it fit). The companion must be ours and its gear closed.</summary>
        public static bool Take(Humanoid c, Player p, ItemDrop.ItemData item, out string why)
        {
            why = null;
            if (!Write(c, _ => { })) { why = "Someone has its gear open."; return false; }
            Inventory mine = p.GetInventory(), its = c.GetInventory();
            if (!its.ContainsItem(item)) return false;
            if (!mine.CanAddItem(item)) { why = "Your inventory is full."; return false; }
            if (c.IsItemEquiped(item)) c.UnequipItem(item, false);
            item.m_equipped = false;
            mine.MoveItemToThis(its, item);
            Activity.Log(c, $"you took its {Localization.instance.Localize(item.m_shared.m_name)}");
            return true;
        }

        /// <summary>Move an item from the player to the companion (true if it fit): gear into its gear slot, the rest into its bag.</summary>
        public static bool Give(Humanoid c, Player p, ItemDrop.ItemData item, out string why)
        {
            why = null;
            if (Gear.IsGear(item)) return Gear.Put(c, p, item, out why);
            if (!Write(c, _ => { })) { why = "Someone has its gear open."; return false; }
            Inventory mine = p.GetInventory(), its = c.GetInventory();
            if (!mine.ContainsItem(item)) return false;
            if (!its.CanAddItem(item)) { why = $"{NameOf(c)}'s bag is full."; return false; }
            if (p.IsItemEquiped(item)) p.UnequipItem(item, false);
            item.m_equipped = false;
            its.MoveItemToThis(mine, item);
            Activity.Log(c, $"you gave it {Localization.instance.Localize(item.m_shared.m_name)} (into its bag)");
            return true;
        }

        public static IEnumerable<ItemDrop.ItemData> Worn(Humanoid h) =>
            new[] { Right(h), Left(h), Helmet(h), Chest(h), Legs(h), Shoulder(h), Utility(h), Ammo(h) }.Where(i => i != null);

        public static bool IsStaff(ItemDrop.ItemData item) => item != null && item.m_shared.m_attack != null && item.m_shared.m_attack.m_attackType == Attack.AttackType.Projectile
            && string.IsNullOrEmpty(item.m_shared.m_ammoType) && item.m_shared.m_attack.m_attackEitr > 0f && item.m_shared.m_skillType == Skills.SkillType.ElementalMagic;

        public static bool IsRanged(ItemDrop.ItemData item) => IsStaff(item) || item != null && item.m_shared.m_attack != null && item.m_shared.m_attack.m_attackType == Attack.AttackType.Projectile
            && !string.IsNullOrEmpty(item.m_shared.m_ammoType);

        public static bool HasAmmoFor(Humanoid h, ItemDrop.ItemData weapon) => weapon != null && (IsStaff(weapon) ? Eitr.Get(h) >= weapon.m_shared.m_attack.m_attackEitr : h.GetInventory().GetAmmoItem(weapon.m_shared.m_ammoType) != null);

        internal static bool IsMelee(ItemDrop.ItemData item) =>
            item.IsWeapon() && !IsRanged(item) && item.m_shared.m_skillType != Skills.SkillType.Pickaxes && item.m_shared.m_skillType != Skills.SkillType.Unarmed
            && item.m_shared.m_attack != null && item.m_shared.m_attack.m_attackType != Attack.AttackType.Projectile;

        public static ItemDrop.ItemData BestMelee(Humanoid h) => h.GetInventory().GetAllItems().Where(IsMelee).OrderByDescending(i => i.GetDamage().GetTotalDamage()).FirstOrDefault();

        /// <summary>
        /// The melee weapon that does the most harm to this creature, by its resistances and weaknesses (a club for skeletons, which blunt
        /// breaks; never a weapon it shrugs off). Without a target: the hardest-hitting one.
        /// </summary>
        public static ItemDrop.ItemData BestMeleeAgainst(Humanoid h, Character target)
        {
            if (target == null) return BestMelee(h);
            HitData.DamageModifiers mods = target.GetDamageModifiers(null);
            return h.GetInventory().GetAllItems().Where(IsMelee).OrderByDescending(i => Effective(i.GetDamage(), mods)).FirstOrDefault();
        }

        private static float Factor(HitData.DamageModifier m) => m switch
        {
            HitData.DamageModifier.Immune => 0f, HitData.DamageModifier.Ignore => 0f, HitData.DamageModifier.VeryResistant => 0.25f,
            HitData.DamageModifier.Resistant => 0.5f, HitData.DamageModifier.Weak => 1.5f, HitData.DamageModifier.VeryWeak => 2f, _ => 1f,
        };

        private static float Effective(HitData.DamageTypes d, HitData.DamageModifiers m) =>
            d.m_blunt * Factor(m.m_blunt) + d.m_slash * Factor(m.m_slash) + d.m_pierce * Factor(m.m_pierce) + d.m_fire * Factor(m.m_fire)
            + d.m_frost * Factor(m.m_frost) + d.m_lightning * Factor(m.m_lightning) + d.m_poison * Factor(m.m_poison) + d.m_spirit * Factor(m.m_spirit)
            + d.m_chop * Factor(m.m_chop) * 0.2f + d.m_pickaxe * Factor(m.m_pickaxe) * 0.2f;
        public static ItemDrop.ItemData BestRanged(Humanoid h) => h.GetInventory().GetAllItems().Where(i => IsRanged(i) && HasAmmoFor(h, i)).OrderByDescending(i => i.GetDamage().GetTotalDamage()).FirstOrDefault();

        public static List<ItemDrop.ItemData> HealingPotions(Humanoid h) =>
            h.GetInventory().GetAllItems().Where(i => i.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Consumable && i.m_shared.m_consumeStatusEffect is SE_Stats se
                && (se.m_healthOverTime > 0f || se.m_healthUpFront > 0f) && i.m_shared.m_food <= 0f).ToList();

        /// <summary>
        /// Wear the best armour and hold the wanted weapon (and a shield with a one-handed one). Anything worn that has left the inventory (you
        /// took it out) is taken off first. Equipped items are not flagged as equipped in the inventory, so an item you take out never
        /// arrives in yours marked as worn.
        /// </summary>
        public static void Maintain(Humanoid h, bool wantRanged, ItemDrop.ItemData tool = null, Character against = null)
        {
            if (h.InAttack()) return;
            Inventory inv = h.GetInventory();
            if (h.GetComponent<ZNetView>()?.IsOwner() ?? false) Gear.Arrange(h); // the best it has into its gear slots
            foreach (ItemDrop.ItemData worn in Worn(h).ToList())
                if (!inv.ContainsItem(worn)) h.UnequipItem(worn, false);

            List<ItemDrop.ItemData> items = inv.GetAllItems();
            foreach (ItemDrop.ItemData.ItemType type in new[] { ItemDrop.ItemData.ItemType.Helmet, ItemDrop.ItemData.ItemType.Chest, ItemDrop.ItemData.ItemType.Legs, ItemDrop.ItemData.ItemType.Shoulder, ItemDrop.ItemData.ItemType.Utility })
            {
                ItemDrop.ItemData best = items.Where(i => i.m_shared.m_itemType == type && (type != ItemDrop.ItemData.ItemType.Utility || i.m_shared.m_maxStackSize <= 1))
                                              .OrderByDescending(i => i.GetArmor()).ThenByDescending(i => i.m_quality).FirstOrDefault();
                if (best != null) Equip(h, best);
            }

            ItemDrop.ItemData weapon = tool != null && inv.ContainsItem(tool) ? tool : wantRanged ? BestRanged(h) ?? BestMeleeAgainst(h, against) : BestMeleeAgainst(h, against) ?? BestRanged(h);
            if (weapon != null) Equip(h, weapon);
            if (weapon != null && IsRanged(weapon))
            {
                ItemDrop.ItemData ammo = inv.GetAmmoItem(weapon.m_shared.m_ammoType);
                if (ammo != null && ammo.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Ammo) Equip(h, ammo);
            }
            // A shield beside a one-handed weapon, or on its own with bare fists (as a player can).
            if (weapon == null || weapon.m_shared.m_itemType == ItemDrop.ItemData.ItemType.OneHandedWeapon)
            {
                ItemDrop.ItemData shield = items.Where(i => i.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Shield).OrderByDescending(i => i.m_shared.m_blockPower).FirstOrDefault();
                if (shield != null) Equip(h, shield);
            }
        }

        private static void Equip(Humanoid h, ItemDrop.ItemData item)
        {
            if (!h.IsItemEquiped(item)) h.EquipItem(item, false);
            item.m_equipped = false;
        }

        /// <summary>Its shield, if it holds one.</summary>
        public static ItemDrop.ItemData Shield(Humanoid h) { ItemDrop.ItemData l = Left(h); return l != null && l.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Shield ? l : null; }

        /// <summary>Something to block with (a shield, or the weapon itself as players do).</summary>
        public static bool CanBlock(Humanoid h) => Shield(h) != null || (Right(h) != null && !IsRanged(Right(h)));

        /// <summary>The armour it wears (helmet, chest, legs, cape, belt): a weapon's or shield's own armour value is not body armour.</summary>
        public static float Armor(Humanoid h) => Worn(h).Where(i => IsArmour(i)).Sum(i => i.GetArmor());

        private static bool IsArmour(ItemDrop.ItemData i)
        {
            var t = i.m_shared.m_itemType;
            return t == ItemDrop.ItemData.ItemType.Helmet || t == ItemDrop.ItemData.ItemType.Chest || t == ItemDrop.ItemData.ItemType.Legs
                || t == ItemDrop.ItemData.ItemType.Shoulder || t == ItemDrop.ItemData.ItemType.Utility;
        }

        /// <summary>Drink a healing potion if it has one and is not already under one's effect. True if it drank.</summary>
        public static bool Drink(Humanoid h)
        {
            SEMan seman = h.GetSEMan();
            foreach (ItemDrop.ItemData potion in HealingPotions(h))
            {
                StatusEffect se = potion.m_shared.m_consumeStatusEffect;
                if (seman.HaveStatusEffect(se.NameHash()) || (!string.IsNullOrEmpty(se.m_category) && seman.HaveStatusEffectCategory(se.m_category))) continue;
                seman.AddStatusEffect(se, true);
                h.m_consumeItemEffects.Create(h.transform.position, Quaternion.identity);
                h.GetInventory().RemoveOneItem(potion);
                return true;
            }
            return false;
        }
    }
}
