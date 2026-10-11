using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using SoftReferenceableAssets;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Dungeons
{
    /// <summary>
    /// What an entrance looks like in the world (a pack's "entranceLook"): data, one file per look, built into the DLL from
    /// packs/looks/&lt;name&gt;.json, so a new kind of dungeon needs no code for its entrance. Each look is its own saved prefab:
    /// quaddungeon_entrance for "crypt" (the default), quaddungeon_entrance_&lt;name&gt; for the others. The looks are read when the prefabs are
    /// registered, before a world loads (no DungeonDB needed). A look's file:
    ///   {
    ///     "name": "giant",                 the look's name (what packs give as "entranceLook"): lower case letters, digits, _
    ///     "about": "...",                  what it is (for whoever edits it; not read)
    ///     "front": 5,                      where players come out (leaving, or logging back in from inside): metres in front of the door
    ///     "parts": [                       in the entrance's own frame: +z is out, towards whoever comes to it; y 0 the ground
    ///       { "prefab": "blackmarble_arch", "at": [0, 4, -0.5], "euler": [0, 180, 0], "scale": [1, 1, 1] },
    ///                                      one of the game's prefabs (a build piece, a prop, a networked object), made as scenery
    ///                                      (Builder.MakePart: nothing saved, nothing to break or use)
    ///       { "location": "Crypt4", "keep": ["exterior", "Gateway/Cube"], "doorway": [0, 0, 6.96], "at": [0, 0, 0], "euler": [0, 0, 0] }
    ///                                      children of one of the game's locations (its prefab name, as ZoneSystem knows it), copied as
    ///                                      they stand in it, so that its point "doorway" (in the location's frame) is at "at"
    ///     ],
    ///     "inside": [                      the way out, as seen from inside the dungeon: in the way-in doorway's frame, its origin on the
    ///       { "prefab": "...", "at": [0, 0, -0.67], "euler": [0, 180, 0], "scale": [1.238, 1.422, 0.479] }
    ///                                      floor under the doorway and +z into the room (prefabs only). None: the stand-in door, scaled to
    ///                                      the doorway
    ///     ]
    ///   }
    /// Players press E on any of its parts to go in. The entrance carries a stand-in (the forest crypt's door, stripped) until its look is
    /// built on each game, and keeps it when the look can't be: a location part missing, or more than half of its prefab parts. Nothing is
    /// built on a dedicated server. crypt, dvergr and giant are always registered, even without their files, so their entrances already in
    /// worlds never go missing (a host deletes a saved object whose prefab isn't registered).
    /// </summary>
    internal static class EntranceLooks
    {
        internal class Look
        {
            public string Name;
            public string Prefab;            // the saved prefab's name
            public float Front = 2.2f;       // how far in front of the entrance a player comes out
            public readonly List<PartDef> Parts = new List<PartDef>();
            public readonly List<LocationPart> Locations = new List<LocationPart>();
            public readonly List<PartDef> Inside = new List<PartDef>();   // the way out, seen from inside (the doorway's frame)
        }

        /// <summary>Children of one of the game's locations, copied into a look.</summary>
        internal class LocationPart
        {
            public string Location;          // ZoneSystem's location prefab name
            public string[] Keep;            // paths of the children copied (Transform.Find)
            public Vector3 Doorway;          // a point in the location's frame, put at At
            public Vector3 At, Euler;
        }

        public const string Default = "crypt";
        private const string Prefix = "looks/";
        private static readonly string[] Always = { "crypt", "dvergr", "giant" };   // looks with entrances in worlds already

        private static List<Look> _builtin, _all;
        private static readonly List<Look> Extra = new List<Look>();

        /// <summary>The looks built into the DLL: the only ones with a saved prefab of their own (always registered, so no saved entrance
        /// ever loses its prefab).</summary>
        public static List<Look> Builtin => _builtin ?? (_builtin = Load());

        /// <summary>Every look in use: the built-in ones, then those from outside (the config folder, or the server's: SetLooks).</summary>
        public static List<Look> All
        {
            get
            {
                // the set in use changed (the config folder read, the server's arrived): its outside looks taken again
                if (_syncGen != PackSync.Generation) { _syncGen = PackSync.Generation; SetLooks(PackSync.Looks()); }
                return _all ?? (_all = Builtin.Concat(Extra.Where(x => !Builtin.Any(b => b.Name == x.Name))).ToList());
            }
        }

        private static int _syncGen = -1;

        public static bool IsBuiltin(string name) => Builtin.Any(l => l.Name == (name ?? "").Trim().ToLowerInvariant());

        /// <summary>The looks from outside the DLL (Sync: the config folder's, or the server's), lower-case name -> its JSON, replacing the
        /// last ones. Built-in names among them are left out (the DLL's stay). They have no prefab of their own: an entrance with one is the
        /// default prefab with the look's name in its ZDO (Keys.Look).</summary>
        public static void SetLooks(IDictionary<string, JObject> looks)
        {
            Extra.Clear();
            foreach (KeyValuePair<string, JObject> kv in looks ?? new Dictionary<string, JObject>())
            {
                if (kv.Value == null || IsBuiltin(kv.Key)) continue;
                try
                {
                    foreach (string problem in LookCheck.Check(kv.Value, kv.Key)) Plugin.Log.LogWarning("Entrance look " + problem);
                    Look look = Read(kv.Value, kv.Key);
                    if (look != null && !Extra.Any(x => x.Name == look.Name)) Extra.Add(look);
                }
                catch (Exception e) { Plugin.Log.LogWarning($"Entrance look {kv.Key} can't be read: {e.Message}"); }
            }
            _all = null;
        }

        /// <summary>A look by name (case ignored), or the default one.</summary>
        public static Look Get(string name)
        {
            string n = (name ?? "").Trim().ToLowerInvariant();
            return All.FirstOrDefault(l => l.Name == n) ?? All.FirstOrDefault(l => l.Name == Default) ?? All[0];
        }

        public static bool Known(string name) => All.Any(l => l.Name == (name ?? "").Trim().ToLowerInvariant());

        /// <summary>A built-in look's saved prefab: quaddungeon_entrance for the default look (it had that name before looks were data),
        /// else quaddungeon_entrance_&lt;look&gt;.</summary>
        public static string PrefabOf(string look) => look == Default ? Entrance.PrefabName : Entrance.PrefabName + "_" + look;

        // ---- reading them

        private static List<Look> Load()
        {
            var looks = new List<Look>();
            var asm = typeof(EntranceLooks).Assembly;
            foreach (string res in asm.GetManifestResourceNames().Where(r => r.StartsWith(Prefix) && r.EndsWith(".json")).OrderBy(r => r))
            {
                try
                {
                    JObject json;
                    using (Stream s = asm.GetManifestResourceStream(res))
                    using (var reader = new StreamReader(s)) json = JObject.Parse(reader.ReadToEnd());
                    foreach (string problem in LookCheck.Check(json, res)) Plugin.Log.LogWarning("Entrance look " + problem);   // (Layout's checks: warnings only)
                    Look look = Read(json, res, true);
                    if (look == null) continue;
                    if (looks.Any(l => l.Name == look.Name)) { Plugin.Log.LogWarning($"{res}: a look called {look.Name} is already built in: left out"); continue; }
                    looks.Add(look);
                }
                catch (Exception e) { Plugin.Log.LogWarning($"{res} can't be read: {e.Message}"); }
            }
            foreach (string name in Always.Where(n => !looks.Any(l => l.Name == n)))
            {
                Plugin.Log.LogWarning($"The entrance look {name} has no file in this version: its entrances show the stand-in");
                looks.Add(new Look { Name = name, Prefab = PrefabOf(name) });
            }
            return looks;
        }

        private static Look Read(JObject json, string where, bool builtin = false)
        {
            string name = ((string)json["name"] ?? "").Trim().ToLowerInvariant();
            if (name.Length == 0 || name.Any(c => !(c >= 'a' && c <= 'z' || c >= '0' && c <= '9' || c == '_')))
            {
                Plugin.Log.LogWarning($"{where}: the look's \"name\" must be lower case letters, digits and _ (it names a saved prefab): left out");
                return null;
            }
            var look = new Look { Name = name, Prefab = builtin ? PrefabOf(name) : Entrance.PrefabName, Front = (float?)json["front"] ?? 2.2f };
            foreach (JObject p in (json["parts"] as JArray ?? new JArray()).OfType<JObject>())
            {
                string location = (string)p["location"];
                if (!string.IsNullOrEmpty(location))
                {
                    look.Locations.Add(new LocationPart
                    {
                        Location = location,
                        Keep = (p["keep"] as JArray ?? new JArray()).Select(k => (string)k).Where(k => !string.IsNullOrEmpty(k)).ToArray(),
                        Doorway = Vec(p["doorway"], Vector3.zero),
                        At = Vec(p["at"], Vector3.zero),
                        Euler = Vec(p["euler"], Vector3.zero),
                    });
                    continue;
                }
                string prefab = (string)p["prefab"];
                if (string.IsNullOrEmpty(prefab)) { Plugin.Log.LogWarning($"{where}: a part has neither \"prefab\" nor \"location\": left out"); continue; }
                look.Parts.Add(new PartDef { Prefab = prefab, At = Vec(p["at"], Vector3.zero), Euler = Vec(p["euler"], Vector3.zero), Scale = Vec(p["scale"], Vector3.one) });
            }
            foreach (JObject p in (json["inside"] as JArray ?? new JArray()).OfType<JObject>())
            {
                string prefab = (string)p["prefab"];
                if (string.IsNullOrEmpty(prefab)) { Plugin.Log.LogWarning($"{where}: an inside part has no \"prefab\": left out"); continue; }
                look.Inside.Add(new PartDef { Prefab = prefab, At = Vec(p["at"], Vector3.zero), Euler = Vec(p["euler"], Vector3.zero), Scale = Vec(p["scale"], Vector3.one) });
            }
            return look;
        }

        private static Vector3 Vec(JToken t, Vector3 or)
        {
            if (!(t is JArray a)) return or;
            return new Vector3(a.Count > 0 ? (float)a[0] : 0f, a.Count > 1 ? (float)a[1] : 0f, a.Count > 2 ? (float)a[2] : 0f);
        }

        // ---- building it

        /// <summary>The entrance's look, built under it (a coroutine on the plugin). The stand-in is hidden once the look is there.</summary>
        public static IEnumerator Dress(DungeonEntrance entrance, GameObject standIn)
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) yield break;   // (a dedicated server)
            // a look from outside the DLL may come a little later (the server's, after connecting): wait for it; still unknown: the stand-in
            float until = Time.time + 20f;
            while (entrance != null && !Known(entrance.Look) && Time.time < until) yield return null;
            if (entrance == null) yield break;
            if (!Known(entrance.Look))
            {
                Plugin.Log.LogWarning($"Entrance look \"{entrance.Look}\" isn't known here (removed, or another server's): the stand-in shows");
                yield break;
            }
            Look look = Get(entrance.Look);
            if (look.Parts.Count == 0 && look.Locations.Count == 0) yield break;   // (the stand-in is all it has)
            // (active from the start: a part made under an inactive parent would wake later, after MakePart's no-ZDO switch is off)
            var root = new GameObject("quaddungeon_look_" + look.Name);
            root.transform.SetParent(entrance.transform, false);
            bool ok = true;
            foreach (LocationPart location in look.Locations)
            {
                bool kept = false;
                IEnumerator load = FromLocation(look, location, root.transform, done => kept = done);
                while (load.MoveNext()) yield return load.Current;
                ok &= kept;
            }
            int made = 0, tried = 0;
            foreach (PartDef part in look.Parts)
            {
                if (root == null) yield break;   // (the entrance went)
                try { if (Builder.MakePart(part, root.transform) != null) made++; }
                catch (Exception e) { Plugin.Log.LogWarning($"Entrance look {look.Name}, part {part.Prefab}: {e.Message}"); }
                if (++tried % 12 == 0) yield return null;
            }
            if (made * 2 < look.Parts.Count) ok = false;
            if (root == null) yield break;
            if (!ok || entrance == null)
            {
                Plugin.Log.LogWarning($"Entrance look {look.Name} couldn't be built here ({made} of {look.Parts.Count} parts): the stand-in stays");
                Object.Destroy(root);
                yield break;
            }
            if (standIn != null) standIn.SetActive(false);
        }

        /// <summary>A location part: its kept children copied (as they stand in the location, moved so its doorway point is at the part's
        /// place), stripped to meshes and colliders; they hold the location's assets while they exist.</summary>
        private static IEnumerator FromLocation(Look look, LocationPart lp, Transform root, Action<bool> done)
        {
            ZoneSystem.ZoneLocation zl = ZoneSystem.instance?.m_locations?.FirstOrDefault(l => l != null && l.m_prefabName == lp.Location && l.m_prefab.IsValid);
            if (zl == null)
            {
                Plugin.Log.LogWarning($"Entrance look {look.Name}: the game has no location {lp.Location} (a game update?)");
                done(false);
                yield break;
            }
            zl.m_prefab.LoadAsync();
            float until = Time.realtimeSinceStartup + 30f;
            while (zl.m_prefab.IsLoading && Time.realtimeSinceStartup < until) yield return null;
            int kept = 0;
            try
            {
                GameObject asset = zl.m_prefab.IsLoaded ? zl.m_prefab.Asset : null;
                if (asset != null && root != null)
                {
                    Transform from = asset.transform;
                    Quaternion unturn = Quaternion.Inverse(from.rotation), turn = Quaternion.Euler(lp.Euler);
                    foreach (string path in lp.Keep)
                    {
                        Transform part = from.Find(path);
                        if (part == null) { Plugin.Log.LogWarning($"Entrance look {look.Name}: {lp.Location} has no {path}"); continue; }
                        // nothing in it may become a saved object (there's nothing networked in these, but a game update could add something)
                        bool was = ZNetView.m_forceDisableInit;
                        GameObject copy;
                        try
                        {
                            ZNetView.m_forceDisableInit = true;
                            copy = Object.Instantiate(part.gameObject, root, false);
                        }
                        finally { ZNetView.m_forceDisableInit = was; }
                        copy.name = part.name;
                        copy.transform.localPosition = lp.At + turn * (unturn * (part.position - from.position) - lp.Doorway);
                        copy.transform.localRotation = turn * unturn * part.rotation;
                        copy.transform.localScale = part.lossyScale;
                        Builder.Strip(copy);
                        copy.HoldReferenceTo(zl.m_prefab);
                        kept++;
                    }
                }
            }
            catch (Exception e) { Plugin.Log.LogWarning($"Entrance look {look.Name} from {lp.Location}: {e.Message}"); }
            finally { zl.m_prefab.Release(); }
            done(kept > 0 && kept == lp.Keep.Length);
        }
    }
}
