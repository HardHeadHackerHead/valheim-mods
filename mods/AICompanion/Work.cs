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
        internal enum Kind { None, Hit, Pick, PickUp, Store, Upgrade, Craft, Hunt, Cook }

        internal class Task
        {
            public Kind Kind;
            public Component Target;
            public Job Job;
            public ItemDrop.ItemData Item;   // what it is upgrading
            public Recipe Recipe;            // what it is crafting
            public float Started, LastClose;
        }

        private static readonly string[] OreWords = { "Ore", "Scrap", "Flametal" };
        private static readonly HashSet<string> Prey = new HashSet<string> { "Deer", "Boar", "Neck", "Hare" };
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
                    float reach = Mathf.Max(1.1f, (tool.m_shared.m_attack?.m_attackRange ?? 1.5f) * 0.75f);
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
                    Upgrades.Craft(st, t.Recipe, (CraftingStation)t.Target);
                    st.Task = null;
                    break;

                case Kind.Cook:
                    if (dist > 2.2f) { moveTo(at, 1.5f, dist > 8f); break; }
                    t.LastClose = Time.time;
                    stop();
                    if (Time.time - t.Started > 240f || !Kitchen.Cook(st, (CookingStation)t.Target)) st.Task = null; // done (what it cooked lies at its feet: it picks it up next)
                    break;

                case Kind.Hunt:
                    var prey = (Character)t.Target;
                    if (prey == null || prey.IsDead()) { st.Task = null; break; }
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
            if (st.Task != null) Brain.Status(st, Describe(st.Task));
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
                Kind.Store => "taking things to its chest",
                Kind.Craft => $"making a {Localization.instance.Localize(t.Recipe?.m_item?.m_itemData.m_shared.m_name ?? "")} at the {Localization.instance.Localize(((CraftingStation)t.Target).m_name)}",
                Kind.Cook => "cooking",
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
            if (t.Kind == Kind.Store && ((Container)t.Target).IsInUse()) return false;
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
            Collider best = null;
            float d = float.MaxValue;
            foreach (Collider col in c.GetComponentsInChildren<Collider>())
            {
                if (col == null || !col.enabled || col.isTrigger) continue;
                Vector3 p = col.ClosestPointOnBounds(from);
                float dd = (p - from).sqrMagnitude;
                if (dd < d) { d = dd; best = col; }
            }
            if (best == null) return c.transform.position;
            Vector3 q = best.ClosestPointOnBounds(from);
            q.y = Mathf.Max(c.transform.position.y, q.y - 0.5f);
            return q;
        }

        private static float Flat(Vector3 a, Vector3 b) { a.y = 0f; b.y = 0f; return Vector3.Distance(a, b); }

        // ---- choosing what to do next ------------------------------------------------------------------------

        private static Task Choose(BrainState st, Job jobs, Vector3 center, float radius)
        {
            Humanoid me = st.Body;
            Inventory inv = me.GetInventory();
            st.WorkNote = null;

            // 1. A full bag goes to its chests first.
            if (inv.GetEmptySlots() <= 1 || inv.GetAllItems().Count(i => !Keeps(me, i)) >= 18 || Carry.Weight(me) > Carry.Max(me) * 0.9f)
            {
                Container chest = Home.Chests(me).Where(c => !c.IsInUse() && Vector3.Distance(c.transform.position, center) < radius + 40f && HasRoom(c, me))
                                     .OrderBy(c => Vector3.Distance(c.transform.position, me.transform.position)).FirstOrDefault();
                if (chest != null) return New(Kind.Store, chest, Job.None);
                if (inv.GetEmptySlots() == 0) { st.WorkNote = "its bag is full: give it a chest (Home tab) or empty its bag"; return null; }
            }

            // 2. Better gear: an upgrade at its workbench (or forge...) when it has the materials, in its bag or its chests.
            if (Time.time >= st.NextUpgradeLook)
            {
                st.NextUpgradeLook = Time.time + 30f;
                var up = Upgrades.Find(me, center, radius);
                if (up != null) { Task u = New(Kind.Upgrade, up.Value.Value, Job.None); u.Item = up.Value.Key; return u; }
                var craft = Upgrades.FindCraft(me, center, radius);
                if (craft != null) { Task t2 = New(Kind.Craft, craft.Value.Value, Job.None); t2.Recipe = craft.Value.Key; return t2; }
            }

            // Its own meals: raw food it carries goes on a cooking station near home.
            if ((jobs & Job.Cook) != 0)
            {
                CookingStation stove = Kitchen.Find(me, center, radius);
                if (stove != null)
                {
                    foreach (CookingStation.ItemConversion conv in stove.m_conversion) if (conv.m_to != null) st.Wanted.Add(conv.m_to.name);
                    st.WorkSpot = stove.transform.position;
                    return New(Kind.Cook, stove, Job.Cook);
                }
            }

            // Hunting: deer and boar near home, for meat and hides (with its bow if it has one).
            if ((jobs & Job.Hunt) != 0)
            {
                Character prey = Character.GetAllCharacters().Where(ch => ch != null && !ch.IsDead() && !ch.IsTamed() && Prey.Contains(Utils.GetPrefabName(ch.gameObject))
                        && Vector3.Distance(ch.transform.position, center) < radius && !Skipped(st, ch))
                    .OrderBy(ch => Vector3.Distance(ch.transform.position, me.transform.position)).FirstOrDefault();
                if (prey != null) return New(Kind.Hunt, prey, Job.Hunt);
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
            Task best = null;
            float bestScore = float.MaxValue;
            foreach (Collider col in Physics.OverlapSphere(center, radius, ~0, QueryTriggerInteraction.Collide))
            {
                GameObject go = col.attachedRigidbody != null ? col.attachedRigidbody.gameObject : col.transform.root.gameObject;
                if (!seen.Add(go)) continue;
                Task t = Consider(st, go, jobs, axe, pick);
                if (t == null || Skipped(st, t.Target)) continue;
                float score = Vector3.Distance(t.Target.transform.position, me.transform.position) + Priority(t);
                if (score < bestScore) { bestScore = score; best = t; }
            }
            if (best == null)
            {
                var missing = new List<string>();
                if ((jobs & Job.Wood) != 0 && axe == null) missing.Add("an axe");
                if ((jobs & (Job.Stone | Job.Ore)) != 0 && pick == null) missing.Add("a pickaxe");
                st.WorkNote = missing.Count > 0 ? $"needs {string.Join(" and ", missing)} for its jobs" : $"nothing left to gather within {radius:0} m";
            }
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
                if (p != null && p.m_respawnTimeMinutes > 0f && !(Companion.Zdo(p)?.GetBool(ZDOVars.s_picked, false) ?? true) && p.m_itemPrefab != null) return New(Kind.Pick, p, Job.Forage);
            }
            return null;
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
            Job jobs = JobsOf(me);
            foreach (ItemDrop.ItemData item in its.GetAllItems().ToList())
            {
                if (!mine.HaveEmptySlot() && !mine.CanAddItem(item)) break;
                if (Wants(me, item, jobs)) { item.m_equipped = false; mine.MoveItemToThis(its, item); took++; }
            }
            if (put + took > 0)
            {
                st.Remember($"at its chest: put away {put}, took {took}");
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

        private static bool Upgradable(Humanoid me, ItemDrop.ItemData i)
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
            List<CraftingStation> stations = (Stations() ?? new List<CraftingStation>()).Where(s => s != null && Vector3.Distance(s.transform.position, center) < radius + 10f).ToList();
            if (stations.Count == 0) return null;
            foreach (ItemDrop.ItemData item in me.GetInventory().GetAllItems().Where(i => Upgradable(me, i)).OrderByDescending(i => me.IsItemEquiped(i)))
            {
                Recipe recipe = ObjectDB.instance?.GetRecipe(item);
                if (recipe == null || recipe.m_craftingStation == null) continue;
                int next = item.m_quality + 1;
                CraftingStation station = stations.FirstOrDefault(s => s.m_name == recipe.m_craftingStation.m_name && s.GetLevel() >= recipe.GetRequiredStationLevel(next));
                if (station == null) continue;
                List<Container> chests = ChestsNear(me, station.transform.position);
                if (recipe.m_resources.All(r => r.m_resItem == null || Have(me, chests, r.m_resItem.m_itemData.m_shared.m_name) >= r.GetAmount(next)))
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
            List<CraftingStation> stations = (Stations() ?? new List<CraftingStation>()).Where(s => s != null && Vector3.Distance(s.transform.position, center) < radius + 10f).ToList();
            if (stations.Count == 0) return null;
            foreach (Recipe r in ObjectDB.instance.m_recipes)
            {
                if (r == null || !r.m_enabled || r.m_item == null || r.m_craftingStation == null) continue;
                if (!WorthMaking(me, r.m_item.m_itemData)) continue;
                CraftingStation station = stations.FirstOrDefault(s => s.m_name == r.m_craftingStation.m_name && s.GetLevel() >= Mathf.Max(1, r.m_minStationLevel));
                if (station == null) continue;
                List<Container> chests = ChestsNear(me, station.transform.position);
                if (r.m_resources.All(q => q.m_resItem == null || Have(me, chests, q.m_resItem.m_itemData.m_shared.m_name) >= q.GetAmount(1)))
                    return new KeyValuePair<Recipe, CraftingStation>(r, station);
            }
            return null;
        }

        private static bool WorthMaking(Humanoid me, ItemDrop.ItemData made)
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
            if (made.IsWeapon() && !Work.IsTool(made) && made.m_shared.m_skillType != Skills.SkillType.Unarmed && made.m_shared.m_attack != null && made.m_shared.m_attack.m_attackType != Attack.AttackType.Projectile)
                return made.GetDamage().GetTotalDamage() > (Companion.BestMelee(me)?.GetDamage().GetTotalDamage() ?? 0f) + 2f;
            return false;
        }

        public static void Craft(BrainState st, Recipe r, CraftingStation station)
        {
            Humanoid me = st.Body;
            if (r == null || !WorthMaking(me, r.m_item.m_itemData)) return;
            List<Container> chests = ChestsNear(me, station.transform.position);
            if (!r.m_resources.All(q => q.m_resItem == null || Have(me, chests, q.m_resItem.m_itemData.m_shared.m_name) >= q.GetAmount(1))) return;
            if (!me.GetInventory().HaveEmptySlot()) return;
            Pay(me, chests, r, 1);
            me.GetInventory().AddItem(r.m_item.gameObject.name, Mathf.Max(1, r.m_amount), 1, 0, 0L, Companion.NameOf(me), false);
            station.m_craftItemEffects.Create(station.transform.position, Quaternion.identity);
            string what = Localization.instance.Localize(r.m_item.m_itemData.m_shared.m_name);
            st.Remember($"made a {what}");
            st.NextGear = 0f;
            Plugin.Instance?.Note($"{Companion.NameOf(me)} made a {what} at {station.transform.position:F0}");
            if (Companion.Master(me) == Player.m_localPlayer) Plugin.Tell($"{Companion.NameOf(me)} made a {what}");
        }

        private static void Pay(Humanoid me, List<Container> chests, Recipe r, int quality)
        {
            foreach (Piece.Requirement q in r.m_resources)
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

        public static void Do(BrainState st, ItemDrop.ItemData item, CraftingStation station)
        {
            Humanoid me = st.Body;
            Recipe recipe = ObjectDB.instance?.GetRecipe(item);
            if (recipe == null || !me.GetInventory().ContainsItem(item) || item.m_quality >= item.m_shared.m_maxQuality) return;
            int next = item.m_quality + 1;
            List<Container> chests = ChestsNear(me, station.transform.position);
            if (!recipe.m_resources.All(r => r.m_resItem == null || Have(me, chests, r.m_resItem.m_itemData.m_shared.m_name) >= r.GetAmount(next))) return; // something went meanwhile
            foreach (Piece.Requirement r in recipe.m_resources)
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
            if (Companion.Master(me) == Player.m_localPlayer) Plugin.Tell($"{Companion.NameOf(me)} upgraded their {what} to level {next}");
            Portraits.Dirty(me);
        }
    }
}
