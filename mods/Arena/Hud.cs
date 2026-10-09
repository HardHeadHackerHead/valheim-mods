using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Arena
{
    /// <summary>
    /// What is shown over the game in the ring, all with the game's own pieces: the announcer's big calls use the biome title (the large
    /// Norse lettering you see on entering a new land) and smaller calls the centre message; the countdown is a centre message; and at the
    /// top of the screen a boss bar (the one the game shows for Eikthyr and the rest) holds the crowd's mood, with the round, the foes left,
    /// the clock and your purse under it. In a duel the bar is your opponent's health.
    /// </summary>
    internal static class Hud
    {
        private static GameObject _bar;
        private static GuiBar _fast, _slow;
        private static TMP_Text _name, _info, _clock;
        private static float _lastBig;

        /// <summary>A small line (a rule, a warning), top left as the game shows them.</summary>
        internal static void Say(string text) => MessageHud.instance?.ShowMessage(MessageHud.MessageType.TopLeft, text);

        /// <summary>
        /// The announcer. A big call (a round beginning, a champion, a win) is the game's biome title, its second line a centre message;
        /// the quick calls (a double kill, a parry) are a centre message alone, so they never queue up behind each other.
        /// </summary>
        internal static void Call(string text, string sub, bool big)
        {
            MessageHud m = MessageHud.instance;
            if (m == null) return;
            if (!Plugin.Announcer.Value) { Say(text + (string.IsNullOrEmpty(sub) ? "" : ": " + sub)); return; }
            if (big && Time.time - _lastBig > 2.5f)
            {
                _lastBig = Time.time;
                m.ShowBiomeFoundMsg(text, false);
                if (!string.IsNullOrEmpty(sub)) m.ShowMessage(MessageHud.MessageType.Center, sub);
            }
            else m.ShowMessage(MessageHud.MessageType.Center, string.IsNullOrEmpty(sub) ? text : text + "\n<size=70%>" + sub + "</size>");
        }

        /// <summary>As the contest and the network call it: long calls are big ones.</summary>
        internal static void Shout(string text, string sub = "", float seconds = 4f) => Call(text, sub, seconds >= 3.5f);

        /// <summary>The countdown number, in the middle of the screen.</summary>
        internal static void Count(string text) => MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center, "<size=150%>" + text + "</size>");

        internal static void Clear()
        {
            if (_bar != null) Object.Destroy(_bar);
            _bar = null;
        }

        // ---- the bar at the top --------------------------------------------------------------------------------------------------

        private static bool MakeBar()
        {
            if (_bar != null) return true;
            EnemyHud hud = EnemyHud.instance;
            if (hud == null || hud.m_baseHudBoss == null) return false;
            _bar = Object.Instantiate(hud.m_baseHudBoss, hud.m_hudRoot.transform);
            _bar.name = "ArenaBar";
            _bar.SetActive(true);
            _fast = _bar.transform.Find("Health/health_fast")?.GetComponent<GuiBar>();
            _slow = _bar.transform.Find("Health/health_slow")?.GetComponent<GuiBar>();
            Transform friendly = _bar.transform.Find("Health/health_fast_friendly");
            if (friendly != null) friendly.gameObject.SetActive(false);
            foreach (string part in new[] { "level_2", "level_3", "Alerted", "Aware" }) { Transform t = _bar.transform.Find(part); if (t != null) t.gameObject.SetActive(false); }
            _name = _bar.transform.Find("Name")?.GetComponent<TMP_Text>();
            // a second line under the bar, in the same lettering, smaller
            if (_name != null)
            {
                GameObject line = Object.Instantiate(_name.gameObject, _name.transform.parent);
                line.name = "ArenaInfo";
                _info = line.GetComponent<TMP_Text>();
                var r = (RectTransform)line.transform;
                Transform health = _bar.transform.Find("Health");
                float below = health != null ? ((RectTransform)health).anchoredPosition.y - ((RectTransform)health).sizeDelta.y - 6f : -60f;
                r.anchoredPosition = new Vector2(((RectTransform)_name.transform).anchoredPosition.x, below);
                r.sizeDelta = new Vector2(Mathf.Max(700f, r.sizeDelta.x), 30f);
                _info.fontSize = 18f;
                _info.enableWordWrapping = false;
                _info.overflowMode = TextOverflowModes.Overflow;
                _info.alignment = TextAlignmentOptions.Top;
                // and under that the clock between the fights, big
                GameObject big = Object.Instantiate(line, line.transform.parent);
                big.name = "ArenaClock";
                _clock = big.GetComponent<TMP_Text>();
                var br = (RectTransform)big.transform;
                br.anchoredPosition = r.anchoredPosition + new Vector2(0f, -34f);
                br.sizeDelta = new Vector2(1100f, 70f);
                _clock.fontSize = 22f;
                _clock.enableWordWrapping = true;
                _clock.text = "";
            }
            return true;
        }

        /// <summary>Every frame: shows the bar during a contest or a duel, and keeps it up to date.</summary>
        internal static void Tick()
        {
            bool contest = Contest.Active, duel = Duel.Active;
            if (!contest && !duel) { if (_bar != null) _bar.SetActive(false); return; }
            if (!MakeBar()) return;
            if (!_bar.activeSelf) _bar.SetActive(true);
            string key = Plugin.YieldKey.Value.ToString();
            if (contest)
            {
                float f = Crowd.Favour / 100f;
                _fast?.SetValue(f); _slow?.SetValue(f);
                Color c = Color.Lerp(new Color(0.75f, 0.25f, 0.15f), new Color(1f, 0.8f, 0.3f), f);
                _fast?.SetColor(c);
                if (_name != null) _name.text = "The Crowd: " + Crowd.Mood;
                string state = Contest.Phase == Contest.PhaseKind.Ready ? "Walk through the gate into the ring"
                             : Contest.Phase == Contest.PhaseKind.Countdown ? "Get ready"
                             : Contest.Phase == Contest.PhaseKind.Arming ? "Arm yourself"
                             : Contest.Phase == Contest.PhaseKind.Victory ? "<color=#ffd27a>VICTORY!</color>"
                             : Contest.Phase == Contest.PhaseKind.Break ? $"Catch your breath  ·  {key} twice: take your purse and leave"
                             : Contest.FoesLeft + (Contest.FoesLeft == 1 ? " foe left" : " foes left");
                string round = Contest.Endless ? "Wave " + Mathf.Max(1, Contest.Round)
                             : Contest.Kind == Contest.KindOf.Road ? $"{Roster.TierNames[Contest.Tier]}, round {Contest.LandRound} of 3"
                             : Contest.Rounds > 1 ? $"Round {Mathf.Max(1, Contest.Round)} of {Contest.Rounds}" : "Champion Bout";
                string clock = Rules.Timed && Contest.Fighting ? $"  ·  <color={(Contest.RoundLeft < 10f ? "#ff7a5a" : "#ffffff")}>{Mathf.CeilToInt(Mathf.Max(0f, Contest.RoundLeft))}s</color>" : "";
                if (_info != null) _info.text = $"{round}  ·  {state}{clock}  ·  <color=#ffd27a>Purse: {Contest.PurseText()}</color>";
                if (_clock != null)
                {
                    string what = Contest.TimerLabel(out float left);
                    if (left < 0f) _clock.text = "";
                    else
                    {
                        int s = Mathf.CeilToInt(Mathf.Max(0f, left));
                        string colour = left <= 10f ? "#ff7a5a" : "#ffd27a";
                        _clock.text = $"<size=200%><b><color={colour}>{s / 60}:{s % 60:00}</color></b></size>\n{what}";
                    }
                }
            }
            else
            {
                Player them = Duel.Opponent, me = Player.m_localPlayer;
                float f = them != null && them.GetMaxHealth() > 0f ? Mathf.Clamp01(them.GetHealth() / them.GetMaxHealth()) : 0f;
                _fast?.SetValue(f); _slow?.SetValue(f);
                _fast?.SetColor(new Color(0.8f, 0.2f, 0.15f));
                if (_name != null) _name.text = Duel.OpponentName;
                if (_clock != null) _clock.text = "";
                float mine = me != null && me.GetMaxHealth() > 0f ? me.GetHealth() / me.GetMaxHealth() : 0f;
                if (_info != null) _info.text = $"Duel  ·  you {Mathf.RoundToInt(mine * 100f)}%  ·  the first to a fifth loses" + (Duel.Wager > 0 ? $"  ·  <color=#ffd27a>{Duel.Wager * 2 - Duel.Wager * 2 / 10} coins to the winner</color>" : "");
            }
        }

        internal static void Draw() { }   // (drawn by the game's interface now)
    }
}
