using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;
using static DHack.Shared.Translations;

namespace Dungeons
{
    // ---- breakable walls

    /// <summary>
    /// A wall section that breaks (a mechanism of kind "breakable", MechDef.Health). The game's own hit flow reaches it: a weapon's hit
    /// looks for an IDestructible above the collider it struck (Projectile.FindHitObject), which this is, on the mechanism's pivot. Only
    /// pickaxe and blunt damage count; the attacker's game sends the damage to the entrance's owner ("hit:key:amount"), who keeps the
    /// count and breaks it at its health (saved: its record, as a one-shot lever's). Every game then hides its parts with the Ashlands
    /// wall's crumble and dust.
    /// </summary>
    internal class DungeonBreakable : MonoBehaviour, IDestructible, Hoverable
    {
        public const string HitSound = "sfx_rock_hit", BreakSound = "sfx_rock_wall_crumble", BreakDust = "vfx_ashland_wall_2x2_destruction";
        private MechSet _set;
        private string _key;
        private float _hinted;

        public void Init(MechSet set, string key) { _set = set; _key = key; }

        public DestructibleType GetDestructibleType() => DestructibleType.Default;

        public void Damage(HitData hit)
        {
            if (_set == null || hit == null || !_set.ByKey.TryGetValue(_key, out Mech m) || _set.IsBroken(m)) return;
            float damage = hit.m_damage.m_pickaxe + hit.m_damage.m_blunt;
            Character attacker = hit.GetAttacker();
            if (damage <= 0f)
            {
                if (attacker == Player.m_localPlayer && Time.time - _hinted > 4f)
                {
                    _hinted = Time.time;
                    Player.m_localPlayer.Message(MessageHud.MessageType.Center, T("dungeons_cracked_hint"));
                }
                return;
            }
            MechSet.PlayAt(HitSound, hit.m_point);
            if (DamageText.instance != null) DamageText.instance.ShowText(DamageText.TextType.Normal, hit.m_point, damage);
            _set.Site.Act("hit:" + _key + ":" + damage.ToString("0.#", CultureInfo.InvariantCulture));
        }

        public string GetHoverText() => Localization.instance.Localize($"{T("dungeons_cracked")}\n<color=#a0a0a0>{T("dungeons_cracked_hint")}</color>");

        public string GetHoverName() => T("dungeons_cracked");

        public float GetHoverOffset() => 0f;
    }

    // ---- shortcuts

    /// <summary>
    /// One door of a shortcut pair (kind "shortcut": MechDef.Pair names the pair, Deep the far door). Both are sealed until a player uses
    /// the deep one from its front (+z); after that, E on either takes them to 1.5 m in front of the other. Open: either door's record (a
    /// reset clears it).
    /// </summary>
    internal class DungeonShortcut : MonoBehaviour, Hoverable, Interactable
    {
        private MechSet _set;
        private string _key;
        private float _next;

        public void Init(MechSet set, string key) { _set = set; _key = key; }

        private Mech Me => _set != null && _set.ByKey.TryGetValue(_key, out Mech m) ? m : null;

        private static bool InFront(Mech m, Vector3 p) => m.Base != null && m.Base.InverseTransformPoint(p).z > 0f;

        public string GetHoverText()
        {
            Mech m = Me;
            if (m == null) return "";
            string text = T("dungeons_shortcut");
            if (_set.ShortcutOpen(m)) text += $"\n[<color=yellow><b>$KEY_Use</b></color>] {T("dungeons_shortcut_go")}";
            else if (m.Def.Deep && Player.m_localPlayer != null && InFront(m, Player.m_localPlayer.transform.position))
                text += $"\n[<color=yellow><b>$KEY_Use</b></color>] {T("dungeons_shortcut_unseal")}";
            else text += $"\n<color=#a0a0a0>{T("dungeons_shortcut_sealed")}</color>";
            return Localization.instance.Localize(text);
        }

        public string GetHoverName() => T("dungeons_shortcut");

        public float GetHoverOffset() => 0f;

        public bool Interact(Humanoid user, bool hold, bool alt)
        {
            Mech m = Me;
            Player p = user as Player;
            if (hold || m == null || p == null || p != Player.m_localPlayer || Time.time < _next) return false;
            _next = Time.time + 1f;
            if (_set.ShortcutOpen(m))
            {
                Mech other = _set.OtherEnd(m);
                if (other?.Base == null) return false;
                Vector3 to = other.Base.position + other.Base.forward * 1.5f + Vector3.up * 0.2f;
                Safety.OurTeleport();
                Builder.Reveal(_set.Site.Built, to);   // (culling: the other end's rooms drawn before the player lands)
                p.TeleportTo(to, Quaternion.LookRotation(Vector3.ProjectOnPlane(other.Base.forward, Vector3.up).normalized), false);
                return true;
            }
            if (!m.Def.Deep || !InFront(m, p.transform.position))
            {
                p.Message(MessageHud.MessageType.Center, T("dungeons_shortcut_sealed"));
                return false;
            }
            MechSet.PlayAt(MechSet.UnlockClunk, m.WorldPos);
            MechSet.PlayAt(MechSet.UnlockChime, m.WorldPos);
            _set.Site.Act("use:" + _key);
            return true;
        }

        public bool UseItem(Humanoid user, ItemDrop.ItemData item) => false;
    }

    // ---- the vault's reward

    /// <summary>
    /// When the vault opens (its gate, or the boss falls), each player inside the dungeon gets their own roll from the pack's "vault" loot
    /// for the land (the chests' lists: "Name", "Name:n", "Name:min-max"), straight into their inventory with a message, what doesn't fit
    /// at their feet. Each player's own game gives it (every game sees the vault open), once per character and per time the dungeon is
    /// new (its parts' generation), noted in the character's custom data.
    /// </summary>
    internal static class VaultReward
    {
        private const int Picks = 3;

        public static void Give(Site site)
        {
            Player p = Player.m_localPlayer;
            if (p == null || p.IsDead() || site?.Layout?.Pack == null || site.Zdo == null || !site.Contains(p.transform.position)) return;
            string key = $"quaddungeon_vault_{site.Id}_{site.Zdo.GetInt(Keys.Gen)}";
            if (p.m_customData.ContainsKey(key)) return;
            p.m_customData[key] = "1";
            string biome = WorldGenerator.instance != null ? WorldGenerator.instance.GetBiome(site.Origin.x, site.Origin.z).ToString() : "Meadows";
            List<string> entries = PackReader.Loot(site.Layout.Pack, biome, "vault");
            if (entries == null || entries.Count == 0) entries = PackReader.Loot(site.Layout.Pack, biome, "rare");
            if (entries == null || entries.Count == 0 || ObjectDB.instance == null) return;
            var rng = new System.Random(unchecked((int)p.GetPlayerID() ^ key.GetStableHashCode()));
            var given = new List<string>();
            foreach (string entry in entries.OrderBy(_ => rng.Next()).Take(Picks))
            {
                string[] f = entry.Split(':');
                GameObject prefab = ObjectDB.instance.GetItemPrefab(f[0].Trim());
                ItemDrop drop = prefab != null ? prefab.GetComponent<ItemDrop>() : null;
                if (drop == null) continue;
                int min = 1, max = 1;
                if (f.Length > 1)
                {
                    string[] range = f[1].Split('-');
                    int.TryParse(range[0], out min);
                    max = range.Length > 1 && int.TryParse(range[1], out int hi) ? hi : min;
                }
                int amount = Mathf.Max(1, rng.Next(Mathf.Max(1, min), Mathf.Max(min, max) + 1));
                Give(p, prefab, drop, amount);
                given.Add($"{amount} {drop.m_itemData.m_shared.m_name}");
            }
            if (given.Count == 0) return;
            p.Message(MessageHud.MessageType.Center, Localization.instance.Localize(T("dungeons_vault_reward", string.Join(", ", given))));
            Plugin.Log.LogInfo($"Dungeon {site.Id}: the vault's reward for {p.GetPlayerName()}: {string.Join(", ", given)}");
        }

        private static void Give(Player p, GameObject prefab, ItemDrop drop, int amount)
        {
            Inventory inv = p.GetInventory();
            int left = amount, stack = Mathf.Max(1, drop.m_itemData.m_shared.m_maxStackSize);
            while (left > 0)
            {
                int n = Mathf.Min(left, stack);
                if (inv.CanAddItem(prefab, n)) inv.AddItem(prefab, n);
                else
                {
                    GameObject go = Object.Instantiate(prefab, p.transform.position + p.transform.forward * 0.6f + Vector3.up, Quaternion.identity);
                    ItemDrop d = go.GetComponent<ItemDrop>();
                    if (d != null) d.SetStack(n);
                }
                left -= n;
            }
        }
    }

    // ---- music

    /// <summary>
    /// The music inside: a fight's track while this game's player is in a fighting encounter's area (its "music", else a combat track, or a
    /// boss track for a boss), else the room's ("music" on the room), else the pack's, else the game's own. Through the game's location
    /// music (MusicMan.LocationMusic), which comes before its environment music; given back (null) as soon as the player is out of the
    /// dungeon, so it's never left stuck. A name the game doesn't have is logged once and skipped.
    /// </summary>
    internal static class DungeonMusic
    {
        public const string FightDefault = "CombatEventL3", BossDefault = "boss_frozenking";
        private static readonly AccessTools.FieldRef<MusicMan, string> Location = AccessTools.FieldRefAccess<MusicMan, string>("m_locationMusic");
        private static string _ours;
        private static float _next;
        private static readonly HashSet<string> Missing = new HashSet<string>();

        public static void Update()
        {
            MusicMan mm = MusicMan.instance;
            if (mm == null) return;
            string want = Wanted(out bool keep);
            if (want != null && !Has(mm, want)) want = null;
            if (want != _ours)
            {
                if (want != null || _ours != null) mm.LocationMusic(want);
                _ours = want;
                _next = Time.time + 2f;
                return;
            }
            // a fight's or boss's track that ran to its end: again (the game's location music plays a track once)
            if (keep && _ours != null && Time.time >= _next)
            {
                _next = Time.time + 2f;
                if (Location(mm) == null) mm.LocationMusic(_ours);
            }
        }

        public static void Clear()
        {
            if (_ours != null && MusicMan.instance != null) MusicMan.instance.LocationMusic(null);
            _ours = null;
        }

        private static string Wanted(out bool keep)
        {
            keep = false;
            Player p = Player.m_localPlayer;
            if (p == null || p.IsDead() || p.transform.position.y < 3000f) return null;
            Site site = Dungeons.Containing(p.transform.position);
            if (site == null || !site.Ready || site.Layout == null) return null;
            Vector3 pos = p.transform.position;
            string fight = site.Mechs?.FightMusicAt(pos);
            if (fight != null) { keep = true; return fight; }
            string asked = site.Mechs?.TriggerMusic(pos);   // (triggers: the music a trigger asked for, TriggerSet.cs)
            if (asked != null) { keep = true; return asked; }
            foreach (PlacedRoom room in site.Layout.Rooms)
            {
                if (string.IsNullOrEmpty(room.Def?.Music)) continue;
                Vector3 c = site.Origin + room.Pos, s = room.Def.Size;
                bool sideways = Mathf.Abs(Mathf.DeltaAngle(room.Yaw, 90f)) < 1f || Mathf.Abs(Mathf.DeltaAngle(room.Yaw, -90f)) < 1f;
                float hx = (sideways ? s.z : s.x) / 2f, hz = (sideways ? s.x : s.z) / 2f;
                if (Mathf.Abs(pos.x - c.x) <= hx && Mathf.Abs(pos.z - c.z) <= hz && Mathf.Abs(pos.y - c.y) <= s.y / 2f) return room.Def.Music;
            }
            return string.IsNullOrEmpty(site.Layout.Pack?.Music) ? null : site.Layout.Pack.Music;
        }

        private static bool Has(MusicMan mm, string name)
        {
            if (mm.m_music.Any(n => n != null && n.m_name == name)) return true;
            if (Missing.Add(name)) Plugin.Log.LogWarning($"Dungeon music: the game has no track called \"{name}\" (dungeon music lists them): not played");
            return false;
        }

        /// <summary>For `dungeon music`: the game's tracks, and what plays.</summary>
        public static Newtonsoft.Json.Linq.JObject Report()
        {
            MusicMan mm = MusicMan.instance;
            if (mm == null) return new Newtonsoft.Json.Linq.JObject { ["error"] = "no music here" };
            return new Newtonsoft.Json.Linq.JObject
            {
                ["tracks"] = new Newtonsoft.Json.Linq.JArray(mm.m_music.Where(n => n != null).Select(n => n.m_name + (n.m_loop ? " (loops)" : ""))),
                ["ours"] = _ours,
                ["locationMusic"] = Location(mm),
                ["defaults"] = $"fights {FightDefault}, bosses {BossDefault}",
            };
        }
    }

    // (the boss's patches: BossBody.cs)
}
