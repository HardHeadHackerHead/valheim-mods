using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace AICompanion
{
    public enum Tactic { Attack, DefendPlayer, BackOff, Retreat, Flee }

    /// <summary>One decision: what to do, against whom, with what.</summary>
    internal class Decision
    {
        public Tactic Action = Tactic.Attack;
        public Character Target;
        public bool Ranged;
        public bool Drink;
        public float Confidence = 1f;
        public string Note = "";
        public float Time;

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
            return what;
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
        public int Fights, Potions;                              // this session, for the Stats tab
        public float FightStart, Taken, Dealt, LastHealth;       // this fight, for its summary
        public int FightKills, FightDecisions;
        // Between jobs (Idle): sitting, emotes, your return and your emotes, what it does at home with nothing to do.
        public bool Sitting, IdleReady;
        public Chair SitChair, IdleChair;
        public Fireplace IdleFire;
        public IdlePlan IdlePlan;
        public float SitKeep, StillSince, AwaySince, NextGreetLook, ComeUntil, EmoteUntil, PendingEmoteAt, YouStillSince, IdleUntil, IdleSince, NextLookAround;
        public string PendingEmote;
        public Character EmoteFace;
        public int YourEmoteId = int.MinValue;
        public Vector3 YouWere, IdleAt, LookAroundAt;
        public TombStone GraveOrdered, GraveOn;
        public readonly List<ItemDrop> PickQueue = new List<ItemDrop>(); // things you pointed it at to pick up (Work.OrderPickUp)
        public float WokeAt = -999f; // up after a fall (Tactics.Careful) // the tombstone you pointed it at; the one it is going to (Grave)
        public Work.Task Task;                                      // gathering
        public CraftingStation RepairAt;                            // repairs
        public float NextRepairLook, RepairSince;
        public Ship Riding;                                         // riding along
        public Chair Seat;
        public Vector3 DeckSpot;
        public float RideLastSeen;
        public ItemDrop.ItemData WorkTool;
        public string WorkNote;
        public Vector3 WorkSpot;
        public float NextWorkLook, NextDoorLook, NextUpgradeLook, YieldUntil;
        public Vector3 YieldTo;
        public Player YieldFrom;
        public bool CaughtUp;                                       // living at home while nobody was there (CatchUp)
        public float NextStamp, NextBagSave, NextGraveLook;
        public readonly List<Loot.Spot> LootSpots = new List<Loot.Spot>();   // where creatures fell, for picking up their drops
        public readonly List<KeyValuePair<ItemDrop, float>> LootDrops = new List<KeyValuePair<ItemDrop, float>>(); // what the world dropped near it
        public Vector3 CookedAt;
        public float NextPassBy;
        public Brain.DoorPlan Door;                                 // the way through a door, when the pathfinding knows none (Brain.MoveTo)
        public Character StallOn;                                   // a fight going nowhere (Brain.Strike): whom, since when, its health then
        public float StallSince, StallHealth;
        public readonly Dictionary<Character, float> LeaveAlone = new Dictionary<Character, float>(); // creatures it could not get at, until when
        public float BlockUntil;
        public float NextBanterLook;                                // small talk (Banter), what it has said
        public bool SaidNight;
        public string LastLand = "", LastBoss = "";
        public bool Asleep;                                         // in bed (Sleep)
        public Bed SleepBed;
        public Vagon Cart;                                          // the cart it pulls (Carts)
        public float CartSince, CartAsked;
        public float ClearUntil;                                    // stepping clear of a falling tree (Work)
        public Vector3 ClearTo;
        public int UnstickStage;                                    // getting unstuck: a step aside, a jump, then a hop to you
        public float SideStepUntil, NextLoanLook;
        public Vector3 SideStepTo;
        public Character Focus;                                     // what you pointed at (Pointing): it goes for it, until when
        public float FocusUntil, CommandUntil;                      // a task you gave it by pointing, until when
        public float InvulnUntil, NextMeadLook, RunBackUntil;       // its roll; meads; hit and run (Tactics)
        public bool HitAndRun;
        public int Blows, FightDodges, ParriesAtStart, BlocksAtStart, PotionsAtStart;
        public readonly HashSet<string> FightFoes = new HashSet<string>();
        public readonly List<string> FightLog = new List<string>();   // its last fights, for the Stats tab
        public float EvadeUntil, NextEvade, EvadeSide, CounterUntil;  // stepping aside, hitting back after a parry (Defense)
        public Vector3 EvadeTo;
        public int Parries, Blocks;                                    // holding its block a moment after a swing (Brain.Strike)
        public Character Blocker;
        public float NextSnapshot, LastHurtAt, GraveSince, GraveBest = float.MaxValue;                     // the activity log (Activity)
        public string LastHurtBy = "";
        public bool Hungry, Weak;                                   // no food in it or on it; and badly hurt with no food (Work)
        public readonly Dictionary<string, float> Unfindable = new Dictionary<string, float>(); // what its goal needs and is not near home, until when
        public Goal Goal;                                           // what it is working toward (Goals)
        public string GoalSaid;
        public float NextTripLook, TripUntil;                       // a trip beyond its home's radius for its goal (Work)
        public float NextFireLook, NextMendLook, NextMend;
        public float NextReadLook;
        public float ManualOrderAt = -999f, HomeSince;              // auto home and away (Following): your last order, back home since
        public bool Outing, MasterWasHome, SortWhenHome;                                  // "let it decide" (Following): what you are doing, and how it fights
        public Doing Doing;
        public bool DefendOnly, Outmatched, Helping;
        public Style AutoStyle = Style.Balanced;
        public string AutoNote = "";
        public float NextFoodLook, NextNeedLook, MasterGoneSince, TripStart;      // looking after itself (Needs), the trip home (Work)
        public readonly HashSet<string> Wanted = new HashSet<string>();                  // what its work drops (to pick up)
        public readonly Dictionary<int, float> Skipped = new Dictionary<int, float>();    // things it gave up on, until when
        public readonly Dictionary<string, int> Gathered = new Dictionary<string, int>(); // this session, for the Work tab
        public float NextEnemyScan, NextGear, NextAsk, LastAsk, HealthAtAsk = 1f;
        public float NextStuckCheck, StuckDistance = float.MaxValue;
        public Vector3 StuckFrom;
        public int StuckFor;
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
            Activity.Log(Body, "» " + line);
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
        private static readonly Func<BaseAI, Vector3, bool> HavePath = AccessTools.MethodDelegate<Func<BaseAI, Vector3, bool>>(AccessTools.Method(typeof(BaseAI), "HavePath"));
        private static readonly Action<BaseAI, Vector3> LookAt =AccessTools.MethodDelegate<Action<BaseAI, Vector3>>(AccessTools.Method(typeof(BaseAI), "LookAt"));
        private static readonly Action<BaseAI, float> Regenerate = AccessTools.MethodDelegate<Action<BaseAI, float>>(AccessTools.Method(typeof(BaseAI), "UpdateRegeneration"));
        private static readonly AccessTools.FieldRef<BaseAI, float> TimeSinceHurt = AccessTools.FieldRefAccess<BaseAI, float>("m_timeSinceHurt");
        private static readonly AccessTools.FieldRef<Character, bool> Blocking = AccessTools.FieldRefAccess<Character, bool>("m_blocking");
        private static readonly AccessTools.FieldRef<Humanoid, float> DrawTime = AccessTools.FieldRefAccess<Humanoid, float>("m_attackDrawTime");

        /// <summary>
        /// The game's MoveTo (it only runs while it has the stamina for it). Where the game's pathfinding finds no way (inside a house with the
        /// door shut, a yard of stakes, a gap it does not know), the game simply stops it: then it walks as a player would, to the nearest door
        /// on its way (it opens it when it gets there), else straight at the spot; watching its step (Steer) keeps it off what hurts and jumps
        /// what is low, and the stuck checks take over at a real wall.
        /// </summary>
        private static bool MoveTo(BaseAI ai, float dt, Vector3 point, float dist, bool run)
        {
            Character c = ai.GetComponent<Character>();
            bool canRun = run && Stamina.CanRun(c);
            bool result = MoveToRaw(ai, dt, point, dist, canRun);
            Vector3 pos = c.transform.position;
            if (Utils.DistanceXZ(point, pos) <= Mathf.Max(dist, 0.75f) || c.GetMoveDir().sqrMagnitude > 0.01f) return result; // arrived, or on its way
            // Through a door: the game's pathfinding takes a shut door for a wall, so it finds no way out of (or into) a house or a gated yard.
            Humanoid h = c as Humanoid;
            BrainState st = h != null ? Get(h) : null;
            if (st != null)
            {
                DoorPlan plan = st.Door != null && Time.time < st.Door.Until && Vector3.Distance(st.Door.Target, point) < 4f ? st.Door : st.Door = PlanDoor(ai, pos, point);
                if (plan != null)
                {
                    if (!plan.Through)
                    {
                        if (Utils.DistanceXZ(plan.Near, pos) > 1.2f) { MoveToRaw(ai, dt, plan.Near, 0.8f, canRun); if (c.GetMoveDir().sqrMagnitude > 0.01f) return false; }
                        if ((Companion.Zdo(plan.Door)?.GetInt(ZDOVars.s_state, 0) ?? 0) == 0 && plan.Door.m_keyItem == null) plan.Door.Interact(h, false, false);
                        plan.Through = true;
                    }
                    if (Utils.DistanceXZ(plan.Far, pos) > 1f)
                    {
                        Vector3 step = plan.Far - pos;
                        step.y = 0f;
                        c.SetMoveDir(step.normalized);
                        c.SetLookDir(step.normalized, 0f);
                        return false;
                    }
                    st.Door = plan.Next; // through: the next door, or the pathfinding takes over from this side
                    return false;
                }
            }
            // Stakes or fire about: straight only when the way is short and every step of it is clear of them (it got hurt walking blind).
            if (h != null && Steer.HazardsNear(h) > 0 && !ClearLine(h, pos, point)) return result;
            Vector3 to = point;
            Vector3 dir = to - pos;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.01f) return result;
            dir.Normalize();
            c.SetMoveDir(dir);
            c.SetRun(canRun);
            c.SetLookDir(dir, 0f);
            return false;
        }

        /// <summary>A short straight way (8 m at most) with no hazard's reach on any step of it (Steer.Safe every half metre).</summary>
        private static bool ClearLine(Humanoid h, Vector3 from, Vector3 to)
        {
            float len = Utils.DistanceXZ(from, to);
            if (len > 8f) return false;
            for (float t = 0.5f; t < len + 0.25f; t += 0.5f)
                if (!Steer.Safe(Vector3.Lerp(from, to, Mathf.Min(1f, t / len)), h)) return false;
            return true;
        }

        /// <summary>Can it walk there (straight, or through a door)? For choosing where to go: a chest behind your stakes is not worth trying.</summary>
        internal static bool CanReach(Humanoid me, Vector3 to)
        {
            BaseAI ai = me.GetComponent<BaseAI>();
            Pathfinding pf = Pathfinding.instance;
            if (ai == null || pf == null) return true;
            if (!pf.FindValidPoint(out Vector3 goal, to, 2.5f, ai.m_pathAgentType)) return false;
            return pf.HavePath(me.transform.position, goal, ai.m_pathAgentType) || PlanDoor(ai, me.transform.position, to) != null;
        }

        internal class DoorPlan { public Door Door; public Vector3 Near, Far, Target; public float Until; public bool Through; public DoorPlan Next; }

        /// <summary>
        /// A door within 30 m that it can walk to from here, and from whose other side it can walk to the spot (the pathfinding's own check,
        /// as if the door were open): the one with the shortest way round. Null when there is none.
        /// </summary>
        private static DoorPlan PlanDoor(BaseAI ai, Vector3 from, Vector3 to)
        {
            Pathfinding pf = Pathfinding.instance;
            if (pf == null) return null;
            Pathfinding.AgentType agent = ai.m_pathAgentType;
            if (!pf.FindValidPoint(out Vector3 goal, to, 2.5f, agent)) goal = to;
            // The doors around, each with a walkable spot on either side.
            var doors = new List<KeyValuePair<Door, Vector3[]>>();
            var seen = new HashSet<Door>();
            int n = Physics.OverlapSphereNonAlloc(from, 40f, DoorHits, PieceMask);
            for (int i = 0; i < n; i++)
            {
                Door door = DoorHits[i].GetComponentInParent<Door>();
                if (door == null || door.m_keyItem != null || !seen.Add(door)) continue;
                if (!pf.FindValidPoint(out Vector3 a, door.transform.position + door.transform.forward * 1.6f, 1.5f, agent)) continue;
                if (!pf.FindValidPoint(out Vector3 b, door.transform.position - door.transform.forward * 1.6f, 1.5f, agent)) continue;
                doors.Add(new KeyValuePair<Door, Vector3[]>(door, new[] { a, b }));
            }
            doors.Sort((x, y) => Vector3.Distance(x.Key.transform.position, from).CompareTo(Vector3.Distance(y.Key.transform.position, from)));
            if (doors.Count > 10) doors.RemoveRange(10, doors.Count - 10);

            // One door: reach its near side, and from its far side reach the spot.
            DoorPlan best = null;
            float bestCost = float.MaxValue;
            var firsts = new List<DoorPlan>();
            foreach (var d in doors)
            {
                Vector3 near = Side(d.Value, from, true), far = Side(d.Value, from, false);
                if (!pf.HavePath(from, near, agent)) continue;
                var step = new DoorPlan { Door = d.Key, Near = near, Far = far, Target = to, Until = Time.time + 20f };
                firsts.Add(step);
                float cost = Vector3.Distance(from, near) + Vector3.Distance(far, goal);
                if (cost < bestCost && pf.HavePath(far, goal, agent)) { bestCost = cost; best = step; }
            }
            if (best != null) return best;

            // Two doors (out of the house, then through the gate).
            foreach (DoorPlan first in firsts)
                foreach (var d in doors)
                {
                    if (d.Key == first.Door) continue;
                    Vector3 near = Side(d.Value, first.Far, true), far = Side(d.Value, first.Far, false);
                    float cost = Vector3.Distance(from, first.Near) + Vector3.Distance(first.Far, near) + Vector3.Distance(far, goal);
                    if (cost >= bestCost || !pf.HavePath(first.Far, near, agent) || !pf.HavePath(far, goal, agent)) continue;
                    bestCost = cost;
                    best = new DoorPlan { Door = first.Door, Near = first.Near, Far = first.Far, Target = to, Until = Time.time + 30f,
                                          Next = new DoorPlan { Door = d.Key, Near = near, Far = far, Target = to, Until = Time.time + 30f } };
                }
            return best;
        }

        /// <summary>The side of a door nearer to (or further from) a spot.</summary>
        private static Vector3 Side(Vector3[] sides, Vector3 from, bool near) =>
            (Vector3.Distance(sides[0], from) < Vector3.Distance(sides[1], from)) == near ? sides[0] : sides[1];

        private static readonly Collider[] DoorHits = new Collider[128];

        /// <summary>A closed door within 12 m that lies towards the spot (no further off than a right angle), the nearest.</summary>
        private static Door DoorOnTheWay(Vector3 from, Vector3 to)
        {
            Vector3 way = to - from;
            way.y = 0f;
            Door best = null;
            float bestDist = float.MaxValue;
            int n = Physics.OverlapSphereNonAlloc(from, 12f, DoorHits, PieceMask);
            for (int i = 0; i < n; i++)
            {
                Door door = DoorHits[i].GetComponentInParent<Door>();
                if (door == null || door.m_keyItem != null) continue;
                Vector3 toDoor = door.transform.position - from;
                toDoor.y = 0f;
                float d = toDoor.magnitude;
                if (d < 1f || d >= bestDist || Vector3.Angle(way, toDoor) > 90f) continue;
                if ((Companion.Zdo(door)?.GetInt(ZDOVars.s_state, 0) ?? 0) != 0) continue; // open already: walk on through
                best = door; bestDist = d;
            }
            return best;
        }

        public static void Forget()
        {
            foreach (BrainState st in States.Values) if (st.Body != null) Blocking(st.Body) = false;
            States.Clear();
        }

        public static void Forget(Humanoid h) { if (h != null) States.Remove(h); }

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
            TimeSinceHurt(ai) += dt;           // (no free healing: food heals it, as it heals a player)
            Humanoid body = ai.GetComponent<Humanoid>();
            Stamina.Tick(body, dt);
            Tactics.Tick(Get(body)); // its roll's moment
            Eitr.Tick(body, dt);
            Weather.Tick(body);

            Humanoid me = ai.GetComponent<Humanoid>();
            BrainState st = Get(me);
            Player master = Companion.Master(me);
            Container gear = me.GetComponent<Container>();

            if (!st.CaughtUp) CatchUp.OnArrive(st);
            if (Companion.OrderOf(me) == Order.Gather && Time.time >= st.NextStamp) { st.NextStamp = Time.time + 5f; CatchUp.Stamp(me); }
            Food.Tick(me, st);
            Activity.Tick(st);
            Following.Tick(st, master);
            Banter.Tick(st, master);
            Idle.Tick(st, master); // up when nothing keeps it sitting; your return, your emotes
            if (Time.time >= st.NextBagSave) { st.NextBagSave = Time.time + 30f; Companion.SaveBag(me); } // wear from fighting and working
            if (Ride.Tick(st, master)) return true; // on a boat with its player: it sits and rides
            Loot.PassBy(st);                         // what is on its list, as it goes by
            if (Time.time >= st.NextGear) { st.NextGear = Time.time + 0.5f; Companion.Maintain(me, st.Current.Ranged, st.InCombat ? null : st.WorkTool, st.InCombat ? st.Current.Target : null); }
            if (gear != null && gear.IsInUse()) { ai.StopMoving(); Blocking(me) = false; SetStatus(st, "waiting while you sort its gear"); return true; }
            if (!st.Asleep && Carts.Tick(st, (p, dd, run) => MoveTo(ai, dt, p, dd, run), () => ai.StopMoving())) return true; // getting hold of a cart

            if (Time.time >= st.NextEnemyScan) { st.NextEnemyScan = Time.time + 0.25f; ScanEnemies(st, master); }
            foreach (Character e in st.Enemies) if (e != null && e.IsDead()) Loot.AddSpot(st, e.transform.position); // its drops, in a moment
            st.Enemies.RemoveAll(e => e == null || e.IsDead()); // killed or gone since the last look (a destroyed one throws on .transform)

            if (Time.time >= st.NextDoorLook) { st.NextDoorLook = Time.time + 0.4f; OpenDoorAhead(me); }
            if (Companion.OrderOf(me) != Order.Gather && !(Companion.OrderOf(me) == Order.Follow && (st.Helping || Time.time < st.CommandUntil))) { st.Task = null; st.WorkTool = null; }

            if (st.Enemies.Count > 0 && st.Asleep) Sleep.Wake(st, "something came");
            Character boss = st.Enemies.FirstOrDefault(e => e != null && e.IsBoss());
            if (boss != null && st.LastBoss != boss.m_name) { st.LastBoss = boss.m_name; Banter.BossSeen(me, Localization.instance.Localize(boss.m_name)); }
            if (st.Enemies.Count == 0)
            {
                if (st.InCombat)
                {
                    st.InCombat = false; st.Labels.Clear(); Summarise(st, "fight over");
                    if (me.GetHealthPercentage() < 0.3f) Banter.CloseFight(me);
                    else if (st.FightKills >= 3) Idle.Queue(st, "cheer", 1f); // a big fight won
                }
                Blocking(me) = false;
                Peaceful(st, master, dt);
                return true;
            }

            if (!st.InCombat)
            {
                st.FightFoes.Clear(); st.FightDodges = 0; st.ParriesAtStart = st.Parries; st.BlocksAtStart = st.Blocks; st.PotionsAtStart = st.Potions;
                st.InCombat = true; st.NextAsk = 0f; st.Fights++;
                st.FightStart = Time.time; st.Taken = st.Dealt = 0f; st.FightKills = st.FightDecisions = 0; st.LastHealth = me.GetHealth();
            }
            foreach (Character e in st.Enemies)
            {
                if (e == null) continue;
                string foe = Localization.instance.Localize(e.m_name);
                if (st.FightFoes.Add(foe) && Tactics.Feared(me, e))
                    Banter.Say(me, "feared:" + foe, 10f, $"Careful! {foe}s have hurt me badly before.", $"A {foe.ToLowerInvariant()}... I'll keep my distance this time.", $"Watch that {foe.ToLowerInvariant()}, it's dangerous.");
            }
            float hp = me.GetHealth();
            if (hp < st.LastHealth) st.Taken += st.LastHealth - hp;
            st.LastHealth = hp;
            MaybeDecide(st, master);
            Fight(st, master, dt);
            return true;
        }

        // ---- out of a fight ------------------------------------------------------------------------------

        private static void Peaceful(BrainState st, Player master, float dt)
        {
            Humanoid me = st.Body;
            if (st.Asleep && Companion.OrderOf(me) != Order.Gather) Sleep.Wake(st, "you called it"); // (come with me, at night)
            if (MakeWay(st, dt)) return;
            if (Idle.Emoting(st, p => LookAt(st.Ai, p))) return;                                                   // a wave, a cheer: its moment
            if (Idle.Come(st, master, (p, dd, run) => MoveTo(st.Ai, dt, p, dd, run), () => st.Ai.StopMoving(), p => LookAt(st.Ai, p))) return; // you beckoned
            if (Loot.Tick(st, (p, dd, run) => MoveTo(st.Ai, dt, p, dd, run))) return;                          // what the fight dropped
            if (Grave.Tick(st, (p, dd, run) => MoveTo(st.Ai, dt, p, dd, run), () => st.Ai.StopMoving())) return; // its things back
            if (Repair.Tick(st, (p, dd, run) => MoveTo(st.Ai, dt, p, dd, run), () => st.Ai.StopMoving())) return;
            Needs.Tick(st, master);                                                                               // a full bag, worn gear, no food
            switch (Companion.OrderOf(me))
            {
                case Order.Stay:
                    st.Ai.StopMoving();
                    Idle.Waiting(st); // (after a while, it sits)
                    SetStatus(st, st.Sitting ? "sitting here, waiting" : "staying here");
                    break;
                case Order.Gather:
                    if (Sleep.Tick(st, (p, dd, run) => MoveTo(st.Ai, dt, p, dd, run), () => st.Ai.StopMoving())) break; // night: in its bed
                    Work.Tick(st, master, dt, (p, dd, run) => MoveTo(st.Ai, dt, p, dd, run), () => st.Ai.StopMoving(), p => LookAt(st.Ai, p));
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
                    // You pointed at something for it to do (Pointing): that first.
                    if (Time.time < st.CommandUntil && (st.Task != null || st.PickQueue.Count > 0))
                    {
                        if (Work.RunOrdered(st, master, dt, (p, dd, run) => MoveTo(st.Ai, dt, p, dd, run), () => st.Ai.StopMoving(), p => LookAt(st.Ai, p))) break;
                        st.CommandUntil = 0f;
                    }
                    // You are mining or chopping: it works the rocks or trees near you (Following, "let it decide").
                    if (Companion.Chosen(me) == Style.Auto && (st.Doing == Doing.Mining || st.Doing == Doing.Chopping) && d < 25f
                        && Work.Help(st, master, dt, (p, dd, run) => MoveTo(st.Ai, dt, p, dd, run), () => st.Ai.StopMoving(), p => LookAt(st.Ai, p), st.Doing == Doing.Mining ? Job.Stone | Job.Ore : Job.Wood))
                        break;
                    if (d > 60f && !master.IsAttached() && master.IsOnGround()) { TeleportBehind(me, master); break; } // left behind (a portal, a boat ride)
                    if (Time.time < st.SideStepUntil) { MoveTo(st.Ai, dt, st.SideStepTo, 0.4f, false); break; } // getting unstuck: a step aside
                    if (d < 8f && st.Cart == null && Idle.WithYou(st, master, d, (p, dd, run) => MoveTo(st.Ai, dt, p, dd, run), p => LookAt(st.Ai, p))) break; // you stopped: it faces you, sits with you
                    if (d > 3.5f)
                    {
                        MoveTo(st.Ai, dt, master.transform.position, 2.5f, d > 8f);
                        if (Stuck(st, master, d) && master.IsOnGround() && !master.IsAttached()) { Unstick(st, master); break; }
                    }
                    else { st.Ai.StopMoving(); st.StuckFor = 0; }
                    SetStatus(st, "following " + master.GetPlayerName() + (st.Cart != null && st.Cart.IsAttached(me) ? " (pulling the cart)" : string.IsNullOrEmpty(st.AutoNote) || Companion.Chosen(me) != Style.Auto ? "" : $" ({st.AutoNote})"));
                    break;
            }
        }

        /// <summary>
        /// Following but getting no closer (a wall of sharpened stakes, a fence, a gap it cannot path over): true after about three seconds, or
        /// sooner when the pathfinder says there is no way at all. Checked once a second.
        /// </summary>
        private static bool Stuck(BrainState st, Player master, float d)
        {
            if (Time.time < st.NextStuckCheck) return false;
            st.NextStuckCheck = Time.time + 1f;
            Vector3 here = st.Body.transform.position;
            bool noProgress = Vector3.Distance(here, st.StuckFrom) < 0.75f; // pressed against something (trailing a running player still moves it)
            st.StuckFrom = here;
            st.StuckDistance = d;
            st.StuckFor = noProgress ? st.StuckFor + 1 : 0;
            if (!noProgress && d < st.StuckDistance - 2f) st.UnstickStage = 0; // getting somewhere again
            bool noWay = d > 6f && !HavePath(st.Ai, master.transform.position);
            if (st.StuckFor >= 3 || (noWay && st.StuckFor >= 1)) { st.StuckFor = 0; return true; }
            return false;
        }

        /// <summary>Stuck behind something while following: first a step aside, then a jump, then (still stuck) a hop over to you.</summary>
        private static void Unstick(BrainState st, Player master)
        {
            Humanoid me = st.Body;
            int stage = st.UnstickStage++;
            if (stage == 0)
            {
                Vector3 toYou = master.transform.position - me.transform.position; toYou.y = 0f;
                Vector3 side = Vector3.Cross(Vector3.up, toYou.normalized) * (UnityEngine.Random.value < 0.5f ? 1f : -1f);
                st.SideStepTo = me.transform.position + side * 2.5f + toYou.normalized * 0.5f;
                st.SideStepUntil = Time.time + 1.2f;
                return;
            }
            if (stage == 1) { me.SetMoveDir((master.transform.position - me.transform.position).normalized); me.Jump(false); return; }
            st.UnstickStage = 0;
            TeleportBehind(me, master, "hopped over to");
        }

        /// <summary>Put it on a free spot beside the player (behind, beside or in front, on the same level, nothing solid in the way).</summary>
        internal static void TeleportBehind(Humanoid me, Player master, string how = "caught up with")
        {
            Vector3 pos = FreeSpotNear(master);
            me.transform.position = pos;
            Rigidbody body = me.GetComponent<Rigidbody>();
            if (body != null) { body.position = pos; body.linearVelocity = Vector3.zero; }
            Get(me).Remember($"{how} {master.GetPlayerName()}");
            Plugin.Instance?.Note($"{Companion.NameOf(me)} {how} {master.GetPlayerName()}");
        }

        private static readonly int Solid = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "vehicle");

        private static Vector3 FreeSpotNear(Player master)
        {
            Transform t = master.transform;
            Vector3[] offsets = { -t.forward * 1.8f, -t.right * 1.6f, t.right * 1.6f, (-t.forward - t.right).normalized * 2f, (-t.forward + t.right).normalized * 2f, t.forward * 1.8f };
            foreach (Vector3 o in offsets)
            {
                Vector3 p = t.position + o;
                if (t.position.y > 3000f) p.y = t.position.y; // inside a dungeon: its own floor, not the world's ground below
                else if (ZoneSystem.instance != null && ZoneSystem.instance.GetSolidHeight(p, out float h))
                {
                    if (Mathf.Abs(h - t.position.y) > 1.2f) continue; // a different level (a wall top, a ditch)
                    p.y = h;
                }
                else p.y = t.position.y;
                if (Physics.CheckCapsule(p + Vector3.up * 0.6f, p + Vector3.up * 1.6f, 0.35f, Solid)) continue; // something in the way
                if (!Steer.Safe(p, null)) continue;                                                                // never onto the stakes
                return p;
            }
            return t.position - t.forward * 0.8f;
        }

        // ---- the fight ----------------------------------------------------------------------------------

        private static void ScanEnemies(BrainState st, Player master)
        {
            Humanoid me = st.Body;
            float range = Plugin.EngageRange.Value;
            Style style = Companion.StyleOf(me);
            bool careful = st.Weak || Tactics.Careful(st);
            st.Enemies.Clear();
            foreach (Character c in Character.GetAllCharacters())
            {
                if (c == null || c == me || c.IsDead() || c.IsPlayer() || !BaseAI.IsEnemy(me, c)) continue;
                if (c.GetComponent<BaseAI>() == null) continue;
                if (st.LeaveAlone.TryGetValue(c, out float until) && Time.time < until && TargetOf(c) != me && TargetOf(c) != master) continue; // could not get at it
                // Harmless animals (deer, hares) are not a fight: it hunts them when hunting (Work), unless one turns on it or you.
                if (c.GetFaction() == Character.Faction.AnimalsVeg && TargetOf(c) != me && TargetOf(c) != master) continue;
                // What it is hunting, and animals that never fight back (deer, hares: running away "targets" it), are prey, not a fight.
                if (st.Task != null && st.Task.Kind == Work.Kind.Hunt && st.Task.Target == c) continue;
                string kind = Utils.GetPrefabName(c.gameObject);
                if (kind == "Deer" || kind == "Hare") continue;
                float toMe = Vector3.Distance(c.transform.position, me.transform.position);
                float toMaster = master != null ? Vector3.Distance(c.transform.position, master.transform.position) : float.MaxValue;
                float limit = style == Style.Defensive ? range * 0.6f : range;
                // Living at home it defends its home: anything that comes within its radius (a raid on the base), not only what is near it.
                // Defending its home: what is attacking it, you or another player within 30 m of home. Not everything that has noticed
                // something somewhere in its home (it ran off across the base to every greydwarf).
                Character theirs = TargetOf(c);
                bool home = Companion.OrderOf(me) == Order.Gather && Vector3.Distance(c.transform.position, Work.Center(me)) < Mathf.Min(Work.RadiusOf(me), 30f)
                            && theirs != null && (theirs == me || theirs.IsPlayer() || Companion.Is(theirs));
                // Badly hurt, nothing eaten or just up after a fall: it keeps clear and only answers what comes at it or you (not a raid on the base).
                if (careful && TargetOf(c) != me && TargetOf(c) != master) continue;
                // Travelling with you, helping you work, or outmatched: only what is after you or it (or right on top of you).
                if (Following.DefendOnly(me) && TargetOf(c) != me && TargetOf(c) != master && toMe > 3f && toMaster > 4f) continue;
                if (toMe < limit || toMaster < limit || home) st.Enemies.Add(c);
            }
            if (st.Focus != null && !st.Focus.IsDead() && Time.time < st.FocusUntil && !st.Enemies.Contains(st.Focus) && Vector3.Distance(st.Focus.transform.position, me.transform.position) < 50f)
                st.Enemies.Add(st.Focus); // you pointed at it
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
            if (now < st.NextAsk && !(urgent && now - st.LastAsk > 0.5f)) return;

            st.LastAsk = now;
            st.NextAsk = now + 1.5f; // and at once when something big happens
            st.HealthAtAsk = me.GetHealthPercentage();
            st.EnemyCountAtAsk = st.Enemies.Count;
            Apply(st, BuiltIn(st, master));
        }

        private static readonly int PieceMask = LayerMask.GetMask("piece", "piece_nonsolid", "Default");

        /// <summary>A closed door right in front of it while it walks: open it, as a player would (not a locked one, not behind a ward it lacks).</summary>
        private static void OpenDoorAhead(Humanoid me)
        {
            // Where it means to go (pressed against a shut door it is not moving at all, and the door must open then most of all).
            Vector3 dir = me.GetMoveDir();
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.01f) return;
            foreach (Collider col in Physics.OverlapSphere(me.transform.position + dir.normalized * 1.1f + Vector3.up, 0.9f, PieceMask))
            {
                Door door = col.GetComponentInParent<Door>();
                if (door == null || door.m_keyItem != null) continue;
                ZDO z = Companion.Zdo(door);
                if (z == null || z.GetInt(ZDOVars.s_state, 0) != 0) continue;
                door.Interact(me, false, false);
                return;
            }
        }

        /// <summary>
        /// A player walking into it, or bumping it: it gets out of the way at once. A shove (the game's own knock-back) sends it aside the moment
        /// you touch it, it then runs about 3 m clear to the side it is already on, and for that moment you and it do not collide, so you are
        /// never stopped by it. True while it is clearing the way.
        /// </summary>
        private static bool MakeWay(BrainState st, float dt)
        {
            Humanoid me = st.Body;
            if (Time.time < st.YieldUntil)
            {
                MoveToRaw(st.Ai, dt, st.YieldTo, 0.4f, true);
                return true;
            }
            st.YieldFrom = null; // (you walk through it anyway: Passing)
            foreach (Player p in Player.GetAllPlayers())
            {
                if (p == null) continue;
                Vector3 toMe = me.transform.position - p.transform.position; toMe.y = 0f;
                float d = toMe.magnitude;
                if (d > 2.5f) continue;
                Vector3 v = p.GetVelocity(); v.y = 0f;
                bool walkingInto = v.magnitude > 0.5f && Vector3.Dot(v.normalized, toMe.normalized) > 0.3f;
                bool touching = d < 1.1f;
                if (!walkingInto && !touching) continue;
                Vector3 dir = v.magnitude > 0.5f ? v.normalized : (d > 0.01f ? toMe.normalized : me.transform.forward);
                Vector3 side = Vector3.Cross(Vector3.up, dir);
                if (Vector3.Dot(side, toMe) < 0f) side = -side; // to the side it is already on
                st.YieldTo = me.transform.position + side * 3f + dir * 1f;
                st.YieldUntil = Time.time + 1.2f;
                st.YieldFrom = p;
                MoveToRaw(st.Ai, dt, st.YieldTo, 0.4f, true);
                return true;
            }
            return false;
        }

        internal static Character TargetOf(Character enemy) => enemy != null && enemy.GetBaseAI() is MonsterAI m ? m.GetTargetCreature() : null;

        /// <summary>
        /// Your rules are rules: below the fall-back health it falls back (and below half of that it flees); a passive companion never attacks;
        /// what you pointed at is its target.
        /// </summary>
        private static void Enforce(BrainState st, Decision d)
        {
            Humanoid me = st.Body;
            if (st.Focus != null && (st.Focus.IsDead() || Time.time > st.FocusUntil)) st.Focus = null;
            if (st.Focus != null && st.Enemies.Contains(st.Focus) && (d.Action == Tactic.Attack || d.Action == Tactic.DefendPlayer)) { d.Action = Tactic.Attack; d.Target = st.Focus; d.Note = "you pointed at it"; }
            Style style = Companion.StyleOf(me);
            float health = me.GetHealthPercentage();
            float retreat = Companion.RetreatOf(me) / 100f * (style == Style.Aggressive ? 0.5f : 1f);
            bool fighting = d.Action == Tactic.Attack || d.Action == Tactic.DefendPlayer;
            string rule = null;
            if (health < retreat * 0.5f && d.Action != Tactic.Flee) { d.Action = Tactic.Flee; rule = $"rule: flee below {retreat * 50f:0}% health"; }
            else if (health < retreat && fighting) { d.Action = Tactic.Retreat; rule = $"rule: fall back below {retreat * 100f:0}% health"; }
            else if (style == Style.Passive && fighting) { d.Action = Tactic.Retreat; rule = "rule: passive, does not attack"; }
            if (rule == null) return;
            d.Note = string.IsNullOrEmpty(d.Note) ? rule : d.Note + "; " + rule;
            if (d.Action != Tactic.Attack) d.Drink |= Companion.Potions(me) && Companion.HealingPotions(me).Count > 0;
        }

        private static void Apply(BrainState st, Decision d)
        {
            Enforce(st, d);
            d.Time = Time.time;
            st.FightDecisions++;
            bool changed = d.Action != st.Current.Action || d.Target != st.Current.Target || d.Ranged != st.Current.Ranged;
            st.Current = d;
            if (d.Drink && Companion.Drink(st.Body)) { st.Remember("drank a healing potion"); st.Potions++; }
            string line = d.Describe(st.Label);
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
            // Something clearly too strong for it (a bear against a stone axe): it gets clear, to its home or to you. With a bow, fed and rested,
            // it may shoot from a distance instead.
            bool careful = Tactics.Careful(st);
            Character danger = st.Enemies.FirstOrDefault(e => e != null && Vector3.Distance(e.transform.position, me.transform.position) < 20f && Tactics.TooStrong(me, e));
            if (danger != null && (careful || Companion.BestRanged(me) == null || Vector3.Distance(danger.transform.position, me.transform.position) < 5f))
            {
                string what = Localization.instance.Localize(danger.m_name);
                d.Action = Tactic.Flee; d.Note = $"the {what.ToLowerInvariant()} is too strong for it";
                Talk.Mention(me, $"That {what.ToLowerInvariant()} is too strong for me. I'm getting clear!", "toostrong:" + what, 3f);
                return d;
            }
            // Nothing eaten, or just up after a fall: it gives ground early.
            if (careful && health < 0.6f) { d.Action = health < 0.35f ? Tactic.Flee : Tactic.Retreat; d.Note = "nothing eaten: careful"; return d; }
            if (Stamina.Get(me) < Stamina.Max(me) * 0.15f && nearest != null && Vector3.Distance(nearest.transform.position, me.transform.position) < 6f)
            { d.Action = Tactic.BackOff; d.Note = "out of breath"; return d; }
            d.Action = Tactic.Attack;
            d.Target = PickTarget(st, master);
            Character t = d.Target;
            d.Ranged = Companion.BestRanged(me) != null && t != null && (Companion.BestMelee(me) == null || Vector3.Distance(t.transform.position, me.transform.position) > 10f
                                                                    || t.IsBoss() || Tactics.Feared(me, t) || Tactics.TooStrong(me, t)); // a boss, what hurt it before, or too strong: from a distance
            if (d.Ranged && Companion.BestMelee(me) != null && st.Enemies.Any(e => e != null && TargetOf(e) == me && Vector3.Distance(e.transform.position, me.transform.position) < 2.5f))
                d.Ranged = false; // cornered: the blade
            if (st.Enemies.Any(e => e != null && e.IsBoss()) && Companion.Potions(me) && health < 0.6f && Companion.HealingPotions(me).Count > 0) d.Drink = true; // earlier against a boss
            return d;
        }

        /// <summary>
        /// Whom the built-in brain hits: keep the current target while it is alive and still fighting (switching every round wastes swings);
        /// otherwise finish the weakest enemy that is on it, then whoever is on the player (if it protects the player), then whoever is on it,
        /// and only then the nearest. Never one that is attacking no one while others are hitting it.
        /// </summary>
        private static Character PickTarget(BrainState st, Player master)
        {
            Humanoid me = st.Body;
            float Dist(Character e) => Vector3.Distance(e.transform.position, me.transform.position);
            // You are badly hurt: whatever is on you, first (it peels it off you).
            if (master != null && Companion.Protect(me) && master.GetHealthPercentage() < 0.4f)
            {
                Character onYou = st.Enemies.Where(e => TargetOf(e) == master).OrderBy(e => Vector3.Distance(e.transform.position, master.transform.position)).FirstOrDefault();
                if (onYou != null) return onYou;
            }
            Character current = st.Current.Target;
            if (current != null && !current.IsDead() && st.Enemies.Contains(current) && Dist(current) < 6f && TargetOf(current) != null) return current;
            var onMe = st.Enemies.Where(e => TargetOf(e) == me && Dist(e) < 5f).ToList();
            if (onMe.Count > 0) return onMe.OrderBy(e => e.GetHealthPercentage()).ThenBy(Dist).First();
            // What you are hitting: it joins in (so it drops sooner).
            Character yours = Tactics.Yours();
            if (yours != null && st.Enemies.Contains(yours)) return yours;
            if (master != null && Companion.Protect(me))
            {
                Character onMaster = st.Enemies.Where(e => TargetOf(e) == master).OrderBy(e => Vector3.Distance(e.transform.position, master.transform.position)).FirstOrDefault();
                if (onMaster != null) return onMaster;
            }
            return st.Enemies.Where(e => TargetOf(e) == me).OrderBy(Dist).FirstOrDefault() ?? st.Enemies.FirstOrDefault();
        }

        private static void Fight(BrainState st, Player master, float dt)
        {
            Humanoid me = st.Body;
            Tactics.Meads(st); // stamina, eitr, resistance: what the moment calls for
            Decision d = st.Current;
            Character nearest = st.Enemies.FirstOrDefault();
            switch (d.Action)
            {
                case Tactic.Attack:
                    Strike(st, d.Target != null && !d.Target.IsDead() && st.Enemies.Contains(d.Target) ? d.Target : nearest, dt);
                    break;
                case Tactic.DefendPlayer:
                    // Whoever is after the player first, else whoever is after the companion, else the nearest. It only runs to the player when
                    // nothing is on itself: running off with enemies hitting its back is how it died while the player circled.
                    Character onMaster = master == null ? null : st.Enemies.Where(e => e != null && TargetOf(e) == master).OrderBy(e => Vector3.Distance(e.transform.position, master.transform.position)).FirstOrDefault();
                    Character onMe = st.Enemies.Where(e => e != null && TargetOf(e) == me).OrderBy(e => Vector3.Distance(e.transform.position, me.transform.position)).FirstOrDefault();
                    if (onMaster != null && onMe == null && Vector3.Distance(me.transform.position, master.transform.position) > 12f)
                    { Blocking(me) = false; MoveTo(st.Ai, dt, master.transform.position, 3f, true); }
                    else Strike(st, onMaster ?? onMe ?? nearest, dt);
                    break;
                case Tactic.BackOff:
                    Guarded(st, nearest, me.transform.position + (me.transform.position - nearest.transform.position).normalized * 4f, false, dt);
                    break;
                case Tactic.Retreat:
                    Vector3 to = master != null ? master.transform.position : me.transform.position + (me.transform.position - nearest.transform.position).normalized * 6f;
                    Guarded(st, nearest, to, Vector3.Distance(to, me.transform.position) > 6f, dt);
                    break;
                default: // flee: to safety (its home, living there; you, following), else away from them all, never into the sea
                    Blocking(me) = false;
                    Vector3? safe = SafePlace(me, master);
                    if (safe.HasValue && Vector3.Distance(safe.Value, me.transform.position) > 3f) { MoveTo(st.Ai, dt, safe.Value, 2f, true); break; }
                    Vector3 away = Vector3.zero;
                    foreach (Character e in st.Enemies) away += (me.transform.position - e.transform.position).normalized;
                    Vector3 dir = away.normalized + (master != null ? (master.transform.position - me.transform.position).normalized * 0.5f : Vector3.zero);
                    Vector3 off = me.transform.position + dir.normalized * 10f;
                    if (safe.HasValue && Vector3.Distance(off, safe.Value) > 12f) off = safe.Value; // keep near safety, not run off for ever
                    MoveTo(st.Ai, dt, off, 0f, true);
                    break;
            }
        }

        /// <summary>Where it is safe: its home when it lives there (inside your walls and stakes), you when it is with you.</summary>
        private static Vector3? SafePlace(Humanoid me, Player master)
        {
            Order order = Companion.OrderOf(me);
            if (order == Order.Gather) return Work.Center(me);
            if (master != null && order == Order.Follow) return master.transform.position;
            if (order == Order.Guard || order == Order.Stay) return Companion.Zdo(me)?.GetVec3(Keys.Post, me.transform.position);
            return null;
        }

        /// <summary>Close in on a target and hit it (or shoot it from a distance with a bow).</summary>
        internal static void Strike(BrainState st, Character target, float dt)
        {
            Humanoid me = st.Body;
            Blocking(me) = false;
            if (target == null) return;
            // A fight going nowhere: 20 s without hurting it, and it is not after anyone (behind a fence or a rock, up a slope it cannot climb):
            // it leaves it alone for two minutes rather than swing at the fence all day.
            if (st.StallOn != target || target.GetHealth() < st.StallHealth - 0.1f) { st.StallOn = target; st.StallSince = Time.time; st.StallHealth = target.GetHealth(); }
            else if (Time.time - st.StallSince > 20f && TargetOf(target) != me && TargetOf(target) != Companion.Master(me))
            {
                st.LeaveAlone[target] = Time.time + 120f;
                foreach (Character gone in st.LeaveAlone.Keys.Where(k => k == null).ToList()) st.LeaveAlone.Remove(gone);
                st.Enemies.Remove(target);
                st.Remember($"could not get at {Localization.instance.Localize(target.m_name)} (20 s without a hit): left it alone");
                if (st.Task != null && st.Task.Target == target) st.Task = null;
                st.StallOn = null;
                return;
            }
            ItemDrop.ItemData weapon = me.GetCurrentWeapon();
            bool ranged = Companion.IsRanged(weapon) && Companion.HasAmmoFor(me, weapon);
            float reach = ranged ? 30f : Mathf.Max(1.2f, (weapon?.m_shared.m_attack?.m_attackRange ?? 1.5f) * 0.9f);
            float dist = Vector3.Distance(target.transform.position, me.transform.position) - target.GetRadius();
            Vector3 aim = target.GetCenterPoint();

            // Defending itself (Defense): mid-step aside, it finishes the step; after a parry it hits back at once; something swinging at it
            // within reach: step aside (a sweep, a shot, an unblockable or too hard a hit, out of breath) or a timed block, raised just before
            // the hit lands so that it is a parry. The game does the rest (block power, stamina, the parry's stagger, the Blocking skill).
            if (Time.time < st.EvadeUntil)
            {
                Vector3 step = st.EvadeTo - me.transform.position;
                step.y = 0f;
                Blocking(me) = false;
                if (step.sqrMagnitude > 0.25f) { me.SetMoveDir(step.normalized); return; }
                st.EvadeUntil = 0f;
            }
            bool counter = Time.time < st.CounterUntil && target.IsStaggering(); // its parry staggered it: now
            if (!counter && !me.InAttack())
            {
                Character swinging = st.Enemies.FirstOrDefault(e => e != null && !e.IsDead() && e.InAttack() && TargetOf(e) == me
                                                                    && Vector3.Distance(e.transform.position, me.transform.position) < Defense.Reach(e));
                if (swinging != null)
                {
                    if (Time.time >= st.NextEvade && Defense.ShouldEvade(me, swinging, out string why) && Defense.StepAside(st, swinging))
                    {
                        st.NextEvade = Time.time + 1.2f;
                        Blocking(me) = false;
                        Activity.Log(me, $"rolled away from {Localization.instance.Localize(swinging.m_name)} ({why})");
                        return;
                    }
                    if (!ranged && Companion.CanBlock(me) && Stamina.Get(me) > 8f)
                    {
                        st.Blocker = swinging;
                        st.Ai.StopMoving();
                        LookAt(st.Ai, swinging.GetCenterPoint());
                        bool up = Defense.RaiseNow(swinging);   // (a learned wind-up: not before the moment)
                        Blocking(me) = up;
                        if (up) st.BlockUntil = Time.time + 0.35f;
                        return;
                    }
                }
                else if (Time.time < st.BlockUntil && !ranged && Companion.CanBlock(me))
                {
                    Character facing = st.Blocker != null && !st.Blocker.IsDead() ? st.Blocker : target;
                    st.Ai.StopMoving();
                    LookAt(st.Ai, facing.GetCenterPoint());
                    Blocking(me) = true;
                    return;
                }
            }

            // Hit and run against a heavy hitter: after its blow, a step back out of reach while the creature swings at nothing (not while it reels).
            if (st.HitAndRun && !me.InAttack()) { st.HitAndRun = false; st.RunBackUntil = Time.time + 0.8f; }
            if (!ranged && Time.time < st.RunBackUntil && !target.IsStaggering())
            {
                Vector3 back = me.transform.position - target.transform.position; back.y = 0f;
                MoveTo(st.Ai, dt, target.transform.position + back.normalized * (Defense.Reach(target) + 1f), 0.3f, true);
                LookAt(st.Ai, target.GetCenterPoint());
                return;
            }
            Player you = Companion.Master(me);

            // A creature that has not noticed it: up from behind at a walk (running is heard), for the game's backstab.
            if (!ranged && Tactics.Unaware(target) && !st.Enemies.Any(e => e != null && TargetOf(e) == me))
            {
                Vector3? behind = Tactics.Behind(me, target, reach);
                if (behind != null && Vector3.Distance(behind.Value, me.transform.position) > 0.8f) { MoveTo(st.Ai, dt, behind.Value, 0.3f, false); if (st.Task == null) Brain.Status(st, "sneaking up on the " + Localization.instance.Localize(target.m_name).ToLowerInvariant()); return; } // (hunting: its job says so, or the two swap every frame)
                if (dist > reach) { MoveTo(st.Ai, dt, target.transform.position, reach * 0.6f, false); return; }
            }

            // A boss: with a bow, keep 12 to 18 m away and work round its side; close in, never stand in front of it while it faces someone else
            // (its breath, its sweep, its stomp reach furthest there): get to its flank first.
            if (target.IsBoss())
            {
                Vector3 fromBoss = me.transform.position - target.transform.position; fromBoss.y = 0f;
                if (st.EvadeSide == 0f) st.EvadeSide = UnityEngine.Random.value < 0.5f ? 1f : -1f;
                if (ranged && dist < 12f) { MoveTo(st.Ai, dt, target.transform.position + Quaternion.Euler(0f, 25f * st.EvadeSide, 0f) * fromBoss.normalized * 15f, 1f, true); return; }
                if (!ranged && TargetOf(target) != me && Vector3.Angle(target.transform.forward, fromBoss) < 60f)
                {
                    Vector3 flank = target.transform.position + Quaternion.Euler(0f, 100f * st.EvadeSide, 0f) * target.transform.forward * (target.GetRadius() + reach * 0.8f);
                    MoveTo(st.Ai, dt, flank, 0.6f, true);
                    Brain.Status(st, "getting round to its side");
                    return;
                }
            }
            // With you on it: round to its other side, so it cannot face you both.
            if (!ranged && Tactics.Pincer(me, you, target, reach) is Vector3 far && Vector3.Distance(far, me.transform.position) > 1.2f)
            {
                MoveTo(st.Ai, dt, far, 0.5f, true);
                if (st.Task == null) Brain.Status(st, "getting round behind the " + Localization.instance.Localize(target.m_name).ToLowerInvariant());
                return;
            }
            // A bow kites: something closing in on it, it backs off between shots, and shoots when its bow is ready and there is room (cornered,
            // the brain gives it its blade: BuiltIn).
            if (ranged && dist < 7f && TargetOf(target) == me && !target.IsBoss() && (dist < 4f || me.GetTimeSinceLastAttack() < 1.6f))
            {
                Vector3 away = me.transform.position - target.transform.position; away.y = 0f;
                MoveTo(st.Ai, dt, me.transform.position + away.normalized * 5f, 0.5f, true);
                return;
            }
            if (dist > reach || (ranged && !st.Ai.CanSeeTarget(target))) { MoveTo(st.Ai, dt, target.transform.position, ranged ? reach * 0.7f : reach * 0.6f, dist > 5f); return; }

            st.Ai.StopMoving();
            Vector3 lookAt = aim;
            if (ranged)
            {
                // Where the arrow has to go: ahead of a moving target, and above it for the drop (Archery: the game's arrow speed and gravity).
                Vector3? shot = Archery.Aim(me, weapon, target);
                if (shot == null) { MoveTo(st.Ai, dt, target.transform.position, 8f, dist > 12f); return; } // out of its reach: closer
                me.SetLookDir(shot.Value, 0f);
                lookAt = me.transform.position + new Vector3(shot.Value.x, 0f, shot.Value.z) * 10f; // its body turns that way
                LookAt(st.Ai, lookAt);
                me.SetLookDir(shot.Value, 0f);
            }
            else
            {
                LookAt(st.Ai, aim);
                // The game swings along its body, tilted toward where it looks (up to the weapon's limit): look at the target's middle from
                // the height the swing starts at, so a boar below it (down a slope) is swung at, not over.
                Vector3 swingFrom = me.transform.position + Vector3.up * (weapon?.m_shared.m_attack?.m_attackHeight ?? 1f);
                Vector3 toTarget = aim - swingFrom;
                if (toTarget.sqrMagnitude > 0.01f) me.SetLookDir(toTarget.normalized, 0f);
            }
            float cost = weapon?.m_shared.m_attack?.m_attackStamina ?? 0f;
            if (!me.InAttack() && cost > 0f && Stamina.Get(me) < cost + 0.1f)
            {
                Guarded(st, target, me.transform.position + (me.transform.position - target.transform.position).normalized * 3f, false, dt);
                return;
            }
            if (me.InAttack() || !st.Ai.IsLookingAt(ranged ? lookAt : aim, ranged ? 4f : 25f)) return;
            if (me.GetTimeSinceLastAttack() < (ranged ? 1.6f : 0.35f)) return;
            if (ranged) DrawTime(me) = 10f; // a full draw: the AI has no hold-the-button, and an undrawn bow does no damage
            bool special = !ranged && Tactics.Special(st, weapon, target, dist); // a sweep into a crowd, a stagger on a tough one
            if (me.StartAttack(target, special))
            {
                st.Blows++;
                if (special) Activity.Log(me, $"used its {Localization.instance.Localize(weapon.m_shared.m_name).ToLowerInvariant()}'s special on the {Localization.instance.Localize(target.m_name).ToLowerInvariant()}");
                if (!ranged && TargetOf(target) == me && Tactics.Heavy(me, target)) st.HitAndRun = true; // (it is the one being swung at)
            }
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

        /// <summary>One line for the log, the menu and the Debug tab: how long, damage taken and dealt, kills, decisions.</summary>
        internal static void Summarise(BrainState st, string how)
        {
            string line = $"{how} after {Time.time - st.FightStart:0} s: took {st.Taken:0} damage, dealt {st.Dealt:0}, {st.FightKills} kill{(st.FightKills == 1 ? "" : "s")}, {st.FightDecisions} decisions";
            st.Remember(line);
            Plugin.Instance?.Note($"{Companion.NameOf(st.Body)}: {line}");
            string record = $"{DateTime.Now:HH:mm}  {how} ({Time.time - st.FightStart:0} s, vs {string.Join(", ", st.FightFoes.Take(4))}): dealt {st.Dealt:0}, took {st.Taken:0}, {st.FightKills} kill{(st.FightKills == 1 ? "" : "s")}, "
                          + $"{st.Parries - st.ParriesAtStart} parr{(st.Parries - st.ParriesAtStart == 1 ? "y" : "ies")}, {st.Blocks - st.BlocksAtStart} blocks, {st.FightDodges} rolls, {st.Potions - st.PotionsAtStart} potions";
            st.FightLog.Insert(0, record);
            if (st.FightLog.Count > 8) st.FightLog.RemoveAt(st.FightLog.Count - 1);
        }

        internal static void Status(BrainState st, string status) => SetStatus(st, status);

        private static void SetStatus(BrainState st, string status)
        {
            if (st.Status == status) return;
            st.Status = status;
            Activity.Log(st.Body, $"now: {status}  (hp {st.Body.GetHealth():0}/{st.Body.GetMaxHealth():0})");
            Companion.Zdo(st.Body)?.Set(Keys.Status, status);
        }
    }
}
