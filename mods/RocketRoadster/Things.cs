using DHack.Shared;
using UnityEngine;

namespace RocketRoadster
{
    /// <summary>
    /// The Rocket Roadster piece: the game's raft with everything of a boat taken off (Shared/GroundVehicle.cs VehicleKit.CloneBase), so it
    /// is saved, synced, worn and owned in multiplayer the way the game's moving pieces are, with the body (Model.cs), hit boxes, a passenger
    /// seat and the Roadster component (driving, thrusters, tricks) on top. Registered from the scene, again whenever the game swaps its
    /// lists, and replaced in place on a hot reload (Shared/PieceReload.cs) so none in a loading area is ever lost (docs/modding-pitfalls.md).
    /// </summary>
    internal static class Things
    {
        internal const string Prefab = "dh_roadster";

        private static GameObject _holder, _prefab;

        private static PieceTable Hammer(ZNetScene scene) => scene.GetPrefab("Hammer")?.GetComponent<ItemDrop>()?.m_itemData.m_shared.m_buildPieces;

        internal static void Register()
        {
            ZNetScene scene = ZNetScene.instance;
            if (scene == null) return;   // (nothing to copy yet: asked again when it wakes)
            if (_prefab == null)
            {
                GameObject raft = scene.GetPrefab("Raft");
                if (raft == null) { Plugin.Log.LogWarning("The game's Raft was not found, so there is no Rocket Roadster"); return; }
                _holder = new GameObject("RocketRoadsterPrefabs");
                _holder.SetActive(false); // keeps the copy from waking up as a real object
                Object.DontDestroyOnLoad(_holder);
                try
                {
                    VehicleLook.Harvest(scene, raft.GetComponentInChildren<Renderer>(true).sharedMaterial);
                    Look.Harvest(scene);
                }
                catch (System.Exception e) { Plugin.Log.LogWarning("Could not take the game's materials, the roadster looks plain: " + e.Message); }
                _prefab = Build(scene, raft);
                if (_prefab == null) return;
            }
            PieceReload.ReplacePrefab(scene, _prefab, Hammer(scene));
            Plugin.Synced.NeedsServer(_prefab);   // not in the build menu on a server without the mod (it would delete the roadster)
            int swapped = PieceReload.Reattach(scene, _prefab);
            if (swapped > 0) Plugin.Log.LogInfo($"{swapped} roadster(s) in the world now run this copy");
        }

        /// <summary>When this copy unloads: out of the build menu, but still registered (Shared/PieceReload.cs).</summary>
        internal static void Unregister()
        {
            if (_prefab == null) return;
            ZNetScene scene = ZNetScene.instance;
            PieceReload.UnloadPiece(_prefab, scene != null ? Hammer(scene) : null);
            _prefab = null;
            _holder = null;
            VehicleLook.Clear();
            Look.Clear();
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

            // what you bump into (and what it lands on when it comes down on its roof), and what you look at to climb in
            VehicleKit.Box(root, "hull", new Vector3(0f, 0.6f, 0f), new Vector3(2.1f, 0.62f, 5.1f), layer);
            VehicleKit.Box(root, "cabin", new Vector3(0f, 1.15f, -0.45f), new Vector3(1.5f, 0.55f, 2.6f), layer);
            VehicleKit.Box(root, "use", new Vector3(-0.6f, 0.95f, -0.1f), new Vector3(1.2f, 1.1f, 1.9f), handles);   // (the driver's side: it sticks out past the solid boxes, as the game takes the first box its look meets)
            VehicleKit.Box(root, "paintspot", new Vector3(0f, 0.98f, 1.75f), new Vector3(1.6f, 0.25f, 1.2f), handles).gameObject.AddComponent<VehiclePaintSpot>();   // (E on the hood: the next colour)

            // the look (it never stops the piece being registered: without it, the host would delete the roadsters in the world)
            try { Model.Build(root); }
            catch (System.Exception e) { Plugin.Log.LogWarning("Could not build the roadster's model: " + e); }
            if (root.Find("seat") == null) Model.Marker(root, "seat", Model.SeatPoint);   // (without a model the seats still exist)

            // the passenger's seat on the right: the game's own seat (Chair)
            Transform beside = root.Find("passenger") ?? Model.Marker(root, "passenger", Model.PassengerPoint);
            var right = new GameObject("passengerseat") { layer = handles };
            right.transform.SetParent(root, false);
            var box = right.AddComponent<BoxCollider>();
            box.center = new Vector3(0.6f, 0.95f, -0.1f);
            box.size = new Vector3(1.2f, 1.1f, 1.9f);
            Chair chair = right.AddComponent<Chair>();
            chair.m_name = "$roadster_passenger";
            chair.m_useDistance = 3.5f;
            chair.m_attachPoint = beside;
            chair.m_attachAnimation = "attach_chair";
            chair.m_inShip = false;
            chair.m_detachOffset = new Vector3(1.4f, 0.4f, 0f);   // (out of the right-hand door)

            Piece piece = go.GetComponent<Piece>();
            piece.m_name = "$roadster_name";
            piece.m_description = "$roadster_desc";
            piece.m_usage = Piece.UsageTagFlags.Transport;
            piece.m_craftingStation = scene.GetPrefab("forge")?.GetComponent<CraftingStation>() ?? scene.GetPrefab("piece_workbench")?.GetComponent<CraftingStation>();
            // the cost setting (a server owner can change it); if neither it nor the default can be read, the raft's: never free to build
            if (!Costs.Apply(piece, Plugin.Cost.Value, Plugin.Log) && !Costs.Apply(piece, Plugin.DefaultCost, Plugin.Log))
                piece.m_resources = raft.GetComponent<Piece>().m_resources;

            // the build menu's picture, drawn from the model (the raft's stays if that fails)
            Sprite icon = Icon.Make(go, new Vector3(0.9f, 0.35f, 1f), null, null);
            if (icon != null) piece.m_icon = icon;

            go.AddComponent<Roadster>();
            return go;
        }
    }
}
