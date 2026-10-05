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
            State state = Plugin.Instance?.Current;
            int running = state?.Active.Count ?? 0, mine = Plugin.Instance?.ToClaim() ?? 0;
            string line = mine > 0 ? $"<color=#9BE37A>{mine} reward{(mine == 1 ? "" : "s")} waiting for you</color>"
                : running > 0 ? $"The group has {running} contract{(running == 1 ? "" : "s")} running"
                : "New notices are posted every day";
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
