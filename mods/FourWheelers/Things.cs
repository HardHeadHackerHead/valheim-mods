using DHack.Shared;
using UnityEngine;

namespace FourWheelers
{
    /// <summary>
    /// The Four Wheeler piece: the game's raft with everything of a boat taken off (Shared/GroundVehicle.cs VehicleKit.CloneBase), so it is
    /// saved, synced, worn and owned in multiplayer the way the game's moving pieces are, with the body (Model.cs), hit boxes, a
    /// passenger seat and a rack added, and GroundVehicle (the driving) on top. Registered from the scene, again whenever the game swaps
    /// its lists, and replaced in place on a hot reload (Shared/PieceReload.cs) so none in a loading area is ever lost.
    /// </summary>
    internal static class Things
    {
        internal const string Prefab = "dh_atv";

        private static GameObject _holder, _prefab;

        private static PieceTable Hammer(ZNetScene scene) => scene.GetPrefab("Hammer")?.GetComponent<ItemDrop>()?.m_itemData.m_shared.m_buildPieces;

        internal static void Register()
        {
            ZNetScene scene = ZNetScene.instance;
            if (scene == null) return;   // (nothing to copy yet: asked again when it wakes)
            if (_prefab == null)
            {
                GameObject raft = scene.GetPrefab("Raft");
                if (raft == null) { Plugin.Log.LogWarning("The game's Raft was not found, so there is no Four Wheeler"); return; }
                _holder = new GameObject("FourWheelerPrefabs");
                _holder.SetActive(false); // keeps the copy from waking up as a real object
                Object.DontDestroyOnLoad(_holder);
                try { VehicleLook.Harvest(scene, raft.GetComponentInChildren<Renderer>(true).sharedMaterial); }
                catch (System.Exception e) { Plugin.Log.LogWarning("Could not take the game's materials, the four wheeler looks plain: " + e.Message); }
                _prefab = Build(scene, raft);
                if (_prefab == null) return;
            }
            PieceReload.ReplacePrefab(scene, _prefab, Hammer(scene));
            Plugin.Synced.NeedsServer(_prefab);   // not in the build menu on a server without the mod (it would delete the four wheeler)
            int swapped = PieceReload.Reattach(scene, _prefab);
            if (swapped > 0) Plugin.Log.LogInfo($"{swapped} four wheeler(s) in the world now run this copy");
        }

        /// <summary>
        /// When this copy unloads: out of the build menu, but still registered, so no area loading before the next copy is up loses its
        /// four wheelers (Shared/PieceReload.cs).
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
            VehicleKit.Box(root, "hull", new Vector3(0f, 0.62f, -0.05f), new Vector3(0.8f, 0.4f, 1.8f), layer);
            VehicleKit.Box(root, "use", new Vector3(0f, 0.7f, -0.03f), new Vector3(1.1f, 0.9f, 0.6f), handles);   // (the rider's seat only: the passenger's and the rack's boxes sit behind it, not inside it)
            VehicleKit.Box(root, "paintspot", new Vector3(0f, 0.8f, 0.62f), new Vector3(0.7f, 0.4f, 0.7f), handles).gameObject.AddComponent<VehiclePaintSpot>();   // (E on the hood: the next colour)

            // the look (it never stops the piece being registered: without it, the host would delete the four wheelers in the world)
            try { Model.Build(root); }
            catch (System.Exception e) { Plugin.Log.LogWarning("Could not build the four wheeler's model: " + e); }
            if (root.Find("seat") == null) Model.Seat(root, "seat", Model.SeatPoint);   // (without a model the seats still exist)

            // the passenger's seat behind: the game's own seat (Chair)
            Transform pillion = root.Find("passenger") ?? Model.Seat(root, "passenger", Model.PassengerPoint);
            var back = new GameObject("passengerseat") { layer = handles };
            back.transform.SetParent(root, false);
            var backBox = back.AddComponent<BoxCollider>();
            backBox.center = new Vector3(0f, 0.9f, -0.55f);
            backBox.size = new Vector3(0.8f, 0.55f, 0.4f);
            Chair chair = back.AddComponent<Chair>();
            chair.m_name = "$fourwheelers_passenger";
            chair.m_useDistance = 3.5f;
            chair.m_attachPoint = pillion;
            chair.m_attachAnimation = "attach_chair";
            chair.m_inShip = false;
            chair.m_detachOffset = new Vector3(0f, 0.5f, 0f);

            // the rack: a small store (its contents drop in a cargo crate if the four wheeler breaks)
            var rack = new GameObject("rack") { layer = handles };
            rack.transform.SetParent(root, false);
            var rackBox = rack.AddComponent<BoxCollider>();
            rackBox.center = new Vector3(0f, 1.0f, -0.9f);
            rackBox.size = new Vector3(0.7f, 0.3f, 0.3f);
            Container hold = rack.AddComponent<Container>();
            hold.m_name = "$fourwheelers_bags";
            hold.m_width = 4;
            hold.m_height = 2;
            hold.m_rootObjectOverride = go.GetComponent<ZNetView>();
            Container karve = scene.GetPrefab("Karve")?.GetComponentInChildren<Container>(true);
            if (karve != null)
            {
                hold.m_bkg = karve.m_bkg;
                hold.m_openEffects = karve.m_openEffects;
                hold.m_closeEffects = karve.m_closeEffects;
                hold.m_destroyedLootPrefab = karve.m_destroyedLootPrefab;
            }
            else Plugin.Log.LogWarning("The Karve's hold was not found: the rack drops nothing special if the four wheeler breaks");

            Piece piece = go.GetComponent<Piece>();
            piece.m_name = "$fourwheelers_atv";
            piece.m_description = "$fourwheelers_atv_desc";
            piece.m_usage = Piece.UsageTagFlags.Transport;
            piece.m_craftingStation = scene.GetPrefab("piece_workbench")?.GetComponent<CraftingStation>();
            // the cost setting (a server owner can change it); if neither it nor the default can be read, the raft's: never free to build
            if (!Costs.Apply(piece, Plugin.Cost.Value, Plugin.Log) && !Costs.Apply(piece, Plugin.DefaultCost, Plugin.Log))
                piece.m_resources = raft.GetComponent<Piece>().m_resources;

            // the build menu's picture, drawn from the model (the raft's stays if that fails)
            Sprite icon = Icon.Make(go, new Vector3(0.8f, 0.32f, 1f), null, null);
            if (icon != null) piece.m_icon = icon;

            go.AddComponent<GroundVehicle>();
            return go;
        }
    }
}
