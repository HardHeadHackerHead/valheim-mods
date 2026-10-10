using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace BountyBoard
{
    /// <summary>The board's menu: today's notices, the group's running contracts, and rewards waiting to be collected.</summary>
    internal static class Window
    {
        internal static bool IsOpen;

        private static BountyBoardStation _board;
        private static int _tab;
        private static Vector2 _scroll;
        private static Rect _rect;
        private static bool _placed;
        private static float _openedAt;
        private static Rect _lastCard;
        private static string _toast = "";
        private static float _toastUntil;

        internal static void Open(BountyBoardStation board)
        {
            _board = board;
            _tab = 0;
            _scroll = Vector2.zero;
            _openedAt = Time.unscaledTime;
            if (InventoryGui.IsVisible()) InventoryGui.instance.Hide();
            IsOpen = true;
        }

        internal static void Close()
        {
            IsOpen = false;
            _board = null;
        }

        internal static void Tick()
        {
            if (!IsOpen) return;
            Player p = Player.m_localPlayer;
            if (p == null || _board == null || Menu.IsVisible() || (_board.transform.position - p.transform.position).sqrMagnitude > 12f * 12f) Close();
        }

        private static void Toast(string text) { _toast = text; _toastUntil = Time.unscaledTime + 4f; }

        // ---- drawing ----

        internal static void Draw()
        {
            if (!IsOpen || _board == null) return;
            FreeTheMouse();
            Styles.Ensure();

            Matrix4x4 previous = GUI.matrix;
            float s = Mathf.Max(0.8f, Screen.height / 1080f);
            GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f));
            float sw = Screen.width / s, sh = Screen.height / s;
            float w = Mathf.Min(660f, sw - 40f), h = Mathf.Min(720f, sh - 50f);
            if (!_placed) { _rect = new Rect((sw - w) / 2f, (sh - h) / 2f, w, h); _placed = true; }
            _rect.width = w; _rect.height = h;
            _rect = GUI.Window(8861, _rect, Contents, GUIContent.none, GUIStyle.none);
            _rect.x = Mathf.Clamp(_rect.x, 0f, Mathf.Max(0f, sw - w));
            _rect.y = Mathf.Clamp(_rect.y, 0f, Mathf.Max(0f, sh - h));
            GUI.matrix = previous;
        }

        private static void Contents(int id)
        {
            float w = _rect.width, h = _rect.height;
            Styles.Rounded(new Rect(0, 0, w, h), new Color(0.07f, 0.06f, 0.05f, 0.98f), 9f);
            Styles.Outline(new Rect(0, 0, w, h), new Color(0.62f, 0.47f, 0.22f, 1f), 9f);

            Plugin plugin = Plugin.Instance;
            State state = plugin?.Current;
            Player player = Player.m_localPlayer;

            GUILayout.BeginArea(new Rect(16f, 12f, w - 32f, h - 24f));
            GUILayout.BeginHorizontal();
            GUILayout.Label("Bounty Board", Styles.Title);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Close", Styles.Button, GUILayout.Width(80), GUILayout.Height(28))) Close();
            GUILayout.EndHorizontal();

            if (state == null || player == null)
            {
                GUILayout.Label(Time.unscaledTime - _openedAt > 3f
                    ? "The host's game is not answering. BountyBoard has to be installed on the host too."
                    : "Reading the board...", Styles.Dim);
                GUILayout.EndArea();
                GUI.DragWindow(new Rect(0, 0, w, 40));
                return;
            }

            int rank = Rules.Rank(state.Total);
            GUILayout.Label($"Everyone here shares these contracts.  Your group: <b>{Rules.RankName[rank]}</b>  ·  {state.Total} done  ·  +{Rules.RankBonus[rank]}% on rewards" +
                (rank + 1 < Rules.RankAt.Length ? $"  ·  next rank at {Rules.RankAt[rank + 1]}" : ""), Styles.Dim);

            bool tracker = Plugin.ShowTracker.Value;
            bool trackerNow = GUILayout.Toggle(tracker, "  Show the contracts on my screen", Styles.Toggle);
            if (trackerNow != tracker) Plugin.ShowTracker.Value = trackerNow;
            GUILayout.Space(2);

            int toClaim = plugin.ToClaim();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button($"Notices ({state.Posted.Count})", _tab == 0 ? Styles.ButtonOn : Styles.Button, GUILayout.Height(30))) { _tab = 0; _scroll = Vector2.zero; }
            if (GUILayout.Button($"Running ({state.Active.Count}/{Plugin.MaxActive.Value})", _tab == 1 ? Styles.ButtonOn : Styles.Button, GUILayout.Height(30))) { _tab = 1; _scroll = Vector2.zero; }
            if (GUILayout.Button($"Rewards ({toClaim})", _tab == 2 ? Styles.ButtonOn : (toClaim > 0 ? Styles.ButtonGood : Styles.Button), GUILayout.Height(30))) { _tab = 2; _scroll = Vector2.zero; }
            GUILayout.EndHorizontal();
            GUILayout.Space(4);

            if (_tab == 0) DrawNotices(state, plugin);
            else if (_tab == 1) DrawRunning(state, plugin, player);
            else DrawRewards(state, plugin);

            GUILayout.Space(4);
            bool toasting = Time.unscaledTime < _toastUntil;
            GUILayout.Label(toasting ? _toast : "A new contract can be taken when one is finished. Kills count when you land the last blow, whoever you are with.", toasting ? Styles.Good : Styles.Dim);
            GUILayout.EndArea();
            GUI.DragWindow(new Rect(0, 0, w, 40));
        }

        private static void DrawNotices(State state, Plugin plugin)
        {
            if (state.Posted.Count == 0) { GUILayout.Label("Nothing is posted yet. Check back tomorrow.", Styles.Dim); return; }
            bool full = state.Active.Count >= Plugin.MaxActive.Value;

            _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.ExpandHeight(true));
            foreach (Bounty b in state.Posted)
            {
                bool taken = state.Active.Any(a => a.Id == b.Id), finished = state.Done.Any(d => d.Id == b.Id);
                string text = finished ? "Done" : taken ? "Taken" : "Take it";
                if (Card(state, b, 0, text, !finished && !taken && !full, Styles.Button) && !finished && !taken)
                {
                    plugin.Take(b);
                    Toast("Taken for the whole group.");
                }
            }
            if (full) GUILayout.Label($"The group is already on {Plugin.MaxActive.Value} contracts. Finish one before taking another.", Styles.Dim);
            GUILayout.EndScrollView();
        }

        private static void DrawRunning(State state, Plugin plugin, Player player)
        {
            if (state.Active.Count == 0) { GUILayout.Label("No contracts are running. Take one from the notices.", Styles.Dim); return; }

            _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.ExpandHeight(true));
            foreach (Bounty b in state.Active.ToList())
            {
                bool gather = b.Kind == Kind.Gather;
                int have = gather ? Mathf.Min(player.GetInventory().CountItems(Rules.ItemName(b.Target)), b.Count - b.Progress) : 0;
                string text = gather ? (have > 0 ? $"Hand in {have}" : "Hand in") : "Hunting";
                if (Card(state, b, b.Progress, text, gather && have > 0, gather && have > 0 ? Styles.ButtonOn : Styles.Button) && gather && have > 0)
                {
                    int n = plugin.HandIn(b);
                    if (n > 0) Toast("Handed in " + n + ".");
                }
                if (b.Progress == 0 && GUI.Button(new Rect(_lastCard.xMax - 104f, _lastCard.yMax - 30f, 94f, 22f), "Give back", Styles.Small)) plugin.Abandon(b);
            }
            GUILayout.EndScrollView();
        }

        private static void DrawRewards(State state, Plugin plugin)
        {
            List<Bounty> mine = state.Done.Where(plugin.Claimable).ToList();
            if (mine.Count == 0) { GUILayout.Label("Nothing to collect. Finished contracts show up here until you take your reward.", Styles.Dim); return; }

            _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.ExpandHeight(true));
            foreach (Bounty b in mine)
                if (Card(state, b, b.Count, "Collect", true, Styles.ButtonOn)) plugin.Claim(b);
            GUILayout.EndScrollView();
        }

        // ---- one contract ----

        private static bool Card(State state, Bounty b, int progress, string buttonText, bool enabled, GUIStyle buttonStyle)
        {
            Rect r = GUILayoutUtility.GetRect(0f, 104f, GUILayout.ExpandWidth(true));
            r.height = 100f;
            _lastCard = r;
            GUILayout.Space(4);

            Styles.Rounded(r, new Color(0.13f, 0.11f, 0.09f, 0.98f), 7f);
            Styles.Outline(r, StarColor(b.Stars) * 0.8f, 7f);

            Styles.DrawIcon(new Rect(r.x + 10f, r.y + 10f, 56f, 56f), IconOf(b));

            float textW = r.width - 200f;
            GUI.Label(new Rect(r.x + 76f, r.y + 5f, textW, 26f), Rules.Describe(b), Styles.Row);
            var tag = new GUIStyle(Styles.Dim) { fontStyle = FontStyle.Bold };
            tag.normal.textColor = StarColor(b.Stars);
            GUI.Label(new Rect(r.x + 76f, r.y + 29f, textW, 18f), $"{Rules.StarNames[Mathf.Clamp(b.Stars, 1, 3)]}  ·  {Rules.TierNames[Mathf.Clamp(b.Tier, 0, Rules.TierNames.Length - 1)]}", tag);

            // the reward: coins and materials, with the group's rank bonus included
            float x = r.x + 76f, y = r.y + 50f;
            GUI.Label(new Rect(x, y, 56f, 22f), "Reward:", Styles.Dim);
            x += 54f;
            x = RewardChip(x, y, ItemIcon("Coins"), Rules.WithBonus(b.Coins, state.Total).ToString());
            foreach (KeyValuePair<string, int> item in b.Items) x = RewardChip(x, y, ItemIcon(item.Key), Rules.WithBonus(item.Value, state.Total).ToString());

            // progress for the whole group
            var bar = new Rect(r.x + 76f, r.y + 76f, textW, 12f);
            Styles.Rounded(bar, new Color(0.05f, 0.04f, 0.03f, 1f), 4f);
            float frac = Mathf.Clamp01(progress / (float)Mathf.Max(1, b.Count));
            if (frac > 0f) Styles.Rounded(new Rect(bar.x, bar.y, bar.width * frac, bar.height), progress >= b.Count ? new Color(0.45f, 0.78f, 0.35f) : new Color(0.78f, 0.58f, 0.22f), 4f);
            GUI.Label(bar, $"{Mathf.Min(progress, b.Count)}/{b.Count}", Styles.BarText);

            var button = new Rect(r.xMax - 114f, r.y + 14f, 104f, 32f);
            bool pressed = false;
            GUI.enabled = enabled;
            if (GUI.Button(button, buttonText, buttonStyle)) pressed = true;
            GUI.enabled = true;
            return pressed;
        }

        private static float RewardChip(float x, float y, Sprite icon, string amount)
        {
            Styles.DrawIcon(new Rect(x, y, 22f, 22f), icon);
            GUI.Label(new Rect(x + 23f, y, 46f, 22f), amount, Styles.ChipText);
            return x + 23f + Styles.ChipText.CalcSize(new GUIContent(amount)).x + 10f;
        }

        private static Color StarColor(int stars) => stars >= 3 ? new Color(0.9f, 0.35f, 0.3f) : stars == 2 ? new Color(0.95f, 0.65f, 0.25f) : new Color(0.62f, 0.5f, 0.3f);

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

        internal static Sprite ItemIcon(string prefab)
        {
            GameObject go = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(prefab) : null;
            ItemDrop drop = go != null ? go.GetComponent<ItemDrop>() : null;
            return drop != null ? drop.m_itemData.GetIcon() : null;
        }

        private static void FreeTheMouse()
        {
            if (Cursor.lockState != CursorLockMode.None) Cursor.lockState = CursorLockMode.None;
            if (!Cursor.visible) Cursor.visible = true;
        }
    }

    /// <summary>The look of the menu and the tracker: the same brown and gold as the other mods.</summary>
    internal static class Styles
    {
        internal static GUIStyle Title, Dim, Row, Good, Button, ButtonOn, ButtonGood, Small, BarText, ChipText, Toggle;
        private static readonly List<Texture2D> Textures = new List<Texture2D>();

        private static Texture2D Box(Color fill, Color border)
        {
            const int size = 6;
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Point };
            for (int x = 0; x < size; x++)
                for (int y = 0; y < size; y++)
                    t.SetPixel(x, y, (x < 2 || y < 2 || x >= size - 2 || y >= size - 2) ? border : fill);
            t.Apply();
            Textures.Add(t);
            return t;
        }

        private static Font _bodyFont, _headingFont;
        private static bool _fontsLooked;

        /// <summary>The game's own fonts, looked up once: Averia for text and numbers, Norse for headings (null if a game update renamed them).</summary>
        internal static Font GameFont(bool heading)
        {
            if (!_fontsLooked)
            {
                _fontsLooked = true;
                try
                {
                    Font[] all = Resources.FindObjectsOfTypeAll<Font>();
                    Font Find(params string[] names)
                    {
                        foreach (string name in names)
                            foreach (Font f in all)
                                if (f != null && f.name.StartsWith(name, System.StringComparison.OrdinalIgnoreCase)) return f;
                        return null;
                    }
                    _bodyFont = Find("AveriaSerifLibre-Bold", "AveriaSerifLibre", "Averia");
                    _headingFont = Find("Norsebold", "Norse") ?? _bodyFont;
                }
                catch (System.Exception) { }
            }
            return heading ? _headingFont : _bodyFont;
        }

        private static GUIStyle Make(Color fill, Color hover, Color border, int font)
        {
            var s = new GUIStyle(GUI.skin.button)
            {
                fontSize = font, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter,
                border = new RectOffset(2, 2, 2, 2), padding = new RectOffset(6, 6, 3, 3), margin = new RectOffset(3, 3, 3, 3),
                font = GameFont(false),
            };
            s.normal.background = Box(fill, border);
            s.hover.background = s.active.background = s.focused.background = Box(hover, border);
            s.normal.textColor = s.hover.textColor = s.active.textColor = s.focused.textColor = new Color(0.95f, 0.9f, 0.8f);
            s.onNormal = s.normal;
            return s;
        }

        internal static void Ensure()
        {
            if (Title != null) return;
            Title = new GUIStyle(GUI.skin.label) { fontSize = 20, fontStyle = FontStyle.Bold };
            Title.normal.textColor = new Color(0.95f, 0.78f, 0.35f);
            Dim = new GUIStyle(GUI.skin.label) { fontSize = 12, wordWrap = true, richText = true, clipping = TextClipping.Clip };
            Dim.normal.textColor = new Color(0.72f, 0.69f, 0.64f);
            Row = new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold, clipping = TextClipping.Clip };
            Row.normal.textColor = new Color(0.95f, 0.91f, 0.84f);
            Good = new GUIStyle(Dim) { fontSize = 13, fontStyle = FontStyle.Bold };
            Good.normal.textColor = new Color(0.6f, 0.9f, 0.6f);
            BarText = new GUIStyle(GUI.skin.label) { fontSize = 10, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            BarText.normal.textColor = Color.white;
            ChipText = new GUIStyle(GUI.skin.label) { fontSize = 13, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
            ChipText.normal.textColor = new Color(0.95f, 0.88f, 0.7f);
            Toggle = new GUIStyle(GUI.skin.toggle) { fontSize = 13 };
            Toggle.normal.textColor = Toggle.onNormal.textColor = Toggle.hover.textColor = Toggle.onHover.textColor = Toggle.active.textColor = Toggle.onActive.textColor = new Color(0.95f, 0.9f, 0.8f);
            Button = Make(new Color(0.22f, 0.19f, 0.15f), new Color(0.33f, 0.27f, 0.18f), new Color(0.45f, 0.36f, 0.2f), 13);
            ButtonOn = Make(new Color(0.55f, 0.42f, 0.16f), new Color(0.62f, 0.48f, 0.2f), new Color(0.95f, 0.78f, 0.35f), 13);
            ButtonGood = Make(new Color(0.2f, 0.36f, 0.2f), new Color(0.28f, 0.48f, 0.26f), new Color(0.5f, 0.8f, 0.45f), 13);
            Small = Make(new Color(0.2f, 0.12f, 0.1f), new Color(0.35f, 0.16f, 0.12f), new Color(0.5f, 0.25f, 0.2f), 11);
            // the game's fonts: Norse for the title (words only), Averia for the rest (Norse draws 0 as a rune)
            Title.font = GameFont(true);
            foreach (GUIStyle s in new[] { Dim, Row, Good, BarText, ChipText, Toggle }) s.font = GameFont(false);
        }

        internal static void Destroy()
        {
            foreach (Texture2D t in Textures) if (t != null) Object.Destroy(t);
            Textures.Clear();
            Title = Dim = Row = Good = Button = ButtonOn = ButtonGood = Small = BarText = ChipText = Toggle = null;
        }

        internal static void Rounded(Rect r, Color c, float radius) =>
            GUI.DrawTexture(r, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, c, Vector4.zero, new Vector4(radius, radius, radius, radius));

        internal static void Outline(Rect r, Color c, float radius) =>
            GUI.DrawTexture(r, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, c, Vector4.one * 1.5f, new Vector4(radius, radius, radius, radius));

        internal static void DrawIcon(Rect r, Sprite sprite)
        {
            if (sprite == null || sprite.texture == null || Event.current.type != EventType.Repaint) return;
            Texture2D tex = sprite.texture;
            Rect t = sprite.textureRect;
            GUI.DrawTextureWithTexCoords(r, tex, new Rect(t.x / tex.width, t.y / tex.height, t.width / tex.width, t.height / tex.height));
        }
    }
}
