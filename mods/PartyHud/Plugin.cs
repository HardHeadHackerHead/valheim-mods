using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using UnityEngine;

namespace PartyHud
{
    /// <summary>
    /// A party panel on the right edge of the screen: every player's Steam picture, name, health, stamina (and Eitr).
    ///
    /// Each player's game broadcasts its own stats a few times a second over Valheim's routed-RPC messages
    /// (the server just relays them), so this works at any distance, with correct maximums. Players without the
    /// mod still show up using what the game shares for nearby players.
    ///
    /// Split across files: Plugin.cs (setup + who to show), Plugin.Net.cs (messages), Plugin.Draw.cs (the panel),
    /// SteamAvatars.cs (profile pictures).
    /// </summary>
    [BepInPlugin(Guid, Name, Version)]
    public partial class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.dhack.partyhud";
        public const string Name = "PartyHud";
        public const string Version = "1.2.2";

        private ConfigEntry<bool> _enabled, _showSelf, _showPortraits, _showDistance, _hideInMenus;
        private ConfigEntry<float> _offsetX, _offsetY, _scale, _opacity;
        private ConfigEntry<int> _maxRows;
        private ConfigEntry<KeyboardShortcut> _toggleKey;
        private bool _visible = true;

        /// <summary>One row in the panel.</summary>
        private class Member
        {
            public long Id;
            public ulong SteamId;
            public string Name;
            public bool Self, HasData;
            public float Hp, MaxHp, St, MaxSt, Eitr, MaxEitr;
            public float Distance = -1f;
        }

        private List<Member> _members = new List<Member>();
        private float _nextMembers;
        private int _awakeFrame;

        private void Awake()
        {
            _enabled = Config.Bind("General", "Enabled", true, "Show the party panel.");
            _showSelf = Config.Bind("General", "ShowSelf", true, "Include yourself in the panel.");
            _showPortraits = Config.Bind("General", "ShowPortraits", true,
                "Show each player's Steam profile picture. Off (or no picture available) = a coloured initial.");
            _showDistance = Config.Bind("General", "ShowDistance", true, "Show how far away each player is.");
            _hideInMenus = Config.Bind("General", "HideInMenus", true,
                "Hide the panel while the inventory, crafting window, build menu, trader or big map is open, so it doesn't overlap them.");
            _maxRows = Config.Bind("General", "MaxPlayers", 8, "Most players to show (you first, then the rest alphabetically).");
            _toggleKey = Config.Bind("General", "ToggleKey", new KeyboardShortcut(KeyCode.F8), "Press to hide or show the panel (handy for screenshots).");
            _offsetX = Config.Bind("Layout", "OffsetX", 10f, "Gap from the right edge of the screen (UI pixels).");
            _offsetY = Config.Bind("Layout", "OffsetY", 300f, "Gap from the top of the screen (UI pixels). Raise it if it overlaps your minimap.");
            _scale = Config.Bind("Layout", "Scale", 1f, "Size of the panel (1 = normal, 1.25 = bigger).");
            _opacity = Config.Bind("Layout", "Opacity", 0.85f, "How solid the panel background is (0.2 to 1).");

            _awakeFrame = Time.frameCount;
            Logger.LogInfo($"{Name} {Version} loaded");

            // Awake with a player already in the world means this was a hot reload.
            if (Player.m_localPlayer != null && Chat.instance != null)
                Chat.instance.AddString("[Mod]", $"{Name} v{Version} reloaded", Talker.Type.Normal);
        }

        // ScriptEngine destroys this copy when mods reload: take back everything we hooked into the game.
        private void OnDestroy()
        {
            UnregisterRpc();
            _remote.Clear();
            SteamAvatars.Clear();
            DestroyDrawResources();
        }

        private void Update()
        {
            if (!_enabled.Value) return;

            // (Not IsDown(): that ignores the key while another modifier, like the Shift you hold to run, is down.)
            if (_toggleKey.Value.MainKey != KeyCode.None && Input.GetKeyDown(_toggleKey.Value.MainKey) && _toggleKey.Value.Modifiers.All(Input.GetKey))
                _visible = !_visible;

            UpdateNetwork();

            if (Player.m_localPlayer == null || ZNet.instance == null) return;
            if (Time.time >= _nextMembers)
            {
                _nextMembers = Time.time + 0.2f;
                BuildMembers();
            }
        }

        private static long MyId => ZDOMan.GetSessionID();

        /// <summary>A player's Steam ID from the game's cross-platform user id, or 0 (not on Steam, e.g. Xbox).</summary>
        private static ulong SteamIdOf(ZNet.PlayerInfo info)
        {
            try
            {
                Splatform.PlatformUserID id = info.m_userInfo.m_id;
                if (id.IsValid && string.Equals(id.m_platform.ToString(), "Steam", StringComparison.OrdinalIgnoreCase)
                    && id.TryParseAsUInt64(out ulong steam))
                    return steam;
            }
            catch (Exception) { }
            return 0;
        }

        /// <summary>Work out who to show and their current numbers. Runs 5 times a second, not every frame.</summary>
        private void BuildMembers()
        {
            Player me = Player.m_localPlayer;
            var list = new List<Member>();

            if (_showSelf.Value)
            {
                list.Add(new Member
                {
                    Id = MyId, Name = me.GetPlayerName(), Self = true, HasData = true, SteamId = SteamAvatars.Own(),
                    Hp = me.GetHealth(), MaxHp = me.GetMaxHealth(),
                    St = me.GetStamina(), MaxSt = me.GetMaxStamina(),
                    Eitr = me.GetEitr(), MaxEitr = me.GetMaxEitr(),
                });
            }

            List<Player> loaded = Player.GetAllPlayers();
            var others = new List<Member>();
            foreach (ZNet.PlayerInfo info in ZNet.instance.GetPlayerList())
            {
                long id = info.m_characterID.UserID;
                if (id == 0 || id == MyId) continue;

                var m = new Member { Id = id, Name = info.m_name, SteamId = SteamIdOf(info) };
                if (info.m_publicPosition) m.Distance = Vector3.Distance(me.transform.position, info.m_position);

                if (_remote.TryGetValue(id, out Remote r) && Time.time - r.Seen < 5f)
                {
                    // Best case: that player's own game told us, so the numbers are exact and work at any distance.
                    m.HasData = true;
                    m.Hp = r.Hp; m.MaxHp = r.MaxHp; m.St = r.St; m.MaxSt = r.MaxSt; m.Eitr = r.Eitr; m.MaxEitr = r.MaxEitr;
                }
                else
                {
                    // They don't have the mod: use what the game shares for players near us (health yes, max stamina no).
                    Player near = loaded.FirstOrDefault(p => p != null && p.GetZDOID().UserID == id);
                    ZDO zdo = near != null && ZDOMan.instance != null ? ZDOMan.instance.GetZDO(near.GetZDOID()) : null;
                    if (near != null && zdo != null)
                    {
                        m.HasData = true;
                        m.Hp = near.GetHealth(); m.MaxHp = near.GetMaxHealth();
                        m.St = zdo.GetFloat(ZDOVars.s_stamina, 0f);
                        m.MaxSt = Mathf.Max(m.St, 100f);
                        m.Distance = Vector3.Distance(me.transform.position, near.transform.position);
                    }
                }
                others.Add(m);
            }

            list.AddRange(others.OrderBy(o => o.Name, StringComparer.OrdinalIgnoreCase));
            _members = list.Take(Mathf.Max(1, _maxRows.Value)).ToList();

            // Forget people who left a while ago.
            foreach (long gone in _remote.Where(kv => Time.time - kv.Value.Seen > 60f && others.All(o => o.Id != kv.Key)).Select(kv => kv.Key).ToList())
                _remote.Remove(gone);
        }

        private static float ParseFloat(string s) =>
            float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out float f) ? f : 0f;
    }
}
