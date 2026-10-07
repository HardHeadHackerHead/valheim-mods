using HarmonyLib;
using UnityEngine;

namespace AICompanion
{
    internal static class Patches
    {
        internal static bool OpeningGear; // set while the menu's "Open its inventory" opens the gear chest
    }

    // ---- the prefab, registered before any saved companion loads (docs/modding-pitfalls.md) ----

    [HarmonyPatch(typeof(ZNetScene), "Awake")]
    internal static class ZNetScene_Awake
    {
        private static void Postfix(ZNetScene __instance) => Prefab.Register(__instance);
    }

    [HarmonyPatch(typeof(ObjectDB), "Awake")]
    internal static class ObjectDB_Awake
    {
        private static void Postfix() { if (ZNetScene.instance != null) Prefab.Register(ZNetScene.instance); }
    }

    // ---- the body ----

    // Its gear chest and its hands share one inventory.
    [HarmonyPatch(typeof(Container), "Awake")]
    internal static class Container_Awake
    {
        private static void Postfix(Container __instance) { if (Companion.Is(__instance)) Companion.ShareInventory(__instance); }
    }

    // Our brain instead of the game's monster AI, for companions only.
    [HarmonyPatch(typeof(MonsterAI), nameof(MonsterAI.UpdateAI))]
    internal static class MonsterAI_UpdateAI
    {
        private static float _lastError;

        private static bool Prefix(MonsterAI __instance, float dt, ref bool __result)
        {
            if (!Companion.Is(__instance)) return true;
            try { __result = Brain.Update(__instance, dt); }
            catch (System.Exception e)
            {
                __result = true;
                if (Time.time - _lastError > 10f) { _lastError = Time.time; Plugin.Instance?.Warn("Companion brain: " + e); }
            }
            return false;
        }
    }

    // E on a companion opens its menu; the menu's own button opens the gear (for its owner only).
    [HarmonyPatch(typeof(Container), nameof(Container.Interact))]
    internal static class Container_Interact
    {
        private static bool Prefix(Container __instance, Humanoid character, bool hold, ref bool __result)
        {
            if (Patches.OpeningGear || !Companion.Is(__instance)) return true;
            __result = true;
            if (!hold && character is Player p && p == Player.m_localPlayer && Plugin.Instance != null)
                Plugin.Instance.OpenMenuFor(p, __instance.GetComponent<Humanoid>());
            return false;
        }
    }

    [HarmonyPatch(typeof(Character), nameof(Character.GetHoverText))]
    internal static class Character_GetHoverText
    {
        private static void Postfix(Character __instance, ref string __result)
        {
            if (!Companion.Is(__instance)) return;
            string status = Companion.StatusOf(__instance);
            __result = Localization.instance.Localize($"{Companion.NameOf(__instance)}\n[<color=yellow><b>$KEY_Use</b></color>] Menu") +
                       (string.IsNullOrEmpty(status) ? "" : $"\n<size=14>{status}</size>");
        }
    }

    [HarmonyPatch(typeof(Container), nameof(Container.GetHoverText))]
    internal static class Container_GetHoverText
    {
        private static void Postfix(Container __instance, ref string __result)
        {
            if (Companion.Is(__instance)) __result = Localization.instance.Localize($"{Companion.NameOf(__instance)}\n[<color=yellow><b>$KEY_Use</b></color>] Menu");
        }
    }

    [HarmonyPatch(typeof(Character), nameof(Character.GetHoverName))]
    internal static class Character_GetHoverName
    {
        private static void Postfix(Character __instance, ref string __result) { if (Companion.Is(__instance)) __result = Companion.NameOf(__instance); }
    }

    // Its armour counts (the game applies armour to players only), and players' swings never hurt it.
    [HarmonyPatch(typeof(Character), "RPC_Damage")]
    internal static class Character_RPC_Damage
    {
        private static bool Prefix(Character __instance, HitData hit)
        {
            if (!(__instance is Humanoid h) || !Companion.Is(h)) return true;
            Character attacker = hit.GetAttacker();
            if (attacker != null && attacker.IsPlayer()) return false;
            float armor = Companion.Armor(h);
            if (armor > 0f) hit.ApplyArmor(armor);
            return true;
        }
    }

    // Damage a companion deals, for its fight summary (counted on the game that runs the enemy, which is usually its own).
    [HarmonyPatch(typeof(Character), "RPC_Damage")]
    internal static class Character_RPC_Damage_Dealt
    {
        private static void Prefix(Character __instance, out float __state) => __state = __instance.GetHealth();

        private static void Postfix(Character __instance, HitData hit, float __state)
        {
            Character attacker = hit?.GetAttacker();
            if (attacker == null || attacker == __instance || !Companion.Is(attacker)) return;
            float done = __state - Mathf.Max(0f, __instance.GetHealth());
            if (done > 0f && Brain.Get(attacker as Humanoid) is BrainState st) st.Dealt += done;
        }
    }

    // Player-built things that hurt whoever touches them (sharpened stakes, spike traps) leave companions alone, as they leave players alone.
    [HarmonyPatch(typeof(Aoe), "ShouldHit")]
    internal static class Aoe_ShouldHit
    {
        private static void Postfix(Aoe __instance, Collider collider, ref bool __result)
        {
            if (!__result || collider == null) return;
            Character c = collider.GetComponentInParent<Character>();
            if (c == null || !Companion.Is(c)) return;
            Piece piece = __instance.GetComponentInParent<Piece>();
            if (piece != null && piece.IsPlacedByPlayer()) __result = false;
        }
    }

    // It falls: its gear goes into a crate where it stood. (And a kill of its own is counted, on the game that runs it.)
    [HarmonyPatch(typeof(Character), nameof(Character.OnDeath))]
    internal static class Character_OnDeath
    {
        private static readonly AccessTools.FieldRef<Character, HitData> LastHit = AccessTools.FieldRefAccess<Character, HitData>("m_lastHit");

        private static void Prefix(Character __instance)
        {
            Character killer = LastHit(__instance)?.GetAttacker();
            if (killer != null && killer != __instance && Companion.Is(killer) && killer.GetComponent<ZNetView>().IsOwner())
            {
                ZDO z = Companion.Zdo(killer);
                z.Set(Keys.Kills, z.GetInt(Keys.Kills, 0) + 1);
                BrainState ks = Brain.Get(killer as Humanoid);
                if (ks != null) { ks.FightKills++; ks.Remember("killed " + Localization.instance.Localize(__instance.m_name)); }
            }
            if (!(__instance is Humanoid h) || !Companion.Is(h)) return;
            BrainState st = Brain.Get(h);
            if (st != null && st.InCombat) { st.Taken += Mathf.Max(0f, st.LastHealth); Brain.Summarise(st, "fell"); }
            ZNetView view = h.GetComponent<ZNetView>();
            if (!view.IsOwner()) return;
            try
            {
                Plugin.Instance?.Note($"{Companion.NameOf(h)} fell at {h.transform.position:F0} (killed by {LastHit(h)?.GetAttacker()?.m_name ?? "?"}, carrying {h.GetInventory().NrOfItems()} item stacks)");
                Companion.DropGear(h);
                Player master = Companion.Master(h);
                if (master == Player.m_localPlayer)
                {
                    Home.MarkDead(master, Companion.IdOf(h), h.transform.position, h);
                    bool bed = Companion.Zdo(h).GetBool(Keys.HasBed, false);
                    Plugin.Tell($"{Companion.NameOf(h)} has fallen. Their gear is in their tombstone (the skull on your map). They wake {(bed ? "in their bed" : "beside you")} in {Plugin.RespawnSeconds.Value:0} s.");
                }
                Net.AnnounceFall(h, h.transform.position);
            }
            catch (System.Exception e) { Plugin.Instance?.Warn("Could not put the fallen companion's gear in a crate: " + e); }
        }
    }

    // The map draws every pin white each frame: give companions' pins their own colour, so they are not taken for players.
    [HarmonyPatch(typeof(Minimap), "UpdatePins")]
    internal static class Minimap_UpdatePins
    {
        private static readonly AccessTools.FieldRef<Minimap, System.Collections.Generic.List<Minimap.PinData>> Pins =
            AccessTools.FieldRefAccess<Minimap, System.Collections.Generic.List<Minimap.PinData>>("m_pins");

        private static void Postfix(Minimap __instance)
        {
            foreach (Minimap.PinData pin in Pins(__instance))
            {
                if (pin.m_iconElement == null || !Net.IsLivePin(pin)) continue;
                pin.m_iconElement.color = Net.PinColor;
                if (pin.m_NamePinData != null && pin.m_NamePinData.PinNameText != null) pin.m_NamePinData.PinNameText.color = Net.PinColor;
            }
        }
    }

    // Through a portal with its player: a companion following within 25 m goes too (unless it carries what portals refuse, like ore).
    [HarmonyPatch(typeof(Player), nameof(Player.TeleportTo))]
    internal static class Player_TeleportTo
    {
        private static void Postfix(Player __instance, Vector3 pos, Quaternion rot, bool __result)
        {
            if (!__result || __instance != Player.m_localPlayer) return;
            foreach (Humanoid c in Companion.All())
            {
                if (!Companion.IsMine(c, __instance) || Companion.OrderOf(c) != Order.Follow) continue;
                if (Vector3.Distance(c.transform.position, __instance.transform.position) > 25f) continue;
                if (!c.IsTeleportable(false)) { Plugin.Tell($"{Companion.NameOf(c)} cannot go through: they carry something the portal refuses"); continue; }
                ZNetView view = c.GetComponent<ZNetView>();
                if (!view.IsOwner()) view.ClaimOwnership();
                Vector3 to = pos - rot * Vector3.forward * 2f;
                c.transform.position = to;
                Rigidbody body = c.GetComponent<Rigidbody>();
                if (body != null) { body.position = to; body.linearVelocity = Vector3.zero; }
                view.GetZDO().SetPosition(to); // it is unloaded here at once; it appears there when you arrive
                Brain.Get(c)?.Remember("came through the portal");
                Plugin.Instance?.Note($"{Companion.NameOf(c)} goes through the portal with {__instance.GetPlayerName()}");
            }
        }
    }

    // ---- while the menu is open: the game ignores clicks, keys and the wheel, and Escape closes only the menu ----

    [HarmonyPatch(typeof(PlayerController), "TakeInput")]
    internal static class PlayerController_TakeInput
    {
        private static void Postfix(ref bool __result) { if (Plugin.MenuOpen) __result = false; }
    }

    [HarmonyPatch(typeof(Player), "TakeInput")]
    internal static class Player_TakeInput
    {
        private static void Postfix(ref bool __result) { if (Plugin.MenuOpen) __result = false; }
    }

    [HarmonyPatch(typeof(GameCamera), nameof(GameCamera.UpdateMouseCapture))]
    internal static class GameCamera_UpdateMouseCapture
    {
        private static bool Prefix()
        {
            if (!Plugin.MenuOpen) return true;
            ZCursor.LockState = CursorLockMode.None;
            ZCursor.Show();
            return false;
        }
    }

    [HarmonyPatch(typeof(ZInput), nameof(ZInput.GetMouseScrollWheel))]
    internal static class ZInput_GetMouseScrollWheel
    {
        private static void Postfix(ref float __result) { if (Plugin.MenuOpen) __result = 0f; }
    }

    [HarmonyPatch(typeof(Menu), "Update")]
    internal static class Menu_Update_EscapeCloses
    {
        private static bool Prefix()
        {
            if (!Plugin.MenuOpen) return true;
            if (!(ZInput.GetKeyDown(KeyCode.Escape) || ZInput.GetButtonDown("JoyMenu"))) return true;
            Plugin.CloseFromEscape();
            return false;
        }
    }
}
