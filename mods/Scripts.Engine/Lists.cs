using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Scripts
{
    /// <summary>
    /// What scripts may give, never give, never take, and spawn. These are data (docs/scripting-api.md section 5): a server's admin will be
    /// able to change them; the first version ships the defaults below and checks every name against the game's own item and creature
    /// lists when it is used, so a name the game does not know is a refusal and not an error.
    /// </summary>
    internal static class Lists
    {
        /// <summary>Items a script may give or drop, with the most in one call.</summary>
        internal static readonly Dictionary<string, int> Allow = new Dictionary<string, int>
        {
            ["Wood"] = 10, ["Stone"] = 10, ["Flint"] = 10, ["Resin"] = 10, ["Coal"] = 10, ["Feathers"] = 10, ["LeatherScraps"] = 10,
            ["Honey"] = 10, ["Blueberries"] = 10, ["Raspberry"] = 10, ["Mushroom"] = 10, ["Dandelion"] = 10, ["Carrot"] = 10, ["Turnip"] = 10,
            ["Coins"] = 10, ["CookedMeat"] = 5, ["Bread"] = 5, ["QueensJam"] = 5,
            // widened (docs/scripting-expansion.md section 6): Meadows, Black Forest and Swamp food and materials, no gear, nothing that ends a progression gate
            ["Stone"] = 10, ["Wood"] = 10, ["FineWood"] = 10, ["RoundLog"] = 10, ["ElderBark"] = 10, ["DeerHide"] = 10, ["BoneFragments"] = 10,
            ["Ooze"] = 10, ["Guck"] = 10, ["Chitin"] = 10, ["Turnip"] = 10, ["Carrot"] = 10, ["Onion"] = 10, ["Cloudberry"] = 10, ["Thistle"] = 10, ["Mushroom"] = 10,
            ["MushroomYellow"] = 10, ["MushroomBlue"] = 10, ["Dandelion"] = 10, ["Acorn"] = 10, ["BeechSeeds"] = 10, ["BirchSeeds"] = 10, ["PineCone"] = 10, ["FirCone"] = 10,
            ["RawMeat"] = 5, ["DeerMeat"] = 5, ["NeckTail"] = 5, ["FishRaw"] = 5, ["CookedDeerMeat"] = 5, ["BoarJerky"] = 5,
            ["NeckTailGrilled"] = 5, ["Sausages"] = 5, ["Blueberries"] = 10, ["Raspberry"] = 10, ["MeadBaseFrostResist"] = 1, ["TrollHide"] = 5, ["Feathers"] = 10, ["LeatherScraps"] = 10,
            ["Tar"] = 5, ["Needle"] = 5, ["CarrotSoup"] = 3, ["TurnipStew"] = 3, ["FishAndBread"] = 3, ["MinceMeatSauce"] = 3, ["Pancakes"] = 3,
        };

        /// <summary>Always refused, even if somebody lists them in Allow: trophies, boss items, keys, gates of progression, rare gear.</summary>
        internal static readonly HashSet<string> NeverGive = new HashSet<string>
        {
            "Wishbone", "DragonEgg", "YagluthDrop", "Megingjord", "BeltStrength", "Hammer", "Hoe", "Cultivator", "CryptKey", "Crypt_Key", "Key_Hildir",
            "HildirKey_forest", "HildirKey_mountain", "HildirKey_plains", "TrophyEikthyr", "TrophyTheElder", "TrophyBonemass", "TrophyDragonQueen",
            "TrophyGoblinKing", "TrophySeekerQueen", "TrophyFader", "BossStone_Eikthyr", "SwordCheat", "StaffSkeleton",
        };

        /// <summary>Items take() refuses, so a script cannot strip a quest item or a key tool.</summary>
        internal static readonly HashSet<string> NeverTake = new HashSet<string>
        {
            "Hammer", "Hoe", "Cultivator", "CryptKey", "Crypt_Key", "Key_Hildir", "Wishbone", "DragonEgg",
        };

        /// <summary>Creatures spawn() may make: up to Black Forest strength (the default tier). A stray stronger one near a new character kills them.</summary>
        internal static readonly HashSet<string> Creatures = new HashSet<string>
        {
            "Boar", "Neck", "Greyling", "Deer", "Greydwarf", "Greydwarf_Elite", "Skeleton", "Hen", "Chicken", "Crow", "Seagal",
        };

        /// <summary>Status effects a script may put on a player or creature, with the longest time in seconds. The host checks each name against the game's own list when it is used: a name the game doesn't know is a refusal.</summary>
        internal static readonly Dictionary<string, int> StatusEffects = new Dictionary<string, int>
        {
            ["Rested"] = 600, ["Resting"] = 300, ["Wet"] = 300, ["Cold"] = 120, ["Freezing"] = 20, ["Frost"] = 20, ["Burning"] = 15, ["Poison"] = 20, ["Lightning"] = 15,
            ["Shelter"] = 300, ["Warm"] = 300, ["Smoked"] = 60, ["Tared"] = 60, ["Spirit"] = 15,
        };

        /// <summary>Visual and audio effect prefabs (found in the running game with `prefabs fx_`, `vfx_`, `sfx_`). Each is also checked at use: it may carry nothing but harmless parts, so a name on this list can never hurt.</summary>
        internal static readonly HashSet<string> VisualEffects = new HashSet<string>
        {
            "fx_Lightning", "fx_Lightning_red", "fx_chainlightning_hit", "fx_redlightning_burst", "vfx_lightningstaff_fire", "fx_lightningweapon_hit",
            "vfx_Firework_Rocket", "vfx_Firework_Rocket_Blue", "vfx_Firework_Rocket_Cyan", "vfx_Firework_Rocket_Green", "vfx_Firework_Rocket_Purple", "vfx_Firework_Rocket_Red", "vfx_Firework_Rocket_Yellow", "vfx_FireWork_BlackCore", "vfx_FireWork_ThunderStone",
            "fx_summon_start", "fx_summon_skeleton_spawn", "fx_summon_spirit_spawn", "fx_summon_troll", "vfx_spawn", "vfx_spawn_large", "vfx_spawn_small",
            "fx_creature_tamed", "vfx_HealthUpgrade", "vfx_MeadSplash", "vfx_MeadStrength", "vfx_MeadBzerker", "vfx_MeadHasty", "vfx_MeadSwimmer", "vfx_MeadTamer",
            "fx_guardstone_activate", "fx_guardstone_deactivate", "fx_fader_meteor_hit", "vfx_tree_fall_hit", "vfx_FireballHit", "vfx_BonemassHit",
            "vfx_Place_beehive", "vfx_Place_chest", "vfx_Place_forge", "vfx_Place_bed", "vfx_Place_cauldron", "vfx_Place_cart",
            "vfx_eikthyr_death", "fx_eikthyr_stomp", "fx_gdking_rootspawn", "vfx_gdking_stomp",
        };

        /// <summary>Sound prefabs (audio only).</summary>
        internal static readonly HashSet<string> Sounds = new HashSet<string>
        {
            "sfx_firework_explode", "sfx_spawn", "sfx_fader_bell", "sfx_gui_craftitem_end", "sfx_gui_craftitem", "sfx_build_hammer_default", "sfx_build_hammer_wood", "sfx_build_hammer_stone",
            "sfx_eikthyr_alert", "sfx_eikthyr_death", "sfx_gdking_alert", "sfx_gdking_scream", "sfx_gdking_stomp", "sfx_troll_alerted", "sfx_troll_idle", "sfx_boar_alerted", "sfx_boar_idle",
            "sfx_boar_death", "sfx_chest_open", "sfx_chest_close", "sfx_staff_lightning_fire", "sfx_StaffLightning_charge", "sfx_tree_fall", "sfx_Bonemass_alert",
        };

        /// <summary>Decorations a script may place: no storage, no crafting, no beds, no portals (checked at use too).</summary>
        internal static readonly HashSet<string> Pieces = new HashSet<string>
        {
            "piece_banner01", "piece_banner02", "piece_banner03", "piece_banner04", "piece_banner05", "piece_banner06", "piece_banner07", "piece_banner08", "piece_banner09", "piece_banner10", "piece_banner11",
            "piece_groundtorch", "piece_groundtorch_green", "piece_groundtorch_blue", "piece_groundtorch_wood", "piece_maypole", "piece_jackoturnip",
            "piece_xmastree", "piece_xmasgarland", "piece_xmascrown", "piece_chair", "piece_chair02", "piece_chair03", "piece_bench01", "piece_logbench01", "piece_table", "piece_table_round",
        };

        internal static bool Trophy(string prefab) => prefab.StartsWith("Trophy");

        // ---- the lists over the wire (docs/scripting-multiplayer.md section 8) ----

        private static JObject _own;      // this game's own lists, kept the first time a server's are applied
        private static HashSet<string> _ownNeverGive, _ownNeverTake;

        /// <summary>The lists as plain data (the server sends them to the players who have the mod).</summary>
        internal static JObject ToJson() => new JObject
        {
            ["allow"] = new JObject(Allow.Select(kv => new JProperty(kv.Key, kv.Value))),
            ["neverGive"] = new JArray(NeverGive.Cast<object>().ToArray()),
            ["neverTake"] = new JArray(NeverTake.Cast<object>().ToArray()),
            ["creatures"] = new JArray(Creatures.Cast<object>().ToArray()),
            ["status"] = new JObject(StatusEffects.Select(kv => new JProperty(kv.Key, kv.Value))),
            ["visual"] = new JArray(VisualEffects.Cast<object>().ToArray()),
            ["sounds"] = new JArray(Sounds.Cast<object>().ToArray()),
            ["pieces"] = new JArray(Pieces.Cast<object>().ToArray()),
        };

        /// <summary>
        /// A server's lists replace this game's while connected to it, with one rule: they can only make this game stricter about what
        /// must never be given or taken. Anything this game's own never-lists or the trophies forbid stays forbidden whatever the server says,
        /// so a server that is not ours still cannot hand a player a boss item. Sizes are capped.
        /// </summary>
        internal static void Apply(JObject d)
        {
            if (d == null) return;
            if (_own == null) { _own = ToJson(); _ownNeverGive = new HashSet<string>(NeverGive); _ownNeverTake = new HashSet<string>(NeverTake); }
            try
            {
                var neverGive = new HashSet<string>(_ownNeverGive); foreach (string x in Strings(d["neverGive"], 500)) neverGive.Add(x);
                var neverTake = new HashSet<string>(_ownNeverTake); foreach (string x in Strings(d["neverTake"], 500)) neverTake.Add(x);
                var allow = new Dictionary<string, int>();
                if (d["allow"] is JObject a)
                    foreach (JProperty p in a.Properties().Take(1000))
                        if (!neverGive.Contains(p.Name) && !Trophy(p.Name) && p.Name.Length <= 60) allow[p.Name] = Math.Max(1, Math.Min(10, (int)p.Value));
                var status = new Dictionary<string, int>();
                if (d["status"] is JObject st) foreach (JProperty p in st.Properties().Take(200)) if (p.Name.Length <= 60) status[p.Name] = Math.Max(5, Math.Min(600, (int)p.Value));
                var creatures = new HashSet<string>(Strings(d["creatures"], 500));
                var visual = new HashSet<string>(Strings(d["visual"], 1000));
                var sounds = new HashSet<string>(Strings(d["sounds"], 1000));
                var pieces = new HashSet<string>(Strings(d["pieces"], 500));
                Replace(Allow, allow); Replace(StatusEffects, status);
                Replace(NeverGive, neverGive); Replace(NeverTake, neverTake);
                Replace(Creatures, creatures); Replace(VisualEffects, visual); Replace(Sounds, sounds); Replace(Pieces, pieces);
            }
            catch (Exception) { Reset(); }
        }

        /// <summary>Back to this game's own lists (left the server, or the mod is going away).</summary>
        internal static void Reset()
        {
            if (_own == null) return;
            JObject own = _own; HashSet<string> g = _ownNeverGive, t = _ownNeverTake;
            _own = null;
            Apply(own);
            Replace(NeverGive, g); Replace(NeverTake, t);
            _own = null; _ownNeverGive = null; _ownNeverTake = null;
        }

        private static IEnumerable<string> Strings(JToken t, int max) => t is JArray a ? a.Take(max).Where(x => x.Type == JTokenType.String).Select(x => (string)x).Where(x => x.Length > 0 && x.Length <= 60) : Enumerable.Empty<string>();

        private static void Replace(HashSet<string> into, IEnumerable<string> with) { var copy = with.ToList(); into.Clear(); foreach (string x in copy) into.Add(x); }
        private static void Replace(Dictionary<string, int> into, Dictionary<string, int> with) { var copy = new Dictionary<string, int>(with); into.Clear(); foreach (var kv in copy) into[kv.Key] = kv.Value; }
    }
}
