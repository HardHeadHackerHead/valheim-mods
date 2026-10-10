using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ClaudeTools
{
    /// <summary>
    /// "screen": test a mod's windows the way a player uses them. See what is on screen (every button, box, slider and text, where it is, and
    /// which overlap), click a button or an item cell by its words, type in a text box, move a slider, press a key for one frame.
    /// Looking is open to everyone; clicking, typing and keys act as the player would, so they follow the game's cheat rule (single player,
    /// the host, or devcommands on), like shoot use.
    /// </summary>
    public partial class Plugin
    {
        private const string UiUsage =
            "screen list [text] | texts [text] | overlaps [text] | click <words> [n] | type <text> | submit | slider <n> <value> | use [name] [alt] | key <tab|escape|e|key name> [frames=1]: " +
            "see and use what is on screen (buttons, item cells, text boxes, sliders) by their words; use presses E on what the player looks at (as the game does); keys reach mods' windows (the game's own reading of E may not see them: use screen use); click, type, slider, use and key act as the player " +
            "(only where cheats are allowed)";

        private void RegisterUiCommands() => Builtin("screen", UiUsage, (a, output, error) => UiCommand(a, output, error));

        private class UiThing
        {
            public string Kind, Label, Window;
            public GameObject Go;
            public Rect Screen;   // pixels, y down from the top
            public bool On;       // can be clicked now
        }

        private static IEnumerator UiCommand(string[] a, Action<JObject> output, Action<string> error)
        {
            string sub = a.Length > 1 ? a[1].ToLowerInvariant() : "list";
            string rest = a.Length > 2 ? string.Join(" ", a.Skip(2)) : "";
            bool acts = sub == "click" || sub == "type" || sub == "submit" || sub == "slider" || sub == "key" || sub == "use";
            if (acts && !CheatsAllowed()) { error(CheatRefusal("screen " + sub)); yield break; }
            switch (sub)
            {
                case "list":
                {
                    List<UiThing> all = UiThings(true, false).Where(t => UiMatches(t, rest)).ToList();
                    output(new JObject { ["count"] = all.Count, ["things"] = new JArray(all.Take(250).Select(UiJson)), ["note"] = all.Count > 250 ? "first 250: add words to narrow it" : null });
                    yield break;
                }
                case "texts":
                {
                    List<UiThing> all = UiThings(false, true).Where(t => UiMatches(t, rest)).ToList();
                    output(new JObject { ["count"] = all.Count, ["texts"] = new JArray(all.Take(300).Select(UiJson)), ["note"] = all.Count > 300 ? "first 300: add words to narrow it" : null });
                    yield break;
                }
                case "overlaps":
                {
                    List<UiThing> all = UiThings(true, true).Where(t => UiMatches(t, rest)).ToList();
                    var pairs = new JArray();
                    for (int i = 0; i < all.Count; i++)
                        for (int j = i + 1; j < all.Count; j++)
                        {
                            UiThing p = all[i], q = all[j];
                            if (p.Go.transform.IsChildOf(q.Go.transform) || q.Go.transform.IsChildOf(p.Go.transform)) continue;   // (a button and its own label)
                            if (p.Go.transform.root != q.Go.transform.root || p.Go.GetComponentInParent<Canvas>()?.rootCanvas != q.Go.GetComponentInParent<Canvas>()?.rootCanvas) continue;   // (one layer over another: the HUD under the inventory)
                            if (p.Kind == "text" && q.Kind == "text" && p.Go.transform.parent == q.Go.transform.parent && p.Label == q.Label) continue; // (a text and its shadow)
                            float area = UiOverlap(p.Screen, q.Screen), smaller = Mathf.Min(p.Screen.width * p.Screen.height, q.Screen.width * q.Screen.height);
                            if (smaller < 1f || area < 0.2f * smaller) continue;
                            // the game's invisible catch-alls (the inventory's drop-on-the-ground area covers the whole screen) cover everything
                            float screenArea = UnityEngine.Screen.width * (float)UnityEngine.Screen.height;
                            if (p.Screen.width * p.Screen.height > 0.4f * screenArea || q.Screen.width * q.Screen.height > 0.4f * screenArea) continue;
                            pairs.Add(new JObject { ["a"] = UiJson(p), ["b"] = UiJson(q), ["covered"] = Math.Round(area / smaller, 2) });
                            if (pairs.Count >= 60) break;
                        }
                    output(new JObject { ["overlaps"] = pairs, ["note"] = pairs.Count == 0 ? "nothing covers anything else by more than a fifth" : "covered: the share of the smaller one under the other" });
                    yield break;
                }
                case "click":
                {
                    // the last word can be which one (1 = first) when several match
                    int n = 1;
                    string words = rest;
                    Match m = Regex.Match(rest, @"^(.*\S)\s+(\d+)$");
                    if (m.Success) { words = m.Groups[1].Value; n = int.Parse(m.Groups[2].Value); }
                    List<UiThing> clickable = UiThings(true, false).Where(t => t.Kind != "slider").ToList();   // (a text box: clicking it puts the cursor in it)
                    List<UiThing> exact = clickable.Where(t => string.Equals(t.Label, words, StringComparison.OrdinalIgnoreCase)).ToList();
                    List<UiThing> found = exact.Count > 0 ? exact : clickable.Where(t => t.Label.IndexOf(words, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
                    if (found.Count == 0) { error($"nothing on screen says \"{words}\" (screen list shows what is there)"); yield break; }
                    if (n < 1 || n > found.Count) { error($"{found.Count} say \"{words}\": pick 1 to {found.Count}"); yield break; }
                    UiThing t = found[n - 1];
                    if (!t.On) { error($"\"{t.Label}\" can't be clicked now (greyed out)"); yield break; }
                    TMP_InputField box = t.Go.GetComponent<TMP_InputField>();
                    if (box != null) { box.Select(); box.ActivateInputField(); }   // (then screen type types in it)
                    else UiClick(t.Go);
                    yield return null; yield return null;   // (mods act on a click in their next Update)
                    output(new JObject { ["clicked"] = UiJson(t), ["alsoMatched"] = found.Count > 1 ? found.Count - 1 : 0 });
                    yield break;
                }
                case "type":
                case "submit":
                {
                    TMP_InputField field = UiFocusedField() ?? UiThings(true, false).Where(x => x.Kind == "input" && x.On).Select(x => x.Go.GetComponent<TMP_InputField>()).FirstOrDefault();
                    if (field == null) { error("no text box on screen"); yield break; }
                    if (sub == "type")
                    {
                        field.ActivateInputField();
                        field.text = rest;   // (fires onValueChanged, as typing does)
                    }
                    else { field.onSubmit.Invoke(field.text); field.onEndEdit.Invoke(field.text); }
                    yield return null; yield return null;
                    output(new JObject { [sub == "type" ? "typed" : "submitted"] = field.text, ["box"] = UiPathOf(field.transform) });
                    yield break;
                }
                case "slider":
                {
                    List<UiThing> sliders = UiThings(true, false).Where(x => x.Kind == "slider").ToList();
                    if (a.Length < 4 || !int.TryParse(a[2], out int i) || !float.TryParse(a[3], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float v))
                    { error("screen slider <n> <value>: n is which slider (screen list shows them, 1 = first)"); yield break; }
                    if (i < 1 || i > sliders.Count) { error($"{sliders.Count} sliders on screen"); yield break; }
                    Slider s = sliders[i - 1].Go.GetComponent<Slider>();
                    s.value = v;   // (fires onValueChanged, as dragging does)
                    yield return null; yield return null;
                    output(new JObject { ["slider"] = UiJson(sliders[i - 1]), ["value"] = s.value });
                    yield break;
                }
                case "use":
                {
                    // E on something, as the game does it (Player.Interact: mods that hook E see it). "screen use <name>": the thing of that name
                    // within a player's reach (3 m), whatever the crosshair is on (the game aims E from the camera behind the player, which a
                    // fence can block); "screen use": what the crosshair is on. "alt" at the end: with the alternate key (Shift).
                    Player p = Player.m_localPlayer;
                    if (p == null) { error("no player"); yield break; }
                    bool alt = rest.Trim().ToLowerInvariant().EndsWith(" alt") || rest.Trim().ToLowerInvariant() == "alt";
                    string want = alt ? rest.Trim().Substring(0, rest.Trim().Length - 3).Trim().ToLowerInvariant() : rest.Trim().ToLowerInvariant();
                    GameObject hover = p.GetHoverObject(), target = null;
                    string Whole(GameObject g) => (g.GetComponentInParent<Piece>()?.name ?? g.transform.root.name).Replace("(Clone)", "");
                    if (want.Length == 0) target = hover;
                    else if (hover != null && (Whole(hover).ToLowerInvariant().Contains(want) || hover.name.ToLowerInvariant().Contains(want))) target = hover;
                    else
                    {
                        float best = 3f;
                        foreach (MonoBehaviour mb in UnityEngine.Object.FindObjectsOfType<MonoBehaviour>())
                        {
                            if (!(mb is Interactable) || !(Whole(mb.gameObject).ToLowerInvariant().Contains(want) || mb.name.ToLowerInvariant().Contains(want))) continue;
                            float d = Vector3.Distance(mb.transform.position, p.transform.position);
                            if (d < best) { best = d; target = mb.gameObject; }
                        }
                    }
                    if (target == null) { error(want.Length == 0 ? "the player isn't looking at anything to use" : $"no \"{want}\" within reach (3 m): shoot near {want} first"); yield break; }
                    var interact = AccessTools.Method(typeof(Player), "Interact", new[] { typeof(GameObject), typeof(bool), typeof(bool) });
                    if (interact == null) { error("the game's Player.Interact wasn't found (a game update?)"); yield break; }
                    interact.Invoke(p, new object[] { target, false, alt });
                    yield return null; yield return null;
                    output(new JObject { ["used"] = target.name.Replace("(Clone)", ""), ["on"] = Whole(target), ["alt"] = alt, ["byCrosshair"] = target == hover });
                    yield break;
                }
                case "key":
                {
                    if (a.Length < 3) { error("screen key <tab|escape|e|key name> [frames=1]"); yield break; }
                    int frames = a.Length > 3 && int.TryParse(a[3], out int f) ? Mathf.Clamp(f, 1, 600) : 1;
                    if (!FakeKeys.Press(a[2], frames, out string what)) { error($"\"{a[2]}\" is not a key or a game button"); yield break; }
                    for (int k = 0; k < frames + 2; k++) yield return null;
                    output(new JObject { ["pressed"] = what, ["frames"] = frames });
                    yield break;
                }
                default:
                    error(UiUsage);
                    yield break;
            }
        }

        private static bool UiMatches(UiThing t, string words) =>
            string.IsNullOrEmpty(words) || t.Label.IndexOf(words, StringComparison.OrdinalIgnoreCase) >= 0 || t.Window.IndexOf(words, StringComparison.OrdinalIgnoreCase) >= 0;

        private static JObject UiJson(UiThing t) => new JObject
        {
            ["kind"] = t.Kind, ["label"] = t.Label, ["window"] = t.Window, ["on"] = t.On,
            ["x"] = Mathf.RoundToInt(t.Screen.x), ["y"] = Mathf.RoundToInt(t.Screen.y), ["w"] = Mathf.RoundToInt(t.Screen.width), ["h"] = Mathf.RoundToInt(t.Screen.height),
        };

        /// <summary>What is on screen now: buttons, toggles, sliders, text boxes and item cells (controls), and/or texts. Only what can be seen.</summary>
        private static List<UiThing> UiThings(bool controls, bool texts)
        {
            var list = new List<UiThing>();
            var screen = new Rect(0, 0, UnityEngine.Screen.width, UnityEngine.Screen.height);
            foreach (Canvas canvas in UnityEngine.Object.FindObjectsOfType<Canvas>().Where(c => c.isRootCanvas && c.enabled && c.gameObject.activeInHierarchy))
            {
                if (controls)
                {
                    foreach (Selectable s in canvas.GetComponentsInChildren<Selectable>(false))
                    {
                        if (s is Scrollbar || s.GetComponent<InventoryElement>() != null) continue;   // (an item cell is listed once, as its item)
                        string kind = s is Button ? "button" : s is Toggle ? "toggle" : s is Slider ? "slider" : s is TMP_InputField ? "input" : null;
                        if (kind == null) continue;
                        UiAdd(list, canvas, s.gameObject, kind, kind == "input" ? UiInputLabel((TMP_InputField)s) : UiLabelOf(s.gameObject), s.IsInteractable(), screen);
                    }
                    // item cells: the inventory's own (the game's grids and mods' copies), by the item in them
                    foreach (InventoryElement e in canvas.GetComponentsInChildren<InventoryElement>(false))
                    {
                        if (e.m_icon == null || !e.m_icon.enabled) continue;
                        string name = e.m_tooltip != null && !string.IsNullOrEmpty(e.m_tooltip.m_topic) ? Localization.instance.Localize(e.m_tooltip.m_topic) : "";
                        string amount = e.m_amount != null && e.m_amount.enabled ? " " + e.m_amount.text : "";
                        UIInputHandler h = e.GetComponentInChildren<UIInputHandler>();
                        UiAdd(list, canvas, h != null ? h.gameObject : e.gameObject, "item", (name + amount).Trim(), true, screen);
                    }
                }
                if (texts)
                    foreach (TMP_Text t in canvas.GetComponentsInChildren<TMP_Text>(false))
                    {
                        if (!t.enabled || string.IsNullOrWhiteSpace(UiPlain(t.text)) || t.color.a < 0.05f) continue;
                        UiAdd(list, canvas, t.gameObject, "text", UiPlain(t.text), true, screen);
                    }
            }
            return list;
        }

        private static void UiAdd(List<UiThing> list, Canvas canvas, GameObject go, string kind, string label, bool on, Rect screen)
        {
            var rt = go.transform as RectTransform;
            if (rt == null) return;
            Rect r = UiScreenRect(rt);
            // a text by its letters, not its box (a short line in a wide box would "overlap" whatever sits in the empty part)
            if (kind == "text" && go.GetComponent<TMP_Text>() is TMP_Text tt && tt.textBounds.size.x > 0.5f)
                r = UiScreenRect(rt, tt.textBounds);
            if (r.width < 1f || r.height < 1f || UiOverlap(r, screen) < 1f) return;   // (off screen: the HUD hides by moving away)
            if (UiFaded(go.transform)) return;
            // scrolled out of a list's view
            foreach (RectMask2D mask in go.GetComponentsInParent<RectMask2D>())
                if (UiOverlap(r, UiScreenRect(mask.rectTransform)) < 1f) return;
            list.Add(new UiThing { Kind = kind, Label = label ?? "", Window = UiWindowOf(canvas, go.transform), Go = go, Screen = r, On = on });
        }

        private static bool UiFaded(Transform t)
        {
            float alpha = 1f;
            foreach (CanvasGroup g in t.GetComponentsInParent<CanvasGroup>())
            {
                alpha *= g.alpha;
                if (g.ignoreParentGroups) break;
            }
            return alpha < 0.05f;
        }

        /// <summary>Screen pixels, y down from the top (overlay canvases: world corners are screen points).</summary>
        private static Rect UiScreenRect(RectTransform rt)
        {
            var c = new Vector3[4];
            rt.GetWorldCorners(c);
            Canvas canvas = rt.GetComponentInParent<Canvas>();
            Camera cam = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            Vector2 a = RectTransformUtility.WorldToScreenPoint(cam, c[0]), b = RectTransformUtility.WorldToScreenPoint(cam, c[2]);
            float x0 = Mathf.Min(a.x, b.x), x1 = Mathf.Max(a.x, b.x), y0 = Mathf.Min(a.y, b.y), y1 = Mathf.Max(a.y, b.y);
            return new Rect(x0, UnityEngine.Screen.height - y1, x1 - x0, y1 - y0);
        }

        /// <summary>Part of a rect (local bounds, as TMP's textBounds) in screen pixels, y down from the top.</summary>
        private static Rect UiScreenRect(RectTransform rt, Bounds local)
        {
            Canvas canvas = rt.GetComponentInParent<Canvas>();
            Camera cam = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            Vector2 a = RectTransformUtility.WorldToScreenPoint(cam, rt.TransformPoint(local.min)), b = RectTransformUtility.WorldToScreenPoint(cam, rt.TransformPoint(local.max));
            float x0 = Mathf.Min(a.x, b.x), x1 = Mathf.Max(a.x, b.x), y0 = Mathf.Min(a.y, b.y), y1 = Mathf.Max(a.y, b.y);
            return new Rect(x0, UnityEngine.Screen.height - y1, x1 - x0, y1 - y0);
        }

        private static float UiOverlap(Rect p, Rect q)
        {
            float w = Mathf.Min(p.xMax, q.xMax) - Mathf.Max(p.xMin, q.xMin), h = Mathf.Min(p.yMax, q.yMax) - Mathf.Max(p.yMin, q.yMin);
            return w > 0f && h > 0f ? w * h : 0f;
        }

        private static string UiWindowOf(Canvas canvas, Transform t)
        {
            // the outermost object under the canvas that isn't just a holder: what a player would call the window
            Transform w = t;
            while (w.parent != null && w.parent != canvas.transform) w = w.parent;
            return canvas.name + "/" + w.name;
        }

        private static string UiLabelOf(GameObject go)
        {
            TMP_Text t = go.GetComponentsInChildren<TMP_Text>(false).FirstOrDefault(x => x.enabled && !string.IsNullOrWhiteSpace(x.text));
            if (t != null) return UiPlain(t.text);
            Text legacy = go.GetComponentsInChildren<Text>(false).FirstOrDefault(x => x.enabled && !string.IsNullOrWhiteSpace(x.text));
            return legacy != null ? UiPlain(legacy.text) : go.name;
        }

        private static string UiInputLabel(TMP_InputField f) =>
            !string.IsNullOrEmpty(f.text) ? f.text : f.placeholder is TMP_Text p && !string.IsNullOrEmpty(p.text) ? "(" + UiPlain(p.text) + ")" : "(text box)";

        private static string UiPlain(string s) => Regex.Replace(s ?? "", "<[^>]*>", "").Replace("\n", " ").Replace("​", "").Trim();   // (an empty text box holds a zero-width space)

        private static string UiPathOf(Transform t)
        {
            var parts = new List<string>();
            for (Transform x = t; x != null && parts.Count < 6; x = x.parent) parts.Insert(0, x.name);
            return string.Join("/", parts);
        }

        private static TMP_InputField UiFocusedField()
        {
            GameObject sel = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            TMP_InputField f = sel != null ? sel.GetComponent<TMP_InputField>() : null;
            return f != null && f.isActiveAndEnabled ? f : null;
        }

        /// <summary>A real click: pointer down, up and click, as the event system sends them (buttons and the inventory's cells both answer).</summary>
        private static void UiClick(GameObject go)
        {
            var data = new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left, clickCount = 1 };
            ExecuteEvents.ExecuteHierarchy(go, data, ExecuteEvents.pointerEnterHandler);
            ExecuteEvents.ExecuteHierarchy(go, data, ExecuteEvents.pointerDownHandler);
            ExecuteEvents.ExecuteHierarchy(go, data, ExecuteEvents.pointerUpHandler);
            ExecuteEvents.ExecuteHierarchy(go, data, ExecuteEvents.pointerClickHandler);
        }
    }

    /// <summary>
    /// Keys pressed by "screen key": for a few frames the game's input (ZInput) says the key or button is down, exactly as if the player
    /// pressed it. Pressing E also says "Use" (the game's button for it), Tab "Inventory", Escape "JoyMenu"'s keyboard twin Escape.
    /// </summary>
    internal static class FakeKeys
    {
        private static readonly HashSet<string> Buttons = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<KeyCode> Keys = new HashSet<KeyCode>();
        private static int _from = -1, _until = -1;

        internal static bool Press(string name, int frames, out string what)
        {
            Buttons.Clear(); Keys.Clear();
            switch (name.ToLowerInvariant())
            {
                case "tab": case "inventory": Keys.Add(KeyCode.Tab); Buttons.Add("Inventory"); break;
                case "escape": case "esc": Keys.Add(KeyCode.Escape); break;
                case "e": case "use": Keys.Add(KeyCode.E); Buttons.Add("Use"); break;
                case "enter": case "return": Keys.Add(KeyCode.Return); break;
                default:
                    if (Enum.TryParse(name, true, out KeyCode k)) Keys.Add(k);
                    else Buttons.Add(name);   // a game button by its name ("Jump", "Map", ...)
                    break;
            }
            what = string.Join(", ", Keys.Select(k => k.ToString()).Concat(Buttons));
            _from = Time.frameCount + 1;   // (from the next frame: every reader sees the same press)
            _until = _from + frames - 1;
            return Keys.Count + Buttons.Count > 0;
        }

        private static bool Now(int frame) => frame >= _from && frame <= _until;
        private static bool Down() => Time.frameCount == _from;   // "pressed this frame": only the first frame

        [HarmonyPatch(typeof(ZInput), nameof(ZInput.GetButtonDown), typeof(string))]
        private static class ButtonDown { private static void Postfix(string name, ref bool __result) { if (!__result && Down() && Buttons.Contains(name)) __result = true; } }

        [HarmonyPatch(typeof(ZInput), nameof(ZInput.GetButton), typeof(string))]
        private static class ButtonHeld { private static void Postfix(string name, ref bool __result) { if (!__result && Now(Time.frameCount) && Buttons.Contains(name)) __result = true; } }

        [HarmonyPatch(typeof(ZInput), nameof(ZInput.GetKeyDown), typeof(KeyCode), typeof(bool))]
        private static class KeyDown { private static void Postfix(KeyCode key, ref bool __result) { if (!__result && Down() && Keys.Contains(key)) __result = true; } }

        [HarmonyPatch(typeof(ZInput), nameof(ZInput.GetKey), typeof(KeyCode), typeof(bool))]
        private static class KeyHeld { private static void Postfix(KeyCode key, ref bool __result) { if (!__result && Now(Time.frameCount) && Keys.Contains(key)) __result = true; } }

        // a mod (or the game) that "uses up" a press resets it: then it is gone for the rest of the frame, as a real one
        [HarmonyPatch(typeof(ZInput), nameof(ZInput.ResetButtonStatus), typeof(string))]
        private static class Reset
        {
            private static void Postfix(string name)
            {
                if (!Buttons.Remove(name)) return;
                if (string.Equals(name, "Inventory", StringComparison.OrdinalIgnoreCase)) Keys.Remove(KeyCode.Tab);
                if (string.Equals(name, "Use", StringComparison.OrdinalIgnoreCase)) Keys.Remove(KeyCode.E);
            }
        }
    }
}
