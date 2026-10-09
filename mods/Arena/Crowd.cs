using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Arena
{
    /// <summary>
    /// The crowd. During a fight the stands fill with vikings (the game's own body, dressed as farmers and fighters, some with a drink or a
    /// torch): some sit until something happens, they turn to follow you, cheer, roar, toast and flex when you do well, jeer and wave you off
    /// when you do badly, and send a wave of cheering round the stands when they are roaring. Their favour (0 to 100) grows with kills, quick
    /// kills, parries, flawless rounds and kills made at the edge of death; it drains when you are hit or the fight drags; it adds to the prize,
    /// and when they love you they throw you food. Their sound is in Sound.cs.
    /// </summary>
    internal static class Crowd
    {
        internal static float Favour { get; private set; }
        internal static float Excitement { get; private set; }

        private sealed class Spec { public GameObject Go; public Vector3 At; public float Angle, Next; public bool Sitting; }

        private static readonly List<Spec> Specs = new List<Spec>();
        private static GameObject _root;
        private static float _lastAction, _lastBoo, _nextGift, _waveAt, _waveAngle, _loudSince, _nextBuild;
        private static bool _open;
        private static int _building;

        internal static bool IsOpen => _open;

        internal static string Mood =>
            Favour < 15f ? "Bored" : Favour < 35f ? "Restless" : Favour < 55f ? "Watching" : Favour < 75f ? "Excited" : Favour < 90f ? "Roaring" : "Ecstatic";

        // ---- the stands ----------------------------------------------------------------------------------------------------

        /// <summary>The crowd fills the arena's stands for a fight.</summary>
        internal static void Open()
        {
            Close(false);
            _open = true;
            Favour = 25f; Excitement = 0.25f; _lastAction = Time.time; _nextGift = Time.time + 30f; _waveAt = 0f; _loudSince = 0f;
            if (Plugin.Spectators.Value && Site.Known && Scenery.Root != null)
            {
                _root = new GameObject("ArenaCrowd");
                _building = 0;
            }
        }

        internal static void Close(bool bow)
        {
            _open = false;
            if (_root != null) Object.Destroy(_root);
            _root = null; Specs.Clear();
        }

        internal static void Clear() { Close(false); Sound.Clear(); }

        /// <summary>The spectators come in a few at a time (each is a whole viking: making them all in one frame would stutter).</summary>
        private static void FillSome()
        {
            if (_root == null || Time.time < _nextBuild) return;
            int want = Mathf.RoundToInt(Layout.Seats.Count * Mathf.Clamp01(Plugin.CrowdSize.Value / 100f));
            if (Specs.Count >= want || _building >= Layout.Seats.Count) return;
            _nextBuild = Time.time + 0.05f;
            int step = Mathf.Max(1, Layout.Seats.Count / Mathf.Max(1, want));
            for (int n = 0; n < 3 && Specs.Count < want && _building < Layout.Seats.Count; n++)
            {
                var rng = new System.Random(_building * 7919 + 13);
                int index = (_building * step + _building / Mathf.Max(1, Layout.Seats.Count / step)) % Layout.Seats.Count;
                Layout.Mark seat = Layout.Seats[index];
                _building++;
                Vector3 local = seat.Pos + new Vector3((float)(rng.NextDouble() - 0.5) * 0.4f, 0f, 0f);
                Vector3 at = Site.World(local);
                Vector3 face = Site.Centre - at; face.y = 0f;
                GameObject go = Figures.Make(_root.transform, at, Quaternion.LookRotation(face), Figures.Spectator(rng), false);
                if (go == null) return;
                foreach (Behaviour b in go.GetComponentsInChildren<Behaviour>(true))
                    if (b != null && b.GetType().Name.Contains("MagicaCloth")) b.enabled = false;   // (capes hang still: cloth for a whole crowd is too much)
                var s = new Spec { Go = go, At = at, Angle = Mathf.Atan2(local.x, local.z), Next = Time.time + (float)rng.NextDouble() * 4f };
                if (rng.NextDouble() < 0.45) { s.Sitting = true; Figures.Hold(go, "sit", true); }
                Specs.Add(s);
            }
        }

        // ---- every frame -----------------------------------------------------------------------------------------------------

        internal static void Tick(float dt)
        {
            Sound.Tick(_open, Excitement);
            if (!_open) { Excitement = Mathf.MoveTowards(Excitement, 0f, dt * 0.2f); return; }
            FillSome();
            float target = Mathf.Clamp01(0.2f + Favour / 140f + (Time.time - _lastAction < 3f ? 0.35f : 0f));
            Excitement = Mathf.MoveTowards(Excitement, target, dt * (target > Excitement ? 1.4f : 0.3f));

            // a fight that drags on bores them
            if (Contest.Fighting && Time.time - _lastAction > 16f)
            {
                Favour = Mathf.Max(0f, Favour - dt * 0.6f);
                if (Favour < 10f && Time.time - _lastBoo > 18f) { _lastBoo = Time.time; Boo(); Hud.Call("BOOO!", "The crowd is bored. Fight!", false); }
            }

            // when they love you they throw food (coins in a no-food contest)
            if (Contest.Fighting && Favour >= 75f && Time.time > _nextGift && Plugin.Gifts.Value) { _nextGift = Time.time + Random.Range(28f, 45f); ThrowGift(); }

            // a wave round the stands when they have been roaring for a while
            if (Excitement > 0.72f) { if (_loudSince == 0f) _loudSince = Time.time; } else _loudSince = 0f;
            if (_loudSince > 0f && Time.time - _loudSince > 3f && _waveAt == 0f) { _waveAt = Time.time; _waveAngle = Random.value * Mathf.PI * 2f; }
            float wave = _waveAt > 0f ? _waveAngle + (Time.time - _waveAt) * 1.8f : -100f;
            if (_waveAt > 0f && Time.time - _waveAt > Mathf.PI * 2f / 1.8f) _waveAt = 0f;

            Player player = Player.m_localPlayer;
            Vector3 look = player != null && Site.OnFloor(player.transform.position, 2f) ? player.transform.position : Site.Centre;
            bool roused = Excitement > 0.5f;
            foreach (Spec s in Specs)
            {
                if (s.Go == null) continue;
                Vector3 to = new Vector3(look.x - s.At.x, 0f, look.z - s.At.z);
                if (to.sqrMagnitude > 0.01f) s.Go.transform.rotation = Quaternion.Slerp(s.Go.transform.rotation, Quaternion.LookRotation(to, Vector3.up), dt * 1.5f);
                // the sitters stand up when it gets exciting, and sit down again when it does not
                if (s.Sitting && roused) { s.Sitting = false; Figures.Hold(s.Go, "sit", false); s.Next = Time.time + Random.Range(0.2f, 1.5f); }
                else if (!s.Sitting && !roused && Random.value < dt * 0.02f) { s.Sitting = true; Figures.Hold(s.Go, "sit", true); }
                if (_waveAt > 0f && Mathf.Abs(Mathf.DeltaAngle(s.Angle * Mathf.Rad2Deg, wave * Mathf.Rad2Deg)) < 10f && Time.time > s.Next)
                { s.Next = Time.time + 2.5f; Figures.Emote(s.Go, "cheer"); continue; }
                if (!s.Sitting && Time.time > s.Next && Random.value < 0.25f + 0.6f * Excitement)
                {
                    s.Next = Time.time + Random.Range(3f, 9f) * (1.4f - Excitement);
                    if (Excitement > 0.35f) Figures.Emote(s.Go, Pick("cheer", "cheer", "roar", "toast", "flex", "challenge", "point", "thumbsup"));
                }
            }
        }

        private static string Pick(params string[] list) => list[Random.Range(0, list.Length)];

        /// <summary>Some of the crowd react at once (a kill, a parry, a round won), a moment apart each.</summary>
        private static void React(float share, params string[] emotes)
        {
            foreach (Spec s in Specs)
            {
                if (s.Go == null || Random.value > share) continue;
                if (s.Sitting) { s.Sitting = false; Figures.Hold(s.Go, "sit", false); }
                s.Next = Time.time + Random.Range(0f, 0.6f);
                Figures.Emote(s.Go, emotes[Random.Range(0, emotes.Length)]);
            }
        }

        private static void ThrowGift()
        {
            Player player = Player.m_localPlayer;
            if (player == null || Specs.Count == 0) return;
            string item = Rules.NoFood ? "Coins" : Roster.Gift(Contest.Tier);
            GameObject prefab = item != null ? ZNetScene.instance.GetPrefab(item) : null;
            if (prefab == null) return;
            Spec from = Specs[Random.Range(0, Specs.Count)];
            if (from.Go != null) Figures.Emote(from.Go, "wave");
            Vector3 start = from.At + Vector3.up * 2f;
            Vector3 land = player.transform.position + new Vector3(Random.Range(-2f, 2f), 0f, Random.Range(-2f, 2f));
            GameObject go = Object.Instantiate(prefab, start, Quaternion.identity);
            ItemDrop drop = go.GetComponent<ItemDrop>();
            if (drop != null && item == "Coins") drop.SetStack(Random.Range(8, 20));
            Rigidbody body = go.GetComponent<Rigidbody>();
            if (body != null)
            {
                Vector3 flat = new Vector3(land.x - start.x, 0f, land.z - start.z);
                float time = Mathf.Clamp(flat.magnitude / 9f, 0.8f, 1.8f);
                Vector3 v = flat / time;
                v.y = (land.y - start.y) / time + 0.5f * -Physics.gravity.y * time;
                body.linearVelocity = v;
            }
            Hud.Call("A GIFT FROM THE CROWD!", "They love you", false);
            Cheer();
        }

        // ---- what the crowd feels -----------------------------------------------------------------------------------------

        /// <summary>Something good (a kill, a parry, a round won): the favour rises and (if asked) the crowd cheers.</summary>
        internal static void Gain(float amount, bool cheer)
        {
            Favour = Mathf.Clamp(Favour + amount, 0f, 100f);
            _lastAction = Time.time;
            if (cheer) Cheer();
        }

        internal static void Set(float favour) => Favour = Mathf.Clamp(favour, 0f, 100f);
        internal static void Hurt(float amount) => Favour = Mathf.Max(0f, Favour - amount);

        /// <summary>A crowd sound by name (as the network sends them), with the crowd's reaction to it.</summary>
        internal static void Play(string name)
        {
            switch (name)
            {
                case "cheer": Cheer(); break;
                case "roar": Roar(); break;
                case "boo": Boo(); break;
                case "applause": Applause(); break;
                case "gong": Sound.Gong(); break;
                case "horn": Sound.Chant(); React(0.7f, "cheer", "roar", "toast"); break;
            }
        }

        internal static void Cheer() { Sound.Cheer(); React(0.35f, "cheer", "roar", "flex", "toast"); }
        internal static void Roar() { Sound.Roar(); React(0.8f, "cheer", "roar", "challenge", "toast", "flex"); }
        internal static void Boo() { Sound.Boo(); React(0.3f, "nonono", "laugh", "point"); }
        internal static void Applause() { Sound.Applause(); React(0.4f, "cheer", "thumbsup"); }
        internal static void Gong() => Sound.Gong();
        internal static void Horn() => Play("horn");
    }
}
