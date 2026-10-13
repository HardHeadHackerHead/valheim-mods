using System;
using System.Linq;
using Jint;
using Jint.Native;
using Jint.Runtime.Descriptors;
using Jint.Runtime.Interop;
using UnityEngine;
using static Scripts.Api;

namespace Scripts
{
    /// <summary>
    /// What a script holds in place of a player or creature: a small object that names it by id. Reading members asks the game at that
    /// moment (a copy, never the live object); calls look the object up again each time, so one that has gone is simply "not valid" and
    /// every call on it does nothing. Nothing here can reach the game object behind it.
    /// </summary>
    internal static class Handles
    {
        internal static string PrefabName(Character c) => c is Player ? "Player" : Utils.GetPrefabName(c.gameObject);

        internal static string Id(Character c)
        {
            if (c is Player p) return "p" + p.GetPlayerID();
            ZDOID z = c.GetZDOID();
            return z.IsNone() ? "" : z.ToString();   // (a creature whose record the game already removed has no id: never "0:0", which every such creature would share)
        }

        /// <summary>The character with that id, if it is loaded and alive in the world.</summary>
        internal static Character Find(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (Character c in Character.GetAllCharacters()) if (c != null && Id(c) == id) return c;
            return null;
        }

        internal static void Getter(Engine e, JsObject o, string name, Func<JsValue> read)
        {
            var fn = new ClrFunction(e, "get " + name, (self, args) => read());
            o.DefineOwnProperty(name, new GetSetPropertyDescriptor(fn, null, true, true));
        }

        internal static void Method(Script s, JsObject o, string name, Func<JsValue[], JsValue> f) => o.Set(name, Func(s, name, f));

        /// <summary>What an actor was at one moment: an event is handed to scripts a frame later, when a creature that died is already gone.</summary>
        internal sealed class Snap
        {
            public string Id, Prefab, Name, SpawnedBy;
            public Vector3 Pos;
            public float MaxHp;
            public int Level;
            public bool IsPlayer, IsBoss, IsTamed;
        }

        internal static Snap Take(Character c)
        {
            if (c == null) return null;
            return new Snap
            {
                Id = Id(c), Prefab = PrefabName(c), IsPlayer = c is Player, IsBoss = c.IsBoss(), IsTamed = c.IsTamed(), Pos = c.transform.position, MaxHp = c.GetMaxHealth(),
                Level = c is Player ? 1 : c.GetLevel(), Name = c is Player p ? p.GetPlayerName() : Localization.instance.Localize(c.m_name), SpawnedBy = c is Player ? null : Players.SpawnedBy(c),
            };
        }

        /// <summary>The actor if it still exists, else a frozen copy of what it was (not valid, dead, every call on it does nothing).</summary>
        internal static JsValue ActorOrFrozen(Script s, Snap snap)
        {
            if (snap == null) return JsValue.Null;
            Character now = Find(snap.Id);
            if (now != null) return Actor(s, now);
            Engine e = s.Engine;
            var o = new JsObject(e);
            o.Set("id", Text(snap.Id)); o.Set("prefab", Text(snap.Prefab)); o.Set("name", Text(snap.Name)); o.Set("pos", Vec(e, snap.Pos));
            o.Set("hp", Num(0)); o.Set("maxHp", Num(snap.MaxHp)); o.Set("level", Num(snap.Level));
            o.Set("isPlayer", Bool(snap.IsPlayer)); o.Set("isBoss", Bool(snap.IsBoss)); o.Set("isTamed", Bool(snap.IsTamed)); o.Set("isDead", Bool(true)); o.Set("valid", Bool(false));
            if (!snap.IsPlayer) o.Set("spawnedBy", snap.SpawnedBy != null ? Text(snap.SpawnedBy) : JsValue.Null);
            // the calls exist and do nothing, as the API says of a handle that is gone
            foreach (string name in new[] { "heal", "damage", "say", "give", "take", "teleport" }) o.Set(name, Func(s, name, a => JsValue.Null));
            o.Set("count", Func(s, "count", a => Num(0))); o.Set("has", Func(s, "has", a => Bool(false)));
            o.Set("distanceTo", Func(s, "distanceTo", a => JsValue.Null));
            ActorExtras.AddFrozen(s, o);
            return o;
        }

        internal static JsValue Actor(Script s, Character c)
        {
            Engine e = s.Engine;
            string id = Id(c);
            var o = new JsObject(e);
            o.Set("id", Text(id));
            o.Set("prefab", Text(PrefabName(c)));
            o.Set("isPlayer", Bool(c is Player));
            JsValue Live(Func<Character, JsValue> read) { Character now = Find(id); return now == null ? JsValue.Null : read(now); }
            Getter(e, o, "valid", () => Bool(Find(id) != null));
            Getter(e, o, "name", () => Live(x => Text(x is Player p ? p.GetPlayerName() : Localization.instance.Localize(x.m_name))));
            Getter(e, o, "pos", () => Live(x => Vec(e, x.transform.position)));
            Getter(e, o, "hp", () => Live(x => Num(x.GetHealth())));
            Getter(e, o, "maxHp", () => Live(x => Num(x.GetMaxHealth())));
            Getter(e, o, "level", () => Live(x => Num(x is Player ? 1 : x.GetLevel())));
            Getter(e, o, "isBoss", () => Live(x => Bool(x.IsBoss())));
            Getter(e, o, "isTamed", () => Live(x => Bool(x.IsTamed())));
            Getter(e, o, "isDead", () => Live(x => Bool(x.IsDead())));
            Method(s, o, "distanceTo", a => { Character x = Find(id); return x == null ? JsValue.Null : Num(Vector3.Distance(x.transform.position, Where(s, Arg(a, 0)))); });
            Method(s, o, "heal", a => { Need(s, "combat"); Act(s); if (!Allowed(s, "combat")) return Refused(s, "combat not approved"); Character x = Find(id); return x == null ? Num(0) : Num(Players.Heal(s, x, N(a, 0))); });
            Method(s, o, "damage", a => { Need(s, "combat"); Act(s); if (!Allowed(s, "combat")) return Refused(s, "combat not approved"); Character x = Find(id); return x == null ? Num(0) : Num(Players.Damage(s, x, N(a, 0))); });

            if (c is Player)
            {
                Getter(e, o, "isDodging", () => Live(x => Bool(x.InDodge())));
                Getter(e, o, "isAdmin", () => Live(x => Bool(x is Player p && Players.IsAdmin(p))));
                Getter(e, o, "biome", () => Live(x => Text(Biome(x.transform.position))));
                Method(s, o, "say", a => { Need(s, "messages"); Act(s); if (!Allowed(s, "messages")) return Refused(s, "messages not approved"); Player p = Find(id) as Player; if (p != null) Players.Say(s, p, Str(a, 0, 120), a.Length > 1 ? a[1] : JsValue.Undefined); return JsValue.Undefined; });
                Method(s, o, "give", a => { Need(s, "items"); Act(s); if (!Allowed(s, "items")) return Refused(s, "items not approved"); Player p = Find(id) as Player; return p == null ? Num(0) : Num(Players.Give(s, p, Str(a, 0, 40), a.Length > 1 ? (int)N(a, 1) : 1)); });
                Method(s, o, "take", a => { Need(s, "takeItems"); Act(s); if (!Allowed(s, "takeItems")) return Refused(s, "takeItems not approved"); Player p = Find(id) as Player; return p == null ? Num(0) : Num(Players.Take(s, p, Str(a, 0, 40), a.Length > 1 ? (int)N(a, 1) : 1)); });
                Method(s, o, "count", a => { if (!s.Manifest.Needs.Contains("items") && !s.Manifest.Needs.Contains("takeItems")) Need(s, "items"); Player p = Find(id) as Player; return p == null ? Num(0) : Num(Players.Count(p, Str(a, 0, 40))); });
                Method(s, o, "has", a => { if (!s.Manifest.Needs.Contains("items") && !s.Manifest.Needs.Contains("takeItems")) Need(s, "items"); Player p = Find(id) as Player; return p == null ? Bool(false) : Bool(Players.Count(p, Str(a, 0, 40)) >= (a.Length > 1 ? (int)N(a, 1) : 1)); });
                Method(s, o, "teleport", a => { Need(s, "teleport"); Act(s); if (!Allowed(s, "teleport")) return Refused(s, "teleport not approved"); Player p = Find(id) as Player; return p == null ? Bool(false) : Bool(Players.Teleport(s, p, Where(s, Arg(a, 0)))); });
            }
            else
            {
                o.Set("spawnedBy", Players.SpawnedBy(c) is string by ? Text(by) : JsValue.Null);
            }
            ActorExtras.Add(s, o, c, id);
            return o;
        }

        private static string Biome(Vector3 p)
        {
            Heightmap.Biome b = WorldGenerator.instance != null ? WorldGenerator.instance.GetBiome(p) : Heightmap.Biome.Meadows;
            return b == Heightmap.Biome.None ? "Meadows" : b.ToString();
        }
    }
}
