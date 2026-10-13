using System.Collections.Generic;
using UnityEngine;

namespace Terminal
{
    /// <summary>
    /// What a terminal can run. The desktop (Desktop.cs) shows an icon for every app registered here and runs one at a time; Valheim is
    /// the first. Another app is a class that fills in this one's blanks and a line in Apps.Register(): its icon, its name (a "$" key or
    /// plain words), how long it takes to open, what it does when it has loaded (on the game that owns the terminal), and how it draws.
    ///
    /// An app draws one of two ways: it paints its own picture into the screen's texture (DrawsItself: Valheim's live view), or it
    /// leaves the window area to the desktop (not yet used: a future app that is plain text and buttons would add its window to
    /// Desktop.cs). Everything an app keeps lives in the terminal's saved data under its own keys, so every player sees it.
    /// </summary>
    internal abstract class App
    {
        internal abstract string Id { get; }
        /// <summary>The words under its icon (a "$" key is translated by the game).</summary>
        internal abstract string Title { get; }
        /// <summary>The picture on its icon: a letter or two.</summary>
        internal abstract string Glyph { get; }
        internal abstract Color Tint { get; }

        /// <summary>How long the loading screen shows when this app is started on this terminal, in seconds.</summary>
        internal virtual float LoadSeconds(TerminalPiece terminal) => 3f;

        /// <summary>The loading screen has finished (runs on the game that owns the terminal).</summary>
        internal virtual void Loaded(TerminalPiece terminal) { }

        /// <summary>The app wants the mouse (a cursor, clicks, the wheel) while it is in front, as the desktop does. Valheim wants the keys instead.</summary>
        internal virtual bool WantsCursor => false;

        /// <summary>The app has a window the desktop draws (built once by BuildWindow, updated every picture by UpdateWindow).</summary>
        internal virtual bool HasWindow => false;
        internal virtual void BuildWindow(Transform root) { }
        internal virtual void UpdateWindow(TerminalPiece terminal, bool driver) { }

        /// <summary>A click in the window (0 to 1 across and down), worked out while the desktop's camera has the texture (so UI hit tests line up).</summary>
        internal virtual void Click(TerminalPiece terminal, Vector2 at, Camera camera, RenderTexture target) { }

        /// <summary>The mouse wheel turned (positive: away from you).</summary>
        internal virtual void Scroll(TerminalPiece terminal, float delta) { }

        /// <summary>The driver's pointer in the window every frame (0 to 1 across and down, from their own cursor), and whether the button went down or came up this frame. For apps that need drags.</summary>
        internal virtual void Pointer(TerminalPiece terminal, Vector2 at, bool down, bool up) { }

        /// <summary>The mouse wheel with the place it turned at (0 to 1 across and down).</summary>
        internal virtual void Wheel(TerminalPiece terminal, Vector2 at, float delta) { }

        /// <summary>Escape pressed in front of this app: true if the app used it (the desktop does not then show).</summary>
        internal virtual bool Escape(TerminalPiece terminal) => false;

        /// <summary>The desktop is being torn down (a reload): let go of anything the app made.</summary>
        internal virtual void Teardown() { }

        /// <summary>The app is running in front: draw its picture into the texture. False: nothing to show (the desktop is drawn instead).</summary>
        internal virtual bool Draw(TerminalPiece terminal, RenderTexture target, bool driver) => false;
    }

    internal static class Apps
    {
        internal static readonly List<App> All = new List<App>();

        internal static void Register()
        {
            All.Clear();
            All.Add(new ValheimApp());
            All.Add(new BrowserApp());
            All.Add(new ScriptsApp());
        }

        internal static void Clear() => All.Clear();

        internal static App Get(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (App app in All) if (app.Id == id) return app;
            return null;
        }
    }

    /// <summary>
    /// Valheim: launching it plays a loading screen (as long as the Proxy needs to come back after a death, at least a few seconds) and
    /// then makes the terminal's Proxy; the app's picture is the Proxy's view (View.cs).
    /// </summary>
    internal sealed class ValheimApp : App
    {
        internal const string AppId = "valheim";
        internal override string Id => AppId;
        internal override string Title => "Valheim";
        internal override string Glyph => "V";
        internal override Color Tint => new Color(0.16f, 0.45f, 0.5f);

        internal override float LoadSeconds(TerminalPiece terminal)
        {
            if (terminal.FindProxy() != null) return 0f;   // already running in the world: just bring it to the front
            return terminal.Recovering(out int left) ? Mathf.Max(4f, left) : 4f;
        }

        internal override void Loaded(TerminalPiece terminal)
        {
            if (terminal.FindProxy() == null) terminal.SpawnProxy();
        }

        internal override bool Draw(TerminalPiece terminal, RenderTexture target, bool driver)
        {
            ProxyBody proxy = terminal.FindProxy();
            return proxy != null && View.Render(proxy, target);
        }
    }
}
