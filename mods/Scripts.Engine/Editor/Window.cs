using System;
using BepInEx.Configuration;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Scripts.Editor
{
    /// <summary>
    /// The scripting program in a window of its own, for anyone with no computer built: the game's wood panel over the world (the same
    /// look as the inventory), the same 1280 x 720 screen inside it that the Terminal shows. Home opens and closes it (a setting), as does
    /// the console command "blockscript". Escape goes back a step, then closes.
    /// </summary>
    internal static class ScriptsWindow
    {
        internal static ConfigEntry<KeyboardShortcut> Hotkey;

        private static RectTransform _window, _content;
        private static EditorScreen _screen;
        private static float _closedAt, _openedAt;
        private static string _picked = "";

        internal static bool IsOpen => _window != null;

        private sealed class Host : IEditorHost
        {
            public void Close() { ScriptsWindow.Close(); }
            public string Picked { get { return _picked; } set { _picked = value ?? ""; } }
        }

        internal static void Open()
        {
            if (IsOpen || Time.unscaledTime < _closedAt + 0.2f) return;
            if (Player.m_localPlayer == null || !GameUi.Ready()) return;
            if (InventoryGui.IsVisible()) InventoryGui.instance.Hide();
            GameUi.IsOpen = () => IsOpen;
            GameUi_EscapeCloses.Close = () => { if (_screen != null) _screen.Escape(); };
            _window = GameUi.Window("BlockscriptWindow", new Vector2(1320f, 760f));
            _content = GameUi.Rect(_window, "Content", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(20f, -20f), new Vector2(1280f, 720f));
            _screen = new EditorScreen(_content, new Host(), GameUi.Sans);
            _openedAt = Time.unscaledTime;
        }

        internal static void Close()
        {
            if (_screen != null) _screen.Destroy();
            _screen = null;
            if (_window != null) Object.Destroy(_window.gameObject);
            _window = null; _content = null;
            _closedAt = Time.unscaledTime;
            UITooltip.HideTooltip();
        }

        /// <summary>Every frame, on every game (a client sees the window too, read-only).</summary>
        internal static void Tick()
        {
            try { TickInner(); }
            catch (Exception e)
            {
                // a fault in the window must never stop the scripts: say so once, close it, and carry on
                Core.Log.LogError("The scripts window failed and was closed: " + e);
                try { Close(); } catch (Exception) { }
                _closedAt = Time.unscaledTime + 5f;
            }
        }

        private static void TickInner()
        {
            if (Hotkey != null && Hotkey.Value.IsDown() && !GameUi.Typing && !global::Console.IsVisible() && !TextInput.IsVisible() && !Chat.instance.HasFocus())
            {
                if (IsOpen) Close(); else Open();
            }
            if (!IsOpen) return;
            if (Player.m_localPlayer == null || Player.m_localPlayer.IsDead() || global::Menu.IsVisible()) { Close(); return; }
            if (!TextInput.IsVisible())
            {
                Vector2 local;
                RectTransformUtility.ScreenPointToLocalPointInRectangle(_content, Input.mousePosition, null, out local);
                Vector2 p = new Vector2(local.x, -local.y);
                _screen.Pointer(p, Input.GetMouseButtonDown(0), Input.GetMouseButtonUp(0));
                if (_screen == null) return;   // (a click on Close closed the window right there)
                _screen.Wheel(p, Wheel());
                if (_screen == null) return;
                Keys();
                if (_screen == null) return;
            }
            if (_screen != null) _screen.Draw();
        }

        /// <summary>
        /// Keys while the window is open and no text box is up: Ctrl+Z undo, Ctrl+Y redo, Ctrl+C remember the selected block, Ctrl+V paste
        /// it, Delete remove it, Ctrl+F search (the blocks, or the code). Read through BepInEx's input, like the wheel.
        /// </summary>
        private static void Keys()
        {
            try
            {
                var input = BepInEx.UnityInput.Current;
                bool ctrl = input.GetKey(KeyCode.LeftControl) || input.GetKey(KeyCode.RightControl);
                if (ctrl)
                {
                    if (input.GetKeyDown(KeyCode.Z)) _screen.Shortcut("undo");
                    else if (input.GetKeyDown(KeyCode.Y)) _screen.Shortcut("redo");
                    else if (input.GetKeyDown(KeyCode.C)) _screen.Shortcut("copy");
                    else if (input.GetKeyDown(KeyCode.V)) _screen.Shortcut("paste");
                    else if (input.GetKeyDown(KeyCode.F)) _screen.Shortcut("search");
                }
                else if (input.GetKeyDown(KeyCode.Delete)) _screen.Shortcut("delete");
            }
            catch (Exception) { }
        }

        /// <summary>The mouse wheel this frame. Read through BepInEx's input (Unity's own getter threw in this game); the game's own is zeroed under a window.</summary>
        private static float Wheel()
        {
            try { return BepInEx.UnityInput.Current.mouseScrollDelta.y; }
            catch (Exception) { return 0f; }
        }

        /// <summary>Claude Tools: open the window on a page.</summary>
        internal static string Go(string page, string file, string extra)
        {
            if (!IsOpen) Open();
            return _screen == null ? "the window could not open (is a world loaded?)" : _screen.Go(page, file, extra);
        }

        internal static void Destroy()
        {
            Close();
            GameUi.Destroy();
        }
    }
}
