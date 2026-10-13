using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Jint;
using Jint.Native;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Scripts
{
    /// <summary>
    /// A script's small memory, kept with the world (docs/scripting-api.md section 6): one file per world next to the scripts, a section
    /// per script, "scope" then "key" then the value as JSON text. Limits: 64 KB a script, 256 keys a scope, 4 KB a value. A script never
    /// sees another's section, and a handle can't be stored (an id can).
    /// </summary>
    internal static class Store
    {
        private const int MaxBytes = 64 * 1024, MaxKeys = 256, MaxValue = 4096;
        private static JObject _all;
        private static string _loadedFor;
        private static float _dirtyAt;

        private static string PathFor()
        {
            string world = ZNet.World != null ? ZNet.World.m_name : "world";
            foreach (char c in Path.GetInvalidFileNameChars()) world = world.Replace(c, '_');
            return Path.Combine(Host.Folder, "store-" + world + ".json");
        }

        private static JObject Root()
        {
            string path = PathFor();
            if (_all == null || _loadedFor != path)
            {
                Flush();
                _loadedFor = path;
                try { _all = File.Exists(path) ? JObject.Parse(File.ReadAllText(path)) : new JObject(); }
                catch (Exception) { _all = new JObject(); }
            }
            return _all;
        }

        private static JObject Section(Script s, string scope, bool create)
        {
            var script = (JObject)Root()[s.File] ?? (create ? (JObject)(Root()[s.File] = new JObject()) : null);
            if (script == null) return null;
            return (JObject)script[scope] ?? (create ? (JObject)(script[scope] = new JObject()) : null);
        }

        internal static JsValue Get(Script s, string scope, string key, JsValue fallback)
        {
            JObject section = Section(s, scope, false);
            if (section == null || section[key] == null) return fallback;
            try { return s.Engine.Invoke(s.Json.Get("parse"), s.Json, new JsValue[] { new JsString(section[key].ToString()) }); }
            catch (Exception) { return fallback; }
        }

        internal static bool Put(Script s, string scope, string key, JsValue value)
        {
            Api.Need(s, "store");
            if (value.IsObject() && Holds(value)) throw new ScriptError("A handle can't be stored: keep its id and look it up again with findById.");
            if (value.IsUndefined()) { Remove(s, scope, key); return true; }
            string text = s.Engine.Invoke(s.Json.Get("stringify"), s.Json, new[] { value }).ToString();
            if (text == "undefined" || text.Length > MaxValue) return false;
            JObject section = Section(s, scope, true);
            if (section[key] == null && section.Count >= MaxKeys) return false;
            if (Size(s) - (section[key]?.ToString().Length ?? 0) + text.Length > MaxBytes) return false;
            section[key] = text;
            _dirtyAt = UnityEngine.Time.unscaledTime + 3f;
            return true;
        }

        internal static void Remove(Script s, string scope, string key)
        {
            Api.Need(s, "store");
            JObject section = Section(s, scope, false);
            if (section != null && section.Remove(key)) _dirtyAt = UnityEngine.Time.unscaledTime + 3f;
        }

        internal static IEnumerable<string> Keys(Script s, string scope)
        {
            JObject section = Section(s, scope, false);
            return section == null ? Enumerable.Empty<string>() : section.Properties().Select(p => p.Name).ToList();
        }

        private static int Size(Script s)
        {
            var script = Root()[s.File] as JObject;
            return script == null ? 0 : script.ToString(Formatting.None).Length;
        }

        /// <summary>A handle (our small objects with id, prefab and valid) anywhere in the first two levels.</summary>
        private static bool Holds(JsValue v)
        {
            var o = v.AsObject();
            if (o.HasOwnProperty("valid") && o.HasOwnProperty("prefab")) return true;
            foreach (var key in o.GetOwnPropertyKeys())
            {
                JsValue inner = o.Get(key);
                if (inner.IsObject() && inner.AsObject().HasOwnProperty("valid") && inner.AsObject().HasOwnProperty("prefab")) return true;
            }
            return false;
        }

        /// <summary>Writes the store a few seconds after the last change, and when the world closes.</summary>
        internal static void Tick()
        {
            if (_dirtyAt > 0f && UnityEngine.Time.unscaledTime >= _dirtyAt) Flush();
        }

        internal static void Flush()
        {
            _dirtyAt = 0f;
            if (_all == null || _loadedFor == null) return;
            try { File.WriteAllText(_loadedFor, _all.ToString(Formatting.Indented)); }
            catch (Exception e) { Core.Log.LogWarning("Could not save the scripts' memory: " + e.Message); }
        }
    }
}
