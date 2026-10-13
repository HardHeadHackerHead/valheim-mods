using System;
using System.Reflection;
using TMPro;
using UnityEngine;
using static DHack.Shared.Translations;

namespace Terminal
{
    /// <summary>
    /// The Scripts program: the scripting editor (docs/scripting-design.md) on the computer's screen. The editor itself (blocks, the list of
    /// scripts, code, powers, log) is drawn and handled by the Scripts mod; this program only gives it a 1280 x 720 area in its window and
    /// the driver's pointer (position, button, wheel). It reaches the Scripts mod through Scripts.HostBridge, found by name, so the Terminal
    /// works without that mod (the window then says so) and the mod works without a computer (its own window). The editor is one per game,
    /// shared by whichever terminal is in front; the script picked is kept in the terminal's saved data (AppText).
    /// </summary>
    internal sealed class ScriptsApp : App
    {
        internal const string AppId = "scripts";
        internal override string Id => AppId;
        internal override string Title => "Blockscript";
        internal override string Glyph => "JS";
        internal override Color Tint => new Color(0.4f, 0.3f, 0.65f);
        internal override float LoadSeconds(TerminalPiece terminal) => 1.2f;
        internal override bool WantsCursor => true;
        internal override bool HasWindow => true;

        private static Type _bridge;
        private static MethodInfo _create, _draw, _pointer, _wheel, _escape, _destroy, _canChange;
        private static bool _looked;
        private static object _editor;
        private static TerminalPiece _terminal;      // the terminal the editor is showing for now
        private static RectTransform _area;
        private static TextMeshProUGUI _note;

        private static void Look()
        {
            if (_looked && _bridge != null) return;
            _looked = true;
            foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies()) { Type t = a.GetType("Scripts.HostBridge", false); if (t != null) { _bridge = t; break; } }
            if (_bridge == null) return;
            MethodInfo M(string name) => _bridge.GetMethod(name, BindingFlags.Public | BindingFlags.Static);
            _create = M("EditorCreate"); _draw = M("EditorDraw"); _pointer = M("EditorPointer"); _wheel = M("EditorWheel");
            _escape = M("EditorEscape"); _destroy = M("EditorDestroy"); _canChange = M("CanChange");
        }

        private static bool Available { get { Look(); return _create != null && _draw != null && _pointer != null; } }

        private static object Invoke(MethodInfo m, params object[] args)
        {
            if (m == null) return null;
            try { return m.Invoke(null, args); }
            catch (TargetInvocationException e) { Plugin.Log.LogWarning("Scripts editor " + m.Name + ": " + e.InnerException); return null; }
        }

        // ---- the window ----

        internal override void BuildWindow(Transform root)
        {
            _area = new GameObject("Editor", typeof(RectTransform)) { layer = root.gameObject.layer }.GetComponent<RectTransform>();
            _area.SetParent(root, false);
            _area.anchorMin = _area.anchorMax = new Vector2(0f, 1f);
            _area.pivot = new Vector2(0f, 1f);
            _area.anchoredPosition = Vector2.zero;
            _area.sizeDelta = new Vector2(1280f, 720f);
            _note = Desktop.Label("Missing", root, "", 26, new Color(0.85f, 0.87f, 0.92f), TextAlignmentOptions.Center, 140f, 300f, 1000f, 80f);
        }

        /// <summary>Makes the editor the first time it is needed (the Scripts mod may load after the Terminal).</summary>
        private static bool Ensure(TerminalPiece t)
        {
            _terminal = t;
            if (_editor != null) return true;
            if (!Available || _area == null) return false;
            _editor = Invoke(_create, _area, Desktop.Font,
                (Action)(() => { if (_terminal != null) _terminal.CloseApp(AppId); }),
                (Func<string>)(() => _terminal != null ? _terminal.AppText : ""),
                (Action<string>)(s => { if (_terminal != null) _terminal.SetAppText(s ?? ""); }));
            return _editor != null;
        }

        internal override void UpdateWindow(TerminalPiece t, bool driver)
        {
            if (_note == null) return;
            if (!Available) { _note.text = T("terminal_scripts_missing"); return; }
            _note.text = "";
            if (!Ensure(t)) return;
            Invoke(_draw, _editor);
        }

        // ---- the driver's pointer ----

        internal override void Pointer(TerminalPiece t, Vector2 at, bool down, bool up)
        {
            if (!Ensure(t)) return;
            Invoke(_pointer, _editor, at.x * 1280f, at.y * 720f, down, up);
        }

        internal override void Wheel(TerminalPiece t, Vector2 at, float delta)
        {
            if (!Ensure(t)) return;
            Invoke(_wheel, _editor, at.x * 1280f, at.y * 720f, delta);
        }

        /// <summary>Escape goes back a step inside the editor (closes a menu or box, leaves the blocks for the list); at the list the editor closes the program itself.</summary>
        internal override bool Escape(TerminalPiece t)
        {
            if (!Ensure(t)) return false;
            Invoke(_escape, _editor);
            return true;
        }

        internal override void Teardown()
        {
            if (_editor != null) Invoke(_destroy, _editor);
            _editor = null; _area = null; _note = null; _terminal = null;
            _looked = false; _bridge = null;
        }
    }
}
