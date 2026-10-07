using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace AICompanion
{
    /// <summary>
    /// The companion's body: a copy of the game's Player prefab without the parts that make it *the* player (input, profile, skills,
    /// talking), with the game's own humanoid and monster AI in their place and a chest-like inventory for its gear. Only game components,
    /// so a reload never leaves old code running on a companion.
    ///
    /// Registered like a piece (see docs/modding-pitfalls.md): from ZNetScene.Awake and ObjectDB.Awake, and on a hot reload, so a saved
    /// companion always finds its prefab when its zone loads. Without the mod a saved companion (and the gear it carries) is deleted by the
    /// host, so everyone in a world with companions needs it, the host (or dedicated server) included.
    /// </summary>
    internal static class Prefab
    {
        public const string PrefabName = "dhack_companion";
        public static readonly int Hash = PrefabName.GetStableHashCode();

        private static GameObject _holder, _prefab;
        private static readonly AccessTools.FieldRef<ZNetScene, Dictionary<int, GameObject>> Named =
            AccessTools.FieldRefAccess<ZNetScene, Dictionary<int, GameObject>>("m_namedPrefabs");

        public static GameObject Get() => _prefab;

        public static void Register(ZNetScene scene)
        {
            if (_prefab == null)
            {
                GameObject player = scene.GetPrefab("Player");
                if (player == null) { Plugin.Instance?.Warn("The Player prefab was not found, so no companion can be made"); return; }
                _holder = new GameObject(Plugin.Name + "Prefabs");
                _holder.SetActive(false); // the copy must not wake up as a real object
                Object.DontDestroyOnLoad(_holder);
                _prefab = Make(player, scene);
            }
            if (!scene.m_prefabs.Contains(_prefab)) scene.m_prefabs.Add(_prefab);
            Named(scene)[Hash] = _prefab;
        }

        public static void Unregister()
        {
            if (_prefab == null) return;
            if (ZNetScene.instance != null)
            {
                ZNetScene.instance.m_prefabs.Remove(_prefab);
                Dictionary<int, GameObject> named = Named(ZNetScene.instance);
                if (named.TryGetValue(Hash, out GameObject current) && current == _prefab) named.Remove(Hash); // only our own entry
            }
            _prefab = null;
            Object.Destroy(_holder);
            _holder = null;
        }

        private static GameObject Make(GameObject source, ZNetScene scene)
        {
            GameObject go = Object.Instantiate(source, _holder.transform, false);
            go.name = PrefabName;
            Humanoid original = source.GetComponent<Humanoid>();

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
            h.m_name = "Companion";
            h.m_group = "dhack_companion";
            h.m_faction = Character.Faction.Players;
            h.m_health = Plugin.BaseHealth.Value; // food adds to it (Food)
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

            MonsterAI ai = go.AddComponent<MonsterAI>();
            ai.m_viewRange = 30f; ai.m_viewAngle = 150f; ai.m_hearRange = 30f; ai.m_mistVision = true;
            ai.m_alertedEffects = new EffectList(); ai.m_idleSound = new EffectList(); ai.m_idleSoundChance = 0f;
            ai.m_pathAgentType = Pathfinding.AgentType.Humanoid;
            ai.m_moveMinAngle = 90f; ai.m_smoothMovement = true; ai.m_jumpInterval = 0f;
            ai.m_randomMoveInterval = 10f; ai.m_randomMoveRange = 4f;
            ai.m_avoidFire = false; ai.m_afraidOfFire = false; ai.m_avoidWater = false; ai.m_aggravatable = false;
            ai.m_attackPlayerObjects = false;
            ai.m_consumeItems = new List<ItemDrop>();

            ZNetView view = go.GetComponent<ZNetView>();
            view.m_persistent = true;
            view.m_distant = false;
            view.m_type = ZDO.ObjectType.Default;
            ZSyncTransform sync = go.GetComponent<ZSyncTransform>();
            if (sync != null) { sync.m_syncPosition = true; sync.m_syncRotation = true; sync.m_syncBodyVelocity = false; sync.m_characterParentSync = false; }
            Rigidbody body = go.GetComponent<Rigidbody>();
            if (body != null) body.mass = 50f;

            // Its gear lives in a chest-like inventory (saved with it, opened with the game's own chest screen); the humanoid uses that same
            // inventory (Patches: Container_Awake), so what you put in is what it can wear and wield.
            Container gear = go.AddComponent<Container>();
            gear.m_name = "Companion";
            gear.m_width = 8; gear.m_height = 4;
            gear.m_privacy = Container.PrivacySetting.Public;
            gear.m_checkGuardStone = false;
            gear.m_autoDestroyEmpty = false;
            gear.m_openEffects = new EffectList(); gear.m_closeEffects = new EffectList();
            return go;
        }
    }
}
