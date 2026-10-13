using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Guns
{
    /// <summary>
    /// The flamethrower. What it looks like is the Cultist's own fire breath (fx_fenring_flames: the game's flame particles, its embers and a
    /// flickering light), one copy at the nozzle of every player holding one, flaming while they fire; it roars with a loop made in code
    /// (Sound.FlameLoop) and lights with the Cultist's breath.
    /// What it hits is worked out only on the shooter's game: each "round" is a puff of flame that flies a few metres, grows as it goes and
    /// catches every creature it passes through once (fire damage, which the game burns off over the next seconds, so the longer the stream
    /// stays on them the harder they burn, plus a little at once). A puff stops at walls and the ground and never damages buildings, so
    /// nobody burns down their own base. Nothing here is networked: other players see and hear the stream from the shot ticks (Share.cs).
    /// </summary>
    internal static class Flame
    {
        private sealed class Nozzle
        {
            public GameObject Go;
            public ParticleSystem[] Systems;
            public GameObject Light;
            public AudioSource Roar, Ignite;
            public Transform Muzzle;
            public bool On;
            public int Seen;   // the frame the stream was last asked for
        }

        private sealed class Puff
        {
            public Vector3 Pos, Vel, Start;
            public float Age, Ttl;
            public readonly HashSet<Character> Hit = new HashSet<Character>();
        }

        private const float ImmediateShare = 0.25f;   // what lands at once, as blunt damage, beside the fire (the game's fire arrows and staff mix the same way)
        private const float Drag = 2.2f, Rise = 2.5f;  // the stream slows and lifts as it goes
        private static GameObject _template;
        private static AudioClip _loop;
        private static AudioClip[] _ignite;
        private static UnityEngine.Audio.AudioMixerGroup _mixer;
        private static readonly Dictionary<Player, Nozzle> Nozzles = new Dictionary<Player, Nozzle>();
        private static readonly List<Puff> Puffs = new List<Puff>();
        private static readonly List<Player> Gone = new List<Player>();
        private static Player _owner;
        private static ItemDrop.ItemData _weapon;
        private static GunDef _def;
        /// <summary>The farthest a puff got from where it left the nozzle (Claude Tools' `guns fire` reports it).</summary>
        internal static float Reach;

        private static readonly int Solid = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "terrain", "vehicle");
        private static readonly int Bodies = LayerMask.GetMask("character", "character_net", "character_ghost", "character_noenv");

        /// <summary>The stream's prefab under the (switched-off) holder: the Cultist's breath with nothing networked and nothing that times out.</summary>
        internal static void Build(ZNetScene scene, Transform holder)
        {
            GameObject source = scene.GetPrefab("fx_fenring_flames");
            if (source == null) { Plugin.Log.LogWarning("the fx_fenring_flames prefab was not found, so the flamethrower shows no flame"); return; }
            GameObject go = Object.Instantiate(source, holder);
            go.name = "dh_flame_stream";
            foreach (TimedDestruction t in go.GetComponentsInChildren<TimedDestruction>(true)) Object.DestroyImmediate(t);
            foreach (ZNetView v in go.GetComponentsInChildren<ZNetView>(true)) Object.DestroyImmediate(v);
            Transform sfx = go.transform.Find("SFX");
            if (sfx != null)
            {
                ZSFX z = sfx.GetComponent<ZSFX>();
                if (z != null && z.m_audioClips != null && z.m_audioClips.Length > 0) _ignite = z.m_audioClips;
                AudioSource a = sfx.GetComponent<AudioSource>();
                if (a != null) _mixer = a.outputAudioMixerGroup;
                Object.DestroyImmediate(sfx.gameObject);
            }
            foreach (LightFlicker f in go.GetComponentsInChildren<LightFlicker>(true)) { f.m_ttl = 0f; f.m_fadeInDuration = 0.08f; }
            foreach (ParticleSystem ps in go.GetComponentsInChildren<ParticleSystem>(true))
            {
                ParticleSystem.MainModule main = ps.main;
                main.loop = true;
                main.playOnAwake = false;
                if (ps.transform.parent == go.transform) ps.transform.localPosition = Vector3.zero;   // (the Cultist's two jets come from its two hands: here both leave the nozzle)
            }
            _template = go;
            _loop = Sound.FlameLoop();
            Fx.Own(_loop);
        }

        // ---- the stream, on every player's game -----------------------------------------------------------------------

        /// <summary>Every frame for a player holding the flamethrower: whether their stream is on. Placed at the nozzle in Late.</summary>
        internal static void Stream(Player p, bool on)
        {
            if (p == null || _template == null) return;
            if (!Nozzles.TryGetValue(p, out Nozzle n) || n.Go == null)
            {
                if (!on) return;
                Nozzles[p] = n = Make();
            }
            n.Seen = Time.frameCount;
            n.Muzzle = Fx.Muzzle(p);
            if (on == n.On) return;
            n.On = on;
            foreach (ParticleSystem ps in n.Systems)
            {
                if (on) ps.Play(false);
                else ps.Stop(false, ParticleSystemStopBehavior.StopEmitting);   // (what is in the air burns out)
            }
            if (n.Light != null) n.Light.SetActive(on);
            if (on)
            {
                if (_ignite != null) n.Ignite.PlayOneShot(_ignite[Random.Range(0, _ignite.Length)], Volume(0.8f));
                if (!n.Roar.isPlaying) { n.Roar.volume = 0f; n.Roar.Play(); }
            }
        }

        private static float Volume(float share) => Mathf.Clamp(Plugin.Volume.Value * share * (_mixer != null ? 1f : AudioMan.GetSFXVolume()), 0f, 1f);

        private static Nozzle Make()
        {
            var go = Object.Instantiate(_template);
            go.name = "QuadsGunsFlame";
            go.SetActive(true);
            var n = new Nozzle { Go = go };
            var systems = new List<ParticleSystem>();
            foreach (ParticleSystem ps in go.GetComponentsInChildren<ParticleSystem>(true))
                if (ps.transform.parent == go.transform) { ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear); systems.Add(ps); }   // (each one's own children follow it)
            n.Systems = systems.ToArray();
            Light light = go.GetComponentInChildren<Light>(true);
            if (light != null) { n.Light = light.gameObject; n.Light.SetActive(false); }
            n.Roar = Source(go, true);
            n.Roar.clip = _loop;
            n.Ignite = Source(go, false);
            return n;
        }

        private static AudioSource Source(GameObject go, bool loop)
        {
            AudioSource s = go.AddComponent<AudioSource>();
            s.outputAudioMixerGroup = _mixer;
            s.loop = loop;
            s.playOnAwake = false;
            s.spatialBlend = 1f;
            s.rolloffMode = AudioRolloffMode.Logarithmic;
            s.minDistance = 4f;
            s.maxDistance = 60f;
            s.dopplerLevel = 0f;
            return s;
        }

        /// <summary>Every frame after the posing: each stream sits at its nozzle, and stops when nobody asked for it this frame (the gun put away).</summary>
        internal static void Late()
        {
            foreach (KeyValuePair<Player, Nozzle> kv in Nozzles)
            {
                Nozzle n = kv.Value;
                if (n.Go == null) continue;
                if (n.Seen != Time.frameCount && n.On) Stream(kv.Key, false);
                if (n.Muzzle != null) n.Go.transform.SetPositionAndRotation(n.Muzzle.position, n.Muzzle.rotation);
                float want = n.On ? Volume(0.9f) : 0f;
                n.Roar.volume = Mathf.MoveTowards(n.Roar.volume, want, Time.deltaTime * (n.On ? 6f : 4f));
                if (!n.On && n.Roar.volume <= 0f && n.Roar.isPlaying) n.Roar.Stop();
            }
        }

        /// <summary>Forgets the streams of players who are gone.</summary>
        internal static void Prune()
        {
            foreach (KeyValuePair<Player, Nozzle> kv in Nozzles) if (kv.Key == null) Gone.Add(kv.Key);
            foreach (Player p in Gone) { if (Nozzles[p].Go != null) Object.Destroy(Nozzles[p].Go); Nozzles.Remove(p); }
            Gone.Clear();
        }

        internal static void Clear()
        {
            foreach (Nozzle n in Nozzles.Values) if (n.Go != null) Object.Destroy(n.Go);
            Nozzles.Clear();
            Puffs.Clear();
            _owner = null; _weapon = null; _def = null;
            _template = null; _loop = null; _ignite = null; _mixer = null;
        }

        // ---- what it hits, on the shooter's game ----------------------------------------------------------------------

        /// <summary>One puff of flame leaves the nozzle (one "round" of the flamethrower).</summary>
        internal static void Emit(Player p, ItemDrop.ItemData weapon, GunDef d, Vector3 at, Vector3 dir)
        {
            _owner = p; _weapon = weapon; _def = d;
            Puffs.Add(new Puff { Pos = at, Start = at, Vel = dir * d.Speed + p.GetVelocity(), Ttl = d.Ttl });
        }

        /// <summary>Every frame: the puffs fly on, catch what they touch, and stop at what is solid.</summary>
        internal static void Tick()
        {
            if (Puffs.Count == 0) return;
            if (_owner == null || _weapon == null || _def == null) { Puffs.Clear(); return; }
            float dt = Time.deltaTime;
            for (int i = Puffs.Count - 1; i >= 0; i--)
            {
                Puff f = Puffs[i];
                Vector3 step = f.Vel * dt;
                float length = step.magnitude;
                bool stop = false;
                if (length > 0.0001f && Physics.SphereCast(f.Pos, 0.1f, step / length, out RaycastHit wall, length, Solid, QueryTriggerInteraction.Ignore))
                {
                    step = step / length * wall.distance;
                    stop = true;
                }
                float radius = Mathf.Lerp(0.2f, 0.9f, f.Age / f.Ttl);   // (it spreads as it goes)
                foreach (Collider c in Physics.OverlapCapsule(f.Pos, f.Pos + step, radius, Bodies, QueryTriggerInteraction.Ignore))
                {
                    Character ch = c.GetComponentInParent<Character>();
                    if (ch == null || f.Hit.Contains(ch) || !CanBurn(_owner, ch)) continue;
                    f.Hit.Add(ch);
                    Burn(ch, c, f.Vel);
                }
                f.Pos += step;
                Reach = Mathf.Max(Reach, Vector3.Distance(f.Start, f.Pos));
                f.Vel = f.Vel * Mathf.Exp(-Drag * dt) + Vector3.up * (Rise * dt);
                f.Age += dt;
                if (stop || f.Age >= f.Ttl) Puffs.RemoveAt(i);
            }
        }

        /// <summary>Who the flame may catch: the game's own rule for a player's projectile (Projectile.IsValidTarget).</summary>
        private static bool CanBurn(Player owner, Character c)
        {
            if (c == owner || c.IsDead()) return false;
            bool enemy = BaseAI.IsEnemy(owner, c) || (c.GetBaseAI() != null && c.GetBaseAI().IsAggravatable());
            if (!owner.IsPVPEnabled() && !enemy) return false;
            return !c.IsDodgeInvincible();
        }

        private static void Burn(Character c, Collider collider, Vector3 vel)
        {
            HitData hit = Fire.MakeHit(_owner, _weapon, _def);
            hit.m_damage.m_blunt = hit.m_damage.m_fire * ImmediateShare;
            hit.m_point = collider.ClosestPoint(c.GetCenterPoint());
            hit.m_dir = vel.normalized;
            hit.m_hitCollider = collider;
            hit.m_ranged = true;
            c.Damage(hit);
            _owner.RaiseSkill(hit.m_skill, hit.m_skillRaiseAmount);
        }
    }
}
