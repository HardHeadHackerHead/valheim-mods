using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace AICompanion
{
    /// <summary>
    /// A companion's home, as a player's: a bed of its own where it wakes after it falls, and chests of its own where it keeps what it
    /// gathers and finds better gear, arrows and potions. You assign them from its menu (Home tab, "Assign"): then E on a bed or a chest gives
    /// it to the companion (or takes it back). The bed and chests carry the companion's id in their ZDO; the companion keeps its bed's spot,
    /// so it can wake there even when the bed is far away. Players cannot claim a companion's bed.
    ///
    /// When it falls, its player's game brings it back after a while (Companion, RespawnSeconds): in its bed, or beside its player without
    /// one, with its name, looks and settings (from its Profile), and nothing in its hands: its gear is in its tombstone, as a player's.
    /// </summary>
    internal static class Home
    {
        public const string HomeKey = "dhc_home", HomeName = "dhc_homename";

        // ---- assigning ----------------------------------------------------------------------------------------

        public static Humanoid AssignFor;
        private static float _nextHint;

        public static void StartAssign(Humanoid c)
        {
            AssignFor = c;
            _nextHint = 0f;
        }

        public static void StopAssign(string why = null)
        {
            if (AssignFor != null && why != null) Plugin.Tell(why);
            AssignFor = null;
        }

        public static long IdOn(Component piece) => Companion.Zdo(piece)?.GetLong(HomeKey, 0L) ?? 0L;

        private static bool Claim(Component piece)
        {
            ZNetView v = piece.GetComponent<ZNetView>();
            if (v == null || !v.IsValid()) return false;
            if (!v.IsOwner()) v.ClaimOwnership();
            return true;
        }

        /// <summary>E on a bed while assigning: it becomes the companion's bed (its old one is let go), or stops being it.</summary>
        public static void ToggleBed(Bed bed, Humanoid c)
        {
            ZDO z = Companion.Zdo(bed);
            long id = Companion.IdOf(c);
            if (z == null) return;
            if (z.GetLong(HomeKey, 0L) == id)
            {
                Claim(bed); z.Set(HomeKey, 0L); z.Set(HomeName, "");
                Companion.Write(c, cz => cz.Set(Keys.HasBed, false));
                Plugin.Tell($"{Companion.NameOf(c)} no longer sleeps here");
                return;
            }
            long owner = z.GetLong(ZDOVars.s_owner, 0L);
            if (owner != 0L) { Plugin.Tell($"That bed is {z.GetString(ZDOVars.s_ownerName, "someone")}'s. Give {Companion.NameOf(c)} a bed nobody sleeps in."); return; }
            if (z.GetLong(HomeKey, 0L) != 0L) { Plugin.Tell($"That bed is {z.GetString(HomeName, "another companion")}'s"); return; }
            foreach (Bed old in Object.FindObjectsOfType<Bed>()) if (old != bed && IdOn(old) == id) { Claim(old); Companion.Zdo(old).Set(HomeKey, 0L); }
            Claim(bed); z.Set(HomeKey, id); z.Set(HomeName, Companion.NameOf(c));
            Vector3 spot = bed.GetSpawnPoint();
            Companion.Write(c, cz => { cz.Set(Keys.HasBed, true); cz.Set(Keys.BedPos, spot); });
            Plugin.Tell($"This is {Companion.NameOf(c)}'s bed now: {Companion.NameOf(c)} wakes here after falling");
            Plugin.Instance?.Note($"{Companion.NameOf(c)} was given the bed at {spot:F0}");
        }

        /// <summary>E on a chest while assigning: it becomes one of the companion's chests, or stops being one.</summary>
        public static void ToggleChest(Container chest, Humanoid c)
        {
            ZDO z = Companion.Zdo(chest);
            long id = Companion.IdOf(c);
            if (z == null) return;
            if (chest.IsInUse()) { Plugin.Tell("Someone has that chest open"); return; }
            bool mine = z.GetLong(HomeKey, 0L) == id;
            if (!mine && z.GetLong(HomeKey, 0L) != 0L) { Plugin.Tell($"That chest is {z.GetString(HomeName, "another companion")}'s"); return; }
            Claim(chest);
            z.Set(HomeKey, mine ? 0L : id);
            z.Set(HomeName, mine ? "" : Companion.NameOf(c));
            _chestCache.Clear();
            Plugin.Tell(mine ? $"{Companion.NameOf(c)} no longer uses this chest" : $"{Companion.NameOf(c)} keeps things in this chest now ({Chests(c).Count} chest{(Chests(c).Count == 1 ? "" : "s")})");
        }

        /// <summary>A chest a player built (not a companion's bag, a tombstone or a cart's load).</summary>
        public static bool IsChest(Container c) =>
            c != null && !Companion.Is(c) && c.GetComponent<TombStone>() == null && c.GetComponentInParent<Piece>() != null && c.GetComponentInParent<Vagon>() == null && c.GetComponentInParent<Ship>() == null;

        // ---- finding its things ------------------------------------------------------------------------------

        private static readonly Dictionary<long, KeyValuePair<float, List<Container>>> _chestCache = new Dictionary<long, KeyValuePair<float, List<Container>>>();

        /// <summary>Its chests that are loaded (near players), refreshed every few seconds.</summary>
        public static List<Container> Chests(Humanoid c)
        {
            long id = Companion.IdOf(c);
            if (_chestCache.TryGetValue(id, out var hit) && Time.time - hit.Key < 4f) return hit.Value.Where(x => x != null).ToList();
            List<Container> found = Object.FindObjectsOfType<Container>().Where(x => IsChest(x) && IdOn(x) == id).ToList();
            _chestCache[id] = new KeyValuePair<float, List<Container>>(Time.time, found);
            return found;
        }

        public static Bed BedOf(Humanoid c)
        {
            long id = Companion.IdOf(c);
            return Object.FindObjectsOfType<Bed>().FirstOrDefault(b => IdOn(b) == id);
        }

        // ---- every second on each player's game ----------------------------------------------------------------

        private static float _nextTick, _nextSnapshot;
        private static readonly Dictionary<long, Minimap.PinData> BedPins = new Dictionary<long, Minimap.PinData>();

        public static void Tick(Player p)
        {
            if (AssignFor != null)
            {
                if (AssignFor == null || AssignFor.IsDead() || Input.GetKeyDown(KeyCode.Escape) || Plugin.MenuKey.Value.IsDown()) StopAssign("Done assigning");
                else if (Time.time >= _nextHint) { _nextHint = Time.time + 6f; Plugin.Tell($"Assigning for {Companion.NameOf(AssignFor)}: E on a bed or a chest gives it (E again takes it back). {Plugin.MenuKey.Value} or Esc when done."); }
            }
            if (Time.time < _nextTick) return;
            _nextTick = Time.time + 1f;

            // Keep each companion's profile up to date while it is near (it is what brings it back when it falls).
            if (Time.time >= _nextSnapshot)
            {
                _nextSnapshot = Time.time + 5f;
                foreach (Humanoid c in Companion.All().Where(c => Companion.IsMine(c, p) && !c.IsDead()))
                    Profile.Save(p, Profile.Of(c));
            }

            // Bring back the fallen once their time is up.
            double now = ZNet.instance.GetTimeSeconds();
            foreach (Profile prof in Profile.Here(p).Where(x => x.Dead && now - x.DiedAt >= Plugin.RespawnSeconds.Value).ToList())
                Respawn(p, prof);

            UpdateBedPins(p);
        }

        /// <summary>On its player's game when it fell (directly, or told by the game that ran it).</summary>
        public static void MarkDead(Player p, long id, Vector3 where, Humanoid body = null, bool grave = true)
        {
            Profile known = Profile.Find(p, id);
            if (known != null && known.Dead && ZNet.instance.GetTimeSeconds() - known.DiedAt < 60.0) return; // already counted (we are told twice)
            Profile prof = body != null ? Profile.Of(body) : known;
            if (prof == null) return;
            prof.Dead = true;
            prof.Skills = Skill.AfterDeath(prof.Skills); // a player's death penalty: 5% off every skill
            prof.DiedAt = ZNet.instance.GetTimeSeconds();
            prof.DiedPos = where;
            prof.HasGrave = grave;   // it goes back for its things when it wakes
            Profile.Save(p, prof);
            Plugin.Instance?.Note($"{prof.Name} will wake {(prof.HasBed ? "in their bed" : "beside you")} in {Plugin.RespawnSeconds.Value:0} s");
        }

        public static Humanoid Respawn(Player p, Profile prof)
        {
            GameObject prefab = Prefab.Get();
            if (prefab == null) return null;
            Vector3 pos = prof.HasBed ? prof.Bed : p.transform.position - p.transform.forward * 2f;
            if (!prof.HasBed && ZoneSystem.instance != null && ZoneSystem.instance.GetSolidHeight(pos, out float h)) pos.y = Mathf.Max(pos.y, h);
            GameObject go = Object.Instantiate(prefab, pos + Vector3.up * 0.2f, Quaternion.identity);
            Humanoid c = go.GetComponent<Humanoid>();
            ZDO z = Companion.Zdo(c);
            z.Set(Keys.Master, p.GetPlayerID());
            z.Set(Keys.MasterName, p.GetPlayerName());
            prof.ApplyTo(c);
            prof.Dead = false;
            Profile.Save(p, prof);
            string where = prof.HasBed ? $"in {(prof.Model == 1 ? "her" : "his")} bed" : "beside you";
            Plugin.Tell($"{prof.Name} wakes up {where}");
            Plugin.Instance?.Note($"{prof.Name} woke {where} at {pos:F0}");
            return c;
        }

        private static void UpdateBedPins(Player p)
        {
            if (Minimap.instance == null) return;
            var want = Profile.Here(p).Where(x => x.HasBed).ToDictionary(x => x.Id);
            foreach (long gone in BedPins.Keys.Where(k => !want.ContainsKey(k)).ToList()) { Minimap.instance.RemovePin(BedPins[gone]); BedPins.Remove(gone); }
            foreach (Profile prof in want.Values)
            {
                if (!BedPins.TryGetValue(prof.Id, out Minimap.PinData pin) || pin == null)
                    BedPins[prof.Id] = pin = Minimap.instance.AddPin(prof.Bed, Minimap.PinType.Bed, $"{prof.Name}'s bed", false, false, 0L);
                pin.m_pos = prof.Bed;
            }
        }

        public static void Stop()
        {
            if (Minimap.instance != null) foreach (Minimap.PinData pin in BedPins.Values) Minimap.instance.RemovePin(pin);
            BedPins.Clear();
            _chestCache.Clear();
            AssignFor = null;
        }
    }

    // ---- beds -----------------------------------------------------------------------------------------------

    [HarmonyPatch(typeof(Bed), nameof(Bed.Interact))]
    internal static class Bed_Interact
    {
        private static bool Prefix(Bed __instance, bool repeat, ref bool __result)
        {
            if (repeat) return true;
            if (Home.AssignFor != null) { Home.ToggleBed(__instance, Home.AssignFor); __result = true; return false; }
            if (Home.IdOn(__instance) == 0L) return true;
            Plugin.Tell($"This is {Companion.Zdo(__instance).GetString(Home.HomeName, "a companion")}'s bed");
            __result = true;
            return false;
        }
    }

    [HarmonyPatch(typeof(Bed), nameof(Bed.GetHoverText))]
    internal static class Bed_GetHoverText
    {
        private static void Postfix(Bed __instance, ref string __result)
        {
            if (Home.AssignFor != null) { __result += $"\n<color=#7fe0c8>[E] {(Home.IdOn(__instance) == Companion.IdOf(Home.AssignFor) ? "Take back from" : "Give to")} {Companion.NameOf(Home.AssignFor)}</color>"; return; }
            if (Home.IdOn(__instance) != 0L) __result = $"{Companion.Zdo(__instance).GetString(Home.HomeName, "A companion")}'s bed";
        }
    }

    // ---- chests (the companion's own bag is handled in Patches: Container_Interact) ----------------------------

    [HarmonyPatch(typeof(Container), nameof(Container.Interact))]
    internal static class Container_Interact_Assign
    {
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(Container __instance, bool hold, ref bool __result)
        {
            if (hold || Home.AssignFor == null || !Home.IsChest(__instance)) return true;
            Home.ToggleChest(__instance, Home.AssignFor);
            __result = true;
            return false;
        }
    }

    [HarmonyPatch(typeof(Container), nameof(Container.GetHoverText))]
    internal static class Container_GetHoverText_Home
    {
        private static void Postfix(Container __instance, ref string __result)
        {
            if (!Home.IsChest(__instance)) return;
            if (Home.AssignFor != null) { __result += $"\n<color=#7fe0c8>[E] {(Home.IdOn(__instance) == Companion.IdOf(Home.AssignFor) ? "Take back from" : "Give to")} {Companion.NameOf(Home.AssignFor)}</color>"; return; }
            if (Home.IdOn(__instance) != 0L) __result += $"\n<color=#7fe0c8>{Companion.Zdo(__instance).GetString(Home.HomeName, "A companion")}'s chest</color>";
        }
    }
}
