using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace FeedFromChests
{
    /// <summary>
    /// Cooking stations (the spit, the iron spit, the oven...): food that is done comes off by itself, so it never burns; E takes
    /// everything that is done straight into your inventory; and a station can keep itself stocked with raw food from nearby chests.
    ///
    /// Taking food off always goes through the station's own "remove done item" code (skill, stats, bonus yield and the network all
    /// stay as in the game); only the very last step, where the game throws the item out of the spit, is replaced: into your inventory
    /// (E), or into a chest assigned to it, or gently off the side of the spit (when it comes off by itself).
    /// </summary>
    internal static class Cooking
    {
        private static readonly List<CookingStation> All = new List<CookingStation>();
        private static readonly System.Reflection.MethodInfo GetSlotM = AccessTools.Method(typeof(CookingStation), "GetSlot");
        private static readonly System.Reflection.MethodInfo IsItemDoneM = AccessTools.Method(typeof(CookingStation), "IsItemDone");
        private static readonly System.Reflection.MethodInfo HaveDoneItemM = AccessTools.Method(typeof(CookingStation), "HaveDoneItem");
        private static readonly System.Reflection.MethodInfo OnInteractM = AccessTools.Method(typeof(CookingStation), "OnInteract");
        private static readonly System.Reflection.MethodInfo GetFreeSlotM = AccessTools.Method(typeof(CookingStation), "GetFreeSlot");
        private static readonly System.Reflection.MethodInfo GetFuelM = AccessTools.Method(typeof(CookingStation), "GetFuel");
        private static readonly System.Reflection.MethodInfo ConversionM = AccessTools.Method(typeof(CookingStation), "GetItemConversion");
        private static float _next;

        /// <summary>While set, the item a spit lets go of goes into this player's inventory (pressing E).</summary>
        internal static Player DeliverTo;
        /// <summary>While set, the item this spit lets go of goes to a chest or slides off its side (food coming off by itself).</summary>
        internal static CookingStation AutoFrom;
        internal static Vector3 AutoToward;

        public static void Register(CookingStation s) { if (s != null && !All.Contains(s)) All.Add(s); }
        public static void Seed() { foreach (CookingStation s in Object.FindObjectsOfType<CookingStation>()) Register(s); }
        public static void Clear() { All.Clear(); DeliverTo = null; AutoFrom = null; }

        public static bool HasDone(CookingStation s) => HaveDoneItemM != null && s != null && (bool)HaveDoneItemM.Invoke(s, null);
        public static bool HasFreeSlot(CookingStation s) => GetFreeSlotM != null && (int)GetFreeSlotM.Invoke(s, null) >= 0;
        public static float Fuel(CookingStation s) => GetFuelM != null ? (float)GetFuelM.Invoke(s, null) : 0f;

        /// <summary>One slot: the item's prefab name ("" if empty), how long it has cooked, and 0 not done / 1 done / 2 burnt.</summary>
        public static void Slot(CookingStation s, int i, out string item, out float cooked, out int status)
        {
            var args = new object[] { i, null, null, null, null };
            GetSlotM.Invoke(s, args);
            item = (string)args[1];
            cooked = (float)args[2];
            status = (int)args[3];
        }

        /// <summary>How long an item takes to cook here (0 if it does not cook here).</summary>
        public static float CookTime(CookingStation s, string item)
        {
            object conversion = ConversionM != null ? ConversionM.Invoke(s, new object[] { item }) : null;
            return conversion != null ? (float)AccessTools.Field(conversion.GetType(), "m_cookTime").GetValue(conversion) : 0f;
        }

        /// <summary>Pressing E at a spit with food that is done: take all of it, into your inventory. Returns how many came off.</summary>
        public static int TakeAllInto(Player player, CookingStation s)
        {
            ZNetView view = s.GetComponent<ZNetView>();
            if (view == null || !view.IsValid() || OnInteractM == null) return 0;
            view.ClaimOwnership();                     // then the station's own removal runs right here, at once
            int taken = 0;
            DeliverTo = player;
            try
            {
                for (int k = 0; k < s.m_slots.Length && HasDone(s); k++)
                {
                    OnInteractM.Invoke(s, new object[] { player }); // the game's own: skill, bonus yield, stats, then remove one
                    taken++;
                }
            }
            finally { DeliverTo = null; }
            return taken;
        }

        /// <summary>Called every frame: every half second, each spit this player runs lets go of what is done, and auto-fed ones are topped up.</summary>
        public static void Tick(Plugin plugin, Player player, float range, bool takeOffDefault)
        {
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 0.5f;
            All.RemoveAll(s => s == null);
            float max = range * range;
            foreach (CookingStation s in All.ToArray())
            {
                if ((s.transform.position - player.transform.position).sqrMagnitude > max) continue;
                ZNetView view = s.GetComponent<ZNetView>();
                if (view == null || !view.IsValid()) continue;
                if (!view.IsOwner()) { if (view.HasOwner()) continue; view.ClaimOwnership(); } // exactly one player runs each station
                AutoSetting setting = AutoFeed.Read(s);
                if (setting.TakeOff && takeOffDefault) TakeOffDone(s, player.transform.position);
                if (setting.On && setting.Allowed.Count > 0) plugin.CookAutoStep(player, s, setting);
            }
        }

        private static void TakeOffDone(CookingStation s, Vector3 toward)
        {
            ZNetView view = s.GetComponent<ZNetView>();
            AutoFrom = s;
            AutoToward = toward;
            try
            {
                for (int k = 0; k < s.m_slots.Length && HasDone(s); k++)
                    view.InvokeRPC("RPC_RemoveDoneItem", toward, 1); // we own it, so this runs right now
            }
            finally { AutoFrom = null; }
        }

        /// <summary>The kind QualityOfLife files an item under (for chests assigned with K): cooked food is "Food".</summary>
        public static string CategoryOf(ItemDrop.ItemData.SharedData s)
        {
            if (s.m_itemType == ItemDrop.ItemData.ItemType.Consumable)
                return !s.m_isDrink && (s.m_food > 0f || s.m_foodStamina > 0f || s.m_foodEitr > 0f) ? "Food" : "Potions";
            return "Materials";
        }

        /// <summary>The station let go of an item. Returns false when we placed it ourselves (so the game does not throw it out).</summary>
        public static bool Deliver(CookingStation s, string itemName, int slot, Vector3 userPoint)
        {
            GameObject prefab = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(itemName) : null;
            if (prefab == null) return true;

            if (DeliverTo != null)
            {
                Inventory inventory = DeliverTo.GetInventory();
                if (!inventory.CanAddItem(prefab, 1)) return true; // no room: the game hands it out as usual
                inventory.AddItem(prefab, 1);
                return false;
            }

            if (AutoFrom == s)
            {
                Plugin plugin = Plugin.Instance;
                AutoSetting setting = AutoFeed.Read(s);
                if (plugin != null && setting.Output && AutoFeed.SendItem(s.transform.position, prefab.GetComponent<ItemDrop>(), 1,
                        plugin.OutputRadius, CategoryOf(prefab.GetComponent<ItemDrop>().m_itemData.m_shared), plugin.Info, s.m_name) > 0)
                    return false;
                SlideOff(s, prefab, slot, AutoToward);
                return false;
            }
            return true;
        }

        /// <summary>Let the item slide gently off the side of the spit, away from the fire, towards whoever is cooking.</summary>
        private static void SlideOff(CookingStation s, GameObject prefab, int slot, Vector3 toward)
        {
            Vector3 from = slot >= 0 && slot < s.m_slots.Length ? s.m_slots[slot].position : s.transform.position + Vector3.up;
            Vector3 dir = toward - s.transform.position;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.01f) dir = -s.transform.forward;
            dir.Normalize();
            GameObject go = Object.Instantiate(prefab, from + dir * 0.55f + Vector3.up * 0.1f, Quaternion.Euler(0f, Random.Range(0, 360), 0f));
            ItemDrop drop = go.GetComponent<ItemDrop>();
            ItemDrop.OnCreateNew(drop);
            Rigidbody body = go.GetComponent<Rigidbody>();
            if (body != null) body.linearVelocity = dir * 1.4f + Vector3.up * 0.8f; // a gentle push: it lands about a metre out
        }

        /// <summary>One line per slot for the menu: what is on it and how far along it is.</summary>
        public static List<string> Lines(CookingStation s)
        {
            var lines = new List<string>();
            for (int i = 0; i < s.m_slots.Length; i++)
            {
                Slot(s, i, out string item, out float cooked, out int status);
                if (item.Length == 0) { lines.Add($"Slot {i + 1}: empty"); continue; }
                string name = AutoFeed.NameOf(item);
                if (status == 2) { lines.Add($"Slot {i + 1}: {name} (burnt)"); continue; }
                if (IsItemDoneM != null && (bool)IsItemDoneM.Invoke(s, new object[] { item })) { lines.Add($"Slot {i + 1}: {name}, ready"); continue; }
                float time = CookTime(s, item);
                lines.Add(time > 0f ? $"Slot {i + 1}: {name}, cooking {Mathf.Clamp01(cooked / time) * 100f:0}%" : $"Slot {i + 1}: {name}");
            }
            if (s.m_useFuel) lines.Add($"Fuel {Mathf.Floor(Fuel(s)):0}/{s.m_maxFuel}");
            return lines;
        }

        private static float _hoverAt;
        private static CookingStation _hoverFor;
        private static string _hoverText = "";

        public static string HoverLine(CookingStation s)
        {
            if (s != _hoverFor || Time.unscaledTime - _hoverAt > 0.5f)
            {
                _hoverFor = s;
                _hoverAt = Time.unscaledTime;
                AutoSetting setting = AutoFeed.Read(s);
                _hoverText = Localization.instance.Localize(
                    $"\n<size=14>[<color=yellow><b>$KEY_Use</b></color>] menu (auto-feed {(setting.On ? "<color=#8fe388>ON</color>" : "off")}), " +
                    $"[<color=yellow><b>$KEY_AltPlace + $KEY_Use</b></color>] put food on by hand. Done food comes off by itself: {(setting.TakeOff ? "<color=#8fe388>ON</color>" : "off")}</size>");
            }
            return _hoverText;
        }
    }

    public partial class Plugin
    {
        /// <summary>One automatic top-up of a cooking station: fuel (ovens) first, then one piece of raw food into a free slot.</summary>
        internal void CookAutoStep(Player player, CookingStation station, AutoSetting setting)
        {
            if (!Stations.TryGet(station.gameObject, out StationInfo info, out _)) return;
            List<Container> chests = Chests.Near(info.Position, _autoRadius.Value);
            if (chests.Count == 0) return;
            LimitPlan plan = Limits.For(info);
            AutoFeed.Silent = true;
            try
            {
                if (info.Fuel != null && station.m_useFuel && Cooking.Fuel(station) < station.m_maxFuel - 1)
                {
                    string fuel = info.Fuel.m_itemData.m_shared.m_name;
                    if (setting.Allowed.Contains(fuel) && Limits.MayFeed(info, setting, plan, info.Fuel, true, Chests.Count(chests, fuel), _outputRadius.Value, out _))
                        AddOne(player, info, info.Fuel, true, chests, chestsOnly: true);
                }
                if (!Cooking.HasFreeSlot(station)) return;
                foreach (ItemDrop drop in info.Inputs.OrderBy(d => Tiers.Rank(d.m_itemData.m_shared)))
                {
                    string name = drop.m_itemData.m_shared.m_name;
                    if (!setting.Allowed.Contains(name) || !Limits.MayFeed(info, setting, plan, drop, false, Chests.Count(chests, name), _outputRadius.Value, out _)) continue;
                    if (AddOne(player, info, drop, false, chests, chestsOnly: true)) break; // one per step
                }
            }
            finally { AutoFeed.Silent = false; }
        }
    }

    [HarmonyPatch(typeof(CookingStation), "Awake")]
    internal static class CookingStation_Awake
    {
        private static void Postfix(CookingStation __instance) => Cooking.Register(__instance);
    }

    // The very last step of taking food off: where the item goes.
    [HarmonyPatch(typeof(CookingStation), "SpawnItem")]
    internal static class CookingStation_SpawnItem
    {
        private static bool Prefix(CookingStation __instance, string name, int slot, Vector3 userPoint)
        {
            try { return Cooking.Deliver(__instance, name, slot, userPoint); }
            catch (System.Exception e) { Plugin.Instance?.Log("Could not place cooked food, the game throws it out instead: " + e.Message); return true; }
        }
    }

    // Looking at a spit: which key does what, and whether it is fed and emptied by itself.
    [HarmonyPatch(typeof(CookingStation), nameof(CookingStation.GetHoverText))]
    internal static class CookingStation_GetHoverText
    {
        private static void Postfix(CookingStation __instance, ref string __result)
        {
            Plugin plugin = Plugin.Instance;
            if (plugin == null || string.IsNullOrEmpty(__result) || !plugin.AutoEnabled) return;
            __result += Cooking.HoverLine(__instance);
        }
    }
}
