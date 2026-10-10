using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;

namespace BuildOrders
{
    /// <summary>
    /// The Plans window (F11), in the game's own look (GameUi.cs): your blueprint library (place one with a live preview, share it), the plans
    /// already placed (how far along, what they still need; build, move or remove them), and the settings that matter while building
    /// (build-all radius, reach, ghost limits...). It is built again whenever what it shows changes; its buttons only queue what they do, which
    /// runs in Update.
    /// </summary>
    public partial class Plugin
    {
        internal static bool PlansWindowOpen;
        private int _plansTab;
        private string _confirmKey;       // a removal waiting for its second click
        private float _confirmUntil;
        private int _confirmBuilt;      // pieces already built from the plan waiting on a Remove
        private float _removeRadius = 20f;
        private string _plansToast = "";
        private float _plansToastUntil;

        private const float PlansW = 880f, PlansH = 680f, PlansPad = 30f, PlansBodyTop = 142f, PlansBottom = 72f;
        private RectTransform _plansWin, _plansList;
        private string _plansShown = "";
        private bool _plansScrollTop;
        private float _plansCheckAt;
        private Action _plansPending;                                   // a click's action, run in Update (never while the window is built)
        private readonly List<Action> _plansLive = new List<Action>();   // labels kept up to date every frame (a slider's value)

        private class BlueprintFile
        {
            public string Path, Name;
            public int Pieces;
            public Vector3 Size;
            public List<KeyValuePair<string, int>> Materials = new List<KeyValuePair<string, int>>();
            public bool Placed;
            public string SharedBy;
            public Texture2D Picture;
            public string Error;
        }

        private List<BlueprintFile> _library = new List<BlueprintFile>();
        private float _libraryAt = -99f;
        private readonly Dictionary<string, Texture2D> _pictures = new Dictionary<string, Texture2D>();

        private void TogglePlansWindow()
        {
            if (Placing) { CancelPlacement(); return; }
            PlansWindowOpen = !PlansWindowOpen;
            if (PlansWindowOpen) { _libraryAt = -99f; if (InventoryGui.IsVisible()) InventoryGui.instance.Hide(); }
        }

        /// <summary>Open the window on a tab (0 blueprints, 1 placed plans, 2 settings), scrolled to the top.</summary>
        internal void ShowPlansTab(int tab)
        {
            _plansTab = tab;
            _plansScrollTop = true;
            _plansShown = "";
            _libraryAt = -99f;
            PlansWindowOpen = true;
        }

        internal static void ClosePlansWindowFromEscape()
        {
            if (Instance == null) return;
            if (Instance._placing != null) Instance.CancelPlacement();
            PlansWindowOpen = false;
        }

        /// <summary>What Escape does while one of the windows is open: the Plans window closes first, then the bridge options (cancelling the bridge).</summary>
        internal static void CloseFromEscape()
        {
            PlansPatches.EscapeFrame = Time.frameCount;
            if (PlansWindowOpen) { ClosePlansWindowFromEscape(); return; }
            if (BridgeOptionsOpen && Instance != null)
            {
                Instance.CancelBridge();
                Player.m_localPlayer?.Message(MessageHud.MessageType.TopLeft, "Bridge cancelled");
            }
        }

        /// <summary>Closes every BuildOrders window (Claude Tools' "shoot close" calls it before a screenshot): the Plans window, the bridge options, a placement.</summary>
        public static void Close()
        {
            ClosePlansWindowFromEscape();
            if (BridgeOptionsOpen) Instance?.CancelBridge();
        }

        private void PlansToast(string text) { _plansToast = text; _plansToastUntil = Time.unscaledTime + 4f; }

        // ---- the library: blueprint files in BepInEx/blueprints ----

        private void RefreshLibrary()
        {
            if (Time.unscaledTime - _libraryAt < 3f) return;
            _libraryAt = Time.unscaledTime;
            var list = new List<BlueprintFile>();
            var placedTitles = new HashSet<string>(_orders.Values.Select(o => o.By ?? "").Where(b => b.StartsWith(BlueprintPrefix)).Select(b => b.Substring(BlueprintPrefix.Length)));
            if (Directory.Exists(BlueprintDir))
                foreach (string file in Directory.GetFiles(BlueprintDir, "*.json").Where(f => !System.IO.Path.GetFileName(f).StartsWith("_")).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
                {
                    var bf = new BlueprintFile { Path = file, Name = System.IO.Path.GetFileNameWithoutExtension(file) };
                    try
                    {
                        JObject doc = JObject.Parse(File.ReadAllText(file));
                        bf.Name = (string)doc["name"] ?? bf.Name;
                        List<Entry> entries = EntriesFrom(doc["pieces"] as JArray ?? new JArray());
                        bf.Pieces = entries.Count;
                        if (entries.Count > 0)
                        {
                            Vector3 lo = entries[0].Local, hi = entries[0].Local;
                            foreach (Entry e in entries) { lo = Vector3.Min(lo, e.Local); hi = Vector3.Max(hi, e.Local); }
                            bf.Size = hi - lo + new Vector3(2f, 2f, 2f);
                        }
                        bf.Materials = MaterialsOf(entries.Select(e => e.Prefab));
                        bf.Placed = placedTitles.Any(t => t == bf.Name || t.StartsWith(bf.Name + " "));
                        bf.SharedBy = (string)doc["sharedBy"];
                        string pic = PictureFile(file);
                        if (pic != null) bf.Picture = Picture(pic); else WantPicture(file);   // none yet: one is made in a moment
                    }
                    catch (Exception e) { bf.Error = e.Message; }
                    list.Add(bf);
                }
            _library = list;
        }

        // ImageConversion.LoadImage has a span overload the compiler cannot resolve against this framework, so it is called by name.
        private static readonly System.Reflection.MethodInfo LoadImageMethod =
            typeof(ImageConversion).GetMethod("LoadImage", new[] { typeof(Texture2D), typeof(byte[]) });

        private Texture2D Picture(string path)
        {
            if (!File.Exists(path)) return null;
            if (_pictures.TryGetValue(path, out Texture2D cached) && cached != null) return cached;
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try { if (LoadImageMethod == null || !(bool)LoadImageMethod.Invoke(null, new object[] { tex, File.ReadAllBytes(path) })) { Destroy(tex); return null; } }
            catch (Exception) { Destroy(tex); return null; }
            _pictures[path] = tex;
            return tex;
        }

        /// <summary>What a set of pieces costs, most first (the game's own names, localized).</summary>
        private List<KeyValuePair<string, int>> MaterialsOf(IEnumerable<string> prefabs)
        {
            var totals = new Dictionary<string, int>();
            foreach (string name in prefabs)
            {
                GameObject prefab = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(name) : null;
                Piece piece = prefab != null ? prefab.GetComponent<Piece>() : null;
                if (piece == null) continue;
                foreach (Piece.Requirement req in piece.m_resources)
                {
                    if (req.m_resItem == null || req.m_amount <= 0) continue;
                    string item = req.m_resItem.m_itemData.m_shared.m_name;
                    totals[item] = (totals.TryGetValue(item, out int n) ? n : 0) + req.m_amount;
                }
            }
            return totals.OrderByDescending(kv => kv.Value).ToList();
        }

        private static readonly Dictionary<string, ItemDrop.ItemData> ItemsByName = new Dictionary<string, ItemDrop.ItemData>();
        private static ObjectDB _itemsFrom;

        /// <summary>An item by its game name ("$item_wood"), for its icon and tooltip; null if no item has it.</summary>
        internal static ItemDrop.ItemData ItemByName(string sharedName)
        {
            ObjectDB db = ObjectDB.instance;
            if (db == null || sharedName == null) return null;
            if (_itemsFrom != db || ItemsByName.Count == 0)
            {
                ItemsByName.Clear();
                _itemsFrom = db;
                foreach (GameObject go in db.m_items)
                {
                    ItemDrop drop = go != null ? go.GetComponent<ItemDrop>() : null;
                    if (drop != null && !ItemsByName.ContainsKey(drop.m_itemData.m_shared.m_name)) ItemsByName[drop.m_itemData.m_shared.m_name] = drop.m_itemData;
                }
            }
            return ItemsByName.TryGetValue(sharedName, out ItemDrop.ItemData data) ? data : null;
        }

        /// <summary>A material in a line: its icon (the game's tooltip on it) and an amount, or its name and the amount when it has no item. Returns the width.</summary>
        private static float Cost(Transform parent, Vector2 pos, string sharedName, string amount, Color color, float size = 24f, float fontSize = 15f)
        {
            ItemDrop.ItemData item = ItemByName(sharedName);
            if (item != null) return GameUi.Chip(parent, pos, item, amount, size, fontSize, color);
            string text = Localization.instance.Localize(sharedName) + " " + amount;
            TMP_Text t = GameUi.Line(parent, text, new Vector2(pos.x, pos.y - (size - fontSize * 1.3f) / 2f), 200f, fontSize, color);
            float w = GameUi.Width(t, text);
            ((RectTransform)t.transform).sizeDelta = new Vector2(w + 2f, size);
            return w + 14f;
        }

        // ---- the window ----------------------------------------------------------------------------------------------------------

        private void DestroyPlansWindow()
        {
            if (_plansWin != null) Destroy(_plansWin.gameObject);
            _plansWin = null; _plansList = null;
            _plansLive.Clear();
            _plansShown = "";
            _plansPending = null;
            UITooltip.HideTooltip();
        }

        /// <summary>Every frame (from Update): open or close the window as PlansWindowOpen says, run a click's action, rebuild it when what it shows changed.</summary>
        private void TickPlansWindow()
        {
            if (!PlansWindowOpen) { if (_plansWin != null || _plansPending != null) DestroyPlansWindow(); return; }
            Player player = Player.m_localPlayer;
            if (player == null || player.IsDead()) { PlansWindowOpen = false; DestroyPlansWindow(); return; }
            bool fresh = _plansWin == null;
            if (fresh)
            {
                if (!GameUi.Ready())
                {
                    PlansWindowOpen = false;
                    player.Message(MessageHud.MessageType.TopLeft, "The Plans window could not open: the game's interface was not found where expected (see the log)");
                    return;
                }
                if (InventoryGui.IsVisible()) InventoryGui.instance.Hide();
            }
            if (_plansPending != null)
            {
                Action act = _plansPending;
                _plansPending = null;
                act();
                if (!PlansWindowOpen) { DestroyPlansWindow(); return; }
                _plansCheckAt = 0f;   // show what it did at once
            }
            foreach (Action live in _plansLive) live();
            if (!fresh && Time.unscaledTime < _plansCheckAt) return;
            _plansCheckAt = Time.unscaledTime + 0.25f;
            List<PlanInfo> plans = Plans(player.transform.position);
            string signature = PlansSignature(player, plans);
            if (fresh || signature != _plansShown) BuildPlansWindow(player, plans, signature);
        }

        private bool Toasting => Time.unscaledTime < _plansToastUntil;
        private bool Confirming(string key) => _confirmKey == key && Time.unscaledTime < _confirmUntil;
        private void AskConfirm(string key) { _confirmKey = key; _confirmUntil = Time.unscaledTime + 4f; }

        /// <summary>Everything the window shows, so it is built again only when something changed.</summary>
        private string PlansSignature(Player player, List<PlanInfo> plans)
        {
            var sb = new StringBuilder();
            sb.Append(_plansTab).Append('|').Append(Toasting ? _plansToast : "").Append('|').Append(Time.unscaledTime < _confirmUntil ? _confirmKey + _confirmBuilt : "");
            sb.Append('|').Append(plans.Count).Append('|').Append(_buildAllRadius.Value).Append('|').Append(_blueprintKey.Value).Append(_planKey.Value);
            if (_plansTab == 0)
            {
                RefreshLibrary();
                RefreshInbox();
                foreach (SharedBlueprint s in _inbox) sb.Append("|S").Append(s.Path).Append(s.Pieces).Append(s.Picture != null);
                foreach (BlueprintFile bf in _library)
                    sb.Append("|B").Append(bf.Path).Append(',').Append(bf.Name).Append(bf.Pieces).Append(bf.Placed).Append(bf.SharedBy).Append(bf.Error).Append(bf.Picture != null)
                      .Append(string.Join(",", bf.Materials.Take(5).Select(kv => kv.Key + kv.Value).ToArray()));
            }
            else if (_plansTab == 1)
            {
                Inventory inv = player.GetInventory();
                foreach (LevelJob j in _levelJobs.Values.Where(j => j.OnDone != null && !j.Finished)) sb.Append("|L").Append(j.Key).Append(j.Status).Append(j.Pass);
                foreach (PlanInfo plan in plans)
                {
                    sb.Append("|P").Append(plan.Key).Append(',').Append(plan.Orders.Count).Append(',').Append(Mathf.RoundToInt(plan.Distance)).Append(',').Append(Compass(plan.Centre - player.transform.position));
                    sb.Append(plan.IsBlueprint && IsBridgePlan(plan.Title)).Append(IsAddonGhostPlan(plan.Key));
                    foreach (var kv in MaterialsOf(plan.Orders.Select(o => o.Prefab)).Take(4)) sb.Append(kv.Key).Append(inv.CountItems(kv.Key)).Append('/').Append(kv.Value);
                    if (_levelJobs.TryGetValue(plan.Key, out LevelJob job)) sb.Append("|J").Append(job.Running).Append(job.Next).Append('/').Append(job.Points.Count).Append(job.Status);
                }
            }
            else sb.Append('|').Append(_showGhosts.Value).Append(_buildByHand.Value).Append(ClaudeToolsInstalled).Append(_selectKey.Value).Append(_removeKey.Value).Append(_toggleGhostsKey.Value).Append(_stabilityKey.Value);
            return sb.ToString();
        }

        private void BuildPlansWindow(Player player, List<PlanInfo> plans, string signature)
        {
            float scrolled = !_plansScrollTop && _plansList != null ? _plansList.anchoredPosition.y : 0f;
            _plansScrollTop = false;
            if (_plansWin != null) Destroy(_plansWin.gameObject);
            _plansList = null;
            _plansLive.Clear();
            UITooltip.HideTooltip();
            _plansShown = signature;

            _plansWin = GameUi.Window("BuildOrdersPlans", new Vector2(PlansW, PlansH));
            Transform w = _plansWin;
            GameUi.Label(w, "Plans and blueprints", new Vector2(PlansPad, -20f), new Vector2(420f, 44f), 34f, GameUi.Warm, GameUi.Norse);
            GameUi.Button(w, "Close", new Vector2(PlansW - PlansPad - 110f, -24f), new Vector2(110f, 38f), () => _plansPending = () => PlansWindowOpen = false);
            GameUi.Braid(w, new Vector2(0f, -76f), PlansW - PlansPad * 2f);

            string[] tabs = { "Blueprints", $"Placed plans ({plans.Count})", "Settings" };
            float tabW = (PlansW - PlansPad * 2f - 16f) / 3f;
            for (int i = 0; i < tabs.Length; i++)
            {
                int tab = i;
                GameUi.Tab(w, tabs[i], new Vector2(PlansPad + i * (tabW + 8f), -90f), new Vector2(tabW, 40f), _plansTab == i, () => _plansPending = () => { _plansTab = tab; _plansScrollTop = true; });
            }

            if (_plansTab == 0) LibraryTab(w, player);
            else if (_plansTab == 1) PlacedTab(w, player, plans);
            else SettingsTab(w);

            // bottom: what just happened, or the keys
            GameUi.Label(w, Toasting ? _plansToast : $"{_planKey.Value}: plan pieces by hand with the hammer   ·   Hold E at a ghost: build everything within {_buildAllRadius.Value:0} m   ·   {_blueprintKey.Value}: this window",
                new Vector2(PlansPad, -(PlansH - 62f)), new Vector2(PlansW - PlansPad * 2f, 44f), Toasting ? 17f : 15f, Toasting ? GameUi.Good : GameUi.Dim);

            if (_plansList != null) _plansList.anchoredPosition = new Vector2(0f, Mathf.Clamp(scrolled, 0f, Mathf.Max(0f, _plansList.sizeDelta.y)));
            foreach (Action live in _plansLive) live();   // (their labels start empty)
        }

        /// <summary>The scrolling list under the tab's own controls, from <paramref name="top"/> down to the bottom line; returns the width a card has.</summary>
        private float PlansList(Transform w, float top)
        {
            RectTransform area = GameUi.Rect(w, "List", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(PlansPad - 6f, -top),
                                             new Vector2(PlansW - PlansPad * 2f + 12f, PlansH - top - PlansBottom));
            _plansList = GameUi.Scroll(area, 6f);
            return _plansList.sizeDelta.x - 6f;
        }

        /// <summary>A note in the list; returns its height.</summary>
        private float ListNote(float y, float width, string text, Color color, float size = 18f)
        {
            TMP_Text t = GameUi.Label(_plansList, text, new Vector2(8f, -y - 4f), new Vector2(width - 16f, 30f), size, color);
            return t.GetPreferredValues(text, width - 16f, 0f).y + 12f;
        }

        // ---- Blueprints ----

        private void LibraryTab(Transform w, Player player)
        {
            RefreshLibrary();
            RefreshInbox();
            GameUi.Label(w, "Blueprints in <color=#ffd27a>BepInEx/blueprints</color>. Place one: a green preview follows where you look; turn it with the mouse wheel and click to place. " +
                            "Share sends one to everyone playing here; a share code is the same as text for a chat.",
                new Vector2(PlansPad, -PlansBodyTop), new Vector2(PlansW - PlansPad * 2f - 272f, 62f), 15f, GameUi.Dim);
            GameUi.Button(w, "Paste code", new Vector2(PlansW - PlansPad - 262f, -PlansBodyTop - 2f), new Vector2(126f, 38f), () => _plansPending = PasteCode);
            GameUi.Button(w, "Open folder", new Vector2(PlansW - PlansPad - 128f, -PlansBodyTop - 2f), new Vector2(128f, 38f), () => _plansPending = () =>
            {
                Directory.CreateDirectory(BlueprintDir);
                Application.OpenURL("file:///" + BlueprintDir.Replace(Path.DirectorySeparatorChar, '/'));
            });

            float cardW = PlansList(w, PlansBodyTop + 70f), y = 0f;
            const float gap = 8f;

            // blueprints other players shared with you
            if (_inbox.Count > 0)
            {
                GameUi.Line(_plansList, $"Shared with you ({_inbox.Count})", new Vector2(8f, -y - 2f), cardW - 16f, 18f, GameUi.Warm);
                y += 30f;
                foreach (SharedBlueprint s in _inbox.ToList())
                {
                    SharedBlueprint shared = s;
                    const float h = 80f;
                    RectTransform card = GameUi.Box(_plansList, new Vector2(0f, -y), new Vector2(cardW, h));
                    PictureOrNone(card, new Vector2(10f, -8f), new Vector2(112f, 64f), s.Picture);
                    float x = 134f, tw = cardW - x - 372f;
                    GameUi.Line(card, $"{s.Name}   <color=#a8d8a0>from {s.From}</color>", new Vector2(x, -10f), tw, 18f, GameUi.Text);
                    string pieces = $"{s.Pieces} pieces";
                    TMP_Text pl = GameUi.Line(card, pieces, new Vector2(x, -42f), tw, 15f, GameUi.Dim);
                    float cx = x + GameUi.Width(pl, pieces) + 14f;
                    foreach (var kv in s.Materials.Take(4))
                    {
                        if (cx > x + tw - 60f) break;
                        cx += Cost(card, new Vector2(cx, -38f), kv.Key, kv.Value.ToString(), GameUi.Text);
                    }
                    var place = GameUi.Button(card, "Place", new Vector2(cardW - 364f, -21f), new Vector2(100f, 38f), () => _plansPending = () => PlaceShared(player, shared));
                    GameUi.Label(place).color = GameUi.Gold;
                    GameUi.Button(card, "Save to library", new Vector2(cardW - 256f, -21f), new Vector2(140f, 38f), () => _plansPending = () => SaveShared(shared), true, 16f);
                    GameUi.Button(card, "Dismiss", new Vector2(cardW - 108f, -21f), new Vector2(96f, 38f), () => _plansPending = () => DismissShared(shared), true, 16f);
                    y += h + gap;
                }
                y += 8f;
            }

            if (_library.Count == 0) y += ListNote(y, cardW, "No blueprints yet. Put blueprint files in the folder (Open folder), or paste a share code.", GameUi.Text);
            foreach (BlueprintFile b in _library)
            {
                BlueprintFile bf = b;
                const float h = 106f;
                RectTransform card = GameUi.Box(_plansList, new Vector2(0f, -y), new Vector2(cardW, h));
                PictureOrNone(card, new Vector2(10f, -11f), new Vector2(150f, 84f), bf.Picture);
                float x = 172f, tw = cardW - x - 158f;
                GameUi.Line(card, bf.Name + (bf.SharedBy != null ? $"   <color=#a8d8a0>from {bf.SharedBy}</color>" : "") + (bf.Placed ? "   <color=#8fe08f>(placed)</color>" : ""),
                    new Vector2(x, -10f), tw, 19f, GameUi.Text);
                if (bf.Error != null) GameUi.Label(card, "Could not read: " + bf.Error, new Vector2(x, -40f), new Vector2(tw, 56f), 15f, GameUi.Warn);
                else
                {
                    GameUi.Line(card, $"{bf.Pieces} pieces   ·   about {bf.Size.x:0} × {bf.Size.z:0} m, {bf.Size.y:0} m high", new Vector2(x, -40f), tw, 15f, GameUi.Dim);
                    float cx = x;
                    foreach (var kv in bf.Materials.Take(5))
                    {
                        if (cx > x + tw - 60f) break;
                        cx += Cost(card, new Vector2(cx, -68f), kv.Key, kv.Value.ToString(), GameUi.Text, 26f);
                    }

                    float bx = cardW - 142f;
                    var place = GameUi.Button(card, "Place", new Vector2(bx, -10f), new Vector2(130f, 40f), () => _plansPending = () => PlaceBlueprint(player, bf));
                    GameUi.Label(place).color = GameUi.Gold;
                    string key = "share:" + bf.Path;
                    bool sending = Confirming(key);
                    var share = GameUi.Button(card, sending ? "Send?" : "Share", new Vector2(bx, -58f), new Vector2(64f, 36f), () => _plansPending = () =>
                    {
                        if (Confirming(key)) { ShareWithEveryone(bf.Path); _confirmKey = null; } else AskConfirm(key);
                    }, true, 16f);
                    if (sending) GameUi.Label(share).color = GameUi.Warn;
                    GameUi.Button(card, "Code", new Vector2(bx + 68f, -58f), new Vector2(62f, 36f), () => _plansPending = () => CopyCode(bf.Path), true, 16f);
                }
                y += h + gap;
            }
            _plansList.sizeDelta = new Vector2(_plansList.sizeDelta.x, Mathf.Max(0f, y - gap));
        }

        private static void PictureOrNone(Transform card, Vector2 pos, Vector2 size, Texture2D picture)
        {
            if (picture != null) { GameUi.Picture(card, pos, size, picture); return; }
            RectTransform none = GameUi.Box(card, pos, size);
            GameUi.Label(none, "no picture", Vector2.zero, size, 14f, GameUi.Dim, null, TextAlignmentOptions.Center, new Vector2(0.5f, 0.5f));
        }

        private void PlaceBlueprint(Player player, BlueprintFile bf)
        {
            try
            {
                JObject doc = JObject.Parse(File.ReadAllText(bf.Path));
                StartPlacement(bf.Name, bf.Path, EntriesFrom(doc["pieces"] as JArray ?? new JArray()), player.transform.eulerAngles.y, 0f, null);
            }
            catch (Exception e) { PlansToast("Could not read " + bf.Name + ": " + e.Message); }
        }

        // ---- Placed plans ----

        private void PlacedTab(Transform w, Player player, List<PlanInfo> plans)
        {
            Vector3 me = player.transform.position;
            float top = PlansBodyTop + 4f;

            // remove every ghost within a distance of you (the slider and the count follow each other live)
            int Inside() => _orders.Values.Count(o => (o.Pos - me).sqrMagnitude <= _removeRadius * _removeRadius);
            TMP_Text radiusLabel = GameUi.Line(w, "", new Vector2(PlansPad, -top - 8f), 290f, 17f, GameUi.Text);
            GameUi.Slider(w, new Vector2(PlansPad + 296f, -top - 4f), new Vector2(230f, 28f), 2f, 150f, _removeRadius, 1f, v => _removeRadius = v);
            bool confirming = Confirming("radius");
            var remove = GameUi.Button(w, "", new Vector2(PlansW - PlansPad - 250f, -top), new Vector2(250f, 38f), () => _plansPending = () =>
            {
                int inside = Inside();
                if (inside <= 0) return;
                if (Confirming("radius")) { int n = RemoveWithin(me, _removeRadius); PlansToast($"Removed {n} ghost(s)"); _confirmKey = null; }
                else AskConfirm("radius");
            }, true, 17f);
            TMP_Text removeLabel = GameUi.Label(remove);
            if (confirming) removeLabel.color = GameUi.Warn;
            float shownRadius = -1f;
            int shownCount = -1;
            _plansLive.Add(() =>
            {
                int inside = Inside();
                if (shownRadius == _removeRadius && shownCount == inside) return;
                shownRadius = _removeRadius; shownCount = inside;
                radiusLabel.text = $"Remove every ghost within <color=#ffd27a>{_removeRadius:0} m</color>:";
                removeLabel.text = confirming ? $"Click again: remove {inside}" : $"Remove ({inside})";
            });
            top += 52f;

            // plans still waiting for their ground to be levelled (their ghosts come afterwards)
            foreach (LevelJob p in _levelJobs.Values.Where(j => j.OnDone != null && !j.Finished).ToList())
            {
                LevelJob pending = p;
                GameUi.Label(w, $"<color=#ffd27a>{pending.Title}</color>: levelling the ground ({pending.Status}, pass {pending.Pass}); its ghosts appear when it is done",
                    new Vector2(PlansPad, -top), new Vector2(PlansW - PlansPad * 2f - 132f, 40f), 16f, GameUi.Text);
                var cancel = GameUi.Button(w, "Cancel", new Vector2(PlansW - PlansPad - 120f, -top), new Vector2(120f, 36f), () => _plansPending = () =>
                {
                    StopLevel(pending.Key);
                    _levelJobs.Remove(pending.Key);
                    ForgetPendingLevel(pending.Key);
                    string ground = RestoreTerrain(pending.Key);
                    PlansToast($"Cancelled \"{pending.Title}\"" + (ground != null ? ". " + ground : ""));
                }, true, 16f);
                GameUi.Label(cancel).color = GameUi.Warn;
                top += 46f;
            }

            float cardW = PlansList(w, top), y = 0f;
            const float gap = 8f, h = 100f;
            if (plans.Count == 0)
            {
                y += ListNote(y, cardW, "Nothing is planned. Place a blueprint, or plan pieces by hand with the hammer.", GameUi.Text);
                _plansList.sizeDelta = new Vector2(_plansList.sizeDelta.x, y);
                return;
            }

            Inventory inv = player.GetInventory();
            foreach (PlanInfo p in plans)
            {
                PlanInfo plan = p;
                RectTransform card = GameUi.Box(_plansList, new Vector2(0f, -y), new Vector2(cardW, h));
                float bx = cardW - 12f - 346f, tw = bx - 24f;
                Vector3 dir = plan.Centre - me; dir.y = 0f;
                GameUi.Line(card, plan.Title, new Vector2(12f, -10f), tw, 19f, plan.IsBlueprint ? GameUi.Text : GameUi.Warm);
                GameUi.Line(card, $"{plan.Orders.Count} pieces left   ·   {plan.Distance:0} m away ({Compass(dir)})", new Vector2(12f, -38f), tw, 15f, GameUi.Dim);
                float cx = 12f;
                foreach (var kv in MaterialsOf(plan.Orders.Select(o => o.Prefab)).Take(4))
                {
                    if (cx > tw - 60f) break;
                    int have = inv.CountItems(kv.Key);
                    cx += Cost(card, new Vector2(cx, -64f), kv.Key, $"{have}/{kv.Value}", have >= kv.Value ? GameUi.Good : GameUi.Warn);
                }

                // its buttons: build, move, remove; under them its levelling
                bool near = plan.Distance <= _buildAllRadius.Value;
                bool bridge = plan.IsBlueprint && IsBridgePlan(plan.Title);
                bool addon = IsAddonGhostPlan(plan.Key);
                string key = "plan:" + plan.Key;
                if (Confirming(key) && _confirmBuilt > 0)
                {
                    // part of it is built: remove just the ghosts, or take the built part down too (materials back)
                    GameUi.Button(card, "Remove ghosts only", new Vector2(bx, -10f), new Vector2(170f, 36f), () => _plansPending = () =>
                    { int n = RemovePlan(plan.Key); PlansToast($"Removed \"{plan.Title}\" ({n} ghosts); what is built stays"); _confirmKey = null; }, true, 15f);
                    int built = _confirmBuilt;
                    var down = GameUi.Button(card, $"Also take down {built} built", new Vector2(bx + 174f, -10f), new Vector2(172f, 36f), () => _plansPending = () =>
                    { int n = RemovePlan(plan.Key, takeDownBuilt: true); PlansToast($"Removed \"{plan.Title}\" ({n} ghosts) and took down what was built"); _confirmKey = null; }, true, 15f);
                    GameUi.Label(down).color = GameUi.Warn;
                }
                else
                {
                    var build = GameUi.Button(card, near ? "Build nearby" : "Too far", new Vector2(bx, -10f), new Vector2(120f, 36f), () => _plansPending = () =>
                    { PlansWindowOpen = false; StartCoroutine(BuildMany(player)); }, near, 16f);
                    if (near) GameUi.Label(build).color = GameUi.Gold;
                    // shapes and bridges should not go through blueprint terrain levelling
                    GameUi.Button(card, addon ? "(shape)" : bridge ? "(bridge)" : "Move", new Vector2(bx + 124f, -10f), new Vector2(100f, 36f),
                        () => _plansPending = () => StartMove(player, plan), !bridge && !addon, 16f);
                    bool sure = Confirming(key);
                    var rem = GameUi.Button(card, sure ? "Click again" : "Remove", new Vector2(bx + 228f, -10f), new Vector2(118f, 36f), () => _plansPending = () =>
                    {
                        if (!Confirming(key)) { AskConfirm(key); _confirmBuilt = plan.IsBlueprint ? BuiltPieces(plan.Key).Count : 0; }
                        else { int n = RemovePlan(plan.Key); PlansToast($"Removed \"{plan.Title}\" ({n} ghosts)"); _confirmKey = null; }
                    }, true, 16f);
                    if (sure) GameUi.Label(rem).color = GameUi.Warn;
                }
                if (_levelJobs.TryGetValue(plan.Key, out LevelJob job) && (job.Running || job.Next < job.Points.Count))
                {
                    GameUi.Label(card, $"<color=#ffd27a>Levelling {job.Next}/{job.Points.Count}: {job.Status}</color>", new Vector2(bx, -52f), new Vector2(220f, 40f), 14f, GameUi.Dim);
                    bool running = job.Running;
                    GameUi.Button(card, running ? "Stop levelling" : "Resume", new Vector2(bx + 228f, -52f), new Vector2(118f, 34f), () => _plansPending = () =>
                    { if (running) StopLevel(plan.Key); else ResumeLevel(plan.Key); }, true, 15f);
                }
                else
                {
                    bool levelable = plan.IsBlueprint && !bridge && !addon;
                    GameUi.Label(card, near ? $"Builds the ghosts within {_buildAllRadius.Value:0} m you have materials for." : $"Walk within {_buildAllRadius.Value:0} m to build it.",
                        new Vector2(bx, -52f), new Vector2(levelable ? 220f : 346f, 40f), 14f, GameUi.Dim);
                    if (levelable)
                        GameUi.Button(card, "Level ground", new Vector2(bx + 228f, -52f), new Vector2(118f, 34f), () => _plansPending = () =>
                            PlansToast($"Levelling the ground under \"{plan.Title}\" ({LevelPlacedPlan(plan)} spots)"), true, 15f);
                }
                y += h + gap;
            }
            _plansList.sizeDelta = new Vector2(_plansList.sizeDelta.x, Mathf.Max(0f, y - gap));
        }

        private static string Compass(Vector3 dir)
        {
            dir.y = 0f;
            if (dir.sqrMagnitude < 1f) return "here";
            float a = Mathf.Repeat(Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg, 360f);
            string[] names = { "north", "north-east", "east", "south-east", "south", "south-west", "west", "north-west" };
            return names[Mathf.RoundToInt(a / 45f) % 8];
        }

        // ---- Settings ----

        private void SettingsTab(Transform w)
        {
            float cardW = PlansList(w, PlansBodyTop), y = 4f;
            y = SettingSlider(y, cardW, "Build-all radius", "Holding E at a ghost, or Build nearby, builds every ghost this close to you.", _buildAllRadius, 4f, 64f, 1f, "0", " m");
            y = SettingSlider(y, cardW, "Reach for E", "How close you must be to a ghost to build it with E.", _buildReach, 2f, 10f, 0.1f, "0.0", " m");
            y = SettingSlider(y, cardW, "Ghost view distance", "Ghosts further away than this are not drawn.", _viewDistance, 20f, 200f, 1f, "0", " m");
            y = SettingSlider(y, cardW, "Ghost opacity", "How solid the ghosts look.", _ghostOpacity, 0.05f, 0.6f, 0.01f, "0.00", "");
            y = SettingSlider(y, cardW, "Most ghosts at once", "Big plans need more; lower it if the game slows down.", null, 50f, 2000f, 10f, "0", "", _maxGhosts);
            y += 6f;
            y = SettingToggle(y, cardW, _showGhosts, "Show ghosts", $"Hide them all for a moment (also {_toggleGhostsKey.Value}).");
            y = SettingToggle(y, cardW, _buildByHand, "Build by pressing E", "Walk up to a ghost and press E to build it, no hammer needed.");
            y += 6f;
            y += ListNote(y, cardW, ClaudeToolsInstalled ? "Claude Tools is installed: an AI assistant can see your game and place blueprints for you while its requests are on (Claude Tools settings)."
                                                         : "Install the Claude Tools mod to let an AI assistant (Claude Code in BepInEx/blueprints) see your game and place blueprints for you.", GameUi.Dim, 15f);
            y += ListNote(y, cardW, $"Keys: {_blueprintKey.Value} this window   ·   {_planKey.Value} plan mode (hammer)   ·   {_selectKey.Value} select an aimed ghost's piece   ·   {_removeKey.Value} remove an aimed ghost (Shift: all within 8 m)   ·   " +
                                    $"{_toggleGhostsKey.Value} show/hide ghosts   ·   {_stabilityKey.Value} stability colours", GameUi.Dim, 15f);
            _plansList.sizeDelta = new Vector2(_plansList.sizeDelta.x, y);
        }

        /// <summary>A setting with the game's slider: its name and value, the slider beside it, what it does under them. Returns the next row's y.</summary>
        private float SettingSlider(float y, float width, string label, string help, BepInEx.Configuration.ConfigEntry<float> entry, float min, float max, float step,
                                    string format, string unit, BepInEx.Configuration.ConfigEntry<int> intEntry = null)
        {
            float Get() => entry != null ? entry.Value : intEntry.Value;
            void Set(float v) { if (entry != null) entry.Value = v; else intEntry.Value = Mathf.RoundToInt(v); }
            string Text(float v) => $"{label}: <color=#ffd27a>{v.ToString(format)}{unit}</color>";

            TMP_Text name = GameUi.Line(_plansList, Text(Get()), new Vector2(8f, -y), 330f, 18f, GameUi.Text);
            UnityEngine.UI.Slider slider = GameUi.Slider(_plansList, new Vector2(350f, -y + 1f), new Vector2(Mathf.Min(340f, width - 370f), 26f), min, max, Get(), step, v =>
            {
                if (Mathf.Abs(v - Get()) > 1e-4f) Set(v);
                name.text = Text(Get());
            });
            // a change from elsewhere (the server's value, the config file) shows too, unless the slider is being dragged
            _plansLive.Add(() =>
            {
                if (Input.GetMouseButton(0)) return;
                float now = Get();
                if (slider != null && Mathf.Abs(slider.value - now) > 1e-4f) slider.SetValueWithoutNotify(now);
                string text = Text(now);
                if (name.text != text) name.text = text;
            });
            TMP_Text note = GameUi.Label(_plansList, help, new Vector2(8f, -y - 30f), new Vector2(width - 16f, 24f), 15f, GameUi.Dim);
            return y + 30f + note.GetPreferredValues(help, width - 16f, 0f).y + 14f;
        }

        /// <summary>An on/off setting: a button that says its state (gold when on), and what it does beside it.</summary>
        private float SettingToggle(float y, float width, BepInEx.Configuration.ConfigEntry<bool> entry, string label, string help)
        {
            var b = GameUi.Button(_plansList, $"{label}: {(entry.Value ? "On" : "Off")}", new Vector2(8f, -y), new Vector2(260f, 38f), () => _plansPending = () => entry.Value = !entry.Value, true, 16f);
            if (entry.Value) GameUi.Label(b).color = GameUi.Gold;
            GameUi.Label(_plansList, help, new Vector2(282f, -y - 2f), new Vector2(width - 290f, 40f), 15f, GameUi.Dim);
            return y + 48f;
        }
    }
}
