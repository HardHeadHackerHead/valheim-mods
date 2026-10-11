using System;
using HarmonyLib;
using UnityEngine;

namespace Parkour
{
    /// <summary>
    /// How the moves feel: their sounds and the camera.
    ///
    /// Sounds and dust are the game's own footsteps, picked for the surface (wood, stone, metal, the ground's own) and sent the way the game
    /// sends a step: the player's "Step" RPC to everybody, so every player hears and sees them, with the mod or without.
    ///   * feet hitting the wall as a wall run or climb starts, and a step on the wall every 0.3 s while on it (the game's own animated
    ///     footsteps are held back meanwhile: they would sound like the ground the jump started from);
    ///   * a foot pushing off the wall at a wall jump (the game's jump plays its own sound and dust);
    ///   * the hands slapping the edge as a ledge is caught and as they move along it (a shimmy), the game's jump effort as the pull-up
    ///     starts, the feet landing on top;
    ///   * a scrape as a slide starts and a step as it ends, and the game's slide dust every 0.2 s while sliding;
    ///   * the landing of a roll, when the fall was too short for the game to play its own landing (the dodge plays its own sound).
    /// Nothing every frame; the Sounds setting turns them off.
    ///
    /// The camera, on top of the game's own each frame (after GameCamera.LateUpdate has placed it; the game places it afresh every frame,
    /// so nothing stays changed): a lean of a few degrees toward the wall while wall running, and a short widening of the view as you
    /// kick off a wall or pull up. Not in free-fly, a menu, the map or the inventory.
    /// </summary>
    internal static class Feel
    {
        private const float StepEvery = 0.3f, RollDegrees = 8f, RollEase = 0.25f, FovAdd = 6f, FovSeconds = 0.4f;
        private static float _nextStep, _roll, _fovAt = -10f;
        private static int _pieceLayer;

        private static readonly Func<FootStep, FootStep.GroundMaterial, FootStep.MotionType, int> BestStep =
            AccessTools.MethodDelegate<Func<FootStep, FootStep.GroundMaterial, FootStep.MotionType, int>>(AccessTools.Method(typeof(FootStep), "FindBestStepEffect"));
        private static readonly AccessTools.FieldRef<GameCamera, Camera> CameraOf = AccessTools.FieldRefAccess<GameCamera, Camera>("m_camera");

        public static void Reset()
        {
            _nextStep = 0f;
            _roll = 0f;
            _fovAt = -10f;
        }

        // ---- sounds ----

        /// <summary>Feet hitting a wall (a wall run or climb begins, a kick pushes off).</summary>
        public static void Touch(Player p, Walls.Wall wall)
        {
            Step(p, wall.Collider, wall.Normal, Foot(p, wall), FootStep.MotionType.Land);
            _nextStep = Time.time + StepEvery;
        }

        /// <summary>Every physics step on the wall: a footstep on it now and then.</summary>
        public static void OnWall(Player p, Walls.Wall wall)
        {
            if (Time.time < _nextStep) return;
            _nextStep = Time.time + StepEvery;
            Step(p, wall.Collider, wall.Normal, Foot(p, wall), FootStep.MotionType.Climbing);
        }

        /// <summary>The hands catching an edge.</summary>
        public static void Grab(Player p, Walls.Ledge ledge) => Step(p, ledge.Top, Vector3.up, ledge.Edge, FootStep.MotionType.Land);

        /// <summary>The effort of pulling up (the game's jump sound), and the view widening a little.</summary>
        public static void PullUp(Player p)
        {
            if (Plugin.Sounds.Value) p.m_jumpEffects.Create(p.transform.position, p.transform.rotation, p.transform, 1f, -1, p.GetZDOID());
            Widen();
        }

        /// <summary>A foot on the ground under the player (a slide starting and ending).</summary>
        public static void Ground(Player p, FootStep.MotionType motion) =>
            Step(p, p.GetLastGroundCollider(), p.GetLastGroundNormal(), p.transform.position, motion);

        private static readonly AccessTools.FieldRef<Character, bool> GameSliding = AccessTools.FieldRefAccess<Character, bool>("m_sliding");
        private static bool _cleaned;

        /// <summary>
        /// While sliding: the game's own slide effect. It is a continuous one (Character.UpdateContinousEffects starts it while the game's
        /// "sliding" flag is set and removes it when it isn't), so the flag is set for this step and the game does the rest. (Creating it
        /// ourselves left looping copies behind, a camera shake among them, that never ended.)
        /// </summary>
        public static void SlideDust(Player p)
        {
            if (Plugin.Sounds.Value) GameSliding(p) = true;   // (the game clears it at the start of every step)
        }

        /// <summary>
        /// Once per game: remove slide effects an earlier build of Parkour created and never removed (loose copies of the player's slide
        /// effect prefabs, not attached to anyone).
        /// </summary>
        public static void CleanUpOnce()
        {
            Player p = Player.m_localPlayer;
            if (_cleaned || p == null || ZNetScene.instance == null) return;
            _cleaned = true;
            var names = new System.Collections.Generic.HashSet<string>();
            foreach (EffectList.EffectData e in p.m_slideEffects.m_effectPrefabs) if (e.m_prefab != null) names.Add(e.m_prefab.name + "(Clone)");
            if (names.Count == 0) return;
            int removed = 0;
            foreach (GameObject go in UnityEngine.Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None))
            {
                if (go == null || go.transform.parent != null || !names.Contains(go.name)) continue;
                ZNetView view = go.GetComponent<ZNetView>();
                if (view != null && view.IsValid()) { if (view.IsOwner()) { ZNetScene.instance.Destroy(go); removed++; } }
                else { UnityEngine.Object.Destroy(go); removed++; }
            }
            if (removed > 0) Plugin.Log.LogInfo($"Removed {removed} slide effect(s) an earlier build left running");
        }

        /// <summary>The feet landing on top after a pull-up.</summary>
        public static void OnTop(Player p, Walls.Ledge ledge) => Step(p, ledge.Top, Vector3.up, p.transform.position, FootStep.MotionType.Land);

        /// <summary>A roll's landing, when the fall was too short for the game's own landing sound (it plays one from 0.8 m).</summary>
        public static void RollLanding(Player p, float height)
        {
            if (height <= 0.8f) Step(p, p.GetLastGroundCollider(), p.GetLastGroundNormal(), p.transform.position, FootStep.MotionType.Land);
        }

        /// <summary>One of the game's footstep effects for this surface, sent to every player as the game sends its steps.</summary>
        private static void Step(Player p, Collider surface, Vector3 normal, Vector3 at, FootStep.MotionType motion)
        {
            if (!Plugin.Sounds.Value || p == null || BestStep == null) return;
            FootStep feet = p.GetComponent<FootStep>();
            ZNetView nview = p.GetComponent<ZNetView>();
            if (feet == null || nview == null || !nview.IsValid()) return;
            FootStep.GroundMaterial material = MaterialOf(surface, normal, at);
            int effect = BestStep(feet, material, motion);
            if (effect < 0 && motion != FootStep.MotionType.Run) effect = BestStep(feet, material, FootStep.MotionType.Run);
            if (effect >= 0) nview.InvokeRPC(ZNetView.Everybody, "Step", effect, at);
        }

        /// <summary>What a surface is made of, the way FootStep tells it for the ground under a character, here for any collider.</summary>
        private static FootStep.GroundMaterial MaterialOf(Collider c, Vector3 normal, Vector3 at)
        {
            if (c == null) return FootStep.GroundMaterial.Default;
            Heightmap ground = c.GetComponent<Heightmap>();
            if (ground != null) return ground.GetGroundMaterial(normal, at);
            FootStepCollider marked = c.GetComponent<FootStepCollider>();
            if (marked != null) return marked.m_material;
            if (_pieceLayer == 0) _pieceLayer = LayerMask.NameToLayer("piece");
            if (c.gameObject.layer != _pieceLayer) return FootStep.GroundMaterial.Default;
            WearNTear piece = c.GetComponentInParent<WearNTear>();
            if (piece == null) return FootStep.GroundMaterial.Default;
            switch (piece.m_materialType)
            {
                case WearNTear.MaterialType.Wood:
                case WearNTear.MaterialType.HardWood: return FootStep.GroundMaterial.Wood;
                case WearNTear.MaterialType.Stone:
                case WearNTear.MaterialType.Marble:
                case WearNTear.MaterialType.Ashstone:
                case WearNTear.MaterialType.Ancient: return FootStep.GroundMaterial.Stone;
                case WearNTear.MaterialType.Iron: return FootStep.GroundMaterial.Metal;
                case WearNTear.MaterialType.Ice: return FootStep.GroundMaterial.Ice;
                default: return FootStep.GroundMaterial.Default;
            }
        }

        /// <summary>The foot nearest the wall, put on the wall's face.</summary>
        private static Vector3 Foot(Player p, Walls.Wall wall)
        {
            FootStep feet = p.GetComponent<FootStep>();
            Vector3 best = p.transform.position + Vector3.up * 0.3f;
            float nearest = float.MaxValue;
            if (feet != null)
                foreach (Transform foot in feet.m_feet)
                {
                    if (foot == null) continue;
                    float d = Vector3.Dot(foot.position - wall.Point, wall.Normal);
                    if (d < nearest) { nearest = d; best = foot.position; }
                }
            return best - wall.Normal * Vector3.Dot(best - wall.Point, wall.Normal);
        }

        // The game's own animated footsteps while on a wall would sound like the ground the jump started from: ours play instead.
        [HarmonyPatch(typeof(FootStep), "OnFoot", new[] { typeof(Transform) })]
        private static class FootStep_OnFoot
        {
            private static bool Prefix(FootStep __instance) =>
                !Plugin.Sounds.Value || (Moves.State != Move.WallRun && Moves.State != Move.Climb)
                || Player.m_localPlayer == null || __instance.gameObject != Player.m_localPlayer.gameObject;
        }

        // ---- the camera ----

        /// <summary>A short widening of the view (a kick off a wall, a pull-up).</summary>
        public static void Widen() => _fovAt = Time.unscaledTime;

        [HarmonyPatch(typeof(GameCamera), "LateUpdate")]
        private static class GameCamera_LateUpdate
        {
            private static void Postfix(GameCamera __instance)
            {
                try { Apply(__instance); }
                catch (Exception e) { Plugin.Log.LogWarning("Parkour camera: " + e.Message); }
            }
        }

        private static void Apply(GameCamera cam)
        {
            Player p = Player.m_localPlayer;
            float dt = Time.unscaledDeltaTime;
            bool free = p != null && !p.IsDead() && !p.InCutscene() && !p.IsAttached() && !GameCamera.InFreeFly()
                        && !Menu.IsVisible() && !Minimap.IsOpen() && !InventoryGui.IsVisible() && !StoreGui.IsVisible();
            if (!free || !Plugin.Enabled.Value) { _roll = 0f; return; }

            // lean toward the wall while running along it: the camera's up turns toward the wall's side
            float target = 0f;
            if (Plugin.CameraTilt.Value && Moves.State == Move.WallRun)
            {
                float side = Vector3.Dot(cam.transform.right, -Moves.WallNormal) >= 0f ? 1f : -1f;   // 1: the wall is on the right
                target = -RollDegrees * side;   // (a negative roll about the view turns its up to the right)
            }
            _roll = Mathf.MoveTowards(_roll, target, RollDegrees / RollEase * dt);
            if (Mathf.Abs(_roll) > 0.01f) cam.transform.rotation *= Quaternion.Euler(0f, 0f, _roll);

            // the view widens a little and settles back (the game's own field of view, m_fov, is left as it is)
            float t = (Time.unscaledTime - _fovAt) / FovSeconds;
            if (!Plugin.FovKick.Value || t < 0f || t >= 1f) return;
            float fov = cam.m_fov + FovAdd * (1f - t) * (1f - t);
            Camera main = CameraOf(cam);
            if (main != null) main.fieldOfView = fov;
            if (cam.m_skyCamera != null) cam.m_skyCamera.fieldOfView = fov;
        }
    }
}
