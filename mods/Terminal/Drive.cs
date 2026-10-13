using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using static DHack.Shared.Translations;

namespace Terminal
{
    /// <summary>
    /// Sitting at a terminal. The player is attached to the stool (the game's own sitting, with its camera held on the monitor) and
    /// everything they press arrives through Player.SetControls, where a prefix takes it and zeroes it for the player, so they never stand
    /// up by accident (pitfall: an attached player lets go on any input). What it means depends on the screen:
    ///
    ///   desktop     the mouse moves a cursor, the attack button clicks (double-click an icon to start it)
    ///   Valheim     the keys steer the Proxy (this game takes its ownership); 1-8 use what it carries, Shift+1-8 drop it, E uses what
    ///               it stands at (a door, a plant) or, with nothing there, stands up; Escape goes back to the desktop
    ///
    /// The terminal's owner (this game, once somebody sits) runs its state (TerminalPiece.cs); the sitter and the Proxy's driver both beat
    /// once a second in the saved data, so one who vanishes frees what they held after a few seconds.
    /// </summary>
    internal static class Drive
    {
        internal static TerminalPiece Current;
        private static ProxyBody _proxy;
        private static float _since, _nextBeat, _nextPrompt;
        private static float _nearClip = -1f;   // the game camera's own near clip, put back when the player stands up
        private static Vector2 _cursor = new Vector2(0.5f, 0.4f);
        private static string _prompt = "";

        // A test command's stand-in keys (Tools.cs): used instead of the real ones until the time is up.
        internal static float TestUntil;
        internal static Vector3 TestMove;
        internal static bool TestRun, TestJump, TestAttack;
        internal static float TestYaw = float.NaN, TestPitch;

        internal static bool Active => Current != null;

        /// <summary>The Valheim app has the front: the keys steer its Proxy (any other app, or the desktop, leaves the Proxy alone).</summary>
        internal static bool GameFront => Current != null && Current.State == TerminalPiece.Mode.App && Apps.Get(Current.RunningApp) is ValheimApp;

        /// <summary>The cursor to draw on a terminal: the sitter's own, straight from their mouse, else the shared one.</summary>
        internal static Vector2 CursorFor(TerminalPiece t) => Current == t ? _cursor : t.Cursor;

        /// <summary>This game's player is sitting at the terminal and steering that Proxy.</summary>
        internal static bool Uses(ProxyBody proxy) => Current != null && _proxy == proxy;

        internal static bool Begin(Player player, TerminalPiece terminal)
        {
            if (Current != null || terminal == null || terminal.Seat == null || terminal.Nview == null || terminal.Nview.GetZDO() == null) return false;
            if (player.IsAttached() || player.IsDead() || player.IsEncumbered()) return false;
            try
            {
                string me = player.GetPlayerName();
                if (terminal.BusyFor(me))
                {
                    player.Message(MessageHud.MessageType.Center, T("terminal_hover_busy", terminal.User));
                    return false;
                }
                terminal.SetUser(me);   // (claims the terminal's saved data: this game runs its state from now on)
                terminal.PowerOn();
                player.AttachStart(terminal.Seat, null, false, false, false, "attach_chair", new Vector3(0f, 0.5f, 0f), terminal.ViewPoint);
                Current = terminal;
                _proxy = null;
                _since = Time.time;
                _nextBeat = Time.time + 1f;
                _cursor = terminal.Cursor;
                CloseUp(true);
                return true;
            }
            catch (System.Exception e) { Plugin.Log.LogWarning("Could not sit down at the terminal: " + e); Current = null; _proxy = null; return false; }
        }

        /// <summary>
        /// The game camera hides anything nearer than half a metre (m_nearClipPlaneMax), and the monitor is 0.4 m from the sitter's eyes:
        /// without this the screen is clipped away and the sitter sees the world behind it. Held at 0.08 m while sitting.
        /// </summary>
        private static void CloseUp(bool on)
        {
            GameCamera cam = GameCamera.instance;
            if (cam == null) return;
            if (on)
            {
                if (_nearClip < 0f) _nearClip = cam.m_nearClipPlaneMax;
                cam.m_nearClipPlaneMax = 0.08f;
            }
            else if (_nearClip >= 0f)
            {
                cam.m_nearClipPlaneMax = _nearClip;
                _nearClip = -1f;
            }
            Camera c = cam.GetComponent<Camera>();
            if (c != null) c.nearClipPlane = on ? 0.08f : cam.m_nearClipPlaneMax;
        }

        internal static void End(string why)
        {
            if (Current == null) return;
            Player player = Player.m_localPlayer;
            try
            {
                if (_proxy != null && _proxy.View != null && _proxy.View.GetZDO() != null)
                {
                    if (_proxy.IsOwner) _proxy.Feed(Vector3.zero, false, false, false, false, false, false, false, _proxy.transform.forward);
                    if (_proxy.Driver == player?.GetPlayerName()) _proxy.SetDriver("");
                }
                if (Current.Nview != null && Current.Nview.GetZDO() != null && Current.User == player?.GetPlayerName()) Current.SetUser("");
                if (player != null && player.IsAttached()) player.AttachStop();
            }
            catch (System.Exception e) { Plugin.Log.LogWarning("Stand up: " + e.Message); }
            CloseUp(false);
            Plugin.Log.LogInfo("Stood up from the terminal (" + why + ")");
            Current = null;
            _proxy = null;
            TestUntil = 0f;
        }

        private static bool Typing() => Console.IsVisible() || Menu.IsVisible() || TextInput.IsVisible() || (Chat.instance != null && Chat.instance.HasFocus());

        internal static void Update()
        {
            if (Current == null) return;
            Player player = Player.m_localPlayer;
            if (player == null || !player.IsAttached() || player.IsDead() || Current.Nview == null || Current.Nview.GetZDO() == null)
            { End("lost"); return; }
            string me = player.GetPlayerName();
            TerminalPiece.Mode mode = Current.State;
            bool game = GameFront;
            _proxy = Current.FindProxy();

            // the Proxy has a driver while the game is in front, and none while it is in the background
            if (_proxy != null && _proxy.View != null && _proxy.View.GetZDO() != null)
            {
                if (game)
                {
                    if (!_proxy.IsOwner) _proxy.View.ClaimOwnership();   // its keys and physics run here now
                    if (_proxy.Driver != me) _proxy.SetDriver(me);
                }
                else if (_proxy.Driver == me)
                {
                    if (_proxy.IsOwner) _proxy.Feed(Vector3.zero, false, false, false, false, false, false, false, _proxy.transform.forward);
                    _proxy.SetDriver("");
                }
            }

            if (Time.time >= _nextBeat)
            {
                _nextBeat = Time.time + 1f;
                Current.BeatUser();
                if (game && _proxy != null && _proxy.IsOwner) { _proxy.Beat(); Tether(); }
            }
            if (Typing()) return;

            // the desktop, and apps that want the mouse: a cursor and clicks (and the wheel)
            App front = mode == TerminalPiece.Mode.App ? Apps.Get(Current.RunningApp) : null;
            if (mode == TerminalPiece.Mode.Desktop || (front != null && front.WantsCursor))
            {
                Vector2 d = ZInput.GetMouseDelta();
                if (d != Vector2.zero)
                {
                    float speed = Plugin.MouseSpeed.Value;
                    _cursor = new Vector2(Mathf.Clamp01(_cursor.x + d.x * 0.0026f * speed), Mathf.Clamp01(_cursor.y - d.y * 0.0044f * speed));
                    Current.SetCursor(_cursor);
                }
                if (ZInput.GetButtonDown("Attack")) Desktop.Click(Current, _cursor);
                if (front != null)
                {
                    float wheel = ZInput.GetMouseScrollWheel();
                    front.Scroll(Current, wheel);
                    front.Wheel(Current, _cursor, wheel);
                    front.Pointer(Current, _cursor, ZInput.GetButtonDown("Attack"), ZInput.GetButtonUp("Attack"));
                }
            }
            else if (game)
            {
                float wheel = ZInput.GetMouseScrollWheel();
                if (Mathf.Abs(wheel) > 0.001f) View.Zoom(-wheel * 6f);   // (the wheel moves the camera in and out, as in the game's own third person)
            }

            // E: what the Proxy stands at, or stand up
            if (Time.time - _since > 0.6f && (ZInput.GetButtonDown("Use") || ZInput.GetButtonDown("JoyUse")))
            {
                if (game && _proxy != null && _proxy.TryInteract()) { }
                else { End("use"); return; }
            }
        }

        /// <summary>Past the tether the Proxy would stand in ground that isn't loaded: it is brought back to the terminal.</summary>
        private static void Tether()
        {
            if (Current == null || _proxy == null || !_proxy.IsOwner) return;
            if (Vector3.Distance(_proxy.transform.position, Current.transform.position) <= Mathf.Min(Plugin.Tether.Value, 120f)) return;
            _proxy.PlaceAt(Current.SpawnPoint.position);
            Player.m_localPlayer?.Message(MessageHud.MessageType.Center, T("terminal_proxy_far"));
        }

        /// <summary>What the player pressed this tick (or a test's stand-in keys), handed to the Proxy while the game is in front.</summary>
        internal static void Feed(Player player, Vector3 move, bool run, bool jump, bool attack, bool attackHold, bool secondary, bool secondaryHold, bool blockHold, bool crouch = false)
        {
            if (!GameFront || _proxy == null || !_proxy.IsOwner) return;
            Vector3 look = player.GetLookDir();
            if (Time.time < TestUntil)
            {
                move = TestMove; run = TestRun; jump = TestJump; attack = TestAttack; attackHold = TestAttack;
                if (!float.IsNaN(TestYaw)) look = Quaternion.Euler(-TestPitch, TestYaw, 0f) * Vector3.forward;
                TestJump = false; TestAttack = false;   // (one press each)
            }
            _proxy.Feed(move, run, jump, attack, attackHold, secondary, secondaryHold, blockHold, look, crouch);
        }

        /// <summary>The number keys 1 to 8 (Shift: drop), while the game is in front.</summary>
        internal static bool Hotbar(int number)
        {
            if (Current == null) return false;
            if (!GameFront || _proxy == null) return true;   // (the number keys do nothing else while sitting)
            _proxy.UseSlot(number, ZInput.GetKey(KeyCode.LeftShift) || ZInput.GetKey(KeyCode.RightShift));
            return true;
        }

        /// <summary>Escape in front of a game goes to the desktop (and the game's own menu is left for the desktop's own Escape).</summary>
        internal static bool Escape()
        {
            if (Current == null || Current.State != TerminalPiece.Mode.App || TextInput.IsVisible()) return false;
            App front = Apps.Get(Current.RunningApp);
            if (front != null && front.Escape(Current)) return true;
            Current.ToDesktop();
            return true;
        }

        // ---- the panel over the screen while the game is in front ----

        private static GUIStyle _label, _title;

        internal static void Overlay()
        {
            if (!GameFront || _proxy == null || _proxy.Body == null || Typing()) return;
            if (_label == null)
            {
                _label = new GUIStyle(GUI.skin.label) { fontSize = 15, normal = { textColor = Color.white } };
                _title = new GUIStyle(_label) { fontStyle = FontStyle.Bold, fontSize = 17 };
            }
            List<ItemDrop.ItemData> slots = _proxy.Slots();
            float x = 24f, y = 150f, w = 270f, h = 58f + 22f * slots.Count;
            GUI.Box(new Rect(x - 8f, y - 6f, w, h), GUIContent.none);
            GUI.Label(new Rect(x, y, w - 16f, 24f), Localization.instance.Localize(_proxy.Body.m_name), _title);
            GUI.Label(new Rect(x, y + 22f, w - 16f, 22f), Mathf.CeilToInt(_proxy.Body.GetHealth()) + " / " + Mathf.CeilToInt(_proxy.Body.GetMaxHealth()), _label);
            for (int i = 0; i < slots.Count; i++)
            {
                ItemDrop.ItemData item = slots[i];
                string line = (i + 1) + "   " + Localization.instance.Localize(item.m_shared.m_name) + (item.m_stack > 1 ? "  x" + item.m_stack : "") + (item.m_equipped ? "   *" : "");
                GUI.Label(new Rect(x, y + 46f + 22f * i, w - 16f, 22f), line, _label);
            }
            GUI.Label(new Rect(x, y + 48f + 22f * slots.Count, w - 16f, 22f), T("terminal_slots_hint"), _label);

            if (Time.time >= _nextPrompt)
            {
                _nextPrompt = Time.time + 0.2f;
                _proxy.FindInteractable(out string hover);
                _prompt = Localization.instance.Localize(hover);
            }
            string prompt = _prompt.Length > 0 ? _prompt : "[E] " + T("terminal_stand");
            GUI.Label(new Rect(Screen.width / 2f - 200f, Screen.height - 96f, 400f, 26f), prompt, new GUIStyle(_title) { alignment = TextAnchor.MiddleCenter });
        }
    }

    // Everything the player presses while sitting goes to the Proxy (or the desktop), and none of it to the player (who would stand up).
    [HarmonyPatch(typeof(Player), nameof(Player.SetControls))]
    internal static class Player_SetControls
    {
        private static void Prefix(Player __instance, ref Vector3 movedir, ref bool attack, ref bool attackHold, ref bool secondaryAttack, ref bool secondaryAttackHold,
            ref bool block, ref bool blockHold, ref bool jump, ref bool crouch, ref bool run, ref bool autoRun, ref bool dodge)
        {
            if (!Drive.Active || __instance != Player.m_localPlayer) return;
            try { Drive.Feed(__instance, movedir, run, jump, attack, attackHold, secondaryAttack, secondaryAttackHold, blockHold, crouch); }
            catch (System.Exception e) { Plugin.Log.LogWarning("Terminal keys: " + e.Message); }
            movedir = Vector3.zero;
            attack = attackHold = secondaryAttack = secondaryAttackHold = block = blockHold = jump = crouch = run = autoRun = dodge = false;
        }
    }

    // The number keys are the Proxy's bag while the game is in front.
    [HarmonyPatch(typeof(Player), nameof(Player.UseHotbarItem))]
    internal static class Player_UseHotbarItem
    {
        private static bool Prefix(Player __instance, int index) => !(Drive.Active && __instance == Player.m_localPlayer && Drive.Hotbar(index));
    }

    // Escape in front of a game minimises it; on the desktop it opens the game's own menu as usual.
    [HarmonyPatch(typeof(Menu), nameof(Menu.Show))]
    internal static class Menu_Show
    {
        private static bool Prefix() => !Drive.Escape();
    }
}
