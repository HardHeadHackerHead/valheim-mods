using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace Terminal
{
    /// <summary>
    /// The Proxy's prefab: a copy of the game's Player prefab without the parts that make it *the* player (input, profile, skills, talking),
    /// with a plain Humanoid in their place and ProxyBody, which a terminal's driver feeds with keys. The pattern is AICompanion's
    /// (mods/AICompanion/Prefab.cs): only game components besides ProxyBody, so a reload never leaves old code running on a Proxy.
    ///
    /// Registered like a piece (see docs/modding-pitfalls.md): from ZNetScene.Awake and ObjectDB.Awake and on a hot reload. Without the
    /// mod a saved Proxy is deleted by the host (the terminal that makes one is gated by NeedsServer).
    /// </summary>
    internal static class ProxyPrefab
    {
        public const string PrefabName = "dh_terminal_proxy";
        public static readonly int Hash = PrefabName.GetStableHashCode();

        private static GameObject _holder, _prefab;
        internal static GameObject Tombstone;   // the player's own tombstone prefab (Player.m_tombstone)
        private static readonly AccessTools.FieldRef<ZNetScene, Dictionary<int, GameObject>> Named =
            AccessTools.FieldRefAccess<ZNetScene, Dictionary<int, GameObject>>("m_namedPrefabs");

        public static void Register(ZNetScene scene)
        {
            if (_prefab == null)
            {
                GameObject player = scene.GetPrefab("Player");
                if (player == null) { Plugin.Log.LogWarning("The Player prefab was not found, so there is no Proxy"); return; }
                _holder = new GameObject(Plugin.Name + "ProxyPrefabs");
                _holder.SetActive(false);   // the copy must not wake up as a real object
                Object.DontDestroyOnLoad(_holder);
                _prefab = Make(player);
            }
            // A hot reload: the old copy left its prefab registered (Unregister) so no Proxy is ever without one; ours replaces it.
            Dictionary<int, GameObject> named = Named(scene);
            if (named.TryGetValue(Hash, out GameObject old) && old != null && old != _prefab && old.name == PrefabName)
            {
                scene.m_prefabs.Remove(old);
                if (old.transform.parent != null && old.transform.parent.name == Plugin.Name + "ProxyPrefabs") Object.Destroy(old.transform.parent.gameObject);
            }
            if (!scene.m_prefabs.Contains(_prefab)) scene.m_prefabs.Add(_prefab);
            named[Hash] = _prefab;
            // Proxies already standing in the world run an older copy's ProxyBody after a reload: give them this copy's.
            int swapped = DHack.Shared.PieceReload.Reattach(scene, _prefab);
            if (swapped > 0) Plugin.Log.LogInfo($"{swapped} Proxy(ies) in the world now run this copy");
        }

        /// <summary>Unloading: the prefab stays registered (a host loading an area before the next copy is up would delete every Proxy).</summary>
        public static void Unregister()
        {
            _prefab = null;
            _holder = null;
        }

        private static GameObject Make(GameObject source)
        {
            GameObject go = Object.Instantiate(source, _holder.transform, false);
            go.name = PrefabName;
            Humanoid original = source.GetComponent<Humanoid>();
            Tombstone = (original as Player)?.m_tombstone ?? Tombstone;   // (what a Proxy leaves when it dies carrying loot)

            foreach (System.Type t in new[] { typeof(PlayerController), typeof(Player), typeof(Talker), typeof(Skills) })
            {
                Component c = go.GetComponent(t);
                if (c != null) Object.DestroyImmediate(c);
            }

            Humanoid h = go.AddComponent<Humanoid>();
            if (original != null)
            {
                h.m_unarmedWeapon = original.m_unarmedWeapon;
                h.m_consumeItemEffects = original.m_consumeItemEffects;
                h.m_equipEffects = original.m_equipEffects;
                h.m_hitEffects = original.m_hitEffects;
                h.m_critHitEffects = original.m_critHitEffects;
                h.m_backstabHitEffects = original.m_backstabHitEffects;
                h.m_deathEffects = original.m_deathEffects;
            }
            h.m_name = "$terminal_proxy";
            h.m_group = "dh_terminal_proxy";
            h.m_faction = Character.Faction.Players;
            h.m_health = Plugin.ProxyHealth.Value;
            h.m_regenAllHPTime = 300f;
            h.m_walkSpeed = 1.6f; h.m_runSpeed = 7f; h.m_speed = 4f; h.m_crouchSpeed = 2f;
            h.m_turnSpeed = 300f; h.m_runTurnSpeed = 300f; h.m_acceleration = 1f;
            h.m_jumpForce = 8f; h.m_jumpForceForward = 2f;
            h.m_canSwim = true; h.m_swimDepth = 1.6f; h.m_swimSpeed = 2f; h.m_swimTurnSpeed = 100f; h.m_swimAcceleration = 0.05f;
            h.m_groundTilt = Character.GroundTiltType.None;
            h.m_eye = Utils.FindChild(go.transform, "EyePos");
            h.m_tolerateWater = true;
            h.m_staggerWhenBlocked = true;
            h.m_damageModifiers = new HitData.DamageModifiers
            {
                m_chop = HitData.DamageModifier.Immune, m_pickaxe = HitData.DamageModifier.Immune, m_spirit = HitData.DamageModifier.Immune,
            };

            // The animation events (footsteps, the moment a swing hits) were wired to the Player we removed.
            CharacterAnimEvent anim = go.GetComponentInChildren<CharacterAnimEvent>(true);
            if (anim != null) AccessTools.Field(typeof(CharacterAnimEvent), "m_character")?.SetValue(anim, h);

            // The game's health bars (EnemyHud) and combat code expect every creature to have an AI; this one has the game's own with its
            // thinking switched off (MonsterAI_UpdateAI below): the driver is the Proxy's only mind.
            MonsterAI ai = go.AddComponent<MonsterAI>();
            ai.m_viewRange = 1f; ai.m_viewAngle = 1f; ai.m_hearRange = 1f;
            ai.m_alertedEffects = new EffectList(); ai.m_idleSound = new EffectList(); ai.m_idleSoundChance = 0f;
            ai.m_pathAgentType = Pathfinding.AgentType.Humanoid;
            ai.m_aggravatable = false; ai.m_attackPlayerObjects = false;
            ai.m_consumeItems = new List<ItemDrop>();

            ZNetView view = go.GetComponent<ZNetView>();
            view.m_persistent = true;
            view.m_distant = false;
            view.m_type = ZDO.ObjectType.Default;
            ZSyncTransform sync = go.GetComponent<ZSyncTransform>();
            if (sync != null) { sync.m_syncPosition = true; sync.m_syncRotation = true; sync.m_syncBodyVelocity = false; sync.m_characterParentSync = false; }
            Rigidbody body = go.GetComponent<Rigidbody>();
            if (body != null) body.mass = 50f;

            go.AddComponent<ProxyBody>();
            return go;
        }
    }

    // No thinking of its own: the Proxy only does what its driver presses (and stands still when nobody does).
    [HarmonyPatch(typeof(MonsterAI), nameof(MonsterAI.UpdateAI))]
    internal static class MonsterAI_UpdateAI
    {
        private static bool Prefix(MonsterAI __instance, ref bool __result)
        {
            if (__instance.GetComponent<ProxyBody>() == null) return true;
            __result = true;
            return false;
        }
    }

    // A Proxy that dies is gone (the game's own death: a ragdoll, then the object is removed) and its terminal starts the respawn clock.
    [HarmonyPatch(typeof(Character), nameof(Character.OnDeath))]
    internal static class Character_OnDeath
    {
        private static void Prefix(Character __instance)
        {
            ProxyBody proxy = __instance.GetComponent<ProxyBody>();
            if (proxy != null) proxy.Died();
        }
    }

    /// <summary>
    /// What makes a Humanoid the Proxy. On the game that owns it (whoever is driving, or whoever is near when nobody is) it takes the
    /// driver's keys (Feed), puts on its starting gear, and stands still when nothing is fed. On every game it shows who drives it and
    /// where it is looking (the terminals' screens read both).
    /// </summary>
    internal partial class ProxyBody : MonoBehaviour
    {
        internal static readonly List<ProxyBody> All = new List<ProxyBody>();   // our own list instead of scene searches (pitfall: big jobs on a timer)

        internal const string KeyId = "dh_tm_id", KeyDriver = "dh_tm_driver", KeyBeat = "dh_tm_beat", KeyAim = "dh_tm_aim", KeyHome = "dh_tm_home", KeyBorn = "dh_tm_born";

        internal Humanoid Body;
        internal ZNetView View;
        private float _fed = -10f, _nextAim;
        private float _yaw, _pitch;
        private int _lastAim = int.MinValue;

        /// <summary>This Proxy's id (a terminal finds its Proxy by it: never a ZDOID, which changes at every world load).</summary>
        internal string Id => View != null && View.GetZDO() != null ? View.GetZDO().GetString(KeyId, "") : "";
        internal bool IsOwner => View != null && View.IsValid() && View.IsOwner();

        private void Awake()
        {
            Body = GetComponent<Humanoid>();
            View = GetComponent<ZNetView>();
            if (View == null || View.GetZDO() == null) return;   // (a copy that isn't a real object)
            All.Add(this);
        }

        private void OnDestroy() => All.Remove(this);

        private void Start()
        {
            if (!IsOwner) return;
            ZDO zdo = View.GetZDO();
            if (zdo.GetInt(KeyBorn, 0) == 0)   // the first moment of this Proxy's life: its health from the settings
            {
                zdo.Set(KeyBorn, 1);
                Body.SetMaxHealth(Plugin.ProxyHealth.Value);
                Body.SetHealth(Plugin.ProxyHealth.Value);
            }
        }

        /// <summary>The name over its head is whoever drives it (the game's health bar reads it every frame), "Proxy" when nobody does.</summary>
        private void Name()
        {
            if (Body == null || View == null || View.GetZDO() == null) return;
            string driver = Driven ? Driver : "";
            string name = driver.Length > 0 ? driver : "$terminal_proxy";
            if (Body.m_name != name) Body.m_name = name;
        }

        private void Update()
        {
            Name();
            if (!IsOwner) return;
            StaminaTick();
            AutoTick();
            BagTick();   // (the bag: loaded when this game takes the Proxy over, saved when it changes, loot picked up)
            if (Time.time - _fed > 0.3f && !HasGoal) { Body.SetMoveDir(Vector3.zero); Body.SetRun(false); }   // (nothing is pressed and no script has sent it somewhere: it stands still)
        }

        // ---- who drives it, where it looks (any game reads these) ----

        internal string Driver => View != null && View.GetZDO() != null ? View.GetZDO().GetString(KeyDriver, "") : "";

        /// <summary>Someone is at the keys: they set the beat every second, so a driver who vanished frees the Proxy after a few seconds.</summary>
        internal bool Driven => Driver.Length > 0 && ZNet.instance != null && ZNet.instance.GetTimeSeconds() - View.GetZDO().GetFloat(KeyBeat, -100f) < 4.0;

        /// <summary>The look direction the driver sets, as a yaw (degrees, compass) and a pitch (degrees up); the terminals' camera follows it.</summary>
        internal void Aim(out float yaw, out float pitch)
        {
            if (!IsOwner || Time.time - _fed > 1f)   // not fed here: read what the driver's game sent
            {
                int packed = View.GetZDO().GetInt(KeyAim, 0);
                yaw = (packed / 2000) / 10f; pitch = (packed % 2000 - 900) / 10f;
                if (yaw == 0f && pitch == -90f) { yaw = transform.eulerAngles.y; pitch = 10f; }
            }
            else { yaw = _yaw; pitch = _pitch; }
        }

        internal void SetDriver(string name)
        {
            ZDO zdo = View.GetZDO();
            zdo.Set(KeyDriver, name ?? "");
            zdo.Set(KeyBeat, (float)ZNet.instance.GetTimeSeconds());
        }

        internal void Beat() => View.GetZDO().Set(KeyBeat, (float)ZNet.instance.GetTimeSeconds());

        // ---- the driver's keys ----

        /// <summary>One tick of the driver's keys: the way the player pressed them (z forward, x right) and the way the camera looks.</summary>
        internal void Feed(Vector3 move, bool run, bool jump, bool attack, bool attackHold, bool secondary, bool secondaryHold, bool blockHold, Vector3 look, bool crouch = false)
        {
            if (!IsOwner || Body == null || Body.IsDead()) return;
            _fed = Time.time;
            look.Normalize();
            _yaw = Quaternion.LookRotation(look).eulerAngles.y;
            _pitch = Mathf.Asin(Mathf.Clamp(look.y, -1f, 1f)) * Mathf.Rad2Deg;   // degrees looking up (negative: down)
            Vector3 flat = look; flat.y = 0f; flat.Normalize();
            Vector3 dir = move.z * flat + move.x * Vector3.Cross(Vector3.up, flat);
            if (crouch) Crouch(!_crouching);
            bool moving = dir.sqrMagnitude > 0.01f;
            bool running = run && moving && !_crouching && Drain(12f);
            Body.SetMoveDir(dir);
            Body.SetRun(running);
            bool fighting = attack || attackHold || secondary || secondaryHold || blockHold;
            if (fighting) Body.SetLookDir(flat, 0f);
            else if (dir.sqrMagnitude > 0.01f) Body.SetLookDir(dir.normalized, 0f);
            bool blocking = blockHold && Drain(4f);
            if (Blocking != null) Blocking.SetValue(Body, blocking);
            if (jump && Spend(8f)) Body.Jump(false);
            if ((attack || secondary) && Spend(10f)) Body.StartAttack(null, secondary);

            if (Time.time >= _nextAim)   // (a small number into the ZDO, and not on every tick)
            {
                _nextAim = Time.time + 0.1f;
                int packed = Mathf.RoundToInt(Mathf.Repeat(_yaw, 360f) * 10f) * 2000 + Mathf.RoundToInt((_pitch + 90f) * 10f);
                if (packed != _lastAim) { _lastAim = packed; View.GetZDO().Set(KeyAim, packed); }
            }
        }

        /// <summary>
        /// The Proxy has died. The game that owns it starts the respawn clock on its home terminal (the terminal's saved data: a Proxy
        /// can't be made again until it runs out), and its driver is stood up with the news.
        /// </summary>
        internal void Died()
        {
            try
            {
                if (View == null || !View.IsValid()) return;
                if (View.IsOwner())
                {
                    Tombstone_();
                    string home = View.GetZDO().GetString(KeyHome, "");
                    foreach (TerminalPiece t in TerminalPiece.All)
                    {
                        if (t.Id != home || t.Nview == null || t.Nview.GetZDO() == null) continue;
                        t.Nview.ClaimOwnership();
                        t.Nview.GetZDO().Set(TerminalPiece.KeyRespawn, (long)(ZNet.instance.GetTimeSeconds() + Plugin.RespawnSeconds.Value));
                        t.Crashed(ValheimApp.AppId);   // (the game "stops working": the desktop, with a message)
                        break;
                    }
                }
                if (Drive.Uses(this)) Player.m_localPlayer?.Message(MessageHud.MessageType.Center, DHack.Shared.Translations.T("terminal_proxy_died"));
                ScriptsBridge.Raise("died");
                Plugin.Log.LogInfo("A Proxy died");
            }
            catch (System.Exception e) { Plugin.Log.LogWarning("Proxy death: " + e); }
        }

        /// <summary>A new Proxy at a place, owned by this game.</summary>
        internal static ProxyBody Spawn(Vector3 at, Quaternion rotation, string id, string homeId)
        {
            ZNetScene scene = ZNetScene.instance;
            GameObject prefab = scene != null ? scene.GetPrefab(ProxyPrefab.Hash) : null;
            if (prefab == null) { Plugin.Log.LogWarning("No Proxy prefab to spawn"); return null; }
            GameObject go = Object.Instantiate(prefab, at, rotation);
            ZNetView view = go.GetComponent<ZNetView>();
            if (view == null || view.GetZDO() == null) return null;
            view.GetZDO().Set(KeyId, id);
            view.GetZDO().Set(KeyHome, homeId);
            return go.GetComponent<ProxyBody>();
        }
    }
}
