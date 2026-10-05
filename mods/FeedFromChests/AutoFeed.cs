using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace FeedFromChests
{
    /// <summary>
    /// A station's automatic-feeding settings: on or off, and which items it may use. Saved on the station itself (so every
    /// player sees the same settings, and they survive saving and loading the world).
    /// </summary>
    internal class AutoSetting
    {
        public bool On;
        public readonly HashSet<string> Allowed = new HashSet<string>(); // item names, e.g. "$item_wood"

        /// <summary>It stops feeding an item once the chests are down to this many of it, so your stock is never used up.</summary>
        public int Reserve = 20;

        /// <summary>What the station makes goes into the chests assigned to it (instead of dropping on the ground).</summary>
        public bool Output = true; // on unless someone turns it off (a station nobody has set up still sends output to its chests)

        /// <summary>Format: "on;item,item;output;reserve" (older saves have fewer parts).</summary>
        public string Encode() => (On ? "1" : "0") + ";" + string.Join(",", Allowed.ToArray()) + ";" + (Output ? "1" : "0") + ";" + Reserve;

        public static AutoSetting Parse(string text)
        {
            var setting = new AutoSetting();
            if (string.IsNullOrEmpty(text)) return setting;
            string[] parts = text.Split(';');
            setting.On = parts[0] == "1";
            if (parts.Length > 1)
                foreach (string name in parts[1].Split(new[] { ',' }, System.StringSplitOptions.RemoveEmptyEntries)) setting.Allowed.Add(name);
            if (parts.Length > 2) setting.Output = parts[2] != "0";
            if (parts.Length > 3 && int.TryParse(parts[3], out int reserve)) setting.Reserve = Mathf.Max(0, reserve);
            return setting;
        }

        public AutoSetting Copy()
        {
            var copy = new AutoSetting { On = On, Output = Output, Reserve = Reserve };
            foreach (string name in Allowed) copy.Allowed.Add(name);
            return copy;
        }
    }

    /// <summary>
    /// Smelters, kilns, furnaces and the like (everything the game builds from its Smelter class) can be set to keep themselves
    /// stocked from the nearby chests: fuel (coal) first, then the lowest-tier ticked item that is in a chest. It runs on whoever
    /// owns the station while they are nearby, takes only from chests (never your inventory), and never over-fills a station.
    /// </summary>
    internal static class AutoFeed
    {
        private static readonly int Key = "ffc_auto".GetStableHashCode();
        private static readonly List<Smelter> All = new List<Smelter>();
        private static readonly Dictionary<Smelter, StationInfo> Infos = new Dictionary<Smelter, StationInfo>();
        private static readonly System.Reflection.MethodInfo QueueSize = AccessTools.Method(typeof(Smelter), "GetQueueSize");
        private static readonly System.Reflection.MethodInfo FuelAmount = AccessTools.Method(typeof(Smelter), "GetFuel");
        private static float _next;

        /// <summary>True while we feed automatically: the game's "Added Wood" messages would otherwise appear every second.</summary>
        public static bool Silent;

        public static void Register(Smelter smelter) { if (smelter != null && !All.Contains(smelter)) All.Add(smelter); }
        public static void Seed() { foreach (Smelter s in Object.FindObjectsOfType<Smelter>()) Register(s); }
        public static void Clear() { All.Clear(); Infos.Clear(); }

        public static bool Supported(StationInfo info) => info != null && info.Component is Smelter;

        public static AutoSetting Read(MonoBehaviour station)
        {
            ZNetView view = station != null ? station.GetComponent<ZNetView>() : null;
            return AutoSetting.Parse(view != null && view.IsValid() ? view.GetZDO().GetString(Key, "") : "");
        }

        public static void Write(MonoBehaviour station, AutoSetting setting)
        {
            ZNetView view = station != null ? station.GetComponent<ZNetView>() : null;
            if (view == null || !view.IsValid()) return;
            view.ClaimOwnership(); // only the owner can change it, and the owner is the one who then runs the feeding
            view.GetZDO().Set(Key, setting.Encode());
        }

        private static int Queue(Smelter s) => QueueSize != null ? (int)QueueSize.Invoke(s, null) : 0;
        private static float Fuel(Smelter s) => FuelAmount != null ? (float)FuelAmount.Invoke(s, null) : 0f;

        /// <summary>Called every frame; does the actual work every <paramref name="interval"/> seconds.</summary>
        public static void Tick(Plugin plugin, Player player, float interval, float range)
        {
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + Mathf.Max(0.25f, interval);

            All.RemoveAll(s => s == null);
            float max = range * range;
            foreach (Smelter smelter in All.ToArray())
            {
                if ((smelter.transform.position - player.transform.position).sqrMagnitude > max) continue;
                ZNetView view = smelter.GetComponent<ZNetView>();
                if (view == null || !view.IsValid() || !view.IsOwner()) continue; // exactly one player runs each station
                AutoSetting setting = Read(smelter);
                if (!setting.On || setting.Allowed.Count == 0) continue;

                if (!Infos.TryGetValue(smelter, out StationInfo info) || !info.Alive)
                {
                    if (!Stations.TryGet(smelter.gameObject, out info, out _)) continue;
                    Infos[smelter] = info;
                }
                plugin.AutoStep(player, smelter, info, setting);
            }
        }

        // ---- sending what the station makes into chests --------------------------------------------

        private static readonly int RulesKey = "DHack_StackRules".GetStableHashCode(); // the chest assignments (QualityOfLife, K)
        private static readonly System.Reflection.MethodInfo ConversionOf = AccessTools.Method(typeof(Smelter), "GetItemConversion");

        /// <summary>Does this chest take this item? 2 = assigned to it by name, 1 = by its kind (metals, materials...), 0 = no.</summary>
        private static int Wants(Container chest, string itemName, string category)
        {
            ZNetView view = Chests.ViewOf(chest);
            string text = view != null && view.IsValid() ? view.GetZDO().GetString(RulesKey, "") : "";
            int best = 0;
            foreach (string part in text.Split('|'))
            {
                if (part.Length < 3) continue;
                bool items = part.StartsWith("I="), categories = part.StartsWith("C=");
                if (!items && !categories) continue;
                foreach (string value in part.Substring(2).Split(','))
                {
                    if (items && value == itemName) return 2;
                    if (categories && value == category) best = 1;
                }
            }
            return best;
        }

        /// <summary>The kinds QualityOfLife files things under, for what smelters and kilns make (bars, charcoal, flour...).</summary>
        private static string CategoryOf(ItemDrop.ItemData.SharedData s)
        {
            string plain = (s.m_name ?? "").ToLowerInvariant();
            if (plain.StartsWith("$item_")) plain = plain.Substring(6);
            if ((plain.EndsWith("ore") && !plain.EndsWith("core")) || plain.EndsWith("scrap")) return "Ores";
            switch (plain)
            {
                case "copper": case "tin": case "bronze": case "iron": case "silver": case "blackmetal": case "flametal": return "Metals";
                default: return "Materials";
            }
        }

        /// <summary>
        /// A station that has finished something: put it in a chest assigned to that item (by name first, then by kind), nearest first.
        /// Returns false if there is no such chest with room, and then the game drops it on the ground as usual.
        /// </summary>
        public static bool SendOutput(Smelter smelter, string ore, int stack, float radius, System.Action<string> log)
        {
            if (Player.m_localPlayer == null || stack <= 0 || ConversionOf == null) return false;
            AutoSetting setting = Read(smelter);
            if (!setting.Output) return false;

            object conversion = ConversionOf.Invoke(smelter, new object[] { ore });
            ItemDrop product = conversion != null ? (ItemDrop)AccessTools.Field(conversion.GetType(), "m_to").GetValue(conversion) : null;
            if (product == null) return false;
            string name = product.m_itemData.m_shared.m_name;
            string category = CategoryOf(product.m_itemData.m_shared);

            List<Container> nearby = Chests.Near(smelter.transform.position, radius);
            var candidates = new List<KeyValuePair<int, Container>>();
            foreach (Container chest in nearby)
            {
                int wants = Wants(chest, name, category);
                if (wants > 0) candidates.Add(new KeyValuePair<int, Container>(wants, chest));
            }
            if (candidates.Count == 0) { log($"{smelter.m_name} made {stack} {Localization.instance.Localize(name)}: none of the {nearby.Count} usable chest(s) within {radius:0} m is assigned to it or to {category}, so it drops on the ground"); Explain(smelter, name, category, radius, log); }
            // by name before by kind; Near() already lists the nearest first and OrderByDescending keeps that order within a group
            foreach (var pair in candidates.OrderByDescending(p => p.Key))
            {
                Container chest = pair.Value;
                Inventory inventory = chest.GetInventory();
                if (!inventory.CanAddItem(product.gameObject, stack)) { log($"{smelter.m_name}: an assigned chest has no room for {stack} {Localization.instance.Localize(name)}"); continue; }

                ZNetView view = Chests.ViewOf(chest);
                if (view != null && !view.IsOwner()) view.ClaimOwnership(); // only the owner can save a chest's contents
                if (!inventory.AddItem(product.gameObject, stack)) continue;
                smelter.m_produceEffects.Create(smelter.transform.position, smelter.transform.rotation);
                log($"{smelter.m_name} made {stack} {Localization.instance.Localize(name)} and put it in a chest");
                return true;
            }
            return false;
        }

        private static float _explainedAt = -100f;

        /// <summary>For tracking down "why did it not go in the chest": every chest within reach of the station, how far it is, and what it is assigned to.</summary>
        private static void Explain(Smelter smelter, string name, string category, float radius, System.Action<string> log)
        {
            if (Time.unscaledTime - _explainedAt < 20f) return;
            _explainedAt = Time.unscaledTime;
            var lines = new List<string>();
            int withRules = 0;
            foreach (Container c in ContainerRegistry.Alive())
            {
                float d = Vector3.Distance(c.transform.position, smelter.transform.position);
                if (d > radius + 15f) continue;
                ZNetView view = Chests.ViewOf(c);
                string rules = view != null && view.IsValid() ? view.GetZDO().GetString(RulesKey, "") : "(no data)";
                if (rules.Length > 0 && rules != "(no data)" && rules != "C=|I=") withRules++;
                if (rules.Length > 0) lines.Add($"{d:0} m: {rules}");
            }
            log($"  looking for an assignment to '{name}' or '{category}'; chests with assignments within {radius + 15f:0} m: {withRules}" + (lines.Count > 0 ? " -> " + string.Join(" ;; ", lines.Take(8).ToArray()) : ""));
        }

        /// <summary>The text added to the station's hover text: whether it is on, and how to change it.</summary>
        private static float _hoverAt;
        private static Smelter _hoverFor;
        private static string _hoverText = "";

        public static string HoverLine(Smelter smelter)
        {
            if (smelter != _hoverFor || Time.unscaledTime - _hoverAt > 0.5f)
            {
                _hoverFor = smelter;
                _hoverAt = Time.unscaledTime;
                _hoverText = $"\n<size=14>Auto-feed from chests: {(Read(smelter).On ? "<color=#8fe388>ON</color>" : "off")}</size>";
            }
            return _hoverText;
        }
    }

    public partial class Plugin
    {
        /// <summary>One automatic top-up of a station: fuel first, then one item. Does nothing if it is full or the chests have none.</summary>
        internal void AutoStep(Player player, Smelter smelter, StationInfo info, AutoSetting setting)
        {
            List<Container> chests = Chests.Near(info.Position, _radius.Value);
            if (chests.Count == 0) return;

            AutoFeed.Silent = true;
            try
            {
                if (info.Fuel != null && smelter.m_maxFuel > 0 && AutoFeedFuel(smelter) < smelter.m_maxFuel - 1)
                {
                    string fuel = info.Fuel.m_itemData.m_shared.m_name;
                    if (setting.Allowed.Contains(fuel) && Chests.Count(chests, fuel) > setting.Reserve) AddOne(player, info, info.Fuel, true, chests, chestsOnly: true);
                }

                if (smelter.m_maxOre > 0 && AutoFeedQueue(smelter) < smelter.m_maxOre)
                {
                    foreach (ItemDrop drop in info.Inputs.OrderBy(d => Tiers.Rank(d.m_itemData.m_shared)))
                    {
                        string name = drop.m_itemData.m_shared.m_name;
                        if (!setting.Allowed.Contains(name) || Chests.Count(chests, name) <= setting.Reserve) continue; // keep the minimum in stock
                        AddOne(player, info, drop, false, chests, chestsOnly: true);
                        break; // one per step: lowest tier first
                    }
                }
            }
            finally
            {
                AutoFeed.Silent = false;
            }
        }

        private static float AutoFeedFuel(Smelter s) => (float)AccessTools.Method(typeof(Smelter), "GetFuel").Invoke(s, null);
        private static int AutoFeedQueue(Smelter s) => (int)AccessTools.Method(typeof(Smelter), "GetQueueSize").Invoke(s, null);
    }

    [HarmonyPatch(typeof(Smelter), "Awake")]
    internal static class Smelter_Awake
    {
        private static void Postfix(Smelter __instance) => AutoFeed.Register(__instance);
    }

    // A smelter or kiln finished something: if it is set to send its output to chests, put it in the right one.
    [HarmonyPatch(typeof(Smelter), "Spawn")]
    internal static class Smelter_Spawn
    {
        private static bool Prefix(Smelter __instance, string ore, int stack)
        {
            Plugin plugin = Plugin.Instance;
            if (plugin == null || !plugin.AutoEnabled) return true;
            try { return !AutoFeed.SendOutput(__instance, ore, stack, plugin.OutputRadius, plugin.Info); }
            catch (System.Exception e) { plugin.Log("Could not send output to a chest, dropping it instead: " + e.Message); return true; }
        }
    }

    // Our automatic feeding is quiet: no "Added Wood" message in the middle of the screen every second.
    [HarmonyPatch(typeof(Player), nameof(Player.Message))]
    internal static class Player_Message
    {
        private static bool Prefix() => !AutoFeed.Silent;
    }

    // Looking at a smelter or kiln: say whether auto-feed is on and which key opens its settings.
    [HarmonyPatch(typeof(Switch), nameof(Switch.GetHoverText))]
    internal static class Switch_GetHoverText
    {
        private static void Postfix(Switch __instance, ref string __result)
        {
            Plugin plugin = Plugin.Instance;
            if (plugin == null || string.IsNullOrEmpty(__result) || !plugin.AutoEnabled) return;
            Smelter smelter = __instance.GetComponentInParent<Smelter>();
            if (smelter != null) __result += AutoFeed.HoverLine(smelter);
        }
    }
}
