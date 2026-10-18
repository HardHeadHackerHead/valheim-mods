using System.Linq;
using TMPro;
using UnityEngine;
using static DHack.Shared.Translations;

namespace Skateboards
{
    /// <summary>
    /// The score on screen while you skate, as the old skateboarding games showed it, in the game's own fonts and colours: the combo's
    /// tricks low in the middle of the screen ("Kickflip + 50-50 + Indy"), its points times its multiplier under them, a balance meter
    /// while grinding (across) or in a manual (up and down) with its needle, the points banked rising and fading ("+4,200") or BAIL! in red,
    /// and the session's score and the best combo at the top right. It hangs under the game's HUD, so it hides with it and scales with the
    /// player's interface scale.
    /// </summary>
    internal static class ScoreHud
    {
        private const string Name = "SkateboardsScore";
        private static RectTransform _root, _meter, _needle, _vmeter, _vneedle, _special, _specialFill;
        private static TMP_Text _tricks, _score, _pop, _session, _note, _keys;
        private static RectTransform _keysBox;
        private static string _keysShown;
        private static UnityEngine.UI.Image _specialImage;
        private static string _tricksShown, _scoreShown, _sessionShown;

        public static void Tick()
        {
            Player p = Player.m_localPlayer;
            bool show = p != null && Plugin.ShowScore.Value && (Skate.Active || Time.time - Tricks.LastBankAt < 2f || Time.time - Tricks.LastBailAt < 2f);
            if (!show) { if (_root != null && _root.gameObject.activeSelf) _root.gameObject.SetActive(false); return; }
            if (_root == null && !Build()) return;
            if (!_root.gameObject.activeSelf) _root.gameObject.SetActive(true);

            string tricks = Tricks.Text();
            if (tricks != _tricksShown) { _tricks.text = tricks; _tricksShown = tricks; }
            string score = Tricks.HasCombo ? Tricks.Format((long)Mathf.Round(Tricks.Points)) + "  x  " + Tricks.Multiplier.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) : "";
            if (score != _scoreShown) { _score.text = score; _scoreShown = score; }

            // banked or bailed, rising and fading
            float sinceBank = Time.time - Tricks.LastBankAt, sinceBail = Time.time - Tricks.LastBailAt;
            if (sinceBail < 1.6f && sinceBail <= sinceBank)
            {
                _pop.text = T("skateboards_bail") + (string.IsNullOrEmpty(Tricks.LastBailText) ? "" : "\n<size=60%>" + Tricks.LastBailText + "</size>");
                _pop.color = Fade(GameUi.Warn, sinceBail, 1.6f);
                _pop.rectTransform.anchoredPosition = new Vector2(0f, 330f + sinceBail * 20f);
            }
            else if (sinceBank < 1.8f)
            {
                _pop.text = "+" + Tricks.Format(Tricks.LastBankPoints);
                _pop.color = Fade(GameUi.Good, sinceBank, 1.8f);
                _pop.rectTransform.anchoredPosition = new Vector2(0f, 330f + sinceBank * 30f);
            }
            else if (_pop.text.Length > 0) _pop.text = "";

            // a callout: Perfect!, Sloppy, a gap, the special
            float sinceNote = Time.time - Tricks.NoteAt;
            if (sinceNote < 1.3f)
            {
                if (_note.text != Tricks.Note) _note.text = Tricks.Note;
                _note.color = Fade(Tricks.NoteColor, sinceNote, 1.3f);
            }
            else if (_note.text.Length > 0) _note.text = "";

            // the special meter: gold and pulsing when full
            _specialFill.sizeDelta = new Vector2(296f * Mathf.Clamp01(Tricks.Special), 6f);
            _specialImage.color = Tricks.SpecialOn ? Color.Lerp(GameUi.Gold, Color.white, 0.25f + 0.25f * Mathf.Sin(Time.time * 8f)) : new Color(0.55f, 0.75f, 1f, 0.9f);

            // the balance meters
            bool grinding = Skate.State == Ride.Grind, manual = Skate.State == Ride.Manual;
            if (_meter.gameObject.activeSelf != grinding) _meter.gameObject.SetActive(grinding);
            if (_vmeter.gameObject.activeSelf != manual) _vmeter.gameObject.SetActive(manual);
            float b = Mathf.Clamp(Skate.Balance, -1f, 1f);
            Color needle = Color.Lerp(GameUi.Good, GameUi.Warn, Mathf.Abs(b));
            if (grinding) { _needle.anchoredPosition = new Vector2(b * 150f, 0f); _needle.GetComponent<UnityEngine.UI.Image>().color = needle; }
            if (manual) { _vneedle.anchoredPosition = new Vector2(0f, b * 75f); _vneedle.GetComponent<UnityEngine.UI.Image>().color = needle; }

            // the key guide: what the keys do now
            bool guide = Plugin.ShowKeys.Value && Skate.Active && Skate.State != Ride.Bail;
            if (_keysBox.gameObject.activeSelf != guide) _keysBox.gameObject.SetActive(guide);
            if (guide)
            {
                string keys = T("skateboards_keys_" + Skate.State.ToString().ToLowerInvariant());
                if (Tricks.SpecialOn && (Skate.State == Ride.Air || Skate.State == Ride.Roll)) keys += "\n" + T("skateboards_keys_special");
                if (keys != _keysShown)
                {
                    _keys.text = keys;
                    _keysShown = keys;
                    _keysBox.sizeDelta = new Vector2(360f, _keys.preferredHeight + 16f);
                }
            }

            string session = T("skateboards_session", Tricks.Format(Tricks.Session), Tricks.Format(Tricks.Best(p)));
            if (session != _sessionShown) { _session.text = session; _sessionShown = session; }
        }

        private static Color Fade(Color c, float since, float over)
        {
            c.a = Mathf.Clamp01(1.4f - since / over * 1.4f);
            return c;
        }

        public static void Destroy()
        {
            if (_root != null) Object.Destroy(_root.gameObject);
            _root = null;
        }

        private static bool Build()
        {
            Transform hud = GameUi.HudRoot;
            if (hud == null || !GameUi.Ready()) return false;
            foreach (Transform old in hud.Cast<Transform>().Where(t => t.name == Name).ToList()) Object.Destroy(old.gameObject);   // (a hot reload's leftover)

            _root = GameUi.Stretch(hud, Name);
            Vector2 bottom = new Vector2(0.5f, 0f);
            _tricks = GameUi.Label(_root, "", new Vector2(0f, 262f), new Vector2(1100f, 70f), 24f, GameUi.Warm, GameUi.Serif, TextAlignmentOptions.Bottom, bottom);
            _tricks.rectTransform.pivot = new Vector2(0.5f, 0f);
            Shadow(_tricks);
            _score = GameUi.Label(_root, "", new Vector2(0f, 218f), new Vector2(600f, 48f), 38f, GameUi.Gold, GameUi.Serif, TextAlignmentOptions.Bottom, bottom);
            _score.rectTransform.pivot = new Vector2(0.5f, 0f);
            _score.fontStyle = FontStyles.Bold;
            Shadow(_score);
            _pop = GameUi.Label(_root, "", new Vector2(0f, 330f), new Vector2(800f, 90f), 44f, GameUi.Good, GameUi.Norse, TextAlignmentOptions.Bottom, bottom);
            _pop.rectTransform.pivot = new Vector2(0.5f, 0f);
            Shadow(_pop);

            _note = GameUi.Label(_root, "", new Vector2(0f, 300f), new Vector2(800f, 40f), 28f, GameUi.Good, GameUi.Norse, TextAlignmentOptions.Bottom, bottom);
            _note.rectTransform.pivot = new Vector2(0.5f, 0f);
            Shadow(_note);
            // the special meter: a thin bar under the score
            _special = GameUi.Rect(_root, "Special", bottom, new Vector2(0.5f, 0.5f), new Vector2(0f, 206f), new Vector2(300f, 10f));
            GameUi.Plain(_special, new Color(0f, 0f, 0f, 0.55f));
            _specialFill = GameUi.Rect(_special, "Fill", new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(2f, 0f), new Vector2(0f, 6f));
            _specialImage = GameUi.Plain(_specialFill, GameUi.Gold);

            // across for a grind: a dark bar with a light middle, the needle on it
            _meter = GameUi.Rect(_root, "Balance", bottom, new Vector2(0.5f, 0.5f), new Vector2(0f, 360f), new Vector2(316f, 14f));
            GameUi.Plain(_meter, new Color(0f, 0f, 0f, 0.55f));
            GameUi.Plain(GameUi.Rect(_meter, "Middle", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(70f, 10f)), new Color(1f, 0.86f, 0.55f, 0.25f));
            _needle = GameUi.Rect(_meter, "Needle", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(7f, 28f));
            GameUi.Plain(_needle, GameUi.Good);
            // up and down for a manual, beside the middle of the screen
            _vmeter = GameUi.Rect(_root, "Manual", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(170f, -40f), new Vector2(14f, 166f));
            GameUi.Plain(_vmeter, new Color(0f, 0f, 0f, 0.55f));
            GameUi.Plain(GameUi.Rect(_vmeter, "Middle", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(10f, 40f)), new Color(1f, 0.86f, 0.55f, 0.25f));
            _vneedle = GameUi.Rect(_vmeter, "Needle", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(28f, 7f));
            GameUi.Plain(_vneedle, GameUi.Good);
            _meter.gameObject.SetActive(false);
            _vmeter.gameObject.SetActive(false);

            // the key guide, bottom right on a dark backing
            _keysBox = GameUi.Rect(_root, "Keys", new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-24f, 150f), new Vector2(360f, 100f));
            GameUi.Plain(_keysBox, new Color(0f, 0f, 0f, 0.62f));
            _keys = GameUi.Label(_keysBox, "", new Vector2(10f, -8f), new Vector2(340f, 200f), 17f, GameUi.Text, GameUi.Serif, TextAlignmentOptions.TopLeft);
            _keys.lineSpacing = 4f;
            _keysBox.gameObject.SetActive(false);
            _keysShown = null;

            _session = GameUi.Label(_root, "", new Vector2(-30f, -250f), new Vector2(320f, 60f), 18f, GameUi.Dim, GameUi.Serif, TextAlignmentOptions.TopRight, new Vector2(1f, 1f));
            _session.rectTransform.pivot = new Vector2(1f, 1f);
            Shadow(_session);
            _tricksShown = _scoreShown = _sessionShown = null;
            return true;
        }

        /// <summary>A dark outline, so the words read over snow and sky.</summary>
        private static void Shadow(TMP_Text t)
        {
            t.outlineWidth = 0.18f;
            t.outlineColor = new Color32(0, 0, 0, 200);
        }
    }
}
