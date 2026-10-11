using UnityEngine;

namespace JetSkis
{
    /// <summary>
    /// The cowl in front of the handlebars: look at it and press E to paint the jet ski its next colour. (Not Shift+E on the helm: Wayfinder
    /// names boats with that, and the game runs every mod's handler, so one key press did both.) On its own hit box on the cowl; its jet
    /// ski is read when it wakes, so a hot reload's copy works at once.
    /// </summary>
    internal class PaintSpot : MonoBehaviour, Hoverable, Interactable
    {
        private JetSki _ski;

        private JetSki Ski => _ski != null ? _ski : (_ski = GetComponentInParent<JetSki>());

        public string GetHoverText()
        {
            JetSki ski = Ski;
            if (ski == null) return "";
            int paint = ((ski.PaintIndex % Effects.Paints.Length) + Effects.Paints.Length) % Effects.Paints.Length;
            return Localization.instance.Localize("$jetskis_jetski\n[<color=yellow><b>$KEY_Use</b></color>] $jetskis_paint ($" + Effects.PaintKeys[paint] + ")");
        }

        public string GetHoverName() => Localization.instance.Localize("$jetskis_jetski");

        public bool Interact(Humanoid user, bool hold, bool alt)
        {
            if (hold || alt || !(user is Player player) || player != Player.m_localPlayer || Ski == null) return false;
            Ski.NextPaint(player);
            return true;
        }

        public bool UseItem(Humanoid user, ItemDrop.ItemData item) => false;

        public float GetHoverOffset() => 0f;
    }
}
