using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace BuildOrders
{
    /// <summary>
    /// The glowing see-through copies of planned pieces. They're made the same way the game makes its own placement ghost:
    /// instantiate the real piece with networking switched off, then strip everything that isn't a picture (physics, sounds,
    /// lights, particles) so it's just a harmless model.
    /// </summary>
    public partial class Plugin
    {
        private readonly Dictionary<string, GameObject> _ghosts = new Dictionary<string, GameObject>();
        private readonly HashSet<string> _badPrefabs = new HashSet<string>();

        private static readonly Color PlannedColor = new Color(0.35f, 0.8f, 1f);
        private static readonly Color AimedColor = new Color(1f, 0.85f, 0.35f);

        private const int MaxGhosts = 150;

        // ---- the see-through material ------------------------------------------------------------
        // Valheim's building material can't be transparent, so every ghost gets a plain transparent material instead.
        // Which transparent shaders exist depends on what the game kept in the build, so we look for the best one at runtime.

        private Shader _ghostShader;
        private bool _shaderSearched;

        // In order of preference: lit transparent shaders look best; flat particle shaders are the fallback.
        private static readonly string[] ShaderPreference =
        {
            "Legacy Shaders/Transparent/Diffuse",
            "Legacy Shaders/Transparent/VertexLit",
            "Legacy Shaders/Particles/Alpha Blended",
            "Particles/Standard Unlit",
            "Custom/AlphaParticle",
            "Mobile/Particles/Alpha Blended",
            "Sprites/Default",
            "Unlit/Transparent",
            "Hidden/Internal-Colored",
        };

        private Shader GhostShader()
        {
            if (_shaderSearched) return _ghostShader;
            _shaderSearched = true;

            Shader[] loaded = Resources.FindObjectsOfTypeAll<Shader>();

            // For troubleshooting: every loaded shader that looks like it could do transparency.
            string[] words = { "transparent", "alpha", "particle", "sprite", "unlit", "ghost", "fade", "blend", "hologram" };
            var candidates = loaded.Select(s => s.name)
                .Where(n => words.Any(w => n.IndexOf(w, System.StringComparison.OrdinalIgnoreCase) >= 0))
                .Distinct().OrderBy(n => n).ToList();
            Logger.LogInfo($"Possible ghost shaders loaded in this game ({candidates.Count}): {string.Join(", ", candidates.ToArray())}");

            // An explicit choice in the config wins, otherwise the first one from our list that exists.
            var wanted = new List<string>();
            if (!string.IsNullOrWhiteSpace(_shaderOverride.Value)) wanted.Add(_shaderOverride.Value.Trim());
            wanted.AddRange(ShaderPreference);

            foreach (string name in wanted)
            {
                Shader s = loaded.FirstOrDefault(x => x.name == name) ?? Shader.Find(name);
                if (s != null && s.isSupported) { _ghostShader = s; break; }
            }
            Logger.LogInfo(_ghostShader != null ? $"Ghosts will use the shader '{_ghostShader.name}'"
                                                : "No transparent shader found: ghosts will fall back to the game's own (solid) material.");
            return _ghostShader;
        }

        /// <summary>Spawn ghosts for orders near the player and remove the ones that are far away or gone.</summary>
        private void UpdateGhosts(Player player)
        {
            if (!_showGhosts.Value) { DestroyAllGhosts(); return; }

            float max = _viewDistance.Value * _viewDistance.Value;
            Vector3 here = player.transform.position;

            foreach (string id in _ghosts.Keys.Where(id => !_orders.ContainsKey(id)).ToList()) DestroyGhost(id);

            // Making a ghost is real work (a whole piece is created and stripped down), so only a few per pass: a big plan fades in
            // over a second or two instead of costing one long frame. The closest ones come first.
            int spawned = 0;
            foreach (Order o in _orders.Values.OrderBy(o => (o.Pos - here).sqrMagnitude))
            {
                bool near = (o.Pos - here).sqrMagnitude <= max;
                bool has = _ghosts.ContainsKey(o.Id);
                if (near && !has && _ghosts.Count < MaxGhosts && spawned < MaxSpawnsPerPass) { SpawnGhost(o); spawned++; }
                else if (!near && has) DestroyGhost(o.Id);
            }
        }

        private const int MaxSpawnsPerPass = 3;

        /// <summary>The materials we created for each ghost. They're ours, so we must free them ourselves or they pile up.</summary>
        private readonly Dictionary<string, List<Material>> _ghostMaterials = new Dictionary<string, List<Material>>();

        private void SpawnGhost(Order o)
        {
            if (_badPrefabs.Contains(o.Prefab)) return;
            GameObject prefab = ZNetScene.instance.GetPrefab(o.Prefab);
            if (prefab == null) { _badPrefabs.Add(o.Prefab); return; }

            GameObject go = null;
            TerrainModifier modifier = prefab.GetComponentInChildren<TerrainModifier>();
            bool modifierWasOn = modifier != null && modifier.enabled;
            try
            {
                if (modifier != null) modifier.enabled = false;
                TerrainOp.m_forceDisableTerrainOps = true;
                ZNetView.m_forceDisableInit = true; // no network object: it's only a picture
                go = Instantiate(prefab, o.Pos, o.Rot);
                go.GetComponent<ItemDrop>()?.MakePiece();
            }
            catch (System.Exception e)
            {
                Logger.LogWarning($"Could not make a ghost of {o.Prefab}: {e.Message}");
                _badPrefabs.Add(o.Prefab);
            }
            finally
            {
                ZNetView.m_forceDisableInit = false;
                TerrainOp.m_forceDisableTerrainOps = false;
                if (modifier != null) modifier.enabled = modifierWasOn;
            }
            if (go == null) return;

            go.name = o.Prefab + "_order";
            Strip(go);
            var materials = new List<Material>();
            MakeTranslucent(go, materials);
            _ghostMaterials[o.Id] = materials;
            _ghosts[o.Id] = go;
            RegisterColliders(o.Id, go);
            Tint(go, aimed: false, id: o.Id);
        }

        /// <summary>Remove everything but the model, the same way the game prepares its own placement ghost.</summary>
        private static void Strip(GameObject go)
        {
            foreach (var c in go.GetComponentsInChildren<Joint>()) Destroy(c);
            foreach (var c in go.GetComponentsInChildren<Rigidbody>()) Destroy(c);
            foreach (var c in go.GetComponentsInChildren<ParticleSystemForceField>()) Destroy(c);
            foreach (var c in go.GetComponentsInChildren<Demister>()) Destroy(c);
            foreach (var c in go.GetComponentsInChildren<Collider>()) c.enabled = false;  // nothing can hit, hover or bump a ghost (kept, switched off, so planning can snap onto it)
            foreach (var c in go.GetComponentsInChildren<TerrainModifier>()) Destroy(c);
            foreach (var c in go.GetComponentsInChildren<GuidePoint>()) Destroy(c);
            foreach (var c in go.GetComponentsInChildren<LightLod>()) Destroy(c);
            foreach (var c in go.GetComponentsInChildren<LightFlicker>()) Destroy(c);
            foreach (var c in go.GetComponentsInChildren<Light>()) Destroy(c);
            foreach (var c in go.GetComponentsInChildren<WispSpawner>()) Destroy(c);
            foreach (var c in go.GetComponentsInChildren<AudioSource>()) c.enabled = false;
            foreach (var c in go.GetComponentsInChildren<ZSFX>()) c.enabled = false;
            foreach (var c in go.GetComponentsInChildren<Windmill>()) c.enabled = false;
            foreach (var c in go.GetComponentsInChildren<ParticleSystem>()) c.gameObject.SetActive(false);

            int layer = LayerMask.NameToLayer("ghost");
            foreach (Transform t in go.GetComponentsInChildren<Transform>()) t.gameObject.layer = layer;

            Transform ghostOnly = go.transform.Find("_GhostOnly");
            if (ghostOnly != null) ghostOnly.gameObject.SetActive(true);

        }

        /// <summary>
        /// Swap every model's material for a plain transparent one (keeping its texture), so the ghost is genuinely see-through.
        /// If the game has no usable transparent shader, fall back to copies of its own material, like the game's placement ghost.
        /// </summary>
        private void MakeTranslucent(GameObject go, List<Material> created)
        {
            Shader shader = GhostShader();

            foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true))
            {
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;

                Material[] original = r.sharedMaterials;
                var replacement = new Material[original.Length];
                for (int i = 0; i < original.Length; i++)
                {
                    if (original[i] == null) continue;

                    if (shader != null)
                    {
                        var m = new Material(shader) { renderQueue = 3000 + i };
                        Texture albedo = original[i].HasProperty("_MainTex") ? original[i].mainTexture : null;
                        if (albedo != null) m.mainTexture = albedo;
                        replacement[i] = m;
                    }
                    else
                    {
                        var copy = new Material(original[i]);
                        if (copy.HasProperty("_ValueNoise")) copy.SetFloat("_ValueNoise", 0f);
                        if (copy.HasProperty("_TriplanarLocalPos")) copy.SetFloat("_TriplanarLocalPos", 1f);
                        replacement[i] = copy;
                    }
                    created.Add(replacement[i]);
                }
                r.sharedMaterials = replacement;
            }
        }

        /// <summary>Colour and opacity of a ghost: cyan normally, gold (and a bit more solid) while you're aiming at it.</summary>
        private void Tint(GameObject go, bool aimed, string id = null)
        {
            float opacity = Mathf.Clamp(_ghostOpacity.Value, 0.03f, 1f);
            Color c = aimed ? AimedColor : PlannedColor;
            c.a = aimed ? Mathf.Min(1f, opacity * 2f + 0.1f) : opacity;

            // With the stability colours showing, a ghost takes the colour of how well it would be supported (a bit more solid, so it reads).
            if (!aimed && _stabilityShown && id != null && _stability.TryGetValue(id, out Stab stab))
            {
                c = StabilityColor(stab);
                c.a = Mathf.Min(1f, opacity * 1.6f + 0.08f);
            }

            foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true))
            {
                foreach (Material m in r.sharedMaterials)
                {
                    if (m == null) continue;
                    if (m.HasProperty("_Color")) m.SetColor("_Color", c);
                    if (m.HasProperty("_TintColor")) m.SetColor("_TintColor", c);  // particle shaders call it this
                    if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", new Color(c.r, c.g, c.b) * 0.4f);
                }
            }
        }

        private void SetHighlight(Order o, bool on)
        {
            if (_ghosts.TryGetValue(o.Id, out GameObject go) && go != null) Tint(go, on, o.Id);
        }

        /// <summary>Re-apply opacity to every ghost (used when the setting changes).</summary>
        private void RetintAll()
        {
            foreach (var kv in _ghosts) if (kv.Value != null) Tint(kv.Value, aimed: Aimed != null && Aimed.Id == kv.Key, id: kv.Key);
        }

        private void DestroyGhost(string id)
        {
            if (_ghosts.TryGetValue(id, out GameObject go) && go != null) Destroy(go);
            _ghosts.Remove(id);
            _ghostColliders.Remove(id);
            FreeMaterials(id);
        }

        private void DestroyAllGhosts()
        {
            foreach (GameObject go in _ghosts.Values) if (go != null) Destroy(go);
            _ghosts.Clear();
            _ghostColliders.Clear();
            foreach (string id in _ghostMaterials.Keys.ToList()) FreeMaterials(id);
        }

        // ---- snapping onto ghosts while planning --------------------------------------------------

        // The game finds things to snap to, and lets you aim at them, through their colliders. A ghost has none while you build for real
        // (so it is only a picture: you walk through it, nothing hits it). While you hold the plan key, they are switched on, on the
        // layer for non-solid pieces, so a wall can snap onto a ghost floor exactly as it would onto a real one.
        private readonly Dictionary<string, List<Collider>> _ghostColliders = new Dictionary<string, List<Collider>>();
        private bool _collidersOn;

        private void RegisterColliders(string id, GameObject ghost)
        {
            var list = new List<Collider>(ghost.GetComponentsInChildren<Collider>(true));
            _ghostColliders[id] = list;
            ApplyColliders(list, _collidersOn);
        }

        private static void ApplyColliders(List<Collider> list, bool on)
        {
            int layer = LayerMask.NameToLayer(on ? "piece_nonsolid" : "ghost");
            foreach (Collider c in list)
            {
                if (c == null) continue;
                c.enabled = on;
                c.gameObject.layer = layer;
            }
        }

        /// <summary>Switch every ghost's colliders on (planning) or off (building for real).</summary>
        internal void SetGhostColliders(bool on)
        {
            if (on == _collidersOn) return;
            _collidersOn = on;
            foreach (List<Collider> list in _ghostColliders.Values) ApplyColliders(list, on);
        }

        private void FreeMaterials(string id)
        {
            if (!_ghostMaterials.TryGetValue(id, out List<Material> list)) return;
            foreach (Material m in list) if (m != null) Destroy(m);
            _ghostMaterials.Remove(id);
        }

        // ---- placing onto an order --------------------------------------------------------------

        /// <summary>
        /// While you place the same piece as a nearby order, snap your placement ghost onto it, so one click builds the
        /// piece exactly where it was planned.
        /// </summary>
        internal void SnapToOrder(GameObject placementGhost)
        {
            if (!_enabled.Value || placementGhost == null || !placementGhost.activeSelf || PlanKeyHeld) return;

            string prefab = Plain(placementGhost.name);
            Vector3 at = placementGhost.transform.position;
            float best = _snapDistance.Value * _snapDistance.Value;
            Order target = null;

            foreach (Order o in _orders.Values)
            {
                if (o.Prefab != prefab) continue;
                float d = (o.Pos - at).sqrMagnitude;
                if (d <= best) { best = d; target = o; }
            }
            if (target != null) placementGhost.transform.SetPositionAndRotation(target.Pos, target.Rot);
        }
    }
}
