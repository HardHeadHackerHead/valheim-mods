using System.Collections.Generic;
using UnityEngine;

namespace Recycler
{
    /// <summary>The Recycler itself: look at it and press the use key to open the menu.</summary>
    public class RecyclerStation : MonoBehaviour, Interactable, Hoverable
    {
        private const string WorkKey = "rc_work";
        private ZNetView _nview;

        private void Awake() => _nview = GetComponent<ZNetView>();

        /// <summary>True for a few seconds after something was recycled here, on every player's screen.</summary>
        public bool Working
        {
            get
            {
                ZDO zdo = _nview != null && _nview.IsValid() ? _nview.GetZDO() : null;
                return zdo != null && ZNet.instance != null && zdo.GetLong(WorkKey, 0L) + 3000L > (long)(ZNet.instance.GetTimeSeconds() * 1000.0);
            }
        }

        internal void MarkWorking()
        {
            if (_nview == null || !_nview.IsValid() || ZNet.instance == null) return;
            _nview.ClaimOwnership();
            _nview.GetZDO().Set(WorkKey, (long)(ZNet.instance.GetTimeSeconds() * 1000.0));
        }

        public bool Interact(Humanoid user, bool hold, bool alt)
        {
            if (hold || user != Player.m_localPlayer) return false;
            Window.Open(this);
            return true;
        }

        public bool UseItem(Humanoid user, ItemDrop.ItemData item) => false;

        public string GetHoverText() => Localization.instance.Localize(
            $"Recycler\n[<color=yellow><b>$KEY_Use</b></color>] Recycle gear\n<size=14>Returns {Calc.Percent(this)}% of the materials (presses: {RecyclerPress.CountNear(transform.position)}/2)</size>");

        public string GetHoverName() => "Recycler";
        public float GetHoverOffset() => 0f;

        /// <summary>The game's crafting sounds and sparks, played when something is recycled.</summary>
        internal static void PlayEffects(Vector3 at)
        {
            Play("piece_workbench", false, at);
            Play("forge", true, at);
        }

        private static void Play(string prefab, bool done, Vector3 at)
        {
            try
            {
                GameObject go = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(prefab) : null;
                CraftingStation station = go != null ? go.GetComponent<CraftingStation>() : null;
                if (station != null) (done ? station.m_craftItemDoneEffects : station.m_craftItemEffects).Create(at, Quaternion.identity);
            }
            catch { /* effects are optional */ }
        }
    }

    /// <summary>The Press: a Recycler with a Press within 8 m returns a larger share (up to two count).</summary>
    public class RecyclerPress : MonoBehaviour, Hoverable
    {
        private static readonly List<RecyclerPress> All = new List<RecyclerPress>();
        private void OnEnable() => All.Add(this);
        private void OnDisable() => All.Remove(this);

        internal static int CountNear(Vector3 position)
        {
            int n = 0;
            foreach (RecyclerPress p in All)
                if (p != null && (p.transform.position - position).sqrMagnitude <= 8f * 8f) n++;
            return Mathf.Min(n, 2);
        }

        public string GetHoverText() => Localization.instance.Localize("Recycler Press\n<size=14>Raises the share returned by a Recycler within 8 m</size>");
        public string GetHoverName() => "Recycler Press";
        public float GetHoverOffset() => 0f;
    }
}
