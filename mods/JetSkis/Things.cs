using System.IO;
using System.Linq;
using UnityEngine;

namespace JetSkis
{
    /// <summary>
    /// The Jet Ski piece: a copy of the game's raft (so it floats, saves, wears, breaks and changes owner in multiplayer as the game's boats
    /// do) with the raft's logs, mast, sail, ladder and seat taken off, its hit boxes and water triggers shrunk to a jet ski, and the new body
    /// (Model.cs) put on. Registered from the scene, again whenever the game swaps its lists, and replaced in place on a hot reload
    /// (Shared/PieceReload.cs) so no jet ski in a loading area is ever lost.
    /// </summary>
    internal static class Things
    {
        internal const string Prefab = "dh_jetski";
        internal static string AttachAnimation = "attach_chair";   // the game's seat animation the rider sits in (the pose goes on top: Rider.cs)

        private static GameObject _holder, _prefab;

        private static PieceTable Hammer(ZNetScene scene) => scene.GetPrefab("Hammer")?.GetComponent<ItemDrop>()?.m_itemData.m_shared.m_buildPieces;

        internal static void Register()
        {
            ZNetScene scene = ZNetScene.instance;
            if (scene == null) return;   // (nothing to copy yet: asked again when it wakes)
            if (_prefab == null)
            {
                GameObject raft = scene.GetPrefab("Raft");
                if (raft == null) { Plugin.Log.LogWarning("The game's Raft was not found, so there is no Jet Ski"); return; }
                _holder = new GameObject("JetSkiPrefabs");
                _holder.SetActive(false); // keeps the copy from waking up as a real object
                Object.DontDestroyOnLoad(_holder);
                try { Look.Harvest(scene, raft.GetComponentInChildren<Renderer>(true).sharedMaterial); }
                catch (System.Exception e) { Plugin.Log.LogWarning("Could not take the game's materials, the jet ski looks plain: " + e.Message); }
                _prefab = Build(scene, raft);
            }
            DHack.Shared.PieceReload.ReplacePrefab(scene, _prefab, Hammer(scene));
            Plugin.Synced.NeedsServer(_prefab);   // not in the build menu on a server without the mod (it would delete the jet ski)
            int swapped = DHack.Shared.PieceReload.Reattach(scene, _prefab);
            if (swapped > 0) Plugin.Log.LogInfo($"{swapped} jet ski(s) in the world now run this copy");
        }

        /// <summary>
        /// When this copy unloads: out of the build menu, but still registered, so no area loading before the next copy is up loses its jet
        /// skis (Shared/PieceReload.cs). The prefab stays until the next copy replaces it, or the game ends.
        /// </summary>
        internal static void Unregister()
        {
            if (_prefab == null) return;
            ZNetScene scene = ZNetScene.instance;
            DHack.Shared.PieceReload.UnloadPiece(_prefab, scene != null ? Hammer(scene) : null);
            _prefab = null;
            _holder = null;
            Look.Clear();
        }

        /// <summary>The cost setting (the server's in multiplayer) on the piece. One that can't be read keeps the last good cost.</summary>
        internal static void ApplyCost()
        {
            Piece piece = _prefab != null ? _prefab.GetComponent<Piece>() : null;
            if (piece != null) DHack.Shared.Costs.Apply(piece, Plugin.Cost.Value, Plugin.Log);
        }

        private static GameObject Build(ZNetScene scene, GameObject raft)
        {
            GameObject go = Object.Instantiate(raft, _holder.transform);
            go.name = Prefab;
            Transform root = go.transform;
            Ship ship = go.GetComponent<Ship>();
            int layer = root.Find("ship/colliders/Cube")?.gameObject.layer ?? go.layer;

            // the raft's own look goes: its logs and planks, the mast and sail, the ropes (an empty mast stays: the game turns it with the wind)
            var mast = new GameObject("mast");
            mast.transform.SetParent(root, false);
            ship.m_mastObject = mast;
            ship.m_sailObject = null;
            ship.m_hasSail = false;
            ship.m_rudderObject = null;   // the handlebars turn instead (JetSki.cs)
            Kill(root, "ship/visual");
            foreach (LODGroup lod in go.GetComponentsInChildren<LODGroup>(true)) Object.DestroyImmediate(lod);
            WearNTear wear = go.GetComponent<WearNTear>();
            if (wear != null) { wear.m_new = null; wear.m_worn = null; wear.m_broken = null; }

            // its hit boxes: one box for the hull instead of a deck of logs
            Transform cols = root.Find("ship/colliders");
            if (cols != null)
                foreach (Transform c in cols.Cast<Transform>().ToList())
                    if (c.name != "Cube") Object.DestroyImmediate(c.gameObject);
            Transform hull = root.Find("ship/colliders/Cube");
            if (hull != null) { hull.localPosition = new Vector3(0f, 0.05f, 0.1f); hull.localRotation = Quaternion.identity; hull.localScale = new Vector3(1.05f, 0.5f, 2.9f); }

            // where the game feels the water, and who is aboard
            Transform floatBox = root.Find("FloatCollider");
            if (floatBox != null) { floatBox.GetComponent<BoxCollider>().size = new Vector3(1.1f, 1f, 2.9f); floatBox.localPosition = new Vector3(0f, 0f, 0.1f); }
            Transform onboard = root.Find("OnboardTrigger");
            if (onboard != null) { onboard.localPosition = new Vector3(0f, 1.0f, 0.1f); onboard.localScale = new Vector3(1.8f, 2.4f, 3.6f); }

            // the raft's sitting spot and ladder go; its controls move to the saddle
            Kill(root, "interactive/mast");
            Kill(root, "interactive/ladder");
            Kill(root, "interactive/ladder_TargetPoint");
            Kill(root, "interactive/controls/rudder");
            Kill(root, "interactive/controls/attachpoint");
            ShipControlls controls = go.GetComponentInChildren<ShipControlls>(true);
            controls.transform.localPosition = Vector3.zero;
            controls.transform.localRotation = Quaternion.identity;
            // the hit boxes you look at and press E on: on a layer that players don't bump into (the rider sits inside one)
            int handles = LayerMask.NameToLayer("piece_nonsolid");
            if (handles < 0) handles = controls.gameObject.layer;
            controls.gameObject.layer = handles;
            BoxCollider grab = controls.GetComponent<BoxCollider>();
            grab.center = new Vector3(0f, 0.8f, -0.1f);   // the front of the saddle and the handlebars: the rider's
            grab.size = new Vector3(0.8f, 0.8f, 0.95f);
            controls.m_hoverText = "$jetskis_ride";
            controls.m_maxUseRange = 4f;
            controls.m_attachAnimation = AttachAnimation;
            controls.m_detachOffset = new Vector3(0f, 0.6f, 0f);
            Transform gui = root.Find("ControlGui");
            if (gui != null) gui.localPosition = new Vector3(0f, 1.3f, -0.4f);

            // the wake and the water sounds, moved to a jet ski's bow and stern; the sail's flapping goes
            Move(root, "WaterEffects/SpeedWake/aft_particles", new Vector3(0f, 0.55f, -1.6f));
            Move(root, "WaterEffects/SpeedWake/vfx_water_surface", new Vector3(0f, 0.55f, 1.4f));
            Transform shadow = root.Find("WaterEffects/WaterSurface/shadow");
            if (shadow != null) shadow.localScale = new Vector3(2.6f, 4.6f, shadow.localScale.z);
            ShipEffects effects = go.GetComponentInChildren<ShipEffects>(true);
            if (effects != null) effects.m_sailSound = null;
            Kill(root, "WaterEffects/SailSound");

            // the boat's handling: no sail and no paddling (the motor and the steering are JetSki.cs's), less hurt from hopping waves
            ship.m_sailForceFactor = 0f;
            ship.m_backwardForce = 0f;
            ship.m_stearForce = 0f;
            ship.m_stearVelForceFactor = 0f;
            ship.m_dampingForward = 0f;
            ship.m_dampingSideway = 0f;
            ship.m_waterImpactDamage = 2f;
            ImpactEffect impact = go.GetComponent<ImpactEffect>();
            if (impact != null) impact.m_damagePlayers = false;   // running into a friend at speed doesn't hurt them

            // the new body (the look never stops the piece being registered: without it, the host would delete the jet skis in the world)
            Transform model = null;
            try { model = Model.Build(root, layer); }
            catch (System.Exception e) { Plugin.Log.LogWarning("Could not build the jet ski's model: " + e); }
            var seat = new GameObject("attachpoint").transform;   // the rider sits here, and leans with the body
            seat.SetParent(model != null ? model : root, false);
            seat.localPosition = Model.SeatPoint;
            controls.m_attachPoint = seat;

            // the passenger's seat behind: the game's own seat (Chair), on a boat
            var back = new GameObject("passenger") { layer = handles };
            back.transform.SetParent(root, false);
            var backBox = back.AddComponent<BoxCollider>();
            backBox.center = new Vector3(0f, 0.8f, Model.PassengerPoint.z);
            backBox.size = new Vector3(0.6f, 0.6f, 0.42f);
            var backSeat = new GameObject("attachpoint").transform;
            backSeat.SetParent(model != null ? model : root, false);
            backSeat.localPosition = Model.PassengerPoint;
            Chair chair = back.AddComponent<Chair>();
            chair.m_name = "$jetskis_passenger";
            chair.m_useDistance = 3.5f;
            chair.m_attachPoint = backSeat;
            chair.m_attachAnimation = AttachAnimation;
            chair.m_inShip = true;
            chair.m_detachOffset = new Vector3(0f, 0.6f, 0f);

            // the cowl: look at it and press E to paint the jet ski (PaintSpot.cs)
            var cowl = new GameObject("paint") { layer = handles };
            cowl.transform.SetParent(root, false);
            var cowlBox = cowl.AddComponent<BoxCollider>();
            cowlBox.center = new Vector3(0f, 0.48f, 0.78f);
            cowlBox.size = new Vector3(0.5f, 0.32f, 0.75f);
            cowl.AddComponent<PaintSpot>();

            // the saddlebags: a small store, as the Karve's hold (its contents drop in a cargo crate if the jet ski breaks)
            var bags = new GameObject("saddlebags") { layer = handles };
            bags.transform.SetParent(root, false);
            foreach (float side in new[] { -1f, 1f })
            {
                // both bags' hit boxes on the store's own object: the game only opens what is on the very object its look ray hits
                var bagBox = bags.AddComponent<BoxCollider>();
                bagBox.center = Model.BagCentre(side);
                bagBox.size = new Vector3(0.2f, 0.4f, 0.36f);
            }
            Container hold = bags.AddComponent<Container>();
            hold.m_name = "$jetskis_bags";
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
            else Plugin.Log.LogWarning("The Karve's hold was not found: the saddlebags drop nothing special if the jet ski breaks");

            Piece piece = go.GetComponent<Piece>();
            piece.m_name = "$jetskis_jetski";
            piece.m_description = "$jetskis_jetski_desc";
            piece.m_usage = Piece.UsageTagFlags.Transport;   // the game's build-menu tags (the raft's, said outright)
            Sprite icon = Icon();
            if (icon != null) piece.m_icon = icon;
            piece.m_craftingStation = scene.GetPrefab("piece_workbench")?.GetComponent<CraftingStation>();
            // the cost setting (a server owner can change it); if neither it nor the default can be read, the raft's: never free to build
            if (!DHack.Shared.Costs.Apply(piece, Plugin.Cost.Value, Plugin.Log) && !DHack.Shared.Costs.Apply(piece, Plugin.DefaultCost, Plugin.Log))
                piece.m_resources = raft.GetComponent<Piece>().m_resources;

            go.AddComponent<JetSki>();
            return go;
        }

        private static void Kill(Transform root, string path)
        {
            Transform t = root.Find(path);
            if (t != null) Object.DestroyImmediate(t.gameObject);
        }

        private static void Move(Transform root, string path, Vector3 at)
        {
            Transform t = root.Find(path);
            if (t != null) t.localPosition = at;
        }

        // Texture2D.LoadImage lives in a module the build cannot reference directly, so it is called by name (as BirdTrap does).
        private static readonly System.Reflection.MethodInfo LoadImage =
            System.Type.GetType("UnityEngine.ImageConversion, UnityEngine.ImageConversionModule")?.GetMethod("LoadImage", new[] { typeof(Texture2D), typeof(byte[]) });

        /// <summary>The build menu's picture, built into the DLL (null without one: the raft's stays).</summary>
        private static Sprite Icon()
        {
            try
            {
                using (Stream s = typeof(Things).Assembly.GetManifestResourceStream("icon-piece.png"))
                {
                    if (s == null) return null;
                    var bytes = new byte[s.Length];
                    s.Read(bytes, 0, bytes.Length);
                    var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    if (LoadImage == null || !(bool)LoadImage.Invoke(null, new object[] { tex, bytes })) return null;
                    return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
                }
            }
            catch (System.Exception e) { Plugin.Log.LogWarning("Could not load the jet ski's build-menu picture: " + e.Message); return null; }
        }
    }
}
