using UnityEngine;

namespace BountyBoard
{
    /// <summary>The board itself: look at it and press the use key to read the notices.</summary>
    public class BountyBoardStation : MonoBehaviour, Interactable, Hoverable
    {
        private ZNetView _nview;

        private void Awake() => _nview = GetComponent<ZNetView>();

        /// <summary>The board's identity: its notices are worked out from this and the day, so every player sees the same ones.</summary>
        internal string Key
        {
            get
            {
                ZDO zdo = _nview != null && _nview.IsValid() ? _nview.GetZDO() : null;
                return zdo != null ? zdo.m_uid.UserID + ":" + zdo.m_uid.ID : "board";
            }
        }

        public bool Interact(Humanoid user, bool hold, bool alt)
        {
            if (hold || user != Player.m_localPlayer) return false;
            Window.Open(this);
            return true;
        }

        public bool UseItem(Humanoid user, ItemDrop.ItemData item) => false;

        public string GetHoverText()
        {
            int ready = 0, active = 0;
            Player me = Player.m_localPlayer;
            if (me != null)
                foreach (Bounty b in Bounties.Active()) { active++; if (Bounties.IsComplete(b, me)) ready++; }
            string line = ready > 0 ? $"<color=#9BE37A>{ready} contract{(ready == 1 ? "" : "s")} ready to hand in</color>" : active > 0 ? $"{active} contract{(active == 1 ? "" : "s")} in progress" : "New notices are posted every day";
            return Localization.instance.Localize($"Bounty Board\n[<color=yellow><b>$KEY_Use</b></color>] Read the notices\n<size=14>{line}</size>");
        }

        public string GetHoverName() => "Bounty Board";
        public float GetHoverOffset() => 0f;

        /// <summary>The game's crafting sounds, played when you take a job or get paid.</summary>
        internal static void PlayEffects(Vector3 at, bool done)
        {
            try
            {
                GameObject go = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab("piece_workbench") : null;
                CraftingStation station = go != null ? go.GetComponent<CraftingStation>() : null;
                if (station != null) (done ? station.m_craftItemDoneEffects : station.m_craftItemEffects).Create(at, Quaternion.identity);
            }
            catch { /* effects are optional */ }
        }
    }
}
