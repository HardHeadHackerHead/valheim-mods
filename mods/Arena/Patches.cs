using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace Arena
{
    // Whichever of ZNetScene and ObjectDB wakes first, the standard is registered before a saved one is loaded (docs/modding-pitfalls.md).
    [HarmonyPatch(typeof(ZNetScene), "Awake")]
    internal static class ZNetScene_Awake { private static void Postfix() => Things.Register(); }

    [HarmonyPatch(typeof(ObjectDB), "Awake")]
    internal static class ObjectDB_Awake { private static void Postfix() { if (ZNetScene.instance != null) Things.Register(); } }

    /// <summary>
    /// Health in the ring. In a duel nobody dies: health is held at a fifth and that player has lost. In a contest you can die (unless dying in
    /// the ring is switched off: then you are carried out at a third instead). Every hit taken in a contest also costs a little of the crowd's favour.
    /// </summary>
    [HarmonyPatch(typeof(Character), nameof(Character.SetHealth))]
    internal static class Character_SetHealth_Ring
    {
        private static void Prefix(Character __instance, ref float health)
        {
            if (__instance != Player.m_localPlayer) return;
            float max = __instance.GetMaxHealth(), now = __instance.GetHealth();
            if (Duel.Fighting && health < max * 0.2f)
            {
                health = Mathf.Max(health, max * 0.2f);
                Duel.Lose();
                return;
            }
            if (!Contest.Active) return;
            if (health < now && max > 0f) Contest.OnHurt((now - health) / max);
            if (!Plugin.RealDeath.Value && health <= 0.5f)
            {
                health = max * 0.3f;
                Contest.Knockout();
            }
        }
    }

    /// <summary>Blocks and parries in a contest please the crowd.</summary>
    [HarmonyPatch(typeof(Humanoid), "BlockAttack")]
    internal static class Humanoid_BlockAttack_Crowd
    {
        private static readonly AccessTools.FieldRef<Humanoid, float> BlockTimer = AccessTools.FieldRefAccess<Humanoid, float>("m_blockTimer");

        private static void Prefix(Humanoid __instance, out bool __state)
        {
            float t = __instance == Player.m_localPlayer ? BlockTimer(__instance) : -1f;
            __state = t >= 0f && t < 0.25f;
        }

        private static void Postfix(Humanoid __instance, bool __result, bool __state)
        {
            if (__result && __instance == Player.m_localPlayer && Contest.Active) Contest.OnBlock(__state);
        }
    }

    /// <summary>
    /// Dying in a contest. On the arena's steel nothing of yours was in the ring: what the arena lent goes back and the coins you picked up
    /// (the purse) are lost, and the rest (the crowd's gifts, what you bought) goes in the tombstone as the game makes it. The tombstone, of
    /// that or of your own gear, is carried out to the forecourt, where you rise again. There is no death marker on the map (you rise beside
    /// your things), and the game's "where you died" point is the forecourt.
    /// </summary>
    [HarmonyPatch(typeof(Player), nameof(Player.OnDeath))]
    internal static class Player_OnDeath_Tomb
    {
        internal static Vector3? MovedTo;
        internal static Vector3 DiedAt;
        private static bool _inContest;

        private static readonly AccessTools.FieldRef<Character, HitData> LastHit = AccessTools.FieldRefAccess<Character, HitData>("m_lastHit");

        private static void Prefix(Player __instance)
        {
            MovedTo = null;
            _inContest = false;
            if (__instance == Player.m_localPlayer && Site.Known && Travel.Distance(__instance.transform.position) < 120f)
            {
                HitData hit = LastHit(__instance);
                Character by = hit?.GetAttacker();
                Plugin.Log.LogWarning($"Died at the arena (in a contest: {Contest.Active}): {(hit != null ? hit.m_hitType + " " + hit.GetTotalDamage().ToString("0.0") + " from " + (by != null ? by.name : "nothing") + " at " + hit.m_point : "no hit known")}, at {Site.Local(__instance.transform.position)} in the arena's frame");
            }
            // the arena's lent things go back to the armourer and the purse is lost; what is left (gifts, purchases) the game puts in the
            // tombstone, never deleted (your own things are with the Arena Master, and come back when you rise)
            if (__instance == Player.m_localPlayer && Kit.Stowed(__instance))
            {
                Kit.TakeBack(__instance);
                if (Contest.Active) Kit.LosePurse(__instance);
            }
            if (__instance != Player.m_localPlayer || !Contest.Active) return;
            _inContest = true;
            if (Plugin.RespawnAtArena.Value) Game_FindSpawnPoint_Arena.Pending = true;
            DiedAt = __instance.transform.position;
            Contest.NoteDeath();
        }

        private static void Postfix(Player __instance)
        {
            if (__instance != Player.m_localPlayer || !_inContest) return;
            _inContest = false;
            Vector3 spot = MovedTo ?? Site.World(Layout.Arrival.Pos);
            MovedTo = null;
            Game.instance?.GetPlayerProfile()?.SetDeathPoint(spot);
            if (Minimap.instance == null) return;
            var pins = AccessTools.Field(typeof(Minimap), "m_pins").GetValue(Minimap.instance) as List<Minimap.PinData>;
            foreach (Minimap.PinData pin in pins?.Where(p => p.m_type == Minimap.PinType.Death && (p.m_pos - DiedAt).sqrMagnitude < 9f).ToList() ?? new List<Minimap.PinData>())
                Minimap.instance.RemovePin(pin);
        }
    }

    [HarmonyPatch(typeof(Player), nameof(Player.CreateTombStone))]
    internal static class Player_CreateTombStone_Outside
    {
        private static void Prefix(Player __instance, out HashSet<TombStone> __state)
        {
            __state = __instance == Player.m_localPlayer && Contest.TombPending ? new HashSet<TombStone>(Object.FindObjectsOfType<TombStone>()) : null;
        }

        private static void Postfix(Player __instance, HashSet<TombStone> __state)
        {
            if (__state == null) return;
            TombStone tomb = Object.FindObjectsOfType<TombStone>().FirstOrDefault(t => t != null && !__state.Contains(t));
            if (tomb == null) return;
            Vector3 spot = Contest.TombSpot(__instance.transform.position);
            tomb.transform.position = spot;
            Rigidbody body = tomb.GetComponent<Rigidbody>();
            if (body != null) { body.position = spot; body.velocity = Vector3.zero; }
            ZNetView view = tomb.GetComponent<ZNetView>();
            // (the game keeps a tombstone near where it was made, and puts it back there: that point moves too)
            if (view != null && view.IsValid()) { view.GetZDO().SetPosition(spot); view.GetZDO().Set(ZDOVars.s_spawnPoint, spot); }
            Player_OnDeath_Tomb.MovedTo = spot;
            Plugin.Log.LogInfo($"Died in the ring: the tombstone was carried out to {spot}");
        }
    }

    // Logging out mid-fight: put things back (the weapon in the hand, the stake, the purse) before the character is saved.
    [HarmonyPatch(typeof(Game), "Shutdown")]
    internal static class Game_Shutdown
    {
        private static void Prefix() { Contest.Abort("You left the game."); Duel.Abort(true); }
    }

    // While the menu is open: keep the game from reacting to clicks and typing, show the mouse, and let Escape close only the menu.
    [HarmonyPatch(typeof(PlayerController), "TakeInput")]
    internal static class PlayerController_TakeInput
    {
        private static void Postfix(ref bool __result) { if (Window.IsOpen) __result = false; }
    }

    [HarmonyPatch(typeof(Player), "TakeInput")]
    internal static class Player_TakeInput
    {
        private static void Postfix(ref bool __result) { if (Window.IsOpen) __result = false; }
    }

    [HarmonyPatch(typeof(GameCamera), nameof(GameCamera.UpdateMouseCapture))]
    internal static class GameCamera_UpdateMouseCapture
    {
        private static bool Prefix()
        {
            if (!Window.IsOpen) return true;
            ZCursor.LockState = CursorLockMode.None;
            ZCursor.Show();
            return false;
        }
    }

    [HarmonyPatch(typeof(ZInput), nameof(ZInput.GetMouseScrollWheel))]
    internal static class ZInput_GetMouseScrollWheel
    {
        private static void Postfix(ref float __result) { if (Window.IsOpen) __result = 0f; }
    }

    [HarmonyPatch(typeof(Menu), "Update")]
    internal static class Menu_Update_EscapeCloses
    {
        private static bool Prefix()
        {
            if (!Window.IsOpen) return true;
            if (!(ZInput.GetKeyDown(KeyCode.Escape) || ZInput.GetButtonDown("JoyMenu"))) return true;
            Window.Close();
            return false;
        }
    }
}

namespace Arena
{
    /// <summary>
    /// Falling in a contest, you rise again at the arena: in its forecourt by the waystone, where the Arena Master gives your things back
    /// (and your tombstone waits, if you fought with your own gear). Otherwise the game's own spawn point (your bed) is used as always.
    /// </summary>
    [HarmonyPatch(typeof(Game), "FindSpawnPoint")]
    internal static class Game_FindSpawnPoint_Arena
    {
        internal static bool Pending;

        private static readonly AccessTools.FieldRef<Game, bool> AfterDeath = AccessTools.FieldRefAccess<Game, bool>("m_respawnAfterDeath");
        private static readonly AccessTools.FieldRef<Game, float> Wait = AccessTools.FieldRefAccess<Game, float>("m_respawnWait");

        private static bool Prefix(Game __instance, ref Vector3 point, ref bool usedLogoutPoint, float dt, ref bool __result)
        {
            if (!Pending || !AfterDeath(__instance) || !Site.Known || !Layout.Loaded) return true;
            usedLogoutPoint = false;
            Vector3 at = Site.World(Layout.Arrival.Pos + new Vector3(0f, 0f, 3f));
            ZNet.instance.SetReferencePosition(at);
            Wait(__instance) += dt;
            point = Vector3.zero;
            __result = false;
            if (Wait(__instance) <= __instance.m_respawnLoadDuration || !ZNetScene.instance.IsAreaReady(at)) return false;
            float ground = ZoneSystem.instance.GetGroundHeight(at, out float h) ? h : at.y;
            point = new Vector3(at.x, Mathf.Max(ground, Site.Origin.y - 0.5f) + 1.2f, at.z);   // (a little over the forecourt's floor, made when you get near)
            Pending = false;
            __result = true;
            Plugin.Log.LogInfo($"Rising again at the arena, at {point}");
            return false;
        }
    }
}

namespace Arena
{
    /// <summary>
    /// The arena's fighters drop no loot. Turning a creature's drops off is not enough: one that leaves a body (a greydwarf, a troll) gives
    /// its loot list to the body, which drops it when it fades. (On the arena's steel they drop coins instead: Armoury.Spill.)
    /// </summary>
    [HarmonyPatch(typeof(Ragdoll), nameof(Ragdoll.Setup))]
    internal static class Ragdoll_Setup_NoLoot
    {
        private static void Prefix(ref CharacterDrop characterDrop)
        {
            ZNetView view = characterDrop != null ? characterDrop.GetComponent<ZNetView>() : null;
            if (view != null && view.IsValid() && view.GetZDO().GetBool("dh_arena", false)) characterDrop = null;
        }
    }
}
