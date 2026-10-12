using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Dungeons
{
    /// <summary>
    /// Beams of light (BeamDef: "beams" on any mechanism), on every game, every frame: a thin glowing line between two points while
    /// its mechanism is in one of the beam's states, with a soft pulse, and a light at its far end. A LineRenderer, no collider: it never
    /// blocks anything and no check sees it. Under the mechanisms' root, named quaddungeon_boss_beam (the room culling leaves anything
    /// named quaddungeon_boss alone). Each game draws its own from the mechanisms' state, so every player sees the same.
    /// </summary>
    internal partial class MechSet
    {
        private class BeamRun
        {
            public GameObject Go;
            public LineRenderer Line;
            public Light Light;
        }

        private readonly Dictionary<KeyValuePair<Mech, int>, BeamRun> _beams = new Dictionary<KeyValuePair<Mech, int>, BeamRun>();
        private static Material _beamMat;

        private void BeamsShow()
        {
            if (_root == null) return;
            foreach (Mech m in All)
            {
                List<BeamDef> list = Beams.Get(m.Def);
                if (list == null || m.Base == null) continue;
                int state = -1;
                for (int i = 0; i < list.Count; i++)
                {
                    var key = new KeyValuePair<Mech, int>(m, i);
                    _beams.TryGetValue(key, out BeamRun run);
                    if (state < 0) state = StateOf(m);
                    bool on = list[i].States.Contains(state);
                    if (!on)
                    {
                        if (run?.Go != null && run.Go.activeSelf) run.Go.SetActive(false);
                        continue;
                    }
                    try
                    {
                        if (run?.Go == null) _beams[key] = run = MakeBeam(list[i]);
                        if (run == null) continue;
                        if (!run.Go.activeSelf) run.Go.SetActive(true);
                        Vector3 a = BeamPoint(m, list[i].FromMech, list[i].From), b = BeamPoint(m, list[i].ToMech, list[i].To);
                        run.Line.SetPosition(0, a);
                        run.Line.SetPosition(1, b);
                        float pulse = 1f + 0.15f * Mathf.Sin(Time.time * 6f + i);
                        run.Line.widthMultiplier = list[i].Width * pulse;
                        if (run.Light != null) run.Light.transform.position = b - (b - a).normalized * 0.3f;
                    }
                    catch (Exception e) { WarnOnce($"Dungeon {Site.Id}: {m.Key}'s beam: {e.Message}"); }
                }
            }
        }

        /// <summary>A beam's end: in the room's frame, or in a mechanism's moving part's frame (it follows it).</summary>
        private Vector3 BeamPoint(Mech m, string mech, Vector3 local)
        {
            if (!string.IsNullOrEmpty(mech))
            {
                Mech other = Sibling(m, mech);
                if (other?.Pivot != null) return other.Pivot.TransformPoint(local);
                WarnOnce($"Dungeon {Site.Id}: {m.Key}'s beam names {mech}, which isn't a mechanism in its room");
            }
            return RoomPoint(m, local);
        }

        private BeamRun MakeBeam(BeamDef def)
        {
            var go = new GameObject("quaddungeon_boss_beam");
            go.transform.SetParent(_root.transform, false);
            var line = go.AddComponent<LineRenderer>();
            line.positionCount = 2;
            line.useWorldSpace = true;
            line.numCapVertices = 4;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.sharedMaterial = BeamMaterial();
            Color c = def.Colour;
            line.startColor = new Color(c.r, c.g, c.b, 0.9f);
            line.endColor = new Color(c.r, c.g, c.b, 0.6f);
            line.widthMultiplier = def.Width;
            var run = new BeamRun { Go = go, Line = line };
            if (def.Light > 0f)
            {
                var lg = new GameObject("quaddungeon_boss_beamlight");
                lg.transform.SetParent(go.transform, false);
                Light l = lg.AddComponent<Light>();
                l.type = LightType.Point;
                l.range = def.Light;
                l.intensity = 1.5f;
                l.color = c;
                l.shadows = LightShadows.None;
                run.Light = l;
            }
            return run;
        }

        /// <summary>An additive, unlit material for the beams: the first of these shaders the game has.</summary>
        private static Material BeamMaterial()
        {
            if (_beamMat != null) return _beamMat;
            Shader s = Shader.Find("Legacy Shaders/Particles/Additive") ?? Shader.Find("Particles/Standard Unlit") ?? Shader.Find("Sprites/Default");
            _beamMat = s != null ? new Material(s) : new Material(Shader.Find("Hidden/Internal-Colored"));
            return _beamMat;
        }
    }
}
