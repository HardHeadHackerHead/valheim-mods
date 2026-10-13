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
    /// Decorations a script can place and take away (docs/scripting-expansion.md section 3). Only prefabs on Lists.Pieces, and only ones
    /// that hold nothing, craft nothing, sleep nobody and hurt nobody (checked on the prefab itself, not trusted from the list). They stay
    /// after the script stops; the script that placed one is the only one that may remove it. The mark is a key on the piece's ZDO.
    /// </summary>
    internal static class Pieces
    {
        // Parts a decoration must not have (measured 2026-10-12 with inspect: braziers and fire pits carry Aoe, the damage area).
        private static readonly HashSet<string> Deny = new HashSet<string>
        {
            "Aoe", "Container", "CraftingStation", "StationExtension", "Bed", "TeleportWorld", "Door", "ItemStand", "ArmorStand", "Smelter", "CookingStation", "Fermenter",
            "Beehive", "Turret", "Ship", "Vagon", "Humanoid", "Character", "MonsterAI", "BaseAI", "AnimalAI", "ItemDrop", "Pickable", "Plant", "Trap", "Incinerator",
            "PrivateArea", "Windmill", "SapCollector", "Switch", "Projectile", "TreeBase", "Destructible", "Rigidbody",
        };

        private static readonly Dictionary<string, string> Checked = new Dictionary<string, string>();
        private static readonly Dictionary<string, List<ZDOID>> Alive = new Dictionary<string, List<ZDOID>>();
        private static readonly int PieceMask = LayerMask.GetMask("piece", "piece_nonsolid");

        private static string Unsafe(GameObject prefab, string name)
        {
            if (Checked.TryGetValue(name, out string known)) return known;
            string why = prefab.GetComponent<Piece>() == null ? "'" + name + "' is not a piece" : "";
            if (why.Length == 0)
                foreach (Component c in prefab.GetComponentsInChildren<Component>(true))
                {
                    if (c == null) continue;
                    string type = c.GetType().Name;
                    if (!Deny.Contains(type)) continue;
                    why = "'" + name + "' carries '" + type + "', so it is not a decoration";
                    break;
                }
            if (why.Length > 0) Core.Log.LogWarning("Scripts: " + why + " (refused as a decoration)");
            Checked[name] = why;
            return why;
        }

        internal static JsValue Spawn(Script s, string prefab, Vector3 pos, JsValue rot)
        {
            if (!Lists.Pieces.Contains(prefab)) return Refused(s, "'" + prefab + "' is not on the list of decorations a script may place");
            GameObject go = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(prefab) : null;
            if (go == null) return Refused(s, "this game has no piece called '" + prefab + "'");
            string why = Unsafe(go, prefab);
            if (why.Length > 0) return Refused(s, why);
            if (!Players.NearAPlayer(pos, 100f)) return Refused(s, "that place is too far from every player");
            if (!ZoneSystem.instance.IsZoneLoaded(pos)) return Refused(s, "that place is not loaded");
            if (!PrivateArea.CheckAccess(pos, 0f, false, false)) return Refused(s, "that place is inside a ward");
            int per = Limits.PiecesPerMinute;
            if (!Players.Rate(s, "piece", per / 60.0, per)) return Refused(s, "too many pieces a minute (" + per + " is the limit; the owner can change it in the Blockscript settings)");
            if (!Alive.TryGetValue(s.File, out List<ZDOID> list)) Alive[s.File] = list = new List<ZDOID>();
            list.RemoveAll(id => ZDOMan.instance.GetZDO(id) == null);
            if (list.Count >= Limits.PiecesAlive) return Refused(s, Limits.PiecesAlive + " pieces from this script stand already (the owner can change this in the Blockscript settings)");
            if (ZoneSystem.instance.GetSolidHeight(pos, out float h) && Math.Abs(pos.y - h) < 3f) pos.y = h;
            foreach (Collider c in Physics.OverlapSphere(pos + Vector3.up * 0.6f, 0.45f, PieceMask))
                if (c.GetComponentInParent<Piece>() != null) return Refused(s, "that spot is taken by another piece");
            Quaternion q = Quaternion.identity;
            if (rot != null && rot.IsObject()) q = Quaternion.Euler(Field(rot, "x"), Field(rot, "y"), Field(rot, "z"));
            GameObject made = UnityEngine.Object.Instantiate(go, pos, q);
            ZNetView view = made.GetComponent<ZNetView>();
            if (view != null && view.GetZDO() != null) { view.GetZDO().Set("dh_script", s.Name); list.Add(view.GetZDO().m_uid); }
            Audit.Add(s, $"placed {prefab} at {pos.x:0},{pos.z:0}");
            Piece piece = made.GetComponent<Piece>();
            return piece != null ? Handle(s, piece) : JsValue.Null;
        }

        private static float Field(JsValue o, string key)
        {
            JsValue v = o.AsObject().Get(key);
            return v.IsNumber() ? (float)v.AsNumber() : 0f;
        }

        internal static Piece Find(string id)
        {
            if (string.IsNullOrEmpty(id) || ZNetScene.instance == null) return null;
            string[] parts = id.Split(':');
            if (parts.Length != 2 || !long.TryParse(parts[0], out long user) || !uint.TryParse(parts[1], out uint number)) return null;
            GameObject go = ZNetScene.instance.FindInstance(new ZDOID(user, number));
            return go != null ? go.GetComponent<Piece>() : null;
        }

        private static string IdOf(Piece p)
        {
            ZNetView v = p.GetComponent<ZNetView>();
            return v != null && v.GetZDO() != null ? v.GetZDO().m_uid.ToString() : "";
        }

        private static string MadeBy(Piece p)
        {
            ZNetView v = p.GetComponent<ZNetView>();
            string by = v != null && v.GetZDO() != null ? v.GetZDO().GetString("dh_script", "") : "";
            return by.Length > 0 ? by : null;
        }

        /// <summary>What a script holds in place of a built piece: its id, and members that ask the game when read.</summary>
        internal static JsValue Handle(Script s, Piece piece)
        {
            Engine e = s.Engine;
            string id = IdOf(piece);
            var o = new JsObject(e);
            o.Set("id", Text(id));
            o.Set("prefab", Text(Utils.GetPrefabName(piece.gameObject)));
            JsValue Live(Func<Piece, JsValue> read) { Piece now = Find(id); return now == null ? JsValue.Null : read(now); }
            Handles.Getter(e, o, "valid", () => Bool(Find(id) != null));
            Handles.Getter(e, o, "pos", () => Live(x => Vec(e, x.transform.position)));
            Handles.Getter(e, o, "rot", () => Live(x => Vec(e, x.transform.eulerAngles)));
            Handles.Getter(e, o, "builtBy", () => Live(x => x.GetCreator() != 0L ? Text("p" + x.GetCreator()) : JsValue.Null));
            Handles.Getter(e, o, "spawnedBy", () => Live(x => MadeBy(x) is string by ? Text(by) : JsValue.Null));
            Handles.Method(s, o, "distanceTo", a => { Piece x = Find(id); return x == null ? JsValue.Null : Num(Vector3.Distance(x.transform.position, Where(s, Arg(a, 0)))); });
            Handles.Method(s, o, "remove", a =>
            {
                if (!Extras.Gate(s, "world")) return Bool(false);
                Piece x = Find(id);
                if (x == null) return Bool(false);
                if (MadeBy(x) != s.Name) return Refused(s, "removing a piece this script did not place");
                ZNetView view = x.GetComponent<ZNetView>();
                if (view != null) view.ClaimOwnership();
                ZNetScene.instance.Destroy(x.gameObject);
                Audit.Add(s, $"removed {Utils.GetPrefabName(x.gameObject)}");
                return Bool(true);
            });
            return o;
        }
    }
}
