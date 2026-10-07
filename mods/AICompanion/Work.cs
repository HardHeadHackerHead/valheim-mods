using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace AICompanion
{
    [Flags]
    public enum Job { None = 0, Wood = 1, Stone = 2, Ore = 4, Forage = 8, Loot = 16, Hunt = 32, Cook = 64 }

    /// <summary>
    /// Gathering, as a player does it: with the order "Gather" it works within its radius of home (its bed, or where it was told to gather)
    /// on the jobs you ticked, with the tools it has:
    ///   Wood   - fallen logs first, then stumps, then standing trees (an axe good enough for the tree: the game's tool tiers);
    ///   Stone  - rocks (a pickaxe);
    ///   Ore    - ore deposits: copper, tin, iron, silver... (a pickaxe good enough for them);
    ///   Forage - wild berries, mushrooms, flowers, thistle (things that grow back: never your planted crops);
    ///   Loot   - anything lying on the ground (off unless you tick it: it would also pick up what you dropped).
    /// It picks up what its own work drops. When its bag is nearly full it carries everything that is not gear to its chests (Home tab),
    /// and while there it takes better armour and weapons, arrows for its bow, healing potions and missing tools. Swings cost stamina, so it
    /// rests now and then; a fight interrupts the work.
    /// </summary>
    internal static class Work
    {
        internal enum Kind { None, Hit, Pick, PickUp, Store, Upgrade, Craft, Hunt, Cook, Fetch, Fuel, Mend }

        internal class Task
        {
            public Kind Kind;
            public Component Target;
            public Job Job;
            public ItemDrop.ItemData Item;   // what it is upgrading
            public Recipe Recipe;            // what it is crafting
            public bool ForGoal;             // toward its goal (Goals)
            public bool Edible;              // food (or raw food to cook), when it forages
            public bool Trip;                // beyond its home's radius, for its goal
            public bool WasTree;             // a standing tree (it steps clear when it falls)
            public bool Ordered;             // you pointed it at this (Pointing)
            public Vector3 Pos;
            public float Closer = 1f;        // how much closer than usual it stands (it steps in when its swings do nothing)
            public int Swings;
            public float HealthAt = -1f;     // the target's health when it last checked its swings
            public float Started, LastClose;
        }

        private static readonly string[] OreWords = { "Ore", "Scrap", "Flametal" };
        private static readonly HashSet<string> Prey = new HashSet<string> { "Deer", "Boar", "Neck", "Hare" };
        private static readonly HashSet<string> Harmless = new HashSet<string> { "Deer", "Hare" }; // they run, never fight back
        private static HashSet<string> _cookable;

        /// <summary>Raw food some cooking station can cook (raw meat, fish, dough...): it keeps it to cook, and fetches it from its chests.</summary>
        public static bool IsCookable(ItemDrop.ItemData i)
        {
            if (_cookable == null && ZNetScene.instance != null)
            {
                _cookable = new HashSet<string>();
                foreach (GameObject prefab in ZNetScene.instance.m_prefabs)
                {
                    CookingStation s = prefab != null ? prefab.GetComponent<CookingStation>() : null;
                    if (s != null) foreach (CookingStation.ItemConversion c in s.m_conversion) if (c.m_from != null) _cookable.Add(c.m_from.m_itemData.m_shared.m_name);
                }
            }
            return _cookable != null && _cookable.Contains(i.m_shared.m_name);
        }
        private static readonly Func<BaseAI, Vector3, bool> HavePath = AccessTools.MethodDelegate<Func<BaseAI, Vector3, bool>>(AccessTools.Method(typeof(BaseAI), "HavePath"));

        public static Job JobsOf(Component c) => (Job)(Companion.Zdo(c)?.GetInt(Keys.Jobs, 0) ?? 0);

        /// <summary>
        /// What it does at home when you have not told it (no jobs ticked): wood with an axe, stone and ore with a pickaxe, and forage when it
        /// is low on food. It always picks up what its own work drops.
        /// </summary>
        public static Job AutoJobs(Humanoid h)
        {
            Job j = Job.None;
            if (Axe(h) != null) j |= Job.Wood;
            if (Pickaxe(h) != null) j |= Job.Stone | Job.Ore;
            bool hungry = h.GetInventory().GetAllItems().Where(Food.IsFood).Sum(f => f.m_stack) < 10;
            if (hungry) j |= Job.Forage | Job.Cook;
            if (hungry && (Companion.BestRanged(h) != null || Companion.BestMelee(h) != null)) j |= Job.Hunt;
            return j;
        }
        public const string PantryKey = "dhc_pantry", StowKey = "dhc_stow";
        /// <summary>Starving with nothing in its own chests, it may take a little food from its player's chests at home (Home tab; off unless you allow it).</summary>
        public static bool UsesPantry(Component c) => Companion.Zdo(c)?.GetBool(PantryKey, false) ?? false;
        /// <summary>Its own chests full (or none), it may put what it gathers into its player's chests at home (Home tab; on). It never takes from them.</summary>
        public static bool Stows(Component c) => Companion.Zdo(c)?.GetBool(StowKey, true) ?? true;

        private static Func<Inventory, Vector3, float, Func<ItemDrop.ItemData, bool>, int> QolStack =>
            AppDomain.CurrentDomain.GetData("DHack.QoL.StackInventory") as Func<Inventory, Vector3, float, Func<ItemDrop.ItemData, bool>, int>;

        /// <summary>
        /// With QualityOfLife installed and putting things in your chests allowed (Home tab): everything it carries but keeps (its gear, food,
        /// tools) goes at once into the right chests near it, by your chest rules (a chest assigned the item, its category, or holding it already).
        /// At home only. How many items it put away.
        /// </summary>
        public static int SortHome(BrainState st)
        {
            Humanoid me = st.Body;
            var stack = QolStack;
            if (stack == null || !Stows(me) || Flat(Center(me), me.transform.position) > RadiusOf(me)) return 0;
            int moved = stack(me.GetInventory(), me.transform.position, 30f, i => Keeps(me, i) || Companion.Worn(me).Contains(i));
            if (moved <= 0) return 0;
            Companion.SaveBag(me);
            st.Remember($"sorted {moved} thing{(moved == 1 ? "" : "s")} into your chests");
            Plugin.Instance?.Note($"{Companion.NameOf(me)} sorted {moved} items into the chests near {me.transform.position:F0} (QualityOfLife)");
            return moved;
        }

        /// <summary>
        /// You pointed it at a chest: there, with QualityOfLife, what it carries (not its gear, food or tools) goes into the right chests around
        /// it by your chest rules (as your "Stack to chests"), and only the rest into the chest you pointed at. How many it sorted.
        /// </summary>
        public static int SortAt(BrainState st, Container chest)
        {
            Humanoid me = st.Body;
            var stack = QolStack;
            if (stack == null || chest == null) return 0;
            int moved = stack(me.GetInventory(), chest.transform.position, 20f, i => Keeps(me, i) || Companion.Worn(me).Contains(i));
            if (moved <= 0) return 0;
            Companion.SaveBag(me);
            st.Remember($"sorted {moved} thing{(moved == 1 ? "" : "s")} into your chests");
            Talk.Mention(me, $"Sorted {moved} thing{(moved == 1 ? "" : "s")} into the right chests.");
            Plugin.Instance?.Note($"{Companion.NameOf(me)} sorted {moved} items into the chests near {chest.transform.position:F0} (QualityOfLife)");
            return moved;
        }

        /// <summary>Its player's chests at home (not a companion's) it may open (no ward against it), nearest first.</summary>
        internal static IEnumerable<Container> YourChests(Humanoid me, Vector3 center, float radius) =>
            UnityEngine.Object.FindObjectsByType<Container>(FindObjectsSortMode.None)
                .Where(c => Home.IsChest(c) && Home.IdOn(c) == 0L && !c.IsInUse() && Vector3.Distance(c.transform.position, center) < radius
                            && (!c.m_checkGuardStone || PrivateArea.CheckAccess(c.transform.position, 0f, false)))
                .OrderBy(c => Vector3.Distance(c.transform.position, me.transform.position));

        public static int RadiusOf(Component c) => Companion.Zdo(c)?.GetInt(Keys.Radius, 30) ?? 30;

        /// <summary>Where it works around: its bed, else where it was told to gather (or stand).</summary>
        public static Vector3 Center(Humanoid c)
        {
            ZDO z = Companion.Zdo(c);
            if (z.GetBool(Keys.HasBed, false)) return z.GetVec3(Keys.BedPos, c.transform.position);
            return z.GetVec3(Keys.Post, c.transform.position);
        }

        // ---- tools ---------------------------------------------------------------------------------------------

        public static ItemDrop.ItemData Axe(Humanoid h) => h.GetInventory().GetAllItems().Where(i => i.m_shared.m_damages.m_chop > 0f && i.IsWeapon() && Usable(i))
            .OrderByDescending(i => i.m_shared.m_toolTier).ThenByDescending(i => i.m_shared.m_damages.m_chop).FirstOrDefault();

        public static ItemDrop.ItemData Pickaxe(Humanoid h) => h.GetInventory().GetAllItems().Where(i => i.m_shared.m_damages.m_pickaxe > 0f && Usable(i))
            .OrderByDescending(i => i.m_shared.m_toolTier).ThenByDescending(i => i.m_shared.m_damages.m_pickaxe).FirstOrDefault();

        private static bool Usable(ItemDrop.ItemData i) => !i.m_shared.m_useDurability || i.m_durability > 0f;

        public static bool IsTool(ItemDrop.ItemData i) => i.m_shared.m_damages.m_chop > 0f || i.m_shared.m_damages.m_pickaxe > 0f;

        /// <summary>What a job needs and whether it has it, for the Work tab: "Bronze axe (tier 2)" or "no axe".</summary>
        public static string ToolFor(Humanoid h, Job job)
        {
            ItemDrop.ItemData tool = job == Job.Wood ? Axe(h) : job == Job.Stone || job == Job.Ore ? Pickaxe(h) : null;
            if (job == Job.Forage || job == Job.Loot) return "bare hands";
            return tool != null ? $"{Localization.instance.Localize(tool.m_shared.m_name)} (tier {tool.m_shared.m_toolTier})" : (job == Job.Wood ? "no axe" : "no pickaxe");
        }

        // ---- every frame while it gathers (no enemies near) ----------------------------------------------------

        public static void Tick(BrainState st, Player master, float dt, Action<Vector3, float, bool> moveTo, Action stop, Action<Vector3> lookAt)
        {
            Humanoid me = st.Body;
            Job jobs = JobsOf(me);
            if (jobs == Job.None) jobs = AutoJobs(me); // living at home: it decides for itself what to do with what it has
            Vector3 center = Center(me);
            float radius = RadiusOf(me);
            if (TravelHome(st, center, radius, moveTo)) return;
            if (Clearing(st, moveTo)) return;
            if (Time.time >= st.NextLoanLook) { st.NextLoanLook = Time.time + 30f; Loans.Return(st); } // food it borrowed and did not need

            if (st.Task != null && !Valid(st, st.Task)) Drop(st);
            if (st.Task == null && !NextQueued(st)) NextInArea(st); // things you pointed it at to pick up; the patch you pointed it at
            if (st.Task == null && Time.time >= st.NextWorkLook)
            {
                st.NextWorkLook = Time.time + 1.5f;
                st.Task = Choose(st, jobs, center, radius);
                if (st.Task == null) st.WorkTool = null;
            }
            if (st.Task == null)
            {
                if (Idle.AtHome(st, master, center, radius, moveTo, stop, lookAt)) return; // between jobs: the fire, a chair, out of the rain, a stroll
                Go(center, moveTo, stop, me); Brain.Status(st, st.WorkNote ?? "nothing left to gather here"); return;
            }
            Execute(st, master, dt, moveTo, stop, lookAt);
        }

        /// <summary>
        /// Out with you, you mining or chopping (Following): the rocks, ore or trees near you that its tools can work, nearest to you first,
        /// worked as it works at home (and what drops picked up). False when there is nothing for it to help with (then it follows), or you
        /// have gone on (more than 25 m).
        /// </summary>
        public static bool Help(BrainState st, Player master, float dt, Action<Vector3, float, bool> moveTo, Action stop, Action<Vector3> lookAt, Job job)
        {
            Humanoid me = st.Body;
            st.Helping = false;
            if (Vector3.Distance(me.transform.position, master.transform.position) > 25f) { st.Task = null; return false; }
            if (Clearing(st, moveTo)) { st.Helping = true; return true; }
            if (st.Task != null && !Valid(st, st.Task)) Drop(st);
            if (st.Task != null && (st.Task.Kind != Kind.Hit || Flat(st.Task.Target.transform.position, master.transform.position) > 20f)) st.Task = null;
            if (st.Task == null && Time.time >= st.NextWorkLook)
            {
                st.NextWorkLook = Time.time + 1f;
                ItemDrop.ItemData axe = Axe(me), pick = Pickaxe(me);
                if ((job == Job.Wood && axe == null) || ((job & (Job.Stone | Job.Ore)) != 0 && pick == null)) return false; // no tool: it guards you and picks up
                var seen = new HashSet<GameObject>();
                Task best = null;
                float bestD = float.MaxValue;
                foreach (Collider col in Physics.OverlapSphere(master.transform.position, 15f, ~0, QueryTriggerInteraction.Collide))
                {
                    GameObject go = col.attachedRigidbody != null ? col.attachedRigidbody.gameObject : col.transform.root.gameObject;
                    if (!seen.Add(go)) continue;
                    Task t = Consider(st, go, job, axe, pick);
                    if (t == null || t.Kind != Kind.Hit || Skipped(st, t.Target) || ClaimedByOther(st, t.Target)) continue;
                    float d = Vector3.Distance(t.Target.transform.position, master.transform.position);
                    if (d < bestD) { bestD = d; best = t; }
                }
                st.Task = best;
                Claim(st, best);
            }
            if (st.Task == null) { st.WorkTool = null; return false; }
            st.Helping = true;
            Execute(st, master, dt, moveTo, stop, lookAt);
            return true;
        }

        /// <summary>What it would do with this thing (a tree, log, rock, ore, a plant), whatever its jobs: for pointing at it. Null: nothing.</summary>
        public static Task Workable(BrainState st, GameObject go) => go == null ? null : Consider(st, go, Job.Wood | Job.Stone | Job.Ore | Job.Forage, Axe(st.Body), Pickaxe(st.Body), true);

        /// <summary>A task you gave it by pointing: it does that next (following you, or at home). Standing or guarding, it comes along for it.</summary>
        public static bool Ordered(BrainState st, Task t)
        {
            if (t == null || t.Target == null) return false;
            Humanoid me = st.Body;
            Order order = Companion.OrderOf(me);
            if ((order == Order.Stay || order == Order.Guard) && !Companion.Write(me, z => z.Set(Keys.Order, (int)Order.Follow))) return false;
            t.Started = t.LastClose = Time.time;
            t.Ordered = true;
            st.Task = t;
            st.Helping = false;
            st.CommandUntil = Time.time + 120f;
            st.Remember($"you pointed: {Describe(t)}");
            return true;
        }

        public static bool Ordered(BrainState st, Kind kind, Component target) => Ordered(st, New(kind, target, Job.None));

        /// <summary>You pointed at things lying on the ground: everything there (within the radius), nearest first. False when nothing lies there.</summary>
        public static bool OrderPickUp(BrainState st, Vector3 at, float radius)
        {
            var drops = new HashSet<ItemDrop>();
            foreach (Collider col in Physics.OverlapSphere(at, radius, ~0, QueryTriggerInteraction.Collide))
            {
                ItemDrop d = col.GetComponentInParent<ItemDrop>();
                if (d != null && d.m_itemData != null && d.GetComponent<Piece>() == null && d.m_itemData.m_shared.m_itemType != ItemDrop.ItemData.ItemType.None) drops.Add(d);
            }
            if (drops.Count == 0) return false;
            List<ItemDrop> order = drops.OrderBy(d => Vector3.Distance(d.transform.position, at)).ToList();
            st.PickQueue.Clear();
            st.PickQueue.AddRange(order.Skip(1));
            return Ordered(st, New(Kind.PickUp, order[0], Job.Loot));
        }

        // ---- a patch of work you pointed it at ------------------------------------------------------------

        /// <summary>You pointed at a tree (rock, plant): the ones around it too, worked through nearest first, and what they drop picked up.</summary>
        internal class Area
        {
            public Vector3 Center;
            public float Radius = 12f, Until;
            public Job Job;
            public int Done;
            public readonly List<Component> Marked = new List<Component>();
        }

        /// <summary>Starts a patch around the thing you pointed at (its task is already ordered). What it will work there, for showing.</summary>
        public static List<Component> OrderArea(BrainState st, Task first)
        {
            var area = new Area { Center = first.Target.transform.position, Until = Time.time + 900f,
                                  Job = first.Kind == Kind.Pick ? Job.Forage : first.Job == Job.Wood ? Job.Wood : Job.Stone | Job.Ore };
            st.Area = area;
            area.Marked.AddRange(AreaTasks(st, area).OrderBy(t => Flat(t.Target.transform.position, area.Center)).Take(8).Select(t => t.Target));
            return area.Marked;
        }

        private static IEnumerable<Task> AreaTasks(BrainState st, Area a)
        {
            ItemDrop.ItemData axe = Axe(st.Body), pick = Pickaxe(st.Body);
            var seen = new HashSet<GameObject>();
            foreach (Collider col in Physics.OverlapSphere(a.Center, a.Radius, ~0, QueryTriggerInteraction.Collide))
            {
                GameObject go = col.attachedRigidbody != null ? col.attachedRigidbody.gameObject : col.transform.root.gameObject;
                if (!seen.Add(go)) continue;
                Task t = Consider(st, go, a.Job, axe, pick, true);
                if (t == null || (t.Kind != Kind.Hit && t.Kind != Kind.Pick) || Skipped(st, t.Target) || ClaimedByOther(st, t.Target)) continue;
                yield return t;
            }
        }

        public static void EndArea(BrainState st, string say)
        {
            if (st.Area == null) return;
            st.Area = null;
            if (say != null) Talk.Tell(st.Body, say);
        }

        /// <summary>
        /// Working a patch: first what came down (the wood, stone or berries lying there), then the nearest thing left (fallen logs before
        /// standing trees). Bag full: near home, to its chest and back; out with you, it stops and says so. Out with you, the patch is left when
        /// you go on (40 m). False when there is nothing more to do there.
        /// </summary>
        public static bool NextInArea(BrainState st)
        {
            Area a = st.Area;
            if (a == null) return false;
            Humanoid me = st.Body;
            Player master = Companion.Master(me);
            if (Time.time > a.Until) { EndArea(st, null); return false; }
            if (Companion.OrderOf(me) == Order.Follow && master != null && Flat(master.transform.position, a.Center) > 40f) { EndArea(st, null); return false; }
            Inventory inv = me.GetInventory();
            if (inv.GetEmptySlots() == 0 || Carry.Over(me))
            {
                Vector3 home = Center(me);
                bool nearHome = Companion.Zdo(me).GetBool(Keys.HasBed, false) && Flat(a.Center, home) < RadiusOf(me) + 40f;
                Container chest = nearHome ? Home.Chests(me).Where(c => !c.IsInUse() && HasRoom(c, me) && !Skipped(st, c)).OrderBy(c => Vector3.Distance(c.transform.position, me.transform.position)).FirstOrDefault() : null;
                if (chest != null)
                {
                    Talk.Mention(me, "My bag's full. I'll put this away and come back.", "areafull", 2f);
                    a.Until = Time.time + 900f;
                    return Ordered(st, New(Kind.Store, chest, Job.None));
                }
                EndArea(st, "My bag is full. Take some of it, or send me home to put it away.");
                return false;
            }
            ItemDrop drop = null;
            float best = float.MaxValue;
            foreach (Collider col in Physics.OverlapSphere(a.Center, a.Radius + 4f, ~0, QueryTriggerInteraction.Collide))
            {
                ItemDrop d = col.GetComponentInParent<ItemDrop>();
                if (d == null || d.m_itemData == null || d.GetComponent<Piece>() != null || Skipped(st, d)) continue;
                float dd = Vector3.Distance(d.transform.position, me.transform.position);
                if (dd < best) { best = dd; drop = d; }
            }
            if (drop != null) return Ordered(st, New(Kind.PickUp, drop, Job.Loot));
            Task next = AreaTasks(st, a).OrderBy(t => Vector3.Distance(t.Target.transform.position, me.transform.position) + Priority(t)).FirstOrDefault();
            if (next == null)
            {
                EndArea(st, a.Done > 0 ? (a.Job == Job.Wood ? "That's the trees here done." : a.Job == Job.Forage ? "That's everything picked here." : "That's the rocks here done.") : null);
                return false;
            }
            a.Done++;
            Claim(st, next);
            if (!Ordered(st, next)) return false;
            Marks.Put(next.Target, me, next.Kind == Kind.Pick ? "picking this" : next.Job == Job.Wood ? "chopping this" : "mining this", () => st.Task == next && next.Target != null);
            return true;
        }

        /// <summary>The next of the things you pointed it at to pick up, when it has finished one. False when there are none left.</summary>
        public static bool NextQueued(BrainState st)
        {
            st.PickQueue.RemoveAll(d => d == null);
            if (st.PickQueue.Count == 0) return false;
            ItemDrop next = st.PickQueue[0];
            st.PickQueue.RemoveAt(0);
            return Ordered(st, New(Kind.PickUp, next, Job.Loot));
        }

        /// <summary>Following you: carry on with a task you pointed at. False when it is done (or gone).</summary>
        public static bool RunOrdered(BrainState st, Player master, float dt, Action<Vector3, float, bool> moveTo, Action stop, Action<Vector3> lookAt)
        {
            if (Clearing(st, moveTo)) return true;
            if (st.Task == null || !Valid(st, st.Task)) { Drop(st); if (NextQueued(st) || NextInArea(st)) return true; return Time.time < st.ClearUntil; }
            Execute(st, master, dt, moveTo, stop, lookAt);
            return st.Task != null;
        }

        /// <summary>Carrying out its task, wherever it was chosen (at home, or helping you).</summary>
        private static void Execute(BrainState st, Player master, float dt, Action<Vector3, float, bool> moveTo, Action stop, Action<Vector3> lookAt)
        {
            Humanoid me = st.Body;
            Task t = st.Task;
            Vector3 at = Point(t.Target, me.transform.position);
            float dist = Flat(at, me.transform.position);
            if (Time.time - t.Started > (t.Trip ? 240f : 60f) || (dist > 3f && Time.time - t.LastClose > (t.Trip ? 120f : 20f)))
            {
                // A piece it could not get to: the rest of that wall too (a stake wall outside your walls: one stake after another, for ever).
                if (t.Kind == Kind.Mend)
                    foreach (Collider col in Physics.OverlapSphere(t.Target.transform.position, 6f, LayerMask.GetMask("piece", "piece_nonsolid")))
                        if (col.GetComponentInParent<WearNTear>() is WearNTear near && near != t.Target) st.Skipped[near.gameObject.GetInstanceID()] = Time.time + 600f;
                Skip(st, t.Target, "could not get to it");
                return;
            }

            switch (t.Kind)
            {
                case Kind.PickUp:
                    if (dist > 1.2f) { moveTo(at, 0.5f, dist > 6f); break; }
                    t.LastClose = Time.time;
                    var drop = (ItemDrop)t.Target;
                    if (!drop.CanPickup(false)) { drop.RequestOwn(); break; }
                    string name = Localization.instance.Localize(drop.m_itemData.m_shared.m_name);
                    int n = drop.m_itemData.m_stack;
                    if (me.Pickup(drop.gameObject, false, false)) { st.Gathered[name] = (st.Gathered.TryGetValue(name, out int had) ? had : 0) + n; }
                    else Skip(st, drop, "no room");
                    st.Task = null;
                    break;

                case Kind.Pick:
                    if (dist > 1.5f) { moveTo(at, 0.8f, dist > 6f); break; }
                    t.LastClose = Time.time;
                    stop();
                    var pickable = (Pickable)t.Target;
                    if (pickable.m_itemPrefab != null) st.Wanted.Add(pickable.m_itemPrefab.name);
                    st.WorkSpot = pickable.transform.position;
                    pickable.Interact(me, false, false);
                    Skip(st, pickable, null, 5f);
                    st.Task = null;
                    break;

                case Kind.Hit:
                    ItemDrop.ItemData tool = t.Job == Job.Wood ? Axe(me) : Pickaxe(me);
                    if (tool == null) { st.Task = null; break; }
                    st.WorkTool = tool;
                    // How close, by what it is: well inside its swing (axes reach 2.2 m, pickaxes 1.8, to the surface), closer for small things
                    // (a sapling, a stump, a small rock), and closer still when its swings do nothing (Closer). Aimed at the middle of its height:
                    // a waist-high swing passes over a sapling.
                    Bounds size = Size(t.Target);
                    float big = Mathf.InverseLerp(0.6f, 3f, Mathf.Max(size.size.x, size.size.z, size.size.y * 0.5f));
                    float reach = Mathf.Lerp(0.6f, Mathf.Min(1.3f, (tool.m_shared.m_attack?.m_attackRange ?? 1.8f) * 0.6f), big) * t.Closer;
                    if (size.size.y > 0.01f && size.size.y < 1.5f) reach = Mathf.Min(reach, 0.5f); // something low: right up to it, to swing down on it
                    if (dist > reach) { moveTo(at, reach * 0.5f, dist > 8f); break; }
                    t.LastClose = Time.time;
                    stop();
                    // A level swing, as a player swings: at the trunk (or rock) at the height its swing starts from, lower only for what is
                    // shorter than that (a sapling, a small rock). Looking down at it from its eyes tilted the swing into the ground, which the
                    // swing hits first and stops at (Attack.DoMeleeAttack): close to a small tree it never touched the tree.
                    float tall = size.size.y > 0.01f ? size.max.y - at.y : 2f;
                    float swingHeight = tool.m_shared.m_attack?.m_attackHeight ?? 1f;
                    Vector3 swingFrom = new Vector3(me.transform.position.x, me.transform.position.y + swingHeight, me.transform.position.z);
                    Vector3 aim = new Vector3(at.x, Mathf.Min(swingFrom.y, at.y + Mathf.Max(0.25f, tall * 0.6f)), at.z);
                    lookAt(aim);
                    Vector3 level = aim - swingFrom;
                    if (level.sqrMagnitude > 0.01f) me.SetLookDir(level.normalized, 0f);
                    if (!me.IsItemEquiped(tool) || me.InAttack() || !st.Ai.IsLookingAt(aim, 25f)) break;
                    float cost = tool.m_shared.m_attack?.m_attackStamina ?? 0f;
                    if (Stamina.Get(me) < cost + 1f) { Brain.Status(st, "catching its breath"); break; }
                    if (me.GetTimeSinceLastAttack() < 0.5f) break;
                    st.WorkSpot = at;
                    if (me.StartAttack(null, false)) Missing(st, t);
                    break;

                case Kind.Store:
                    if (dist > 2f) { moveTo(at, 1.2f, dist > 8f); break; }
                    t.LastClose = Time.time;
                    stop();
                    if (t.Ordered) SortAt(st, (Container)t.Target); // you pointed at the chest: your "Stack to chests" from there first
                    Store(st, (Container)t.Target);
                    st.Task = null;
                    break;

                case Kind.Craft:
                    if (dist > 2.6f && !CanReachOver(t, dist)) { moveTo(at, 1.8f, dist > 8f); break; }
                    t.LastClose = Time.time;
                    stop();
                    Upgrades.Craft(st, t.Recipe, (CraftingStation)t.Target, t.ForGoal);
                    st.Task = null;
                    break;

                case Kind.Cook:
                    if (dist > 2.2f) { moveTo(at, 1.5f, dist > 8f); break; }
                    t.LastClose = Time.time;
                    stop();
                    if (Time.time - t.Started > 240f || !Kitchen.Cook(st, (CookingStation)t.Target)) st.Task = null; // done (what it cooked lies at its feet: it picks it up next)
                    break;

                case Kind.Mend:
                    // A hammer reaches a few metres, as yours does: it stands back (out of a stake wall's reach), and when it cannot get nearer
                    // after a while (the piece behind a wall, up on the roof) it repairs it from where it is, close enough.
                    if (dist > 4.5f && !(dist < 6.5f && Time.time - t.Started > 8f)) { moveTo(at, 3.2f, dist > 8f); break; }
                    t.LastClose = Time.time;
                    stop();
                    lookAt(at + Vector3.up);
                    st.WorkTool = Mending.Hammer(me);
                    if (Time.time - t.Started < 0.8f || Time.time < st.NextMend) break;
                    st.NextMend = Time.time + 0.9f;
                    if (!Mending.Fix(st, (WearNTear)t.Target)) { st.Task = null; break; }
                    WearNTear more = Mending.Damaged(me, me.transform.position, 8f, c => !Skipped(st, c)); // the next one beside it
                    if (more != null) { t.Target = more; t.Started = Time.time; } else st.Task = null;
                    break;

                case Kind.Fuel:
                    if (dist > 2f) { moveTo(at, 1.2f, dist > 8f); break; }
                    t.LastClose = Time.time;
                    stop();
                    Fires.Feed(st, (Fireplace)t.Target);
                    st.Task = null;
                    break;

                case Kind.Fetch:
                    if (dist > 2f) { moveTo(at, 1.2f, dist > 8f); break; }
                    t.LastClose = Time.time;
                    stop();
                    TakeFood(st, (Container)t.Target);
                    st.Task = null;
                    break;

                case Kind.Hunt:
                    var prey = (Character)t.Target;
                    if (prey != null && prey.IsDead()) Loot.AddSpot(st, prey.transform.position); // its meat and hide, in a moment
                    if (prey == null || prey.IsDead()) { st.Task = null; break; }
                    if (Companion.BestRanged(me) == null && Harmless.Contains(Utils.GetPrefabName(prey.gameObject)) && Vector3.Distance(prey.transform.position, me.transform.position) > 30f)
                    { Skip(st, prey, "it outran it", 3f); break; } // no bow: no catching a deer (a boar comes at it)
                    t.LastClose = Time.time;
                    st.WorkTool = Companion.BestRanged(me) ?? Companion.BestMelee(me);
                    Brain.Strike(st, prey, Time.deltaTime);
                    break;

                case Kind.Upgrade:
                    if (dist > 2.6f && !CanReachOver(t, dist)) { moveTo(at, 1.8f, dist > 8f); break; }
                    t.LastClose = Time.time;
                    stop();
                    Upgrades.Do(st, t.Item, (CraftingStation)t.Target);
                    st.Task = null;
                    break;
            }
            if (st.Task != null) Brain.Status(st, Describe(st.Task) + (st.Helping ? " to help you" : st.Task.ForGoal && st.Goal != null ? $" for its {st.Goal.What}" : ""));
        }

        /// <summary>
        /// Sent home from far away (an adventure, another island): it sets off toward home, and a few seconds later it is there, as a player
        /// would recall home. Its area may not be loaded then: it carries on there, and catches up when someone comes back (CatchUp).
        /// </summary>
        private static bool TravelHome(BrainState st, Vector3 center, float radius, Action<Vector3, float, bool> moveTo)
        {
            Humanoid me = st.Body;
            float far = Flat(center, me.transform.position);
            if (far < radius + 60f || Time.time < st.TripUntil || st.Area != null) { st.TripStart = 0f; return false; } // (not while working a patch you pointed it at)
            if (st.TripStart == 0f) { st.TripStart = Time.time; st.Task = null; st.Remember("set off home"); }
            moveTo(center, 2f, true);
            Brain.Status(st, "heading home");
            if (Time.time - st.TripStart < (far > 250f ? 8f : 45f)) return true; // from an adventure far away it is home soon; nearby it walks
            st.TripStart = 0f;
            Vector3 pos = center + Vector3.up * 0.2f;
            me.transform.position = pos;
            Rigidbody body = me.GetComponent<Rigidbody>();
            if (body != null) { body.position = pos; body.linearVelocity = Vector3.zero; }
            Companion.Zdo(me)?.SetPosition(pos);
            st.Remember("got home");
            Plugin.Instance?.Note($"{Companion.NameOf(me)} went home to {pos:F0}");
            return true;
        }

        /// <summary>The size of what it works on (its solid colliders together).</summary>
        private static Bounds Size(Component c)
        {
            Bounds b = new Bounds(c.transform.position, Vector3.zero);
            bool any = false;
            foreach (Collider col in c.GetComponentsInChildren<Collider>())
            {
                if (col == null || !col.enabled || col.isTrigger) continue;
                if (!any) { b = col.bounds; any = true; } else b.Encapsulate(col.bounds);
            }
            return b;
        }

        /// <summary>
        /// Every third swing: did the last three do anything? (its health, kept by the game in its save, went down) When not, it stands closer
        /// next time (down to a third of the usual), until its blows land.
        /// </summary>
        private static void Missing(BrainState st, Task t)
        {
            t.Swings++;
            if (t.Swings % 3 != 1) return;
            float health = Companion.Zdo(t.Target)?.GetFloat(ZDOVars.s_health, -1f) ?? -1f;
            // A big rock (copper, a boulder) keeps no one health (each part has its own): whether its last swing touched it (Attack_DoMeleeAttack_Log).
            bool missed = health < 0f ? st.SwingMissed : t.HealthAt >= 0f && health >= t.HealthAt - 0.01f;
            if (missed && t.Closer > 0.35f)
            {
                t.Closer = Mathf.Max(0.6f, t.Closer * 0.75f); // (not right into it: too close, it swings past)
                st.Remember($"its swings missed the {Hoverable(t.Target)}: it steps in closer");
            }
            t.HealthAt = health;
        }

        /// <summary>A station it cannot get right up to (against a wall, in a cramped corner): after a while, within a few metres, it reaches over.</summary>
        private static bool CanReachOver(Task t, float dist) => dist < 6f && Time.time - t.Started > 8f;

        private static void Go(Vector3 center, Action<Vector3, float, bool> moveTo, Action stop, Humanoid me)
        {
            if (Flat(center, me.transform.position) > 4f) moveTo(center, 2f, Flat(center, me.transform.position) > 12f);
            else stop();
        }

        internal static string Describe(Task t)
        {
            string what = t.Target != null ? Localization.instance.Localize(t.Target is ItemDrop d ? d.m_itemData.m_shared.m_name : t.Target is Pickable p && p.m_itemPrefab != null ? p.m_itemPrefab.GetComponent<ItemDrop>()?.m_itemData.m_shared.m_name ?? "" : Hoverable(t.Target)) : "";
            return t.Kind switch
            {
                Kind.Hit => (t.Job == Job.Wood ? "chopping " : "mining ") + what,
                Kind.Pick => "picking " + what,
                Kind.PickUp => "picking up " + what,
                Kind.Store => Home.IdOn(t.Target) == 0L ? "putting things in your chest" : "taking things to its chest",
                Kind.Craft => $"making a {Localization.instance.Localize(t.Recipe?.m_item?.m_itemData.m_shared.m_name ?? "")} at the {Localization.instance.Localize(((CraftingStation)t.Target).m_name)}",
                Kind.Cook => "cooking",
                Kind.Fetch => "getting something to eat from your chest",
                Kind.Fuel => "putting wood on the fire",
                Kind.Mend => "repairing " + Hoverable(t.Target).ToLowerInvariant(),
                Kind.Hunt => "hunting " + Localization.instance.Localize(((Character)t.Target)?.m_name ?? ""),
                Kind.Upgrade => $"upgrading its {Localization.instance.Localize(t.Item?.m_shared.m_name ?? "")} at the {Localization.instance.Localize(((CraftingStation)t.Target).m_name)}",
                _ => "gathering",
            };
        }

        private static string Hoverable(Component c)
        {
            Hoverable h = c.GetComponentInParent<Hoverable>();
            string n = h?.GetHoverName();
            return string.IsNullOrEmpty(n) ? Utils.GetPrefabName(c.gameObject).Replace('_', ' ') : n;
        }

        private static bool Valid(BrainState st, Task t)
        {
            if (t.Target == null) return false;
            if (t.Kind == Kind.Pick && Companion.Zdo(t.Target)?.GetBool(ZDOVars.s_picked, false) == true) return false;
            if ((t.Kind == Kind.Store || t.Kind == Kind.Fetch) && ((Container)t.Target).IsInUse()) return false;
            if (t.Kind == Kind.Mend && ((WearNTear)t.Target).GetHealthPercentage() >= 0.999f) return false;
            return true;
        }

        private static void Skip(BrainState st, Component target, string why, float minutes = 10f)
        {
            if (target != null) st.Skipped[target.gameObject.GetInstanceID()] = Time.time + minutes * 60f;
            if (why != null && target != null) st.Remember($"gave up on {Hoverable(target)}: {why}");
            st.Task = null;
        }

        private static bool Skipped(BrainState st, Component c) => st.Skipped.TryGetValue(c.gameObject.GetInstanceID(), out float until) && Time.time < until;

        private static Vector3 Point(Component c, Vector3 from)
        {
            // A standing tree (a full one, or a small one or sapling, which the game makes a plain destructible): the surface of its trunk (the
            // nearest of its colliders' real shapes close to its foot; the bounds take in the whole crown, and it stopped 4 m short and swung at
            // air). A trunk it cannot measure (one mesh for the whole tree): a hand's breadth out from its foot, towards it.
            if (c is TreeBase || c is Destructible des && (des.m_destructibleType == DestructibleType.Tree
                || des.m_damages.m_chop != HitData.DamageModifier.Immune && des.m_damages.m_pickaxe == HitData.DamageModifier.Immune))
            {
                // Something low (a stump): its real middle and size from its shape (a stump's pivot is off to one side: it stood 2 m off
                // swinging over it), and the edge of it nearest to it.
                Bounds low = Size(c);
                if (low.size.y > 0.01f && low.size.y < 1.5f)
                {
                    Vector3 mid = low.center, away = from - mid;
                    mid.y = c.transform.position.y; away.y = 0f;
                    float radius = Mathf.Min(low.extents.x, low.extents.z) * 0.8f;
                    return away.sqrMagnitude > 0.01f ? mid + away.normalized * radius : mid;
                }
                Vector3 foot = c.transform.position, nearest = foot;
                float bestD = float.MaxValue;
                foreach (Collider col in c.GetComponentsInChildren<Collider>())
                {
                    if (col == null || !col.enabled || col.isTrigger || col is MeshCollider m && !m.convex) continue;
                    Vector3 p = col.ClosestPoint(from);
                    if (Flat(p, foot) > 1.5f) continue; // a branch or the crown, not the trunk
                    float dd = (p - from).sqrMagnitude;
                    if (dd < bestD) { bestD = dd; nearest = p; }
                }
                if (bestD == float.MaxValue) { Vector3 toMe = from - foot; toMe.y = 0f; if (toMe.sqrMagnitude > 0.01f) nearest = foot + toMe.normalized * 0.25f; }
                nearest.y = foot.y;
                return nearest;
            }
            Collider best = null;
            float d = float.MaxValue;
            foreach (Collider col in c.GetComponentsInChildren<Collider>())
            {
                if (col == null || !col.enabled || col.isTrigger) continue;
                Vector3 p = Closest(col, from);
                float dd = (p - from).sqrMagnitude;
                if (dd < d) { d = dd; best = col; }
            }
            if (best == null) return c.transform.position;
            Vector3 q = Closest(best, from);
            q.y = Mathf.Max(c.transform.position.y, q.y - 0.5f);
            return q;
        }

        private static float Flat(Vector3 a, Vector3 b) { a.y = 0f; b.y = 0f; return Vector3.Distance(a, b); }

        /// <summary>
        /// The nearest point of the collider's real shape (a long log's, not its box's). A concave mesh (a boulder, an ore vein) Unity cannot
        /// measure that way: a line from it to the mesh's middle, where it meets the surface (its box's corner was up to a metre off a round rock).
        /// </summary>
        private static Vector3 Closest(Collider col, Vector3 from)
        {
            if (!(col is MeshCollider mesh) || mesh.convex) return col.ClosestPoint(from);
            Vector3 start = new Vector3(from.x, Mathf.Clamp(from.y + 0.8f, col.bounds.min.y + 0.2f, col.bounds.max.y), from.z);
            Vector3 toMiddle = col.bounds.center - start;
            if (toMiddle.sqrMagnitude > 0.01f && col.Raycast(new Ray(start, toMiddle.normalized), out RaycastHit hit, toMiddle.magnitude + 1f)) return hit.point;
            return col.ClosestPointOnBounds(from);
        }

        // ---- choosing what to do next ------------------------------------------------------------------------

        private static Task Choose(BrainState st, Job jobs, Vector3 center, float radius)
        {
            Humanoid me = st.Body;
            Inventory inv = me.GetInventory();
            st.WorkNote = null;

            // Back home: what it brought goes straight into your chests (QualityOfLife), once it is in.
            if (st.SortWhenHome && Flat(center, me.transform.position) < radius * 0.8f) { st.SortWhenHome = false; SortHome(st); }

            // 1. A full bag goes to its chests first (sorted into yours at once, with QualityOfLife).
            if ((inv.GetEmptySlots() <= 1 || inv.GetAllItems().Count(i => !Keeps(me, i)) >= 18 || Carry.Weight(me) > Carry.Max(me) * 0.9f) && SortHome(st) > 0) return null;
            if (inv.GetEmptySlots() <= 1 || inv.GetAllItems().Count(i => !Keeps(me, i)) >= 18 || Carry.Weight(me) > Carry.Max(me) * 0.9f)
            {
                Container chest = Home.Chests(me).Where(c => !c.IsInUse() && Vector3.Distance(c.transform.position, center) < radius + 40f && HasRoom(c, me) && !Skipped(st, c))
                                     .OrderBy(c => Vector3.Distance(c.transform.position, me.transform.position)).FirstOrDefault(c => Brain.CanReach(me, c.transform.position));
                if (chest == null && Stows(me))
                    chest = YourChests(me, center, radius + 20f).Where(c => HasRoom(c, me) && !Skipped(st, c)).Take(6).FirstOrDefault(c => Brain.CanReach(me, c.transform.position));
                if (chest != null) return New(Kind.Store, chest, Job.None);
                if (inv.GetEmptySlots() == 0) { st.WorkNote = "its bag is full: give it a chest (Home tab) or empty its bag"; return null; }
            }

            // Hungry: no food on it and fewer than two meals in it. A player only heals from food, and so does it: hungry, food comes before
            // everything else. Weak: badly hurt with nothing in it, so it keeps out of fights (no hunting).
            bool hungry = !inv.GetAllItems().Any(Food.IsFood);
            st.Hungry = hungry && Food.Meals(me).Count < 2;
            st.Weak = Food.Meals(me).Count == 0 && me.GetHealthPercentage() < 0.4f;
            if (hungry && Time.time >= st.NextFoodLook)
            {
                st.NextFoodLook = Time.time + 20f;
                Container pantry = Home.Chests(me).Where(c => !c.IsInUse() && c.GetInventory().GetAllItems().Any(i => Food.IsFood(i) || IsCookable(i)) && !Skipped(st, c))
                                       .OrderBy(c => Vector3.Distance(c.transform.position, me.transform.position)).FirstOrDefault(c => Brain.CanReach(me, c.transform.position));
                if (pantry != null) return New(Kind.Store, pantry, Job.None);
                if (Food.Meals(me).Count == 0 && UsesPantry(me))
                {
                    Container yours = YourFood(me, center, radius, c => !Skipped(st, c));
                    if (yours != null) return New(Kind.Fetch, yours, Job.None);
                }
                if (Food.Meals(me).Count == 0) Talk.Tell(me, "I'm out of food and there's none in my chests. I'll forage and hunt, but some cooked meat would help.", "nofood", 20f);
            }
            // Its food slots running low: more from its chests, when they have some it would take.
            if (!hungry && Gear.FoodLow(me) && Time.time >= st.NextRefillLook)
            {
                st.NextRefillLook = Time.time + 120f;
                Container larder = Home.Chests(me).Where(c => !c.IsInUse() && !Skipped(st, c) && c.GetInventory().GetAllItems().Any(i => Gear.WantsFood(me, i)))
                                       .OrderBy(c => Vector3.Distance(c.transform.position, me.transform.position)).FirstOrDefault(c => Brain.CanReach(me, c.transform.position));
                if (larder != null) { st.Remember("went to its chest to fill its food slots"); return New(Kind.Store, larder, Job.None); }
            }

            // Its fires: the ones under cooking stations and by its bed, topped up before they go out (wood from its bag or its chests).
            if (Time.time >= st.NextFireLook)
            {
                st.NextFireLook = Time.time + 20f;
                Fireplace fire = Fires.Low(me, center, radius);
                if (fire != null) return New(Kind.Fuel, fire, Job.None);
            }

            // Your base: damaged walls, floors and the rest repaired with a hammer, as you would (a hammer it makes itself when it can).
            if (Time.time >= st.NextMendLook)
            {
                st.NextMendLook = Time.time + 20f;
                WearNTear damaged = Mending.Damaged(me, center, radius, c => !Skipped(st, c) && Brain.CanReach(me, c.transform.position, 4f)); // (somewhere to stand within a hammer's reach of it)
                if (damaged != null)
                {
                    if (Mending.Hammer(me) == null) Mending.MakeHammer(st);
                    if (Mending.Hammer(me) != null) return New(Kind.Mend, damaged, Job.None);
                    Talk.Tell(me, "Parts of the base are damaged. Give me a hammer and I'll fix them.", "nohammer", 30f);
                }
            }

            // 2. Better gear: an upgrade at its workbench (or forge...) when it has the materials, in its bag or its chests; else what it is
            //    working toward (Goals), and the in-between materials for it it can make now.
            if (Time.time >= st.NextUpgradeLook)
            {
                st.NextUpgradeLook = Time.time + 30f;
                var up = Upgrades.Find(me, center, radius);
                if (up != null) { Task u = New(Kind.Upgrade, up.Value.Value, Job.None); u.Item = up.Value.Key; return u; }
                var craft = Upgrades.FindCraft(me, center, radius);
                if (craft != null && craft.Value.Value == null) { Upgrades.Craft(st, craft.Value.Key, null); craft = null; } // no station needed: made on the spot
                if (craft != null) { Task t2 = New(Kind.Craft, craft.Value.Value, Job.None); t2.Recipe = craft.Value.Key; return t2; }
                foreach (string gone in st.Unfindable.Where(kv => kv.Value < Time.time).Select(kv => kv.Key).ToList()) st.Unfindable.Remove(gone);
                st.Goal = Goals.Pick(me, center, radius, st);
                Goals.Announce(st, st.Goal);
                var step = Goals.StepReady(me, st.Goal);
                if (step != null && step.Value.Value == null) { Upgrades.Craft(st, step.Value.Key, null, true); step = null; }
                if (step != null) { Task t3 = New(Kind.Craft, step.Value.Value, Job.None); t3.Recipe = step.Value.Key; t3.ForGoal = true; return t3; }
            }

            // What its goal needs decides its jobs too, when you have not ticked any.
            Job goalJobs = Goals.JobsFor(st.Goal, out HashSet<string> goalPrey);
            if (JobsOf(me) == Job.None) jobs |= goalJobs & ~Job.Loot;

            // Its own meals: raw food it carries goes on a cooking station near home.
            if ((jobs & Job.Cook) != 0)
            {
                CookingStation stove = Kitchen.Find(me, center, radius);
                if (stove == null && st.Hungry && inv.GetAllItems().Any(IsCookable))
                    Talk.Tell(me, "I have raw food but no cooking station near home to cook it on. Could you build one by a fire?", "nostove", 20f);
                if (stove != null)
                {
                    foreach (CookingStation.ItemConversion conv in stove.m_conversion) if (conv.m_to != null) st.Wanted.Add(conv.m_to.name);
                    st.WorkSpot = stove.transform.position;
                    return New(Kind.Cook, stove, Job.Cook);
                }
            }

            // Hunting: deer and boar near home, for meat and hides (with its bow if it has one); for its goal, the animals that drop what it needs.
            // Weak (badly hurt, nothing eaten), only what never fights back: a deer is the meal that gets it going again.
            if ((jobs & Job.Hunt) != 0 && (!st.Weak || Companion.BestRanged(me) != null) || (st.Weak && st.Hungry && Companion.BestRanged(me) != null))
            {
                bool forGoal = goalPrey.Count > 0 && !st.Hungry;
                HashSet<string> kinds = st.Weak ? Harmless : forGoal ? goalPrey : Prey;
                float reachable = Companion.BestRanged(me) != null ? float.MaxValue : 25f; // without a bow only what it can get to before it runs
                // For its goal, and fit, further out too: a hunting trip, as far as the world around you is loaded (about 170 m).
                bool fit = forGoal && !st.Weak && me.GetHealthPercentage() > 0.6f;
                Character Find(float range) => Character.GetAllCharacters().Where(ch => ch != null && !ch.IsDead() && !ch.IsTamed() && kinds.Contains(Utils.GetPrefabName(ch.gameObject))
                        && !(st.LeaveAlone.TryGetValue(ch, out float until) && Time.time < until)
                        && (!Harmless.Contains(Utils.GetPrefabName(ch.gameObject)) || Vector3.Distance(ch.transform.position, me.transform.position) < reachable) // boars come at it: only runners need a bow
                        && Vector3.Distance(ch.transform.position, center) < range && !Skipped(st, ch))
                    .OrderBy(ch => Vector3.Distance(ch.transform.position, me.transform.position)).FirstOrDefault();
                Character prey = Find(radius);
                bool trip = false;
                if (prey == null && fit) { prey = Find(170f); trip = prey != null; }
                if (prey != null)
                {
                    Task h = New(Kind.Hunt, prey, Job.Hunt);
                    h.ForGoal = forGoal;
                    h.Trip = trip;
                    if (trip)
                    {
                        if (Time.time >= st.TripUntil)
                        {
                            string what = Localization.instance.Localize(prey.m_name).ToLowerInvariant();
                            Talk.Mention(me, $"No {what} near home, so I'm going hunting for one, {Flat(prey.transform.position, center):0} m {Compass(prey.transform.position - center)} of home, for my {st.Goal?.What}.", "hunt:" + what, 10f);
                            st.Remember($"went hunting {what} further out");
                        }
                        st.TripUntil = Time.time + 180f;
                    }
                    return h;
                }
            }

            // 3. What its own work dropped (or anything, with Loot ticked).
            ItemDrop loot = ItemDrops().Where(d => d != null && Vector3.Distance(d.transform.position, center) < radius && !Skipped(st, d)
                    && ((jobs & Job.Loot) != 0 || (st.Wanted.Contains(Utils.GetPrefabName(d.gameObject)) && Vector3.Distance(d.transform.position, st.WorkSpot) < 10f))
                    && inv.CanAddItem(d.m_itemData))
                .OrderBy(d => Vector3.Distance(d.transform.position, me.transform.position)).FirstOrDefault();
            if (loot != null) return New(Kind.PickUp, loot, Job.Loot);

            // 4. Something to work on.
            ItemDrop.ItemData axe = Axe(me), pick = Pickaxe(me);
            var seen = new HashSet<GameObject>();
            bool goalSeen = false;
            Task best = null;
            float bestScore = float.MaxValue;
            foreach (Collider col in Physics.OverlapSphere(center, radius, ~0, QueryTriggerInteraction.Collide))
            {
                GameObject go = col.attachedRigidbody != null ? col.attachedRigidbody.gameObject : col.transform.root.gameObject;
                if (!seen.Add(go)) continue;
                Task t = Consider(st, go, jobs, axe, pick);
                if (t == null || Skipped(st, t.Target) || ClaimedByOther(st, t.Target)) continue;
                t.ForGoal = st.Goal != null && Drops(t.Target).Any(st.Goal.Wants);
                if (t.ForGoal) goalSeen = true;
                float score = Vector3.Distance(t.Target.transform.position, me.transform.position) + Priority(t) - (t.ForGoal ? 60f : 0f) - (t.Edible && st.Hungry ? 200f : 0f); // food when hungry, then its goal
                if (score < bestScore) { bestScore = score; best = t; }
            }
            if (best == null)
            {
                var missing = new List<string>();
                if ((jobs & Job.Wood) != 0 && axe == null) missing.Add("an axe");
                if ((jobs & (Job.Stone | Job.Ore)) != 0 && pick == null) missing.Add("a pickaxe");
                st.WorkNote = st.Goal != null && st.Goal.Raw.Count > 0 && !goalSeen ? $"looking for {string.Join(" or ", st.Goal.Names.Values)} for its {st.Goal.What} (none near home)"
                            : missing.Count > 0 ? $"needs {string.Join(" and ", missing)} for its jobs" : $"nothing left to gather within {radius:0} m";
            }
            // Nothing near home for its goal: a trip for it, as far as the world is loaded around you (about 170 m from home), when it is fit.
            bool searched = false;
            if (st.Goal != null && st.Goal.Raw.Count > 0 && !goalSeen && JobsOf(me) == Job.None && !st.Weak && !st.Hungry && me.GetHealthPercentage() > 0.6f
                && (Time.time >= st.NextTripLook || Time.time < st.TripUntil))
            {
                st.NextTripLook = Time.time + 20f;
                Task trip = FindTrip(st, center, radius, jobs | goalJobs, axe, pick);
                searched = trip == null;
                if (trip != null)
                {
                    bool starting = Time.time >= st.TripUntil;
                    st.TripUntil = Time.time + 300f;
                    if (starting)
                    {
                        Vector3 way = trip.Target.transform.position - center;
                        string names = string.Join(" and ", st.Goal.Names.Values);
                        Talk.Mention(me, $"Nothing near home has {names}, so I'm going to get some, {Flat(trip.Target.transform.position, center):0} m {Compass(way)} of home.", "trip:" + st.Goal.What, 10f);
                        st.Remember($"set off on a trip for {names}");
                        Journal.Trip(me, $"Went on a trip {Flat(trip.Target.transform.position, center):0} m {Compass(way)} of home for {names}.");
                    }
                    return trip;
                }
                if (Time.time < st.TripUntil) { st.TripUntil = 0f; st.Remember("came back from its trip"); } // done (or nothing more out there): home
            }
            if (searched && st.Goal != null && st.Goal.Raw.Count > 0 && !goalSeen && JobsOf(me) == Job.None && !goalPrey.Any())
            {
                // Nothing near home drops what its goal needs: those things count as "ask for them" for ten minutes, so it picks a goal it can do.
                Talk.Tell(me, $"I can't find any {string.Join(" or ", st.Goal.Names.Values)} around here for my {st.Goal.What}, even further out. I'll look while you're away, or bring me some.", "far:" + st.Goal.What, 30f);
                foreach (string item in st.Goal.Raw.Keys) st.Unfindable[item] = Time.time + 600f;
                st.NextUpgradeLook = 0f;
            }
            if (best == null && inv.GetEmptySlots() == 0) Talk.Tell(me, "My bag is full and none of my chests has room. Give me another chest (Home tab).", "full", 15f);
            Claim(st, best);
            return best;
        }

        private static float Priority(Task t)
        {
            // fallen logs before standing trees (finish what is started), small rocks before big
            if (t.Target is TreeLog) return -15f;
            if (t.Target is Destructible) return -5f;
            if (t.Target is TreeBase) return 5f;
            return 0f;
        }

        internal static Task New(Kind kind, Component target, Job job) => new Task { Kind = kind, Target = target, Job = job, Started = Time.time, LastClose = Time.time,
                                                                                     WasTree = target is TreeBase, Pos = target != null ? target.transform.position : Vector3.zero };

        // ---- safer chopping (the ideas from Offline Companions) ----

        private static readonly Dictionary<int, KeyValuePair<long, float>> Claims = new Dictionary<int, KeyValuePair<long, float>>();

        /// <summary>Another companion is on that tree or rock already.</summary>
        private static bool ClaimedByOther(BrainState st, Component target) =>
            target != null && Claims.TryGetValue(target.gameObject.GetInstanceID(), out var claim) && claim.Key != Companion.IdOf(st.Body) && claim.Value > Time.time;

        private static void Claim(BrainState st, Task t)
        {
            if (t?.Target == null || t.Kind != Kind.Hit) return;
            if (Claims.Count > 200) foreach (int gone in Claims.Where(kv => kv.Value.Value < Time.time).Select(kv => kv.Key).ToList()) Claims.Remove(gone);
            Claims[t.Target.gameObject.GetInstanceID()] = new KeyValuePair<long, float>(Companion.IdOf(st.Body), Time.time + 60f);
        }

        /// <summary>A standing tree this near something you built would fall on it: left standing (unless you point at it).</summary>
        private static bool NearBuildings(Vector3 at) =>
            Physics.OverlapSphere(at, 10f, LayerMask.GetMask("piece", "piece_nonsolid")).Any(col => col.GetComponentInParent<Piece>() is Piece p && p.IsPlacedByPlayer());

        /// <summary>The tree it chopped came down: a few steps aside (uphill if it can), out of the way of the falling trunk.</summary>
        private static void BeginClear(BrainState st, Vector3 tree)
        {
            Humanoid me = st.Body;
            Vector3 toTree = tree - me.transform.position; toTree.y = 0f;
            Vector3 side = Vector3.Cross(Vector3.up, toTree.sqrMagnitude > 0.01f ? toTree.normalized : me.transform.forward);
            Vector3 a = me.transform.position + side * 4f - toTree.normalized, b = me.transform.position - side * 4f - toTree.normalized;
            float ha = 0f, hb = 0f;
            if (ZoneSystem.instance != null) { ZoneSystem.instance.GetSolidHeight(a, out ha); ZoneSystem.instance.GetSolidHeight(b, out hb); }
            st.ClearTo = ha >= hb ? a : b;
            st.ClearUntil = Time.time + 2f;
        }

        private static bool Clearing(BrainState st, Action<Vector3, float, bool> moveTo)
        {
            if (Time.time >= st.ClearUntil) return false;
            moveTo(st.ClearTo, 0.5f, true);
            Brain.Status(st, "stepping clear of the falling tree");
            return true;
        }

        private static void Drop(BrainState st)
        {
            if (st.Task != null && st.Task.Kind == Kind.Hit && st.Task.WasTree && st.Task.Target == null) BeginClear(st, st.Task.Pos);
            st.Task = null;
        }

        internal static Task Consider(BrainState st, GameObject go, Job jobs, ItemDrop.ItemData axe, ItemDrop.ItemData pick, bool ordered = false)
        {
            if (!ordered && go.GetComponent<TreeBase>() != null && NearBuildings(go.transform.position)) return null; // it would fall on what you built
            if (go.GetComponent<Piece>() != null || go.GetComponent<Character>() != null) return null; // never what players built, never creatures
            if ((jobs & Job.Wood) != 0 && axe != null)
            {
                TreeLog log = go.GetComponent<TreeLog>();
                if (log != null && axe.m_shared.m_toolTier >= log.m_minToolTier) return Wood(st, log, log.m_dropWhenDestroyed);
                TreeBase tree = go.GetComponent<TreeBase>();
                if (tree != null && axe.m_shared.m_toolTier >= tree.m_minToolTier) return Wood(st, tree, tree.m_dropWhenDestroyed);
            }
            Destructible des = go.GetComponent<Destructible>();
            if (des != null)
            {
                DropTable drops = go.GetComponent<DropOnDestroyed>()?.m_dropWhenDestroyed;
                if (drops == null || drops.m_drops.Count == 0) return null; // bushes and the like: nothing to gain
                bool wood = des.m_destructibleType == DestructibleType.Tree || des.m_damages.m_chop != HitData.DamageModifier.Immune && des.m_damages.m_pickaxe == HitData.DamageModifier.Immune;
                if (wood && (jobs & Job.Wood) != 0 && axe != null && axe.m_shared.m_toolTier >= des.m_minToolTier) return Wood(st, des, drops);
                if (!wood && pick != null && pick.m_shared.m_toolTier >= des.m_minToolTier && des.m_damages.m_pickaxe != HitData.DamageModifier.Immune)
                {
                    Job job = IsOre(drops) ? Job.Ore : Job.Stone;
                    if ((jobs & job) != 0) return Rock(st, des, drops, job);
                }
                return null;
            }
            if (pick != null && (jobs & (Job.Stone | Job.Ore)) != 0)
            {
                MineRock rock = go.GetComponent<MineRock>();
                if (rock != null && pick.m_shared.m_toolTier >= rock.m_minToolTier) { Job job = IsOre(rock.m_dropItems) ? Job.Ore : Job.Stone; if ((jobs & job) != 0) return Rock(st, rock, rock.m_dropItems, job); }
                MineRock5 rock5 = go.GetComponent<MineRock5>();
                if (rock5 != null && pick.m_shared.m_toolTier >= rock5.m_minToolTier) { Job job = IsOre(rock5.m_dropItems) ? Job.Ore : Job.Stone; if ((jobs & job) != 0) return Rock(st, rock5, rock5.m_dropItems, job); }
            }
            if ((jobs & Job.Forage) != 0)
            {
                Pickable p = go.GetComponent<Pickable>();
                // Only what grows back by itself (wild berries, mushrooms, thistle...): planted crops do not, and are never touched.
                bool wanted = p != null && p.m_itemPrefab != null && st.Goal != null && st.Goal.Wants(p.m_itemPrefab.name);
                if (p != null && p.m_itemPrefab != null && !(Companion.Zdo(p)?.GetBool(ZDOVars.s_picked, false) ?? true) && (p.m_respawnTimeMinutes > 0f || wanted || ordered) // (pointed at: a stone, a flint on the shore)
                    && !Goals.IsCrop(Utils.GetPrefabName(p.gameObject)) && !(Heightmap.FindHeightmap(p.transform.position)?.IsCultivated(p.transform.position) ?? false))
                {
                    ItemDrop.ItemData item = p.m_itemPrefab.GetComponent<ItemDrop>()?.m_itemData;
                    bool edible = item != null && (Food.IsFood(item) || IsCookable(item));
                    if (st.Hungry && !edible && !ordered) return null; // hungry: berries and mushrooms, not dandelions
                    Task t = New(Kind.Pick, p, Job.Forage);
                    t.Edible = edible;
                    return t;
                }
            }
            return null;
        }

        /// <summary>
        /// The nearest thing beyond its home's radius (out to about 170 m, as far as the world around you is loaded) that drops what its goal
        /// needs: a fallen log, a rock, flint on the shore.
        /// </summary>
        private static Task FindTrip(BrainState st, Vector3 center, float radius, Job jobs, ItemDrop.ItemData axe, ItemDrop.ItemData pick)
        {
            Humanoid me = st.Body;
            var seen = new HashSet<GameObject>();
            Task best = null;
            float bestDist = float.MaxValue;
            foreach (Collider col in Physics.OverlapSphere(center, 170f, ~0, QueryTriggerInteraction.Collide))
            {
                GameObject go = col.attachedRigidbody != null ? col.attachedRigidbody.gameObject : col.transform.root.gameObject;
                if (!seen.Add(go)) continue;
                Task t = Consider(st, go, jobs, axe, pick);
                if (t == null || Skipped(st, t.Target) || !Drops(t.Target).Any(st.Goal.Wants)) continue;
                float d = Vector3.Distance(t.Target.transform.position, me.transform.position);
                if (d >= bestDist) continue;
                bestDist = d;
                best = t;
            }
            if (best != null) { best.Trip = true; best.ForGoal = true; }
            return best;
        }

        internal static string Compass(Vector3 way)
        {
            string[] names = { "north", "north-east", "east", "south-east", "south", "south-west", "west", "north-west" };
            float angle = (Mathf.Atan2(way.x, way.z) * Mathf.Rad2Deg + 360f + 22.5f) % 360f;
            return names[Mathf.FloorToInt(angle / 45f) % 8];
        }

        /// <summary>The item prefab names something drops when worked (a tree, a rock, a plant).</summary>
        internal static IEnumerable<string> Drops(Component c)
        {
            GameObject go = c.gameObject;
            if (go.GetComponent<Pickable>() is Pickable p) return p.m_itemPrefab != null ? new[] { p.m_itemPrefab.name } : new string[0];
            DropTable t = go.GetComponent<TreeBase>()?.m_dropWhenDestroyed ?? go.GetComponent<TreeLog>()?.m_dropWhenDestroyed ?? go.GetComponent<MineRock>()?.m_dropItems
                          ?? go.GetComponent<MineRock5>()?.m_dropItems ?? go.GetComponent<DropOnDestroyed>()?.m_dropWhenDestroyed;
            return t?.m_drops?.Where(d => d.m_item != null).Select(d => d.m_item.name) ?? Enumerable.Empty<string>();
        }

        private static Task Wood(BrainState st, Component c, DropTable drops) { Want(st, drops); return New(Kind.Hit, c, Job.Wood); }
        private static Task Rock(BrainState st, Component c, DropTable drops, Job job) { Want(st, drops); return New(Kind.Hit, c, job); }

        private static void Want(BrainState st, DropTable drops)
        {
            if (drops?.m_drops == null) return;
            foreach (DropTable.DropData d in drops.m_drops) if (d.m_item != null) st.Wanted.Add(d.m_item.name);
        }

        internal static bool IsOre(DropTable drops) => drops?.m_drops != null && drops.m_drops.Any(d => d.m_item != null && OreWords.Any(w => d.m_item.name.Contains(w)));

        private static readonly AccessTools.FieldRef<List<ItemDrop>> Instances = AccessTools.StaticFieldRefAccess<List<ItemDrop>>(AccessTools.Field(typeof(ItemDrop), "s_instances"));
        private static IEnumerable<ItemDrop> ItemDrops() => Instances() ?? new List<ItemDrop>();

        // ---- your food, when it is starving ---------------------------------------------------------------------

        /// <summary>A plant that gives food (berries, mushrooms) or raw food to cook.</summary>
        internal static bool Edible(Component c)
        {
            ItemDrop.ItemData item = (c as Pickable)?.m_itemPrefab?.GetComponent<ItemDrop>()?.m_itemData;
            return item != null && (Food.IsFood(item) || IsCookable(item));
        }

        /// <summary>Move some of a chest's item into its bag. False if it did not fit.</summary>
        internal static bool Move(Container chest, Humanoid me, ItemDrop.ItemData item, int n, bool loan = false)
        {
            ZNetView view = chest.GetComponent<ZNetView>();
            if (view == null || !view.IsValid()) return false;
            if (!view.IsOwner()) view.ClaimOwnership();
            if (!me.GetInventory().CanAddItem(item, n)) return false;
            ItemDrop.ItemData copy = item.Clone();
            copy.m_stack = n;
            copy.m_equipped = false;
            if (loan) Loans.Mark(copy, chest); // from your chest: what it does not eat goes back there
            if (!me.GetInventory().AddItem(copy)) return false;
            chest.GetInventory().RemoveItem(item, n);
            return true;
        }

        /// <summary>A chest of its player's at home (not a companion's) with food in it, that it may open (no ward against it).</summary>
        internal static Container YourFood(Humanoid me, Vector3 center, float radius, Func<Container, bool> allowed = null) =>
            UnityEngine.Object.FindObjectsByType<Container>(FindObjectsSortMode.None)
                .Where(c => Home.IsChest(c) && Home.IdOn(c) == 0L && !c.IsInUse() && Vector3.Distance(c.transform.position, center) < radius && (allowed == null || allowed(c))
                            && c.GetInventory().GetAllItems().Any(Food.IsFood) && (!c.m_checkGuardStone || PrivateArea.CheckAccess(c.transform.position, 0f, false)))
                .OrderBy(c => Vector3.Distance(c.transform.position, me.transform.position)).Take(6).FirstOrDefault(c => Brain.CanReach(me, c.transform.position));

        /// <summary>Up to "max" of the best food in a chest of yours into its bag. What it took ("3 cooked meat"), or null.</summary>
        internal static string TakeFoodFrom(Humanoid me, Container chest, int max)
        {
            if (chest == null || chest.IsInUse()) return null;
            var took = new Dictionary<string, int>();
            int left = max;
            foreach (ItemDrop.ItemData food in chest.GetInventory().GetAllItems().Where(Food.IsFood).OrderByDescending(i => i.m_shared.m_food + i.m_shared.m_foodStamina).ToList())
            {
                if (left <= 0) break;
                int n = Mathf.Min(left, food.m_stack);
                string name = Localization.instance.Localize(food.m_shared.m_name);
                if (!Move(chest, me, food, n, true)) continue;
                took[name] = (took.TryGetValue(name, out int had) ? had : 0) + n;
                left -= n;
            }
            if (took.Count == 0) return null;
            string list = string.Join(", ", took.Select(kv => $"{kv.Value} {kv.Key.ToLowerInvariant()}"));
            Plugin.Instance?.Note($"{Companion.NameOf(me)} took {list} to eat from the chest at {chest.transform.position:F0}");
            return list;
        }

        /// <summary>Up to five of the best food in the chest (enough to eat twice or three times), and it tells its player what it took.</summary>
        private static void TakeFood(BrainState st, Container chest)
        {
            Humanoid me = st.Body;
            string list = TakeFoodFrom(me, chest, 5);
            if (list == null) { Skip(st, chest, "no room for the food", 5f); return; }
            st.Remember($"took {list} from your chest to eat");
            Talk.Tell(me, $"I had nothing to eat, so I took {list} from your chest. Thanks!");
        }

        // ---- its chests --------------------------------------------------------------------------------------

        /// <summary>What it keeps on itself: anything it wears or could use (weapons, armour, shields, tools, ammo, healing potions).</summary>
        public static bool Keeps(Humanoid h, ItemDrop.ItemData i) => Gear.InSlot(i) || h.IsItemEquiped(i) || Companion.Useful(h, i) || IsTool(i) || IsCookable(i) || Mending.IsHammer(i);

        private static bool HasRoom(Container chest, Humanoid h) =>
            chest.GetInventory().HaveEmptySlot() || h.GetInventory().GetAllItems().Any(i => !Keeps(h, i) && chest.GetInventory().CanAddItem(i, 1));

        /// <summary>
        /// At one of its chests: put away everything it does not keep, then take what would make it better: armour and a weapon better than
        /// its own, arrows for its bow, healing potions, and a missing axe or pickaxe for its jobs.
        /// </summary>
        public static void Store(BrainState st, Container chest)
        {
            Humanoid me = st.Body;
            ZNetView view = chest.GetComponent<ZNetView>();
            if (view == null || !view.IsValid() || chest.IsInUse()) return;
            if (chest.m_checkGuardStone && !PrivateArea.CheckAccess(chest.transform.position, 0f, false)) { Skip(st, chest, "it is behind a ward", 5f); return; }
            if (!view.IsOwner()) view.ClaimOwnership();
            Inventory mine = me.GetInventory(), its = chest.GetInventory();
            int put = 0, took = 0;
            foreach (ItemDrop.ItemData item in mine.GetAllItems().Where(i => !Keeps(me, i)).ToList())
            {
                if (!its.CanAddItem(item)) continue;
                its.MoveItemToThis(mine, item);
                put++;
            }
            bool yours = Home.IdOn(chest) == 0L; // a chest of its player's: it only puts things in
            Job jobs = JobsOf(me);
            if (!yours)
                foreach (ItemDrop.ItemData item in its.GetAllItems().ToList())
                {
                    if (!mine.HaveEmptySlot() && !mine.CanAddItem(item)) break;
                    if (Wants(me, item, jobs)) { item.m_equipped = false; mine.MoveItemToThis(its, item); took++; }
                }
            if (put + took > 0)
            {
                st.Remember(yours ? $"put away {put} things in your chest" : $"at its chest: put away {put}, took {took}");
                Plugin.Instance?.Note($"{Companion.NameOf(me)} put away {put} and took {took} at its chest {chest.transform.position:F0}");
            }
        }

        private static bool Wants(Humanoid me, ItemDrop.ItemData item, Job jobs)
        {
            var type = item.m_shared.m_itemType;
            List<ItemDrop.ItemData> have = me.GetInventory().GetAllItems();
            if (type == ItemDrop.ItemData.ItemType.Helmet || type == ItemDrop.ItemData.ItemType.Chest || type == ItemDrop.ItemData.ItemType.Legs || type == ItemDrop.ItemData.ItemType.Shoulder)
                return item.GetArmor() > (have.Where(i => i.m_shared.m_itemType == type).Select(i => i.GetArmor()).DefaultIfEmpty(0f).Max()) + 0.5f;
            if (type == ItemDrop.ItemData.ItemType.Shield)
                return item.m_shared.m_blockPower > have.Where(i => i.m_shared.m_itemType == type).Select(i => i.m_shared.m_blockPower).DefaultIfEmpty(0f).Max() + 0.5f;
            if (Companion.IsRanged(item)) return Companion.BestRanged(me) == null && have.Any(a => a.m_shared.m_ammoType == item.m_shared.m_ammoType && a.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Ammo);
            if (item.IsWeapon() && !IsTool(item) && Companion.Useful(me, item))
                return item.GetDamage().GetTotalDamage() > (Companion.BestMelee(me)?.GetDamage().GetTotalDamage() ?? 0f) + 1f;
            if (type == ItemDrop.ItemData.ItemType.Ammo)
            {
                ItemDrop.ItemData bow = Companion.BestRanged(me) ?? have.FirstOrDefault(Companion.IsRanged);
                return bow != null && item.m_shared.m_ammoType == bow.m_shared.m_ammoType && have.Where(a => a.m_shared.m_name == item.m_shared.m_name).Sum(a => a.m_stack) < 40;
            }
            if (Food.IsFood(item)) return have.Where(Food.IsFood).Sum(f => f.m_stack) < 10;
            if (Gear.WantsFood(me, item)) return true; // for its food slots
            if (IsCookable(item)) return have.Where(Food.IsFood).Sum(f => f.m_stack) < 10 && have.Where(IsCookable).Sum(f => f.m_stack) < 10; // to cook for itself
            if (type == ItemDrop.ItemData.ItemType.Consumable && Companion.Useful(me, item)) return Companion.HealingPotions(me).Sum(p => p.m_stack) < 3;
            if ((jobs & Job.Wood) != 0 && item.m_shared.m_damages.m_chop > 0f && item.IsWeapon() && Axe(me) == null) return true;
            if ((jobs & (Job.Stone | Job.Ore)) != 0 && item.m_shared.m_damages.m_pickaxe > 0f && Pickaxe(me) == null) return true;
            return false;
        }
    }

    /// <summary>
    /// Upgrading its gear as a player does: an item it keeps (weapon, armour, shield, tool) goes up a quality level at a station of the kind it
    /// is made at, of the level the game asks for that quality, paying the game's own materials for that level, from its bag first and then
    /// its chests near the station. It does this while living at home.
    /// </summary>
    internal static class Upgrades
    {
        private static readonly AccessTools.FieldRef<List<CraftingStation>> Stations = AccessTools.StaticFieldRefAccess<List<CraftingStation>>(AccessTools.Field(typeof(CraftingStation), "m_allStations"));

        public static List<CraftingStation> StationsNear(Vector3 center, float range) =>
            (Stations() ?? new List<CraftingStation>()).Where(s => s != null && !s.m_upgrader && Vector3.Distance(s.transform.position, center) < range).ToList();

        /// <summary>
        /// What a recipe needs at an ordinary station (workbench, forge...), as the game counts it (Player.HaveRequirementItems): items marked
        /// as upgrader resources (the battle idols) are only for the upgrader stations, and are left out. For recipes where any one ingredient
        /// will do, it pays them all (never less than the game asks).
        /// </summary>
        public static IEnumerable<Piece.Requirement> Needs(Recipe r) =>
            r == null ? Enumerable.Empty<Piece.Requirement>() : r.m_resources.Where(q => q.m_resItem != null && !q.m_upgraderResource);

        public static bool Upgradable(ItemDrop.ItemData i)
        {
            var t = i.m_shared.m_itemType;
            bool gear = i.IsWeapon() || Work.IsTool(i) || t == ItemDrop.ItemData.ItemType.Shield || t == ItemDrop.ItemData.ItemType.Helmet || t == ItemDrop.ItemData.ItemType.Chest
                        || t == ItemDrop.ItemData.ItemType.Legs || t == ItemDrop.ItemData.ItemType.Shoulder;
            return gear && i.m_quality < i.m_shared.m_maxQuality;
        }

        internal static List<Container> ChestsNear(Humanoid me, Vector3 at) => Home.Chests(me).Where(c => c != null && !c.IsInUse() && Vector3.Distance(c.transform.position, at) < 25f).ToList();

        private static int Have(Humanoid me, List<Container> chests, string name) => me.GetInventory().CountItems(name) + chests.Sum(c => c.GetInventory().CountItems(name));

        /// <summary>Something it can upgrade now, and where (null if nothing).</summary>
        public static KeyValuePair<ItemDrop.ItemData, CraftingStation>? Find(Humanoid me, Vector3 center, float radius)
        {
            List<CraftingStation> stations = StationsNear(center, radius + 10f);
            if (stations.Count == 0) return null;
            foreach (ItemDrop.ItemData item in me.GetInventory().GetAllItems().Where(Upgradable).OrderByDescending(i => me.IsItemEquiped(i)))
            {
                Recipe recipe = ObjectDB.instance?.GetRecipe(item);
                if (recipe == null || recipe.m_craftingStation == null) continue;
                int next = item.m_quality + 1;
                CraftingStation station = stations.FirstOrDefault(s => s.m_name == recipe.m_craftingStation.m_name && s.GetLevel() >= recipe.GetRequiredStationLevel(next));
                if (station == null) continue;
                List<Container> chests = ChestsNear(me, station.transform.position);
                if (Upgrades.Needs(recipe).All(r => r.m_resItem == null || Have(me, chests, r.m_resItem.m_itemData.m_shared.m_name) >= r.GetAmount(next)))
                    return new KeyValuePair<ItemDrop.ItemData, CraftingStation>(item, station);
            }
            return null;
        }

        /// <summary>
        /// Something new worth making, and where (null if nothing): armour better than what it has for that slot, a better shield, a better
        /// weapon, an axe or pickaxe of a higher tier, a bow when it has none, or arrows for its bow when it is low; at a station of the
        /// recipe's kind and level near home, with all the materials in its bag and its chests.
        /// </summary>
        public static KeyValuePair<Recipe, CraftingStation>? FindCraft(Humanoid me, Vector3 center, float radius)
        {
            if (ObjectDB.instance == null) return null;
            List<CraftingStation> stations = StationsNear(center, radius + 10f);
            foreach (Recipe r in ObjectDB.instance.m_recipes)
            {
                if (r == null || !r.m_enabled || r.m_item == null) continue;
                if (!WorthMaking(me, r.m_item.m_itemData)) continue;
                CraftingStation station = r.m_craftingStation == null ? null : stations.FirstOrDefault(s => s.m_name == r.m_craftingStation.m_name && s.GetLevel() >= Mathf.Max(1, r.m_minStationLevel));
                if (station == null && r.m_craftingStation != null) continue;
                List<Container> chests = ChestsNear(me, station != null ? station.transform.position : me.transform.position); // (no station: its chests near it)
                if (Upgrades.Needs(r).All(q => q.m_resItem == null || Have(me, chests, q.m_resItem.m_itemData.m_shared.m_name) >= q.GetAmount(1)))
                    return new KeyValuePair<Recipe, CraftingStation>(r, station);
            }
            return null;
        }

        public static bool WorthMaking(Humanoid me, ItemDrop.ItemData made)
        {
            var have = me.GetInventory().GetAllItems();
            var t = made.m_shared.m_itemType;
            if (t == ItemDrop.ItemData.ItemType.Helmet || t == ItemDrop.ItemData.ItemType.Chest || t == ItemDrop.ItemData.ItemType.Legs || t == ItemDrop.ItemData.ItemType.Shoulder)
                return made.GetArmor() > have.Where(i => i.m_shared.m_itemType == t).Select(i => i.GetArmor()).DefaultIfEmpty(0f).Max() + 1f;
            if (t == ItemDrop.ItemData.ItemType.Shield)
                return made.m_shared.m_blockPower > have.Where(i => i.m_shared.m_itemType == t).Select(i => i.m_shared.m_blockPower).DefaultIfEmpty(0f).Max() + 1f;
            if (made.m_shared.m_damages.m_chop > 0f && made.IsWeapon() && made.m_shared.m_skillType == Skills.SkillType.Axes)
                return made.m_shared.m_toolTier > (Work.Axe(me)?.m_shared.m_toolTier ?? -1);
            if (made.m_shared.m_damages.m_pickaxe > 0f)
                return made.m_shared.m_toolTier > (Work.Pickaxe(me)?.m_shared.m_toolTier ?? -1);
            if (t == ItemDrop.ItemData.ItemType.Ammo)
            {
                ItemDrop.ItemData bow = have.FirstOrDefault(i => Companion.IsRanged(i) && !Companion.IsStaff(i));
                return bow != null && made.m_shared.m_ammoType == bow.m_shared.m_ammoType && have.Where(a => a.m_shared.m_ammoType == bow.m_shared.m_ammoType && a.m_shared.m_itemType == t).Sum(a => a.m_stack) < 20;
            }
            if (Companion.IsRanged(made) && !Companion.IsStaff(made)) return !have.Any(i => Companion.IsRanged(i) && !Companion.IsStaff(i));
            if (made.IsWeapon() && !Work.IsTool(made) && made.m_shared.m_itemType != ItemDrop.ItemData.ItemType.Torch && made.m_shared.m_skillType != Skills.SkillType.Unarmed && made.m_shared.m_attack != null && made.m_shared.m_attack.m_attackType != Attack.AttackType.Projectile)
                return made.GetDamage().GetTotalDamage() > (Companion.BestMelee(me)?.GetDamage().GetTotalDamage() ?? 0f) + 2f;
            return false;
        }

        /// <summary>Make it (paying from its bag, then its chests near the station). What it made ("made a flint axe"), or null.</summary>
        public static string Craft(BrainState st, Recipe r, CraftingStation station, bool forGoal = false)
        {
            Humanoid me = st.Body;
            if (r == null || (!forGoal && !WorthMaking(me, r.m_item.m_itemData))) return null;
            List<Container> chests = ChestsNear(me, station != null ? station.transform.position : me.transform.position); // (no station: its chests near it)
            if (!Upgrades.Needs(r).All(q => q.m_resItem == null || Have(me, chests, q.m_resItem.m_itemData.m_shared.m_name) >= q.GetAmount(1))) return null;
            if (!me.GetInventory().HaveEmptySlot()) return null;
            Pay(me, chests, r, 1);
            me.GetInventory().AddItem(r.m_item.gameObject.name, Mathf.Max(1, r.m_amount), 1, 0, 0L, Companion.NameOf(me), false);
            if (station != null) station.m_craftItemEffects.Create(station.transform.position, Quaternion.identity);
            string what = Localization.instance.Localize(r.m_item.m_itemData.m_shared.m_name);
            st.Remember($"made a {what}");
            st.NextGear = 0f;
            Plugin.Instance?.Note($"{Companion.NameOf(me)} made a {what} at {(station != null ? station.transform.position : me.transform.position):F0}");
            if (forGoal) Talk.Mention(me, $"I made {Mathf.Max(1, r.m_amount)} {what.ToLowerInvariant()} for my {st.Goal?.What}."); // (a step on the way: not for chat)
            else Talk.Tell(me, st.Goal != null && st.Goal.Recipe == r ? $"I made my {what.ToLowerInvariant()}!" : $"I made a {what.ToLowerInvariant()}.");
            if (!forGoal) Journal.Made(me, $"a {what.ToLowerInvariant()}");
            return forGoal ? $"made {Mathf.Max(1, r.m_amount)} {what.ToLowerInvariant()}" : $"made a {what.ToLowerInvariant()}";
        }

        private static void Pay(Humanoid me, List<Container> chests, Recipe r, int quality)
        {
            foreach (Piece.Requirement q in Upgrades.Needs(r))
            {
                if (q.m_resItem == null) continue;
                string name = q.m_resItem.m_itemData.m_shared.m_name;
                int left = q.GetAmount(quality);
                int fromBag = Mathf.Min(left, me.GetInventory().CountItems(name));
                if (fromBag > 0) { me.GetInventory().RemoveItem(name, fromBag); left -= fromBag; }
                foreach (Container chest in chests)
                {
                    if (left <= 0) break;
                    int take = Mathf.Min(left, chest.GetInventory().CountItems(name));
                    if (take <= 0) continue;
                    ZNetView v = chest.GetComponent<ZNetView>();
                    if (v != null && !v.IsOwner()) v.ClaimOwnership();
                    chest.GetInventory().RemoveItem(name, take);
                    left -= take;
                }
            }
        }

        /// <summary>Upgrade it a level. What it did ("upgraded its club to level 2"), or null.</summary>
        public static string Do(BrainState st, ItemDrop.ItemData item, CraftingStation station)
        {
            Humanoid me = st.Body;
            Recipe recipe = ObjectDB.instance?.GetRecipe(item);
            if (recipe == null || !me.GetInventory().ContainsItem(item) || item.m_quality >= item.m_shared.m_maxQuality) return null;
            int next = item.m_quality + 1;
            List<Container> chests = ChestsNear(me, station.transform.position);
            if (!Upgrades.Needs(recipe).All(r => r.m_resItem == null || Have(me, chests, r.m_resItem.m_itemData.m_shared.m_name) >= r.GetAmount(next))) return null; // something went meanwhile
            foreach (Piece.Requirement r in Upgrades.Needs(recipe))
            {
                if (r.m_resItem == null) continue;
                string name = r.m_resItem.m_itemData.m_shared.m_name;
                int left = r.GetAmount(next);
                int fromBag = Mathf.Min(left, me.GetInventory().CountItems(name));
                if (fromBag > 0) { me.GetInventory().RemoveItem(name, fromBag); left -= fromBag; }
                foreach (Container chest in chests)
                {
                    if (left <= 0) break;
                    int take = Mathf.Min(left, chest.GetInventory().CountItems(name));
                    if (take <= 0) continue;
                    ZNetView v = chest.GetComponent<ZNetView>();
                    if (v != null && !v.IsOwner()) v.ClaimOwnership();
                    chest.GetInventory().RemoveItem(name, take);
                    left -= take;
                }
            }
            item.m_quality = next;
            item.m_durability = item.GetMaxDurability();
            Companion.SaveBag(me);
            station.m_craftItemEffects.Create(station.transform.position, Quaternion.identity);
            string what = Localization.instance.Localize(item.m_shared.m_name);
            st.Remember($"upgraded its {what} to level {next}");
            Plugin.Instance?.Note($"{Companion.NameOf(me)} upgraded its {what} to level {next} at {station.transform.position:F0}");
            Talk.Tell(me, $"I upgraded my {what} to level {next}.");
            Journal.Upgraded(me, $"its {what.ToLowerInvariant()} to level {next}");
            Portraits.Dirty(me);
            return $"upgraded its {what.ToLowerInvariant()} to level {next}";
        }
    }
}
