using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Arena
{
    /// <summary>
    /// The Arena itself, put up from its layout whenever you come near it (and taken down when you go far away). Every part is a real game
    /// piece built as plain scenery: it looks as it does in the game, creatures and players walk on it and bump into it, but it is not saved,
    /// cannot be broken or taken down, and is the same for everyone. Fires and torches burn, the grates in the podium rise to let fighters in,
    /// and the forecourt has the Hall of Fame (a stone statue for each champion, with their name), the Arena Master at his desk, and the
    /// waystone you arrive by.
    /// </summary>
    internal static class Scenery
    {
        internal const float BuildWithin = 260f, DropBeyond = 330f;

        private sealed class Grate { public Transform T; public Vector3 Closed; public float Open, Until; public bool Main; public Collider[] Solid; }

        private static GameObject _root, _props, _hall;
        private static readonly List<Grate> Grates = new List<Grate>();
        private static readonly List<Material> Owned = new List<Material>();
        private static Animator _mainGate;
        private static bool _building;
        private static string _propSet;
        private static string _hallShown;
        private static float _nextHall;
        private static TMP_FontAsset _font;
        private static GameObject _master;
        internal static GameObject MasterFigure => _master;

        internal static bool Built => _root != null && !_building;
        internal static Transform Root => _root != null ? _root.transform : null;
        internal static string PropSet => _propSet;

        internal static void Tick(float dt)
        {
            Player p = Player.m_localPlayer;
            if (!Site.Known || p == null || !Layout.Loaded) { if (_root != null) Drop(); return; }
            float away = new Vector2(p.transform.position.x - Site.Origin.x, p.transform.position.z - Site.Origin.z).magnitude;
            if (_root == null && !_building && away < BuildWithin) Plugin.Instance.StartCoroutine(Build());
            else if (_root != null && !_building && away > DropBeyond) Drop();
            if (_root == null || _building) return;

            foreach (Grate g in Grates)
            {
                if (g.T == null) continue;
                float want = Time.time < g.Until ? 1f : 0f;
                g.Open = Mathf.MoveTowards(g.Open, want, dt * (want > g.Open ? 1.4f : 0.8f));
                g.T.localPosition = g.Closed + Vector3.up * (Mathf.SmoothStep(0f, 1f, g.Open) * 2.95f);
                if (g.Main) { bool shut = g.Open < 0.5f; foreach (Collider c in g.Solid) if (c != null && c.enabled != shut) c.enabled = shut; }
            }
            if (Time.time > _nextHall) { _nextHall = Time.time + 5f; UpdateHall(); }
        }

        // ---- putting it up ------------------------------------------------------------------------------------------------

        private static IEnumerator Build()
        {
            _building = true;
            Drop();
            _building = true;
            var root = new GameObject("TheArena");
            root.transform.SetPositionAndRotation(Site.Origin, Site.Turn);
            _root = root;
            var missing = new HashSet<string>();
            var made = new List<GameObject>(Layout.Parts.Count);
            int n = 0;
            float started = Time.realtimeSinceStartup;
            foreach (Layout.Part part in Layout.Parts)
            {
                if (root == null) { _building = false; yield break; }
                made.Add(Make(part, root.transform, missing));
                if (++n % 60 == 0) yield return null;
            }
            // the grates in the podium, and the main gate standing open
            Grates.Clear();
            foreach (Layout.Mark gate in Layout.Gates)
            {
                GameObject go = gate.Piece >= 0 && gate.Piece < made.Count ? made[gate.Piece] : null;
                var grate = new Grate { T = go != null ? go.transform : null, Closed = go != null ? go.transform.localPosition : Vector3.zero, Main = gate.Main,
                                        Solid = go != null ? go.GetComponentsInChildren<Collider>(true) : new Collider[0] };
                Grates.Add(grate);
            }
            GameObject main = Layout.MainGate.Piece >= 0 && Layout.MainGate.Piece < made.Count ? made[Layout.MainGate.Piece] : null;
            _mainGate = main != null ? main.GetComponentInChildren<Animator>(true) : null;
            if (_mainGate != null) { _mainGate.enabled = true; _mainGate.SetInteger("state", 1); }

            BuildForecourt(root.transform);
            _hallShown = null;
            UpdateHall();
            if (missing.Count > 0) Plugin.Log.LogWarning("The arena has pieces this game does not: " + string.Join(", ", missing));
            Plugin.Log.LogInfo($"The Arena put up: {made.Count(m => m != null)} pieces in {Time.realtimeSinceStartup - started:0.0} s");
            _building = false;
            if (_propSet != null) ShowProps(_propSet);
        }

        /// <summary>A piece as plain scenery: made without joining the network, then everything that would make it a building piece is taken off.</summary>
        private static GameObject Make(Layout.Part part, Transform parent, HashSet<string> missing)
        {
            GameObject prefab = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(part.Prefab) : null;
            if (prefab == null) { missing.Add(part.Prefab); return null; }
            GameObject go;
            ZNetView.m_forceDisableInit = true;
            TerrainOp.m_forceDisableTerrainOps = true;
            try { go = Object.Instantiate(prefab, parent.TransformPoint(part.Pos), parent.rotation * part.Rot, parent); }
            finally { ZNetView.m_forceDisableInit = false; TerrainOp.m_forceDisableTerrainOps = false; }
            go.name = part.Prefab;
            Strip(go, part.Prefab.Contains("stake"));
            return go;
        }

        internal static void Strip(GameObject go, bool keepHarm = false)
        {
            // fire that burns (braziers, torches): scenery should not set you alight. Only the stakes in the ring keep their bite.
            if (!keepHarm) foreach (Aoe harm in go.GetComponentsInChildren<Aoe>(true)) Object.DestroyImmediate(harm);
            // fires: burning, with nothing to feed
            foreach (Fireplace f in go.GetComponentsInChildren<Fireplace>(true))
            {
                if (f.m_enabledObject != null) f.m_enabledObject.SetActive(true);
                if (f.m_enabledObjectHigh != null) f.m_enabledObjectHigh.SetActive(true);
                if (f.m_enabledObjectLow != null) f.m_enabledObjectLow.SetActive(false);
                if (f.m_fullObject != null) f.m_fullObject.SetActive(true);
                if (f.m_halfObject != null) f.m_halfObject.SetActive(false);
                if (f.m_emptyObject != null) f.m_emptyObject.SetActive(false);
                Object.Destroy(f);
            }
            foreach (Rigidbody b in go.GetComponentsInChildren<Rigidbody>(true)) Object.Destroy(b);
            foreach (Joint j in go.GetComponentsInChildren<Joint>(true)) Object.Destroy(j);
            foreach (TerrainModifier t in go.GetComponentsInChildren<TerrainModifier>(true)) Object.Destroy(t);
            foreach (MonoBehaviour mb in go.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (mb == null) continue;
                bool game = mb is Piece || mb is WearNTear || mb is ZSyncTransform || mb is ZSyncAnimation || mb is Door || mb is Container
                            || mb is Interactable || mb is Hoverable || mb is StaticPhysics;
                if (game && !(mb is Stand)) Object.Destroy(mb);
            }
            foreach (ZNetView v in go.GetComponentsInChildren<ZNetView>(true)) Object.Destroy(v);
        }

        internal static void Drop()
        {
            if (_root != null) Object.Destroy(_root);
            _root = null; _props = null; _hall = null; _mainGate = null;
            Grates.Clear();
            foreach (Material m in Owned) if (m != null) Object.Destroy(m);
            Owned.Clear();
            _building = false;
        }

        // ---- the grates ---------------------------------------------------------------------------------------------------------

        /// <summary>Raises a grate in the podium for a while (the main grate is where fighters walk in and out).</summary>
        internal static void OpenGate(int index, float seconds)
        {
            if (index < 0 || index >= Grates.Count) return;
            Grates[index].Until = Mathf.Max(Grates[index].Until, Time.time + seconds);
        }

        internal static void CloseGate(int index)
        {
            if (index >= 0 && index < Grates.Count) Grates[index].Until = 0f;
        }

        internal static int MainGrate => Layout.Gates.FindIndex(g => g.Main);
        internal static bool GateOpen(int index) => index >= 0 && index < Grates.Count && Grates[index].Open > 0.7f;

        // ---- cover on the floor --------------------------------------------------------------------------------------------------

        internal static void ShowProps(string set)
        {
            ClearProps();
            _propSet = set;
            if (_root == null || set == null || !Layout.Props.TryGetValue(set, out List<Layout.Part> parts)) return;
            _props = new GameObject("Cover");
            _props.transform.SetParent(_root.transform, false);
            var missing = new HashSet<string>();
            foreach (Layout.Part part in parts)
            {
                // cover follows the floor's mounds
                var lifted = new Layout.Part { Prefab = part.Prefab, Rot = part.Rot, Pos = part.Pos + Vector3.up * Layout.FloorHeight(part.Pos.x, part.Pos.z) };
                Make(lifted, _props.transform, missing);
            }
        }

        internal static void ClearProps()
        {
            if (_props != null) Object.Destroy(_props);
            _props = null;
            _propSet = null;
        }

        // ---- the forecourt: the waystone, the Arena Master, the Hall of Fame -------------------------------------------------------

        private static Material Mat(Color c, float metal = 0f)
        {
            Material m = Things.Plain(ZNetScene.instance, c, metal);
            Owned.Add(m);
            return m;
        }

        private static void BuildForecourt(Transform root)
        {
            // the waystone you arrive by (and leave by)
            if (Things.StandObject != null)
            {
                ZNetView.m_forceDisableInit = true;
                GameObject stone;
                try { stone = Object.Instantiate(Things.StandObject, root.TransformPoint(Layout.Arrival.Pos), root.rotation * Quaternion.Euler(0f, 180f, 0f), root); }
                finally { ZNetView.m_forceDisableInit = false; }
                stone.SetActive(true);
                Strip(stone);
                Stand s = stone.GetComponent<Stand>() ?? stone.AddComponent<Stand>();
                s.Arrival = true;
            }

            // the Arena Master behind his desk: a viking of the game's own body, dressed for the part
            GameObject master = Figures.Make(root, root.TransformPoint(Layout.MasterSpot.Pos), root.rotation * Quaternion.Euler(0f, 180f, 0f), Figures.Master(), true);
            if (master != null)
            {
                master.name = "ArenaMaster";
                int layer = LayerMask.NameToLayer("piece");
                foreach (Collider c in master.GetComponentsInChildren<Collider>(true)) { c.gameObject.layer = layer >= 0 ? layer : 0; c.isTrigger = false; }
                master.AddComponent<Master>();
                _master = master;
            }

            // the gods watch over the Hall of Fame: the game's own statues of Thor and Freya
            foreach (var god in new[] { ("StatueThor", Layout.Thor), ("StatueFreya", Layout.Freya) })
            {
                if (god.Item2 == null) continue;
                GameObject prefab = ZNetScene.instance.GetPrefab(god.Item1);
                if (prefab == null) continue;
                ZNetView.m_forceDisableInit = true;
                GameObject statue;
                try { statue = Object.Instantiate(prefab, root.TransformPoint(god.Item2.Pos), root.rotation * Quaternion.Euler(0f, god.Item2.Yaw, 0f), root); }
                finally { ZNetView.m_forceDisableInit = false; }
                Strip(statue);
                statue.transform.localScale = Vector3.one * 2.4f;   // (the game's statues are small: these are a god's height)
            }

            _hall = new GameObject("HallOfFame");
            _hall.transform.SetParent(root, false);
        }

        private static Transform Part(Transform parent, PrimitiveType shape, Vector3 scale, Vector3 at, Material m)
        {
            GameObject go = GameObject.CreatePrimitive(shape);
            Object.Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = at;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = m;
            return go.transform;
        }

        /// <summary>The Hall of Fame: a plinth for each contest, with a stone statue of its champion, their name and run.</summary>
        private static void UpdateHall()
        {
            if (_hall == null) return;
            List<Ladder.Entry> hall = Ladder.Hall();
            string key = string.Join(";", hall.Select(e => e.Kind + e.Name + e.Score + e.Look));
            if (key == _hallShown) return;
            _hallShown = key;
            foreach (Transform child in _hall.transform) Object.Destroy(child.gameObject);
            Material stone = Figures.Stone();
            for (int i = 0; i < Layout.Hall.Count; i++)
            {
                Layout.Mark m = Layout.Hall[i];
                var spot = new GameObject("Plinth" + (i + 1)).transform;
                spot.SetParent(_hall.transform, false);
                spot.localPosition = m.Pos;
                spot.localRotation = Quaternion.Euler(0f, m.Yaw, 0f);
                // the name plate on the plinth's face toward the court (the plinth's top is m.Pos; its face 0.95 m out from its middle)
                float face = 0.97f, down = -m.Pos.y * 0.5f;
                string kind = i < Ladder.Kinds.Length ? Ladder.Kinds[i] : "";
                if (Ladder.Champion(kind, out Ladder.Entry champ))
                {
                    // the champion as they fought: their body, hair, beard and gear (an entry from before looks were kept: you as you are now,
                    // if it is yours; else a champion made up from the name)
                    Player me = Player.m_localPlayer;
                    Figures.Look look = Figures.FromLook(champ.Look)
                                        ?? (me != null && me.GetPlayerName() == champ.Name ? Figures.FromLook(Figures.LookOf(me)) : null)
                                        ?? Figures.Champion(champ.Name.GetHashCode());
                    GameObject statue = Figures.Make(spot, spot.position, spot.rotation, look, false);
                    if (statue != null)
                    {
                        statue.transform.localScale = Vector3.one * 1.25f;
                        // a pose with both feet on the ground (some emotes jump), set down on the plinth's top
                        if (stone != null) Figures.Petrify(statue, stone, new[] { "flex", "point", "thumbsup", "challenge" }[(Mathf.Abs(champ.Name.GetHashCode()) + i) % 4], spot.position.y);
                    }
                    Text(spot, $"<b>{champ.Name}</b>\n<size=55%>{champ.What}  ·  {champ.Score}</size>", new Vector3(0f, down + 0.35f, face), new Color(1f, 0.82f, 0.35f), 1.6f);
                }
                else Text(spot, "<i>Unclaimed</i>", new Vector3(0f, down + 0.35f, face), new Color(0.62f, 0.6f, 0.57f), 1.6f);
                // which contest's champion stands here
                Text(spot, "<smallcaps>Champion of</smallcaps>\n" + (i < Ladder.KindTitles.Length ? Ladder.KindTitles[i] : ""), new Vector3(0f, down - 0.45f, face), new Color(0.9f, 0.72f, 0.3f), 1.5f);
            }
        }

        private static void Text(Transform parent, string text, Vector3 at, Color color, float width)
        {
            var go = new GameObject("Name");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = at;
            go.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);   // read from the court
            var tmp = go.AddComponent<TextMeshPro>();
            TMP_FontAsset font = Font();
            if (font != null) tmp.font = font;
            tmp.text = text;
            tmp.enableAutoSizing = true;
            tmp.fontSizeMin = 0.5f; tmp.fontSizeMax = 2.4f;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = color;
            tmp.enableWordWrapping = false;
            tmp.overflowMode = TextOverflowModes.Overflow;
            tmp.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            tmp.rectTransform.sizeDelta = new Vector2(width, 0.5f);
        }

        private static TMP_FontAsset Font()
        {
            if (_font != null) return _font;
            if (Ui.Ready() && Ui.Norse != null) { _font = Ui.Norse; return _font; }
            GameObject sign = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab("sign") : null;
            TMP_Text t = sign != null ? sign.GetComponentInChildren<TMP_Text>(true) : null;
            _font = t != null ? t.font : TMP_Settings.defaultFontAsset;
            return _font;
        }
    }

    /// <summary>The Arena Master at his desk in the forecourt: press E to enter a contest, challenge a player, or see the standings.</summary>
    internal class Master : MonoBehaviour, Hoverable, Interactable
    {
        public string GetHoverText()
        {
            string busy = Net.Busy ? $"\n<color=#e8a060>{Net.BusyText}</color>" : "";
            return Localization.instance.Localize("The Arena Master\n[<color=yellow><b>$KEY_Use</b></color>] \"Step up, challenger!\"") + busy;
        }

        public string GetHoverName() => "The Arena Master";
        public float GetHoverOffset() => 0f;
        public bool Interact(Humanoid user, bool hold, bool alt)
        {
            if (hold || user != Player.m_localPlayer) return false;
            Window.Open(Window.Context.Master);
            return true;
        }
        public bool UseItem(Humanoid user, ItemDrop.ItemData item) => false;
    }
}
