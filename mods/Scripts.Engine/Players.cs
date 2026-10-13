using System;
using System.Collections.Generic;
using System.Linq;
using Jint;
using Jint.Native;
using UnityEngine;
using static Scripts.Api;

namespace Scripts
{
    /// <summary>
    /// What scripts do to the world, with the caps of docs/scripting-api.md section 5 on every action. A script can only act on the
    /// player whose game this is for now (items, teleports and healing of other players need their own game's say-so: a later step), and
    /// a refusal says why once. Everything an action did is also written to the audit log (Audit.cs).
    /// </summary>
    internal static class Players
    {
        // ---- rates ----

        private sealed class Bucket { public double Tokens, Last; }
        private static readonly Dictionary<string, Bucket> Buckets = new Dictionary<string, Bucket>();

        /// <summary>A token bucket per script and action: perSecond refills it, burst is how many may go at once.</summary>
        internal static bool Rate(Script s, string key, double perSecond, double burst)
        {
            string k = s.File + "|" + key;
            double now = Time.timeAsDouble;
            if (!Buckets.TryGetValue(k, out Bucket b)) Buckets[k] = b = new Bucket { Tokens = burst, Last = now };
            b.Tokens = Math.Min(burst, b.Tokens + (now - b.Last) * perSecond);
            b.Last = now;
            if (b.Tokens < 1.0) return false;
            b.Tokens -= 1.0;
            return true;
        }

        // hourly counts per script and player: [window start, count]
        private static readonly Dictionary<string, double[]> Hourly = new Dictionary<string, double[]>();

        internal static int HourlyLeft(Script s, string player, string what, int cap)
        {
            string k = s.File + "|" + player + "|" + what;
            double now = Time.timeAsDouble;
            if (!Hourly.TryGetValue(k, out double[] w) || now - w[0] > 3600.0) Hourly[k] = w = new[] { now, 0.0 };
            return cap - (int)w[1];
        }

        internal static void HourlyUse(Script s, string player, string what, int n) { HourlyLeft(s, player, what, int.MaxValue); Hourly[s.File + "|" + player + "|" + what][1] += n; }

        internal static bool IsAdmin(Who w) => w != null && (w.Peer == 0L ? (ZNet.instance == null || ZNet.instance.IsServer() || Mp.Wire.LocalIsAdmin) : w.Remote && Mp.Wire.IsAdmin(w.Peer));

        // ---- a player wherever they are (Roster.cs): another game's player is asked through their game, this game's own directly ----

        internal static int Give(Script s, Who w, string prefab, int n) => w == null ? 0 : w.Remote ? Mp.Remote.Give(s, w, prefab, n) : w.Loaded is Player p ? Give(s, p, prefab, n) : 0;
        internal static int Take(Script s, Who w, string prefab, int n) => w == null ? 0 : w.Remote ? Mp.Remote.Take(s, w, prefab, n) : w.Loaded is Player p ? Take(s, p, prefab, n) : 0;
        internal static int Count(Who w, string prefab) => w == null ? 0 : w.Remote ? Mp.Remote.Count(w, prefab) : w.Loaded is Player p ? Count(p, prefab) : 0;
        internal static bool Teleport(Script s, Who w, Vector3 pos) => w != null && (w.Remote ? Mp.Remote.Teleport(s, w, pos) : w.Loaded is Player p && Teleport(s, p, pos));
        internal static double Heal(Script s, Who w, double n) => w == null ? 0.0 : w.Remote ? Mp.Remote.Heal(s, w, n) : w.Loaded is Player p ? Heal(s, p, n) : 0.0;
        internal static double Damage(Script s, Who w, double n)
        {
            if (w == null) return 0.0;
            if (!w.Remote) return w.Loaded is Player p ? Damage(s, p, n) : 0.0;
            return Mp.Remote.Hurt(s, w, n);   // (it keeps the once-a-second rate itself)
        }

        /// <summary>The game's own centre or corner message on one player's screen, wherever they are (the message goes to their character's own game).</summary>
        internal static void MessageTo(Who w, MessageHud.MessageType type, string text)
        {
            if (w == null) return;
            Player p = w.Loaded;
            if (p != null) { p.Message(type, text); return; }
            if (w.Remote && ZRoutedRpc.instance != null) ZRoutedRpc.instance.InvokeRoutedRPC(w.Peer, w.Character, "Message", (int)type, text, 0);
        }

        internal static void Say(Script s, Who w, string text, JsValue opts)
        {
            if (!Rate(s, "say", 5.0, 5.0)) { Host.Refuse(s, "too many messages a second"); return; }
            bool corner = opts.IsObject() && opts.AsObject().Get("style") is JsValue st && st.IsString() && st.AsString() == "corner";
            MessageTo(w, corner ? MessageHud.MessageType.TopLeft : MessageHud.MessageType.Center, text);
        }

        internal static bool IsAdmin(Player p) => p == Player.m_localPlayer ? (ZNet.instance == null || ZNet.instance.IsServer() || Mp.Wire.LocalIsAdmin) : Mp.Remote.IsRemote(p) && Mp.Wire.IsAdmin(p.GetOwner());

        private static bool Mine(Script s, Player p, string what)
        {
            if (p == Player.m_localPlayer) return true;
            Host.Refuse(s, what + " only works for the player whose game this is, for now");
            return false;
        }

        // ---- messages ----

        internal static void Say(Script s, Player p, string text, JsValue opts)
        {
            if (!Rate(s, "say", 5.0, 5.0)) { Host.Refuse(s, "too many messages a second"); return; }
            bool corner = opts.IsObject() && opts.AsObject().Get("style") is JsValue st && st.IsString() && st.AsString() == "corner";
            p.Message(corner ? MessageHud.MessageType.TopLeft : MessageHud.MessageType.Center, text);
        }

        internal static void Broadcast(Script s, string text)
        {
            if (!Rate(s, "broadcast", 1.0, 1.0)) { Host.Refuse(s, "too many announcements a second"); return; }
            foreach (Who w in Roster.All()) MessageTo(w, MessageHud.MessageType.Center, text);
        }

        // ---- items ----

        private static GameObject ItemPrefab(string prefab) => ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(prefab) : null;

        /// <summary>Puts items in a player's bag: only what fits (never drops or loses the rest). How many went in. Used for this game's own player and by the Agent.</summary>
        internal static int AddToBag(Player p, string prefab, int n)
        {
            GameObject item = ItemPrefab(prefab);
            if (item == null || p == null) return 0;
            Inventory inv = p.GetInventory();
            int give = 0;
            for (int k = n; k >= 1; k--) if (inv.CanAddItem(item, k)) { give = k; break; }
            if (give == 0) return 0;
            if (inv.AddItem(prefab, give, 1, 0, 0L, "", false) == null) return 0;
            return give;
        }

        /// <summary>Takes items out of a player's bag (never a worn or held one). How many came out.</summary>
        internal static int RemoveFromBag(Player p, string prefab, int n)
        {
            Inventory inv = p.GetInventory();
            int taken = 0;
            foreach (ItemDrop.ItemData item in inv.GetAllItems().ToList())
            {
                if (taken >= n) break;
                if (item.m_equipped || item.m_dropPrefab == null || Utils.GetPrefabName(item.m_dropPrefab) != prefab) continue;
                int part = Math.Min(item.m_stack, n - taken);
                if (inv.RemoveItem(item, part)) taken += part;
            }
            return taken;
        }

        internal static int Give(Script s, Player p, string prefab, int n)
        {
            if (Mp.Remote.IsRemote(p)) return Mp.Remote.Give(s, Roster.Of(p), prefab, n);
            if (!Mine(s, p, "items")) return 0;
            int most = Lists.Most(prefab);
            if (most <= 0) { Host.Refuse(s, "'" + prefab + "' can't be given"); return 0; }
            GameObject item = ItemPrefab(prefab);
            if (item == null) { Host.Refuse(s, "no item called '" + prefab + "'"); return 0; }
            n = Cut(s, prefab, n, most, "gift");
            string who = Handles.Id(p);
            int left = HourlyLeft(s, who, "give", Limits.ItemsPerHour);
            if (left <= 0) { Host.Refuse(s, "the hourly limit for items (" + Limits.ItemsPerHour + "; the owner can change it in the Blockscript settings)"); return 0; }
            n = Mathf.Min(n, left);
            int give = AddToBag(p, prefab, n);
            if (give == 0) { Host.Refuse(s, "the bag is full"); return 0; }
            HourlyUse(s, who, "give", give);
            Audit.Add(s, $"gave {give} {prefab} to {p.GetPlayerName()}");
            return give;
        }

        internal static int Take(Script s, Player p, string prefab, int n)
        {
            if (Mp.Remote.IsRemote(p)) return Mp.Remote.Take(s, Roster.Of(p), prefab, n);
            if (!Mine(s, p, "items")) return 0;
            if (Lists.NeverTake.Contains(prefab)) { Host.Refuse(s, "'" + prefab + "' can't be taken"); return 0; }
            string who = Handles.Id(p);
            int left = HourlyLeft(s, who, "take", 20);
            if (left <= 0) { Host.Refuse(s, "the hourly limit for taking items"); return 0; }
            n = Mathf.Min(Mathf.Clamp(n, 1, 20), left);
            int taken = RemoveFromBag(p, prefab, n);
            if (taken > 0) { HourlyUse(s, who, "take", taken); Audit.Add(s, $"took {taken} {prefab} from {p.GetPlayerName()}"); }
            return taken;
        }

        internal static int Count(Player p, string prefab)
        {
            if (Mp.Remote.IsRemote(p)) return Mp.Remote.Count(Roster.Of(p), prefab);
            if (p != Player.m_localPlayer) return 0;
            int n = 0;
            foreach (ItemDrop.ItemData item in p.GetInventory().GetAllItems())
                if (item.m_dropPrefab != null && Utils.GetPrefabName(item.m_dropPrefab) == prefab) n += item.m_stack;
            return n;
        }

        internal static int DropItem(Script s, string prefab, int n, Vector3 pos)
        {
            int most = Lists.Most(prefab);
            if (most <= 0) { Host.Refuse(s, "'" + prefab + "' can't be dropped"); return 0; }
            GameObject item = ItemPrefab(prefab);
            if (item == null) { Host.Refuse(s, "no item called '" + prefab + "'"); return 0; }
            if (!NearAPlayer(pos, 60f)) { Host.Refuse(s, "that place is too far from every player"); return 0; }
            int left = HourlyLeft(s, "world", "give", Limits.ItemsPerHour);
            if (left <= 0) { Host.Refuse(s, "the hourly limit for items (" + Limits.ItemsPerHour + "; the owner can change it in the Blockscript settings)"); return 0; }
            n = Mathf.Min(Cut(s, prefab, n, most, "drop"), left);
            GameObject drop = UnityEngine.Object.Instantiate(item, pos + Vector3.up * 0.5f, Quaternion.identity);
            var itemDrop = drop.GetComponent<ItemDrop>();
            if (itemDrop == null) { UnityEngine.Object.Destroy(drop); return 0; }
            itemDrop.SetStack(n);
            HourlyUse(s, "world", "give", n);
            Audit.Add(s, $"dropped {n} {prefab} at {pos.x:0},{pos.z:0}");
            return n;
        }

        /// <summary>A count cut to the most one gift or drop may hold, saying so in the log when it was cut (a script asking for 100 gets 10, and its author learns why).</summary>
        internal static int Cut(Script s, string prefab, int n, int most, string what)
        {
            if (n > most) Host.Refuse(s, "only " + most + " " + prefab + " in one " + what + " (asked for " + n + "; the owner can raise 'Most items in one gift or drop' in the Blockscript settings)");
            return Mathf.Clamp(n, 1, most);
        }

        internal static bool NearAPlayer(Vector3 pos, float range) => Roster.Near(pos, range);

        // ---- creatures ----

        private static readonly Dictionary<string, List<ZDOID>> Alive = new Dictionary<string, List<ZDOID>>();

        internal static string SpawnedBy(Character c)
        {
            ZNetView view = c.GetComponent<ZNetView>();
            string by = view != null && view.GetZDO() != null ? view.GetZDO().GetString("dh_script", "") : "";
            return by.Length > 0 ? by : null;
        }

        internal static JsValue Spawn(Script s, string prefab, Vector3 pos, JsValue opts)
        {
            if (!Lists.Creatures.Contains(prefab)) return Refused(s, "'" + prefab + "' can't be spawned (not on the creature list for this tier)");
            GameObject creature = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(prefab) : null;
            if (creature == null || creature.GetComponent<Character>() == null) return Refused(s, "no creature called '" + prefab + "'");
            if (!NearAPlayer(pos, 60f)) return Refused(s, "that place is too far from every player");
            if (!ZoneSystem.instance.IsZoneLoaded(pos)) return Refused(s, "that place is not loaded on the game that runs the scripts (it is far from its own player: placing things near a player who is far away is not possible yet)");
            if (!Rate(s, "spawn", Limits.SpawnsPerMinute / 60.0, Limits.SpawnsPerMinute)) return Refused(s, "too many spawns a minute (" + Limits.SpawnsPerMinute + " is the limit; the owner can change it in the Blockscript settings)");
            if (!Alive.TryGetValue(s.File, out List<ZDOID> list)) Alive[s.File] = list = new List<ZDOID>();
            list.RemoveAll(id => ZDOMan.instance.GetZDO(id) == null);
            if (list.Count >= Limits.Alive) return Refused(s, Limits.Alive + " creatures from this script are alive already (the owner can change this in the Blockscript settings)");
            if (ZoneSystem.instance.GetSolidHeight(pos, out float h)) pos.y = h + 0.2f;
            GameObject go = UnityEngine.Object.Instantiate(creature, pos, Quaternion.Euler(0f, UnityEngine.Random.value * 360f, 0f));
            var character = go.GetComponent<Character>();
            int level = 1;
            if (opts.IsObject() && opts.AsObject().Get("level") is JsValue lv && lv.IsNumber()) level = Mathf.Clamp((int)lv.AsNumber(), 1, 3);
            character.SetLevel(level);
            ZNetView view = go.GetComponent<ZNetView>();
            if (view != null && view.GetZDO() != null) { view.GetZDO().Set("dh_script", s.Name); list.Add(view.GetZDO().m_uid); }
            Audit.Add(s, $"spawned {prefab} at {pos.x:0},{pos.z:0}");
            return Handles.Actor(s, character);
        }

        /// <summary>Creatures a script spawned go when the script does.</summary>
        internal static void ForgetScript(Script s)
        {
            if (Alive.TryGetValue(s.File, out List<ZDOID> list))
            {
                foreach (ZDOID id in list)
                {
                    GameObject go = ZNetScene.instance != null ? ZNetScene.instance.FindInstance(id) : null;
                    if (go != null) { var view = go.GetComponent<ZNetView>(); if (view != null) { view.ClaimOwnership(); ZNetScene.instance.Destroy(go); } }
                }
                list.Clear();
            }
        }

        // ---- fights ----

        internal static double Heal(Script s, Character c, double n)
        {
            if (c is Player rp && Mp.Remote.IsRemote(rp)) return Mp.Remote.Heal(s, Roster.Of(rp), n);
            if (!c.GetComponent<ZNetView>().IsOwner()) return Mp.Remote.OnceOwned(s, c, "healing", x => Heal(s, x, n)) ? Math.Min(500.0, Math.Max(0.0, n)) : 0.0;
            float before = c.GetHealth();
            c.Heal((float)Math.Min(500.0, Math.Max(0.0, n)), true);
            return Math.Max(0f, c.GetHealth() - before);
        }

        internal static double Damage(Script s, Character c, double n)
        {
            if (c.IsBoss()) { Host.Refuse(s, "damage to a boss"); return 0; }
            if (c is Player rp && Mp.Remote.IsRemote(rp)) return Mp.Remote.Hurt(s, Roster.Of(rp), n);
            if (!c.GetComponent<ZNetView>().IsOwner()) return Mp.Remote.OnceOwned(s, c, "damage", x => Damage(s, x, n)) ? Math.Min(500.0, Math.Max(0.0, n)) : 0.0;
            double amount = Math.Min(500.0, Math.Max(0.0, n));
            if (c is Player p)
            {
                if (!Rate(s, "hurt-" + Handles.Id(p), 1.0, 1.0)) { Host.Refuse(s, "damage to a player more than once a second"); return 0; }
                amount = Math.Min(amount, Math.Min(50.0, p.GetHealth() - 1f));   // (a script can't kill a player)
                if (amount <= 0.0) return 0;
            }
            var hit = new HitData { m_damage = { m_damage = (float)amount } };
            float before = c.GetHealth();
            c.Damage(hit);
            Audit.Add(s, $"hurt {Handles.PrefabName(c)} by {amount:0}");
            return Math.Max(0f, before - c.GetHealth());
        }

        // ---- the world ----

        internal static bool Teleport(Script s, Player p, Vector3 pos)
        {
            if (Mp.Remote.IsRemote(p)) return Mp.Remote.Teleport(s, Roster.Of(p), pos);
            if (!Mine(s, p, "teleport")) return false;
            if (!Rate(s, "tp-" + Handles.Id(p), 0.1, 1.0)) { Host.Refuse(s, "teleports more than once in ten seconds"); return false; }
            // the height the script gave is ignored: a landing a few metres off the ground kills
            if (!ZoneSystem.instance.IsZoneLoaded(pos) || !ZoneSystem.instance.GetSolidHeight(pos, out float h)) { Host.Refuse(s, "no ground to land on there"); return false; }
            pos.y = h + 0.2f;
            bool ok = SafeTeleport.Go(p, pos);
            if (ok) Audit.Add(s, $"teleported {p.GetPlayerName()} to {pos.x:0},{pos.z:0}");
            return ok;
        }

        private static double _weatherUntil;

        internal static bool SetWeather(Script s, string name, double seconds)
        {
            string env;
            switch (name)
            {
                case "clear": env = "Clear"; break;
                case "rain": env = "Rain"; break;
                case "storm": env = "ThunderStorm"; break;
                case "mist": env = "Misty"; break;
                case "snow": env = "Snow"; break;
                default: Host.Refuse(s, "no weather called '" + name + "'"); return false;
            }
            if (EnvMan.instance == null && !Mp.Remote.AnyAgent) return false;
            if (!Rate(s, "weather", 1.0 / 60.0, 1.0)) { Host.Refuse(s, "weather changes more than once a minute"); return false; }
            seconds = Math.Min(1800.0, Math.Max(30.0, seconds));
            if (EnvMan.instance != null) EnvMan.instance.SetForceEnvironment(env);
            Mp.Remote.FxEnv(name, seconds);
            _weatherUntil = Time.timeAsDouble + seconds;
            Audit.Add(s, $"set the weather to {name} for {seconds:0} s");
            return true;
        }

        internal static bool SetTime(Script s, double t)
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer()) { Host.Refuse(s, "only the game that runs the world can set the time"); return false; }
            if (!Rate(s, "time", 1.0 / 20.0, 3.0)) { Host.Refuse(s, "the time of day can be set three times at once, then once every 20 seconds"); return false; }
            t = Math.Max(0.0, Math.Min(1.0, t));
            double day = EnvMan.instance != null ? EnvMan.instance.m_dayLengthSec : 1800.0, now = ZNet.instance.GetTimeSeconds();
            double next = now - (now % day) + t * day;
            if (next <= now) next += day;   // (always forward: a day that has passed does not come back)
            ZNet.instance.SetNetTime(next);
            Audit.Add(s, $"set the time of day to {t:0.00}");
            return true;
        }

        /// <summary>Called every frame: puts the weather back when a script's time for it is up.</summary>
        internal static void Tick()
        {
            WorldExtras.Tick();
            if (_weatherUntil > 0.0 && Time.timeAsDouble >= _weatherUntil)
            {
                _weatherUntil = 0.0;
                if (EnvMan.instance != null) EnvMan.instance.SetForceEnvironment("");
            }
        }
    }

    /// <summary>A short list of what scripts did to the world, with the script's name and the time, so a player who lost something can see who did it.</summary>
    internal static class Audit
    {
        internal static readonly List<string> Lines = new List<string>();

        internal static void Add(Script s, string what)
        {
            string line = $"{DateTime.Now:HH:mm:ss} [{s.Name}] {what}";
            if (Lines.Count >= 300) Lines.RemoveAt(0);
            Lines.Add(line);
            Core.Log.LogInfo("Audit: " + line);
            s.Say("did: " + what);
        }
    }
}
