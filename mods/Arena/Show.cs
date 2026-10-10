using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Arena
{
    /// <summary>
    /// The show around a fight: chests that rise out of the floor in the middle of the ring (the armourer's, with the next land's steel and
    /// meals; the prize chest after a win), and fireworks over the stands.
    ///
    /// The chests are the game's own chests, real ones (so you open them and take what is in them as from any chest), marked in their saved
    /// data: the armourer's is taken away when the round begins (anything left in it was lent, and goes back), and should the game close with
    /// one still standing it is cleared away the next time the arena is seen. A prize chest is yours: it stays until it is empty.
    /// </summary>
    internal static class Show
    {
        internal const int Armoury = 1, Prize = 2, Reward = 3;
        private const string ChestKey = "dh_arena_chest", ChestTimeKey = "dh_arena_chest_t";

        private static GameObject _chest;
        private static int _chestKind;
        private static int _chestItems;

        internal static bool ChestStanding => _chest != null;
        /// <summary>The chest standing in the ring has been emptied (everything put in it taken).</summary>
        internal static bool ChestEmptied => _chest != null && _chestItems > 0 && (_chest.GetComponent<Container>()?.GetInventory().NrOfItems() ?? 0) == 0;

        /// <summary>A chest rises out of the floor in the middle of the ring with these things in it. Lent things are marked as lent.</summary>
        internal static Container PopChest(int kind, IEnumerable<(string Prefab, int Amount, int Quality, string Loan)> items)
        {
            TakeChest();
            string name = kind == Armoury ? "piece_chest_blackmetal" : kind == Reward ? "piece_chest" : "piece_chest_treasure";
            GameObject prefab = ZNetScene.instance != null ? (ZNetScene.instance.GetPrefab(name) ?? ZNetScene.instance.GetPrefab("piece_chest")) : null;
            if (prefab == null) return null;
            // on whatever stands in the middle (the platform between lands), beside the player if they are standing right there
            Vector3 at = Floor(Site.Centre);
            Player me = Player.m_localPlayer;
            if (me != null)
            {
                Vector3 off = me.transform.position - at; off.y = 0f;
                if (off.magnitude < 1.3f) at = Floor(Site.Centre - (off.sqrMagnitude > 0.01f ? off.normalized : Site.Turn * Vector3.forward) * 1.6f);
            }
            Quaternion turn = Quaternion.LookRotation(Site.Turn * Vector3.back);   // (its lid toward the main gate, where you come in)
            GameObject go = Object.Instantiate(prefab, at, turn);
            ZNetView view = go.GetComponent<ZNetView>();
            if (view == null || !view.IsValid()) { Object.Destroy(go); return null; }
            view.GetZDO().Set(ChestKey, kind);   // (before the items: a container reloads when its saved data changes)
            view.GetZDO().Set(ChestTimeKey, ZNet.instance.GetTime().Ticks);
            Piece piece = go.GetComponent<Piece>();
            if (piece != null) piece.m_canBeRemoved = false;
            WearNTear wear = go.GetComponent<WearNTear>();
            if (wear != null) Object.Destroy(wear);   // (nothing breaks it in the ring)
            Container chest = go.GetComponent<Container>();
            if (chest == null) { view.Destroy(); return null; }
            chest.m_name = kind == Armoury ? "The Armourer's Chest" : kind == Reward ? "The Round's Reward" : "Your Prize";
            Inventory inv = chest.GetInventory();
            int count = 0;
            foreach (var it in items)
            {
                GameObject itemPrefab = ZNetScene.instance.GetPrefab(it.Prefab);
                ItemDrop drop = itemPrefab != null ? itemPrefab.GetComponent<ItemDrop>() : null;
                if (drop == null || it.Amount <= 0) continue;
                int left = it.Amount, max = Mathf.Max(1, drop.m_itemData.m_shared.m_maxStackSize);
                while (left > 0)
                {
                    int n = Mathf.Min(left, max);
                    left -= n;
                    ItemDrop.ItemData item = inv.AddItem(it.Prefab, n, Mathf.Clamp(it.Quality, 1, drop.m_itemData.m_shared.m_maxQuality), 0, 0L, "The Arena", false);
                    if (item == null) { Contest.Give(it.Prefab, n + left, Player.m_localPlayer); left = 0; break; }   // (a full chest: straight to you)
                    item.m_durability = item.GetMaxDurability();
                    if (it.Loan != null) item.m_customData[Kit.LoanKey] = it.Loan;
                    count++;
                }
            }
            inv.m_onChanged?.Invoke();   // (saved with the marks: an item changed in place is not saved by itself)
            _chest = go; _chestKind = kind; _chestItems = count;
            Plugin.Instance.StartCoroutine(Rise(go.transform, at));
            Fx.SpawnPuff(at);
            Effect("vfx_spawn_large", at);
            Effect("sfx_chest_open", at);
            return chest;
        }

        /// <summary>Clears what lies about on the floor of the ring from the last fight (thrown gifts, dropped things nobody picked up).</summary>
        internal static void ClearFloor()
        {
            int n = 0;
            foreach (ItemDrop drop in Object.FindObjectsOfType<ItemDrop>())
            {
                if (drop == null || !Site.OnFloor(drop.transform.position, 0.5f)) continue;
                ZNetView view = drop.GetComponent<ZNetView>();
                if (view == null || !view.IsValid()) continue;
                view.ClaimOwnership();
                view.Destroy();
                n++;
            }
            if (n > 0) Plugin.Log.LogInfo($"Cleared {n} things left lying in the ring");
        }

        /// <summary>Takes the chest standing in the ring away (whatever is left in an armourer's chest was lent, and goes with it).</summary>
        internal static void TakeChest()
        {
            if (_chest == null) return;
            GameObject go = _chest;
            _chest = null;
            Container chest = go.GetComponent<Container>();
            if (_chestKind == Prize && chest != null && chest.GetInventory().NrOfItems() > 0)
            {
                // the prize is yours: what is still in the chest goes in your bag
                Player p = Player.m_localPlayer;
                if (p != null)
                    foreach (ItemDrop.ItemData item in chest.GetInventory().GetAllItems().ToList())
                    {
                        if (!p.GetInventory().AddItem(item)) ItemDrop.DropItem(item, item.m_stack, p.transform.position + Vector3.up, Quaternion.identity);
                    }
                chest.GetInventory().RemoveAll();
            }
            if (InventoryGui.IsVisible() && InventoryGui.instance != null) InventoryGui.instance.Hide();
            Fx.SpawnPuff(go.transform.position);
            ZNetView view = go.GetComponent<ZNetView>();
            if (view != null && view.IsValid()) { view.ClaimOwnership(); view.Destroy(); } else Object.Destroy(go);
        }

        /// <summary>
        /// Chests the arena left standing (the game closed mid-fight): the armourer's go, a prize chest goes once it is empty. And any tombstone
        /// in the arena (from before tombstones were carried out properly) is carried out to the forecourt.
        /// </summary>
        internal static void Tidy()
        {
            // (never while anyone fights: another player's chest is that player's, standing in the ring for them right now)
            if (!Site.Known || Net.Busy || Player.m_localPlayer == null || Travel.Distance(Player.m_localPlayer.transform.position) > 300f) return;
            long now = ZNet.instance.GetTime().Ticks;
            foreach (Container c in Object.FindObjectsOfType<Container>())
            {
                if (c == null || c.gameObject == _chest) continue;
                ZNetView view = c.GetComponent<ZNetView>();
                if (view == null || !view.IsValid()) continue;
                int kind = view.GetZDO().GetInt(ChestKey, 0);
                if (kind == 0 || kind == Prize && c.GetInventory().NrOfItems() > 0) continue;
                if (now - view.GetZDO().GetLong(ChestTimeKey, 0L) < System.TimeSpan.TicksPerMinute * 15) continue;   // (only old ones: left by a fight long over)   // (the armourer's and the reward chests: lent things)
                view.ClaimOwnership();
                view.Destroy();
            }
            foreach (TombStone tomb in Object.FindObjectsOfType<TombStone>())
            {
                ZNetView view = tomb != null ? tomb.GetComponent<ZNetView>() : null;
                if (view == null || !view.IsValid()) continue;
                Vector3 local = Site.Local(tomb.transform.position);
                if (new Vector2(local.x, local.z).magnitude > Layout.Facade + 1f) continue;
                Vector3 spot = Contest.TombSpot(tomb.transform.position);
                view.ClaimOwnership();
                tomb.transform.position = spot;
                Rigidbody body = tomb.GetComponent<Rigidbody>();
                if (body != null) { body.position = spot; body.velocity = Vector3.zero; }
                view.GetZDO().SetPosition(spot);
                view.GetZDO().Set(ZDOVars.s_spawnPoint, spot);
                Plugin.Log.LogInfo($"A tombstone in the arena was carried out to the forecourt, at {spot}");
            }
            // fighters a fight left in the world (the fighter's game closed or crashed mid-round): taken away by the game that holds them now
            int fighters = 0;
            foreach (Character c in Character.GetAllCharacters().ToList())
            {
                if (c == null || c is Player) continue;
                ZNetView view = c.GetComponent<ZNetView>();
                if (view == null || !view.IsValid() || !view.IsOwner() || !view.GetZDO().GetBool("dh_arena", false)) continue;
                view.Destroy();
                fighters++;
            }
            if (fighters > 0) Plugin.Log.LogInfo($"Took away {fighters} of the arena's fighters left from a fight that never ended");
            // and no death markers at the arena on the map (you rise there, beside your things)
            if (Minimap.instance != null && HarmonyLib.AccessTools.Field(typeof(Minimap), "m_pins").GetValue(Minimap.instance) is List<Minimap.PinData> pins)
                foreach (Minimap.PinData pin in pins.Where(p => p.m_type == Minimap.PinType.Death && Travel.Distance(p.m_pos) < Layout.Footprint + 40f).ToList())
                    Minimap.instance.RemovePin(pin);
        }

        /// <summary>A chest the arena put up (on any game: what it sets on its own copy is not seen by the others).</summary>
        internal static bool IsArenaChest(Component c)
        {
            ZNetView view = c != null ? c.GetComponent<ZNetView>() : null;
            return view != null && view.IsValid() && view.GetZDO().GetInt(ChestKey, 0) != 0;
        }

        private static IEnumerator Rise(Transform t, Vector3 at)
        {
            float s = 0f;
            while (s < 1f && t != null)
            {
                s += Time.deltaTime / 1.3f;
                t.position = at + Vector3.down * (1.2f * (1f - Mathf.SmoothStep(0f, 1f, s)));
                yield return null;
            }
            if (t != null) t.position = at;
        }

        /// <summary>Where something stands on whatever is in the middle of the ring (the floor, or the cover set up there).</summary>
        private static Vector3 Floor(Vector3 p)
        {
            int mask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "terrain");
            Physics.SyncTransforms();   // (cover put up this very frame counts)
            if (Physics.Raycast(p + Vector3.up * 8f, Vector3.down, out RaycastHit hit, 16f, mask)) return hit.point + Vector3.up * 0.02f;
            return new Vector3(p.x, Site.Ground(p, p.y), p.z);
        }

        private static void Effect(string prefab, Vector3 at)
        {
            GameObject fx = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(prefab) : null;
            if (fx != null) Object.Instantiate(fx, at, Quaternion.identity);
        }

        // ---- the Armourer -----------------------------------------------------------------------------------------------------------

        private static GameObject _armourer;
        internal static bool ArmourerStanding => _armourer != null;

        /// <summary>The Armourer, by the main grate inside the ring: a smith in iron with a hammer, facing the middle.</summary>
        internal static void Armourer(bool on, Vector3? here = null)
        {
            if (!on)
            {
                if (_armourer != null) { Fx.SpawnPuff(_armourer.transform.position); Object.Destroy(_armourer); }
                _armourer = null;
                if (StoreGui.IsVisible()) StoreGui.instance.Hide();
                return;
            }
            if (_armourer != null || !Site.Known) return;
            Vector3 local = new Vector3(3.2f, 0f, -(Layout.Floor - 2.2f));
            Vector3 at = here ?? Floor(Site.World(local));   // (somewhere else only for testing)
            _armourer = new GameObject("ArenaArmourer");
            if (Scenery.Root != null) _armourer.transform.SetParent(Scenery.Root, true);   // (goes with the arena when it is taken down)
            GameObject smith = Figures.Make(_armourer.transform, at, Quaternion.LookRotation(Site.Turn * new Vector3(-local.x, 0f, -local.z)), new Figures.Look
            {
                Model = 0, Skin = new Color(0.82f, 0.62f, 0.5f), Hair = new Color(0.3f, 0.18f, 0.1f),
                HairItem = "Hair3", Beard = "Beard5", Chest = "ArmorIronChest", Legs = "ArmorIronLegs", Right = "Hammer", Utility = "BeltStrength",
            }, true);
            if (smith == null) return;
            int layer = LayerMask.NameToLayer("piece");
            foreach (Collider c in smith.GetComponentsInChildren<Collider>(true)) { c.gameObject.layer = layer >= 0 ? layer : 0; c.isTrigger = false; }
            smith.AddComponent<Armourer>();
            Fx.SpawnPuff(at);
            Effect("vfx_spawn", at);
        }

        /// <summary>The mod loading or unloading: no Armourer is left standing (one from before a reload would stay forever).</summary>
        internal static void ClearLeftovers()
        {
            Armourer(false);
            // (by name: one left by an earlier copy of the mod is of that copy's types, not this one's)
            foreach (GameObject go in Object.FindObjectsOfType<GameObject>()) if (go != null && go.name == "ArenaArmourer" && go != _armourer) Object.Destroy(go);
        }

        // ---- fireworks ----------------------------------------------------------------------------------------------------------

        private static readonly string[] Rockets = { "vfx_Firework_Rocket_Red", "vfx_Firework_Rocket_Yellow", "vfx_Firework_Rocket_Blue", "vfx_Firework_Rocket_Green", "vfx_Firework_Rocket_Purple", "vfx_Firework_Rocket_Cyan" };

        /// <summary>Rockets go up from the top of the arena's wall all round, a few at a time, for a while.</summary>
        internal static void Fireworks(int count, float over)
        {
            if (Plugin.Instance != null && Site.Known) Plugin.Instance.StartCoroutine(Launch(count, over));
        }

        private static IEnumerator Launch(int count, float over)
        {
            for (int i = 0; i < count; i++)
            {
                float a = Random.value * Mathf.PI * 2f;
                Vector3 local = new Vector3(Mathf.Sin(a) * (Layout.Facade - 1f), Layout.Top + 1f, Mathf.Cos(a) * (Layout.Facade - 1f));
                GameObject rocket = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(Rockets[Random.Range(0, Rockets.Length)]) : null;
                if (rocket != null)
                {
                    // aimed up and a little in, over the ring
                    Vector3 up = (Vector3.up * 3f - new Vector3(local.x, 0f, local.z).normalized * 0.6f).normalized;
                    Object.Instantiate(rocket, Site.World(local), Quaternion.LookRotation(Site.Turn * up));
                }
                yield return new WaitForSeconds(over / Mathf.Max(1, count) * Random.Range(0.5f, 1.5f));
            }
        }
    }

    /// <summary>
    /// The arena's chests are never torn down with the hammer, on anyone's game (the game that puts one up marks only its own copy; on the
    /// others the hammer would give back the chest's building materials).
    /// </summary>
    [HarmonyLib.HarmonyPatch(typeof(Piece), nameof(Piece.CanBeRemoved))]
    internal static class Piece_CanBeRemoved_ArenaChest
    {
        private static void Postfix(Piece __instance, ref bool __result)
        {
            if (__result && Show.IsArenaChest(__instance)) __result = false;
        }
    }
}
