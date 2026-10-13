using System;
using System.Reflection;
using Jint;
using Jint.Native;
using static Scripts.Api;

namespace Scripts
{
    /// <summary>
    /// The script's `proxy` (docs/scripting-api.md section 7): the Terminal mod's Proxy character. The Terminal depends on this mod, not the
    /// other way round, so everything here goes through one class in the Terminal that this mod finds by name when it is installed
    /// (Terminal.ScriptsBridge). Without the Terminal every call is refused and `proxy.busy` is true.
    /// </summary>
    internal static class Proxies
    {
        private static Type _bridge;

        private static float _checked;

        /// <summary>
        /// The Terminal's bridge class. A reloaded Terminal leaves its older copy in memory, so the newest copy is the one used, and the
        /// answer is looked for again every few seconds.
        /// </summary>
        internal static Type Bridge
        {
            get
            {
                if (_bridge != null && UnityEngine.Time.unscaledTime - _checked < 5f) return _bridge;
                _checked = UnityEngine.Time.unscaledTime;
                Type found = null;
                foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
                {
                    Type t = a.GetType("Terminal.ScriptsBridge", false);
                    if (t != null) found = t;   // (the last one loaded is the newest)
                }
                _bridge = found;
                return _bridge;
            }
        }

        /// <summary>Calls a static method of the Terminal's bridge: null if the Terminal is not installed.</summary>
        internal static object Call(string method, params object[] args)
        {
            Type t = Bridge;
            MethodInfo m = t?.GetMethod(method, BindingFlags.Public | BindingFlags.Static);
            if (m == null) return null;
            try { return m.Invoke(null, args); }
            catch (TargetInvocationException e) { Core.Log.LogWarning("Proxy bridge " + method + ": " + e.InnerException?.Message); return null; }
        }

        internal static void Bind(Script s)
        {
            var e = s.Engine;
            JsValue Acting(string what, Func<JsValue[], JsValue> run) => Func(s, what, a =>
            {
                Need(s, "proxy"); Act(s);
                if (!Allowed(s, "proxy")) return Refused(s, "proxy not approved");
                if (Bridge == null) return Refused(s, "the Terminal is not installed");
                return run(a);
            });
            JsValue Reading(string what, Func<JsValue[], JsValue> run) => Func(s, what, a =>
            {
                Need(s, "proxy");
                return Bridge == null ? JsValue.Null : run(a);
            });
            Set(e, "proxy", Obj(e,
                ("walkTo", Acting("walkTo", a => Bool(Proxies.Call("WalkTo", (float)Where(s, Arg(a, 0)).x, (float)Where(s, Arg(a, 0)).z, a.Length > 1 && a[1].IsObject() && a[1].AsObject().Get("run").IsBoolean() && a[1].AsObject().Get("run").AsBoolean()) is true))),
                ("stop", Acting("stop", a => { Proxies.Call("Stop"); return JsValue.Undefined; })),
                ("home", Acting("home", a => { Proxies.Call("Home"); return JsValue.Undefined; })),
                ("arrived", Reading("arrived", a => Bool(Proxies.Call("Arrived") is true))),
                ("say", Acting("say", a => { Proxies.Call("Say", Str(a, 0, 120)); return JsValue.Undefined; })),
                ("has", Reading("has", a => Bool(Proxies.Call("Has", Str(a, 0, 40), a.Length > 1 ? (int)N(a, 1) : 1) is true))),
                ("equip", Acting("equip", a => Bool(Proxies.Call("Equip", Str(a, 0, 12), Str(a, 1, 40)) is true))),
                ("use", Acting("use", a => Bool(Proxies.Call("Use", (float)Where(s, Arg(a, 0)).x, (float)Where(s, Arg(a, 0)).y, (float)Where(s, Arg(a, 0)).z) is true))),
                ("attack", Acting("attack", a => Bool(Proxies.Call("Attack", (float)Where(s, Arg(a, 0)).x, (float)Where(s, Arg(a, 0)).y, (float)Where(s, Arg(a, 0)).z) is true))),
                ("look", Acting("look", a => { var p = Where(s, Arg(a, 0)); Proxies.Call("Look", (float)p.x, (float)p.y, (float)p.z); return JsValue.Undefined; })),
                ("nearest", Reading("nearest", a => Nearest(s, Str(a, 0, 12), N(a, 1)))),
                ("deposit", Acting("deposit", a => Num(Proxies.Call("Deposit", (float)Where(s, Arg(a, 0)).x, (float)Where(s, Arg(a, 0)).y, (float)Where(s, Arg(a, 0)).z, a.Length > 1 ? OnlyList(a[1]) : null) is int n ? n : 0))),
                ("withdraw", Acting("withdraw", a => Num(Proxies.Call("Withdraw", (float)Where(s, Arg(a, 0)).x, (float)Where(s, Arg(a, 0)).y, (float)Where(s, Arg(a, 0)).z, Str(a, 1, 40), (int)N(a, 2)) is int n ? n : 0))),
                ("pos", JsValue.Null), ("busy", Bool(true)), ("isDead", Bool(false)), ("hp", Num(0)), ("maxHp", Num(0)), ("driver", JsValue.Null)));
            // live members: replaced by getters (a plain value would be a copy from when the script started)
            var proxy = e.GetValue("proxy").AsObject();
            Getter(s, proxy, "pos", () => Proxies.Call("Pos") is float[] p ? Vec(e, new UnityEngine.Vector3(p[0], p[1], p[2])) : JsValue.Null);
            Getter(s, proxy, "busy", () => Bool(Proxies.Call("Busy") is not false));
            Getter(s, proxy, "isDead", () => Bool(Proxies.Call("IsDead") is true));
            Getter(s, proxy, "hp", () => Num(Proxies.Call("Hp") is float h ? h : 0f));
            Getter(s, proxy, "maxHp", () => Num(Proxies.Call("MaxHp") is float m ? m : 0f));
            Getter(s, proxy, "driver", () => Proxies.Call("Driver") is string name && name.Length > 0 ? Text(name) : JsValue.Null);
        }

        private static void Getter(Script s, Jint.Native.Object.ObjectInstance o, string name, Func<JsValue> read)
        {
            var fn = new Jint.Runtime.Interop.ClrFunction(s.Engine, "get " + name, (self, args) => read());
            o.DefineOwnProperty(name, new Jint.Runtime.Descriptors.GetSetPropertyDescriptor(fn, null, true, true));
        }

        private static string[] OnlyList(JsValue options)
        {
            if (!options.IsObject() || !(options.AsObject().Get("only") is JsArray list)) return null;
            var names = new System.Collections.Generic.List<string>();
            for (uint i = 0; i < Math.Min(40u, list.Length); i++) if (list.Get(i).IsString()) names.Add(list.Get(i).AsString());
            return names.ToArray();
        }

        private static JsValue Nearest(Script s, string kind, double radius)
        {
            if (!(Proxies.Call("Nearest", kind, (float)Math.Min(60.0, Math.Max(1.0, radius))) is object[] found) || found.Length < 4) return JsValue.Null;
            return Obj(s.Engine, ("id", Text((string)found[0])), ("kind", Text(kind)), ("pos", Vec(s.Engine, new UnityEngine.Vector3((float)found[1], (float)found[2], (float)found[3]))), ("valid", Bool(true)));
        }
    }
}
