using System.Linq;
using UnityEngine;
using static DHack.Shared.Translations;

namespace Dungeons
{
    // What a player's hover and E find on the building blocks (BlockSet.cs): each on its mechanism's pivot, asking the entrance's owner
    // (MechSet's "blk:" acts) for anything that changes the dungeon. Names are the mod's words; the look is the pack's parts.

    /// <summary>The base: the block's mechanism, and the item names a player reads.</summary>
    internal abstract class DungeonBlock : MonoBehaviour, Hoverable, Interactable
    {
        protected MechSet Set;
        protected string Key;
        protected float Next;

        public void Init(MechSet set, string key) { Set = set; Key = key; }

        protected Mech Me => Set != null && Set.ByKey.TryGetValue(Key, out Mech m) ? m : null;

        protected static string Use(string what) => $"\n[<color=yellow><b>$KEY_Use</b></color>] {what}";
        protected static string Grey(string what) => $"\n<color=#a0a0a0>{what}</color>";

        /// <summary>An item's name as the player reads it (its prefab's, translated), or the name itself.</summary>
        public static string ItemName(Pack pack, string name)
        {
            string prefab = TriggerReader.ItemPrefab(pack, name);
            GameObject go = ObjectDB.instance != null && prefab != null ? ObjectDB.instance.GetItemPrefab(prefab) : null;
            string shown = go?.GetComponent<ItemDrop>()?.m_itemData.m_shared.m_name ?? name ?? "";
            return Localization.instance != null ? Localization.instance.Localize(shown) : shown;
        }

        protected bool Ready(Humanoid user, bool hold)
        {
            if (hold || Set == null || user != Player.m_localPlayer || Time.time < Next || Me == null) return false;
            Next = Time.time + 0.5f;
            return true;
        }

        public abstract string GetHoverName();
        public abstract string GetHoverText();
        public float GetHoverOffset() => 0f;
        public virtual bool Interact(Humanoid user, bool hold, bool alt) => false;
        public virtual bool UseItem(Humanoid user, ItemDrop.ItemData item) => false;
    }

    /// <summary>A pedestal: E puts its item on (or a hotbar key one it accepts), holding E takes it off.</summary>
    internal class DungeonPedestal : DungeonBlock
    {
        public override string GetHoverName() => T("dungeons_pedestal");

        public override string GetHoverText()
        {
            Mech m = Me;
            if (m?.Def.Block == null) return "";
            Pack pack = Set.Layout.Pack;
            string on = Set.ItemOn(m), text = T("dungeons_pedestal");
            if (!string.IsNullOrEmpty(on))
                text += Grey(T("dungeons_pedestal_holds", ItemName(pack, on))) + $"\n[<color=yellow><b>$ui_hold $KEY_Use</b></color>] {T("dungeons_pedestal_take")}";
            else
            {
                ItemDrop.ItemData have = Accepted(Player.m_localPlayer, m);
                text += have != null ? Use(T("dungeons_pedestal_place", ItemName(pack, have.m_dropPrefab?.name)))
                                     : Grey(T("dungeons_pedestal_wants", ItemName(pack, m.Def.Block.Item ?? m.Def.Block.Accepts.FirstOrDefault())));
            }
            return Localization.instance.Localize(text);
        }

        /// <summary>The item the player would put on: the pedestal's own first, else any it accepts.</summary>
        private ItemDrop.ItemData Accepted(Player p, Mech m)
        {
            if (p == null) return null;
            Pack pack = Set.Layout.Pack;
            string right = TriggerReader.ItemPrefab(pack, m.Def.Block.Item);
            var ok = m.Def.Block.Accepts.Select(a => TriggerReader.ItemPrefab(pack, a)).ToList();
            var items = p.GetInventory().GetAllItems().Where(i => i?.m_dropPrefab != null && ok.Contains(i.m_dropPrefab.name)).ToList();
            return items.FirstOrDefault(i => i.m_dropPrefab.name == right) ?? items.FirstOrDefault();
        }

        public override bool Interact(Humanoid user, bool hold, bool alt)
        {
            Mech m = Me;
            if (m == null || user != Player.m_localPlayer || Time.time < Next) return false;
            if (!string.IsNullOrEmpty(Set.ItemOn(m)))
            {
                if (!hold) return false;   // (taking is a held E, as on the game's item stands)
                Next = Time.time + 1.5f;
                Set.Site.Act("blk:take:" + Key);
                return true;
            }
            if (hold) return false;
            Next = Time.time + 0.5f;
            ItemDrop.ItemData item = Accepted((Player)user, m);
            if (item == null) { user.Message(MessageHud.MessageType.Center, T("dungeons_pedestal_wants", ItemName(Set.Layout.Pack, m.Def.Block.Item ?? m.Def.Block.Accepts.FirstOrDefault()))); return false; }
            return Set.PlaceFrom((Player)user, m, item);
        }

        public override bool UseItem(Humanoid user, ItemDrop.ItemData item)
        {
            Mech m = Me;
            if (m == null || user != Player.m_localPlayer || !string.IsNullOrEmpty(Set.ItemOn(m)) || Time.time < Next) return false;
            Next = Time.time + 0.5f;
            Set.PlaceFrom((Player)user, m, item);
            return true;
        }
    }

    /// <summary>A brazier: lit by E with a torch (or anything burning) in hand, or by a fire hit.</summary>
    internal class DungeonBrazier : DungeonBlock, IDestructible
    {
        public override string GetHoverName() => T("dungeons_brazier");

        public override string GetHoverText()
        {
            Mech m = Me;
            if (m?.Def.Block == null) return "";
            string text = T("dungeons_brazier");
            if (Set.StateOf(m) >= 1) text += Grey(T("dungeons_brazier_lit"));
            else if (m.Def.Block.Lightable) text += Flame(Player.m_localPlayer) ? Use(T("dungeons_brazier_light")) : Grey(T("dungeons_brazier_needs"));
            return Localization.instance.Localize(text);
        }

        /// <summary>Something burning in the player's hands: a torch, or any item that deals fire.</summary>
        public static bool Flame(Humanoid h)
        {
            if (h == null) return false;
            foreach (ItemDrop.ItemData i in new[] { h.RightItem, h.LeftItem })
                if (i != null && (i.m_shared.m_damages.m_fire > 0f || (i.m_dropPrefab != null && i.m_dropPrefab.name.ToLowerInvariant().Contains("torch")))) return true;
            return false;
        }

        public override bool Interact(Humanoid user, bool hold, bool alt)
        {
            if (!Ready(user, hold)) return false;
            Mech m = Me;
            if (!m.Def.Block.Lightable || Set.StateOf(m) >= 1) return false;
            if (!Flame(user)) { user.Message(MessageHud.MessageType.Center, T("dungeons_brazier_needs")); return false; }
            Set.Site.Act("blk:light:" + Key);
            return true;
        }

        public DestructibleType GetDestructibleType() => DestructibleType.Default;

        public void Damage(HitData hit)
        {
            Mech m = Me;
            if (m?.Def.Block == null || hit == null || hit.m_damage.m_fire <= 0f || !m.Def.Block.Lightable || Time.time < Next) return;
            if (hit.GetAttacker() != Player.m_localPlayer || Set.StateOf(m) >= 1) return;   // (the attacker's game asks, once)
            Next = Time.time + 0.5f;
            Set.Site.Act("blk:light:" + Key);
        }
    }

    /// <summary>A lock: E with its key opens it (the key is taken once the owner says it's open).</summary>
    internal class DungeonLock : DungeonBlock
    {
        public override string GetHoverName() => T("dungeons_lock");

        public override string GetHoverText()
        {
            Mech m = Me;
            if (m?.Def.Block == null || Set.StateOf(m) >= 1) return "";
            string key = ItemName(Set.Layout.Pack, m.Def.Block.Key);
            bool has = BlockKeys.Has(Player.m_localPlayer, TriggerReader.ItemPrefab(Set.Layout.Pack, m.Def.Block.Key), Set.Site);
            return Localization.instance.Localize(T("dungeons_lock") + (has ? Use(T("dungeons_lock_open", key)) : Grey(T("dungeons_lock_needs", key))));
        }

        public override bool Interact(Humanoid user, bool hold, bool alt)
        {
            if (!Ready(user, hold)) return false;
            Mech m = Me;
            if (Set.StateOf(m) >= 1) return false;
            string key = TriggerReader.ItemPrefab(Set.Layout.Pack, m.Def.Block.Key);
            if (!BlockKeys.Has((Player)user, key, Set.Site))
            {
                user.Message(MessageHud.MessageType.Center, T("dungeons_lock_needs", ItemName(Set.Layout.Pack, m.Def.Block.Key)));
                return false;
            }
            Next = Time.time + 2f;
            Set.Unlock(m);
            return true;
        }
    }

    /// <summary>A teleporter: E (or stepping on it, BlockSet) takes the player to its pair's other end.</summary>
    internal class DungeonTeleporter : DungeonBlock
    {
        public override string GetHoverName() => T("dungeons_teleporter");

        public override string GetHoverText()
        {
            Mech m = Me;
            if (m == null) return "";
            string text = T("dungeons_teleporter");
            if (m.Def.Block != null && !m.Def.Block.Send) text += Grey(T("dungeons_teleporter_arrival"));
            else if (Set.CanSend(m, out _)) text += m.Def.Block != null && m.Def.Block.Step ? Grey(T("dungeons_teleporter_step")) : Use(T("dungeons_teleporter_go"));
            else text += Grey(T("dungeons_teleporter_off"));
            return Localization.instance.Localize(text);
        }

        public override bool Interact(Humanoid user, bool hold, bool alt)
        {
            if (!Ready(user, hold)) return false;
            Mech m = Me;
            if (!Set.CanSend(m, out Mech to)) { user.Message(MessageHud.MessageType.Center, T("dungeons_teleporter_off")); return false; }
            Next = Time.time + 2.5f;
            Set.Teleport((Player)user, m, to);
            return true;
        }
    }

    /// <summary>A checkpoint: E makes it this player's (Checkpoints.cs).</summary>
    internal class DungeonCheckpoint : DungeonBlock
    {
        public override string GetHoverName() => T("dungeons_checkpoint");

        public override string GetHoverText()
        {
            Mech m = Me;
            if (m == null) return "";
            return Localization.instance.Localize(T("dungeons_checkpoint") + (Checkpoints.IsMine(Set.Site, m) ? Grey(T("dungeons_checkpoint_yours")) : Use(T("dungeons_checkpoint_set"))));
        }

        public override bool Interact(Humanoid user, bool hold, bool alt)
        {
            if (!Ready(user, hold)) return false;
            Mech m = Me;
            if (Checkpoints.IsMine(Set.Site, m)) return false;
            Checkpoints.Set(Set.Site, m, (Player)user);
            foreach (string fx in m.Def.Block?.Fx ?? new System.Collections.Generic.List<string>()) MechSet.PlayAt(fx, m.WorldPos);
            return true;
        }
    }

    /// <summary>A pushblock: what it is and how it moves (walking into it: BlockSet.Push).</summary>
    internal class DungeonPushblock : DungeonBlock
    {
        public override string GetHoverName() => T("dungeons_pushblock");

        public override string GetHoverText() => Localization.instance.Localize(T("dungeons_pushblock") + Grey(T("dungeons_pushblock_hint")));
    }
}
