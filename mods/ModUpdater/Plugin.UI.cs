using System.Linq;
using UnityEngine;

namespace ModUpdater
{
    /// <summary>The F7 window. Uses Unity's immediate-mode GUI (OnGUI): simple, no assets, works everywhere.</summary>
    public partial class Plugin
    {
        private const int WindowId = 7731;

        private static readonly Color Good = new Color(0.45f, 1f, 0.55f);
        private static readonly Color Warn = new Color(1f, 0.78f, 0.3f);
        private static readonly Color Bad = new Color(1f, 0.45f, 0.45f);
        private static readonly Color Dim = new Color(0.7f, 0.7f, 0.7f);

        private Rect _window;
        private bool _windowPlaced;
        private Vector2 _scroll;
        private GUIStyle _title, _small;

        private void ToggleWindow()
        {
            WindowOpen = !WindowOpen;
            if (!WindowOpen) return;

            ScanLocal();
            BuildRows();
            // Refresh when opened if we've never checked, or it's been a while.
            if (Configured && (System.DateTime.Now - _lastRefresh).TotalSeconds > 60)
                StartCoroutine(RefreshRoutine(autoInstall: false));
        }

        private void OnGUI()
        {
            if (!WindowOpen) return;

            // Scale for big screens so the text stays readable.
            float s = Mathf.Max(1f, Screen.height / 1080f);
            GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f));

            if (!_windowPlaced)
            {
                float w = 860f, h = 620f;
                _window = new Rect((Screen.width / s - w) / 2f, (Screen.height / s - h) / 2f, w, h);
                _windowPlaced = true;
            }
            if (_title == null)
            {
                _title = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, fontSize = 15 };
                _small = new GUIStyle(GUI.skin.label) { fontSize = 11, wordWrap = true };
            }

            _window = GUI.Window(WindowId, _window, DrawWindow, "Mod Manager");
        }

        private void DrawWindow(int id)
        {
            GUILayout.BeginVertical();

            // --- top bar ---
            GUILayout.BeginHorizontal();
            GUILayout.Label(_statusLine, GUILayout.ExpandWidth(true));
            GUI.enabled = !_busy && Configured;
            if (GUILayout.Button("Refresh", GUILayout.Width(90)))
                StartCoroutine(RefreshRoutine(autoInstall: false));
            int pending = _rows.Count(NeedsUpdate);
            GUI.enabled = !_busy && pending > 0;
            if (GUILayout.Button($"Update all ({pending})", GUILayout.Width(120))) InstallAll();
            GUI.enabled = true;
            if (GUILayout.Button("Close", GUILayout.Width(70))) WindowOpen = false;
            GUILayout.EndHorizontal();

            GUILayout.Space(6);
            _scroll = GUILayout.BeginScrollView(_scroll);

            // --- mods table ---
            GUILayout.Label("Mods", _title);
            GUILayout.BeginHorizontal();
            Cell("Mod", 230, Dim); Cell("Installed", 90, Dim); Cell("GitHub", 90, Dim); Cell("Status", 200, Dim);
            GUILayout.EndHorizontal();

            foreach (Row row in _rows)
            {
                GUILayout.BeginHorizontal();
                Cell(row.Name, 230, Color.white);
                Cell(row.LocalVersion ?? "-", 90, Color.white);
                Cell(row.RemoteVersion ?? "-", 90, Color.white);
                StatusCell(row);
                if (row.Remote != null && (row.Status == Status.UpdateAvailable || row.Status == Status.NotInstalled || row.Status == Status.Rebuilt))
                {
                    GUI.enabled = !_busy;
                    if (GUILayout.Button(row.Status == Status.NotInstalled ? "Install" : "Update", GUILayout.Width(80))) InstallOne(row);
                    GUI.enabled = true;
                }
                GUILayout.EndHorizontal();

                if (!string.IsNullOrEmpty(row.Description))
                {
                    GUI.contentColor = Dim;
                    GUILayout.Label("    " + row.Description, _small);
                    GUI.contentColor = Color.white;
                }
            }
            if (_rows.Count == 0) GUILayout.Label("No mods found yet. Press Refresh.");

            GUILayout.Space(14);
            DrawPlayers();

            GUILayout.EndScrollView();
            GUILayout.EndVertical();

            GUI.DragWindow(new Rect(0, 0, 10000, 22));
        }

        private void DrawPlayers()
        {
            GUILayout.Label("Players", _title);
            if (_remote.Length == 0)
            {
                GUILayout.Label("Refresh to compare against the versions on GitHub.", _small);
                return;
            }

            if (ZRoutedRpc.instance == null || Player.m_localPlayer == null)
            {
                GUILayout.Label("Join a world to see what the other players have.", _small);
                return;
            }

            DrawPlayerRow("You", _local.ToDictionary(m => m.Guid, m => m.Version, System.StringComparer.Ordinal));

            var others = OtherPlayers();
            if (others.Count == 0) GUILayout.Label("Nobody else is in this world right now.", _small);

            foreach (var kv in others)
            {
                if (_peers.TryGetValue(kv.Key, out PeerMods peer))
                    DrawPlayerRow(kv.Value, peer.Versions);
                else
                {
                    GUILayout.BeginHorizontal();
                    Cell(kv.Value, 110, Color.white);
                    Cell("no mod manager installed (or still loading) - press Refresh", 520, Bad);
                    GUILayout.EndHorizontal();
                }
            }
        }

        /// <summary>One line per player: each mod from GitHub, coloured by whether they have the latest version.</summary>
        private void DrawPlayerRow(string who, System.Collections.Generic.Dictionary<string, string> versions)
        {
            GUILayout.BeginHorizontal();
            Cell(who, 110, Color.white);
            foreach (RemoteMod r in _remote)
            {
                if (!versions.TryGetValue(r.guid, out string v)) Cell($"{r.name}: missing", 190, Bad);
                else if (CompareVersions(v, r.version) >= 0) Cell($"{r.name}: v{v}", 190, Good);
                else Cell($"{r.name}: v{v} (old)", 190, Warn);
            }
            GUILayout.EndHorizontal();
        }

        private static void Cell(string text, float width, Color color)
        {
            GUI.contentColor = color;
            GUILayout.Label(text, GUILayout.Width(width));
            GUI.contentColor = Color.white;
        }

        private static void StatusCell(Row row)
        {
            switch (row.Status)
            {
                case Status.UpToDate: Cell("Up to date", 200, Good); break;
                case Status.UpdateAvailable: Cell("Update available", 200, Warn); break;
                case Status.NotInstalled: Cell("Not installed", 200, Bad); break;
                case Status.LocalNewer: Cell("Newer than GitHub (unpublished)", 200, Dim); break;
                case Status.Rebuilt: Cell("Differs from GitHub (same version)", 200, Warn); break;
                default: Cell("Local only", 200, Dim); break;
            }
        }
    }
}
