using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using static DHack.Shared.Translations;
using static AICompanion.Words;

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
                Plugin.Tell(T("aicompanion_home_no_longer_sleeps", Companion.NameOf(c)));
                return;
            }
            long owner = z.GetLong(ZDOVars.s_owner, 0L);
            if (owner != 0L) { Plugin.Tell(T("aicompanion_home_bed_players", Named(z.GetString(ZDOVars.s_ownerName, ""), "aicompanion_home_someone"), Companion.NameOf(c))); return; }
            if (z.GetLong(HomeKey, 0L) != 0L) { Plugin.Tell(T("aicompanion_home_bed_others", Named(z.GetString(HomeName, ""), "aicompanion_home_another_companion"))); return; }
            GiveBed(bed, c);
            Plugin.Tell(T("aicompanion_home_bed_now", Companion.NameOf(c)));
        }

        /// <summary>A name saved on a bed or chest, or (none saved) the words for someone.</summary>
        internal static string Named(string name, string key) => string.IsNullOrEmpty(name) ? T(key) : name;

        private static bool FreeBed(Bed b) { ZDO z = Companion.Zdo(b); return z != null && z.GetLong(ZDOVars.s_owner, 0L) == 0L && z.GetLong(HomeKey, 0L) == 0L; }

        private static void GiveBed(Bed bed, Humanoid c)
        {
            long id = Companion.IdOf(c);
            foreach (Bed old in Object.FindObjectsOfType<Bed>()) if (old != bed && IdOn(old) == id) { Claim(old); Companion.Zdo(old).Set(HomeKey, 0L); Companion.Zdo(old).Set(HomeName, ""); }
            ZDO z = Companion.Zdo(bed);
            Claim(bed); z.Set(HomeKey, id); z.Set(HomeName, Companion.NameOf(c));
            Vector3 spot = bed.GetSpawnPoint();
            Companion.Write(c, cz => { cz.Set(Keys.HasBed, true); cz.Set(Keys.BedPos, spot); });
            Plugin.Instance?.Note($"{Companion.NameOf(c)} was given the bed at {spot:F0}");
        }

        private static void GiveChest(Container chest, Humanoid c)
        {
            Claim(chest);
            Companion.Zdo(chest).Set(HomeKey, Companion.IdOf(c));
            Companion.Zdo(chest).Set(HomeName, Companion.NameOf(c));
            _chestCache.Clear();
        }

        /// <summary>
        /// Setting up home in one step: with no bed it takes the nearest free bed (nobody sleeps in it, no companion has it) within 40 m, and
        /// with no chests the empty chests (up to two) within 8 m of its bed. What it took, in its own words ("I've taken ..."), or null.
        /// Change them in the Home tab as before.
        /// </summary>
        public static string SetUp(Humanoid c)
        {
            ZDO z = Companion.Zdo(c);
            if (z == null) return null;
            string bedFar = null;   // the bed it took: how far (for its words)
            int chestsTaken = 0;
            Vector3 from = c.transform.position;
            Bed bed = null;
            if (!z.GetBool(Keys.HasBed, false))
            {
                bed = Object.FindObjectsOfType<Bed>().Where(b => FreeBed(b) && Vector3.Distance(b.transform.position, from) < 40f)
                            .OrderBy(b => Vector3.Distance(b.transform.position, from)).FirstOrDefault();
                if (bed != null) { GiveBed(bed, c); bedFar = Vector3.Distance(bed.transform.position, from).ToString("0"); }
            }
            else bed = BedOf(c);
            if (bed != null && Chests(c).Count == 0)
            {
                Vector3 at = bed.transform.position;
                List<Container> empty = Object.FindObjectsOfType<Container>()
                    .Where(x => IsChest(x) && IdOn(x) == 0L && !Containers.InUse(x) && x.GetInventory().NrOfItems() == 0 && Vector3.Distance(x.transform.position, at) < 8f)
                    .OrderBy(x => Vector3.Distance(x.transform.position, at)).Take(2).ToList();
                foreach (Container ch in empty) GiveChest(ch, c);
                chestsTaken = empty.Count;
            }
            // what it took, in its own words (packed text: said to its player, maybe on another game)
            if (bedFar == null) return chestsTaken == 0 ? null : P(chestsTaken == 1 ? "aicompanion_home_took_chest" : "aicompanion_home_took_chests");
            return chestsTaken == 0 ? P("aicompanion_home_took_bed", bedFar) : P(chestsTaken == 1 ? "aicompanion_home_took_bed_chest" : "aicompanion_home_took_bed_chests", bedFar);
        }

        // ---- the two orders ----------------------------------------------------------------------------------

        /// <summary>Come with me: it follows you on your adventure. (Automatic: it decided itself, Following.)</summary>
        public static bool Follow(Humanoid c, bool manual = true)
        {
            bool ok = Companion.Write(c, z => z.Set(Keys.Order, (int)Order.Follow));
            if (!ok) return false;
            BrainState st = Brain.Get(c);
            if (manual) { st.ManualOrderAt = Time.time; st.Outing = false; st.Remember(P("aicompanion_home_remember_called")); Errands.ForTheRoad(st); }
            return true;
        }

        /// <summary>
        /// Go home and live there: to its bed (it takes a free one near it the first time, and empty chests beside it), or, with no bed
        /// anywhere, around where it stands. Far from home, it sets off and is there a few seconds later (Work: TravelHome).
        /// </summary>
        public static bool GoHome(Humanoid c, bool quiet = false, bool manual = true)
        {
            string setUp = Companion.Zdo(c)?.GetBool(Keys.HasBed, false) == true && Chests(c).Count > 0 ? null : SetUp(c);
            bool ok = Companion.Write(c, z =>
            {
                z.Set(Keys.Order, (int)Order.Gather);
                if (!z.GetBool(Keys.HasBed, false)) z.Set(Keys.Post, c.transform.position);
            });
            if (!ok) return false;
            BrainState st = Brain.Get(c);
            st.Outing = false;
            st.NextArmoryLook = 0f; // a look through your chests for better gear, once it is back
            st.SortWhenHome = true; // what it brought back goes into your chests when it is there (Work.SortHome)
            if (manual) { st.ManualOrderAt = Time.time; st.Remember(P("aicompanion_home_remember_sent_home")); }
            bool bed = Companion.Zdo(c).GetBool(Keys.HasBed, false);
            if (setUp != null) Talk.Tell(c, setUp);
            else if (!bed && !quiet) Talk.Tell(c, P("aicompanion_home_no_free_bed"), "nobed", 10f);
            return true;
        }

        /// <summary>Holding the menu key: your companions near you all come with you, or (when any of them is with you already) all go home.</summary>
        public static void ToggleAll(Player p)
        {
            var near = Companion.All().Where(c => Companion.IsMine(c, p) && !c.IsDead() && Vector3.Distance(c.transform.position, p.transform.position) < 100f).ToList();
            if (near.Count == 0) { Plugin.Tell(T("aicompanion_home_none_near", Plugin.MenuKey.Value)); return; }
            bool home = near.Any(c => Companion.OrderOf(c) == Order.Follow);
            foreach (Humanoid c in near)
            {
                if (home) { if (GoHome(c, true)) Talk.Say(c, P("aicompanion_home_say_heading_home")); }
                else if (Follow(c)) Talk.Say(c, P("aicompanion_home_say_behind_you"));
            }
            string names = near.Select(Companion.NameOf).Aggregate((a, b) => T("aicompanion_home_and", a, b));
            bool one = near.Count == 1;
            Plugin.Tell(home ? T(one ? "aicompanion_home_go_home_one" : "aicompanion_home_go_home_many", names, Plugin.MenuKey.Value)
                             : T(one ? "aicompanion_home_come_one" : "aicompanion_home_come_many", names));
        }

        /// <summary>E on a chest while assigning: it becomes one of the companion's chests, or stops being one.</summary>
        public static void ToggleChest(Container chest, Humanoid c)
        {
            ZDO z = Companion.Zdo(chest);
            long id = Companion.IdOf(c);
            if (z == null) return;
            if (Containers.InUse(chest)) { Plugin.Tell(T("aicompanion_home_chest_open")); return; }
            bool mine = z.GetLong(HomeKey, 0L) == id;
            if (!mine && z.GetLong(HomeKey, 0L) != 0L) { Plugin.Tell(T("aicompanion_home_chest_others", Named(z.GetString(HomeName, ""), "aicompanion_home_another_companion"))); return; }
            Claim(chest);
            z.Set(HomeKey, mine ? 0L : id);
            z.Set(HomeName, mine ? "" : Companion.NameOf(c));
            _chestCache.Clear();
            int count = Chests(c).Count;
            Plugin.Tell(mine ? T("aicompanion_home_chest_let_go", Companion.NameOf(c)) : T(count == 1 ? "aicompanion_home_chest_taken_one" : "aicompanion_home_chest_taken_many", Companion.NameOf(c), count));
        }

        /// <summary>
        /// A chest a player built to keep things in (not a companion's bag, a tombstone, a cart's or ship's load, or a piece that only looks
        /// like a chest: NotStorage). The only containers a companion takes as its own, fills or takes from.
        /// </summary>
        public static bool IsChest(Container c)
        {
            if (c == null || c.GetComponent<TombStone>() != null || c.GetComponentInParent<Character>() != null) return false;
            Piece piece = c.GetComponentInParent<Piece>();
            if (piece == null || c.GetComponentInParent<Vagon>() != null || c.GetComponentInParent<Ship>() != null) return false;
            return Companion.Zdo(c) != null && !NotStorage(c); // (its own save data: where its claim is kept)
        }

        /// <summary>Built, with a Container, but not a chest to keep things in (the Obliterator, a Ledger Chest, the Arena's chests...): Shared/SpecialContainers.cs.</summary>
        public static bool NotStorage(Container c) => DHack.Shared.SpecialContainers.NotStorage(c);

        /// <summary>
        /// A companion that took one of those before they were left out (0.22.2 and older: an empty Ledger Chest or Obliterator beside its
        /// bed) lets go of it: only its claim is cleared, what is in it stays where it is. Not while someone has it open.
        /// </summary>
        private static void LetGo(Container chest, Humanoid c)
        {
            if (!Containers.Take(chest)) return; // (open on some game: next time)
            ZDO z = Companion.Zdo(chest);
            if (z == null) return;
            z.Set(HomeKey, 0L);
            z.Set(HomeName, "");
            string what = Localization.instance != null ? Localization.instance.Localize(chest.m_name) : chest.m_name;
            int items = chest.GetInventory()?.NrOfItems() ?? 0;
            Plugin.Instance?.Note($"{Companion.NameOf(c)} let go of the {what} at {chest.transform.position:F0}: it is not a chest to keep things in ({items} stacks left in it)");
            if (Player.m_localPlayer != null && Companion.IsMine(c, Player.m_localPlayer))
                Plugin.Tell(T(items > 0 ? "aicompanion_home_not_storage_items" : "aicompanion_home_not_storage", Companion.NameOf(c), what));
        }

        // ---- finding its things ------------------------------------------------------------------------------

        private static readonly Dictionary<long, KeyValuePair<float, List<Container>>> _chestCache = new Dictionary<long, KeyValuePair<float, List<Container>>>();

        /// <summary>Its chests that are loaded (near players), refreshed every few seconds.</summary>
        public static List<Container> Chests(Humanoid c)
        {
            long id = Companion.IdOf(c);
            if (_chestCache.TryGetValue(id, out var hit) && Time.time - hit.Key < 4f) return hit.Value.Where(x => x != null).ToList();
            List<Container> claimed = Object.FindObjectsOfType<Container>().Where(x => x != null && IdOn(x) == id).ToList();
            if (id != 0L) foreach (Container bad in claimed.Where(NotStorage)) LetGo(bad, c);
            List<Container> found = claimed.Where(IsChest).ToList();
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
                if (AssignFor == null || AssignFor.IsDead() || Input.GetKeyDown(KeyCode.Escape) || Plugin.MenuKey.Value.IsDown()) StopAssign(T("aicompanion_home_done_assigning"));
                else if (Time.time >= _nextHint) { _nextHint = Time.time + 6f; Plugin.Tell(T("aicompanion_home_assigning", Companion.NameOf(AssignFor), Plugin.MenuKey.Value)); }
            }
            if (Time.time < _nextTick) return;
            _nextTick = Time.time + 1f;

            // Keep each companion's profile up to date while it is near (it is what brings it back when it falls).
            if (Time.time >= _nextSnapshot)
            {
                _nextSnapshot = Time.time + 5f;
                foreach (Humanoid c in Companion.All().Where(c => Companion.IsMine(c, p) && !c.IsDead() && c.GetHealth() > 0f))
                {
                    // Not over its fall: a body that fell on another player's game still stands here (only the game running it knows it
                    // died) until it is taken away, and saving it as it looks would wipe out the fall, so it would never wake again.
                    Profile known = Profile.Find(p, Companion.IdOf(c));
                    if (known != null && known.Dead) continue;
                    Profile.Save(p, Profile.Of(c));
                }
            }

            // Bring back the fallen once their time is up.
            double now = ZNet.instance.GetTimeSeconds();
            foreach (Profile prof in Profile.Here(p).Where(x => x.Dead && now - x.DiedAt >= Plugin.RespawnSeconds.Value).ToList())
                Respawn(p, prof);

            UpdateBedPins(p);
        }

        /// <summary>On its player's game when it fell (directly, or told by the game that ran it).</summary>
        public static void MarkDead(Player p, long id, Vector3 where, Humanoid body = null, bool grave = true, string kept = null)
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
            Remote.Forget(id);       // (whether it is up somewhere: ask afresh)
            if (kept != null) prof.Kept = kept; // (told by an older version's game that ran it: it kept the gear apart; now it is in the tombstone)
            Profile.Save(p, prof);
            Plugin.Instance?.Note($"{prof.Name} will wake {(prof.HasBed ? "in their bed" : "beside you")} in {Plugin.RespawnSeconds.Value:0} s");
        }

        /// <summary>
        /// Wake it (its time is up, "Wake now", or "Bring back"). Never a second one: if it is up anywhere in the world its record just
        /// stops saying it fell. Only the game hosting the world knows that for sure; another player's game asks it first (Remote.InWorld)
        /// and wakes it only once the answer is "nowhere" (null until then: asked again on the next tick).
        /// </summary>
        public static Humanoid Respawn(Player p, Profile prof)
        {
            Humanoid up = Companion.All().FirstOrDefault(h => Companion.IdOf(h) == prof.Id && !h.IsDead() && h.GetHealth() > 0f);
            ZDO away = up == null && Remote.Sure ? Remote.Find(prof.Id) : null;
            bool? elsewhere = up != null || Remote.Sure ? (bool?)false : Remote.InWorld(prof.Id); // (a client: what the host says)
            if (up != null || (away != null && away.GetFloat(ZDOVars.s_health, 1f) > 0f) || elsewhere == true)
            {
                prof.Dead = false;
                if (elsewhere != true) prof.Kept = ""; // (seen up: what it kept was given back. Only the host's word: keep it, in case it was old news)
                Profile.Save(p, prof);
                Plugin.Instance?.Note($"{prof.Name} is already up: not woken again");
                return up;
            }
            if (elsewhere == null) return null; // (the host has not answered yet)
            GameObject prefab = Prefab.Get();
            if (prefab == null) return null;
            Vector3 pos = prof.HasBed ? prof.Bed : p.transform.position - p.transform.forward * 2f;
            if (!prof.HasBed && ZoneSystem.instance != null && ZoneSystem.instance.GetSolidHeight(pos, out float h)) pos.y = Mathf.Max(pos.y, h);
            GameObject go = Object.Instantiate(prefab, pos + Vector3.up * 0.2f, Quaternion.identity);
            Humanoid c = go.GetComponent<Humanoid>();
            ZDO z = Companion.Zdo(c);
            z.Set(Keys.Master, p.GetPlayerID());
            z.Set(Keys.MasterName, p.GetPlayerName());
            if (!prof.Dead && Remote.TombOf(prof.Id) is Vector3 tomb) { prof.DiedPos = tomb; prof.HasGrave = true; } // (brought back: it fell where its player's game never heard of it)
            prof.ApplyTo(c);
            int kept = Companion.Unpack(c, prof.Kept); // (a fall from before 0.22.0: the gear it wore was kept apart)
            kept += Grave.WearAgain(c, prof.HasGrave || prof.Dead ? prof.DiedPos : Vector3.zero, out bool left); // its gear back out of its tombstone, when it is here
            if (kept > 0) Plugin.Instance?.Note($"{prof.Name} woke wearing its gear ({kept})");
            prof.Kept = "";
            Companion.Zdo(c)?.Set(Companion.KeptKey, "");
            prof.HasGrave = prof.HasGrave && left;
            Companion.Zdo(c)?.Set(Grave.HasKey, prof.HasGrave);
            prof.Dead = false;
            Profile.Save(p, prof);
            string where = prof.HasBed ? $"in {(prof.Model == 1 ? "her" : "his")} bed" : "beside you";   // (for the log)
            Plugin.Tell(T(!prof.HasBed ? "aicompanion_home_wakes_beside" : prof.Model == 1 ? "aicompanion_home_wakes_bed_her" : "aicompanion_home_wakes_bed_his", prof.Name));
            Brain.Get(c).WokeAt = Time.time; // up after a fall: careful for a while (Tactics.Careful)
            if (prof.HasGrave)
            {
                bool atHome = prof.HasBed && Vector3.Distance(prof.DiedPos, Work.Center(c)) < Work.RadiusOf(c) + 10f; // (Grave: it fetches those itself)
                Talk.Tell(c, atHome ? P("aicompanion_home_up_get_things") : P("aicompanion_home_up_things_far", Remote.FromHere(prof.DiedPos - pos)), "woke", 0.5f);
            }
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
                    BedPins[prof.Id] = pin = Minimap.instance.AddPin(prof.Bed, Minimap.PinType.Bed, T("aicompanion_home_bed_of", prof.Name), false, false, 0L);
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
            Plugin.Tell(T("aicompanion_home_this_bed_of", Home.Named(Companion.Zdo(__instance).GetString(Home.HomeName, ""), "aicompanion_home_a_companion")));
            __result = true;
            return false;
        }
    }

    [HarmonyPatch(typeof(Bed), nameof(Bed.GetHoverText))]
    internal static class Bed_GetHoverText
    {
        private static void Postfix(Bed __instance, ref string __result)
        {
            if (Home.AssignFor != null) { __result += "\n<color=#7fe0c8>[E] " + T(Home.IdOn(__instance) == Companion.IdOf(Home.AssignFor) ? "aicompanion_home_take_back_from" : "aicompanion_home_give_to", Companion.NameOf(Home.AssignFor)) + "</color>"; return; }
            if (Home.IdOn(__instance) != 0L) __result = T("aicompanion_home_bed_of", Home.Named(Companion.Zdo(__instance).GetString(Home.HomeName, ""), "aicompanion_home_a_companion_start"));
        }
    }

    // ---- chests (the companion's own bag is handled in Patches: Container_Interact) ----------------------------

    [HarmonyPatch(typeof(Container), nameof(Container.Interact))]
    internal static class Container_Interact_Assign
    {
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(Container __instance, bool hold, ref bool __result)
        {
            if (hold || Home.AssignFor == null) return true;
            if (Home.NotStorage(__instance) && __instance.GetComponentInParent<Piece>() != null)
            {
                Plugin.Tell(T("aicompanion_home_not_a_chest", Companion.NameOf(Home.AssignFor)));
                __result = true;
                return false;
            }
            if (!Home.IsChest(__instance)) return true;
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
            if (Home.AssignFor != null) { __result += "\n<color=#7fe0c8>[E] " + T(Home.IdOn(__instance) == Companion.IdOf(Home.AssignFor) ? "aicompanion_home_take_back_from" : "aicompanion_home_give_to", Companion.NameOf(Home.AssignFor)) + "</color>"; return; }
            if (Home.IdOn(__instance) != 0L) __result += "\n<color=#7fe0c8>" + T("aicompanion_home_chest_of", Home.Named(Companion.Zdo(__instance).GetString(Home.HomeName, ""), "aicompanion_home_a_companion_start")) + "</color>";
        }
    }
}
