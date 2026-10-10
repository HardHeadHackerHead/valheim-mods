using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;

namespace BountyBoard
{
    /// <summary>
    /// The board's menu, in the game's own look (GameUi.cs): today's notices, the group's running contracts and rewards waiting to be
    /// collected, as cards with their trophy, reward and progress. It is built again whenever what it shows changes.
    /// </summary>
    internal static class Window
    {
        private const float W = 800f, H = 720f, Pad = 30f, CardH = 104f, CardGap = 8f, ButtonW = 156f;

        internal static bool IsOpen => _window != null;
        internal static float ClosedAt = -1f;   // (the E that closes it mustn't open it again)

        private static RectTransform _window, _list;
        private static BountyBoardStation _board;
        private static int _tab;
        private static float _openedAt, _nextCheck;
        private static string _shown = "", _toast = "";
        private static float _toastUntil;
        private static Action _pending;   // a click's action, run in Tick (never while the window is being built)

        internal static void Open(BountyBoardStation board)
        {
            if (!GameUi.Ready()) return;
            GameUi.IsOpen = () => IsOpen;
            GameUi_EscapeCloses.Close = Close;
            _board = board;
            _tab = 0;
            _openedAt = Time.unscaledTime;
            if (InventoryGui.IsVisible()) InventoryGui.instance.Hide();
            Build(true);
        }

        internal static void Close()
        {
            if (_window != null) UnityEngine.Object.Destroy(_window.gameObject);
            _window = null; _list = null; _board = null; _pending = null; _shown = "";
            ClosedAt = Time.unscaledTime;
            UITooltip.HideTooltip();
        }

        internal static void Tick()
        {
            if (_window == null) { if (_board != null) Close(); return; }   // (the game rebuilt its interface: a world change)
            Player p = Player.m_localPlayer;
            bool useAgain = Time.unscaledTime > _openedAt + 0.2f && ZInput.GetButtonDown("Use");   // E closes it again, as it does a chest
            if (p == null || p.IsDead() || _board == null || Menu.IsVisible() || useAgain || (_board.transform.position - p.transform.position).sqrMagnitude > 12f * 12f)
            {
                Close();
                return;
            }
            if (_pending != null) { Action a = _pending; _pending = null; a(); }
            if (Time.unscaledTime >= _nextCheck)
            {
                _nextCheck = Time.unscaledTime + 0.25f;
                if (Signature() != _shown) Build(false);
            }
        }

        private static void Toast(string text) { _toast = text; _toastUntil = Time.unscaledTime + 4f; }
        private static bool Toasting => Time.unscaledTime < _toastUntil;

        /// <summary>Everything the window shows, so it is built again only when something changed.</summary>
        private static string Signature()
        {
            Plugin plugin = Plugin.Instance;
            State s = plugin?.Current;
            Player p = Player.m_localPlayer;
            var sb = new System.Text.StringBuilder();
            sb.Append(_tab).Append('|').Append(Plugin.ShowTracker.Value).Append('|').Append(Plugin.MaxActive.Value).Append('|').Append(Toasting ? _toast : "");
            sb.Append('|').Append(s == null ? (Time.unscaledTime - _openedAt > 3f ? "silent" : "asking") : s.Total.ToString());
            if (s != null && p != null)
            {
                foreach (Bounty b in s.Posted) sb.Append("|P").Append(b.Id);
                foreach (Bounty b in s.Active)
                    sb.Append("|A").Append(b.Id).Append(',').Append(b.Progress).Append(',').Append(b.Kind == Kind.Gather ? p.GetInventory().CountItems(Rules.ItemName(b.Target)) : 0);
                foreach (Bounty b in s.Done) sb.Append("|D").Append(b.Id).Append(plugin.Claimable(b));
            }
            return sb.ToString();
        }

        // ---- the window --------------------------------------------------------------------------------------------------------

        private static void Build(bool fresh)
        {
            if (!GameUi.Ready() || _board == null) { Close(); return; }
            float scrolled = !fresh && _list != null ? _list.anchoredPosition.y : 0f;
            if (_window != null) UnityEngine.Object.Destroy(_window.gameObject);
            UITooltip.HideTooltip();
            _shown = Signature();

            _window = GameUi.Window("BountyBoardWindow", new Vector2(W, H));
            Transform w = _window;
            GameUi.Label(w, "Bounty Board", new Vector2(Pad, -20f), new Vector2(360f, 44f), 34f, GameUi.Warm, GameUi.Norse);
            GameUi.Braid(w, new Vector2(0f, -76f), W - Pad * 2f);

            Plugin plugin = Plugin.Instance;
            State state = plugin?.Current;
            Player player = Player.m_localPlayer;
            if (state == null || player == null)
            {
                GameUi.Label(w, Time.unscaledTime - _openedAt > 3f
                        ? "The host's game is not answering.\n\n<size=85%><color=#bdb7a9>BountyBoard has to be installed on the host too: it keeps the group's contracts.</color></size>"
                        : "Reading the board...", new Vector2(Pad, -100f), new Vector2(W - Pad * 2f, 160f), 20f, GameUi.Text);
                return;
            }

            // the group's standing, top right
            int rank = Rules.Rank(state.Total);
            string next = rank + 1 < Rules.RankAt.Length ? $"  ·  next rank at {Rules.RankAt[rank + 1]}" : "";
            GameUi.Label(w, $"Your group: <color=#ffd27a>{Rules.RankName[rank]}</color>\n<size=80%><color=#bdb7a9>{state.Total} done  ·  +{Rules.RankBonus[rank]}% on rewards{next}</color></size>",
                new Vector2(-Pad, -22f), new Vector2(420f, 50f), 19f, GameUi.Text, GameUi.Sans, TextAlignmentOptions.TopRight, new Vector2(1f, 1f));

            // tabs
            int toClaim = plugin.ToClaim();
            string[] tabs = { $"Notices ({state.Posted.Count})", $"Running ({state.Active.Count}/{Plugin.MaxActive.Value})", $"Rewards ({toClaim})" };
            float tabW = (W - Pad * 2f - 16f) / 3f;
            for (int i = 0; i < 3; i++)
            {
                int tab = i;
                var t = GameUi.Tab(w, tabs[i], new Vector2(Pad + i * (tabW + 8f), -92f), new Vector2(tabW, 40f), _tab == i, () => { _tab = tab; _shown = ""; });
                if (i == 2 && toClaim > 0 && _tab != 2) GameUi.Label(t).color = GameUi.Good;   // rewards waiting
            }

            // the cards
            RectTransform area = GameUi.Rect(w, "List", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(Pad - 6f, -142f), new Vector2(W - Pad * 2f + 12f, H - 142f - 84f));
            _list = GameUi.Scroll(area, 6f);
            float y = 0f, cardW = _list.sizeDelta.x - 6f;
            void Note(string text)
            {
                TMP_Text t = GameUi.Label(_list, text, new Vector2(8f, -y - 6f), new Vector2(cardW - 16f, 30f), 18f, GameUi.Dim);
                y += t.GetPreferredValues(text, cardW - 16f, 0f).y + 14f;   // (its own height: a fixed one can push the cards past the box)
            }

            if (_tab == 0)
            {
                bool full = state.Active.Count >= Plugin.MaxActive.Value;
                if (state.Posted.Count == 0) Note("Nothing is posted yet. Check back tomorrow.");
                if (full && state.Posted.Count > 0) Note($"The group is already on {Plugin.MaxActive.Value} contracts. Finish one before taking another.");
                foreach (Bounty b in state.Posted)
                {
                    bool taken = state.Active.Any(a => a.Id == b.Id), finished = state.Done.Any(d => d.Id == b.Id);
                    Bounty bounty = b;
                    Card(state, b, 0, ref y, cardW, finished ? "Done" : taken ? "Taken" : "Take it", !finished && !taken && !full, false,
                        () => { plugin.Take(bounty); Toast("Taken for the whole group."); });
                }
            }
            else if (_tab == 1)
            {
                if (state.Active.Count == 0) Note("No contracts are running. Take one from the notices.");
                foreach (Bounty b in state.Active.ToList())
                {
                    bool gather = b.Kind == Kind.Gather;
                    int have = gather ? Mathf.Min(player.GetInventory().CountItems(Rules.ItemName(b.Target)), b.Count - b.Progress) : 0;
                    Bounty bounty = b;
                    RectTransform card = Card(state, b, b.Progress, ref y, cardW, gather ? (have > 0 ? $"Hand in {have}" : "Hand in") : "Hunting", gather && have > 0, gather && have > 0,
                        () => { int n = plugin.HandIn(bounty); if (n > 0) Toast("Handed in " + n + "."); });
                    if (b.Progress == 0)
                        GameUi.Button(card, "Give back", new Vector2(cardW - ButtonW - 14f, -58f), new Vector2(ButtonW, 32f), () => _pending = () => plugin.Abandon(bounty), true, 15f);
                }
            }
            else
            {
                List<Bounty> mine = state.Done.Where(plugin.Claimable).ToList();
                if (mine.Count == 0) Note("Nothing to collect. Finished contracts show up here until you take your reward.");
                foreach (Bounty b in mine)
                {
                    Bounty bounty = b;
                    Card(state, b, b.Count, ref y, cardW, "Collect", true, true, () => plugin.Claim(bounty));
                }
            }
            _list.sizeDelta = new Vector2(_list.sizeDelta.x, Mathf.Max(0f, y - CardGap));   // (no gap after the last card: it would scroll for nothing)
            _list.anchoredPosition = new Vector2(0f, Mathf.Clamp(scrolled, 0f, Mathf.Max(0f, y)));

            // bottom: what just happened (or a tip), and the switch for the contracts on your screen
            GameUi.Label(w, Toasting ? _toast : "Kills count when you land the last blow, whoever you are with.", new Vector2(Pad, -(H - 66f)),
                new Vector2(W - Pad * 2f - 300f, 48f), 17f, Toasting ? GameUi.Good : GameUi.Dim);
            bool on = Plugin.ShowTracker.Value;
            var toggle = GameUi.Button(w, on ? "Contracts on screen: On" : "Contracts on screen: Off", new Vector2(W - Pad - 284f, -(H - 72f)), new Vector2(284f, 44f),
                () => _pending = () => Plugin.ShowTracker.Value = !Plugin.ShowTracker.Value, true, 17f);
            if (on) GameUi.Label(toggle).color = GameUi.Gold;
        }

        /// <summary>One contract: its trophy, what it asks, how hard and where, the reward (with the group's bonus), the group's progress and a button.</summary>
        private static RectTransform Card(State state, Bounty b, int progress, ref float y, float width, string action, bool enabled, bool good, Action act)
        {
            RectTransform card = GameUi.Box(_list, new Vector2(0f, -y), new Vector2(width, CardH));
            y += CardH + CardGap;
            float textW = width - 96f - ButtonW - 30f;

            GameUi.Icon(card, new Vector2(14f, -20f), new Vector2(64f, 64f), IconOf(b));
            GameUi.Label(card, Rules.Describe(b), new Vector2(92f, -10f), new Vector2(textW, 28f), 21f, GameUi.Text);
            TMP_Text tag = GameUi.Label(card, $"{Rules.StarNames[Mathf.Clamp(b.Stars, 1, 3)]}  ·  {Rules.TierNames[Mathf.Clamp(b.Tier, 0, Rules.TierNames.Length - 1)]}",
                new Vector2(92f, -42f), new Vector2(textW, 22f), 16f, StarColor(b.Stars));
            tag.enableWordWrapping = false;

            // the reward on the same line, after how hard and where: icons with the amounts (and the game's tooltips), rank bonus included
            float x = 92f + tag.GetPreferredValues(tag.text, textW, 0f).x + 26f;
            foreach (KeyValuePair<string, int> item in new[] { new KeyValuePair<string, int>("Coins", b.Coins) }.Concat(b.Items))
            {
                ItemDrop.ItemData data = ItemOf(item.Key);
                if (data == null || x > 92f + textW - 60f) continue;
                x += GameUi.Chip(card, new Vector2(x, -38f), data, Rules.WithBonus(item.Value, state.Total).ToString());
            }

            // the whole group's progress
            float frac = Mathf.Clamp01(progress / (float)Mathf.Max(1, b.Count));
            GameUi.Bar(card, new Vector2(92f, -74f), new Vector2(textW, 18f), frac, progress >= b.Count ? GameUi.Good : GameUi.Gold, $"{Mathf.Min(progress, b.Count)}/{b.Count}", 14f);

            var button = GameUi.Button(card, action, new Vector2(width - ButtonW - 14f, -12f), new Vector2(ButtonW, 40f), () => _pending = act, enabled, 18f);
            if (enabled && good) GameUi.Label(button).color = GameUi.Gold;
            return card;
        }

        private static Color StarColor(int stars) => stars >= 3 ? new Color(0.95f, 0.45f, 0.38f) : stars == 2 ? new Color(1f, 0.72f, 0.35f) : GameUi.Dim;

        private static readonly Dictionary<string, Sprite> IconCache = new Dictionary<string, Sprite>();

        private static Sprite IconOf(Bounty b)
        {
            string key = b.Kind + b.Target;
            if (IconCache.TryGetValue(key, out Sprite cached) && cached != null) return cached;
            Sprite sprite = null;
            if (b.Kind == Kind.Gather) sprite = ItemIcon(b.Target);
            else if (b.Kind == Kind.Sweep) sprite = ItemIcon("TrophyBoar");
            else
                foreach (string candidate in new[] { "Trophy" + b.Target, "Trophy" + b.Target.Replace("_", ""), "Trophy" + b.Target.Split('_')[0], "TrophyBoar" })
                    if ((sprite = ItemIcon(candidate)) != null) break;
            IconCache[key] = sprite;
            return sprite;
        }

        private static ItemDrop.ItemData ItemOf(string prefab)
        {
            GameObject go = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(prefab) : null;
            ItemDrop drop = go != null ? go.GetComponent<ItemDrop>() : null;
            return drop != null ? drop.m_itemData : null;
        }

        internal static Sprite ItemIcon(string prefab) => ItemOf(prefab)?.GetIcon();
    }
}
