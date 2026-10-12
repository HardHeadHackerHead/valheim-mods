using DHack.Shared;
using UnityEngine;

namespace MountainBikes
{
    /// <summary>
    /// The Mountain Bike piece: the game's raft with everything of a boat taken off (Shared/GroundVehicle.cs VehicleKit.CloneBase), so it is
    /// saved, synced, worn and owned in multiplayer the way the game's moving pieces are, with the body (Model.cs)
    /// and hit boxes added, and GroundVehicle (the driving) on top. Registered from the scene, again whenever the game swaps
    /// its lists, and replaced in place on a hot reload (Shared/PieceReload.cs) so none in a loading area is ever lost.
    /// </summary>
    internal static class Things
    {
        internal const string Prefab = "dh_bike";

        private static GameObject _holder, _prefab;

        private static PieceTable Hammer(ZNetScene scene) => scene.GetPrefab("Hammer")?.GetComponent<ItemDrop>()?.m_itemData.m_shared.m_buildPieces;

        internal static void Register()
        {
            ZNetScene scene = ZNetScene.instance;
            if (scene == null) return;   // (nothing to copy yet: asked again when it wakes)
            if (_prefab == null)
            {
                GameObject raft = scene.GetPrefab("Raft");
                if (raft == null) { Plugin.Log.LogWarning("The game's Raft was not found, so there is no Mountain Bike"); return; }
                _holder = new GameObject("MountainBikePrefabs");
                _holder.SetActive(false); // keeps the copy from waking up as a real object
                Object.DontDestroyOnLoad(_holder);
                try { VehicleLook.Harvest(scene, raft.GetComponentInChildren<Renderer>(true).sharedMaterial); }
                catch (System.Exception e) { Plugin.Log.LogWarning("Could not take the game's materials, the mountain bike looks plain: " + e.Message); }
                _prefab = Build(scene, raft);
                if (_prefab == null) return;
            }
            PieceReload.ReplacePrefab(scene, _prefab, Hammer(scene));
            Plugin.Synced.NeedsServer(_prefab);   // not in the build menu on a server without the mod (it would delete the mountain bike)
            int swapped = PieceReload.Reattach(scene, _prefab);
            if (swapped > 0) Plugin.Log.LogInfo($"{swapped} mountain bike(s) in the world now run this copy");
        }

        /// <summary>
        /// When this copy unloads: out of the build menu, but still registered, so no area loading before the next copy is up loses its
        /// mountain bikes (Shared/PieceReload.cs).
        /// </summary>
        internal static void Unregister()
        {
            if (_prefab == null) return;
            ZNetScene scene = ZNetScene.instance;
            PieceReload.UnloadPiece(_prefab, scene != null ? Hammer(scene) : null);
            _prefab = null;
            _holder = null;
            VehicleLook.Clear();
        }

        /// <summary>The cost setting (the server's in multiplayer) on the piece. One that can't be read keeps the last good cost.</summary>
        internal static void ApplyCost()
        {
            Piece piece = _prefab != null ? _prefab.GetComponent<Piece>() : null;
            if (piece != null) Costs.Apply(piece, Plugin.Cost.Value, Plugin.Log);
        }

        private static GameObject Build(ZNetScene scene, GameObject raft)
        {
            GameObject go = VehicleKit.CloneBase(scene, _holder.transform, Prefab, out int layer);
            if (go == null) return null;
            Transform root = go.transform;
            int handles = LayerMask.NameToLayer("piece_nonsolid");   // the hit boxes you look at and press E on: nothing bumps into them
            if (handles < 0) handles = layer;

            // what you bump into, and what you look at to climb on
            VehicleKit.Box(root, "hull", new Vector3(0f, 0.8f, 0f), new Vector3(0.28f, 0.42f, 0.7f), layer);   // (short and high: the wheels carry it, so on a slope the frame never meets the ground first)
            VehicleKit.Box(root, "use", new Vector3(0f, 0.65f, -0.5f), new Vector3(0.6f, 1.1f, 0.9f), handles);
            VehicleKit.Box(root, "paintspot", new Vector3(0f, 0.8f, 0.2f), new Vector3(0.5f, 0.4f, 0.6f), handles).gameObject.AddComponent<VehiclePaintSpot>();   // (E on the frame in front of the saddle: the next colour)

            // the look (it never stops the piece being registered: without it, the host would delete the mountain bikes in the world)
            try { Model.Build(root); }
            catch (System.Exception e) { Plugin.Log.LogWarning("Could not build the mountain bike's model: " + e); }
            if (!System.Linq.Enumerable.Any(root.GetComponentsInChildren<Transform>(true), x => x.name == "seat")) Model.Seat(root, "seat", Model.SeatPoint);

            Piece piece = go.GetComponent<Piece>();
            piece.m_name = "$mountainbikes_bike";
            piece.m_description = "$mountainbikes_bike_desc";
            piece.m_usage = Piece.UsageTagFlags.Transport;
            piece.m_craftingStation = scene.GetPrefab("piece_workbench")?.GetComponent<CraftingStation>();
            // the cost setting (a server owner can change it); if neither it nor the default can be read, the raft's: never free to build
            if (!Costs.Apply(piece, Plugin.Cost.Value, Plugin.Log) && !Costs.Apply(piece, Plugin.DefaultCost, Plugin.Log))
                piece.m_resources = raft.GetComponent<Piece>().m_resources;

            // the build menu's picture, drawn from the model (the raft's stays if that fails)
            Sprite icon = Icon.Make(go, new Vector3(1f, 0.25f, 0.8f), null, null);
            if (icon != null) piece.m_icon = icon;

            go.AddComponent<GroundVehicle>();
            return go;
        }
    }
}
