using DHack.Shared;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace Terminal
{
    /// <summary>
    /// The Terminal piece: a copy of the game's wooden chest with its storage and look taken off, our model (Model.cs) and TerminalPiece
    /// on it. Registered from the scene (both Awake hooks and the plugin's Awake, per docs/modding-pitfalls.md) and replaced in place on a
    /// hot reload (Shared/PieceReload.cs) so no area loading meanwhile loses a terminal.
    /// </summary>
    internal static class Things
    {
        internal const string Prefab = "dh_terminal";

        private static GameObject _holder, _prefab;

        private static PieceTable Hammer(ZNetScene scene) => scene.GetPrefab("Hammer")?.GetComponent<ItemDrop>()?.m_itemData.m_shared.m_buildPieces;

        internal static void Register()
        {
            ZNetScene scene = ZNetScene.instance;
            if (scene == null) return;
            GameObject source = scene.GetPrefab("piece_chest_wood");
            if (source == null || scene.GetPrefab("Hammer") == null) { Plugin.Log.LogWarning("The chest or hammer prefab was not found, so there is no terminal"); return; }
            if (_prefab == null)
            {
                _holder = new GameObject("TerminalPrefabs");
                _holder.SetActive(false);   // keeps the copy from waking up as a real object
                Object.DontDestroyOnLoad(_holder);
                try { Model.Harvest(scene, source.GetComponentInChildren<Renderer>(true).sharedMaterial); }
                catch (System.Exception e) { Plugin.Log.LogWarning("Could not take the game's materials, the terminal looks plain: " + e.Message); }
                _prefab = Make(scene, source);
            }
            PieceReload.ReplacePrefab(scene, _prefab, Hammer(scene));
            Plugin.Synced.NeedsServer(_prefab);   // not in the build menu on a server without the mod (it would delete the terminal)
            int swapped = PieceReload.Reattach(scene, _prefab);
            if (swapped > 0) Plugin.Log.LogInfo($"{swapped} terminal(s) in the world now run this copy");
        }

        /// <summary>When this copy unloads: out of the build menu, still registered (Shared/PieceReload.cs).</summary>
        internal static void Unregister()
        {
            if (_prefab == null) return;
            ZNetScene scene = ZNetScene.instance;
            PieceReload.UnloadPiece(_prefab, scene != null ? Hammer(scene) : null);
            _prefab = null;
            _holder = null;
            Model.Clear();
        }

        /// <summary>The hammer's build menu lists the terminal (the "terminal status" test command).</summary>
        internal static bool InMenu()
        {
            ZNetScene scene = ZNetScene.instance;
            PieceTable table = scene != null ? Hammer(scene) : null;
            return _prefab != null && table != null && table.m_pieces.Contains(_prefab);
        }

        internal static void ApplyCost()
        {
            Piece piece = _prefab != null ? _prefab.GetComponent<Piece>() : null;
            if (piece != null) DHack.Shared.Costs.Apply(piece, Plugin.Cost.Value, Plugin.Log);
        }

        private static GameObject Make(ZNetScene scene, GameObject source)
        {
            GameObject go = Object.Instantiate(source, _holder.transform);
            go.name = Prefab;
            Container container = go.GetComponent<Container>();
            if (container != null) Object.DestroyImmediate(container);   // it is not storage (pitfall: A Container isn't always a chest)

            int layer = LayerMask.NameToLayer("piece");
            if (layer < 0) layer = go.layer;
            Renderer[] chest = go.GetComponentsInChildren<Renderer>(true);
            foreach (Collider c in go.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
            // what you look at and bump into: the desk (and the screen above it) and the stool
            Hit(go.transform, "desk", new Vector3(0f, 0.4f, 0f), new Vector3(1.7f, 0.8f, 0.85f), layer);
            Hit(go.transform, "monitor", new Vector3(0f, 1.12f, 0.2f), new Vector3(0.9f, 0.55f, 0.1f), layer);
            Hit(go.transform, "stool", new Vector3(0f, 0.25f, -0.75f), new Vector3(0.45f, 0.5f, 0.45f), layer);

            try
            {
                Model.Build(go.transform);
                foreach (Renderer r in chest) r.enabled = false;   // hide the chest
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning("Could not build the terminal's look, it keeps the chest's: " + e);
                Transform model = go.transform.Find("Model");
                if (model != null) Object.DestroyImmediate(model.gameObject);
            }
            if (go.transform.Find("seat") == null) Plugin.Log.LogWarning("The terminal has no seat point");

            DHack.Shared.SnapPoints.Clear(go);
            DHack.Shared.SnapPoints.Box(go, new Vector3(-0.85f, 0f, -1.0f), new Vector3(0.85f, 0f, 0.43f));

            Piece piece = go.GetComponent<Piece>();
            piece.m_name = "$terminal_piece";
            piece.m_description = "$terminal_piece_desc";
            piece.m_category = Piece.PieceCategory.Misc;
            piece.m_usage = Piece.UsageTagFlags.Furniture | Piece.UsageTagFlags.Decor;
            piece.m_craftingStation = scene.GetPrefab("piece_workbench")?.GetComponent<CraftingStation>();
            // the cost setting (a server owner can change it); if neither it nor the default can be read, the chest's stays: never free to build
            if (!DHack.Shared.Costs.Apply(piece, Plugin.Cost.Value, Plugin.Log)) DHack.Shared.Costs.Apply(piece, Plugin.DefaultCost, Plugin.Log);
            try
            {
                Sprite picture = DHack.Shared.Icon.Make(go);
                if (picture != null) piece.m_icon = picture;
            }
            catch (System.Exception e) { Plugin.Log.LogWarning("Could not draw the build-menu picture: " + e.Message); }
            go.AddComponent<TerminalPiece>();
            return go;
        }

        private static void Hit(Transform parent, string name, Vector3 centre, Vector3 size, int layer)
        {
            var go = new GameObject(name) { layer = layer };
            go.transform.SetParent(parent, false);
            var box = go.AddComponent<BoxCollider>();
            box.center = centre;
            box.size = size;
        }
    }
}
