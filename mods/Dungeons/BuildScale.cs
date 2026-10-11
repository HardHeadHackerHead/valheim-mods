using HarmonyLib;
using UnityEngine;

namespace Dungeons
{
    // Parts scaled up (a giant's brazier, a 4x chain): the transform scales meshes, colliders and LOD distances by itself, but not what's
    // measured in metres elsewhere: a light's range (and LightLod's copy of it, taken in its Awake, before the part was scaled), how far
    // away LightLod switches it on, and fire and smoke particles (Unity's default scaling mode ignores the parent's scale).
    internal static partial class Builder
    {
        private static readonly AccessTools.FieldRef<LightLod, float> LodBaseRange = AccessTools.FieldRefAccess<LightLod, float>("m_baseRange");
        private static readonly AccessTools.FieldRef<LightLod, Light> LodLight = AccessTools.FieldRefAccess<LightLod, Light>("m_light");

        /// <summary>A part's lights and particles grown with its scale (by the mean of its three factors: a floor stretched 2x one way gets
        /// 1.26x). Call after Strip (which switches a fire's burning object on, so its LightLod has woken).</summary>
        private static void ScaleLook(GameObject go, Vector3 scale)
        {
            float k = Mathf.Pow(Mathf.Abs(scale.x * scale.y * scale.z), 1f / 3f);
            if (k <= 0f || Mathf.Abs(k - 1f) < 0.01f) return;
            foreach (Light light in go.GetComponentsInChildren<Light>(true)) light.range *= k;
            foreach (LightLod lod in go.GetComponentsInChildren<LightLod>(true))
            {
                lod.m_lightDistance *= k;
                lod.m_shadowDistance *= k;
                // awake already: it keeps the range to fade up to (and set the light's own to 0); not yet: it reads the scaled one when it wakes
                if (LodLight(lod) != null) LodBaseRange(lod) *= k;
            }
            foreach (ParticleSystem ps in go.GetComponentsInChildren<ParticleSystem>(true))
            {
                ParticleSystem.MainModule main = ps.main;
                if (main.scalingMode == ParticleSystemScalingMode.Local) main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            }
        }
    }
}
