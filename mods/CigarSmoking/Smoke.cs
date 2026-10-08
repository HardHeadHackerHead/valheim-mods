using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace CigarSmoking
{
    /// <summary>
    /// Lives on every player. While the player's ZDO says a cigar is lit (see SE_Smoking) it shows a cigar in their mouth, an ember with a
    /// small flickering light, and smoke; the cigar burns shorter as the time runs out. It reads the ZDO, so it works for other players too.
    /// With nothing in the right hand the cigar is held in it, and now and then brought up to the mouth for a puff; with a weapon (or
    /// tool) in the right hand it stays in the mouth.
    /// </summary>
    internal class Smoke : MonoBehaviour
    {
        private Player _player;
        private ZNetView _view;
        private Transform _head;
        private Cigar.Parts _cigar;
        private Light _light;
        private ParticleSystem _smoke;
        private Transform _hand, _forearm, _index, _little, _grip, _index2, _middle1, _middle2;
        private VisEquipment _vis;
        private float _nextPuff, _seed;
        private float _blend = 1f;                 // 0: the cigar is in the hand, 1: in the mouth
        private float _holdUntil, _nextHandPuff;   // time until the cigar stays at the mouth, and when the next puff is due
        private bool _lit;
        private float _exhaleAt;
        private long _lastStart;
        private bool _warned;
        private int _type, _builtType = -1;
        private static readonly System.Reflection.FieldInfo RightItemHash = AccessTools.Field(typeof(VisEquipment), "m_rightItem");
        private static readonly HashSet<int> CigarHashes = new HashSet<int>(System.Array.ConvertAll(Types.All, t => t.Prefab.GetStableHashCode()));

        internal static void Attach(Player p)
        {
            if (p == null) return;
            Clear(p, keep: null);
            p.gameObject.AddComponent<Smoke>();
        }

        /// <summary>
        /// Removes every Smoke on a player except keep, including ones left by an earlier build of this mod (a hot reload only knows its own
        /// type, and the old copy would go on running its old code).
        /// </summary>
        internal static void Clear(Player p, Smoke keep)
        {
            foreach (MonoBehaviour m in p.GetComponents<MonoBehaviour>())
                if (m != null && m != keep && m.GetType().FullName == typeof(Smoke).FullName) Destroy(m);
        }

        private void Awake()
        {
            _player = GetComponent<Player>();
            _view = GetComponent<ZNetView>();
            _seed = Random.value * 100f;
        }

        /// <summary>How much of the cigar is left: 0 when nobody is smoking, up to 1 when it was just lit.</summary>
        private float Remaining()
        {
            if (_view == null || !_view.IsValid() || ZNet.instance == null || _player == null || _player.IsDead()) return 0f;
            ZDO zdo = _view.GetZDO();
            long end = zdo.GetLong(SE_Smoking.KeyEnd, 0L);
            if (end == 0L) return 0f;
            long start = zdo.GetLong(SE_Smoking.KeyStart, 0L);
            double now = ZNet.instance.GetTimeSeconds() * 1000.0;
            if (now >= end || end <= start) return 0f;
            return Mathf.Clamp01((float)((end - now) / (end - start)));
        }

        private void LateUpdate()
        {
            float left = Remaining();
            if (left <= 0f) { Hide(); return; }

            _type = _view.GetZDO().GetInt(SE_Smoking.KeyType, 0);
            if (_cigar != null && _type != _builtType)   // another kind of cigar was lit: draw that one
            {
                Destroy(_cigar.Root.gameObject);
                if (_smoke != null) Destroy(_smoke.gameObject);
                _cigar = null; _smoke = null; _light = null;
            }
            if (!Ensure()) return;
            long started = _view.GetZDO().GetLong(SE_Smoking.KeyStart, 0L);
            if (!_lit || started != _lastStart)   // just lit, or lit again from a new cigar
            {
                _lit = true;
                _lastStart = started;
                _holdUntil = Time.time + 2.5f;   // the game's "eat" animation brings the hand to the mouth
                _blend = 1f;
                _nextHandPuff = Time.time + Random.Range(8f, 14f);
            }
            _cigar.Root.gameObject.SetActive(true);
            _cigar.SetBurn(left);

            bool free = HandFree();
            bool lighting = Time.time < _holdUntil;   // the game's own animation, which also shows its own cigar in the hand
            if (free && !lighting && Time.time > _nextHandPuff)
            {
                if (CanPuff())
                {
                    _player.GetComponent<ZSyncAnimation>()?.SetTrigger("eat");   // the arm brings the hand to the mouth (other games see it too)
                    _exhaleAt = Time.time + 1.5f;
                    _nextHandPuff = Time.time + Random.Range(14f, 24f);
                }
                else _nextHandPuff = Time.time + 2f;
            }
            if (_exhaleAt > 0f && Time.time > _exhaleAt) { _exhaleAt = 0f; if (Plugin.DrawSmoke.Value) Exhale(); }

            // with a weapon in the right hand the cigar is in the mouth, otherwise in the hand (it goes where the hand goes)
            _blend = Mathf.MoveTowards(_blend, free ? 0f : 1f, Time.deltaTime / 0.35f);
            Place(Smooth(_blend));
            bool glow = Plugin.DrawGlow.Value;
            _light.enabled = glow;
            if (glow) _light.intensity = 0.35f + 0.25f * Mathf.PerlinNoise(Time.time * 6f, _seed);

            if (Plugin.DrawSmoke.Value)
            {
                if (!_smoke.isPlaying) _smoke.Play();
                _smoke.transform.rotation = Quaternion.Euler(-90f, 0f, 0f); // smoke always rises
                var emission = _smoke.emission;
                emission.rateOverTime = _blend > 0.8f ? 4.5f : 1.5f;   // it smoulders more quietly in the hand
                if (!free && Time.time > _nextPuff)
                {
                    _nextPuff = Time.time + Random.Range(9f, 16f);
                    var puff = new ParticleSystem.EmitParams { startSize = 0.22f, startLifetime = 3.2f, velocity = new Vector3(0f, 0.28f, 0f) };
                    _smoke.Emit(puff, 6);
                }
            }
            else if (_smoke.isPlaying) _smoke.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }

        private void Hide()
        {
            if (_cigar == null) return;
            if (_cigar.Root.gameObject.activeSelf) _cigar.Root.gameObject.SetActive(false);
            if (_smoke != null && _smoke.isPlaying) _smoke.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }

        private void OnDestroy()
        {
            if (_cigar != null) Destroy(_cigar.Root.gameObject);
            if (_smoke != null) Destroy(_smoke.gameObject);
        }

        private bool Ensure()
        {
            if (_head == null || _hand == null || _forearm == null)
            {
                Animator animator = GetComponentInChildren<Animator>();
                if (animator == null) return false;
                _head = Utils.GetBoneTransform(animator, HumanBodyBones.Head);
                _hand = Utils.GetBoneTransform(animator, HumanBodyBones.RightHand);
                _forearm = Utils.GetBoneTransform(animator, HumanBodyBones.RightLowerArm);
                if (_forearm == null && _hand != null) _forearm = _hand.parent;   // this rig has no forearm bone of its own
                _index = FindChild(_hand, "RightHandIndex1");   // the finger bones are not mapped as humanoid bones, but they are the hand's children
                _little = FindChild(_hand, "RightHandPinky1");
                _grip = FindChild(_hand, "RightHand_Attach");   // where the game puts what the hand holds
                _index2 = FindChild(_hand, "RightHandIndex2");
                _middle1 = FindChild(_hand, "RightHandMiddle1");
                _middle2 = FindChild(_hand, "RightHandMiddle2");
                _vis = GetComponent<VisEquipment>();
                if (_head == null || _hand == null || _forearm == null)
                {
                    if (!_warned) { _warned = true; Debug.LogWarning($"[CigarSmoking] bones: head={_head != null} hand={_hand != null} forearm={_forearm != null} animator={animator.name} human={animator.isHuman}"); }
                    return false;
                }
            }
            if (_cigar == null)
            {
                _cigar = Cigar.Build(null, "Cigar", Types.ByIndex(_type), true);   // not under the head bone: its scale would stretch the cigar
                _builtType = _type;
                _cigar.TipRenderer.sharedMaterial = Cigar.EmberMat();

                var lightGo = new GameObject("Ember");
                lightGo.transform.SetParent(_cigar.Tip, false);
                _light = lightGo.AddComponent<Light>();
                _light.type = LightType.Point;
                _light.range = 1.6f;
                _light.color = new Color(1f, 0.55f, 0.25f);
                _light.shadows = LightShadows.None;

                _smoke = MakeSmoke(_cigar.Tip);
            }
            return true;
        }

        // Every player has the same rig: in the head bone's own space the face looks along -X and the top of the head is +Y
        // (found by logging a standing player's forward and up in head space). The bone itself sits at the base of the neck and is scaled 95 times,
        // so the cigar is placed in world space, a little above the bone (it sits at the base of the neck) and ahead of it.
        private static readonly Vector3 Forward = new Vector3(-1f, 0f, 0f), Up = Vector3.up;

        private static Transform FindChild(Transform root, string name)
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true)) if (t.name == name) return t;
            return null;
        }

        private static float Smooth(float t) => t * t * (3f - 2f * t);

        /// <summary>Nothing (but a cigar) in the right hand.</summary>
        private bool HandFree()
        {
            if (_vis == null || RightItemHash == null) return true;
            int item = (int)RightItemHash.GetValue(_vis);
            return item == 0 || CigarHashes.Contains(item);
        }

        /// <summary>Only the player's own game starts a puff, and only when standing about, so the arm does not fight a swing or a run.</summary>
        private bool CanPuff() =>
            _view.IsOwner() && !_player.InAttack() && !_player.IsBlocking() && !_player.IsSwimming() && _player.IsOnGround() && _player.GetVelocity().magnitude < 1.5f;

        private void MouthPose(out Vector3 pos, out Quaternion rot)
        {
            Vector3 fwd = _head.TransformDirection(Forward).normalized, up = _head.TransformDirection(Up).normalized;
            pos = _head.position + fwd * 0.125f + up * 0.10f;   // the mouth end just past the lips
            rot = Quaternion.LookRotation((fwd + up * 0.15f).normalized, up);   // forward and a touch up, like a cigar held in the teeth
        }

        private void HandPose(out Vector3 pos, out Quaternion rot)
        {
            if (_index != null && _index2 != null && _middle1 != null && _middle2 != null && _little != null)
            {
                // pinched in the gap between the index and middle fingers, at their first joints, like a cigarette: it lies in the gap
                // (not across the fingers), pointing out from the back of the hand and a little towards the fingertips
                Vector3 across = (_index.position - _little.position).normalized;
                Vector3 fingers = (_index2.position - _index.position).normalized;
                Vector3 palm = Vector3.Cross(across, fingers).normalized;
                Vector3 curl = _grip != null ? _grip.position - _index.position : Vector3.zero;   // the grip point is inside the curled fingers: that is the palm side
                if (Vector3.Dot(palm, curl) < 0f) palm = -palm;
                Vector3 axis = (fingers * 0.45f - palm).normalized;
                Vector3 gap = (_index.position + _index2.position + _middle1.position + _middle2.position) * 0.25f;
                pos = gap + axis * 0.015f;   // the mouth end is hardly behind the fingers, so even a burnt-down cigar has its ember clear of the hand
                rot = Quaternion.LookRotation(axis, Vector3.up);
                return;
            }
            // without finger bones: by where the arm points, which is only a rough guess
            Vector3 along = (_hand.position - _forearm.position).normalized;
            Vector3 dir = (transform.forward + Vector3.up * 0.5f).normalized;
            pos = _hand.position + along * 0.10f - dir * 0.05f + Vector3.up * 0.01f;
            rot = Quaternion.LookRotation(dir, Vector3.up);
        }

        /// <summary>blend: 0 holds the cigar in the hand, 1 in the mouth.</summary>
        private void Place(float blend)
        {
            MouthPose(out Vector3 mp, out Quaternion mr);
            HandPose(out Vector3 hp, out Quaternion hr);
            Transform t = _cigar.Root;
            t.position = Vector3.Lerp(hp, mp, blend);
            t.rotation = Quaternion.Slerp(hr, mr, blend);
        }

        private void Exhale()
        {
            MouthPose(out Vector3 mp, out Quaternion mr);
            Vector3 at = mp + mr * Vector3.forward * 0.03f;
            var puff = new ParticleSystem.EmitParams { startSize = 0.24f, startLifetime = 3.4f, position = at, applyShapeToPosition = false };
            for (int i = 0; i < 9; i++)
            {
                puff.velocity = mr * Vector3.forward * Random.Range(0.15f, 0.4f) + new Vector3(0f, Random.Range(0.1f, 0.3f), 0f);
                _smoke.Emit(puff, 1);
            }
        }

        private static Material _smokeMat;

        private static ParticleSystem MakeSmoke(Transform parent)
        {
            var go = new GameObject("CigarSmoke");
            go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.loop = true;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.8f, 2.8f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.05f, 0.14f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.10f);
            main.startColor = new Color(0.85f, 0.85f, 0.85f, 0.55f);
            main.gravityModifier = -0.02f;
            main.maxParticles = 60;

            var emission = ps.emission;
            emission.rateOverTime = 9f;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 10f;
            shape.radius = 0.004f;

            var velocity = ps.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;
            velocity.x = new ParticleSystem.MinMaxCurve(0f);
            velocity.y = new ParticleSystem.MinMaxCurve(0.2f);
            velocity.z = new ParticleSystem.MinMaxCurve(0f);

            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.12f;
            noise.frequency = 0.7f;

            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.45f), new Keyframe(1f, 2.6f)));

            var color = ps.colorOverLifetime;
            color.enabled = true;
            var fade = new Gradient();
            fade.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                         new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0f, 1f) });
            color.color = new ParticleSystem.MinMaxGradient(fade);

            var rend = go.GetComponent<ParticleSystemRenderer>();
            rend.renderMode = ParticleSystemRenderMode.Billboard;
            rend.sharedMaterial = SmokeMat();
            return ps;
        }

        private static Material SmokeMat()
        {
            if (_smokeMat != null) return _smokeMat;
            const int n = 32;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(n * 0.5f, n * 0.5f)) / (n * 0.5f);
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Pow(Mathf.Clamp01(1f - d), 1.6f)));
                }
            tex.Apply(false, false);
            _smokeMat = new Material(Shader.Find("Sprites/Default")) { mainTexture = tex };
            return _smokeMat;
        }
    }
}
