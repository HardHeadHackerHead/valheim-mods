using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace BuildOrders
{
    /// <summary>
    /// The bridge builder. "Bridge" is a piece in the hammer's build menu (Misc) that is never built itself: click where a bridge should start,
    /// walk or swim across (a ghost of the bridge stretches after you), and click where it should end. The bridge becomes a plan like a placed
    /// blueprint (build it with E or hold E, remove it, take it down for a refund), with posts down to the riverbed. The ground is not levelled
    /// (that would reshape the riverbank). Styles are separate generators, so more can be added (stone, rope, a covered bridge...).
    /// </summary>
    public partial class Plugin
    {
        internal const string BridgeToolPrefab = "piece_bo_bridge";
        private static GameObject _bridgeHolder, _bridgeTool;
        private static readonly AccessTools.FieldRef<Player, GameObject> PlacementGhost = AccessTools.FieldRefAccess<Player, GameObject>("m_placementGhost");

        // ---- the tool in the hammer's menu ----

        internal void RegisterBridgeTool(ZNetScene scene)
        {
            if (ObjectDB.instance == null || scene == null) return;
            GameObject hammer = ObjectDB.instance.GetItemPrefab("Hammer");
            GameObject pole = scene.GetPrefab("wood_pole");
            if (hammer == null || pole == null) return;
            if (_bridgeTool == null)
            {
                _bridgeHolder = new GameObject("BuildOrdersBridgeTool");
                _bridgeHolder.SetActive(false);   // keeps the copy from waking up as a real object
                DontDestroyOnLoad(_bridgeHolder);
                _bridgeTool = Instantiate(pole, _bridgeHolder.transform);
                _bridgeTool.name = BridgeToolPrefab;
                Piece piece = _bridgeTool.GetComponent<Piece>();
                piece.m_name = "Bridge";
                piece.m_description = "Plans a bridge: click where it starts, walk or swim across, and click where it ends; then choose its width, material, rails, roof and more, and confirm. It becomes ghosts to build, with posts down to the riverbed. Costs nothing until you build it.";
                piece.m_category = Piece.PieceCategory.Misc;
                piece.m_craftingStation = null;
                piece.m_resources = new Piece.Requirement[0];
                try { piece.m_icon = BridgeIcon() ?? piece.m_icon; } catch (Exception e) { Logger.LogWarning("Bridge icon: " + e.Message); }
            }
            PieceTable table = hammer.GetComponent<ItemDrop>().m_itemData.m_shared.m_buildPieces;
            if (!table.m_pieces.Contains(_bridgeTool)) table.m_pieces.Add(_bridgeTool);
        }

        private void UnregisterBridgeTool()
        {
            CancelBridge();
            GameObject hammer = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab("Hammer") : null;
            if (hammer != null && _bridgeTool != null) hammer.GetComponent<ItemDrop>().m_itemData.m_shared.m_buildPieces.m_pieces.Remove(_bridgeTool);
            if (_bridgeHolder != null) Destroy(_bridgeHolder);
            _bridgeHolder = null; _bridgeTool = null;
        }

        internal static bool IsBridgeTool(Piece piece) => piece != null && piece.gameObject.name == BridgeToolPrefab;

        private static readonly System.Reflection.MethodInfo UpdateKnownRecipes = AccessTools.Method(typeof(Player), "UpdateKnownRecipesList");
        private float _nextBridgeKnownCheck;

        /// <summary>
        /// The game only looks for newly unlocked pieces when the inventory changes, so a piece added while playing stays hidden until you pick
        /// something up. The Bridge costs nothing (always unlocked): ask the game to look again as soon as it is in the menu.
        /// </summary>
        private void MakeBridgeKnown(Player player)
        {
            if (_bridgeTool == null || player == null || Time.unscaledTime < _nextBridgeKnownCheck) return;
            _nextBridgeKnownCheck = Time.unscaledTime + 3f;
            if (player.IsRecipeKnown(_bridgeTool.GetComponent<Piece>().m_name)) return;
            try { UpdateKnownRecipes?.Invoke(player, null); } catch (Exception e) { Logger.LogWarning("Could not unlock the Bridge piece: " + e.Message); }
        }

        // ---- drawing a bridge ----

        private Vector3? _bridgeStart;
        private Vector3 _bridgeEnd;
        private float _bridgeAt;
        private BridgeDesign _bridgeDesign;
        private bool _bridgeEndFixed, _bridgeDirty;   // the end is set (the options panel is open); the settings changed
        private readonly List<KeyValuePair<string, GameObject>> _bridgeGhosts = new List<KeyValuePair<string, GameObject>>();
        private readonly List<Material> _bridgeMaterials = new List<Material>();
        internal static bool BridgeDrawing => Instance != null && Instance._bridgeStart.HasValue;

        /// <summary>A click with the Bridge tool: the first sets the start, the second the end (and plans it).</summary>
        internal void BridgeClick(Player player)
        {
            GameObject ghost = PlacementGhost(player);
            if (ghost == null) return;
            Vector3 at = ghost.transform.position;
            if (!_bridgeStart.HasValue)
            {
                _bridgeStart = at;
                _bridgeAt = 0f;
                player.Message(MessageHud.MessageType.Center, "Bridge start set: walk or swim across, then click where it ends (Esc cancels)");
                return;
            }
            BridgeDesign design = DesignBridge(_bridgeStart.Value, at, CurrentBridgeOptions());
            if (design.Entries.Count == 0 || design.Length < 2f) { player.Message(MessageHud.MessageType.Center, "Too short for a bridge: click further away"); return; }
            // the end is set: keep the ghost there and open the options (Confirm places it)
            _bridgeEnd = at;
            _bridgeEndFixed = true;
            OpenBridgeOptions();
        }

        internal void CancelBridge()
        {
            _bridgeStart = null;
            _bridgeEndFixed = false;
            BridgeOptionsOpen = false;
            _bridgeOpts = null;
            foreach (var kv in _bridgeGhosts) if (kv.Value != null) Destroy(kv.Value);
            _bridgeGhosts.Clear();
            foreach (Material m in _bridgeMaterials) if (m != null) Destroy(m);
            _bridgeMaterials.Clear();
            _bridgeDesign = null;
        }

        /// <summary>Every frame while a bridge is being drawn: stretch the ghost to where the hammer is aiming.</summary>
        private void UpdateBridge(Player player)
        {
            if (!_bridgeStart.HasValue) return;
            if (player == null || player.IsDead() || (!_bridgeEndFixed && (!player.InPlaceMode() || !IsBridgeTool(player.GetSelectedPiece())))) { CancelBridge(); return; }
            Vector3 end = _bridgeEnd;
            if (!_bridgeEndFixed)
            {
                GameObject ghost = PlacementGhost(player);
                if (ghost == null || !ghost.activeSelf) return;
                end = ghost.transform.position;
                if ((end - _bridgeEnd).sqrMagnitude < 0.09f && _bridgeDesign != null && !_bridgeDirty) return;
                if (Time.unscaledTime - _bridgeAt < 0.15f && _bridgeDesign != null && !_bridgeDirty) return;   // a few times a second is plenty
            }
            else if (!_bridgeDirty) return;   // the end is set: only the settings change it now
            _bridgeAt = Time.unscaledTime;
            _bridgeDirty = false;
            _bridgeEnd = end;
            _bridgeDesign = DesignBridge(_bridgeStart.Value, end, _bridgeOpts ?? CurrentBridgeOptions());

            // the preview: reuse the ghosts already made where the piece is the same, make or hide the rest
            Quaternion turn = Quaternion.Euler(0f, _bridgeDesign.Yaw, 0f);
            Vector3 start = _bridgeStart.Value;
            int i = 0;
            for (; i < _bridgeDesign.Entries.Count && i < 700; i++)
            {
                Entry e = _bridgeDesign.Entries[i];
                Vector3 pos = start + turn * e.Local;
                Quaternion rot = turn * e.Rot;
                if (i < _bridgeGhosts.Count && _bridgeGhosts[i].Key != e.Prefab)
                {
                    if (_bridgeGhosts[i].Value != null) Destroy(_bridgeGhosts[i].Value);
                    _bridgeGhosts[i] = new KeyValuePair<string, GameObject>(e.Prefab, null);
                }
                if (i >= _bridgeGhosts.Count) _bridgeGhosts.Add(new KeyValuePair<string, GameObject>(e.Prefab, null));
                GameObject go = _bridgeGhosts[i].Value;
                if (go == null)
                {
                    go = MakeGhostObject(e.Prefab, pos, rot, _bridgeMaterials);
                    if (go == null) continue;
                    go.name = e.Prefab + "_bridge_preview";
                    TintWith(go, new Color(PreviewColor.r, PreviewColor.g, PreviewColor.b, Mathf.Clamp(_ghostOpacity.Value * 1.4f, 0.12f, 0.6f)));
                    _bridgeGhosts[i] = new KeyValuePair<string, GameObject>(e.Prefab, go);
                }
                if (!go.activeSelf) go.SetActive(true);
                go.transform.SetPositionAndRotation(pos, rot);
            }
            for (; i < _bridgeGhosts.Count; i++) if (_bridgeGhosts[i].Value != null && _bridgeGhosts[i].Value.activeSelf) _bridgeGhosts[i].Value.SetActive(false);
        }

        private void DrawBridgeBanner(float sw)
        {
            if (!_bridgeStart.HasValue || BridgeOptionsOpen) return;
            BridgeDesign d = _bridgeDesign;
            var r = new Rect(sw / 2f - 450f, 70f, 900f, d != null && d.Warnings.Count > 0 ? 78f : 56f);
            Round(r, new Color(0.04f, 0.08f, 0.1f, 0.88f), 8f);
            Outline(r, new Color(PreviewColor.r, PreviewColor.g, PreviewColor.b, 0.9f), 8f);
            string head = d == null ? "BRIDGE: aim where it should end" :
                $"BRIDGE   {d.Length:0} m   ·   {d.Entries.Count} pieces   ·   " + string.Join(", ", MaterialsOf(d.Entries.Select(e => e.Prefab)).Select(kv => $"{kv.Value} {Localization.instance.Localize(kv.Key)}").ToArray());
            Label(new Rect(r.x, r.y + 4f, r.width, 22f), head, _bold, PreviewColor, TextAnchor.MiddleCenter);
            Label(new Rect(r.x, r.y + 28f, r.width, 20f), "Walk or swim across and aim where it ends   ·   Click: plan it   ·   Esc (or another piece): cancel", _text, Color.white, TextAnchor.MiddleCenter);
            if (d != null && d.Warnings.Count > 0)
                Label(new Rect(r.x, r.y + 50f, r.width, 20f), string.Join("   ·   ", d.Warnings.ToArray()), _bold, new Color(1f, 0.65f, 0.4f), TextAnchor.MiddleCenter);
        }

        // ---- bridges are not levelled, and are not moved (their posts are cut to the riverbed where they stand) ----

        private string LastPlacedTitle()
        {
            try { return (string)Newtonsoft.Json.Linq.JObject.Parse(System.IO.File.ReadAllText(ImportsFile))["_last"] ?? "Bridge"; }
            catch (Exception) { return "Bridge"; }
        }

        private void MarkNoLevel(string key)
        {
            try
            {
                var all = Newtonsoft.Json.Linq.JObject.Parse(System.IO.File.ReadAllText(ImportsFile));
                string title = key.StartsWith(BlueprintPrefix) ? key.Substring(BlueprintPrefix.Length) : key;
                if (all[title] is Newtonsoft.Json.Linq.JObject rec) { rec["bridge"] = true; System.IO.File.WriteAllText(ImportsFile, all.ToString()); }
            }
            catch (Exception) { }
        }

        private static HashSet<string> _bridgeTitles = new HashSet<string>();
        private static float _bridgeTitlesAt = -99f;

        /// <summary>Is this placed plan a bridge? (Read from _imports.json at most every two seconds: the window asks every frame.)</summary>
        internal static bool IsBridgePlan(string title)
        {
            if (Time.unscaledTime - _bridgeTitlesAt > 2f)
            {
                _bridgeTitlesAt = Time.unscaledTime;
                try
                {
                    var all = Newtonsoft.Json.Linq.JObject.Parse(System.IO.File.ReadAllText(ImportsFile));
                    _bridgeTitles = new HashSet<string>(all.Properties().Where(p => p.Value is Newtonsoft.Json.Linq.JObject o && (bool?)o["bridge"] == true).Select(p => p.Name));
                }
                catch (Exception) { }
            }
            return _bridgeTitles.Contains(title);
        }

        /// <summary>Plan a bridge between two points straight away (for the assistant's "bridge" command). Returns the design used.</summary>
        internal BridgeDesign PlanBridge(Player player, Vector3 start, Vector3 end, BridgeOptions options = null)
        {
            BridgeDesign design = DesignBridge(start, end, options ?? CurrentBridgeOptions());
            if (design.Entries.Count == 0) return design;
            PlaceEntries(player, "Bridge", null, design.Entries, start, design.Yaw, 0f, fixedBaseY: start.y);
            MarkNoLevel(BlueprintPrefix + LastPlacedTitle());
            _bridgeTitlesAt = -99f;
            return design;
        }

        /// <summary>The build-menu picture: a little bridge, drawn the way blueprint pictures are.</summary>
        private Sprite BridgeIcon()
        {
            if (ZNetScene.instance == null) return null;
            var root = new GameObject("BridgeIconStage");
            root.transform.position = new Vector3(0f, -3500f, 0f);
            int layer = 30;
            try
            {
                // three planks with posts and rails, by hand (the design reads the ground, which is not there)
                var parts = new List<KeyValuePair<string, Matrix4x4>>();
                for (int s = 0; s < 3; s++) parts.Add(new KeyValuePair<string, Matrix4x4>("wood_floor", Matrix4x4.TRS(new Vector3(0, 0.05f, s * 2 + 1), Quaternion.identity, Vector3.one)));
                for (int k = 0; k <= 3; k++)
                    foreach (float x in new[] { -0.85f, 0.85f })
                    {
                        parts.Add(new KeyValuePair<string, Matrix4x4>("wood_pole", Matrix4x4.TRS(new Vector3(x, 0.55f, k * 2), Quaternion.identity, Vector3.one)));
                        if (k < 3) parts.Add(new KeyValuePair<string, Matrix4x4>("wood_beam", Matrix4x4.TRS(new Vector3(x, 1.1f, k * 2 + 1), Quaternion.Euler(0, -90, 0), Vector3.one)));
                        if (k % 2 == 0 && x < 0f || k == 3) parts.Add(new KeyValuePair<string, Matrix4x4>("wood_pole2", Matrix4x4.TRS(new Vector3(x, -1.05f, k * 2), Quaternion.identity, Vector3.one)));
                    }
                foreach (var part in parts)
                {
                    GameObject prefab = ZNetScene.instance.GetPrefab(part.Key);
                    if (prefab == null) continue;
                    ZNetView.m_forceDisableInit = true;
                    GameObject copy;
                    try { copy = Instantiate(prefab, root.transform); }
                    finally { ZNetView.m_forceDisableInit = false; }
                    copy.transform.localPosition = part.Value.GetColumn(3);
                    copy.transform.localRotation = part.Value.rotation;
                    foreach (Collider c in copy.GetComponentsInChildren<Collider>(true)) c.enabled = false;
                }
                foreach (Transform t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;

                var camGo = new GameObject("BridgeIconCamera");
                camGo.transform.SetParent(root.transform, false);
                Camera cam = camGo.AddComponent<Camera>();
                cam.enabled = false;
                cam.cullingMask = 1 << layer;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
                cam.fieldOfView = 30f;
                Vector3 centre = root.transform.position + new Vector3(0f, 0f, 3f);
                cam.transform.position = centre + new Vector3(5.5f, 3.2f, -4.5f);
                cam.transform.LookAt(centre);
                var lightGo = new GameObject("BridgeIconLight");
                lightGo.transform.SetParent(root.transform, false);
                lightGo.transform.rotation = Quaternion.Euler(40f, 30f, 0f);
                Light light = lightGo.AddComponent<Light>();
                light.type = LightType.Directional;
                light.intensity = 1.3f;
                light.cullingMask = 1 << layer;

                RenderTexture rt = RenderTexture.GetTemporary(256, 256, 24, RenderTextureFormat.ARGB32);
                RenderTexture prev = RenderTexture.active;
                try
                {
                    cam.targetTexture = rt;
                    cam.Render();
                    RenderTexture.active = rt;
                    var tex = new Texture2D(256, 256, TextureFormat.RGBA32, false);
                    tex.ReadPixels(new Rect(0, 0, 256, 256), 0, 0);
                    tex.Apply();
                    return Sprite.Create(tex, new Rect(0, 0, 256, 256), new Vector2(0.5f, 0.5f), 100f);
                }
                finally
                {
                    RenderTexture.active = prev;
                    cam.targetTexture = null;
                    RenderTexture.ReleaseTemporary(rt);
                }
            }
            finally { DestroyImmediate(root); }
        }
    }

    [HarmonyPatch(typeof(ZNetScene), "Awake")]
    internal static class ZNetScene_Awake_Bridge
    {
        private static void Postfix(ZNetScene __instance) => Plugin.Instance?.RegisterBridgeTool(__instance);
    }
}
