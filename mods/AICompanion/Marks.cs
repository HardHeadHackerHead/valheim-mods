using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace AICompanion
{
    /// <summary>
    /// What you pointed it at (the command key), shown on the thing itself for as long as it is on it: a soft pulsing glow (the game's own
    /// highlight, as on pieces you look at while building) and a line above it ("Rádvar: chopping this"). The tree it chops, the rock it mines,
    /// the plant it picks, the enemy it goes for, the chest it fills, its tombstone, the cart it pulls. On your game only (it is your
    /// pointing); it fades when the job is done, dropped, or after three minutes.
    /// </summary>
    internal static class Marks
    {
        private static readonly int Emission = Shader.PropertyToID("_EmissionColor");
        private static readonly Color Glow = new Color(0.45f, 0.85f, 0.75f); // the companions' colour (their map pins)

        private class Mark
        {
            public GameObject Go;
            public Func<bool> Active;
            public string Text;
            public float Until, NextLine, Height;
            public bool Spot; // a marker made for a spot on the ground (destroyed with the mark)
        }

        private static readonly List<Mark> All = new List<Mark>();

        /// <summary>Marks the thing while the job lasts (active: still on it). A new mark on the same thing replaces the old.</summary>
        public static void Put(Component target, Humanoid who, string doing, Func<bool> active)
        {
            if (target == null || who == null) return;
            GameObject go = target.gameObject;
            foreach (Mark old in All.Where(m => m.Go == go).ToList()) Clear(old);
            var mark = new Mark { Go = go, Active = active, Text = doing != null ? $"{Companion.NameOf(who)}: {doing}" : null, Until = Time.time + 900f, Height = HeightOf(go) };
            All.Add(mark);
        }

        /// <summary>
        /// A spot on the ground (where it waits, where it picks things up): a small light on the ground there and a line above it, while
        /// the job lasts.
        /// </summary>
        public static void PutSpot(Vector3 at, Humanoid who, string doing, Func<bool> active)
        {
            if (who == null) return;
            foreach (Mark old in All.Where(m => m.Spot && m.Go != null && Vector3.Distance(m.Go.transform.position, at) < 1.5f).ToList()) Clear(old);
            var go = new GameObject("DHack_CompanionSpot");
            go.transform.position = at + Vector3.up * 0.3f;
            Light light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = Glow;
            light.range = 3f;
            light.intensity = 2.5f;
            light.shadows = LightShadows.None;
            All.Add(new Mark { Go = go, Active = active, Text = $"{Companion.NameOf(who)}: {doing}", Until = Time.time + 600f, Height = 1.2f, Spot = true });
        }

        /// <summary>Every frame on your game: the glow pulses; a mark whose job is over fades.</summary>
        public static void Tick()
        {
            if (All.Count == 0) return;
            float pulse = 0.25f + 0.15f * Mathf.Sin(Time.time * 4f);
            foreach (Mark m in All.ToList())
            {
                bool on = false;
                try { on = m.Go != null && Time.time < m.Until && m.Active(); } catch { }
                if (!on) { Clear(m); continue; }
                if (m.Spot) { Light l = m.Go.GetComponent<Light>(); if (l != null) l.intensity = 1.5f + 6f * (pulse - 0.1f); }
                else MaterialMan.instance?.SetValue(m.Go, Emission, Glow * pulse);
                if (m.Text != null && Time.time >= m.NextLine && Chat.instance != null)
                {
                    m.NextLine = Time.time + 5f;
                    Chat.instance.SetNpcText(m.Go, Vector3.up * m.Height, Pointing.Reach, 6f, "", m.Text, false); // (seen as far as you can point)
                }
            }
        }

        private static void Clear(Mark m)
        {
            All.Remove(m);
            if (m.Go == null) return;
            Chat.instance?.ClearNpcText(m.Go);
            if (m.Spot) { UnityEngine.Object.Destroy(m.Go); return; }
            MaterialMan.instance?.ResetValue(m.Go, Emission);
        }

        public static void Forget() { foreach (Mark m in All.ToList()) Clear(m); }

        /// <summary>Where the line goes: above it, but no higher than a tall tree's trunk (you look at the trunk, not the crown).</summary>
        private static float HeightOf(GameObject go)
        {
            float top = 1.5f;
            foreach (Renderer r in go.GetComponentsInChildren<Renderer>())
                if (r.enabled && (r is MeshRenderer || r is SkinnedMeshRenderer)) top = Mathf.Max(top, r.bounds.max.y - go.transform.position.y);
            return Mathf.Clamp(top + 0.4f, 1.2f, 3.2f);
        }
    }
}
