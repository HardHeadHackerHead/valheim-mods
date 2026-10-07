using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace AICompanion
{
    /// <summary>
    /// How a companion looks, chosen like a player's character: body, hair and beard styles (the game's own, from the character creator's
    /// list), skin tone, and hair colour and shade (the creator's two sliders). Stored in its ZDO as the game stores a player's looks (so
    /// every player sees the same), plus the slider positions, and kept in its Profile so it wakes looking the same.
    ///
    /// The game gives a player-like NPC with no hair or beard a random one when it is made; "none" is therefore saved as "no hair" /
    /// "no beard" too, or a bald companion would wake with hair.
    /// </summary>
    internal static class Looks
    {
        private const string SkinT = "dhc_skin_t", HairT = "dhc_hair_t", HairL = "dhc_hair_l";

        // The creator's ranges (PlayerCustomizaton): skin from fair to dark, hair from fair to dark brown, then a shade multiplier.
        private static readonly Color Skin0 = new Color(1f, 1f, 1f), Skin1 = new Color(0.32f, 0.23f, 0.17f);
        private static readonly Color Hair0 = new Color(1f, 0.86f, 0.55f), Hair1 = new Color(0.45f, 0.24f, 0.11f);
        private const float HairMin = 0.1f, HairMax = 1f;

        public static List<ItemDrop> Hairs() => Styles("Hair");
        public static List<ItemDrop> Beards() => Styles("Beard");

        private static List<ItemDrop> Styles(string prefix)
        {
            if (ObjectDB.instance == null) return new List<ItemDrop>();
            return ObjectDB.instance.GetAllItems(ItemDrop.ItemData.ItemType.Customization, prefix)
                .Where(i => i != null && !i.name.EndsWith("None"))
                .OrderBy(i => Number(i.name)).ThenBy(i => i.name).ToList();
        }

        private static int Number(string name) { string d = new string(name.Where(char.IsDigit).ToArray()); return int.TryParse(d, out int n) ? n : 999; }

        public static int Model(Component c) => Companion.Zdo(c)?.GetInt(ZDOVars.s_modelIndex, 0) ?? 0;
        public static int Hair(Component c) => Companion.Zdo(c)?.GetInt(ZDOVars.s_hairItem, 0) ?? 0;
        public static int Beard(Component c) => Companion.Zdo(c)?.GetInt(ZDOVars.s_beardItem, 0) ?? 0;
        public static float SkinTone(Component c) => Companion.Zdo(c)?.GetFloat(SkinT, 0.3f) ?? 0.3f;
        public static float HairTone(Component c) => Companion.Zdo(c)?.GetFloat(HairT, 0.5f) ?? 0.5f;
        public static float HairShade(Component c) => Companion.Zdo(c)?.GetFloat(HairL, 0.8f) ?? 0.8f;

        public static string StyleName(int hash, List<ItemDrop> styles)
        {
            if (hash == 0) return "none";
            int i = styles.FindIndex(s => s.name.GetStableHashCode() == hash);
            return i < 0 ? "?" : $"{i + 1} of {styles.Count}";
        }

        // ---- changing (on the game that owns it: callers go through Companion.Write) ---------------------------

        private static VisEquipment Vis(Humanoid c) => c.GetComponent<VisEquipment>();

        /// <summary>
        /// A different body: swapping the body mesh on a character already drawn breaks it (Unity stops rendering it: "mesh data size and
        /// vertex stride"), so the new body is written to its ZDO and the companion is made again from it at once, as the game does when it
        /// comes back into range. A body set as it is made (summoning, waking) is fine.
        /// </summary>
        public static void SetModel(Humanoid c, int model)
        {
            ZDO z = Companion.Zdo(c);
            if (z == null || z.GetInt(ZDOVars.s_modelIndex, 0) == model) return;
            z.Set(ZDOVars.s_modelIndex, model);
            if (model == 1) { z.Set(ZDOVars.s_beardItem, 0); z.Set(ZDOVars.s_noBeard, true); } // the second body has no beard
            // (the companion is then made again on every game that sees the new body: VisEquipment_UpdateBaseModel)
        }

        private static readonly HarmonyLib.AccessTools.FieldRef<ZNetScene, Dictionary<ZDO, ZNetView>> Instances =
            HarmonyLib.AccessTools.FieldRefAccess<ZNetScene, Dictionary<ZDO, ZNetView>>("m_instances");

        /// <summary>Let the game make this companion again from its ZDO next frame (its bag, looks and settings are all in the ZDO).</summary>
        public static void Remake(Humanoid c)
        {
            ZNetView view = c.GetComponent<ZNetView>();
            ZDO zdo = view?.GetZDO();
            if (zdo == null || ZNetScene.instance == null) return;
            Brain.Forget(c);
            view.ResetZDO();
            Instances(ZNetScene.instance).Remove(zdo);
            UnityEngine.Object.Destroy(c.gameObject);
            Plugin.Instance?.Note("A companion was remade with a different body");
        }

        /// <summary>For a new companion, while it is being made (before it is ever drawn): set the body directly.</summary>
        public static void SetModelNow(Humanoid c, int model)
        {
            Vis(c)?.SetModel(model);
            if (model == 1) SetBeard(c, 0);
        }

        public static void SetHair(Humanoid c, int hash)
        {
            ZDO z = Companion.Zdo(c);
            Vis(c)?.SetHairItem(hash);
            z.Set(ZDOVars.s_hairItem, hash);
            z.Set(ZDOVars.s_noHair, hash == 0);
            Portraits.Dirty(c);
        }

        public static void SetBeard(Humanoid c, int hash)
        {
            ZDO z = Companion.Zdo(c);
            Vis(c)?.SetBeardItem(hash);
            z.Set(ZDOVars.s_beardItem, hash);
            z.Set(ZDOVars.s_noBeard, hash == 0);
            Portraits.Dirty(c);
        }

        public static void SetSkin(Humanoid c, float t)
        {
            Companion.Zdo(c).Set(SkinT, t);
            Vis(c)?.SetSkinColor(Utils.ColorToVec3(Color.Lerp(Skin0, Skin1, t)));
            Portraits.Dirty(c);
        }

        public static void SetHairColor(Humanoid c, float tone, float shade)
        {
            ZDO z = Companion.Zdo(c);
            z.Set(HairT, tone);
            z.Set(HairL, shade);
            Vis(c)?.SetHairColor(Utils.ColorToVec3(Color.Lerp(Hair0, Hair1, tone) * Mathf.Lerp(HairMin, HairMax, shade)));
            Portraits.Dirty(c);
        }

        /// <summary>The next or previous style in the list ("none" sits at the start).</summary>
        public static int Step(int current, List<ItemDrop> styles, int by)
        {
            var hashes = new List<int> { 0 };
            hashes.AddRange(styles.Select(s => s.name.GetStableHashCode()));
            int i = Mathf.Max(0, hashes.IndexOf(current));
            return hashes[(i + by + hashes.Count) % hashes.Count];
        }

        /// <summary>A random look. <paramref name="fresh"/>: it is being made right now (summoning), so the body can be set directly.</summary>
        public static void Randomise(Humanoid c) => Randomise(c, false);

        public static void Randomise(Humanoid c, bool fresh)
        {
            List<ItemDrop> hairs = Hairs(), beards = Beards();
            int model = UnityEngine.Random.Range(0, 2);
            if (fresh) SetModelNow(c, model);
            SetSkin(c, UnityEngine.Random.Range(0f, 0.9f));
            SetHairColor(c, UnityEngine.Random.value, UnityEngine.Random.Range(0.35f, 1f));
            if (hairs.Count > 0) SetHair(c, hairs[UnityEngine.Random.Range(0, hairs.Count)].name.GetStableHashCode());
            if (model == 0 && beards.Count > 0) SetBeard(c, UnityEngine.Random.value < 0.85f ? beards[UnityEngine.Random.Range(0, beards.Count)].name.GetStableHashCode() : 0);
            if (!fresh) SetModel(c, model); // last: a different body makes it again
        }

        /// <summary>Copy a player's looks (body, colours, hair and beard) from what the game keeps for them.</summary>
        public static void CopyFrom(Humanoid c, Player p)
        {
            ZDO pz = p.GetComponent<ZNetView>()?.GetZDO();
            if (pz == null) return;
            VisEquipment vis = Vis(c);
            if (vis == null) return;
            vis.SetSkinColor(pz.GetVec3(ZDOVars.s_skinColor, Vector3.one));
            vis.SetHairColor(pz.GetVec3(ZDOVars.s_hairColor, Vector3.one));
            SetHair(c, pz.GetInt(ZDOVars.s_hairItem, 0));
            SetBeard(c, pz.GetInt(ZDOVars.s_beardItem, 0));
            Portraits.Dirty(c);
            SetModel(c, pz.GetInt(ZDOVars.s_modelIndex, 0)); // last: a different body makes it again
        }
    }

    /// <summary>
    /// A small picture of each companion's face, for its menu and the party panel (PartyHud reads it through AppDomain data
    /// "DHack.CompanionPortrait", a function from the companion's id to a texture). A hidden camera with its own light looks at the face of
    /// companions loaded here, a couple of times a second, seeing only the companion's own layers. Companions not loaded here have no picture
    /// (the panel shows their initial), unless one was taken earlier this session.
    /// </summary>
    internal static class Portraits
    {
        private class Shot { public RenderTexture Tex; public float At = -99f; public string Sig = ""; public float ChangedAt = -99f; }
        private static readonly Dictionary<long, Shot> Shots = new Dictionary<long, Shot>();
        private static Camera _cam;
        private static Light _light;
        private static int _next;

        public static void Start() => AppDomain.CurrentDomain.SetData("DHack.CompanionPortrait", (Func<long, Texture>)Get);

        public static Texture Get(long id) => Shots.TryGetValue(id, out Shot s) && s.Tex != null && s.At > 0f ? s.Tex : null;

        public static void Dirty(Component c) { if (Shots.TryGetValue(Companion.IdOf(c), out Shot s)) s.Sig = ""; }

        private static string Signature(Humanoid c)
        {
            ZDO z = Companion.Zdo(c);
            return string.Join("|", z.GetInt(ZDOVars.s_modelIndex, 0), z.GetInt(ZDOVars.s_hairItem, 0), z.GetInt(ZDOVars.s_beardItem, 0), z.GetVec3(ZDOVars.s_skinColor, Vector3.one),
                z.GetVec3(ZDOVars.s_hairColor, Vector3.one), z.GetInt(ZDOVars.s_helmetItem, 0), z.GetInt(ZDOVars.s_chestItem, 0), z.GetInt(ZDOVars.s_shoulderItem, 0),
                z.GetInt(ZDOVars.s_rightItem, 0), z.GetInt(ZDOVars.s_leftItem, 0));
        }

        /// <summary>One companion per frame at most, each about twice a second (sooner when its looks just changed).</summary>
        public static void Tick()
        {
            // Only companions near you: far away the game stops drawing a character in full, and the picture comes out empty. The last good
            // picture stays until it is near again.
            Player me = Player.m_localPlayer;
            List<Humanoid> all = Companion.All().Where(c => !c.IsDead() && me != null && Vector3.Distance(c.transform.position, me.transform.position) < 40f).ToList();
            if (all.Count == 0) return;
            _next = (_next + 1) % all.Count;
            Humanoid c = all[_next];
            long id = Companion.IdOf(c);
            if (!Shots.TryGetValue(id, out Shot shot)) Shots[id] = shot = new Shot();
            // A new picture only when its looks or what it wears change (then a moment later, once the new gear shows), not every moment.
            string sig = Signature(c);
            if (sig != shot.Sig) { shot.Sig = sig; shot.ChangedAt = Time.time; return; }
            if (shot.ChangedAt < 0f || Time.time - shot.ChangedAt < 0.6f) return;
            shot.ChangedAt = -99f;
            try { Render(c, shot); }
            catch (Exception e) { Plugin.Instance?.Warn("Could not take a companion's portrait: " + e.Message); shot.At = Time.time + 30f; }
        }

        private static void Render(Humanoid c, Shot shot)
        {
            if (_cam == null)
            {
                var go = new GameObject("dhc_portrait_camera") { hideFlags = HideFlags.HideAndDontSave };
                _cam = go.AddComponent<Camera>();
                _cam.enabled = false;
                _cam.clearFlags = CameraClearFlags.SolidColor;
                _cam.backgroundColor = new Color(0.16f, 0.2f, 0.19f, 1f);
                _cam.fieldOfView = 24f;
                _cam.nearClipPlane = 0.05f;
                _cam.farClipPlane = 3f;
                _light = go.AddComponent<Light>();
                _light.type = LightType.Point;
                _light.range = 3f;
                _light.intensity = 1.6f;
                _light.enabled = false;
            }
            if (shot.Tex == null) { shot.Tex = new RenderTexture(128, 128, 16) { name = "dhc_portrait", antiAliasing = 2 }; shot.Tex.Create(); }

            int mask = 0;
            foreach (Renderer r in c.GetComponentsInChildren<Renderer>()) mask |= 1 << r.gameObject.layer;
            Vector3 head = c.GetHeadPoint();
            Vector3 forward = c.transform.forward; forward.y = 0f;
            if (forward.sqrMagnitude < 0.01f) forward = Vector3.forward;
            forward.Normalize();
            _cam.transform.position = head + forward * 0.8f + Vector3.up * 0.04f;
            _cam.transform.LookAt(head + Vector3.up * 0.02f);
            _cam.cullingMask = mask;
            _cam.targetTexture = shot.Tex;
            _light.cullingMask = mask;
            _light.enabled = true;
            _cam.Render();
            _light.enabled = false;
            _cam.targetTexture = null;
            shot.At = Time.time;
        }

        public static void Stop()
        {
            AppDomain.CurrentDomain.SetData("DHack.CompanionPortrait", null);
            foreach (Shot s in Shots.Values) if (s.Tex != null) { s.Tex.Release(); UnityEngine.Object.Destroy(s.Tex); }
            Shots.Clear();
            if (_cam != null) UnityEngine.Object.Destroy(_cam.gameObject);
            _cam = null;
            _light = null;
        }
    }
}

namespace AICompanion
{
    // Any game about to swap a companion's body mesh after it has been drawn (its body was changed, here or on another player's game) makes it
    // again from its ZDO instead: swapping the mesh of a drawn character stops Unity rendering it. The first body (as it is made) is set as usual.
    [HarmonyLib.HarmonyPatch(typeof(VisEquipment), "UpdateBaseModel")]
    internal static class VisEquipment_UpdateBaseModel
    {
        private static readonly HarmonyLib.AccessTools.FieldRef<VisEquipment, int> Current = HarmonyLib.AccessTools.FieldRefAccess<VisEquipment, int>("m_currentModelIndex");
        private static readonly System.Collections.Generic.HashSet<VisEquipment> Drawn = new System.Collections.Generic.HashSet<VisEquipment>();

        private static bool Prefix(VisEquipment __instance)
        {
            if (!Companion.Is(__instance)) return true;
            int wanted = Companion.Zdo(__instance).GetInt(ZDOVars.s_modelIndex, 0);
            if (Drawn.Add(__instance)) { if (Drawn.Count > 64) Drawn.RemoveWhere(v => v == null); return true; } // its first body
            if (wanted == Current(__instance)) return true;
            Looks.Remake(__instance.GetComponent<Humanoid>());
            return false;
        }
    }
}
