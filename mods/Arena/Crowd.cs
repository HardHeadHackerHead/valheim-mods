using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;
using static DHack.Shared.Translations;

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

        private sealed class Spec { public GameObject Go; public Animator Anim; public Vector3 At; public float Angle, Next; public bool Sitting; public int SettleAt; }

        private static readonly List<Spec> Specs = new List<Spec>();
        private static GameObject _root;
        private static float _lastAction, _lastBoo, _waveAt, _waveAngle, _loudSince;
        private static bool _open;
        private static int _building;

        internal static bool IsOpen => _open;

        internal static string Mood =>
            T(Favour < 15f ? "arena_mood_bored" : Favour < 35f ? "arena_mood_restless" : Favour < 55f ? "arena_mood_watching" : Favour < 75f ? "arena_mood_excited" : Favour < 90f ? "arena_mood_roaring" : "arena_mood_ecstatic");

        // ---- the stands ----------------------------------------------------------------------------------------------------

        /// <summary>The crowd fills the arena's stands for a fight.</summary>
        internal static void Open()
        {
            Close(false);
            _open = true;
            Favour = 25f; Excitement = 0.25f; _lastAction = Time.time; _waveAt = 0f; _loudSince = 0f;
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

        /// <summary>The spectators come in one a frame (each is a whole viking: making them all at once would stutter).</summary>
        private static void FillSome()
        {
            if (_root == null) return;
            int want = Mathf.RoundToInt(Layout.Seats.Count * Mathf.Clamp01(Plugin.CrowdSize.Value / 100f));
            if (Specs.Count >= want || _building >= Layout.Seats.Count) return;
            int step = Mathf.Max(1, Layout.Seats.Count / Mathf.Max(1, want));
            var rng = new System.Random(_building * 7919 + 13);
            int index = (_building * step + _building / Mathf.Max(1, Layout.Seats.Count / step)) % Layout.Seats.Count;
            Layout.Mark seat = Layout.Seats[index];
            _building++;
            Vector3 local = seat.Pos + new Vector3((float)(rng.NextDouble() - 0.5) * 0.4f, 0f, 0f);
            Vector3 at = Site.World(local);
            Vector3 face = Site.Centre - at; face.y = 0f;
            GameObject go = Figures.Make(_root.transform, at, Quaternion.LookRotation(face), Figures.Spectator(rng), false);
            if (go == null) return;
            // (settled once the game has dressed it, a frame or two from now: capes hang still, cloth for a whole crowd is too much)
            var s = new Spec { Go = go, Anim = go.GetComponentInChildren<Animator>(), At = at, Angle = Mathf.Atan2(local.x, local.z), Next = Time.time + (float)rng.NextDouble() * 4f, SettleAt = Time.frameCount + 3 };
            if (rng.NextDouble() < 0.45) { s.Sitting = true; Figures.Hold(s.Anim, "sit", true); }
            Specs.Add(s);
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
                if (Favour < 10f && Time.time - _lastBoo > 18f) { _lastBoo = Time.time; Boo(); Hud.Call(T("arena_call_boo"), T("arena_call_boo_sub"), false); }
            }

            // what they throw you, and what they chant for (Favours.cs)
            Favours.Tick();

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
                if (s.SettleAt > 0 && Time.frameCount >= s.SettleAt) { s.SettleAt = 0; Figures.Settle(s.Go); }
                Vector3 to = new Vector3(look.x - s.At.x, 0f, look.z - s.At.z);
                if (to.sqrMagnitude > 0.01f) s.Go.transform.rotation = Quaternion.Slerp(s.Go.transform.rotation, Quaternion.LookRotation(to, Vector3.up), dt * 1.5f);
                // the sitters stand up when it gets exciting, and sit down again when it does not
                if (s.Sitting && roused) { s.Sitting = false; Figures.Hold(s.Anim, "sit", false); s.Next = Time.time + Random.Range(0.2f, 1.5f); }
                else if (!s.Sitting && !roused && Random.value < dt * 0.02f) { s.Sitting = true; Figures.Hold(s.Anim, "sit", true); }
                if (_waveAt > 0f && Mathf.Abs(Mathf.DeltaAngle(s.Angle * Mathf.Rad2Deg, wave * Mathf.Rad2Deg)) < 10f && Time.time > s.Next)
                { s.Next = Time.time + 2.5f; Figures.Emote(s.Anim, "cheer"); continue; }
                if (!s.Sitting && Time.time > s.Next && Random.value < 0.25f + 0.6f * Excitement)
                {
                    s.Next = Time.time + Random.Range(3f, 9f) * (1.4f - Excitement);
                    if (Excitement > 0.35f) Figures.Emote(s.Anim, Pick("cheer", "cheer", "roar", "toast", "flex", "challenge", "point", "thumbsup"));
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
                if (s.Sitting) { s.Sitting = false; Figures.Hold(s.Anim, "sit", false); }
                s.Next = Time.time + Random.Range(0f, 0.6f);
                Figures.Emote(s.Anim, emotes[Random.Range(0, emotes.Length)]);
            }
        }

        /// <summary>
        /// Someone in the stands near you throws you a thing: it sails over the bars and lands in the middle of the paved border's first block,
        /// a metre in from the podium wall (never on the wall, never against it), below where they sit, and a glow marks where it came down.
        /// False when it could not be thrown.
        /// </summary>
        internal static bool Throw(string item, int amount, System.Action<ItemDrop> setup = null)
        {
            Player player = Player.m_localPlayer;
            if (player == null || Layout.Seats.Count == 0) return false;
            GameObject prefab = item != null ? ZNetScene.instance.GetPrefab(item) : null;
            if (prefab == null || prefab.GetComponent<ItemDrop>() == null) return false;
            // from one of the few spectators nearest your side of the stands (or a seat there, with the spectators switched off)
            Vector3 me = Site.Local(player.transform.position);
            float mine = Mathf.Atan2(me.x, me.z);
            Spec from = null;
            if (Specs.Count > 0)
            {
                var near = new List<Spec>(Specs);
                near.Sort((a, b) => Mathf.Abs(Mathf.DeltaAngle(a.Angle * Mathf.Rad2Deg, mine * Mathf.Rad2Deg)).CompareTo(Mathf.Abs(Mathf.DeltaAngle(b.Angle * Mathf.Rad2Deg, mine * Mathf.Rad2Deg))));
                from = near[Random.Range(0, Mathf.Min(5, near.Count))];
            }
            if (from?.Go != null) Figures.Emote(from.Anim, "wave");
            float angle = from != null ? from.Angle : mine + Random.Range(-0.25f, 0.25f);
            Vector3 start = (from != null ? from.At : Site.World(Layout.Seats[Random.Range(0, Layout.Seats.Count)].Pos)) + Vector3.up * 2f;
            Vector3 land = Landing(angle);
            GameObject go = Object.Instantiate(prefab, start, Quaternion.identity);
            ItemDrop drop = go.GetComponent<ItemDrop>();
            if (drop != null)
            {
                setup?.Invoke(drop);
                // (from a land the world has not reached: only lent, it goes back when the fight is over)
                if (!Kit.IsLoan(drop.m_itemData) && !Kit.Keeps(item, Contest.Tier)) drop.m_itemData.m_customData[Kit.LoanKey] = Kit.GiftLoan;
                drop.SetStack(Mathf.Max(1, amount));   // (and saves it, with whatever setup changed)
                ZNetView view = go.GetComponent<ZNetView>();
                if (view != null && view.IsValid()) view.GetZDO().Set(Show.GiftKey, true);   // (what is left of it in the ring is cleared away: Show.ClearGifts)
                Plugin.Instance.StartCoroutine(Fly(drop, start, land));
            }
            Cheer();
            return true;
        }

        /// <summary>
        /// Where a gift lands: on the paved border round the fighting floor, in the middle of its first block (the blocks are 2 m, laid against
        /// the podium wall), at this angle round the ring; never in front of a gate, where the grate and what comes through it would take it.
        /// </summary>
        private static Vector3 Landing(float angle)
        {
            float deg = angle * Mathf.Rad2Deg;
            foreach (Layout.Mark gate in Layout.Gates)
            {
                float at = Mathf.Atan2(gate.Pos.x, gate.Pos.z) * Mathf.Rad2Deg, off = Mathf.DeltaAngle(at, deg);
                if (Mathf.Abs(off) < GateClear) deg = at + (off < 0f ? -GateClear : GateClear);
            }
            float a = deg * Mathf.Deg2Rad, r = Layout.Podium - 1f;
            Vector3 local = new Vector3(r * Mathf.Sin(a), Layout.FloorHeight(r * Mathf.Sin(a), r * Mathf.Cos(a)), r * Mathf.Cos(a));
            Vector3 world = Site.World(local);
            world.y = Site.Ground(world, world.y) + 0.25f;
            return world;
        }

        private const float GateClear = 9f;   // (degrees either side of a gate: about 2.2 m of the border, the grate's width and a little)

        /// <summary>
        /// The throw: an arc high enough to clear the bars on the parapet, flown by hand rather than left to the physics (a thrown thing would
        /// bounce off the bars, the wall or the floor and roll anywhere), then set down at rest on its spot.
        /// </summary>
        private static System.Collections.IEnumerator Fly(ItemDrop drop, Vector3 from, Vector3 to)
        {
            Rigidbody body = drop != null ? drop.GetComponent<Rigidbody>() : null;
            if (body != null) { body.isKinematic = true; body.linearVelocity = Vector3.zero; }
            // high enough over the bars: they stand at the parapet's front, flush with the podium wall's face, up to about 8 m over the floor
            Vector3 fromLocal = Site.Local(from), toLocal = Site.Local(to);
            float rFrom = new Vector2(fromLocal.x, fromLocal.z).magnitude, rTo = new Vector2(toLocal.x, toLocal.z).magnitude;
            float k = rFrom > rTo + 0.1f ? Mathf.Clamp((rFrom - (Layout.Podium + 0.05f)) / (rFrom - rTo), 0.05f, 0.95f) : 0.5f;
            float clear = Site.World(new Vector3(0f, 9f, 0f)).y - Mathf.Lerp(from.y, to.y, k);
            float lift = Mathf.Max(1.5f, clear / (4f * k * (1f - k)));
            float time = Mathf.Clamp(Vector3.Distance(from, to) / 9f, 0.9f, 1.6f), t = 0f;
            while (t < time)
            {
                if (drop == null) yield break;
                t += Time.deltaTime;
                float u = Mathf.Clamp01(t / time);
                Vector3 p = Vector3.Lerp(from, to, u) + Vector3.up * (4f * lift * u * (1f - u));
                drop.transform.position = p;
                drop.transform.rotation = Quaternion.Euler(u * 540f, u * 220f, 0f);   // (it tumbles through the air)
                yield return null;
            }
            if (drop == null) yield break;
            drop.transform.SetPositionAndRotation(to, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
            if (body != null) { body.isKinematic = false; body.linearVelocity = Vector3.zero; body.angularVelocity = Vector3.zero; }
            Show.GiftMarker(to);
        }

        /// <summary>The crowd answers an emote of yours with the same (or laughs at you).</summary>
        internal static void Mirror(string emote)
        {
            if (emote == "laugh") { Sound.Boo(); React(0.5f, "laugh", "point"); return; }
            Sound.Cheer();
            React(0.55f, emote, emote, "cheer");
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

        /// <summary>A win: everyone on their feet, cheering, and a wave going round the stands.</summary>
        internal static void Celebrate()
        {
            Favour = 100f; Excitement = 1f; _lastAction = Time.time;
            foreach (Spec s in Specs) if (s.Go != null && s.Sitting) { s.Sitting = false; Figures.Hold(s.Anim, "sit", false); }
            React(1f, "cheer", "roar", "toast", "flex", "challenge");
            _waveAt = Time.time; _waveAngle = Random.value * Mathf.PI * 2f; _loudSince = Time.time;
        }

        internal static void Cheer() { Sound.Cheer(); React(0.35f, "cheer", "roar", "flex", "toast"); }
        internal static void Roar() { Sound.Roar(); React(0.8f, "cheer", "roar", "challenge", "toast", "flex"); }
        internal static void Boo() { Sound.Boo(); React(0.3f, "nonono", "laugh", "point"); }
        internal static void Applause() { Sound.Applause(); React(0.4f, "cheer", "thumbsup"); }
        internal static void Gong() => Sound.Gong();
        internal static void Horn() => Play("horn");
    }
}
