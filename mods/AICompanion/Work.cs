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
        internal enum Kind { None, Hit, Pick, PickUp, Store, Upgrade, Craft, Hunt, Cook, Fetch }

        internal class Task
        {
            public Kind Kind;
            public Component Target;
            public Job Job;
            public ItemDrop.ItemData Item;   // what it is upgrading
            public Recipe Recipe;            // what it is crafting
            public bool ForGoal;             // toward its goal (Goals)
            public bool Edible;              // food (or raw food to cook), when it forages
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

            if (st.Task != null && !Valid(st, st.Task)) st.Task = null;
            if (st.Task == null && Time.time >= st.NextWorkLook)
            {
                st.NextWorkLook = Time.time + 1.5f;
                st.Task = Choose(st, jobs, center, radius);
                if (st.Task == null) st.WorkTool = null;
            }
            if (st.Task == null) { Go(center, moveTo, stop, me); Brain.Status(st, st.WorkNote ?? "nothing left to gather here"); return; }

            Task t = st.Task;
            Vector3 at = Point(t.Target, me.transform.position);
            float dist = Flat(at, me.transform.position);
            if (Time.time - t.Started > 60f || (dist > 3f && Time.time - t.LastClose > 20f)) { Skip(st, t.Target, "could not get to it"); return; }

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
                    float reach = Mathf.Max(1.1f, (tool.m_shared.m_attack?.m_attackRange ?? 1.5f) * 0.75f) + (t.Target is TreeBase ? 0.4f : 0f); // (to a trunk's middle)
                    if (dist > reach) { moveTo(at, reach * 0.7f, dist > 8f); break; }
                    t.LastClose = Time.time;
                    stop();
                    Vector3 aim = at + Vector3.up * (t.Job == Job.Wood ? 1f : 0.4f);
                    lookAt(aim);
                    if (!me.IsItemEquiped(tool) || me.InAttack() || !st.Ai.IsLookingAt(aim, 25f)) break;
                    float cost = tool.m_shared.m_attack?.m_attackStamina ?? 0f;
                    if (Stamina.Get(me) < cost + 1f) { Brain.Status(st, "catching its breath"); break; }
                    if (me.GetTimeSinceLastAttack() < 0.5f) break;
                    st.WorkSpot = at;
                    me.StartAttack(null, false);
                    break;

                case Kind.Store:
                    if (dist > 2f) { moveTo(at, 1.2f, dist > 8f); break; }
                    t.LastClose = Time.time;
                    stop();
                    Store(st, (Container)t.Target);
                    st.Task = null;
                    break;

                case Kind.Craft:
                    if (dist > 2.6f) { moveTo(at, 1.8f, dist > 8f); break; }
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

                case Kind.Fetch:
                    if (dist > 2f) { moveTo(at, 1.2f, dist > 8f); break; }
                    t.LastClose = Time.time;
                    stop();
                    TakeFood(st, (Container)t.Target);
                    st.Task = null;
                    break;

                case Kind.Hunt:
                    var prey = (Character)t.Target;
                    if (prey == null || prey.IsDead()) { st.Task = null; break; }
                    if (Companion.BestRanged(me) == null && Vector3.Distance(prey.transform.position, me.transform.position) > 30f) { Skip(st, prey, "it outran it", 3f); break; } // no bow: no catching a deer
                    t.LastClose = Time.time;
                    st.WorkTool = Companion.BestRanged(me) ?? Companion.BestMelee(me);
                    Brain.Strike(st, prey, Time.deltaTime);
                    break;

                case Kind.Upgrade:
                    if (dist > 2.6f) { moveTo(at, 1.8f, dist > 8f); break; }
                    t.LastClose = Time.time;
                    stop();
                    Upgrades.Do(st, t.Item, (CraftingStation)t.Target);
                    st.Task = null;
                    break;
            }
            if (st.Task != null) Brain.Status(st, Describe(st.Task) + (st.Task.ForGoal && st.Goal != null ? $" for its {st.Goal.What}" : ""));
        }

        /// <summary>
        /// Sent home from far away (an adventure, another island): it sets off toward home, and a few seconds later it is there, as a player
        /// would recall home. Its area may not be loaded then: it carries on there, and catches up when someone comes back (CatchUp).
        /// </summary>
        private static bool TravelHome(BrainState st, Vector3 center, float radius, Action<Vector3, float, bool> moveTo)
        {
            Humanoid me = st.Body;
            if (Flat(center, me.transform.position) < radius + 60f) { st.TripStart = 0f; return false; }
            if (st.TripStart == 0f) { st.TripStart = Time.time; st.Task = null; st.Remember("set off home"); }
            moveTo(center, 2f, true);
            Brain.Status(st, "heading home");
            if (Time.time - st.TripStart < 8f) return true;
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

        private static void Go(Vector3 center, Action<Vector3, float, bool> moveTo, Action stop, Humanoid me)
        {
            if (Flat(center, me.transform.position) > 4f) moveTo(center, 2f, Flat(center, me.transform.position) > 12f);
            else stop();
        }

        private static string Describe(Task t)
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
            // A standing tree: its trunk (the bounds of its colliders take in the whole crown, and it stopped short of the trunk and swung at air).
            if (c is TreeBase) return c.transform.position;
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

        /// <summary>The nearest point of the collider's real shape (a long log's, not its box's), where Unity can tell (all but concave meshes).</summary>
        private static Vector3 Closest(Collider col, Vector3 from) => col is MeshCollider mesh && !mesh.convex ? col.ClosestPointOnBounds(from) : col.ClosestPoint(from);

        // ---- choosing what to do next ------------------------------------------------------------------------

        private static Task Choose(BrainState st, Job jobs, Vector3 center, float radius)
        {
            Humanoid me = st.Body;
            Inventory inv = me.GetInventory();
            st.WorkNote = null;

            // 1. A full bag goes to its chests first.
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
                Character prey = Character.GetAllCharacters().Where(ch => ch != null && !ch.IsDead() && !ch.IsTamed() && kinds.Contains(Utils.GetPrefabName(ch.gameObject))
                        && (!Harmless.Contains(Utils.GetPrefabName(ch.gameObject)) || Vector3.Distance(ch.transform.position, me.transform.position) < reachable) // boars come at it: only runners need a bow
                        && Vector3.Distance(ch.transform.position, center) < radius && !Skipped(st, ch))
                    .OrderBy(ch => Vector3.Distance(ch.transform.position, me.transform.position)).FirstOrDefault();
                if (prey != null) { Task h = New(Kind.Hunt, prey, Job.Hunt); h.ForGoal = forGoal; return h; }
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
                if (t == null || Skipped(st, t.Target)) continue;
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
                st.WorkNote = missing.Count > 0 ? $"needs {string.Join(" and ", missing)} for its jobs" : $"nothing left to gather within {radius:0} m";
            }
            if (st.Goal != null && st.Goal.Raw.Count > 0 && !goalSeen && JobsOf(me) == Job.None && !goalPrey.Any())
            {
                // Nothing near home drops what its goal needs: those things count as "ask for them" for ten minutes, so it picks a goal it can do.
                Talk.Tell(me, $"I can't find any {string.Join(" or ", st.Goal.Names.Values)} within {radius:0} m of home for my {st.Goal.What}.", "far:" + st.Goal.What, 30f);
                foreach (string item in st.Goal.Raw.Keys) st.Unfindable[item] = Time.time + 600f;
                st.NextUpgradeLook = 0f;
            }
            if (best == null && inv.GetEmptySlots() == 0) Talk.Tell(me, "My bag is full and none of my chests has room. Give me another chest (Home tab).", "full", 15f);
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

        private static Task New(Kind kind, Component target, Job job) => new Task { Kind = kind, Target = target, Job = job, Started = Time.time, LastClose = Time.time };

        private static Task Consider(BrainState st, GameObject go, Job jobs, ItemDrop.ItemData axe, ItemDrop.ItemData pick)
        {
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
                if (p != null && p.m_respawnTimeMinutes > 0f && !(Companion.Zdo(p)?.GetBool(ZDOVars.s_picked, false) ?? true) && p.m_itemPrefab != null)
                {
                    ItemDrop.ItemData item = p.m_itemPrefab.GetComponent<ItemDrop>()?.m_itemData;
                    bool edible = item != null && (Food.IsFood(item) || IsCookable(item));
                    if (st.Hungry && !edible) return null; // hungry: berries and mushrooms, not dandelions
                    Task t = New(Kind.Pick, p, Job.Forage);
                    t.Edible = edible;
                    return t;
                }
            }
            return null;
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
        internal static bool Move(Container chest, Humanoid me, ItemDrop.ItemData item, int n)
        {
            ZNetView view = chest.GetComponent<ZNetView>();
            if (view == null || !view.IsValid()) return false;
            if (!view.IsOwner()) view.ClaimOwnership();
            if (!me.GetInventory().CanAddItem(item, n)) return false;
            ItemDrop.ItemData copy = item.Clone();
            copy.m_stack = n;
            copy.m_equipped = false;
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
                if (!Move(chest, me, food, n)) continue;
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
        public static bool Keeps(Humanoid h, ItemDrop.ItemData i) => h.IsItemEquiped(i) || Companion.Useful(h, i) || IsTool(i) || IsCookable(i);

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

        private static List<Container> ChestsNear(Humanoid me, Vector3 at) => Home.Chests(me).Where(c => c != null && !c.IsInUse() && Vector3.Distance(c.transform.position, at) < 25f).ToList();

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
                List<Container> chests = station != null ? ChestsNear(me, station.transform.position) : new List<Container>();
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
            List<Container> chests = station != null ? ChestsNear(me, station.transform.position) : new List<Container>();
            if (!Upgrades.Needs(r).All(q => q.m_resItem == null || Have(me, chests, q.m_resItem.m_itemData.m_shared.m_name) >= q.GetAmount(1))) return null;
            if (!me.GetInventory().HaveEmptySlot()) return null;
            Pay(me, chests, r, 1);
            me.GetInventory().AddItem(r.m_item.gameObject.name, Mathf.Max(1, r.m_amount), 1, 0, 0L, Companion.NameOf(me), false);
            if (station != null) station.m_craftItemEffects.Create(station.transform.position, Quaternion.identity);
            string what = Localization.instance.Localize(r.m_item.m_itemData.m_shared.m_name);
            st.Remember($"made a {what}");
            st.NextGear = 0f;
            Plugin.Instance?.Note($"{Companion.NameOf(me)} made a {what} at {(station != null ? station.transform.position : me.transform.position):F0}");
            Talk.Tell(me, forGoal ? $"I made {Mathf.Max(1, r.m_amount)} {what.ToLowerInvariant()} for my {st.Goal?.What}." : st.Goal != null && st.Goal.Recipe == r ? $"I made my {what.ToLowerInvariant()}!" : $"I made a {what.ToLowerInvariant()}.");
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
            Portraits.Dirty(me);
            return $"upgraded its {what.ToLowerInvariant()} to level {next}";
        }
    }
}
