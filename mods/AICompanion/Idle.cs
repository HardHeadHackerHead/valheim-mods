using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace AICompanion
{
    internal enum IdlePlan { None, Fire, Chair, Shelter, Stroll }

    /// <summary>
    /// What a person does between jobs:
    ///   - at home with nothing to do: it sits by a burning fire (warming up when cold, wet or in the evening), takes a free chair or bench,
    ///     gets under a roof when it rains, or takes a stroll round its home and looks about; it faces you when you are near;
    ///   - out with you: when you stop it turns to face you, after a while of you standing about it sits down, and when you sit (on the ground
    ///     or in a chair) it sits too, in a free chair beside you when there is one; it gets up the moment you go on;
    ///   - told to stay: after half a minute it sits down where it is;
    ///   - it waves when you come back after a while away, cheers after a big fight or a boss, gives a thumbs up when you hand it something,
    ///     and answers your emotes (a wave with a wave, a dance with a dance; "come here" brings it over to you for a little while).
    /// The game's own emotes and sitting poses (the player's animations, which its body has), seen by everyone. It gets up whenever anything
    /// else needs doing: sitting has to be kept up every frame, so any other behaviour stands it up.
    /// </summary>
    internal static class Idle
    {
        private static readonly AccessTools.FieldRef<Character, ZSyncAnimation> Anim = AccessTools.FieldRefAccess<Character, ZSyncAnimation>("m_zanim");
        private static readonly Dictionary<Chair, Humanoid> Seats = new Dictionary<Chair, Humanoid>();
        private static readonly int Roof = LayerMask.GetMask("Default", "static_solid", "piece");
        private static readonly List<Piece> Near = new List<Piece>();

        // Your emote: its answer.
        private static readonly Dictionary<string, string> Answer = new Dictionary<string, string>
        {
            ["wave"] = "wave", ["cheer"] = "cheer", ["thumbsup"] = "thumbsup", ["laugh"] = "laugh", ["dance"] = "dance", ["bow"] = "bow",
            ["flex"] = "flex", ["toast"] = "toast", ["challenge"] = "cheer", ["roar"] = "roar", ["headbang"] = "headbang", ["vibe"] = "vibe",
            ["blowkiss"] = "laugh", ["loveyou"] = "thumbsup", ["shrug"] = "shrug", ["nonono"] = "shrug", ["kneel"] = "bow", ["cower"] = "laugh",
            ["cry"] = "comehere", ["despair"] = "thumbsup", ["comehere"] = "thumbsup",
        };

        public static void Forget()
        {
            foreach (Humanoid c in Companion.All().ToList())
                if (c.GetComponent<ZNetView>().IsOwner()) Stand(Brain.Get(c));
            Seats.Clear();
        }

        // ---- every frame ---------------------------------------------------------------------------------

        /// <summary>Every frame on the game running it: up when nothing keeps it sitting; your return and your emotes.</summary>
        public static void Tick(BrainState st, Player master)
        {
            Humanoid me = st.Body;
            if (!st.IdleReady)
            {
                // A pose saved from before (its area reloaded while it sat or slept): up.
                st.IdleReady = true;
                ZSyncAnimation z = Anim(me);
                z?.SetBool("emote_sit", false); z?.SetBool("attach_chair", false); z?.SetBool("attach_bed", false);
            }
            if ((st.Sitting || st.SitChair != null) && (Time.time > st.SitKeep || st.Enemies.Count > 0 || me.GetMoveDir().sqrMagnitude > 0.01f)) Stand(st); // (on its way somewhere: up at once)
            if (me.GetVelocity().sqrMagnitude > 0.1f) st.StillSince = Time.time;
            Greet(st, master);
            Mirror(st, master);
        }

        // ---- emotes --------------------------------------------------------------------------------------

        /// <summary>One of the game's emotes (wave, cheer, thumbsup...), standing still for its moment. False when it cannot now.</summary>
        public static bool Emote(BrainState st, string emote)
        {
            Humanoid me = st.Body;
            if (me == null || st.Asleep || st.Riding != null || st.InCombat || me.InAttack() || me.IsAttached() || !me.IsOnGround() || me.InWater()) return false;
            Stand(st);
            st.Ai.StopMoving();
            Anim(me)?.SetTrigger("emote_" + emote);
            st.EmoteUntil = Time.time + 2.2f;
            Activity.Log(me, "emote: " + emote);
            return true;
        }

        /// <summary>An emote in a moment (when it is free; dropped if it stays busy for a few seconds), facing someone.</summary>
        public static void Queue(BrainState st, string emote, float delay = 0.6f, Character face = null)
        {
            if (st == null) return;
            st.PendingEmote = emote;
            st.PendingEmoteAt = Time.time + delay;
            st.EmoteFace = face;
        }

        /// <summary>Out of a fight: true while an emote plays (it stands for it and the rest waits).</summary>
        public static bool Emoting(BrainState st, Action<Vector3> lookAt)
        {
            if (st.PendingEmote != null && Time.time >= st.PendingEmoteAt)
            {
                string e = st.PendingEmote;
                st.PendingEmote = null;
                if (Time.time - st.PendingEmoteAt < 4f) Emote(st, e);
            }
            if (Time.time >= st.EmoteUntil) return false;
            st.Ai.StopMoving();
            if (st.EmoteFace != null && !st.EmoteFace.IsDead()) lookAt(st.EmoteFace.GetHeadPoint());
            return true;
        }

        /// <summary>Back after a while away (two minutes or more, out of sight): a wave and a word.</summary>
        private static void Greet(BrainState st, Player master)
        {
            if (Time.time < st.NextGreetLook) return;
            st.NextGreetLook = Time.time + 1f;
            Humanoid me = st.Body;
            float d = master != null ? Vector3.Distance(master.transform.position, me.transform.position) : float.MaxValue;
            if (d > 60f) { if (st.AwaySince == 0f) st.AwaySince = Time.time; return; }
            if (d > 20f || st.AwaySince == 0f) return;
            bool longAway = Time.time - st.AwaySince > 120f;
            st.AwaySince = 0f;
            if (!longAway || st.InCombat || st.Asleep) return;
            Queue(st, "wave", 0.4f, master);
            Banter.Say(me, "welcome", 0f, $"Welcome back, {master.GetPlayerName()}!", "There you are!", "Good to see you again.", "Back safe, I see.");
            st.Remember("you came back: it waved");
        }

        /// <summary>Your emotes (the game saves them on your character for everyone to see): it answers, within 12 m.</summary>
        private static void Mirror(BrainState st, Player master)
        {
            ZDO z = master != null ? master.GetComponent<ZNetView>()?.GetZDO() : null;
            if (z == null) return;
            int id = z.GetInt(ZDOVars.s_emoteID, 0);
            if (st.YourEmoteId == int.MinValue || id == st.YourEmoteId) { st.YourEmoteId = id; return; }
            st.YourEmoteId = id;
            string e = z.GetString(ZDOVars.s_emote, "");
            if (e.Length == 0 || st.InCombat || st.Asleep || Vector3.Distance(master.transform.position, st.Body.transform.position) > 12f) return;
            if (e == "comehere") st.ComeUntil = Time.time + 20f;
            if (!Answer.TryGetValue(e, out string reply)) return; // (sitting, resting: it sits with you, WithYou)
            Queue(st, reply, 0.7f, master);
            if (e == "cry" || e == "despair") Banter.Say(st.Body, "cheerup", 1f, "Chin up. We'll get through this.", "Hey, it's not so bad.", "Come on, I'll cook something.");
            st.Remember($"you did a {e}: it answered with a {reply}");
        }

        /// <summary>You beckoned ("come here"): it comes over and stays by you for a little while, then goes back to what it was doing.</summary>
        public static bool Come(BrainState st, Player master, Action<Vector3, float, bool> moveTo, Action stop, Action<Vector3> lookAt)
        {
            if (master == null || Time.time >= st.ComeUntil) return false;
            float d = Vector3.Distance(master.transform.position, st.Body.transform.position);
            if (d > 40f) { st.ComeUntil = 0f; return false; }
            if (d > 2.2f) moveTo(master.transform.position, 1.6f, d > 8f);
            else { stop(); lookAt(master.GetHeadPoint()); }
            Brain.Status(st, d > 2.2f ? "coming over to you" : "here with you");
            return true;
        }

        // ---- sitting -------------------------------------------------------------------------------------

        /// <summary>Sits down on the ground (the game's sit), or stays sitting.</summary>
        public static void SitDown(BrainState st)
        {
            if (st.SitChair != null) { HoldChair(st); return; }
            st.SitKeep = Time.time + 0.3f;
            if (st.Sitting) return;
            Humanoid me = st.Body;
            if (!me.IsOnGround() || me.InWater() || me.InAttack() || me.IsAttached()) return;
            st.Ai.StopMoving();
            Anim(me)?.SetBool("emote_sit", true);
            st.Sitting = true;
        }

        private static bool Free(Chair chair, Humanoid me) =>
            chair != null && !chair.m_inShip && chair.m_attachPoint != null && !chair.IsInUse() && (!Seats.TryGetValue(chair, out Humanoid who) || who == null || who == me);

        /// <summary>Sits in the chair (or stays in it), as a player does. False when it is taken.</summary>
        public static bool TakeChair(BrainState st, Chair chair)
        {
            Humanoid me = st.Body;
            if (st.SitChair == chair) { HoldChair(st); return true; }
            if (!Free(chair, me)) return false;
            Stand(st);
            Seats[chair] = me;
            st.SitChair = chair;
            st.Ai.StopMoving();
            Anim(me)?.SetBool(chair.m_attachAnimation, true);
            Rigidbody body = me.GetComponent<Rigidbody>();
            if (body != null) { body.linearVelocity = Vector3.zero; body.isKinematic = true; }
            HoldChair(st);
            return true;
        }

        private static void HoldChair(BrainState st)
        {
            Chair chair = st.SitChair;
            if (chair == null) { Stand(st); return; }
            st.SitKeep = Time.time + 0.3f;
            Transform at = chair.m_attachPoint;
            st.Body.transform.SetPositionAndRotation(at.position, at.rotation);
            Rigidbody body = st.Body.GetComponent<Rigidbody>();
            if (body != null) { body.position = at.position; body.rotation = at.rotation; } // (kinematic: no velocity to clear)
            Anim(st.Body)?.SetBool(chair.m_attachAnimation, true); // kept up (free when already set)
        }

        /// <summary>Up (from the ground or out of its chair).</summary>
        public static void Stand(BrainState st)
        {
            Humanoid me = st?.Body;
            if (me == null) return;
            ZSyncAnimation z = Anim(me);
            if (st.Sitting) { z?.SetBool("emote_sit", false); z?.SetTrigger("emote_stop"); st.Sitting = false; }
            if (st.SitChair != null || Seats.Values.Contains(me))
            {
                Chair chair = st.SitChair;
                st.SitChair = null;
                foreach (Chair ch in Seats.Where(kv => kv.Value == me).Select(kv => kv.Key).ToList()) Seats.Remove(ch);
                if (chair != null)
                {
                    z?.SetBool(chair.m_attachAnimation, false);
                    me.transform.position = chair.m_attachPoint.position + Flat(chair.m_attachPoint.forward) * 0.8f + Vector3.up * 0.2f; // up, in front of it
                }
                Rigidbody body = me.GetComponent<Rigidbody>();
                if (body != null && !st.Asleep && st.Riding == null) body.isKinematic = false;
            }
        }

        private static Vector3 Flat(Vector3 v) { v.y = 0f; return v.sqrMagnitude > 0.0001f ? v.normalized : Vector3.forward; }

        // ---- out with you --------------------------------------------------------------------------------

        /// <summary>
        /// Following and close to you, nothing going on: it faces you when you stop, sits when you sit (a free chair beside you if there is
        /// one) or after you have stood about for a while. True when it is busy with that (going to a chair, sitting).
        /// </summary>
        public static bool WithYou(BrainState st, Player master, float d, Action<Vector3, float, bool> moveTo, Action<Vector3> lookAt)
        {
            Humanoid me = st.Body;
            Vector3 you = master.transform.position;
            if (Vector3.Distance(you, st.YouWere) > 0.6f) { st.YouWere = you; st.YouStillSince = Time.time; }
            float still = Time.time - st.YouStillSince;
            bool youSit = master.IsSitting() || (master.IsAttached() && !master.InBed() && !master.IsAttachedToShip());
            bool sit = d < 8f && (youSit ? still > 1.5f : still > 25f && master.IsOnGround());
            if (sit)
            {
                if (st.SitChair != null) { HoldChair(st); Brain.Status(st, "sitting with you"); return true; }
                if (!st.Sitting)
                {
                    Chair chair = youSit ? FreeChairs(you, 4f, me).FirstOrDefault() : null;
                    if (chair != null)
                    {
                        float cd = Vector3.Distance(chair.m_attachPoint.position, me.transform.position);
                        if (cd > 1.3f) { moveTo(chair.m_attachPoint.position, 0.6f, false); Brain.Status(st, "taking a seat by you"); return true; }
                        if (TakeChair(st, chair)) { Brain.Status(st, "sitting with you"); return true; }
                    }
                    if (d > 3.5f) return false; // (closer first)
                }
                SitDown(st);
                lookAt(master.GetHeadPoint());
                Brain.Status(st, youSit ? "sitting with you" : "taking a rest while you look around");
                return true;
            }
            if (d < 6f && still > 2f) lookAt(master.GetHeadPoint());
            return false;
        }

        /// <summary>Told to stay: after half a minute standing it sits down where it is.</summary>
        public static void Waiting(BrainState st)
        {
            if (Time.time - st.StillSince > 30f) SitDown(st);
        }

        // ---- at home with nothing to do --------------------------------------------------------------------

        /// <summary>Living at home with no job: by the fire, in a chair, out of the rain, or a stroll. False when it has nothing better than the middle of its home.</summary>
        public static bool AtHome(BrainState st, Player master, Vector3 center, float radius, Action<Vector3, float, bool> moveTo, Action stop, Action<Vector3> lookAt)
        {
            Humanoid me = st.Body;
            if (Time.time >= st.IdleUntil || (st.IdlePlan != IdlePlan.None && !StillGood(st))) Pick(st, center, radius);
            string note = st.WorkNote != null ? $" ({st.WorkNote})" : "";
            bool youNear = master != null && Vector3.Distance(master.transform.position, me.transform.position) < 8f;
            switch (st.IdlePlan)
            {
                case IdlePlan.Chair:
                {
                    Chair chair = st.IdleChair;
                    float d = Vector3.Distance(chair.m_attachPoint.position, me.transform.position);
                    string name = Localization.instance.Localize(chair.m_name).ToLowerInvariant();
                    if (st.SitChair != chair && d > 1.3f) { moveTo(chair.m_attachPoint.position, 0.6f, d > 12f); Brain.Status(st, $"going to sit on the {name}{note}"); return true; }
                    if (!TakeChair(st, chair)) { st.IdlePlan = IdlePlan.None; st.IdleUntil = 0f; return true; } // (taken meanwhile: something else)
                    Brain.Status(st, (st.IdleFire != null ? $"sitting by the fire on the {name}" : $"sitting on the {name}") + note);
                    return true;
                }
                case IdlePlan.Fire:
                {
                    float d = Vector3.Distance(st.IdleAt, me.transform.position);
                    if (!st.Sitting && d > 0.8f) { moveTo(st.IdleAt, 0.4f, d > 12f); Brain.Status(st, "going to the fire" + note); return true; }
                    stop();
                    lookAt(st.IdleFire.transform.position + Vector3.up * 0.5f);
                    SitDown(st);
                    bool cold = EnvMan.IsCold() || EnvMan.IsWet() || me.GetSEMan().HaveStatusEffect(SEMan.s_statusEffectWet) || Evening();
                    Brain.Status(st, (cold ? "warming itself by the fire" : "resting by the fire") + note);
                    return true;
                }
                case IdlePlan.Shelter:
                {
                    float d = Vector3.Distance(st.IdleAt, me.transform.position);
                    if (d > 0.8f) { moveTo(st.IdleAt, 0.4f, true); Brain.Status(st, "getting out of the rain" + note); return true; }
                    stop();
                    if (youNear) lookAt(master.GetHeadPoint()); else LookAround(st, lookAt);
                    if (Time.time - st.StillSince > 15f) SitDown(st);
                    Brain.Status(st, "keeping out of the rain" + note);
                    return true;
                }
                case IdlePlan.Stroll:
                {
                    float d = Vector3.Distance(st.IdleAt, me.transform.position);
                    if (d > 1f && Time.time - st.IdleSince < 25f) { moveTo(st.IdleAt, 0.6f, false); Brain.Status(st, "taking a stroll" + note); return true; }
                    stop();
                    if (youNear) lookAt(master.GetHeadPoint()); else LookAround(st, lookAt);
                    Brain.Status(st, "looking about" + note);
                    return true;
                }
            }
            return false;
        }

        private static bool Evening() => EnvMan.instance != null && (EnvMan.IsNight() || EnvMan.instance.GetDayFraction() > 0.72f);

        private static bool StillGood(BrainState st)
        {
            switch (st.IdlePlan)
            {
                case IdlePlan.Fire: return st.IdleFire != null && st.IdleFire.IsBurning();
                case IdlePlan.Chair: return st.IdleChair != null && (st.SitChair == st.IdleChair || Free(st.IdleChair, st.Body)) && (st.IdleFire == null || st.IdleFire.IsBurning());
                case IdlePlan.Shelter: return EnvMan.IsWet();
                case IdlePlan.Stroll: return !EnvMan.IsWet() || Roofed(st.Body.transform.position);
            }
            return false;
        }

        /// <summary>What to do for the next minute or so.</summary>
        private static void Pick(BrainState st, Vector3 center, float radius)
        {
            Humanoid me = st.Body;
            st.IdlePlan = IdlePlan.None; st.IdleFire = null; st.IdleChair = null; st.IdleSince = Time.time;
            st.IdleUntil = Time.time + 5f; // (nothing found: another look in a few seconds, not every frame)
            bool rain = EnvMan.IsWet();
            bool cold = EnvMan.IsCold() || Evening() || me.GetSEMan().HaveStatusEffect(SEMan.s_statusEffectWet) || me.GetSEMan().HaveStatusEffect(SEMan.s_statusEffectCold);
            Fireplace fire = Fires(center, radius).OrderBy(f => Vector3.Distance(f.transform.position, me.transform.position)).FirstOrDefault(f => !rain || Roofed(f.transform.position));
            float roll = UnityEngine.Random.value;
            if (fire != null && (cold || rain || roll < 0.45f))
            {
                Chair byFire = FreeChairs(fire.transform.position, 4f, me).FirstOrDefault(ch => !rain || Roofed(ch.m_attachPoint.position));
                if (byFire != null && UnityEngine.Random.value < 0.6f) { Plan(st, IdlePlan.Chair, byFire.m_attachPoint.position, 60f, 120f); st.IdleChair = byFire; st.IdleFire = fire; return; }
                Vector3? spot = BySide(fire, me);
                if (spot != null) { Plan(st, IdlePlan.Fire, spot.Value, 60f, 120f); st.IdleFire = fire; return; }
            }
            if (rain && !Roofed(me.transform.position))
            {
                Vector3? dry = Dry(center, radius, me);
                if (dry != null) { Plan(st, IdlePlan.Shelter, dry.Value, 40f, 60f); return; }
            }
            if (!rain && roll < 0.7f)
            {
                Chair chair = FreeChairs(center, radius, me).OrderBy(ch => UnityEngine.Random.value).FirstOrDefault();
                if (chair != null) { Plan(st, IdlePlan.Chair, chair.m_attachPoint.position, 45f, 90f); st.IdleChair = chair; return; }
            }
            if (rain && Roofed(me.transform.position)) { Plan(st, IdlePlan.Shelter, me.transform.position, 40f, 60f); return; }
            for (int i = 0; i < 8; i++)
            {
                Vector2 r = UnityEngine.Random.insideUnitCircle * radius * 0.6f;
                Vector3 p = center + new Vector3(r.x, 0f, r.y);
                if (!Ground(ref p, center.y, 4f) || !Steer.Safe(p, me)) continue;
                Plan(st, IdlePlan.Stroll, p, 25f, 45f);
                return;
            }
        }

        private static void Plan(BrainState st, IdlePlan plan, Vector3 at, float min, float max)
        {
            st.IdlePlan = plan;
            st.IdleAt = at;
            st.IdleUntil = Time.time + UnityEngine.Random.Range(min, max);
            Activity.Log(st.Body, $"between jobs: {plan.ToString().ToLowerInvariant()}");
        }

        private static void LookAround(BrainState st, Action<Vector3> lookAt)
        {
            if (Time.time >= st.NextLookAround)
            {
                st.NextLookAround = Time.time + UnityEngine.Random.Range(3f, 7f);
                Vector2 r = UnityEngine.Random.insideUnitCircle.normalized * 10f;
                st.LookAroundAt = st.Body.transform.position + new Vector3(r.x, 1.5f, r.y);
            }
            lookAt(st.LookAroundAt);
        }

        // ---- the places ------------------------------------------------------------------------------------

        private static bool Roofed(Vector3 feet) => Physics.Raycast(feet + Vector3.up * 2.1f, Vector3.up, 20f, Roof);

        /// <summary>Burning fires that warm (campfires, hearths, braziers: not torches) around its home.</summary>
        private static IEnumerable<Fireplace> Fires(Vector3 center, float radius)
        {
            Near.Clear();
            Piece.GetAllPiecesInRadius(center, radius, Near);
            foreach (Piece p in Near)
            {
                Fireplace f = p != null ? p.GetComponent<Fireplace>() : null;
                if (f == null || !f.IsBurning()) continue;
                if (!f.GetComponentsInChildren<EffectArea>().Any(a => (a.m_type & EffectArea.Type.Heat) != 0)) continue;
                yield return f;
            }
        }

        private static List<Chair> FreeChairs(Vector3 center, float radius, Humanoid me)
        {
            Near.Clear();
            Piece.GetAllPiecesInRadius(center, radius, Near);
            return Near.Where(p => p != null).SelectMany(p => p.GetComponentsInChildren<Chair>()).Where(ch => Free(ch, me))
                       .OrderBy(ch => Vector3.Distance(ch.m_attachPoint.position, center)).ToList();
        }

        /// <summary>A spot two or three metres from the fire, on its level, not in the fire's reach (its nearest side to it first).</summary>
        private static Vector3? BySide(Fireplace fire, Humanoid me)
        {
            Vector3 c = fire.transform.position;
            Vector3 toMe = Flat(me.transform.position - c);
            foreach (float dist in new[] { 2.2f, 2.8f, 3.4f })
                for (int i = 0; i < 8; i++)
                {
                    Vector3 p = c + Quaternion.Euler(0f, (i % 2 == 0 ? 1 : -1) * (i + 1) / 2 * 45f, 0f) * toMe * dist;
                    if (!Ground(ref p, c.y, 1f) || !Steer.Safe(p, me)) continue;
                    if (Physics.CheckCapsule(p + Vector3.up * 0.6f, p + Vector3.up * 1.6f, 0.35f, Roof)) continue; // a wall, a post
                    return p;
                }
            return null;
        }

        /// <summary>A roofed spot around its home (nearest to the middle first).</summary>
        private static Vector3? Dry(Vector3 center, float radius, Humanoid me)
        {
            for (float ring = 0f; ring <= radius; ring += 3f)
                for (int i = 0; i < (ring == 0f ? 1 : 12); i++)
                {
                    Vector3 p = center + Quaternion.Euler(0f, i * 30f, 0f) * Vector3.forward * ring;
                    if (!Ground(ref p, center.y, 6f) || !Roofed(p) || !Steer.Safe(p, me)) continue;
                    if (Physics.CheckCapsule(p + Vector3.up * 0.6f, p + Vector3.up * 1.6f, 0.35f, Roof)) continue;
                    return p;
                }
            return null;
        }

        /// <summary>Puts the spot on the ground or floor there (false when that is far above or below the given height).</summary>
        private static bool Ground(ref Vector3 p, float near, float within)
        {
            p.y = near; // (cast from just above that height: from high up it would land on a roof)
            if (ZoneSystem.instance == null || !ZoneSystem.instance.GetSolidHeight(p, out float h, 2)) return false;
            if (Mathf.Abs(h - near) > within) return false;
            p.y = h;
            return true;
        }
    }

    /// <summary>
    /// A companion held in place (asleep in its bed, in a chair, in a boat seat: its body made kinematic and put on the spot every frame) skips
    /// the game's walking step, which sets every character's velocity each physics step: on a kinematic body Unity warns about it every time
    /// (thousands of "Setting linear velocity of a kinematic body is not supported" in the log). Nothing else of the physics step is skipped.
    /// </summary>
    [HarmonyPatch(typeof(Character), "UpdateMotion")]
    internal static class Character_UpdateMotion_Held
    {
        private static readonly AccessTools.FieldRef<Character, Rigidbody> Body = AccessTools.FieldRefAccess<Character, Rigidbody>("m_body");

        private static bool Prefix(Character __instance)
        {
            Rigidbody body = Body(__instance);
            return body == null || !body.isKinematic || !Companion.Is(__instance);
        }
    }
}
