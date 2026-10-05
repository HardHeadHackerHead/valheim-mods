using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace BountyBoard
{
    /// <summary>The notice board menu: the notices posted today, and the contracts you have taken.</summary>
    internal static class Window
    {
        internal static bool IsOpen;

        private static BountyBoardStation _board;
        private static int _tab;
        private static Vector2 _scroll;
        private static Rect _rect;
        private static bool _placed;
        private static Rect _lastCard;
        private static string _toast = "";
        private static float _toastUntil;

        private static List<Bounty> _posted = new List<Bounty>();
        private static float _postedAt = -99f;
        private static string _postedFor = "";

        internal static void Open(BountyBoardStation board)
        {
            _board = board;
            _tab = 0;
            _scroll = Vector2.zero;
            _postedAt = -99f;
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

        private static void Refresh()
        {
            string key = _board.Key;
            if (key == _postedFor && Time.unscaledTime - _postedAt < 2f) return;
            _postedFor = key;
            _postedAt = Time.unscaledTime;
            _posted = Bounties.Posted(key, Plugin.NoticesPerBoard.Value);
        }

        private static void Toast(string text) { _toast = text; _toastUntil = Time.unscaledTime + 4f; }

        // ---- drawing ----

        internal static void Draw()
        {
            if (!IsOpen || _board == null) return;
            FreeTheMouse();
            Styles.Ensure();
            Refresh();

            Matrix4x4 previous = GUI.matrix;
            float s = Mathf.Max(0.8f, Screen.height / 1080f);
            GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f));
            float sw = Screen.width / s, sh = Screen.height / s;
            float w = Mathf.Min(620f, sw - 40f), h = Mathf.Min(680f, sh - 60f);
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

            Player player = Player.m_localPlayer;
            int total = Bounties.Total(), rank = Bounties.Rank(total);

            GUILayout.BeginArea(new Rect(16f, 12f, w - 32f, h - 24f));
            GUILayout.BeginHorizontal();
            GUILayout.Label("Bounty Board", Styles.Title);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Close", Styles.Button, GUILayout.Width(80), GUILayout.Height(28))) Close();
            GUILayout.EndHorizontal();

            GUILayout.Label($"Rank: <b>{Bounties.RankName[rank]}</b>  ·  {total} contract{(total == 1 ? "" : "s")} done  ·  +{Bounties.RankBonus[rank]}% pay" +
                (rank + 1 < Bounties.RankAt.Length ? $"  ·  next rank at {Bounties.RankAt[rank + 1]}" : ""), Styles.Dim);
            GUILayout.Space(4);

            int activeCount = Bounties.Active().Count;
            GUILayout.BeginHorizontal();
            if (GUILayout.Button($"Notices ({_posted.Count})", _tab == 0 ? Styles.ButtonOn : Styles.Button, GUILayout.Height(30))) { _tab = 0; _scroll = Vector2.zero; }
            if (GUILayout.Button($"My contracts ({activeCount}/{Plugin.MaxActive.Value})", _tab == 1 ? Styles.ButtonOn : Styles.Button, GUILayout.Height(30))) { _tab = 1; _scroll = Vector2.zero; }
            GUILayout.EndHorizontal();
            GUILayout.Space(4);

            if (_tab == 0) DrawNotices(player); else DrawContracts(player);

            GUILayout.Space(4);
            string foot = Time.unscaledTime < _toastUntil ? _toast : "New notices are posted each morning. Kills count when you land the last blow.";
            GUILayout.Label(foot, Time.unscaledTime < _toastUntil ? Styles.Good : Styles.Dim);
            GUILayout.EndArea();
            GUI.DragWindow(new Rect(0, 0, w, 40));
        }

        private static void DrawNotices(Player player)
        {
            if (_posted.Count == 0) { GUILayout.Label("Nothing is posted yet. Check back tomorrow.", Styles.Dim); return; }
            HashSet<string> done = Bounties.Done();
            List<Bounty> active = Bounties.Active();
            bool full = active.Count >= Plugin.MaxActive.Value;

            _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.ExpandHeight(true));
            foreach (Bounty b in _posted)
            {
                bool taken = active.Any(a => a.Id == b.Id), finished = done.Contains(b.Id);
                string button = finished ? "Done" : taken ? "Taken" : "Take it";
                if (Card(b, b.Progress, button, !finished && !taken && !full, Styles.Button) && !finished && !taken)
                {
                    if (Bounties.Take(b)) { Toast("Contract taken. Good hunting."); BountyBoardStation.PlayEffects(_board.transform.position, false); }
                }
            }
            if (full) GUILayout.Label("You are carrying as many contracts as you can. Finish or drop one first.", Styles.Dim);
            GUILayout.EndScrollView();
        }

        private static void DrawContracts(Player player)
        {
            List<Bounty> active = Bounties.Active();
            if (active.Count == 0) { GUILayout.Label("You have no contracts. Take one from the notices.", Styles.Dim); return; }

            _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.ExpandHeight(true));
            foreach (Bounty b in active.ToList())
            {
                bool ready = Bounties.IsComplete(b, player);
                int progress = b.Kind == Kind.Gather ? Mathf.Min(b.Count, player.GetInventory().CountItems(Bounties.ItemName(b.Target))) : b.Progress;
                bool pressed = Card(b, progress, ready ? "Hand in" : "In progress", ready, ready ? Styles.ButtonOn : Styles.Button);
                if (pressed && ready)
                {
                    int paid = Bounties.Claim(b, player);
                    if (paid > 0) { Toast($"Paid {paid} coins. Well done."); BountyBoardStation.PlayEffects(_board.transform.position, true); }
                }
                Rect last = _lastCard;
                if (GUI.Button(new Rect(last.xMax - 104f, last.yMax - 30f, 94f, 22f), "Drop", Styles.Small)) Bounties.Abandon(b.Id);
            }
            GUILayout.EndScrollView();
        }

        /// <summary>One notice: what to do, progress, the pay, and a button. Returns true when the button was pressed (and was enabled).</summary>
        private static bool Card(Bounty b, int progress, string buttonText, bool enabled, GUIStyle buttonStyle)
        {
            Rect r = GUILayoutUtility.GetRect(0f, 84f, GUILayout.ExpandWidth(true));
            r.height = 80f;
            _lastCard = r;
            GUILayout.Space(4);

            Styles.Rounded(r, new Color(0.13f, 0.11f, 0.09f, 0.98f), 7f);
            Styles.Outline(r, new Color(0.38f, 0.30f, 0.17f, 1f), 7f);

            // the picture: the loot to bring, or the trophy of what to hunt
            var pic = new Rect(r.x + 10f, r.y + 12f, 56f, 56f);
            Styles.DrawIcon(pic, IconOf(b));

            string verb = b.Kind == Kind.Gather ? "Bring" : b.Kind == Kind.Elite ? "Slay starred" : "Hunt";
            GUI.Label(new Rect(r.x + 76f, r.y + 6f, r.width - 190f, 26f), $"{verb}  {b.Count} × {Bounties.TargetName(b)}", Styles.Row);
            GUI.Label(new Rect(r.x + 76f, r.y + 30f, r.width - 190f, 20f),
                $"{Bounties.TierNames[Mathf.Clamp(b.Tier, 0, Bounties.TierNames.Length - 1)]}  ·  reward {PayText(b)} coins", Styles.Dim);

            // progress bar
            var bar = new Rect(r.x + 76f, r.y + 56f, r.width - 190f, 12f);
            Styles.Rounded(bar, new Color(0.05f, 0.04f, 0.03f, 1f), 4f);
            float frac = Mathf.Clamp01(progress / (float)Mathf.Max(1, b.Count));
            if (frac > 0f) Styles.Rounded(new Rect(bar.x, bar.y, bar.width * frac, bar.height), progress >= b.Count ? new Color(0.45f, 0.78f, 0.35f) : new Color(0.78f, 0.58f, 0.22f), 4f);
            GUI.Label(bar, $"{Mathf.Min(progress, b.Count)}/{b.Count}", Styles.BarText);

            var button = new Rect(r.xMax - 104f, r.y + 14f, 94f, 32f);
            bool pressed = false;
            GUI.enabled = enabled;
            if (GUI.Button(button, buttonText, buttonStyle)) pressed = true;
            GUI.enabled = true;
            return pressed;
        }

        private static string PayText(Bounty b)
        {
            int bonus = Bounties.RankBonus[Bounties.Rank(Bounties.Total())];
            int pay = Mathf.RoundToInt(b.Coins * (1f + bonus / 100f));
            return bonus > 0 ? pay + " (+" + bonus + "%)" : pay.ToString();
        }

        private static readonly Dictionary<string, Sprite> IconCache = new Dictionary<string, Sprite>();

        private static Sprite IconOf(Bounty b)
        {
            string key = b.Kind + b.Target;
            if (IconCache.TryGetValue(key, out Sprite cached) && cached != null) return cached;
            Sprite sprite = null;
            if (b.Kind == Kind.Gather) sprite = ItemIcon(b.Target);
            else
                foreach (string candidate in new[] { "Trophy" + b.Target, "Trophy" + b.Target.Replace("_", ""), "Trophy" + b.Target.Split('_')[0], "TrophyBoar" })
                    if ((sprite = ItemIcon(candidate)) != null) break;
            IconCache[key] = sprite;
            return sprite;
        }

        private static Sprite ItemIcon(string prefab)
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

    /// <summary>The look of the menu: the same brown and gold as the other mods.</summary>
    internal static class Styles
    {
        internal static GUIStyle Title, Dim, Row, Good, Button, ButtonOn, Small, BarText;
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

        private static GUIStyle Make(Color fill, Color hover, Color border, int font)
        {
            var s = new GUIStyle(GUI.skin.button)
            {
                fontSize = font, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter,
                border = new RectOffset(2, 2, 2, 2), padding = new RectOffset(6, 6, 3, 3), margin = new RectOffset(3, 3, 3, 3),
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
            Button = Make(new Color(0.22f, 0.19f, 0.15f), new Color(0.33f, 0.27f, 0.18f), new Color(0.45f, 0.36f, 0.2f), 13);
            ButtonOn = Make(new Color(0.55f, 0.42f, 0.16f), new Color(0.62f, 0.48f, 0.2f), new Color(0.95f, 0.78f, 0.35f), 13);
            Small = Make(new Color(0.2f, 0.12f, 0.1f), new Color(0.35f, 0.16f, 0.12f), new Color(0.5f, 0.25f, 0.2f), 11);
        }

        internal static void Destroy()
        {
            foreach (Texture2D t in Textures) if (t != null) Object.Destroy(t);
            Textures.Clear();
            Title = Dim = Row = Good = Button = ButtonOn = Small = BarText = null;
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
