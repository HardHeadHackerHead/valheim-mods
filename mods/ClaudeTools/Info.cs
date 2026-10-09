using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace ClaudeTools
{
    /// <summary>
    /// What the assistant can find out (status, inventory, what is nearby, what the player looks at, the mods and their settings, the log) and
    /// the few things it can do for the player that change nothing in the world (a message on screen, a map pin, waiting).
    /// </summary>
    public partial class Plugin
    {
        private void RegisterBuiltIns()
        {
            RegisterPictureCommands();
            RegisterRenderCommands();

            Builtin("help", "help: every command, with the mod that adds it", (a, output, error) =>
            {
                output(new JObject
                {
                    ["commands"] = new JArray(Commands.Values.OrderBy(c => c.Owner == Name ? 0 : 1).ThenBy(c => c.Owner).ThenBy(c => c.Name)
                        .Select(c => new JObject { ["command"] = c.Name, ["usage"] = c.Usage, ["from"] = c.Owner })),
                    ["places"] = new JArray(new[] { "world", "here", "look" }.Concat(Frames.Keys.Select(k => "(names " + k + " knows)"))),
                });
                return null;
            });

            Builtin("wait", "wait <seconds>: pause (let things happen before the next picture)", (a, output, error) => Wait(Mathf.Clamp(F(a, 1, 2f), 0f, 60f)));

            Builtin("status", "status: world, where the player is, health, stamina, biome, day, time, weather, who is online", (a, output, error) =>
            {
                Player p = Player.m_localPlayer;
                Transform cam = GameCamera.instance != null ? GameCamera.instance.transform : null;
                EnvMan env = EnvMan.instance;
                float frac = env != null ? env.GetDayFraction() : 0f;
                int minutes = Mathf.RoundToInt(frac * 24f * 60f) % (24 * 60);
                output(new JObject
                {
                    ["world"] = ZNet.instance != null ? ZNet.instance.GetWorldName() : "",
                    ["player"] = p.GetPlayerName(),
                    ["position"] = Vec(p.transform.position), ["yaw"] = Math.Round(p.transform.eulerAngles.y, 1),
                    ["camera"] = cam != null ? Vec(cam.position) : null,
                    ["health"] = Math.Round(p.GetHealth(), 1), ["maxHealth"] = Math.Round(p.GetMaxHealth(), 1),
                    ["stamina"] = Math.Round(p.GetStamina(), 1), ["maxStamina"] = Math.Round(p.GetMaxStamina(), 1),
                    ["biome"] = WorldGenerator.instance != null ? WorldGenerator.instance.GetBiome(p.transform.position).ToString() : "",
                    ["day"] = env != null ? env.GetDay() : 0,
                    ["time"] = $"{minutes / 60:00}:{minutes % 60:00}",
                    ["weather"] = env != null && env.GetCurrentEnvironment() != null ? env.GetCurrentEnvironment().m_name : "",
                    ["online"] = new JArray(Player.GetAllPlayers().Where(x => x != null).Select(x => new JObject { ["name"] = x.GetPlayerName(), ["distance"] = Math.Round(Vector3.Distance(x.transform.position, p.transform.position), 1) })),
                });
                return null;
            });

            Builtin("inventory", "inventory: what the player carries (name, stack, quality, durability, equipped) and totals", (a, output, error) =>
            {
                Player p = Player.m_localPlayer;
                Inventory inv = p.GetInventory();
                var items = new JArray();
                var totals = new Dictionary<string, int>();
                foreach (ItemDrop.ItemData it in inv.GetAllItems())
                {
                    string name = Localization.instance.Localize(it.m_shared.m_name);
                    items.Add(new JObject
                    {
                        ["name"] = name, ["id"] = it.m_shared.m_name, ["stack"] = it.m_stack, ["quality"] = it.m_quality,
                        ["durability"] = it.m_shared.m_useDurability ? Math.Round(100f * it.m_durability / Mathf.Max(1f, it.GetMaxDurability())) + "%" : null,
                        ["equipped"] = it.m_equipped, ["slot"] = new JArray(it.m_gridPos.x, it.m_gridPos.y),
                    });
                    totals[name] = (totals.TryGetValue(name, out int n) ? n : 0) + it.m_stack;
                }
                output(new JObject
                {
                    ["items"] = items,
                    ["totals"] = new JObject(totals.OrderBy(kv => kv.Key).Select(kv => new JProperty(kv.Key, kv.Value))),
                    ["weight"] = Math.Round(inv.GetTotalWeight(), 1), ["maxWeight"] = Math.Round(p.GetMaxCarryWeight(), 1),
                    ["freeSlots"] = inv.GetEmptySlots(),
                });
                return null;
            });

            Builtin("nearby", "nearby [radius]: creatures and players around the player (health, level), buildings by kind, items on the ground", (a, output, error) =>
            {
                Player p = Player.m_localPlayer;
                Vector3 me = p.transform.position;
                float radius = Mathf.Clamp(F(a, 1, 30f), 2f, 100f);
                var creatures = new JArray();
                foreach (Character c in Character.GetAllCharacters().Where(c => c != null && !c.IsPlayer() && Vector3.Distance(c.transform.position, me) <= radius)
                                                                   .OrderBy(c => Vector3.Distance(c.transform.position, me)).Take(60))
                    creatures.Add(new JObject
                    {
                        ["name"] = Localization.instance.Localize(c.m_name), ["prefab"] = Utils.GetPrefabName(c.gameObject), ["level"] = c.GetLevel(),
                        ["health"] = Math.Round(c.GetHealth()), ["maxHealth"] = Math.Round(c.GetMaxHealth()),
                        ["distance"] = Math.Round(Vector3.Distance(c.transform.position, me), 1), ["tame"] = c.IsTamed(), ["boss"] = c.IsBoss(),
                        ["position"] = Vec(c.transform.position),
                    });
                var players = new JArray(Player.GetAllPlayers().Where(x => x != null && x != p && Vector3.Distance(x.transform.position, me) <= radius)
                    .Select(x => new JObject { ["name"] = x.GetPlayerName(), ["distance"] = Math.Round(Vector3.Distance(x.transform.position, me), 1), ["health"] = Math.Round(x.GetHealth()) }));
                var pieces = new List<Piece>();
                Piece.GetAllPiecesInRadius(me, radius, pieces);
                var byKind = pieces.Where(x => x != null).GroupBy(x => Utils.GetPrefabName(x.gameObject)).OrderByDescending(g => g.Count()).Take(30)
                                   .Select(g => new JProperty(g.Key, g.Count()));
                var items = new Dictionary<string, int>();
                foreach (Collider col in Physics.OverlapSphere(me, radius, LayerMask.GetMask("item"), QueryTriggerInteraction.Collide))
                {
                    ItemDrop drop = col.GetComponentInParent<ItemDrop>();
                    if (drop == null) continue;
                    string n = Localization.instance.Localize(drop.m_itemData.m_shared.m_name);
                    items[n] = (items.TryGetValue(n, out int k) ? k : 0) + drop.m_itemData.m_stack;
                }
                output(new JObject
                {
                    ["radius"] = radius, ["creatures"] = creatures, ["players"] = players,
                    ["buildings"] = pieces.Count, ["buildingsByKind"] = new JObject(byKind),
                    ["itemsOnGround"] = new JObject(items.OrderByDescending(kv => kv.Value).Select(kv => new JProperty(kv.Key, kv.Value))),
                });
                return null;
            });

            // Chests and their QualityOfLife assignments ("Stack to chests" rules, the K menu): looked at, and set, without touching what is in them.
            int rulesKey = "DHack_StackRules".GetStableHashCode();
            Builtin("chests", "chests [radius]: the chests around the player: where, what is in them (by name and by the game's item name), their assignment (QualityOfLife), whose", (a, output, error) =>
            {
                Player p = Player.m_localPlayer;
                Vector3 me = p.transform.position;
                float radius = Mathf.Clamp(F(a, 1, 15f), 2f, 60f);
                var list = new JArray();
                foreach (Container c in UnityEngine.Object.FindObjectsByType<Container>(FindObjectsSortMode.None)
                             .Where(c => c != null && c.GetInventory() != null && c.GetComponentInParent<Piece>() != null && c.GetComponent<TombStone>() == null && Vector3.Distance(c.transform.position, me) <= radius)   // (one not set up yet has no inventory)
                             .OrderBy(c => Vector3.Distance(c.transform.position, me)))
                {
                    ZNetView v = c.GetComponent<ZNetView>();
                    ZDO z = v != null && v.IsValid() ? v.GetZDO() : null;
                    var contents = c.GetInventory().GetAllItems().GroupBy(i => i.m_shared.m_name)
                        .Select(g => $"{Localization.instance.Localize(g.Key)} x{g.Sum(i => i.m_stack)} ({g.Key})").ToArray();
                    list.Add(new JObject
                    {
                        ["chest"] = Utils.GetPrefabName(c.gameObject), ["position"] = Vec(c.transform.position), ["distance"] = Math.Round(Vector3.Distance(c.transform.position, me), 1),
                        ["slots"] = $"{c.GetInventory().NrOfItems()}/{c.GetInventory().GetWidth() * c.GetInventory().GetHeight()}",
                        ["rule"] = z?.GetString(rulesKey, "") ?? "", ["companions"] = (z?.GetLong("dhc_home", 0L) ?? 0L) != 0L ? z.GetString("dhc_homename", "a companion's") : "",
                        ["contents"] = new JArray(contents),
                    });
                }
                output(new JObject { ["radius"] = radius, ["chests"] = list });
                return null;
            });

            Builtin("chestrule", "chestrule <x> <y> <z> <rule>: set the QualityOfLife assignment of the chest at x,y,z (\"C=Food,Tools|I=$item_wood\"; \"-\" clears it). Only the assignment: nothing in the chest is touched", (a, output, error) =>
            {
                if (a.Length < 5) { error("chestrule <x> <y> <z> <rule>   (rule: C=<categories>|I=<item names>, or - to clear)"); return null; }
                var at = new Vector3(F(a, 1, 0f), F(a, 2, 0f), F(a, 3, 0f));
                string rule = string.Join(" ", a.Skip(4)).Trim();
                if (rule == "-") rule = "";
                Container c = UnityEngine.Object.FindObjectsByType<Container>(FindObjectsSortMode.None)
                    .Where(k => k != null && k.GetComponentInParent<Piece>() != null && Vector3.Distance(k.transform.position, at) < 0.5f)
                    .OrderBy(k => Vector3.Distance(k.transform.position, at)).FirstOrDefault(); // (exactly that one: chests stacked on each other share x and z)
                if (c == null) { error($"no chest at {at}"); return null; }
                if (c.IsInUse()) { error("someone has that chest open"); return null; }
                ZNetView v = c.GetComponent<ZNetView>();
                if (v == null || !v.IsValid()) { error("that chest is not ready"); return null; }
                if (!v.IsOwner()) v.ClaimOwnership();
                v.GetZDO().Set(rulesKey, rule);
                output(new JObject { ["chest"] = Utils.GetPrefabName(c.gameObject), ["position"] = Vec(c.transform.position), ["rule"] = rule });
                return null;
            });

            Builtin("looking", "looking: what the player's crosshair is on (name, kind, distance, health)", (a, output, error) =>
            {
                if (GameCamera.instance == null) { error("no camera"); return null; }
                Transform cam = GameCamera.instance.transform;
                Player p = Player.m_localPlayer;
                var hits = Physics.RaycastAll(cam.position, cam.forward, 60f, ~0, QueryTriggerInteraction.Ignore).OrderBy(h => h.distance);
                foreach (RaycastHit hit in hits)
                {
                    if (hit.collider.GetComponentInParent<Player>() == p) continue;   // the player's own body
                    GameObject root = hit.collider.GetComponentInParent<ZNetView>()?.gameObject ?? hit.collider.transform.root.gameObject;
                    var info = new JObject
                    {
                        ["object"] = Utils.GetPrefabName(root), ["distance"] = Math.Round(Vector3.Distance(p.transform.position, hit.point), 1),
                        ["point"] = Vec(hit.point), ["layer"] = LayerMask.LayerToName(hit.collider.gameObject.layer),
                    };
                    Hoverable hover = hit.collider.GetComponentInParent<Hoverable>();
                    if (hover != null) info["name"] = Localization.instance.Localize(hover.GetHoverName());
                    Piece piece = root.GetComponent<Piece>();
                    WearNTear wear = root.GetComponent<WearNTear>();
                    if (piece != null) { info["kind"] = "building piece"; info["name"] = Localization.instance.Localize(piece.m_name); }
                    if (wear != null) info["condition"] = Math.Round(100f * wear.GetHealthPercentage()) + "%";
                    Character ch = root.GetComponent<Character>();
                    if (ch != null) { info["kind"] = "creature"; info["name"] = Localization.instance.Localize(ch.m_name); info["health"] = Math.Round(ch.GetHealth()); }
                    if (root.GetComponent<ItemDrop>() != null) info["kind"] = "item";
                    if (hit.collider.gameObject.layer == LayerMask.NameToLayer("terrain")) info["kind"] = "ground";
                    output(info);
                    return null;
                }
                output(new JObject { ["object"] = "nothing within 60 m (sky)" });
                return null;
            });

            Builtin("mods", "mods: the mods running, with their versions", (a, output, error) =>
            {
                output(new JObject
                {
                    ["mods"] = new JArray(LoadedMods().Select(m =>
                    {
                        BepInPlugin meta = MetadataHelper.GetMetadata(m);
                        return new JObject { ["guid"] = meta?.GUID, ["name"] = meta?.Name, ["version"] = meta?.Version?.ToString() };
                    }).OrderBy(o => (string)o["name"]))
                });
                return null;
            });

            Builtin("config", "config <mod> [section] [key]: a mod's settings; config set <mod> <section> <key> <value> changes one (needs AllowConfigChanges)", (a, output, error) =>
            {
                bool set = a.Length > 1 && a[1].Equals("set", StringComparison.OrdinalIgnoreCase);
                int at = set ? 2 : 1;
                if (a.Length <= at) { error("which mod? (\"mods\" lists them)"); return null; }
                BaseUnityPlugin mod = FindMod(a[at]);
                if (mod == null) { error($"no mod called '{a[at]}' (\"mods\" lists them)"); return null; }
                ConfigFile cfg = mod.Config;
                if (set)
                {
                    if (!_allowConfigChanges.Value) { error("changing settings is off (AllowConfigChanges in com.dhack.claudetools.cfg)"); return null; }
                    if (a.Length < at + 4) { error("config set <mod> <section> <key> <value>"); return null; }
                    var def = cfg.Keys.FirstOrDefault(k => k.Section.Equals(a[at + 1], StringComparison.OrdinalIgnoreCase) && k.Key.Equals(a[at + 2], StringComparison.OrdinalIgnoreCase));
                    if (def == null) { error($"{a[at]} has no setting {a[at + 1]}/{a[at + 2]}"); return null; }
                    ConfigEntryBase entry = cfg[def];
                    string before = entry.GetSerializedValue();
                    entry.SetSerializedValue(Rest(a, at + 3));
                    output(new JObject { ["mod"] = a[at], ["setting"] = def.Section + "/" + def.Key, ["was"] = before, ["now"] = entry.GetSerializedValue() });
                    return null;
                }
                string section = a.Length > at + 1 ? a[at + 1] : null, key = a.Length > at + 2 ? a[at + 2] : null;
                var list = new JArray();
                foreach (ConfigDefinition def in cfg.Keys.Where(k => (section == null || k.Section.Equals(section, StringComparison.OrdinalIgnoreCase)) && (key == null || k.Key.Equals(key, StringComparison.OrdinalIgnoreCase))))
                {
                    ConfigEntryBase entry = cfg[def];
                    list.Add(new JObject { ["section"] = def.Section, ["key"] = def.Key, ["value"] = entry.GetSerializedValue(), ["default"] = TomlDefault(entry), ["about"] = entry.Description?.Description });
                }
                output(new JObject { ["mod"] = MetadataHelper.GetMetadata(mod)?.Name, ["settings"] = list });
                return null;
            });

            Builtin("log", "log [lines] [text]: the last lines of the BepInEx log (only those containing the text, if given)", (a, output, error) =>
            {
                string path = Path.Combine(Paths.BepInExRootPath, "LogOutput.log");
                if (!File.Exists(path)) { error("no log file"); return null; }
                int count = Mathf.Clamp(I(a, 1, 40), 1, 300);
                string filter = Rest(a, 2);
                var lines = new List<string>();
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var reader = new StreamReader(fs))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        if (line.Length == 0 || (filter.Length > 0 && line.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0)) continue;
                        lines.Add(line.Length > 400 ? line.Substring(0, 400) + "..." : line);
                        if (lines.Count > count) lines.RemoveAt(0);
                    }
                }
                output(new JObject { ["log"] = new JArray(lines) });
                return null;
            });

            Builtin("give", "give <item prefab> [amount]: put an item in the player's inventory (for trying out a mod's new item)", (a, output, error) =>
            {
                if (a.Length < 2) { error("give <item prefab> [amount]"); return null; }
                GameObject prefab = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(a[1]) : null;
                if (prefab == null) { error($"no item called '{a[1]}'"); return null; }
                int amount = Mathf.Clamp(I(a, 2, 1), 1, 100);
                ItemDrop.ItemData added = Player.m_localPlayer.GetInventory().AddItem(prefab.name, amount, 1, 0, 0L, "", false);
                if (added == null) { error("the inventory is full"); return null; }
                added.m_worldLevel = (byte)Game.m_worldLevel;   // as an item picked up in this world has, or recipes would not count it
                output(new JObject { ["gave"] = prefab.name, ["amount"] = amount });
                return null;
            });

            Builtin("grow", "grow <seconds> [radius]: make the plants (cultivated seedlings) near the player <seconds> older, so they grow without the wait", (a, output, error) =>
            {
                if (a.Length < 2) { error("grow <seconds> [radius]"); return null; }
                float seconds = F(a, 1, 60f), radius = Mathf.Clamp(F(a, 2, 20f), 2f, 60f);
                Vector3 me = Player.m_localPlayer.transform.position;
                int key = "plantTime".GetStableHashCode();
                var done = new JArray();
                foreach (Plant plant in UnityEngine.Object.FindObjectsByType<Plant>(FindObjectsSortMode.None).Where(x => x != null && Vector3.Distance(x.transform.position, me) <= radius))
                {
                    ZNetView view = plant.GetComponent<ZNetView>();
                    if (view == null || !view.IsValid()) continue;
                    if (!view.IsOwner()) view.ClaimOwnership();
                    ZDO zdo = view.GetZDO();
                    long was = zdo.GetLong(key, 0L);
                    if (was == 0L) continue;
                    if (seconds == 0f)   // "grow 0": make it grow right now and say what came of it
                    {
                        try { GameObject made = plant.Grow(); done.Add(new JObject { ["plant"] = Utils.GetPrefabName(plant.gameObject), ["grewInto"] = made != null ? Utils.GetPrefabName(made) : "(nothing: " + plant.GetStatus() + ")" }); }
                        catch (Exception e) { done.Add(new JObject { ["plant"] = Utils.GetPrefabName(plant.gameObject), ["error"] = e.GetType().Name + ": " + e.Message }); }
                        continue;
                    }
                    zdo.Set(key, was - (long)(seconds * TimeSpan.TicksPerSecond));
                    done.Add(new JObject { ["plant"] = Utils.GetPrefabName(plant.gameObject), ["position"] = Vec(plant.transform.position), ["age"] = Math.Round((ZNet.instance.GetTime().Ticks - (was - (long)(seconds * TimeSpan.TicksPerSecond))) / (double)TimeSpan.TicksPerSecond),
                        ["status"] = plant.GetStatus().ToString(), ["growsInto"] = new JArray(plant.m_grownPrefabs.Select(g => g != null ? g.name : "NOTHING")), ["owner"] = view.IsOwner(), ["growTime"] = plant.m_growTime });
                }
                output(new JObject { ["aged"] = done, ["note"] = "plants check their growth every ten seconds or so" });
                return null;
            });

            Builtin("fixlevels", "fixlevels <prefix>: give every item in the bag whose prefab name starts with <prefix> this world's level (for items added by an older give, which recipes would not count)", (a, output, error) =>
            {
                if (a.Length < 2) { error("fixlevels <prefix>"); return null; }
                int n = 0;
                foreach (ItemDrop.ItemData item in Player.m_localPlayer.GetInventory().GetAllItems())
                    if (item.m_dropPrefab != null && item.m_dropPrefab.name.StartsWith(a[1]) && item.m_worldLevel != (byte)Game.m_worldLevel) { item.m_worldLevel = (byte)Game.m_worldLevel; n++; }
                output(new JObject { ["fixed"] = n, ["worldLevel"] = Game.m_worldLevel });
                return null;
            });

            Builtin("drop", "drop <item prefab> [amount]: drop an item from the player's bag on the ground in front of them, as dragging it out of the bag does", (a, output, error) =>
            {
                if (a.Length < 2) { error("drop <item prefab> [amount]"); return null; }
                Player p = Player.m_localPlayer;
                GameObject prefab = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(a[1]) : null;
                if (prefab == null) { error($"no item called '{a[1]}'"); return null; }
                string itemName = prefab.GetComponent<ItemDrop>().m_itemData.m_shared.m_name;
                ItemDrop.ItemData item = p.GetInventory().GetAllItems().FirstOrDefault(i => i.m_shared.m_name == itemName);
                if (item == null) { error($"the player has no '{a[1]}'"); return null; }
                bool dropped = p.DropItem(p.GetInventory(), item, Mathf.Clamp(I(a, 2, 1), 1, item.m_stack));
                output(new JObject { ["dropped"] = a[1], ["ok"] = dropped });
                return null;
            });

            Builtin("pickup", "pickup <item prefab>: pick up the nearest dropped item of that kind within 4 m, as walking up to it and pressing use does", (a, output, error) =>
            {
                if (a.Length < 2) { error("pickup <item prefab>"); return null; }
                Player p = Player.m_localPlayer;
                ItemDrop nearest = UnityEngine.Object.FindObjectsByType<ItemDrop>(FindObjectsSortMode.None)
                    .Where(d => d != null && d.gameObject.name.StartsWith(a[1]) && Vector3.Distance(d.transform.position, p.transform.position) < 4f)
                    .OrderBy(d => Vector3.Distance(d.transform.position, p.transform.position)).FirstOrDefault();
                if (nearest == null) { error($"no dropped '{a[1]}' within 4 m"); return null; }
                bool ok = p.Pickup(nearest.gameObject, false, false);
                output(new JObject { ["pickedUp"] = a[1], ["ok"] = ok });
                return null;
            });

            Builtin("use", "use <item prefab>: use an item from the player's inventory, as a double-click does (eat, drink, light)", (a, output, error) =>
            {
                if (a.Length < 2) { error("use <item prefab>"); return null; }
                Player p = Player.m_localPlayer;
                GameObject prefab = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(a[1]) : null;
                if (prefab == null) { error($"no item called '{a[1]}'"); return null; }
                string itemName = prefab.GetComponent<ItemDrop>().m_itemData.m_shared.m_name;
                Func<IEnumerable<ItemDrop.ItemData>> mine = () => p.GetInventory().GetAllItems().Where(i => i.m_shared.m_name == itemName);
                ItemDrop.ItemData item = mine().FirstOrDefault();
                if (item == null) { error($"the player has no '{a[1]}'"); return null; }
                int before = mine().Sum(i => i.m_stack);
                p.UseItem(p.GetInventory(), item, false);
                output(new JObject { ["used"] = a[1], ["countBefore"] = before, ["countAfter"] = mine().Sum(i => i.m_stack) });
                return null;
            });

            Builtin("say","say <text>: show a message in the middle of the player's screen", (a, output, error) =>
            {
                string text = Rest(a, 1);
                Player.m_localPlayer.Message(MessageHud.MessageType.Center, text.Length > 200 ? text.Substring(0, 200) : text);
                output(new JObject { ["said"] = text });
                return null;
            });

            Builtin("pin", "pin <place|x z> <text>: put a pin with a label on the player's map (place: here, look, or a name)", (a, output, error) =>
            {
                Vector3 pos; string label;
                if (a.Length >= 3 && float.TryParse(a[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float x)
                    && float.TryParse(a[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float z))
                { pos = new Vector3(x, 0f, z); label = Rest(a, 3); }
                else
                {
                    if (!Frame(a.Length > 1 ? a[1] : "here", out pos, out float _, out string err)) { error(err); return null; }
                    label = Rest(a, 2);
                }
                if (!AddPin(pos, label.Length > 0 ? label : "Claude")) { error("could not add a pin (the map is not ready?)"); return null; }
                output(new JObject { ["pin"] = label, ["at"] = Vec(pos) });
                return null;
            });
        }

        private static IEnumerator Wait(float seconds)
        {
            yield return new WaitForSeconds(seconds);
        }

        /// <summary>The mods running, from BepInEx's own list (hot-reloaded ones too). Much cheaper than Resources.FindObjectsOfTypeAll.</summary>
        private static IEnumerable<BaseUnityPlugin> LoadedMods() => Chainloader.PluginInfos.Values.Select(i => i.Instance).Where(p => p != null);

        private static BaseUnityPlugin FindMod(string name) =>
            LoadedMods().FirstOrDefault(m =>
            {
                BepInPlugin meta = MetadataHelper.GetMetadata(m);
                return meta != null && (meta.GUID.Equals(name, StringComparison.OrdinalIgnoreCase) || meta.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            });

        private static string TomlDefault(ConfigEntryBase entry)
        {
            try { return TomlTypeConverter.ConvertToString(entry.DefaultValue, entry.SettingType); }
            catch (Exception) { return entry.DefaultValue?.ToString(); }
        }

        /// <summary>A pin on the map, through whichever AddPin the game has (its parameters change between versions).</summary>
        private static bool AddPin(Vector3 pos, string label)
        {
            if (Minimap.instance == null) return false;
            MethodInfo add = typeof(Minimap).GetMethods().FirstOrDefault(m => m.Name == "AddPin" && m.GetParameters().Length >= 3
                && m.GetParameters()[0].ParameterType == typeof(Vector3) && m.GetParameters()[2].ParameterType == typeof(string));
            if (add == null) return false;
            var args = add.GetParameters().Select((p, i) =>
                i == 0 ? (object)pos :
                i == 1 ? Minimap.PinType.Icon3 :
                i == 2 ? label :
                p.Name == "save" ? true :
                p.HasDefaultValue ? p.DefaultValue :
                p.ParameterType.IsValueType ? Activator.CreateInstance(p.ParameterType) : null).ToArray();
            add.Invoke(Minimap.instance, args);
            return true;
        }
    }
}
