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
    /// The expansion's globals (docs/scripting-expansion.md): signals, the real clock, places and directions, effects and sounds, the map
    /// ping, banners, wind, random events, decorations, nearby players and pieces, and the web. Each action follows the pattern of the
    /// others: the power must be in the manifest (a ScriptError if not), approved (a refusal if not), under its rate, and in the audit log.
    /// </summary>
    internal static class Extras
    {
        internal static readonly int GroundMask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "piece_nonsolid", "terrain", "vehicle");

        /// <summary>The player or creature a handle stands for, if it is still there.</summary>
        internal static Character ActorOf(JsValue v)
        {
            if (v == null || !v.IsObject()) return null;
            JsValue id = v.AsObject().Get("id");
            return id.IsString() ? Handles.Find(id.AsString()) : null;
        }

        internal static bool Gate(Script s, string cap)
        {
            Need(s, cap);
            Act(s);
            if (Allowed(s, cap)) return true;
            Refused(s, cap + " not approved");
            return false;
        }

        internal static void Bind(Script s)
        {
            Engine e = s.Engine;

            // ---- signals: a script talking to itself ----
            Fn(s, "onSignal", a =>
            {
                string name = Str(a, 0, 30); JsValue fn = Arg(a, 1);
                if (!fn.IsObject()) throw new ScriptError("onSignal(name, function): the second thing must be a function.");
                if (!s.Signals.TryGetValue(name, out List<JsValue> list))
                {
                    if (s.Signals.Count >= 20) throw new ScriptError("A script can listen to 20 signal names.");
                    s.Signals[name] = list = new List<JsValue>();
                }
                if (list.Count < 20) list.Add(fn);
                return JsValue.Undefined;
            });
            Fn(s, "signal", a =>
            {
                string name = Str(a, 0, 30);
                if (!Players.Rate(s, "signal", 100.0, 100.0)) return Refused(s, "more than 100 signals a second");
                if (s.TopLevel) throw new ScriptError("Send signals from an event or a timer, not at the top of the script.");
                JsValue data = a.Length > 1 ? a[1] : JsValue.Undefined;
                if (s.Signals.TryGetValue(name, out List<JsValue> list)) foreach (JsValue fn in list.ToList()) Host.EnqueueSignal(s, fn, data);
                return JsValue.Undefined;
            });

            // ---- the clock and places ----
            Fn(s, "clock", a => { DateTime n = DateTime.Now; return Obj(e, ("hour", Num(n.Hour)), ("minute", Num(n.Minute)), ("second", Num(n.Second))); });
            Fn(s, "add", a => Vec(e, Where(s, Arg(a, 0)) + Where(s, Arg(a, 1))));
            Fn(s, "scale", a => Vec(e, Where(s, Arg(a, 0)) * (float)N(a, 1)));
            Fn(s, "bearing", a =>
            {
                Vector3 d = Where(s, Arg(a, 1)) - Where(s, Arg(a, 0));
                double deg = Math.Atan2(d.x, d.z) * 180.0 / Math.PI;
                return Num(deg < 0.0 ? deg + 360.0 : deg);
            });
            Fn(s, "ahead", a =>
            {
                Character c = ActorOf(Arg(a, 0));
                if (c == null) return JsValue.Null;
                double meters = Math.Max(-50.0, Math.Min(50.0, N(a, 1)));
                Vector3 flat = Quaternion.Euler(0f, c.transform.eulerAngles.y, 0f) * Vector3.forward;
                return Vec(e, c.transform.position + flat * (float)meters);
            });
            Fn(s, "lookingAt", a =>
            {
                Player p = ActorOf(Arg(a, 0)) as Player;
                if (p == null) return JsValue.Null;
                double meters = a.Length > 1 ? Math.Max(1.0, Math.Min(100.0, N(a, 1))) : 60.0;
                if (Physics.Raycast(p.GetEyePoint(), p.GetLookDir(), out RaycastHit hit, (float)meters, GroundMask)) return Vec(e, hit.point);
                return JsValue.Null;
            });

            // ---- reading the world ----
            Fn(s, "nearbyPlayers", a =>
            {
                Vector3 c = Where(s, Arg(a, 0)); double r = Math.Min(200.0, Math.Max(1.0, N(a, 1)));
                return Array(e, Player.GetAllPlayers().Where(p => Vector3.Distance(p.transform.position, c) <= r).OrderBy(p => Vector3.Distance(p.transform.position, c)).Take(50).Select(p => Handles.Actor(s, p)).ToArray());
            });
            Fn(s, "nearbyPieces", a =>
            {
                if (!Players.Rate(s, "nearbyPieces", 2.0, 4.0)) return Refused(s, "nearbyPieces more than twice a second");
                Vector3 c = Where(s, Arg(a, 0)); double r = Math.Min(50.0, Math.Max(1.0, N(a, 1)));
                string prefab = null;
                if (a.Length > 2 && a[2].IsObject() && a[2].AsObject().Get("prefab") is JsValue pv && pv.IsString()) prefab = pv.AsString();
                var found = new List<Piece>();
                Piece.GetAllPiecesInRadius(c, (float)r, found);
                return Array(e, found.Where(p => p != null && (prefab == null || Utils.GetPrefabName(p.gameObject) == prefab)).OrderBy(p => Vector3.Distance(p.transform.position, c)).Take(100).Select(p => Pieces.Handle(s, p)).ToArray());
            });

            // ---- sights, sounds, banners, pings ----
            Fn(s, "effect", a => { if (!Gate(s, "effects")) return JsValue.Null; return Bool(Effects.Play(s, Str(a, 0, 60), Where(s, Arg(a, 1)), a.Length > 2 ? a[2] : JsValue.Undefined, false)); });
            Fn(s, "sound", a => { if (!Gate(s, "effects")) return JsValue.Null; return Bool(Effects.Play(s, Str(a, 0, 60), Where(s, Arg(a, 1)), JsValue.Undefined, true)); });
            Fn(s, "lightning", a => { if (!Gate(s, "effects")) return JsValue.Null; return Bool(Effects.Play(s, "fx_Lightning", Where(s, Arg(a, 0)), JsValue.Undefined, false)); });
            Fn(s, "shake", a => { if (!Gate(s, "effects")) return JsValue.Null; return Bool(Effects.Shake(s, Where(s, Arg(a, 0)), N(a, 1), N(a, 2))); });
            Fn(s, "ping", a => { if (!Gate(s, "messages")) return JsValue.Null; return Bool(Effects.Ping(s, Where(s, Arg(a, 0)), a.Length > 1 && a[1].IsString() ? a[1].AsString() : "")); });
            Fn(s, "banner", a => { if (!Gate(s, "messages")) return JsValue.Null; return Bool(Effects.Banner(s, null, Str(a, 0, 60))); });

            // ---- the world object grows ----
            var world = e.Global.Get("world").AsObject();
            world.Set("setWind", Func(s, "setWind", a => { if (!Gate(s, "weather")) return JsValue.Null; return Bool(WorldExtras.SetWind(s, N(a, 0), N(a, 1), N(a, 2))); }));
            world.Set("startEvent", Func(s, "startEvent", a => { if (!Gate(s, "spawn")) return JsValue.Null; return Bool(WorldExtras.StartEvent(s, Str(a, 0, 40))); }));
            world.Set("stopEvent", Func(s, "stopEvent", a => { if (!Gate(s, "spawn")) return JsValue.Null; return Bool(WorldExtras.StopEvent(s)); }));
            world.Set("spawnPiece", Func(s, "spawnPiece", a => { if (!Gate(s, "world")) return JsValue.Null; return Pieces.Spawn(s, Str(a, 0, 60), Where(s, Arg(a, 1)), a.Length > 2 ? a[2] : JsValue.Undefined); }));
            e.Global.Get("net").AsObject().Set("send", Func(s, "send", a => { if (!Gate(s, "net")) return JsValue.Null; return Bool(WorldExtras.Send(s, Str(a, 0, 40), Str(a, 1, 2000))); }));
        }

        /// <summary>Finds a built piece or creature by the id a script was given.</summary>
        internal static JsValue FindById(Script s, string id)
        {
            Character c = Handles.Find(id);
            if (c != null) return Handles.Actor(s, c);
            Piece p = Pieces.Find(id);
            return p != null ? Pieces.Handle(s, p) : JsValue.Null;
        }
    }
}
