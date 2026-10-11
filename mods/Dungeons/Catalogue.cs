using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Dungeons
{
    /// <summary>
    /// The game's own dungeon rooms, from DungeonDB: each one's name, themes, size, doorways (position and facing in the room's own frame,
    /// type, entrance, whether a door may go there) and the networked parts inside it (doors, chests, spawners: what the game saves).
    /// Read by loading each room's prefab (a soft reference) and letting it go again.
    /// </summary>
    internal static class Catalogue
    {
        public static JArray Rooms(string theme)
        {
            var list = new JArray();
            if (DungeonDB.instance == null) return list;
            foreach (DungeonDB.RoomData data in DungeonDB.GetRooms())
            {
                if (!string.IsNullOrEmpty(theme) && data.m_theme.ToString().IndexOf(theme, System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                data.m_prefab.Load();
                try
                {
                    GameObject prefab = data.m_prefab.Asset;
                    Room room = prefab != null ? prefab.GetComponent<Room>() : null;
                    if (room == null) continue;
                    Transform root = room.transform;
                    var doors = new JArray();
                    foreach (RoomConnection c in room.GetComponentsInChildren<RoomConnection>(true))
                    {
                        doors.Add(new JObject
                        {
                            ["type"] = c.m_type,
                            ["at"] = V(root.InverseTransformPoint(c.transform.position)),
                            ["facing"] = V(root.InverseTransformDirection(c.transform.forward)),
                            ["entrance"] = c.m_entrance,
                            ["door"] = c.m_allowDoor,
                        });
                    }
                    var parts = room.GetComponentsInChildren<ZNetView>(true)
                        .GroupBy(z => z.gameObject.name.Split('(')[0].Trim()).OrderByDescending(g => g.Count())
                        .Select(g => g.Count() > 1 ? $"{g.Key} x{g.Count()}" : g.Key);
                    Bounds? box = null;
                    foreach (Renderer r in room.GetComponentsInChildren<Renderer>(true))
                    {
                        if (!(r is MeshRenderer || r is SkinnedMeshRenderer)) continue;   // (not particles or lines)
                        if (box == null) box = r.bounds;
                        else { Bounds b = box.Value; b.Encapsulate(r.bounds); box = b; }
                    }
                    list.Add(new JObject
                    {
                        ["name"] = prefab.name,
                        ["theme"] = data.m_theme.ToString(),
                        ["enabled"] = data.m_enabled && room.m_enabled,
                        ["size"] = new JArray(room.m_size.x, room.m_size.y, room.m_size.z),
                        ["entrance"] = room.m_entrance,
                        ["endCap"] = room.m_endCap,
                        ["divider"] = room.m_divider,
                        ["perimeter"] = room.m_perimeter,
                        ["weight"] = room.m_weight,
                        ["meshBox"] = box == null ? null : new JObject { ["centre"] = V(root.InverseTransformPoint(box.Value.center)), ["size"] = V(box.Value.size) },
                        ["doorways"] = doors,
                        ["networked"] = new JArray(parts),
                    });
                }
                finally { data.m_prefab.Release(); }
            }
            return list;
        }

        private static JArray V(Vector3 v) => new JArray(Mathf.Round(v.x * 100f) / 100f, Mathf.Round(v.y * 100f) / 100f, Mathf.Round(v.z * 100f) / 100f);
    }
}
