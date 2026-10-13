using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace Terminal
{
    /// <summary>
    /// The Proxy's things: what it carries (kept in its saved data, so whichever game owns it next has the same bag), loot it walks over
    /// (picked up while someone is driving), using, eating and dropping what it carries (the driver's number keys), the tombstone it
    /// leaves when it dies with loot, and E on a door or a plant.
    ///
    /// The bag lives in memory on the game that owns the Proxy, and as base64 in the Proxy's ZDO (one small string, written only when it
    /// changes: pitfall "keep a moving object's ZDO small"; the bag is 5x3, about 1 KB at most). The starting gear is tagged in its
    /// custom data, so a death leaves the loot and not a free set of gear.
    /// </summary>
    internal partial class ProxyBody
    {
        internal const string KeyBag = "dh_tm_bag", KeyGeared = "dh_tm_geared";
        private const string StartTag = "dh_tm_start";
        private static readonly string[] StartingGear = { "ArmorRagsChest", "ArmorRagsLegs", "Club" };
        private static readonly System.Reflection.FieldInfo Blocking = AccessTools.Field(typeof(Humanoid), "m_blocking");
        private static readonly Collider[] Near = new Collider[24];

        private bool _bagReady, _bagDirty;
        private float _nextSave, _nextPickup, _dropLock;
        private string _lastSaved = "";

        private void MarkDirty() => _bagDirty = true;

        /// <summary>Every tick on the owner: take the bag over, save it when it changed, pick up what lies near.</summary>
        private void BagTick()
        {
            if (Body == null) return;
            if (!IsOwner) { if (_bagReady) { _bagReady = false; Body.GetInventory().m_onChanged -= MarkDirty; } return; }
            if (!_bagReady) LoadBag();
            if (!_bagReady) return;
            if (_bagDirty || Time.time >= _nextSave) SaveBag();
            if (Time.time >= _nextPickup)
            {
                _nextPickup = Time.time + 0.3f;
                if (Driven && Time.time >= _dropLock) PickUp();
            }
        }

        private void LoadBag()
        {
            if (ObjectDB.instance == null) return;
            ZDO zdo = View.GetZDO();
            Inventory bag = Body.GetInventory();
            Body.UnequipAllItems();
            string text = zdo.GetString(KeyBag, "");
            bool loaded = false;
            if (text.Length > 0)
            {
                try
                {
                    bag.Load(new ZPackage(text));
                    foreach (ItemDrop.ItemData item in bag.GetAllItems().ToList())
                        if (item.m_equipped) { item.m_equipped = false; Body.EquipItem(item, false); }   // (the flag says "was worn": wear it again)
                    loaded = true;
                }
                catch (System.Exception e) { Plugin.Log.LogWarning("The Proxy's bag could not be read, starting again: " + e.Message); }
            }
            if (!loaded && zdo.GetInt(KeyGeared, 0) == 0)
            {
                zdo.Set(KeyGeared, 1);
                foreach (string name in StartingGear)
                {
                    if (ObjectDB.instance.GetItemPrefab(name) == null) { Plugin.Log.LogWarning("Starting gear not found: " + name); continue; }
                    ItemDrop.ItemData item = bag.AddItem(name, 1, 1, 0, 0L, "", false);
                    if (item == null) continue;
                    item.m_customData[StartTag] = "1";
                    Body.EquipItem(item, false);
                }
            }
            bag.m_onChanged -= MarkDirty;
            bag.m_onChanged += MarkDirty;
            _bagReady = true;
            _bagDirty = true;
        }

        private void SaveBag()
        {
            _bagDirty = false;
            _nextSave = Time.time + 10f;   // (wear changes items without telling the bag: a slow save catches it)
            var pkg = new ZPackage();
            Body.GetInventory().Save(pkg);
            string text = pkg.GetBase64();
            if (text == _lastSaved) return;
            _lastSaved = text;
            View.GetZDO().Set(KeyBag, text);
        }

        /// <summary>
        /// Put the Proxy somewhere (the tether bringing it home, a test): on its Rigidbody too, or physics puts it back. Owner only.
        /// </summary>
        internal void PlaceAt(Vector3 at)
        {
            if (!IsOwner) return;
            Rigidbody body = GetComponent<Rigidbody>();
            if (body != null) { body.position = at; body.linearVelocity = Vector3.zero; body.angularVelocity = Vector3.zero; }
            transform.position = at;
            View.GetZDO().SetPosition(at);
        }

        // ---- for the "terminal" test command ----

        internal int BagBytes => View != null && View.GetZDO() != null ? View.GetZDO().GetString(KeyBag, "").Length : 0;
        internal void ForceReload() { _bagReady = false; }   // (take the bag from the saved data again, as a game that has just taken the Proxy over does)

        internal bool Give(string prefab, int amount)
        {
            if (!IsOwner || Body == null) return false;
            return Body.GetInventory().AddItem(prefab, amount, 1, 0, 0L, "", false) != null;
        }

        // ---- loot ----

        private void PickUp()
        {
            int n = Physics.OverlapSphereNonAlloc(transform.position + Vector3.up, 2.2f, Near, LayerMask.GetMask("item"));
            for (int i = 0; i < n; i++)
            {
                ItemDrop drop = Near[i].GetComponentInParent<ItemDrop>();
                if (drop != null) Body.Pickup(drop.gameObject, false, true);
            }
        }

        // ---- the driver's number keys ----

        /// <summary>What the number keys mean, in order: the bag's items as the game lists them in grid order.</summary>
        internal List<ItemDrop.ItemData> Slots() => Body.GetInventory().GetAllItemsInGridOrder().Take(8).ToList();

        /// <summary>Key 1-8: wear or wield it, eat it, drink it. With Shift: drop it.</summary>
        internal void UseSlot(int number, bool drop)
        {
            if (!IsOwner || Body == null || Body.IsDead()) return;
            List<ItemDrop.ItemData> slots = Slots();
            if (number < 1 || number > slots.Count) return;
            ItemDrop.ItemData item = slots[number - 1];
            if (drop)
            {
                if (Body.DropItem(Body.GetInventory(), item, item.m_stack)) _dropLock = Time.time + 3f;   // (not picked up again at once)
                return;
            }
            if (item.m_shared.m_food > 0f && item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Consumable)
            {
                Eat(item);
                return;
            }
            Body.UseItem(null, item, false);
        }

        /// <summary>
        /// Food: the game's own eating is the Player's, so the Proxy gets its health back at once, as much as the food's worth, and the
        /// food is used up. (Drinks and other consumables go through the game's own UseItem.)
        /// </summary>
        private void Eat(ItemDrop.ItemData item)
        {
            if (Body.GetHealth() >= Body.GetMaxHealth()) return;
            Body.Heal(item.m_shared.m_food, true);
            Body.GetInventory().RemoveOneItem(item);
            Body.m_consumeItemEffects?.Create(transform.position, Quaternion.identity);
        }

        // ---- E on something ----

        private static readonly int InteractMask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "piece_nonsolid", "item", "vehicle");

        /// <summary>
        /// What E would work: a door, a plant, a switch or a hive within reach in front of the Proxy (null: nothing, so E stands up). Only
        /// things that don't need a player: chests, stations, beds and portals open windows for the local player, and are left out.
        /// </summary>
        internal Interactable FindInteractable(out string hover)
        {
            hover = "";
            if (Body == null) return null;
            // like the game's own: a ray from the camera through the middle of the picture, and the thing it meets must be within reach of the body
            Terminal.View.Pose(this, out Vector3 from, out Vector3 dir, out float away);
            Vector3 body = Body.m_eye != null ? Body.m_eye.position : transform.position + Vector3.up * 1.6f;
            RaycastHit[] hits = Physics.RaycastAll(from, dir, away + 4f, InteractMask, QueryTriggerInteraction.Ignore);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (RaycastHit hit in hits)
            {
                if (hit.collider.transform.IsChildOf(transform)) continue;
                if (Vector3.Distance(body, hit.point) > 3.5f) return null;
                Interactable it = hit.collider.GetComponentInParent<Interactable>();
                if (it == null) return null;   // (something solid in the way: nothing to use)
                if (!(it is Door || it is Pickable || it is Switch || it is Beehive)) return null;
                hover = (it as Hoverable)?.GetHoverText() ?? "";
                return it;
            }
            return null;
        }

        /// <summary>What the interact ray meets, nearest first (the "terminal interact" test command).</summary>
        internal string DescribeRay()
        {
            Terminal.View.Pose(this, out Vector3 from, out Vector3 dir, out float away);
            RaycastHit[] hits = Physics.RaycastAll(from, dir, away + 8f, ~0, QueryTriggerInteraction.Collide);   // (every layer: to see which one a thing is on)
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            Aim(out float yaw, out float pitch);
            return $"from {from} dir {dir} yaw {yaw:0} pitch {pitch:0} fed {Time.time - _fed:0.0}s ago: " + string.Join("; ", hits.Take(6).Select(h => h.collider.name + "/" + LayerMask.LayerToName(h.collider.gameObject.layer) + " " + h.distance.ToString("0.0")).ToArray());
        }

        internal bool TryInteract()
        {
            if (!IsOwner || Body == null || Body.IsDead()) return false;
            Interactable it = FindInteractable(out _);
            if (it == null) return false;
            try { it.Interact(Body, false, false); }
            catch (System.Exception e) { Plugin.Log.LogWarning("The Proxy could not use that: " + e.Message); }
            return true;
        }

        // ---- death ----

        /// <summary>
        /// What it carried goes into a tombstone (the game's own, filled the way the game fills a player's: MoveInventoryToGrave resizes
        /// the grave to the bag, the pitfall in docs/modding-pitfalls.md). The starting gear doesn't: it comes back with the next Proxy.
        /// </summary>
        private void Tombstone_()
        {
            if (Body == null || ProxyPrefab.Tombstone == null) return;
            try
            {
                Inventory bag = Body.GetInventory();
                bag.m_onChanged -= MarkDirty;
                foreach (ItemDrop.ItemData item in bag.GetAllItems().ToList())
                    if (item.m_customData.ContainsKey(StartTag)) { Body.UnequipItem(item, false); bag.RemoveItem(item); }
                if (bag.NrOfItems() == 0) return;
                Body.UnequipAllItems();
                GameObject grave = Instantiate(ProxyPrefab.Tombstone, Body.GetCenterPoint(), transform.rotation);
                grave.GetComponent<Container>().GetInventory().MoveInventoryToGrave(bag);
                string who = Driver.Length > 0 ? Driver : Localization.instance.Localize("$terminal_proxy");
                grave.GetComponent<TombStone>().Setup(who, 0L);
                Plugin.Log.LogInfo("A Proxy left a tombstone");
            }
            catch (System.Exception e) { Plugin.Log.LogWarning("Proxy tombstone: " + e); }
        }
    }
}
