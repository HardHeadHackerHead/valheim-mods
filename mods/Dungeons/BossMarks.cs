using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Dungeons
{
    /// <summary>
    /// What a boss fight shows on the floor and on the boss, from the game's own assets: the telegraphs are the ward's circle (the guard
    /// stone's CircleProjector, its dashes on the floor), recoloured red, a ring for where a blow lands and a second one filling it as the
    /// blow comes, or two lines and a filling middle for a charge's lane; effects kept on the boss (an aura, the protection staff's
    /// bubble for a shield) are the game's effect prefabs with only their particles, lights and sound left. All local objects, never
    /// saved: each game makes its own from the fight's record.
    /// </summary>
    internal static class BossMarks
    {
        private static GameObject _holder, _dash;
        private static readonly Dictionary<Color, GameObject> Tinted = new Dictionary<Color, GameObject>();
        public static readonly Color Amber = new Color(1f, 0.72f, 0.18f, 1f);   // a target: where to lure the boss (a weakTo's mark)
        private static readonly Color Red = new Color(1f, 0.22f, 0.08f, 1f);

        /// <summary>The red dash every telegraph is made of (a copy of the ward circle's), or null when the game has no guard stone.</summary>
        private static GameObject Dash()
        {
            if (_dash != null) return _dash;
            GameObject stone = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab("guard_stone") : null;
            CircleProjector ring = stone != null ? stone.GetComponent<PrivateArea>()?.m_areaMarker : null;
            if (ring == null || ring.m_prefab == null) return null;
            if (_holder == null)
            {
                // the dash lives under a switched-off holder: copies made from it are switched on (its own flag is on)
                _holder = new GameObject("quaddungeon_bossmarks");
                _holder.SetActive(false);
                Object.DontDestroyOnLoad(_holder);
            }
            bool was = ZNetView.m_forceDisableInit;
            try
            {
                ZNetView.m_forceDisableInit = true;
                _dash = Object.Instantiate(ring.m_prefab, _holder.transform);
            }
            finally { ZNetView.m_forceDisableInit = was; }
            foreach (Renderer r in _dash.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] == null) continue;
                    var m = new Material(mats[i]);
                    if (m.HasProperty("_Color")) m.SetColor("_Color", Red);
                    if (m.HasProperty("_TintColor")) m.SetColor("_TintColor", Red);
                    if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", Red * 2f);
                    mats[i] = m;
                }
                r.sharedMaterials = mats;
            }
            return _dash;
        }

        /// <summary>A red ring of the ward's dashes on the floor round a point.</summary>
        public static GameObject Ring(Transform parent, Vector3 at, float radius, Color? colour = null)
        {
            GameObject dash = colour.HasValue ? DashIn(colour.Value) : Dash();
            if (dash == null) return null;
            var go = new GameObject("quaddungeon_bossring");
            go.transform.SetParent(parent, true);
            go.transform.position = at + Vector3.up * 0.1f;
            go.SetActive(false);
            CircleProjector c = go.AddComponent<CircleProjector>();
            c.m_prefab = dash;
            c.m_mask = 0;   // (no ground search: from 500 m up it would find the dungeon's roof; the ring lies flat at its point, on the floor)
            c.m_radius = radius;
            c.m_nrOfSegments = Mathf.Clamp(Mathf.RoundToInt(radius * 7f), 10, 80);
            c.m_speed = 0.4f;
            go.SetActive(true);
            return go;
        }

        /// <summary>The dash in another colour (the red one copied and tinted again).</summary>
        private static GameObject DashIn(Color colour)
        {
            if (Tinted.TryGetValue(colour, out GameObject made) && made != null) return made;
            GameObject red = Dash();
            if (red == null) return null;
            made = Object.Instantiate(red, _holder.transform);
            foreach (Renderer r in made.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] == null) continue;
                    var m = new Material(mats[i]);
                    if (m.HasProperty("_Color")) m.SetColor("_Color", colour);
                    if (m.HasProperty("_TintColor")) m.SetColor("_TintColor", colour);
                    if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", colour * 2f);
                    mats[i] = m;
                }
                r.sharedMaterials = mats;
            }
            Tinted[colour] = made;
            return made;
        }

        /// <summary>A ring's radius now (the filling one grows).</summary>
        public static void Grow(GameObject ring, float radius)
        {
            CircleProjector c = ring != null ? ring.GetComponent<CircleProjector>() : null;
            if (c != null) c.m_radius = Mathf.Max(0.2f, radius);
        }

        /// <summary>A lane of dashes from a to b, width wide: its two sides, and its middle up to `fill` (0..1) of the way.</summary>
        public static GameObject Lane(Transform parent, Vector3 a, Vector3 b, float width)
        {
            GameObject dash = Dash();
            if (dash == null) return null;
            var go = new GameObject("quaddungeon_bosslane");
            go.transform.SetParent(parent, true);
            Vector3 d = Vector3.ProjectOnPlane(b - a, Vector3.up);
            float len = d.magnitude;
            if (len < 0.5f) return go;
            Vector3 dir = d / len, side = Vector3.Cross(Vector3.up, dir);
            Quaternion turn = Quaternion.LookRotation(dir);
            for (float s = 0f; s <= len; s += 0.9f)
                foreach (float k in new[] { -0.5f, 0.5f })
                {
                    Vector3 p = a + dir * s + side * (k * width);
                    p.y = Mathf.Lerp(a.y, b.y, s / len);
                    Object.Instantiate(dash, p + Vector3.up * 0.1f, turn, go.transform);
                }
            return go;
        }

        /// <summary>The lane's middle filled up to `fill`: dashes added along its centre line.</summary>
        public static void FillLane(GameObject lane, Vector3 a, Vector3 b, float fill, ref int made)
        {
            GameObject dash = Dash();
            if (dash == null || lane == null) return;
            Vector3 d = Vector3.ProjectOnPlane(b - a, Vector3.up);
            float len = d.magnitude;
            if (len < 0.5f) return;
            Vector3 dir = d / len;
            int want = Mathf.FloorToInt(Mathf.Clamp01(fill) * len / 0.9f);
            for (; made < want; made++)
            {
                Vector3 p = a + dir * (made * 0.9f);
                p.y = Mathf.Lerp(a.y, b.y, made * 0.9f / len);
                Object.Instantiate(dash, p + Vector3.up * 0.1f, Quaternion.LookRotation(dir), lane.transform);
            }
        }

        /// <summary>One of the game's effect prefabs kept on something: made without a ZDO, with only its particles, lights and sound
        /// (its scripts, a timer that would remove it among them, taken off). Null when the game has no such prefab.</summary>
        public static GameObject Attach(string prefab, Transform parent, Vector3 local, float scale)
        {
            GameObject source = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(prefab) : null;
            if (source == null) { MechSet.WarnOnceBoss($"Boss: the game's effect {prefab} isn't in this version"); return null; }
            bool was = ZNetView.m_forceDisableInit;
            GameObject go;
            try
            {
                ZNetView.m_forceDisableInit = true;
                go = Object.Instantiate(source, parent);
            }
            catch (Exception e) { MechSet.WarnOnceBoss($"Boss: making {prefab}: {e.Message}"); return null; }
            finally { ZNetView.m_forceDisableInit = was; }
            foreach (MonoBehaviour mb in go.GetComponentsInChildren<MonoBehaviour>(true)) Object.DestroyImmediate(mb);
            go.transform.localPosition = local;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one * scale;
            foreach (ParticleSystem ps in go.GetComponentsInChildren<ParticleSystem>(true))
            {
                ParticleSystem.MainModule main = ps.main;
                main.loop = true;
                main.scalingMode = ParticleSystemScalingMode.Hierarchy;
                if (!ps.isPlaying) ps.Play();
            }
            return go;
        }

        /// <summary>Removes marks.</summary>
        public static void Clear(List<GameObject> marks)
        {
            foreach (GameObject g in marks) if (g != null) Object.Destroy(g);
            marks.Clear();
        }
    }
}
