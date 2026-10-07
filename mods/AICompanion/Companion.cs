using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace AICompanion
{
    public enum Order { Follow, Stay, Guard }
    public enum Style { Aggressive, Balanced, Defensive, Passive }

    /// <summary>
    /// A companion's saved settings, in its ZDO so they travel with it and every player sees the same. Only the ZDO's owner writes them
    /// (the menu claims ownership first), as the game itself does.
    /// </summary>
    internal static class Keys
    {
        public const string Master = "dhc_master", MasterName = "dhc_mastername", Name = "dhc_name", Order = "dhc_order", Post = "dhc_post",
            Style = "dhc_style", Retreat = "dhc_retreat", Potions = "dhc_potions", Protect = "dhc_protect", UseJev = "dhc_usejev", Status = "dhc_status";
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

        public static long MasterId(Component c) => Zdo(c)?.GetLong(Keys.Master, 0L) ?? 0L;
        public static bool IsMine(Component c, Player p) => p != null && MasterId(c) == p.GetPlayerID();
        public static string NameOf(Component c) { string n = Zdo(c)?.GetString(Keys.Name, ""); return string.IsNullOrEmpty(n) ? "Companion" : n; }
        public static Order OrderOf(Component c) => (Order)(Zdo(c)?.GetInt(Keys.Order, 0) ?? 0);
        public static Style StyleOf(Component c) => (Style)(Zdo(c)?.GetInt(Keys.Style, 1) ?? 1);
        public static int RetreatOf(Component c) => Zdo(c)?.GetInt(Keys.Retreat, 30) ?? 30;
        public static bool Potions(Component c) => Zdo(c)?.GetBool(Keys.Potions, true) ?? true;
        public static bool Protect(Component c) => Zdo(c)?.GetBool(Keys.Protect, true) ?? true;
        public static bool UsesJev(Component c) => Zdo(c)?.GetBool(Keys.UseJev, true) ?? true;
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

        /// <summary>Change a setting: take the companion over first (only an owner's writes stick). False if someone has its gear open.</summary>
        public static bool Write(Humanoid c, System.Action<ZDO> change)
        {
            ZNetView view = c != null ? c.GetComponent<ZNetView>() : null;
            if (view == null || !view.IsValid()) return false;
            Container gear = c.GetComponent<Container>();
            if (!view.IsOwner())
            {
                if (gear != null && gear.IsInUse()) return false;
                view.ClaimOwnership();
            }
            change(view.GetZDO());
            return true;
        }

        private static float _nextClaim;

        /// <summary>Your companions run on your game while you are near them, so Jev is asked with your key. Never while its gear is open.</summary>
        public static void KeepOwnership(Player p)
        {
            if (Time.time < _nextClaim) return;
            _nextClaim = Time.time + 1f;
            foreach (Humanoid c in All())
            {
                if (!IsMine(c, p)) continue;
                ZNetView view = c.GetComponent<ZNetView>();
                if (view.IsOwner() || Vector3.Distance(c.transform.position, p.transform.position) > 64f) continue;
                Container gear = c.GetComponent<Container>();
                if (gear != null && gear.IsInUse()) continue;
                view.ClaimOwnership();
            }
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
            zdo.Set(Keys.Style, (int)Style.Balanced);
            zdo.Set(Keys.Retreat, 30);
            zdo.Set(Keys.Potions, true);
            zdo.Set(Keys.Protect, true);
            zdo.Set(Keys.UseJev, true);
            Dress(c);
            p.m_customData["dhc_name"] = NameOf(c); // remembered, so a fallen companion comes back with the same name
            Plugin.Instance?.Note($"{p.GetPlayerName()} summoned the companion {NameOf(c)} at {pos:F0}");
            return c;
        }

        /// <summary>A look of its own: man or woman, skin and hair colour (the game picks the hair and beard for player-like NPCs).</summary>
        private static void Dress(Humanoid c)
        {
            VisEquipment vis = c.GetComponent<VisEquipment>();
            if (vis == null) return;
            int model = Random.Range(0, 2);
            vis.SetModel(model);
            float skin = Random.Range(0.45f, 1f);
            vis.SetSkinColor(new Vector3(skin, skin * Random.Range(0.85f, 0.95f), skin * Random.Range(0.7f, 0.85f)));
            Vector3[] hair = { new Vector3(0.95f, 0.8f, 0.5f), new Vector3(0.55f, 0.35f, 0.2f), new Vector3(0.25f, 0.15f, 0.1f), new Vector3(0.75f, 0.35f, 0.15f), new Vector3(0.1f, 0.1f, 0.1f) };
            vis.SetHairColor(hair[Random.Range(0, hair.Length)]);
            if (model == 1) { vis.SetBeardItem(0); Zdo(c).Set(ZDOVars.s_noBeard, true); } // the second model is the woman
        }

        /// <summary>Send it home: only with its gear taken out first, so nothing can be lost.</summary>
        public static bool Dismiss(Humanoid c, out string why)
        {
            Container gear = c.GetComponent<Container>();
            if (gear != null && gear.GetInventory().NrOfItems() > 0) { why = "Take its gear first (Open its inventory)"; return false; }
            ZNetView view = c.GetComponent<ZNetView>();
            if (!view.IsOwner()) view.ClaimOwnership();
            Plugin.Instance?.Note($"The companion {NameOf(c)} was sent home");
            view.Destroy();
            why = null;
            return true;
        }

        /// <summary>It fell: everything it carried goes into a crate where it stood (the game's own floating cargo crate), so nothing is lost.</summary>
        public static void DropGear(Humanoid c)
        {
            Container gear = c.GetComponent<Container>();
            Inventory inv = gear != null ? gear.GetInventory() : null;
            if (inv == null || inv.NrOfItems() == 0) return;
            c.UnequipAllItems();
            GameObject cratePrefab = ZNetScene.instance.GetPrefab("CargoCrate");
            Container crate = cratePrefab != null ? Object.Instantiate(cratePrefab, c.transform.position + Vector3.up * 0.5f, c.transform.rotation).GetComponent<Container>() : null;
            int moved = 0, dropped = 0;
            foreach (ItemDrop.ItemData item in inv.GetAllItems().ToList())
            {
                item.m_equipped = false;
                if (crate != null && crate.GetInventory().AddItem(item)) { moved++; continue; }
                ItemDrop.DropItem(item, item.m_stack, c.transform.position + Vector3.up, Quaternion.identity);
                dropped++;
            }
            inv.RemoveAll();
            Plugin.Instance?.Note($"{NameOf(c)} fell at {c.transform.position:F0}: {moved} item stacks put in a crate, {dropped} dropped on the ground");
        }

        // ---- gear ----------------------------------------------------------------------------------------

        /// <summary>The humanoid's own inventory is the gear chest's (called when the chest wakes up), so it wears and wields what you put in.</summary>
        public static void ShareInventory(Container gear)
        {
            Humanoid h = gear.GetComponent<Humanoid>();
            if (h != null && HumanoidInventory != null) HumanoidInventory.SetValue(h, gear.GetInventory());
        }

        public static IEnumerable<ItemDrop.ItemData> Worn(Humanoid h) =>
            new[] { Right(h), Left(h), Helmet(h), Chest(h), Legs(h), Shoulder(h), Utility(h), Ammo(h) }.Where(i => i != null);

        public static bool IsRanged(ItemDrop.ItemData item) => item != null && item.m_shared.m_attack != null && item.m_shared.m_attack.m_attackType == Attack.AttackType.Projectile
            && !string.IsNullOrEmpty(item.m_shared.m_ammoType);

        public static bool HasAmmoFor(Humanoid h, ItemDrop.ItemData weapon) => weapon != null && h.GetInventory().GetAmmoItem(weapon.m_shared.m_ammoType) != null;

        private static bool IsMelee(ItemDrop.ItemData item) =>
            item.IsWeapon() && !IsRanged(item) && item.m_shared.m_skillType != Skills.SkillType.Pickaxes && item.m_shared.m_skillType != Skills.SkillType.Unarmed
            && item.m_shared.m_attack != null && item.m_shared.m_attack.m_attackType != Attack.AttackType.Projectile;

        public static ItemDrop.ItemData BestMelee(Humanoid h) => h.GetInventory().GetAllItems().Where(IsMelee).OrderByDescending(i => i.GetDamage().GetTotalDamage()).FirstOrDefault();
        public static ItemDrop.ItemData BestRanged(Humanoid h) => h.GetInventory().GetAllItems().Where(i => IsRanged(i) && HasAmmoFor(h, i)).OrderByDescending(i => i.GetDamage().GetTotalDamage()).FirstOrDefault();

        public static List<ItemDrop.ItemData> HealingPotions(Humanoid h) =>
            h.GetInventory().GetAllItems().Where(i => i.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Consumable && i.m_shared.m_consumeStatusEffect is SE_Stats se
                && (se.m_healthOverTime > 0f || se.m_healthUpFront > 0f) && i.m_shared.m_food <= 0f).ToList();

        /// <summary>
        /// Wear the best armour and hold the wanted weapon (and a shield with a one-handed one). Anything worn that has left the inventory (you
        /// took it out) is taken off first. Equipped items are not flagged as equipped in the inventory, so an item you take out never
        /// arrives in yours marked as worn.
        /// </summary>
        public static void Maintain(Humanoid h, bool wantRanged)
        {
            if (h.InAttack()) return;
            Inventory inv = h.GetInventory();
            foreach (ItemDrop.ItemData worn in Worn(h).ToList())
                if (!inv.ContainsItem(worn)) h.UnequipItem(worn, false);

            List<ItemDrop.ItemData> items = inv.GetAllItems();
            foreach (ItemDrop.ItemData.ItemType type in new[] { ItemDrop.ItemData.ItemType.Helmet, ItemDrop.ItemData.ItemType.Chest, ItemDrop.ItemData.ItemType.Legs, ItemDrop.ItemData.ItemType.Shoulder })
            {
                ItemDrop.ItemData best = items.Where(i => i.m_shared.m_itemType == type).OrderByDescending(i => i.GetArmor()).FirstOrDefault();
                if (best != null) Equip(h, best);
            }

            ItemDrop.ItemData weapon = wantRanged ? BestRanged(h) ?? BestMelee(h) : BestMelee(h) ?? BestRanged(h);
            if (weapon != null) Equip(h, weapon);
            if (weapon != null && IsRanged(weapon))
            {
                ItemDrop.ItemData ammo = inv.GetAmmoItem(weapon.m_shared.m_ammoType);
                if (ammo != null && ammo.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Ammo) Equip(h, ammo);
            }
            if (weapon != null && weapon.m_shared.m_itemType == ItemDrop.ItemData.ItemType.OneHandedWeapon)
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

        public static float Armor(Humanoid h) => Worn(h).Where(i => i.m_shared.m_itemType != ItemDrop.ItemData.ItemType.Shield).Sum(i => i.GetArmor());

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
