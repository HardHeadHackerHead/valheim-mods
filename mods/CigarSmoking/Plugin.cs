using System.Collections.Generic;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace CigarSmoking
{
    /// <summary>
    /// Quad's Cigars: tobacco from the wild plant to the cigar in your mouth. Three strains grow in three biomes; leaves are dried on a rack and
    /// aged in a barrel; cigars are rolled at a table (with a Humidor for the finer ones) and give a timed status effect while they burn, with a
    /// cigar, ember and smoke drawn on the player (other players with the mod see it too).
    ///
    /// Split across files: Plugin.cs (setup, hooks), Things.cs (registering everything), Items.cs and Pieces.cs (what is added), Types.cs (the
    /// cigar types and strains), Curer.cs (rack and barrel), World.cs (wild plants), Smoking.cs (status effects), Smoke.cs (what is drawn on a
    /// smoking player), Cigar.cs, Look.cs, Meshes.cs, ModelBuilder.cs, Icons.cs (looks), Models/ (made by tools/modelkit).
    /// </summary>
    [BepInPlugin(Guid, Name, Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.dhack.cigarsmoking";
        public const string Name = "Quad's Cigars";
        public const string Version = "0.2.0";
        public const string EffectPrefix = "SE_dh_smoking_";

        internal static ConfigEntry<float> Minutes, EffectStrength, GrowMinutes, DryMinutes, CureMinutes;
        internal static ConfigEntry<bool> DrawSmoke, DrawGlow;

        private Harmony _harmony;

        private void Awake()
        {
            Minutes = Config.Bind("Smoking", "Minutes", 5f, new ConfigDescription("How long one cigar lasts (minutes).", new AcceptableValueRange<float>(0.5f, 60f)));
            EffectStrength = Config.Bind("Smoking", "EffectStrength", 100f, new ConfigDescription("How strong the cigars' bonuses are (percent of the default). 0 for none.", new AcceptableValueRange<float>(0f, 300f)));
            DrawSmoke = Config.Bind("Look", "DrawSmoke", true, "Draw the smoke curling up from smoking players.");
            DrawGlow = Config.Bind("Look", "DrawGlow", true, "Give the ember a small flickering light (nice at night).");
            GrowMinutes = Config.Bind("Growing", "TobaccoGrowMinutes", 25f, new ConfigDescription("How long a tobacco plant takes to grow (minutes). Needs a restart.", new AcceptableValueRange<float>(1f, 240f)));
            DryMinutes = Config.Bind("Growing", "DryingMinutes", 2f, new ConfigDescription("How long a batch of leaves takes to dry on the rack (minutes). Needs a restart.", new AcceptableValueRange<float>(0.1f, 120f)));
            CureMinutes = Config.Bind("Growing", "CuringMinutes", 6f, new ConfigDescription("How long a batch of leaves takes to age in the barrel (minutes). Needs a restart.", new AcceptableValueRange<float>(0.1f, 240f)));

            _harmony = new Harmony(Guid);
            _harmony.PatchAll();
            Things.Register(); // a hot reload while in a world
            foreach (Player p in Player.GetAllPlayers()) Smoke.Attach(p);
            Logger.LogInfo($"{Name} {Version} loaded");
        }

        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
            foreach (Player p in Player.GetAllPlayers()) Smoke.Clear(p, null);
            Things.Unregister();
        }
    }

    // Whichever of ZNetScene and ObjectDB wakes first, the Cigar is registered before a saved one is loaded.
    [HarmonyPatch(typeof(ZNetScene), "Awake")]
    internal static class ZNetScene_Awake
    {
        private static void Postfix() => Things.Register();
    }

    [HarmonyPatch(typeof(ObjectDB), "Awake")]
    internal static class ObjectDB_Awake
    {
        private static void Postfix() => Things.Register();
    }

    // The game swaps in another ObjectDB's item lists when a world loads, which drops what was added to the old ones.
    [HarmonyPatch(typeof(ObjectDB), nameof(ObjectDB.CopyOtherDB))]
    internal static class ObjectDB_CopyOtherDB
    {
        private static void Postfix() => Things.Register();
    }

    [HarmonyPatch(typeof(Player), "Awake")]
    internal static class Player_Awake
    {
        private static void Postfix(Player __instance) => Smoke.Attach(__instance);
    }
}

namespace CigarSmoking
{
    // A cigar can be lit while another burns: the game would refuse (the same status effect is already on, or one of the same kind), but here
    // it starts a fresh one. Using it goes on to SE_Smoking.ResetTime or Setup, which restarts the timer and the length that has burned away.
    [HarmonyPatch(typeof(Player), nameof(Player.CanConsumeItem))]
    internal static class Player_CanConsumeItem
    {
        private static bool Prefix(ItemDrop.ItemData item, ref bool __result)
        {
            if (item == null) return true;
            SE_Smoking effect = Things.EffectFor(item.m_shared.m_name);
            if (effect == null) return true;
            // A cigar made before a reload of this mod points at an effect that no longer exists: use the current one.
            item.m_shared.m_consumeStatusEffect = effect;
            __result = true;
            return false;
        }
    }
}
