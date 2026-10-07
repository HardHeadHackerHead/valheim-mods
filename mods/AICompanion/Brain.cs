using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace AICompanion
{
    public enum Tactic { Attack, DefendPlayer, BackOff, Retreat, Flee }

    /// <summary>One decision: what to do, against whom, with what. From Jev or from the built-in brain.</summary>
    internal class Decision
    {
        public Tactic Action = Tactic.Attack;
        public Character Target;
        public bool Ranged;
        public bool Drink;
        public float Confidence = 1f;
        public bool FromJev;
        public string Note = "";
        public float Time;
        public bool AskedJev;                                    // for the Debug tab: what was sent and what came back
        public string Request = "", Response = "", Error = "";
        public float Ms = -1f;
        public List<string> Answers = new List<string>();

        public string Describe(Func<Character, string> label)
        {
            string what = Action switch
            {
                Tactic.Attack => "attack " + (Target != null ? label(Target) : "nearest"),
                Tactic.DefendPlayer => "defend you",
                Tactic.BackOff => "back off",
                Tactic.Retreat => "fall back to you",
                _ => "flee",
            };
            if (Ranged && (Action == Tactic.Attack || Action == Tactic.DefendPlayer)) what += " (bow)";
            if (Drink) what += ", drink";
            return what + (FromJev ? $"  ·  Jev {Confidence * 100f:0}%" : "  ·  built-in");
        }
    }

    /// <summary>What each companion is doing, kept by whichever game runs it (its ZDO owner).</summary>
    internal class BrainState
    {
        public Humanoid Body;
        public MonsterAI Ai;
        public Decision Current = new Decision();
        public readonly List<Character> Enemies = new List<Character>();
        public readonly Dictionary<Character, string> Labels = new Dictionary<Character, string>();
        public readonly List<string> History = new List<string>(); // newest first, for the menu
        public bool InCombat, Asking;
        public int Fights, Potions, JevCalls, BuiltInCalls;     // this session, for the Overview tab
        public float NextEnemyScan, NextGear, NextAsk, LastAsk, HealthAtAsk = 1f;
        public int EnemyCountAtAsk;
        public string Status = "";

        public string Label(Character c)
        {
            if (c == null) return "?";
            if (Labels.TryGetValue(c, out string l)) return l;
            string name = Localization.instance.Localize(c.m_name);
            int n = 1;
            while (Labels.ContainsValue($"{name} {n}")) n++;
            return Labels[c] = $"{name} {n}";
        }

        public void Remember(string line)
        {
            History.Insert(0, line);
            if (History.Count > 8) History.RemoveAt(History.Count - 1);
        }
    }

    /// <summary>
    /// The companion's every frame, in place of the game's monster AI (Patches: MonsterAI_UpdateAI). Out of a fight it follows you, stays
    /// put or guards a spot. In a fight it carries out the current decision with the game's own movement and attacks, and asks for a new
    /// decision every few seconds or at once when something big happens (a new enemy, its target dead, a big hit).
    /// </summary>
    internal static class Brain
    {
        private static readonly Dictionary<Humanoid, BrainState> States = new Dictionary<Humanoid, BrainState>();

        private static readonly Func<BaseAI, float, Vector3, float, bool, bool> MoveToRaw = AccessTools.MethodDelegate<Func<BaseAI, float, Vector3, float, bool, bool>>(AccessTools.Method(typeof(BaseAI), "MoveTo"));
        private static readonly Action<BaseAI, Vector3> LookAt = AccessTools.MethodDelegate<Action<BaseAI, Vector3>>(AccessTools.Method(typeof(BaseAI), "LookAt"));
        private static readonly Action<BaseAI, float> Regenerate = AccessTools.MethodDelegate<Action<BaseAI, float>>(AccessTools.Method(typeof(BaseAI), "UpdateRegeneration"));
        private static readonly AccessTools.FieldRef<BaseAI, float> TimeSinceHurt = AccessTools.FieldRefAccess<BaseAI, float>("m_timeSinceHurt");
        private static readonly AccessTools.FieldRef<Character, bool> Blocking = AccessTools.FieldRefAccess<Character, bool>("m_blocking");
        private static readonly AccessTools.FieldRef<Humanoid, float> DrawTime = AccessTools.FieldRefAccess<Humanoid, float>("m_attackDrawTime");

        /// <summary>The game's MoveTo, but it only runs while it has the stamina for it.</summary>
        private static bool MoveTo(BaseAI ai, float dt, Vector3 point, float dist, bool run) =>
            MoveToRaw(ai, dt, point, dist, run && Stamina.CanRun(ai.GetComponent<Character>()));

        public static void Forget()
        {
            foreach (BrainState st in States.Values) if (st.Body != null) Blocking(st.Body) = false;
            States.Clear();
        }

        public static BrainState Get(Humanoid h)
        {
            if (h == null) return null;
            if (!States.TryGetValue(h, out BrainState st))
            {
                foreach (Humanoid gone in States.Keys.Where(k => k == null).ToList()) States.Remove(gone);
                States[h] = st = new BrainState { Body = h, Ai = h.GetComponent<MonsterAI>() };
            }
            return st;
        }

        /// <summary>Runs on the companion's owner only; returns what the game's UpdateAI would.</summary>
        public static bool Update(MonsterAI ai, float dt)
        {
            ZNetView view = ai.GetComponent<ZNetView>();
            if (view == null || !view.IsValid() || !view.IsOwner()) return false;
            Regenerate(ai, dt);
            TimeSinceHurt(ai) += dt;
            Stamina.Tick(ai.GetComponent<Humanoid>(), dt);

            Humanoid me = ai.GetComponent<Humanoid>();
            BrainState st = Get(me);
            Player master = Companion.Master(me);
            Container gear = me.GetComponent<Container>();

            if (Time.time >= st.NextGear) { st.NextGear = Time.time + 0.5f; Companion.Maintain(me, st.Current.Ranged); }
            if (gear != null && gear.IsInUse()) { ai.StopMoving(); Blocking(me) = false; SetStatus(st, "waiting while you sort its gear"); return true; }

            if (Time.time >= st.NextEnemyScan) { st.NextEnemyScan = Time.time + 0.25f; ScanEnemies(st, master); }
            st.Enemies.RemoveAll(e => e == null || e.IsDead()); // killed or gone since the last look (a destroyed one throws on .transform)

            if (st.Enemies.Count == 0)
            {
                if (st.InCombat) { st.InCombat = false; st.Labels.Clear(); st.Remember("fight over"); }
                Blocking(me) = false;
                Peaceful(st, master, dt);
                return true;
            }

            if (!st.InCombat) { st.InCombat = true; st.NextAsk = 0f; st.Fights++; }
            MaybeDecide(st, master);
            Fight(st, master, dt);
            return true;
        }

        // ---- out of a fight ------------------------------------------------------------------------------

        private static void Peaceful(BrainState st, Player master, float dt)
        {
            Humanoid me = st.Body;
            switch (Companion.OrderOf(me))
            {
                case Order.Stay:
                    st.Ai.StopMoving();
                    SetStatus(st, "staying here");
                    break;
                case Order.Guard:
                    Vector3 post = Companion.Zdo(me).GetVec3(Keys.Post, me.transform.position);
                    if (Vector3.Distance(post, me.transform.position) > 2f) MoveTo(st.Ai, dt, post, 1f, Vector3.Distance(post, me.transform.position) > 8f);
                    else st.Ai.StopMoving();
                    SetStatus(st, "guarding");
                    break;
                default:
                    if (master == null) { st.Ai.StopMoving(); SetStatus(st, "waiting for " + (Companion.Zdo(me).GetString(Keys.MasterName, "its friend"))); break; }
                    float d = Vector3.Distance(master.transform.position, me.transform.position);
                    if (d > 60f && !master.IsAttached() && master.IsOnGround()) { TeleportBehind(me, master); break; } // left behind (a portal, a boat ride)
                    if (d > 3.5f) MoveTo(st.Ai, dt, master.transform.position, 2.5f, d > 8f);
                    else st.Ai.StopMoving();
                    SetStatus(st, "following " + master.GetPlayerName());
                    break;
            }
        }

        internal static void TeleportBehind(Humanoid me, Player master)
        {
            Vector3 pos = master.transform.position - master.transform.forward * 2.5f;
            if (ZoneSystem.instance != null && ZoneSystem.instance.GetSolidHeight(pos, out float h)) pos.y = Mathf.Max(pos.y, h);
            me.transform.position = pos;
            Rigidbody body = me.GetComponent<Rigidbody>();
            if (body != null) { body.position = pos; body.linearVelocity = Vector3.zero; }
            Plugin.Instance?.Note($"{Companion.NameOf(me)} caught up with {master.GetPlayerName()}");
        }

        // ---- the fight ----------------------------------------------------------------------------------

        private static void ScanEnemies(BrainState st, Player master)
        {
            Humanoid me = st.Body;
            float range = Plugin.EngageRange.Value;
            Style style = Companion.StyleOf(me);
            st.Enemies.Clear();
            foreach (Character c in Character.GetAllCharacters())
            {
                if (c == null || c == me || c.IsDead() || c.IsPlayer() || !BaseAI.IsEnemy(me, c)) continue;
                if (c.GetComponent<BaseAI>() == null) continue;
                float toMe = Vector3.Distance(c.transform.position, me.transform.position);
                float toMaster = master != null ? Vector3.Distance(c.transform.position, master.transform.position) : float.MaxValue;
                float limit = style == Style.Defensive ? range * 0.6f : range;
                if (toMe < limit || toMaster < limit) st.Enemies.Add(c);
            }
            st.Enemies.Sort((a, b) => Vector3.Distance(a.transform.position, me.transform.position).CompareTo(Vector3.Distance(b.transform.position, me.transform.position)));
        }

        private static void MaybeDecide(BrainState st, Player master)
        {
            float now = Time.time;
            Humanoid me = st.Body;
            bool targetGone = st.Current.Target != null && (st.Current.Target.IsDead() || !st.Enemies.Contains(st.Current.Target));
            bool bigHit = st.HealthAtAsk - me.GetHealthPercentage() > 0.2f;
            bool newEnemy = st.Enemies.Count > st.EnemyCountAtAsk;
            bool urgent = targetGone || bigHit || newEnemy;
            if (st.Asking || now < st.NextAsk && !(urgent && now - st.LastAsk > 0.5f)) return;

            st.LastAsk = now;
            st.NextAsk = now + Plugin.DecisionSeconds.Value;
            st.HealthAtAsk = me.GetHealthPercentage();
            st.EnemyCountAtAsk = st.Enemies.Count;

            Decision fallback = BuiltIn(st, master);
            if (targetGone) Apply(st, fallback); // never keep swinging at nothing while Jev thinks
            if (Plugin.UseJev.Value && Companion.UsesJev(me) && !string.IsNullOrEmpty(Plugin.ApiKey.Value) && Jev.Ready)
            {
                st.Asking = true;
                Plugin.Instance.StartCoroutine(Jev.Decide(st, master, fallback, d =>
                {
                    st.Asking = false;
                    if (st.Body != null) Apply(st, d);
                }));
            }
            else Apply(st, fallback);
        }

        private static void Apply(BrainState st, Decision d)
        {
            d.Time = Time.time;
            bool changed = d.Action != st.Current.Action || d.Target != st.Current.Target || d.Ranged != st.Current.Ranged;
            st.Current = d;
            if (d.Drink && Companion.Drink(st.Body)) { st.Remember("drank a healing potion"); st.Potions++; }
            string line = d.Describe(st.Label);
            if (d.FromJev) st.JevCalls++; else st.BuiltInCalls++;
            DebugLog.Add(new DecisionRecord
            {
                When = DateTime.Now, Companion = Companion.NameOf(st.Body), Outcome = line, Note = d.Note, FromJev = d.FromJev, AskedJev = d.AskedJev,
                Confidence = d.Confidence, Ms = d.Ms, Error = d.Error, Request = d.Request, Response = d.Response, Answers = d.Answers,
                Enemies = string.Join(", ", st.Enemies.Where(e => e != null).Select(st.Label)),
            });
            if (changed || d.Drink) st.Remember(line + (string.IsNullOrEmpty(d.Note) ? "" : "  (" + d.Note + ")"));
            SetStatus(st, line);
            if (changed && Plugin.ShowDecisions.Value && Chat.instance != null)
                Chat.instance.SetNpcText(st.Body.gameObject, Vector3.up * 2.3f, 25f, 2.5f, "", line, false);
        }

        /// <summary>The built-in brain: retreat when low, otherwise protect the player first (if asked) and hit the nearest.</summary>
        public static Decision BuiltIn(BrainState st, Player master)
        {
            Humanoid me = st.Body;
            Style style = Companion.StyleOf(me);
            float health = me.GetHealthPercentage();
            float retreat = Companion.RetreatOf(me) / 100f * (style == Style.Aggressive ? 0.5f : 1f);
            var d = new Decision { Note = "" };
            d.Drink = Companion.Potions(me) && health < 0.5f && Companion.HealingPotions(me).Count > 0;
            Character nearest = st.Enemies.FirstOrDefault();
            if (style == Style.Passive) { d.Action = health < retreat ? Tactic.Flee : Tactic.Retreat; return d; }
            if (health < retreat * 0.5f) { d.Action = Tactic.Flee; d.Note = "nearly dead"; return d; }
            if (health < retreat) { d.Action = Tactic.Retreat; d.Note = "hurt"; return d; }
            if (Stamina.Get(me) < Stamina.Max(me) * 0.15f && nearest != null && Vector3.Distance(nearest.transform.position, me.transform.position) < 6f)
            { d.Action = Tactic.BackOff; d.Note = "out of breath"; return d; }
            Character onMaster = master != null && Companion.Protect(me) ? st.Enemies.FirstOrDefault(e => e.GetBaseAI() is MonsterAI m && m.GetTargetCreature() == master) : null;
            d.Action = Tactic.Attack;
            d.Target = onMaster ?? nearest;
            Character t = d.Target;
            d.Ranged = Companion.BestRanged(me) != null && t != null && (Companion.BestMelee(me) == null || Vector3.Distance(t.transform.position, me.transform.position) > 10f);
            return d;
        }

        private static void Fight(BrainState st, Player master, float dt)
        {
            Humanoid me = st.Body;
            Decision d = st.Current;
            Character nearest = st.Enemies.FirstOrDefault();
            switch (d.Action)
            {
                case Tactic.Attack:
                    Strike(st, d.Target != null && !d.Target.IsDead() && st.Enemies.Contains(d.Target) ? d.Target : nearest, dt);
                    break;
                case Tactic.DefendPlayer:
                    Character threat = master == null ? nearest : st.Enemies.OrderBy(e => Vector3.Distance(e.transform.position, master.transform.position)).First();
                    if (master != null && Vector3.Distance(me.transform.position, master.transform.position) > 8f && Vector3.Distance(threat.transform.position, master.transform.position) > 6f)
                    { Blocking(me) = false; MoveTo(st.Ai, dt, master.transform.position, 3f, true); }
                    else Strike(st, threat, dt);
                    break;
                case Tactic.BackOff:
                    Guarded(st, nearest, me.transform.position + (me.transform.position - nearest.transform.position).normalized * 4f, false, dt);
                    break;
                case Tactic.Retreat:
                    Vector3 to = master != null ? master.transform.position : me.transform.position + (me.transform.position - nearest.transform.position).normalized * 6f;
                    Guarded(st, nearest, to, Vector3.Distance(to, me.transform.position) > 6f, dt);
                    break;
                default: // flee: away from them all, towards the player when there is one
                    Blocking(me) = false;
                    Vector3 away = Vector3.zero;
                    foreach (Character e in st.Enemies) away += (me.transform.position - e.transform.position).normalized;
                    Vector3 dir = away.normalized + (master != null ? (master.transform.position - me.transform.position).normalized * 0.5f : Vector3.zero);
                    MoveTo(st.Ai, dt, me.transform.position + dir.normalized * 10f, 0f, true);
                    break;
            }
        }

        /// <summary>Close in on a target and hit it (or shoot it from a distance with a bow).</summary>
        private static void Strike(BrainState st, Character target, float dt)
        {
            Humanoid me = st.Body;
            Blocking(me) = false;
            if (target == null) return;
            ItemDrop.ItemData weapon = me.GetCurrentWeapon();
            bool ranged = Companion.IsRanged(weapon) && Companion.HasAmmoFor(me, weapon);
            float reach = ranged ? 22f : Mathf.Max(1.2f, (weapon?.m_shared.m_attack?.m_attackRange ?? 1.5f) * 0.9f);
            float dist = Vector3.Distance(target.transform.position, me.transform.position) - target.GetRadius();
            Vector3 aim = target.GetCenterPoint();

            if (ranged && dist < 4f) { MoveTo(st.Ai, dt, me.transform.position + (me.transform.position - target.transform.position).normalized * 4f, 0f, true); return; }
            if (dist > reach || (ranged && !st.Ai.CanSeeTarget(target))) { MoveTo(st.Ai, dt, target.transform.position, ranged ? reach * 0.7f : reach * 0.6f, dist > 5f); return; }

            st.Ai.StopMoving();
            LookAt(st.Ai, aim);
            float cost = weapon?.m_shared.m_attack?.m_attackStamina ?? 0f;
            if (!me.InAttack() && cost > 0f && Stamina.Get(me) < cost + 0.1f)
            {
                Guarded(st, target, me.transform.position + (me.transform.position - target.transform.position).normalized * 3f, false, dt);
                return;
            }
            if (me.InAttack() || !st.Ai.IsLookingAt(aim, ranged ? 6f : 25f)) return;
            if (me.GetTimeSinceLastAttack() < (ranged ? 1.6f : 0.35f)) return;
            if (ranged) DrawTime(me) = 10f; // a full draw: the AI has no hold-the-button, and an undrawn bow does no damage
            me.StartAttack(target, false);
            if (ranged) DrawTime(me) = 0f;
        }

        /// <summary>Move somewhere with the shield up, facing the threat.</summary>
        private static void Guarded(BrainState st, Character threat, Vector3 to, bool run, float dt)
        {
            Humanoid me = st.Body;
            bool close = threat != null && Vector3.Distance(threat.transform.position, me.transform.position) < 5f;
            Blocking(me) = close && !run && Companion.CanBlock(me);
            if (Vector3.Distance(to, me.transform.position) > 1.5f) MoveTo(st.Ai, dt, to, 1f, run);
            else st.Ai.StopMoving();
            if (threat != null && close) LookAt(st.Ai, threat.GetCenterPoint());
        }

        private static void SetStatus(BrainState st, string status)
        {
            if (st.Status == status) return;
            st.Status = status;
            Companion.Zdo(st.Body)?.Set(Keys.Status, status);
        }
    }
}
