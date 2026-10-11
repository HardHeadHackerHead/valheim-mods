using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static DHack.Shared.Translations;

namespace BetterCraftingStations
{
    /// <summary>
    /// The filter chips above the crafting list: a row of types (weapons, armor, tools, ammo...) with "All" and "Can craft", and a row of tiers
    /// (wood and flint, bronze, iron...). They are the game's own tab button copied, so they look like the rest of the window, and the list
    /// below is shortened to make room for them. Only what the station makes shows, with counts.
    /// </summary>
    internal static class Bar
    {
        private static readonly AccessTools.FieldRef<InventoryGui, RectTransform> ListRoot = AccessTools.FieldRefAccess<InventoryGui, RectTransform>("m_recipeListRoot");
        private static readonly AccessTools.FieldRef<InventoryGui, Button> TabCraft = AccessTools.FieldRefAccess<InventoryGui, Button>("m_tabCraft");
        private static readonly AccessTools.FieldRef<InventoryGui, Scrollbar> ListScroll = AccessTools.FieldRefAccess<InventoryGui, Scrollbar>("m_recipeListScroll");

        private static readonly Action<InventoryGui, bool> UpdateCraftingPanel = AccessTools.MethodDelegate<Action<InventoryGui, bool>>(AccessTools.Method(typeof(InventoryGui), "UpdateCraftingPanel"));

        private const float ChipHeight = 20f, Gap = 2f, FontSize = 12f;

        /// <summary>The recipes the station offered, before the chips narrowed them (set when the game builds the list).</summary>
        public static List<Recipe> All;

        /// <summary>The chips are due but the crafting list isn't on screen yet: the game sets the panel up while opening, before the
        /// inventory is shown, so the bar is built on a later frame (Plugin's InventoryGui.Update patch), once the list is there.</summary>
        public static bool Pending;

        private static GameObject _root;
        private static readonly List<GameObject> Chips = new List<GameObject>();
        private static RectTransform _list, _scrollbar;
        private static Geo _listGeo, _scrollGeo;
        private static float _taken;
        private static string _signature = "";
        private static bool _dumped;

        private struct Geo
        {
            public Vector2 AnchorMin, AnchorMax, OffsetMin, OffsetMax, Size, Pos;
            public static Geo Of(RectTransform r) => new Geo { AnchorMin = r.anchorMin, AnchorMax = r.anchorMax, OffsetMin = r.offsetMin, OffsetMax = r.offsetMax, Size = r.sizeDelta, Pos = r.anchoredPosition };
            public void Restore(RectTransform r) { r.anchorMin = AnchorMin; r.anchorMax = AnchorMax; r.offsetMin = OffsetMin; r.offsetMax = OffsetMax; r.sizeDelta = Size; r.anchoredPosition = Pos; }
        }

        // ---- show, update, hide ---------------------------------------------------------------------------------------

        public static void Refresh(InventoryGui gui)
        {
            bool show = Plugin.Enabled && All != null && All.Count > 0 && gui != null && !gui.InUpradeTab() && Player.m_localPlayer?.GetCurrentCraftingStation() != null;
            if (!show)
            {
                Pending = false;
                if (_root != null && _root.activeSelf) Plugin.Log.LogInfo($"Chips hidden: enabled {Plugin.Enabled}, recipes {All?.Count ?? -1}, craft tab {gui != null && !gui.InUpradeTab()}, station {Player.m_localPlayer?.GetCurrentCraftingStation() != null}");
                Hide();
                return;
            }
            RectTransform list = ListRoot(gui)?.GetComponentInParent<ScrollRect>(true)?.GetComponent<RectTransform>();
            if (list == null || !list.gameObject.activeInHierarchy) { Pending = true; return; }   // (opening: not on screen yet)
            Pending = false;

            string station = Filters.StationKey();
            Filters.State state = Filters.Of(station);
            string signature = $"{station}|{All.Count}|{state.Type}|{state.Tier}|{state.CanCraft}";
            if (signature == _signature && _root != null && _root.activeSelf) return;
            _signature = signature;

            if (_list != list) { Hide(); _list = list; _listGeo = Geo.Of(list); } // (a new window: start from its own shape)
            RestoreGeometry();
            if (_root == null) Create(gui, list);
            if (!_dumped) { _dumped = true; Dump(list); }

            float height = Build(gui, list, station, state);
            _root.SetActive(height > 0f);
            if (height > 0f) Take(gui, list, height);
        }

        public static void Hide()
        {
            _signature = "";
            RestoreGeometry();
            if (_root != null) _root.SetActive(false);
        }

        /// <summary>When the mod unloads (a reload): the window as the game made it.</summary>
        public static void Destroy()
        {
            RestoreGeometry();
            if (_root != null) UnityEngine.Object.Destroy(_root);
            _root = null;
            Chips.Clear();
        }

        // ---- making room ----------------------------------------------------------------------------------------------

        private static void Create(InventoryGui gui, RectTransform list)
        {
            _root = new GameObject("ChipBar", typeof(RectTransform));
            _root.transform.SetParent(list.parent, false);
            _root.transform.SetAsLastSibling();
        }

        private static void RestoreGeometry()
        {
            if (_taken <= 0f) return;
            if (_list != null) _listGeo.Restore(_list);
            if (_scrollbar != null) _scrollGeo.Restore(_scrollbar);
            _taken = 0f;
        }

        /// <summary>The list shortened from the top by "height", the bar standing where its top was.</summary>
        private static void Take(InventoryGui gui, RectTransform list, float height)
        {
            var corners = new Vector3[4];
            list.GetWorldCorners(corners);                    // (0 bottom-left, 1 top-left, 2 top-right, 3 bottom-right)
            var bar = (RectTransform)_root.transform;
            bar.anchorMin = bar.anchorMax = new Vector2(0.5f, 0.5f);
            bar.pivot = new Vector2(0f, 1f);
            bar.sizeDelta = new Vector2(list.rect.width, height);
            bar.position = corners[1];

            Shrink(list, height);
            _scrollbar = ListScroll(gui)?.GetComponent<RectTransform>();
            if (_scrollbar != null && !_scrollbar.IsChildOf(list)) { _scrollGeo = Geo.Of(_scrollbar); Shrink(_scrollbar, height); }
            else _scrollbar = null;
            _taken = height;
        }

        private static void Shrink(RectTransform r, float h)
        {
            if (!Mathf.Approximately(r.anchorMin.y, r.anchorMax.y)) r.offsetMax = new Vector2(r.offsetMax.x, r.offsetMax.y - h);
            else
            {
                r.sizeDelta = new Vector2(r.sizeDelta.x, r.sizeDelta.y - h);
                r.anchoredPosition = new Vector2(r.anchoredPosition.x, r.anchoredPosition.y - r.pivot.y * h);
            }
        }

        // ---- the chips --------------------------------------------------------------------------------------------------

        private static float Build(InventoryGui gui, RectTransform list, string station, Filters.State state)
        {
            foreach (GameObject old in Chips) if (old != null) UnityEngine.Object.Destroy(old);
            Chips.Clear();

            // How many each chip would show, given what the other row has chosen.
            int Count(Func<Recipe, bool> mine, Filters.State others) => Filters.Apply(All, others).Count(mine);
            var typesHere = Classify.TypeOrder.Where(t => All.Any(r => Classify.TypeOf(r) == t)).ToList();
            var tiersHere = Enumerable.Range(0, Classify.TierNames.Length).Where(t => All.Any(r => Classify.TierOf(r) == t)).ToList();
            if (typesHere.Count < 2 && tiersHere.Count < 2) return 0f;   // nothing to choose between

            var rows = new List<List<(string Label, bool On, Action Click)>>();

            var typeRow = new List<(string, bool, Action)>
            {
                (T("bettercraftingstations_all", All.Count), !Filters.Active(state), () => { state.Type = null; state.Tier = -1; state.CanCraft = false; }),
            };
            var noType = new Filters.State { Tier = state.Tier, CanCraft = state.CanCraft };
            foreach (string t in typesHere)
            {
                string type = t;
                int n = Count(r => Classify.TypeOf(r) == type, noType);
                if (n == 0 && state.Type != type) continue;
                typeRow.Add((T("bettercraftingstations_chip", Classify.Label(type), n), state.Type == type, () => state.Type = state.Type == type ? null : type));
            }
            typeRow.Add((T("bettercraftingstations_can_craft"), state.CanCraft, () => state.CanCraft = !state.CanCraft));
            rows.Add(typeRow);

            if (tiersHere.Count > 1)
            {
                var tierRow = new List<(string, bool, Action)>();
                var noTier = new Filters.State { Type = state.Type, CanCraft = state.CanCraft };
                foreach (int t in tiersHere)
                {
                    int tier = t;
                    int n = Count(r => Classify.TierOf(r) == tier, noTier);
                    if (n == 0 && state.Tier != tier) continue;
                    tierRow.Add((T("bettercraftingstations_chip", T(Classify.TierNames[tier]), n), state.Tier == tier, () => state.Tier = state.Tier == tier ? -1 : tier));
                }
                rows.Add(tierRow);
            }

            Button template = TabCraft(gui);
            if (template == null) return 0f;
            float width = list.rect.width, y = 0f;
            foreach (var row in rows)
            {
                float x = 0f;
                foreach (var chip in row)
                {
                    float guess = Mathf.Max(28f, chip.Label.Sum(c => c >= 0x2E80 ? 2 : 1) * 6.3f + 10f);   // (Chinese letters take two widths)
                    GameObject made = MakeChip(template, chip.Label, chip.On, chip.Click, gui, 0f, 0f, guess);
                    var rt = (RectTransform)made.transform;
                    float w = rt.sizeDelta.x;                                          // (as wide as its words really are: see MakeChip)
                    if (x > 0f && x + w > width) { x = 0f; y += ChipHeight + Gap; }   // (the row wraps)
                    rt.anchoredPosition = new Vector2(x, -y);
                    Chips.Add(made);
                    x += w + Gap;
                }
                y += ChipHeight + Gap;
            }
            return y + 2f;
        }

        private static GameObject MakeChip(Button template, string label, bool on, Action click, InventoryGui gui, float x, float y, float w)
        {
            GameObject go = UnityEngine.Object.Instantiate(template.gameObject, _root.transform);
            go.name = "Chip " + label;
            // The copy keeps the tab's gamepad binding (UIGamePad, which would press the real tab, and its hint) and its ButtonTextColor
            // (wired to the real tab: errors every frame). Remove them all, on the copy and everything under it.
            foreach (Transform hint in go.GetComponentsInChildren<Transform>(true).Where(t => t != go.transform && t.name.StartsWith("gamepad_hint")).ToList())
                UnityEngine.Object.DestroyImmediate(hint.gameObject);
            foreach (Component c in go.GetComponentsInChildren<Component>(true).Where(c => c != null && (c.GetType().Name == "UIGamePad" || c.GetType().Name == "ButtonTextColor")).ToList())
                UnityEngine.Object.Destroy(c);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.sizeDelta = new Vector2(w, ChipHeight);
            rt.anchoredPosition = new Vector2(x, -y);
            Button button = go.GetComponent<Button>();
            button.onClick = new Button.ButtonClickedEvent();   // (not the tab's own: its listeners come along with the copy)
            button.interactable = !on;                          // (the chosen one looks like the chosen tab)
            button.onClick.AddListener(() => { click(); _signature = ""; UpdateCraftingPanel(gui, false); });
            TMP_Text text = go.transform.Find("Text")?.GetComponent<TMP_Text>() ?? go.GetComponentInChildren<TMP_Text>();
            if (text != null)
            {
                text.text = label;
                text.fontSize = FontSize;
                text.enableAutoSizing = false;
                text.textWrappingMode = TextWrappingModes.NoWrap;
                text.overflowMode = TextOverflowModes.Ellipsis;
                // the letters' real width (wide ones, as in "Ammo", outgrow a guess from how many there are), plus the text's own margins
                var tr = (RectTransform)text.transform;
                float margins = Mathf.Max(0f, w - tr.rect.width);
                float need = Mathf.Ceil(text.GetPreferredValues(label, 1000f, 0f).x + Mathf.Max(margins, 8f) + 4f);
                if (need > w) rt.sizeDelta = new Vector2(need, ChipHeight);
            }
            foreach (LayoutElement le in go.GetComponents<LayoutElement>()) UnityEngine.Object.Destroy(le);
            return go;
        }

        // ---- finding out how the window is laid out (written to the log once per game start, for tuning) ---------------

        private static void Dump(RectTransform list)
        {
            try
            {
                var sb = new System.Text.StringBuilder("Crafting window layout:\n");
                Transform level = list;
                for (int up = 0; up < 4 && level != null; up++, level = level.parent)
                {
                    sb.AppendLine($" [{level.name}] {(level as RectTransform)?.rect.size}");
                    foreach (Transform child in level.parent != null ? level.parent : level)
                    {
                        var r = child as RectTransform;
                        if (r == null) continue;
                        sb.AppendLine($"    {(child == level ? "*" : " ")} {child.name}{(child.gameObject.activeSelf ? "" : " (off)")} anchors {r.anchorMin}-{r.anchorMax} pivot {r.pivot} size {r.rect.size} pos {r.anchoredPosition}");
                    }
                }
                Plugin.Log.LogInfo(sb.ToString());
            }
            catch (Exception e) { Plugin.Log.LogWarning("Could not describe the crafting window: " + e.Message); }
        }
    }
}
