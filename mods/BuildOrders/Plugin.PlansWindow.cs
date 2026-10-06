using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace BuildOrders
{
    /// <summary>
    /// The Plans window (F11): your blueprint library (place one with a live preview), the plans already placed (how far along, what they
    /// still need; build, move or remove them), and the settings that matter while building (build-all radius, reach, ghost limits...).
    /// </summary>
    public partial class Plugin
    {
        internal static bool PlansWindowOpen;
        private int _plansTab;
        private Vector2 _plansScroll;
        private Rect _plansRect;
        private bool _plansPlaced;
        private string _confirmKey;       // a removal waiting for its second click
        private float _confirmUntil;
        private int _confirmBuilt;      // pieces already built from the plan waiting on a Remove
        private float _removeRadius = 20f;
        private string _plansToast = "";
        private float _plansToastUntil;

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

        internal static void ClosePlansWindowFromEscape()
        {
            if (Instance == null) return;
            if (Instance._placing != null) Instance.CancelPlacement();
            PlansWindowOpen = false;
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

        // ---- drawing ----

        private GUIStyle _wTitle, _wText, _wDim, _wBold, _wButton, _wButtonOn, _wButtonGood, _wButtonBad, _wToggle;
        private readonly List<Texture2D> _wTextures = new List<Texture2D>();

        private Texture2D Box(Color fill, Color border)
        {
            const int size = 6;
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Point };
            for (int x = 0; x < size; x++)
                for (int y = 0; y < size; y++)
                    t.SetPixel(x, y, (x < 2 || y < 2 || x >= size - 2 || y >= size - 2) ? border : fill);
            t.Apply();
            _wTextures.Add(t);
            return t;
        }

        private GUIStyle MakeButton(Color fill, Color hover, Color border)
        {
            var s = new GUIStyle(GUI.skin.button)
            {
                fontSize = 13, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter,
                border = new RectOffset(2, 2, 2, 2), padding = new RectOffset(8, 8, 3, 3), margin = new RectOffset(3, 3, 3, 3),
            };
            s.normal.background = Box(fill, border);
            s.hover.background = s.active.background = s.focused.background = Box(hover, border);
            s.normal.textColor = s.hover.textColor = s.active.textColor = s.focused.textColor = new Color(0.95f, 0.92f, 0.85f);
            return s;
        }

        private void EnsureWindowStyles()
        {
            if (_wTitle != null) return;
            _wTitle = new GUIStyle(GUI.skin.label) { fontSize = 20, fontStyle = FontStyle.Bold };
            _wTitle.normal.textColor = Cyan;
            _wText = new GUIStyle(GUI.skin.label) { fontSize = 14, wordWrap = false, clipping = TextClipping.Clip, richText = true };
            _wText.normal.textColor = new Color(0.94f, 0.92f, 0.88f);
            _wBold = new GUIStyle(_wText) { fontStyle = FontStyle.Bold, fontSize = 15 };
            _wDim = new GUIStyle(GUI.skin.label) { fontSize = 12, wordWrap = true, richText = true };
            _wDim.normal.textColor = new Color(0.7f, 0.72f, 0.74f);
            _wButton = MakeButton(new Color(0.14f, 0.2f, 0.24f), new Color(0.2f, 0.3f, 0.36f), new Color(0.3f, 0.5f, 0.6f));
            _wButtonOn = MakeButton(new Color(0.2f, 0.42f, 0.52f), new Color(0.26f, 0.5f, 0.6f), Cyan);
            _wButtonGood = MakeButton(new Color(0.16f, 0.36f, 0.2f), new Color(0.22f, 0.46f, 0.26f), new Color(0.5f, 0.85f, 0.55f));
            _wButtonBad = MakeButton(new Color(0.4f, 0.14f, 0.12f), new Color(0.52f, 0.18f, 0.15f), new Color(0.9f, 0.45f, 0.4f));
            _wToggle = new GUIStyle(GUI.skin.toggle) { fontSize = 14 };
            _wToggle.normal.textColor = _wToggle.onNormal.textColor = _wToggle.hover.textColor = _wToggle.onHover.textColor = new Color(0.94f, 0.92f, 0.88f);
        }

        private void DestroyWindowResources()
        {
            foreach (Texture2D t in _wTextures) if (t != null) Destroy(t);
            _wTextures.Clear();
            foreach (Texture2D t in _pictures.Values) if (t != null) Destroy(t);
            _pictures.Clear();
            _wTitle = null;
        }

        private void DrawPlansWindow()
        {
            if (!PlansWindowOpen) return;
            if (Cursor.lockState != CursorLockMode.None) Cursor.lockState = CursorLockMode.None;
            if (!Cursor.visible) Cursor.visible = true;
            EnsureWindowStyles();

            Matrix4x4 saved = GUI.matrix;
            float s = Mathf.Max(0.8f, Screen.height / 1080f);
            GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f));
            float sw = Screen.width / s, sh = Screen.height / s;
            float w = Mathf.Min(820f, sw - 40f), h = Mathf.Min(700f, sh - 60f);
            if (!_plansPlaced) { _plansRect = new Rect((sw - w) / 2f, (sh - h) / 2f, w, h); _plansPlaced = true; }
            _plansRect.width = w; _plansRect.height = h;
            _plansRect = GUI.Window(8871, _plansRect, PlansContents, GUIContent.none, GUIStyle.none);
            _plansRect.x = Mathf.Clamp(_plansRect.x, 0f, Mathf.Max(0f, sw - w));
            _plansRect.y = Mathf.Clamp(_plansRect.y, 0f, Mathf.Max(0f, sh - h));
            GUI.matrix = saved;
        }

        private void PlansContents(int id)
        {
            float w = _plansRect.width, h = _plansRect.height;
            Round(new Rect(0, 0, w, h), new Color(0.05f, 0.07f, 0.08f, 0.97f), 10f);
            Round(new Rect(0, 0, w, h), new Color(0.05f, 0.07f, 0.08f, 0.9f), 10f); // twice: the game's scene shows through one layer
            Outline(new Rect(0, 0, w, h), new Color(Cyan.r, Cyan.g, Cyan.b, 0.8f), 10f);
            Player player = Player.m_localPlayer;
            if (player == null) return;

            GUILayout.BeginArea(new Rect(18f, 12f, w - 36f, h - 24f));
            GUILayout.BeginHorizontal();
            GUILayout.Label("Plans and blueprints", _wTitle);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Close", _wButton, GUILayout.Width(80), GUILayout.Height(28))) PlansWindowOpen = false;
            GUILayout.EndHorizontal();
            GUILayout.Space(4);

            List<PlanInfo> plans = Plans(player.transform.position);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Blueprints", _plansTab == 0 ? _wButtonOn : _wButton, GUILayout.Height(32))) { _plansTab = 0; _plansScroll = Vector2.zero; }
            if (GUILayout.Button($"Placed plans ({plans.Count})", _plansTab == 1 ? _wButtonOn : _wButton, GUILayout.Height(32))) { _plansTab = 1; _plansScroll = Vector2.zero; }
            if (GUILayout.Button("Settings", _plansTab == 2 ? _wButtonOn : _wButton, GUILayout.Height(32))) { _plansTab = 2; _plansScroll = Vector2.zero; }
            GUILayout.EndHorizontal();
            GUILayout.Space(6);

            if (_plansTab == 0) DrawLibrary(player);
            else if (_plansTab == 1) DrawPlacedPlans(player, plans);
            else DrawPlanSettings();

            GUILayout.Space(4);
            bool toast = Time.unscaledTime < _plansToastUntil;
            GUILayout.Label(toast ? _plansToast : $"{_planKey.Value}: plan pieces by hand with the hammer   ·   Hold E at a ghost: build everything within {_buildAllRadius.Value:0} m   ·   {_blueprintKey.Value}: this window", toast ? _wBold : _wDim);
            GUILayout.EndArea();
            GUI.DragWindow(new Rect(0, 0, w, 44));
        }

        private bool Confirming(string key) => _confirmKey == key && Time.unscaledTime < _confirmUntil;
        private void AskConfirm(string key) { _confirmKey = key; _confirmUntil = Time.unscaledTime + 4f; }

        private void DrawLibrary(Player player)
        {
            RefreshLibrary();
            RefreshInbox();
            GUILayout.BeginHorizontal();
            GUILayout.Label($"Blueprints in <b>BepInEx/blueprints</b>. Place one: a green preview follows where you look; turn it with the mouse wheel and click to place. " +
                            "Share sends one to everyone playing here; a share code is the same as text for a chat.", _wDim);
            if (GUILayout.Button("Paste code", _wButton, GUILayout.Width(110), GUILayout.Height(28))) PasteCode();
            if (GUILayout.Button("Open folder", _wButton, GUILayout.Width(110), GUILayout.Height(28))) { Directory.CreateDirectory(BlueprintDir); Application.OpenURL("file:///" + BlueprintDir.Replace(Path.DirectorySeparatorChar, '/')); }
            GUILayout.EndHorizontal();
            GUILayout.Space(4);

            _plansScroll = GUILayout.BeginScrollView(_plansScroll, GUILayout.ExpandHeight(true));

            // blueprints other players shared with you
            if (_inbox.Count > 0)
            {
                GUILayout.Label($"<b>Shared with you</b> ({_inbox.Count})", _wText);
                foreach (SharedBlueprint s in _inbox.ToList())
                {
                    Rect r = GUILayoutUtility.GetRect(0f, 70f, GUILayout.ExpandWidth(true));
                    r.height = 64f;
                    Round(r, new Color(0.1f, 0.13f, 0.09f, 1f), 8f);
                    Outline(r, new Color(0.5f, 0.75f, 0.45f, 1f), 8f);
                    var spic = new Rect(r.x + 6f, r.y + 6f, 92f, 52f);
                    if (s.Picture != null) GUI.DrawTexture(spic, s.Picture, ScaleMode.ScaleToFit); else Round(spic, new Color(0.13f, 0.17f, 0.12f, 1f), 5f);
                    float tw = r.width - 486f;
                    GUI.Label(new Rect(spic.xMax + 10f, r.y + 7f, tw, 24f), $"{s.Name}   <color=#a8d8a0>from {s.From}</color>", _wBold);
                    string mats = string.Join("   ", s.Materials.Take(4).Select(kv => $"{Localization.instance.Localize(kv.Key)} {kv.Value}").ToArray());
                    GUI.Label(new Rect(spic.xMax + 10f, r.y + 34f, tw, 22f), $"{s.Pieces} pieces   ·   {mats}", _wDim);
                    if (GUI.Button(new Rect(r.xMax - 364f, r.y + 15f, 110f, 34f), "Place", _wButtonGood)) PlaceShared(player, s);
                    if (GUI.Button(new Rect(r.xMax - 248f, r.y + 15f, 130f, 34f), "Save to library", _wButton)) SaveShared(s);
                    if (GUI.Button(new Rect(r.xMax - 112f, r.y + 15f, 100f, 34f), "Dismiss", _wButton)) DismissShared(s);
                    GUILayout.Space(4);
                }
                GUILayout.Space(6);
            }
            if (_library.Count == 0) GUILayout.Label("No blueprints yet.", _wText);
            foreach (BlueprintFile bf in _library)
            {
                Rect r = GUILayoutUtility.GetRect(0f, 104f, GUILayout.ExpandWidth(true));
                r.height = 98f;
                Round(r, new Color(0.09f, 0.12f, 0.14f, 1f), 8f);
                Outline(r, new Color(0.25f, 0.4f, 0.48f, 1f), 8f);
                var pic = new Rect(r.x + 8f, r.y + 8f, 136f, 82f);
                if (bf.Picture != null) GUI.DrawTexture(pic, bf.Picture, ScaleMode.ScaleToFit);
                else { Round(pic, new Color(0.13f, 0.17f, 0.2f, 1f), 6f); GUI.Label(pic, "no picture", new GUIStyle(_wDim) { alignment = TextAnchor.MiddleCenter }); }

                float x = pic.xMax + 12f, tw = r.width - (pic.width + 30f) - 130f;
                GUI.Label(new Rect(x, r.y + 6f, tw, 24f), bf.Name + (bf.SharedBy != null ? $"   <color=#a8d8a0>from {bf.SharedBy}</color>" : "") + (bf.Placed ? "   <color=#8fe08f>(placed)</color>" : ""), _wBold);
                if (bf.Error != null) GUI.Label(new Rect(x, r.y + 32f, tw, 40f), "Could not read: " + bf.Error, _wDim);
                else
                {
                    GUI.Label(new Rect(x, r.y + 32f, tw, 20f), $"{bf.Pieces} pieces   ·   about {bf.Size.x:0} × {bf.Size.z:0} m, {bf.Size.y:0} m high", _wText);
                    string mats = string.Join("   ", bf.Materials.Take(5).Select(kv => $"{Localization.instance.Localize(kv.Key)} {kv.Value}").ToArray());
                    GUI.Label(new Rect(x, r.y + 56f, tw, 36f), mats.Length > 0 ? "Needs: " + mats : "", _wDim);
                }
                if (bf.Error == null)
                {
                    string key = "share:" + bf.Path;
                    if (GUI.Button(new Rect(r.xMax - 122f, r.y + 54f, 54f, 30f), Confirming(key) ? "Send?" : "Share", Confirming(key) ? _wButtonBad : _wButton))
                    {
                        if (Confirming(key)) { ShareWithEveryone(bf.Path); _confirmKey = null; } else AskConfirm(key);
                    }
                    if (GUI.Button(new Rect(r.xMax - 64f, r.y + 54f, 52f, 30f), "Code", _wButton)) CopyCode(bf.Path);
                }
                if (bf.Error == null && GUI.Button(new Rect(r.xMax - 122f, r.y + 12f, 110f, 34f), "Place", _wButtonGood))
                {
                    try
                    {
                        JObject doc = JObject.Parse(File.ReadAllText(bf.Path));
                        StartPlacement(bf.Name, bf.Path, EntriesFrom(doc["pieces"] as JArray ?? new JArray()), player.transform.eulerAngles.y, 0f, null);
                    }
                    catch (Exception e) { PlansToast("Could not read " + bf.Name + ": " + e.Message); }
                }
                GUILayout.Space(4);
            }
            GUILayout.EndScrollView();
        }

        private void DrawPlacedPlans(Player player, List<PlanInfo> plans)
        {
            Vector3 me = player.transform.position;
            GUILayout.BeginHorizontal();
            GUILayout.Label($"Remove every ghost within <b>{_removeRadius:0} m</b> of you:", _wText, GUILayout.Width(330));
            _removeRadius = Mathf.Round(GUILayout.HorizontalSlider(_removeRadius, 2f, 150f, GUILayout.Width(200)));
            int inside = _orders.Values.Count(o => (o.Pos - me).sqrMagnitude <= _removeRadius * _removeRadius);
            if (GUILayout.Button(Confirming("radius") ? $"Click again: remove {inside}" : $"Remove ({inside})", Confirming("radius") ? _wButtonBad : _wButton, GUILayout.Width(190), GUILayout.Height(28)) && inside > 0)
            {
                if (Confirming("radius")) { int n = RemoveWithin(me, _removeRadius); PlansToast($"Removed {n} ghost(s)"); _confirmKey = null; }
                else AskConfirm("radius");
            }
            GUILayout.EndHorizontal();
            GUILayout.Space(4);

            // plans still waiting for their ground to be levelled (their ghosts come afterwards)
            foreach (LevelJob pending in _levelJobs.Values.Where(j => j.OnDone != null && !j.Finished).ToList())
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label($"<b>{pending.Title}</b>: levelling the ground ({pending.Status}, pass {pending.Pass}); its ghosts appear when it is done", _wText);
                if (GUILayout.Button("Cancel", _wButtonBad, GUILayout.Width(110), GUILayout.Height(28)))
                {
                    StopLevel(pending.Key);
                    _levelJobs.Remove(pending.Key);
                    string ground = RestoreTerrain(pending.Key);
                    PlansToast($"Cancelled \"{pending.Title}\"" + (ground != null ? ". " + ground : ""));
                }
                GUILayout.EndHorizontal();
            }

            if (plans.Count == 0) { GUILayout.Label("Nothing is planned. Place a blueprint, or plan pieces by hand with the hammer.", _wText); GUILayout.FlexibleSpace(); return; }

            _plansScroll = GUILayout.BeginScrollView(_plansScroll, GUILayout.ExpandHeight(true));
            Inventory inv = player.GetInventory();
            foreach (PlanInfo plan in plans)
            {
                Rect r = GUILayoutUtility.GetRect(0f, 96f, GUILayout.ExpandWidth(true));
                r.height = 90f;
                Round(r, new Color(0.09f, 0.12f, 0.14f, 1f), 8f);
                Outline(r, plan.IsBlueprint ? new Color(0.25f, 0.4f, 0.48f, 1f) : new Color(0.45f, 0.4f, 0.25f, 1f), 8f);
                float tw = r.width - 360f;
                Vector3 dir = plan.Centre - me; dir.y = 0f;
                GUI.Label(new Rect(r.x + 12f, r.y + 6f, tw, 24f), plan.Title, _wBold);
                GUI.Label(new Rect(r.x + 12f, r.y + 32f, tw, 20f), $"{plan.Orders.Count} pieces left   ·   {plan.Distance:0} m away ({Compass(dir)})", _wText);
                List<KeyValuePair<string, int>> needs = MaterialsOf(plan.Orders.Select(o => o.Prefab));
                string needText = string.Join("   ", needs.Take(4).Select(kv =>
                {
                    int have = inv.CountItems(kv.Key);
                    string colour = have >= kv.Value ? "#8fe08f" : "#ffb38a";
                    return $"<color={colour}>{Localization.instance.Localize(kv.Key)} {have}/{kv.Value}</color>";
                }).ToArray());
                GUI.Label(new Rect(r.x + 12f, r.y + 56f, tw, 30f), needText, _wDim);

                float bx = r.xMax - 342f;
                bool near = plan.Distance <= _buildAllRadius.Value;
                GUI.enabled = near;
                if (GUI.Button(new Rect(bx, r.y + 12f, 110f, 32f), near ? "Build nearby" : "Too far", _wButtonGood)) { PlansWindowOpen = false; StartCoroutine(BuildMany(player)); }
                GUI.enabled = true;
                bool bridge = plan.IsBlueprint && IsBridgePlan(plan.Title);
                GUI.enabled = !bridge;   // a bridge's posts are cut to the riverbed where it stands: draw a new one instead of moving it
                if (GUI.Button(new Rect(bx + 114f, r.y + 12f, 100f, 32f), bridge ? "(bridge)" : "Move", _wButton)) StartMove(player, plan);
                GUI.enabled = true;
                string key = "plan:" + plan.Key;
                if (!Confirming(key))
                {
                    if (GUI.Button(new Rect(bx + 218f, r.y + 12f, 118f, 32f), "Remove", _wButton)) { AskConfirm(key); _confirmBuilt = plan.IsBlueprint ? BuiltPieces(plan.Key).Count : 0; }
                }
                else if (_confirmBuilt > 0)
                {
                    // part of it is built: remove just the ghosts, or take the built part down too (materials back)
                    if (GUI.Button(new Rect(bx, r.y + 12f, 160f, 32f), "Remove ghosts only", _wButton)) { int n = RemovePlan(plan.Key); PlansToast($"Removed \"{plan.Title}\" ({n} ghosts); what is built stays"); _confirmKey = null; }
                    if (GUI.Button(new Rect(bx + 164f, r.y + 12f, 172f, 32f), $"Also take down {_confirmBuilt} built", _wButtonBad)) { int n = RemovePlan(plan.Key, takeDownBuilt: true); PlansToast($"Removed \"{plan.Title}\" ({n} ghosts) and took down what was built"); _confirmKey = null; }
                }
                else if (GUI.Button(new Rect(bx + 218f, r.y + 12f, 118f, 32f), "Click again", _wButtonBad)) { int n = RemovePlan(plan.Key); PlansToast($"Removed \"{plan.Title}\" ({n} ghosts)"); _confirmKey = null; }
                if (_levelJobs.TryGetValue(plan.Key, out LevelJob job) && (job.Running || job.Next < job.Points.Count))
                {
                    GUI.Label(new Rect(bx, r.y + 50f, 214f, 34f), $"<color=#e0b070>Levelling {job.Next}/{job.Points.Count}: {job.Status}</color>", _wDim);
                    if (GUI.Button(new Rect(bx + 218f, r.y + 50f, 118f, 30f), job.Running ? "Stop levelling" : "Resume", _wButton))
                    { if (job.Running) StopLevel(plan.Key); else ResumeLevel(plan.Key); }
                }
                else
                {
                    bool levelable = plan.IsBlueprint && !IsBridgePlan(plan.Title);
                    float lw = levelable ? 214f : 336f;
                    GUI.Label(new Rect(bx, r.y + 50f, lw, 34f), near ? $"Builds every ghost within {_buildAllRadius.Value:0} m of you that you have materials for, lowest first." : $"Walk within {_buildAllRadius.Value:0} m to build it.", _wDim);
                    if (levelable && GUI.Button(new Rect(bx + 218f, r.y + 50f, 118f, 30f), "Level ground", _wButton))
                        PlansToast($"Levelling the ground under \"{plan.Title}\" ({LevelPlacedPlan(plan)} spots)");
                }
                GUILayout.Space(4);
            }
            GUILayout.EndScrollView();
        }

        private static string Compass(Vector3 dir)
        {
            if (dir.sqrMagnitude < 1f) return "here";
            float a = Mathf.Repeat(Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg, 360f);
            string[] names = { "north", "north-east", "east", "south-east", "south", "south-west", "west", "north-west" };
            return names[Mathf.RoundToInt(a / 45f) % 8];
        }

        private void DrawPlanSettings()
        {
            _plansScroll = GUILayout.BeginScrollView(_plansScroll, GUILayout.ExpandHeight(true));
            Slider("Build-all radius", "Holding E at a ghost, or Build nearby, builds every ghost this close to you.", _buildAllRadius, 4f, 64f, "0", " m");
            Slider("Reach for E", "How close you must be to a ghost to build it with E.", _buildReach, 2f, 12f, "0.0", " m");
            Slider("Ghost view distance", "Ghosts further away than this are not drawn.", _viewDistance, 20f, 200f, "0", " m");
            Slider("Ghost opacity", "How solid the ghosts look.", _ghostOpacity, 0.05f, 0.6f, "0.00", "");
            IntSlider("Most ghosts at once", "Big plans need more; lower it if the game slows down.", _maxGhosts, 50, 2000);
            GUILayout.Space(6);
            Toggle(_showGhosts, "Show ghosts", "Hide them all for a moment (also F9).");
            Toggle(_buildByHand, "Build by pressing E", "Walk up to a ghost and press E to build it, no hammer needed.");
            GUILayout.Label(ClaudeToolsInstalled ? "Claude Tools is installed: an AI assistant can see your game and place blueprints for you while its requests are on (Claude Tools settings)." : "Install the Claude Tools mod to let an AI assistant (Claude Code in BepInEx/blueprints) see your game and place blueprints for you.", _wDim);
            GUILayout.Space(6);
            GUILayout.Label($"Keys: {_blueprintKey.Value} this window   ·   {_planKey.Value} plan mode (hammer)   ·   {_selectKey.Value} select an aimed ghost's piece   ·   {_removeKey.Value} remove an aimed ghost (Shift: all within 8 m)   ·   " +
                            $"{_toggleGhostsKey.Value} show/hide ghosts   ·   {_stabilityKey.Value} stability colours", _wDim);
            GUILayout.EndScrollView();
        }

        private void Slider(string label, string help, BepInEx.Configuration.ConfigEntry<float> entry, float min, float max, string format, string unit)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label($"{label}: <b>{entry.Value.ToString(format)}{unit}</b>", _wText, GUILayout.Width(300));
            float v = GUILayout.HorizontalSlider(entry.Value, min, max, GUILayout.Width(300));
            if (Mathf.Abs(v - entry.Value) > 1e-4f) entry.Value = (float)Math.Round(v, format.Length > 3 ? 2 : (format.Contains(".") ? 1 : 0));
            GUILayout.EndHorizontal();
            GUILayout.Label(help, _wDim);
            GUILayout.Space(4);
        }

        private void IntSlider(string label, string help, BepInEx.Configuration.ConfigEntry<int> entry, int min, int max)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label($"{label}: <b>{entry.Value}</b>", _wText, GUILayout.Width(300));
            int v = Mathf.RoundToInt(GUILayout.HorizontalSlider(entry.Value, min, max, GUILayout.Width(300)) / 10f) * 10;
            if (v != entry.Value) entry.Value = Mathf.Clamp(v, min, max);
            GUILayout.EndHorizontal();
            GUILayout.Label(help, _wDim);
            GUILayout.Space(4);
        }

        private void Toggle(BepInEx.Configuration.ConfigEntry<bool> entry, string label, string help)
        {
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(entry.Value ? "On" : "Off", entry.Value ? _wButtonGood : _wButton, GUILayout.Width(56), GUILayout.Height(26))) entry.Value = !entry.Value;
            GUILayout.Space(6);
            GUILayout.BeginVertical();
            GUILayout.Label(label, _wText);
            GUILayout.Label(help, _wDim);
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
            GUILayout.Space(2);
        }
    }
}
