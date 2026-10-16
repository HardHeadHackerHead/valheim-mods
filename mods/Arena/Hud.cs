using System.Collections.Generic;
using System.Linq;
using System.Text;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static DHack.Shared.Translations;

namespace Arena
{
    /// <summary>
    /// What is shown over the game in the ring. A panel of our own at the top of the screen, in the inventory's sunken look: the contest,
    /// the round, the crowd's favour as a bar, the foes left, a champion's health, what you would walk out with now as the game's item icons
    /// (a gold "+n" rising from whatever grows), what a win adds, and the announcer's last word; between rounds a second panel under it with
    /// the clock, what comes next and the keys to start now or leave. In a duel it holds your opponent's health; watching someone else's
    /// fight, theirs. The announcer's big calls stay the game's biome title; its other words go in the panel, so the middle of the screen
    /// stays clear. No line is longer than the screen takes (Wrap; the title's lettering shrinks to fit).
    /// </summary>
    internal static class Hud
    {
        private static float _lastBig;

        // (how many letters a line holds: the top-left messages are narrow, the middle of the screen wider; a CJK letter counts as two)
        private const int TopLeftWidth = 52, CentreWidth = 58, TitleWidth = 24;

        /// <summary>A small line (a rule, a warning), top left as the game shows them.</summary>
        internal static void Say(string text) => MessageHud.instance?.ShowMessage(MessageHud.MessageType.TopLeft, Wrap(text, TopLeftWidth));

        /// <summary>
        /// The text broken into lines of at most this many letters (at spaces; between any two letters of a language written without them),
        /// its rich-text tags not counted.
        /// </summary>
        internal static string Wrap(string text, int width)
        {
            if (string.IsNullOrEmpty(text)) return text;
            var sb = new StringBuilder(text.Length + 8);
            string[] lines = text.Split('\n');
            for (int li = 0; li < lines.Length; li++)
            {
                if (li > 0) sb.Append('\n');
                int used = 0, space = -1;
                bool tag = false;
                foreach (char c in lines[li])
                {
                    if (c == '<') tag = true;
                    if (tag) { sb.Append(c); if (c == '>') tag = false; continue; }
                    int w = Wide(c) ? 2 : 1;
                    if (c == ' ') space = sb.Length;
                    if (used + w > width && used > 0)
                    {
                        if (c == ' ') { sb.Append('\n'); used = 0; space = -1; continue; }
                        if (space >= 0 && !Wide(c)) { sb[space] = '\n'; used = Visible(sb, space + 1); space = -1; }
                        else { sb.Append('\n'); used = 0; space = -1; }
                    }
                    sb.Append(c);
                    used += w;
                }
            }
            return sb.ToString();
        }

        private static bool Wide(char c) => c >= '\u2E80' && c <= '\u9FFF' || c >= '\uAC00' && c <= '\uD7AF' || c >= '\uFF00' && c <= '\uFFEF';

        private static int Visible(StringBuilder sb, int from)
        {
            int n = 0;
            bool tag = false;
            for (int i = from; i < sb.Length; i++)
            {
                char c = sb[i];
                if (c == '<') tag = true;
                if (tag) { if (c == '>') tag = false; continue; }
                n += Wide(c) ? 2 : 1;
            }
            return n;
        }

        private static int Letters(string text) => Visible(new StringBuilder(text ?? ""), 0);

        // the big calls this mod has put up, so the biome title's lettering is fitted only for those (MessageHud_UpdateBiomeFound_Fit)
        private static readonly HashSet<string> Titles = new HashSet<string>();
        internal static bool IsOurs(string text) => text != null && Titles.Remove(text);

        /// <summary>
        /// The announcer. A big call (a round beginning, a champion, a win) is the game's biome title, its second line a centre message;
        /// the quick calls (a double kill, a parry) are a centre message alone, so they never queue up behind each other.
        /// </summary>
        internal static void Call(string text, string sub, bool big)
        {
            MessageHud m = MessageHud.instance;
            if (m == null) return;
            if (!Plugin.Announcer.Value) { if (!Ticker(text, sub)) Say(string.IsNullOrEmpty(sub) ? text : T("arena_call_and_sub", text, sub)); return; }
            if (big && Time.time - _lastBig > 2.5f && Letters(text) <= TitleWidth && !text.Contains("\n"))
            {
                _lastBig = Time.time;
                if (Titles.Count > 8) Titles.Clear();
                Titles.Add(text);
                m.ShowBiomeFoundMsg(text, false);
                // (the line under it in the panel, where there is one: the middle of the screen stays clear)
                if (!string.IsNullOrEmpty(sub) && !Ticker("", sub)) m.ShowMessage(MessageHud.MessageType.Center, Wrap(sub, CentreWidth));
                return;
            }
            // (the quick calls, and big ones too long for the title's lettering, in the panel's last line)
            if (Ticker(text, sub)) return;
            if (big) m.ShowMessage(MessageHud.MessageType.Center, "<size=130%>" + Wrap(text, CentreWidth * 10 / 13) + "</size>" + (string.IsNullOrEmpty(sub) ? "" : "\n<size=80%>" + Wrap(sub, CentreWidth * 10 / 8) + "</size>"));
            else m.ShowMessage(MessageHud.MessageType.Center, Wrap(text, CentreWidth) + (string.IsNullOrEmpty(sub) ? "" : "\n<size=70%>" + Wrap(sub, CentreWidth * 10 / 7) + "</size>"));
        }

        /// <summary>As the contest and the network call it: long calls are big ones. The call itself in capitals.</summary>
        internal static void Shout(string text, string sub = "", float seconds = 4f) => Call(Words.Upper(text), sub, seconds >= 3.5f);

        /// <summary>The countdown number, in the middle of the screen.</summary>
        internal static void Count(string text) => MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center, "<size=150%>" + text + "</size>");

        internal static void Clear()
        {
            if (_panel != null) Object.Destroy(_panel.gameObject);
            if (_break != null) Object.Destroy(_break.gameObject);
            _panel = _break = null;
            Cells.Clear();
            _hints = "";
        }

        // ---- the panel at the top ------------------------------------------------------------------------------------------------
        //
        // One panel, in the inventory's sunken look, at the top of the screen:
        //   the contest and its land                                  the round (or wave)
        //   The Crowd: Roaring   [=========-----]                     3 foes left · 0:45
        //   Grimtooth the Mauler ★★  [==========------]               (while a champion is out)
        //   Yours now   [coin] 196  [hide] 12  [scraps] 20 ...        (a gold "+n" rises from whatever grows)
        //   On a win: 875 coins, ...                                   (a Bout or a trial)
        //   DOUBLE!  ·  the crowd is on its feet                       (the announcer's last word, for a few seconds)
        // and under it, between rounds, a second one: the clock, what comes next, and the keys (start now, leave).

        private const float W = 620f, Pad = 14f, BreakW = 520f;
        private static RectTransform _panel, _break, _crowdBar, _champBar, _strip, _hintRow;
        private static TMP_Text _title, _round, _crowd, _foes, _champName, _yours, _onWin, _ticker, _timer, _next;
        private static float _tickerUntil, _nextText;
        private static string _hints = "";
        private static bool _primed;   // (the strip has been filled once this time: from now on whatever grows, or appears, pops)

        private sealed class Cell { public RectTransform Root; public Image Icon; public TMP_Text Count; public int Shown; }
        private static readonly Dictionary<string, Cell> Cells = new Dictionary<string, Cell>();

        private static bool Make()
        {
            if (_panel != null) return true;
            if (!Ui.Ready() || Ui.Canvas == null) return false;
            _panel = Ui.Rect(Ui.Canvas, "ArenaHud", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -20f), new Vector2(W, 120f));
            Ui.Backing(_panel);
            _title = Ui.Fit(Ui.Label(_panel, "", new Vector2(Pad, -8f), new Vector2(W - 2 * Pad - 190f, 26f), 19f, Ui.Warm, Ui.Serif));
            _round = Ui.Fit(Ui.Label(_panel, "", new Vector2(W - Pad - 190f, -10f), new Vector2(190f, 24f), 16f, Ui.Gold, Ui.Serif, TextAlignmentOptions.TopRight));
            _crowd = Ui.Fit(Ui.Label(_panel, "", new Vector2(Pad, 0f), new Vector2(170f, 22f), 14f, Ui.Text));
            _crowdBar = Ui.Bar(_panel, new Vector2(Pad + 176f, 0f), new Vector2(230f, 12f), Ui.Gold);
            _foes = Ui.Fit(Ui.Label(_panel, "", new Vector2(W - Pad - 180f, 0f), new Vector2(180f, 22f), 14f, Ui.Text, null, TextAlignmentOptions.TopRight));
            _champName = Ui.Fit(Ui.Label(_panel, "", new Vector2(Pad, 0f), new Vector2(240f, 22f), 15f, new Color(1f, 0.55f, 0.45f), Ui.Serif));
            _champBar = Ui.Bar(_panel, new Vector2(Pad + 246f, 0f), new Vector2(W - 2 * Pad - 246f, 12f), new Color(0.8f, 0.18f, 0.14f));
            _yours = Ui.Fit(Ui.Label(_panel, T("arena_hud_yours"), new Vector2(Pad, 0f), new Vector2(96f, 22f), 13f, Ui.Dim));
            _strip = Ui.Rect(_panel, "Winnings", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(Pad + 100f, 0f), new Vector2(W - 2 * Pad - 100f, 34f));
            var row = _strip.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.childAlignment = TextAnchor.MiddleLeft; row.spacing = 12f;
            row.childControlWidth = false; row.childControlHeight = false; row.childForceExpandWidth = false; row.childForceExpandHeight = false;
            _onWin = Ui.Fit(Ui.Label(_panel, "", new Vector2(Pad, 0f), new Vector2(W - 2 * Pad, 20f), 13f, Ui.Dim));
            _ticker = Ui.Fit(Ui.Label(_panel, "", new Vector2(Pad, 0f), new Vector2(W - 2 * Pad, 22f), 15f, Ui.Warm, Ui.Serif));

            _break = Ui.Rect(Ui.Canvas, "ArenaBreak", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(BreakW, 132f));
            Ui.Backing(_break);
            _timer = Ui.Label(_break, "", new Vector2(0f, -6f), new Vector2(BreakW, 44f), 34f, Ui.Gold, Ui.Norse, TextAlignmentOptions.Top);
            _next = Ui.Label(_break, "", new Vector2(Pad, -50f), new Vector2(BreakW - 2 * Pad, 42f), 15f, Ui.Text, null, TextAlignmentOptions.Top);   // (two lines: next, and what to do now)
            _hintRow = Ui.Rect(_break, "Keys", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -96f), new Vector2(BreakW - 2 * Pad, 28f));
            var keys = _hintRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            keys.childAlignment = TextAnchor.MiddleCenter; keys.spacing = 8f;
            keys.childControlWidth = false; keys.childControlHeight = false; keys.childForceExpandWidth = false; keys.childForceExpandHeight = false;
            _panel.gameObject.SetActive(false);
            _break.gameObject.SetActive(false);
            return true;
        }

        /// <summary>
        /// Every frame: the panel shows during a contest or a duel (yours), or to someone watching another player's fight from near the arena,
        /// and steps aside while a window (the bag, a chest, the Armourer, the Arena Master, the map) is open or the game's HUD is hidden. Its
        /// words change four times a second; the bars every frame.
        /// </summary>
        internal static void Tick()
        {
            bool contest = Contest.Active, duel = Duel.Active, watch = !contest && !duel && Net.RemoteFight && Net.InRange;
            if (!contest && !duel && !watch) { Hide(); _primed = false; return; }
            bool hidden = global::Hud.IsUserHidden() || InventoryGui.IsVisible() || StoreGui.IsVisible() || Minimap.IsOpen() || Window.IsOpen;
            if (hidden) { Hide(); return; }
            if (!Make()) return;
            if (!_panel.gameObject.activeSelf) { _panel.gameObject.SetActive(true); _nextText = 0f; }
            if (contest || watch) Ui.SetBar(_crowdBar, Crowd.Favour / 100f, Color.Lerp(new Color(0.75f, 0.25f, 0.15f), new Color(1f, 0.8f, 0.3f), Crowd.Favour / 100f));
            else
            {
                Player them = Duel.Opponent;
                Ui.SetBar(_crowdBar, them != null && them.GetMaxHealth() > 0f ? them.GetHealth() / them.GetMaxHealth() : 0f, new Color(0.8f, 0.2f, 0.15f));
            }
            Character champ = contest ? Contest.ChampionOut : null;
            if (champ != null) Ui.SetBar(_champBar, champ.GetMaxHealth() > 0f ? champ.GetHealth() / champ.GetMaxHealth() : 0f);
            if (Time.unscaledTime < _nextText) return;
            _nextText = Time.unscaledTime + 0.25f;
            if (_ticker.text.Length > 0 && Time.unscaledTime > _tickerUntil) _ticker.text = "";
            var things = new List<(string Prefab, int Amount)>();
            string onWin = "";
            if (contest)
            {
                _title.text = Contest.KindName(Contest.Kind) + "  ·  " + Roster.LandName(Contest.Tier);
                _round.text = Contest.Endless ? T("arena_wave", Mathf.Max(1, Contest.Round))
                            : Contest.Kind == Contest.KindOf.Road ? T("arena_round_of", Contest.LandRound, 3)
                            : Contest.Rounds > 1 ? T("arena_round_of", Mathf.Max(1, Contest.Round), Contest.Rounds) : "";
                _crowd.text = T("arena_hud_crowd", Crowd.Mood);
                string clock = Rules.Timed && Contest.Fighting ? $"  ·  {Mathf.CeilToInt(Mathf.Max(0f, Contest.RoundLeft)) / 60}:{Mathf.CeilToInt(Mathf.Max(0f, Contest.RoundLeft)) % 60:00}" : "";
                _foes.text = Contest.Phase == Contest.PhaseKind.Victory ? "<color=#ffd27a>" + T("arena_victory") + "</color>"
                           : Contest.Fighting ? T(Contest.FoesLeft == 1 ? "arena_foe_left" : "arena_foes_left", Contest.FoesLeft) + clock : "";
                things = Contest.Winnings();
                onWin = Contest.OnWin();
                if (champ != null)
                {
                    int stars = Mathf.Max(0, champ.GetLevel() - 1);
                    _champName.text = champ.GetHoverName() + (stars > 0 ? " <color=#ffd27a>" + new string('★', stars) + "</color>" : "");
                }
            }
            else if (duel)
            {
                _title.text = T("arena_hist_duel", Duel.OpponentName);
                _round.text = "";
                _crowd.text = Duel.OpponentName;
                Player me = Player.m_localPlayer;
                _foes.text = T("arena_hud_you", Mathf.RoundToInt(me != null && me.GetMaxHealth() > 0f ? me.GetHealth() / me.GetMaxHealth() * 100f : 0f));
                if (Duel.Wager > 0) things.Add(("Coins", Duel.Wager * 2 - Duel.Wager * 2 / 10));
            }
            else
            {
                _title.text = T("arena_hud_watch", Net.RemoteName);
                _round.text = Net.RemoteKind == "endless" ? T("arena_wave", Mathf.Max(1, Net.RemoteRound))
                            : Net.RemoteRounds > 1 ? T("arena_round_of", Mathf.Max(1, Net.RemoteRound), Net.RemoteRounds) : "";
                _crowd.text = T("arena_hud_crowd", Crowd.Mood);
                _foes.text = Net.RemoteKind != "" && Net.RemoteFoes > 0 ? T(Net.RemoteFoes == 1 ? "arena_foe_left" : "arena_foes_left", Net.RemoteFoes) : "";
            }
            UpdateStrip(things);
            _onWin.text = onWin;
            Layout(champ != null, things.Count > 0, onWin.Length > 0, _ticker.text.Length > 0);
            UpdateBreak(contest);
        }

        private static void Hide()
        {
            if (_panel != null && _panel.gameObject.activeSelf) _panel.gameObject.SetActive(false);
            if (_break != null && _break.gameObject.activeSelf) _break.gameObject.SetActive(false);
        }

        /// <summary>The rows set one under another, only those with something to show, and the panel as tall as they need.</summary>
        private static void Layout(bool champ, bool winnings, bool onWin, bool ticker)
        {
            float y = -38f;
            void Put(Component c, float at) { var r = (RectTransform)c.transform; r.anchoredPosition = new Vector2(r.anchoredPosition.x, at); }
            Put(_crowd, y); Put(_foes, y); _crowdBar.anchoredPosition = new Vector2(_crowdBar.anchoredPosition.x, y - 5f);
            y -= 26f;
            _champName.gameObject.SetActive(champ); _champBar.gameObject.SetActive(champ);
            if (champ) { Put(_champName, y); _champBar.anchoredPosition = new Vector2(_champBar.anchoredPosition.x, y - 5f); y -= 26f; }
            _yours.gameObject.SetActive(winnings); _strip.gameObject.SetActive(winnings);
            if (winnings) { Put(_yours, y - 7f); _strip.anchoredPosition = new Vector2(_strip.anchoredPosition.x, y); y -= 38f; }
            _onWin.gameObject.SetActive(onWin);
            if (onWin) { Put(_onWin, y); y -= 22f; }
            _ticker.gameObject.SetActive(ticker);
            if (ticker) { Put(_ticker, y); y -= 24f; }
            _panel.sizeDelta = new Vector2(W, -y + 4f);
            _break.anchoredPosition = new Vector2(0f, _panel.anchoredPosition.y - _panel.sizeDelta.y - 8f);
        }

        // ---- between the rounds --------------------------------------------------------------------------------------------------

        /// <summary>The second panel, while walking in, arming and between rounds: the clock, what comes next, and the keys.</summary>
        private static void UpdateBreak(bool contest)
        {
            float left = -1f;
            string what = contest ? Contest.TimerLabel(out left) : "";
            bool show = contest && left >= 0f && (Contest.Phase == Contest.PhaseKind.Ready || Contest.Phase == Contest.PhaseKind.Arming || Contest.Phase == Contest.PhaseKind.Break);
            if (_break.gameObject.activeSelf != show) _break.gameObject.SetActive(show);
            if (!show) return;
            int s = Mathf.CeilToInt(Mathf.Max(0f, left));
            _timer.text = $"<color={(left <= 10f ? "#ff7a5a" : "#ffd27a")}>{s / 60}:{s % 60:00}</color>";
            _next.text = what;
            // the keys: start now (between rounds and arming), and leave (backing out before the first round)
            string leave = Plugin.YieldKey.Value.ToString();
            bool before = Contest.BeforeTheFight;
            var keys = new List<(string Key, string Does)>();
            if (Contest.Phase != Contest.PhaseKind.Ready)
            {
                BepInEx.Configuration.KeyboardShortcut ready = Plugin.ReadyKey.Value;
                keys.Add((ready.MainKey != KeyCode.None ? ready.ToString() : T("arena_key_challenge"), T("arena_hint_start")));
            }
            keys.Add((T("arena_key_twice", leave), T(before ? "arena_hint_back_out" : "arena_hint_leave")));
            string sig = string.Join("|", keys.Select(k => k.Key + k.Does));
            if (sig == _hints) return;
            _hints = sig;
            foreach (Transform child in _hintRow) Object.Destroy(child.gameObject);
            foreach (var k in keys) Hint(k.Key, k.Does);
        }

        /// <summary>A key in a little sunken box, and what it does beside it.</summary>
        private static void Hint(string key, string does)
        {
            var group = new GameObject("Hint", typeof(RectTransform)).GetComponent<RectTransform>();
            group.SetParent(_hintRow, false);
            TMP_Text k = Ui.Label(group, key, Vector2.zero, new Vector2(200f, 24f), 14f, Ui.Gold, Ui.Serif, TextAlignmentOptions.Center);
            float kw = Mathf.Ceil(k.GetPreferredValues(key, 400f, 24f).x) + 16f;
            TMP_Text d = Ui.Label(group, does, Vector2.zero, new Vector2(240f, 24f), 14f, Ui.Text, null, TextAlignmentOptions.Left);
            float dw = Mathf.Min(220f, Mathf.Ceil(d.GetPreferredValues(does, 400f, 24f).x) + 4f);
            RectTransform cap = Ui.Rect(group, "Key", new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(kw, 24f));
            Ui.Backing(cap, 1f);
            cap.SetAsFirstSibling();
            var kr = k.rectTransform; kr.SetParent(cap, false); kr.anchorMin = kr.anchorMax = kr.pivot = new Vector2(0.5f, 0.5f); kr.anchoredPosition = Vector2.zero; kr.sizeDelta = new Vector2(kw, 24f);
            var dr = d.rectTransform; dr.anchorMin = dr.anchorMax = dr.pivot = new Vector2(0f, 0.5f); dr.anchoredPosition = new Vector2(kw + 6f, 0f); dr.sizeDelta = new Vector2(dw, 24f);
            Ui.Fit(d);
            group.sizeDelta = new Vector2(kw + 6f + dw + 10f, 26f);
        }

        // ---- the announcer's line ----------------------------------------------------------------------------------------------

        /// <summary>The announcer's word in the panel's last line, for a few seconds (true: shown there; false: no panel to show it in).</summary>
        private static bool Ticker(string text, string sub)
        {
            if (_panel == null || !_panel.gameObject.activeInHierarchy) return false;
            string line = string.IsNullOrEmpty(text) ? sub : string.IsNullOrEmpty(sub) ? "<b>" + text + "</b>" : "<b>" + text + "</b>  ·  " + sub.Replace("\n", "  ·  ");
            _ticker.text = line;
            _tickerUntil = Time.unscaledTime + 5f;
            _nextText = 0f;
            return true;
        }

        // ---- your winnings, as the game's item icons -----------------------------------------------------------------------------

        private static Cell MakeCell(string prefab)
        {
            var root = new GameObject("Cell", typeof(RectTransform)).GetComponent<RectTransform>();
            root.SetParent(_strip, false);
            root.sizeDelta = new Vector2(78f, 34f);
            var icon = new GameObject("Icon", typeof(RectTransform)).AddComponent<Image>();
            var ir = icon.rectTransform;
            ir.SetParent(root, false);
            ir.anchorMin = ir.anchorMax = new Vector2(0f, 0.5f); ir.pivot = new Vector2(0f, 0.5f);
            ir.anchoredPosition = Vector2.zero; ir.sizeDelta = new Vector2(32f, 32f);
            icon.preserveAspect = true; icon.raycastTarget = false;
            icon.sprite = ZNetScene.instance?.GetPrefab(prefab)?.GetComponent<ItemDrop>()?.m_itemData.GetIcon();
            TMP_Text count = Ui.Label(root, "", new Vector2(36f, -6f), new Vector2(44f, 24f), 16f, Ui.Gold, Ui.Serif);
            count.enableWordWrapping = false;
            return new Cell { Root = root, Icon = icon, Count = count };
        }

        /// <summary>A cell for each thing you would walk out with, its count, and a gold "+n" rising from any that grew.</summary>
        private static void UpdateStrip(List<(string Prefab, int Amount)> things)
        {
            var seen = new HashSet<string>();
            foreach (var t in things)
            {
                seen.Add(t.Prefab);
                if (!Cells.TryGetValue(t.Prefab, out Cell cell) || cell.Root == null) Cells[t.Prefab] = cell = MakeCell(t.Prefab);
                if (!cell.Root.gameObject.activeSelf) { cell.Root.gameObject.SetActive(true); cell.Shown = 0; }
                if (t.Amount > cell.Shown && (cell.Shown > 0 || _primed)) Pop(cell, t.Amount - cell.Shown);
                cell.Shown = t.Amount;
                cell.Count.text = t.Amount.ToString();
            }
            foreach (var c in Cells) if (!seen.Contains(c.Key) && c.Value.Root != null) { c.Value.Root.gameObject.SetActive(false); c.Value.Shown = 0; }
            _primed = true;
        }

        private static void Pop(Cell cell, int by)
        {
            if (Plugin.Instance == null) return;
            TMP_Text plus = Ui.Label(cell.Root, "+" + by, new Vector2(36f, 8f), new Vector2(60f, 24f), 15f, new Color(1f, 0.9f, 0.4f), Ui.Serif);
            Plugin.Instance.StartCoroutine(Rise(plus, cell.Root));
        }

        private static System.Collections.IEnumerator Rise(TMP_Text plus, RectTransform cell)
        {
            var r = plus.rectTransform;
            Vector2 from = r.anchoredPosition;
            for (float t = 0f; t < 1.1f; t += Time.unscaledDeltaTime)
            {
                if (plus == null || cell == null) yield break;
                float k = t / 1.1f;
                r.anchoredPosition = from + new Vector2(0f, 26f * k);
                plus.alpha = 1f - k * k;
                cell.localScale = Vector3.one * (1f + 0.25f * Mathf.Sin(Mathf.Clamp01(k * 3f) * Mathf.PI));   // (a little jump)
                yield return null;
            }
            if (cell != null) cell.localScale = Vector3.one;
            if (plus != null) Object.Destroy(plus.gameObject);
        }

        internal static void Draw() { }   // (drawn by the game's interface now)
    }


    /// <summary>
    /// The biome title's lettering for the arena's big calls: it shrinks to fit its box (as far as half its size) rather than running off the
    /// screen, in any language. The game's own titles are left as they are.
    /// </summary>
    [HarmonyPatch(typeof(MessageHud), "UpdateBiomeFound")]
    internal static class MessageHud_UpdateBiomeFound_Fit
    {
        private static GameObject _seen;

        private static void Postfix(GameObject ___m_biomeMsgInstance)
        {
            if (___m_biomeMsgInstance == null || ___m_biomeMsgInstance == _seen) return;
            _seen = ___m_biomeMsgInstance;
            TMP_Text title = Utils.FindChild(_seen.transform, "Title")?.GetComponent<TMP_Text>();
            if (title == null || !Hud.IsOurs(title.text)) return;
            title.enableWordWrapping = false;
            title.fontSizeMax = title.fontSize;
            title.fontSizeMin = title.fontSize * 0.5f;
            title.enableAutoSizing = true;
        }
    }
}
