using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace QualityOfLife
{
    /// <summary>
    /// Zoom the camera out much further than the game's 6 m (scroll wheel, as usual). Each scroll step grows with the distance, so
    /// getting from close up to far out takes a few notches, not dozens. Zooming in and first person are unchanged.
    /// </summary>
    public partial class Plugin
    {
        private static ConfigEntry<bool> _zoomEnabled;
        private static ConfigEntry<float> _zoomMax, _zoomMaxBoat;

        private void BindCameraConfig()
        {
            _zoomEnabled = Config.Bind("Camera", "FarZoom", true, "Let the scroll wheel zoom the camera out further than the game allows.");
            _zoomMax = Config.Bind("Camera", "MaxDistance", 20f, new ConfigDescription(
                "How far out the camera can go on foot (metres). The game's own limit is 6.", new AcceptableValueRange<float>(6f, 60f)));
            _zoomMaxBoat = Config.Bind("Camera", "MaxDistanceBoat", 40f, new ConfigDescription(
                "How far out the camera can go while steering a boat (metres).", new AcceptableValueRange<float>(6f, 80f)));
        }

        /// <summary>Put the game's own limits back (a hot reload or switching the feature off).</summary>
        internal static void RestoreCamera()
        {
            GameCamera cam = GameCamera.instance;
            if (cam == null || !CameraZoom.Saved) return;
            cam.m_maxDistance = CameraZoom.Max;
            cam.m_maxDistanceBoat = CameraZoom.MaxBoat;
            cam.m_zoomSens = CameraZoom.Sens;
            if (CameraZoom.Distance(cam) > cam.m_maxDistance) CameraZoom.Distance(cam) = cam.m_maxDistance;
            CameraZoom.Saved = false;
        }

        [HarmonyPatch(typeof(GameCamera), "UpdateCamera")]
        internal static class CameraZoom
        {
            internal static readonly AccessTools.FieldRef<GameCamera, float> Distance = AccessTools.FieldRefAccess<GameCamera, float>("m_distance");
            internal static bool Saved;
            internal static float Max, MaxBoat, Sens; // the game's own values

            private static void Prefix(GameCamera __instance)
            {
                if (_zoomEnabled == null) return;
                if (!_zoomEnabled.Value) { if (Saved) RestoreCamera(); return; }
                if (!Saved) { Max = __instance.m_maxDistance; MaxBoat = __instance.m_maxDistanceBoat; Sens = __instance.m_zoomSens; Saved = true; }
                __instance.m_maxDistance = Mathf.Max(Max, _zoomMax.Value);
                __instance.m_maxDistanceBoat = Mathf.Max(MaxBoat, _zoomMaxBoat.Value);
                // Each scroll notch moves 0.5 m in the game; out here that grows with the distance (about a tenth of it per notch).
                __instance.m_zoomSens = Sens * Mathf.Max(1f, Distance(__instance) / Mathf.Max(1f, Max));
            }
        }
    }
}
