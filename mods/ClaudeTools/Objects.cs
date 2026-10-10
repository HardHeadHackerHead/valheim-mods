using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace ClaudeTools
{
    /// <summary>
    /// "objects": every object of one prefab in the world, loaded or not (a piece built far away, a mod's leftovers), and removing them.
    /// The game that hosts the world (single player, or the host) knows every object; a client only knows those near the players.
    /// Removing follows the game's cheat rule (single player, the host, or devcommands on) and only takes an exact prefab name.
    /// </summary>
    public partial class Plugin
    {
        private void RegisterObjectCommands()
        {
            Builtin("prefabs", "prefabs <text>: every prefab name (pieces, items, creatures, effects) containing the text: the exact name objects, " +
                "render, inspect and give need", (a, output, error) =>
            {
                string text = Rest(a, 1).ToLowerInvariant();
                if (text.Length == 0) { error("say what to look for, e.g. prefabs warstone"); return null; }
                if (ZNetScene.instance == null) { error("no world loaded"); return null; }
                var names = ZNetScene.instance.m_prefabs.Where(p => p != null && p.name.ToLowerInvariant().Contains(text))
                    .Select(p => p.name).Distinct().OrderBy(n => n).ToList();
                output(new JObject { ["count"] = names.Count, ["prefabs"] = new JArray(names.Take(150)) });
                return null;
            });
            RegisterObjectsCommand();
        }

        private void RegisterObjectsCommand() =>
            Builtin("objects", "objects <prefab> [remove]: every object of that prefab in the world (where, loaded or not); remove deletes them all " +
                "(exact prefab name; only where cheats are allowed)", (a, output, error) =>
            {
                string prefab = a.Length > 1 ? a[1] : "";
                bool remove = a.Length > 2 && a[2].Equals("remove", StringComparison.OrdinalIgnoreCase);
                if (prefab.Length == 0) { error("say which prefab, e.g. objects dh_arena_standard"); return null; }
                if (ZDOMan.instance == null || ZNetScene.instance == null) { error("no world loaded"); return null; }
                if (ZNetScene.instance.GetPrefab(prefab) == null) { error($"no prefab called {prefab} (exact name, as in inspect or nearby)"); return null; }
                if (remove && !CheatsAllowed()) { error(CheatRefusal("objects remove")); return null; }

                var found = new List<ZDO>();
                int index = 0;
                while (!ZDOMan.instance.GetAllZDOsWithPrefabIterative(prefab, found, ref index)) { } // (the game's own search, sector by sector)
                found = found.Distinct().ToList();
                Vector3 me = Player.m_localPlayer != null ? Player.m_localPlayer.transform.position : Vector3.zero;
                var list = new JArray(found.OrderBy(z => (z.GetPosition() - me).sqrMagnitude).Take(60).Select(z => new JObject
                {
                    ["at"] = Vec(z.GetPosition()), ["distance"] = Math.Round(Vector3.Distance(z.GetPosition(), me)),
                    ["loaded"] = ZNetScene.instance.FindInstance(z) != null, ["creator"] = z.GetLong(ZDOVars.s_creator, 0L),
                }));
                var result = new JObject
                {
                    ["prefab"] = prefab, ["count"] = found.Count, ["list"] = list,
                    ["complete"] = ZNet.instance != null && ZNet.instance.IsServer() ? "every one in the world" : "only those near the players (a client doesn't know the rest)",
                };
                if (remove)
                {
                    int removed = 0;
                    foreach (ZDO z in found)
                    {
                        try
                        {
                            ZNetView view = ZNetScene.instance.FindInstance(z);
                            if (view != null)
                            {
                                view.ClaimOwnership();
                                ZNetScene.instance.Destroy(view.gameObject);
                            }
                            else
                            {
                                z.SetOwner(ZDOMan.GetSessionID()); // (only an object's owner may destroy it)
                                ZDOMan.instance.DestroyZDO(z);
                            }
                            removed++;
                        }
                        catch (Exception e) { Log?.LogWarning($"objects remove: {prefab} at {z.GetPosition()}: {e.Message}"); }
                    }
                    Log?.LogInfo($"objects remove: removed {removed} of {found.Count} {prefab}");
                    result["removed"] = removed;
                }
                output(result);
                return null;
            });
    }
}
