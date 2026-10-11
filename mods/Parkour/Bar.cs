using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Parkour
{
    /// <summary>
    /// The parkour bar: a blue copy of the game's own stamina bar, just above it. It fills when a move starts and empties as it's used: a
    /// wall run's time left, a climb's height left, and between wall jumps the jumps left (in steps, the number beside it). It shows only
    /// during a move and fades out about a second after, with the game's own fade when the copy brings the stamina bar's animator.
    /// It sits beside the stamina bar in the HUD, so it scales and hides with the HUD (and with Claude Tools' "shoot hud off").
    /// </summary>
    internal static class Bar
    {
        private const string Name = "parkour_bar";
        private static readonly Color Blue = new Color(0.30f, 0.62f, 1f), PaleBlue = new Color(0.62f, 0.82f, 1f);

        private static GameObject _bar;
        private static RectTransform _rect;
        private static GuiBar _fast, _slow;
        private static TMP_Text _count;
        private static Animator _anim;        // the stamina bar's own fade, in the copy (when it's there)
        private static CanvasGroup _group;    // else ours
        private static float _shownAt = -10f, _value;

        /// <summary>For the status command: on screen now, and how full.</summary>
        public static bool Showing => _bar != null && Time.time - _shownAt < 1f;
        public static float Value => _value;

        public static void Destroy()
        {
            if (_bar != null) Object.Destroy(_bar);
            _bar = null;
            _rect = null;
            _fast = _slow = null;
            _count = null;
            _anim = null;
            _group = null;
            _shownAt = -10f;
        }

        /// <summary>Every frame, after the HUD has placed the stamina bar.</summary>
        public static void Late()
        {
            Player p = Player.m_localPlayer;
            Hud hud = Hud.instance;
            if (p == null || hud == null || hud.m_staminaBar2Root == null) return;
            float v = Plugin.Enabled.Value && Plugin.ShowBar.Value ? Moves.BarValue(p) : -1f;
            if (v >= 0f) { _shownAt = Time.time; _value = v; }
            bool show = Time.time - _shownAt < 1f;   // (stays a second after the move, fading)
            if (_bar == null && (!show || !Make(hud))) return;

            // just above the stamina bar, as wide, wherever the game has put it (higher in build mode and on a ship)
            RectTransform stamina = hud.m_staminaBar2Root;
            float width = Mathf.Max(32f, stamina.rect.width - 16f);
            _rect.anchoredPosition = stamina.anchoredPosition + new Vector2(0f, stamina.rect.height + 2f);
            _rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width + 16f);
            _fast?.SetWidth(width); _slow?.SetWidth(width);
            _fast?.SetValue(_value); _slow?.SetValue(_value);
            if (_count != null)
            {
                bool kicks = Moves.State == Move.WallRun || (Moves.State == Move.None && Moves.KicksUsed > 0);
                string text = kicks ? Moves.KicksLeft(p).ToString() : "";
                if (_count.text != text) _count.text = text;
            }
            if (_anim != null) _anim.SetBool("Visible", show);
            else if (_group != null) _group.alpha = Mathf.MoveTowards(_group.alpha, show ? 1f : 0f, Time.deltaTime * 3f);
        }

        /// <summary>The copy of the stamina bar, tinted blue (old copies from before a reload taken away first).</summary>
        private static bool Make(Hud hud)
        {
            RectTransform stamina = hud.m_staminaBar2Root;
            Transform parent = stamina.parent;
            if (parent == null) return false;
            foreach (Transform t in parent.Cast<Transform>().Where(t => t.name == Name).ToList()) Object.Destroy(t.gameObject);

            _bar = Object.Instantiate(stamina.gameObject, parent);
            _bar.name = Name;
            _bar.SetActive(true);
            _rect = (RectTransform)_bar.transform;
            GuiBar[] bars = _bar.GetComponentsInChildren<GuiBar>(true);
            _fast = bars.FirstOrDefault(b => hud.m_staminaBar2Fast != null && b.name == hud.m_staminaBar2Fast.name) ?? bars.FirstOrDefault();
            _slow = bars.FirstOrDefault(b => b != _fast && hud.m_staminaBar2Slow != null && b.name == hud.m_staminaBar2Slow.name);
            Tint(_fast, Blue);
            // the stamina bar's second, trailing bar (what was just spent) would show the last move under the new one: one bar only
            if (_slow != null) { _slow.gameObject.SetActive(false); _slow = null; }
            if (_fast != null) { _fast.m_smoothFill = false; _fast.m_smoothDrain = false; }   // (a new move: full at once, not filling up)
            _count = hud.m_staminaText != null ? _bar.GetComponentsInChildren<TMP_Text>(true).FirstOrDefault(t => t.name == hud.m_staminaText.name) : null;
            if (_count != null) _count.text = "";

            // the game's fade: its stamina animator, if it was on the bar (or inside it) and so came along in the copy
            _anim = null;
            Animator theirs = hud.m_staminaAnimator;
            if (theirs != null && (theirs.transform == stamina || theirs.transform.IsChildOf(stamina)))
            {
                string path = Path(stamina, theirs.transform);
                Transform mine = path.Length == 0 ? _bar.transform : _bar.transform.Find(path);
                _anim = mine != null ? mine.GetComponent<Animator>() : null;
                if (_anim != null && !_anim.parameters.Any(x => x.name == "Visible")) _anim = null;
            }
            _group = null;
            if (_anim == null)
            {
                _group = _bar.GetComponent<CanvasGroup>();
                if (_group == null) _group = _bar.AddComponent<CanvasGroup>();
                _group.alpha = 0f;
            }
            return _fast != null;
        }

        private static void Tint(GuiBar bar, Color color)
        {
            if (bar == null) return;
            bar.SetColor(color);
            Image image = bar.m_bar != null ? bar.m_bar.GetComponent<Image>() : null;   // (in case the bar hasn't woken up yet)
            if (image != null) image.color = color;
        }

        private static string Path(Transform root, Transform t)
        {
            string path = "";
            for (; t != null && t != root; t = t.parent) path = path.Length == 0 ? t.name : t.name + "/" + path;
            return path;
        }
    }
}
