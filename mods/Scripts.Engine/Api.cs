using System;
using System.Collections.Generic;
using System.Linq;
using Jint;
using Jint.Native;
using Jint.Runtime.Interop;
using UnityEngine;

namespace Scripts
{
    /// <summary>A bug in the script (a wrong argument, a power it did not list, an action at top level): the current handler stops.</summary>
    internal sealed class ScriptError : Exception { public ScriptError(string message) : base(message) { } }

    /// <summary>
    /// What a script can call (docs/scripting-api.md, version 1). Every global is a function the host made: a script never holds a game
    /// object, only plain values and handles (Handles.cs) that name an object by id. A call that needs a power the script did not list is
    /// a ScriptError; a power it listed but nobody approved, a cap, an unknown name or a dead handle is a refusal (the call returns
    /// false, 0 or null and the script goes on). Sections of the API not built yet say so when called.
    /// </summary>
    internal static class Api
    {
        internal const int Version = 1;
        private static readonly System.Random Rng = new System.Random();   // (seeded by the host: a script can't fix the odds)

        // ---- binding ----

        internal static void Bind(Script s)
        {
            Engine e = s.Engine;

            // time
            Fn(s, "minutes", a => Num(N(a, 0) * 60.0));
            Fn(s, "hours", a => Num(N(a, 0) * 3600.0));
            Fn(s, "time", a => Num(ZNet.instance != null ? ZNet.instance.GetTimeSeconds() : 0.0));
            Fn(s, "day", a => Num(Events.DayNumber()));
            Fn(s, "timeOfDay", a => Num(EnvMan.instance != null ? EnvMan.instance.GetDayFraction() : 0.5));
            Fn(s, "isNight", a => Bool(EnvMan.instance != null && EnvMan.IsNight()));
            Fn(s, "isDay", a => Bool(EnvMan.instance != null && EnvMan.IsDay()));

            // random
            Fn(s, "chance", a => Bool(Rng.NextDouble() < N(a, 0)));
            Fn(s, "random", a => { double lo = N(a, 0), hi = N(a, 1); return Num(lo + Rng.NextDouble() * (hi - lo)); });
            Fn(s, "randomInt", a => { int lo = (int)Math.Floor(N(a, 0)), hi = (int)Math.Floor(N(a, 1)); if (hi < lo) { int t = lo; lo = hi; hi = t; } return Num(lo + Rng.Next(hi - lo + 1)); });
            Fn(s, "pick", a =>
            {
                if (!(Arg(a, 0) is JsArray list) || list.Length == 0) return JsValue.Null;
                return list.Get((uint)Rng.Next((int)list.Length));
            });

            // positions
            Fn(s, "vec", a => Vec(e, new Vector3((float)N(a, 0), (float)N(a, 1), (float)N(a, 2))));
            Fn(s, "distance", a => Num(Vector3.Distance(Where(s, Arg(a, 0)), Where(s, Arg(a, 1)))));
            Fn(s, "offset", a => Vec(e, Where(s, Arg(a, 0)) + new Vector3((float)N(a, 1), (float)N(a, 2), (float)N(a, 3))));
            Fn(s, "around", a =>
            {
                Vector3 p = Where(s, Arg(a, 0)); double r = Math.Min(200.0, Math.Max(0.0, N(a, 1)));
                double ang = Rng.NextDouble() * Math.PI * 2.0, d = Math.Sqrt(Rng.NextDouble()) * r;
                return Vec(e, new Vector3(p.x + (float)(Math.Cos(ang) * d), p.y, p.z + (float)(Math.Sin(ang) * d)));
            });
            Fn(s, "groundHeight", a => ZoneSystem.instance != null && ZoneSystem.instance.GetSolidHeight(Where(s, Arg(a, 0)), out float h) ? Num(h) : JsValue.Null);
            Fn(s, "biomeAt", a => Text(WorldGen(Where(s, Arg(a, 0)))));

            // looking things up
            Fn(s, "players", a => Array(e, Player.GetAllPlayers().Select(p => Handles.Actor(s, p)).ToArray()));
            Fn(s, "findPlayer", a =>
            {
                string name = Str(a, 0, 40);
                Player p = Player.GetAllPlayers().FirstOrDefault(x => string.Equals(x.GetPlayerName(), name, StringComparison.OrdinalIgnoreCase));
                return p == null ? JsValue.Null : Handles.Actor(s, p);
            });
            Fn(s, "findById", a => Extras.FindById(s, Str(a, 0, 64)));
            Fn(s, "nearbyCreatures", a => NearbyCreatures(s, Where(s, Arg(a, 0)), N(a, 1), Arg(a, 2)));
            Fn(s, "weather", a => Text(CurrentWeather()));
            {
                // log(anything, ...) and log.info / warn / error / debug, print(...) and assert(condition, text): lines for whoever is writing the script
                JsValue logFn = Func(s, "log", a => UserLog(s, LogLevel.Info, a));
                Jint.Native.Object.ObjectInstance logObj = logFn.AsObject();
                logObj.Set("info", Func(s, "info", a => UserLog(s, LogLevel.Info, a)));
                logObj.Set("warn", Func(s, "warn", a => UserLog(s, LogLevel.Warn, a)));
                logObj.Set("error", Func(s, "error", a => UserLog(s, LogLevel.Error, a)));
                logObj.Set("debug", Func(s, "debug", a => UserLog(s, LogLevel.Debug, a)));
                e.SetValue("log", logFn);
                Fn(s, "print", a => UserLog(s, LogLevel.Info, a));
                Fn(s, "assert", a =>
                {
                    JsValue c = Arg(a, 0);
                    bool ok = c.IsBoolean() ? c.AsBoolean() : !(c.IsUndefined() || c.IsNull() || (c.IsNumber() && c.AsNumber() == 0) || (c.IsString() && c.AsString().Length == 0));
                    if (!ok) UserLog(s, LogLevel.Error, new[] { new Jint.Native.JsString("assertion failed" + (a.Length > 1 ? ": " + Show(s, a[1]) : "")) });
                    return Bool(ok);
                });
            }

            // registering
            Fn(s, "on", a =>
            {
                string name = Str(a, 0, 30); JsValue fn = Arg(a, 1);
                if (!Events.Known(name)) throw new ScriptError("There is no event called '" + name + "'.");
                if (!fn.IsObject()) throw new ScriptError("on(name, function): the second thing must be a function.");
                if (!s.Handlers.TryGetValue(name, out List<JsValue> list)) s.Handlers[name] = list = new List<JsValue>();
                if (list.Count < 20) list.Add(fn);
                return JsValue.Undefined;
            });
            Fn(s, "off", a => { string name = a.Length > 0 && a[0].IsString() ? a[0].AsString() : null; if (name == null) s.Handlers.Clear(); else s.Handlers.Remove(name); return JsValue.Undefined; });
            Fn(s, "every", a => Num(Host.AddTimer(s, Arg(a, 1), N(a, 0), true)));
            Fn(s, "after", a => Num(Host.AddTimer(s, Arg(a, 1), N(a, 0), false)));
            Fn(s, "cancel", a => { Host.Cancel(s, (int)N(a, 0)); return JsValue.Undefined; });
            Fn(s, "command", a =>
            {
                string name = Str(a, 0, 20).TrimStart('/').ToLowerInvariant();
                if (name.Length == 0 || name.Any(c => !char.IsLetterOrDigit(c))) throw new ScriptError("A command name is letters and digits.");
                if (s.Commands.Count >= 5) throw new ScriptError("A script can have five commands.");
                if (Events.GameCommand(name)) throw new ScriptError("'/" + name + "' is already a command of the game.");
                s.Commands[name] = Arg(a, 1);
                return JsValue.Undefined;
            });

            // actions
            Fn(s, "broadcast", a => { Need(s, "messages"); Act(s); if (!Allowed(s, "messages")) return Refused(s, "messages not approved"); Players.Broadcast(s, Str(a, 0, 120)); return JsValue.Undefined; });

            // objects
            Set(e, "host", Obj(e, ("apiVersion", Num(Version))));
            Set(e, "store", Obj(e,
                ("get", Func(s, "get", a => Store.Get(s, Str(a, 0, 64), Str(a, 1, 64), a.Length > 2 ? a[2] : JsValue.Undefined))),
                ("set", Func(s, "set", a => Bool(Store.Put(s, Str(a, 0, 64), Str(a, 1, 64), Arg(a, 2))))),
                ("remove", Func(s, "remove", a => { Store.Remove(s, Str(a, 0, 64), Str(a, 1, 64)); return JsValue.Undefined; })),
                ("keys", Func(s, "keys", a => Array(e, Store.Keys(s, Str(a, 0, 64)).Select(k => Text(k)).ToArray())))));
            Set(e, "world", Obj(e,
                ("setWeather", Func(s, "setWeather", a => { Need(s, "weather"); Act(s); return Allowed(s, "weather") ? Bool(Players.SetWeather(s, Str(a, 0, 12), N(a, 1))) : Refused(s, "weather not approved"); })),
                ("setTime", Func(s, "setTime", a => { Need(s, "weather"); Act(s); return Allowed(s, "weather") ? Bool(Players.SetTime(s, N(a, 0))) : Refused(s, "weather not approved"); })),
                ("spawnPiece", Func(s, "spawnPiece", a => throw new ScriptError("world.spawnPiece is not built yet in this version.")))));
            Set(e, "net", Obj(e, ("send", Func(s, "send", a => throw new ScriptError("net.send is not built yet in this version.")))));
            Fn(s, "spawn", a => { Need(s, "spawn"); Act(s); return Allowed(s, "spawn") ? Players.Spawn(s, Str(a, 0, 40), Where(s, Arg(a, 1)), Arg(a, 2)) : Refused(s, "spawn not approved"); });
            Fn(s, "dropItem", a => { Need(s, "spawn"); Act(s); return Allowed(s, "spawn") ? Num(Players.DropItem(s, Str(a, 0, 40), (int)N(a, 1), Where(s, Arg(a, 2)))) : Refused(s, "spawn not approved"); });
            Fn(s, "playSound", a => throw new ScriptError("playSound is not built yet in this version."));
            Proxies.Bind(s);
            Extras.Bind(s);
        }

        internal static void Forget(Script s) => Players.ForgetScript(s);

        // ---- the powers ----

        /// <summary>The script must have listed the power in its manifest (else it is a bug in the script).</summary>
        internal static void Need(Script s, string cap)
        {
            if (!s.Manifest.Needs.Contains(cap)) throw new ScriptError("This needs '" + cap + "' in the // needs: line of the manifest.");
        }

        /// <summary>The power was approved by the player or the admin (else the call is refused).</summary>
        internal static bool Allowed(Script s, string cap) => cap == "messages" || cap == "store" || cap == "effects" || s.Approved.Contains(cap);

        /// <summary>Actions may only run inside an event or a timer: at top level players and zones may not be there.</summary>
        internal static void Act(Script s)
        {
            if (s.TopLevel) throw new ScriptError("Do that inside an event or a timer (on, every, after), not at the top of the script.");
        }

        internal static JsValue Refused(Script s, string reason) { Host.Refuse(s, reason); return JsValue.Null; }

        internal static void Log(Script s, string text) { s.Say(text); }

        /// <summary>A line written by the script itself. Any values: text is as it is, numbers and yes/no print, objects and lists as JSON.</summary>
        internal static JsValue UserLog(Script s, LogLevel level, JsValue[] a)
        {
            if (!Players.Rate(s, "log", 1.0, 30.0)) { Host.Refuse(s, "too many log lines a second (30 at once, then one a second)"); return JsValue.Undefined; }
            s.Say(level, string.Join(" ", a.Select(v => Show(s, v)).ToArray()), "", "log", 0, true);
            return JsValue.Undefined;
        }

        internal static string Show(Script s, JsValue v)
        {
            if (v == null || v.IsUndefined()) return "undefined";
            if (v.IsNull()) return "null";
            if (v.IsString()) return v.AsString();
            if (v.IsNumber() || v.IsBoolean()) return v.ToString();
            try
            {
                if (s.Json != null && s.Json.Get("stringify") is JsValue fn && fn.IsObject())
                {
                    JsValue r = s.Engine.Invoke(fn, s.Json, new[] { v });
                    if (r.IsString()) return r.AsString();
                }
            }
            catch (Exception) { }
            return v.IsObject() && v.AsObject() is Jint.Native.Function.Function ? "[function]" : "[object]";
        }

        // ---- reading arguments ----

        internal static JsValue Arg(JsValue[] a, int i) => i < a.Length ? a[i] : JsValue.Undefined;

        internal static double N(JsValue[] a, int i)
        {
            JsValue v = Arg(a, i);
            if (!v.IsNumber()) throw new ScriptError("Argument " + (i + 1) + " must be a number.");
            double d = v.AsNumber();
            if (double.IsNaN(d) || double.IsInfinity(d)) throw new ScriptError("Argument " + (i + 1) + " is not a usable number.");
            return d;
        }

        internal static string Str(JsValue[] a, int i, int max)
        {
            JsValue v = Arg(a, i);
            if (!v.IsString()) throw new ScriptError("Argument " + (i + 1) + " must be text.");
            string s = v.AsString();
            return s.Length > max ? s.Substring(0, max) : s;
        }

        // ---- making values ----

        internal static JsValue Num(double d) => JsNumber.Create(d);
        internal static JsValue Bool(bool b) => b ? JsBoolean.True : JsBoolean.False;
        internal static JsValue Text(string t) => new JsString(t ?? "");

        internal static JsValue Vec(Engine e, Vector3 v) =>
            Obj(e, ("x", Num(v.x)), ("y", Num(v.y)), ("z", Num(v.z)));

        internal static JsValue Array(Engine e, JsValue[] items) => new JsArray(e, items);

        internal static JsObject Obj(Engine e, params (string, JsValue)[] props)
        {
            var o = new JsObject(e);
            foreach ((string name, JsValue value) in props) o.Set(name, value);
            return o;
        }

        internal static void Set(Engine e, string name, JsValue value) => e.SetValue(name, value);

        internal static void Fn(Script s, string name, Func<JsValue[], JsValue> f) => s.Engine.SetValue(name, Func(s, name, f));

        internal static JsValue Func(Script s, string name, Func<JsValue[], JsValue> f) =>
            new ClrFunction(s.Engine, name, (self, args) => f(args));

        /// <summary>A position from a plain {x,y,z} object, or from a handle that has one.</summary>
        internal static Vector3 Where(Script s, JsValue v)
        {
            if (!v.IsObject()) throw new ScriptError("A position is {x, y, z} (make one with vec(x, y, z)), or a player, creature or piece.");
            var o = v.AsObject();
            JsValue pos = o.Get("pos");
            if (pos.IsObject()) o = pos.AsObject();
            JsValue x = o.Get("x"), y = o.Get("y"), z = o.Get("z");
            if (!x.IsNumber() || !y.IsNumber() || !z.IsNumber()) throw new ScriptError("A position needs numbers x, y and z.");
            return new Vector3((float)x.AsNumber(), (float)y.AsNumber(), (float)z.AsNumber());
        }

        // ---- the world ----

        private static string WorldGen(Vector3 p)
        {
            Heightmap.Biome b = WorldGenerator.instance != null ? WorldGenerator.instance.GetBiome(p) : Heightmap.Biome.Meadows;
            switch (b)
            {
                case Heightmap.Biome.BlackForest: return "BlackForest";
                case Heightmap.Biome.Swamp: return "Swamp";
                case Heightmap.Biome.Mountain: return "Mountain";
                case Heightmap.Biome.Plains: return "Plains";
                case Heightmap.Biome.Mistlands: return "Mistlands";
                case Heightmap.Biome.AshLands: return "AshLands";
                case Heightmap.Biome.DeepNorth: return "DeepNorth";
                case Heightmap.Biome.Ocean: return "Ocean";
                default: return "Meadows";
            }
        }

        internal static string CurrentWeather()
        {
            string env = EnvMan.instance != null ? EnvMan.instance.GetCurrentEnvironment()?.m_name ?? "" : "";
            string l = env.ToLowerInvariant();
            if (l.Contains("thunder") || l.Contains("storm")) return l.Contains("snow") ? "snow" : "storm";
            if (l.Contains("rain")) return "rain";
            if (l.Contains("mist") || l.Contains("fog")) return "mist";
            if (l.Contains("snow")) return "snow";
            return "clear";
        }

        private static JsValue NearbyCreatures(Script s, Vector3 center, double radius, JsValue opts)
        {
            radius = Math.Min(50.0, Math.Max(1.0, radius));
            string prefab = null; bool hostile = false;
            if (opts.IsObject())
            {
                JsValue p = opts.AsObject().Get("prefab"), h = opts.AsObject().Get("hostile");
                if (p.IsString()) prefab = p.AsString();
                hostile = h.IsBoolean() && h.AsBoolean();
            }
            var found = Character.GetAllCharacters()
                .Where(c => c != null && !c.IsPlayer() && !c.IsDead() && Vector3.Distance(c.transform.position, center) <= radius)
                .Where(c => prefab == null || Handles.PrefabName(c) == prefab)
                .Where(c => !hostile || !c.IsTamed() && c.GetFaction() != Character.Faction.Players)
                .OrderBy(c => Vector3.Distance(c.transform.position, center))
                .Take(50)
                .Select(c => Handles.Actor(s, c))
                .ToArray();
            return Array(s.Engine, found);
        }
    }
}
