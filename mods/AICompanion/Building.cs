using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace AICompanion
{
    /// <summary>
    /// Building your plans, a duty at home (Duties: "Build our plans"). With BuildOrders installed, the ghosts you planned within its home are
    /// pieces it builds, as a player would: it picks the next piece that will stand with what is built (BuildOrders works that out, as for
    /// "Build all"), takes what the piece costs out of its bag or your chests near home (and its own), walks to it with a hammer (it makes
    /// one when it can), and puts it up. It pays for it itself: nothing is taken from you. It carries what several pieces need in one trip.
    /// With nothing it can build it says what is missing, and the next duty is worked (wood and stone for the plan, say).
    /// BuildOrders publishes three functions through AppDomain data (Plugin.Helpers.cs there); without it this does nothing.
    /// </summary>
    internal static class Building
    {
        internal class Cand { public string Id, Prefab; public Vector3 Pos; }

        private static Func<Vector3, float, int> CountFn => AppDomain.CurrentDomain.GetData("DHack.BuildOrders.Count") as Func<Vector3, float, int>;
        private static Func<Vector3, float, int, string[]> NextFn => AppDomain.CurrentDomain.GetData("DHack.BuildOrders.Next") as Func<Vector3, float, int, string[]>;
        private static Func<string, bool> PlaceFn => AppDomain.CurrentDomain.GetData("DHack.BuildOrders.Place") as Func<string, bool>;

        private static Func<Vector3, float, string[]> BlockersFn => AppDomain.CurrentDomain.GetData("DHack.BuildOrders.Blockers") as Func<Vector3, float, string[]>;

        public static bool Available => CountFn != null && NextFn != null && PlaceFn != null;

        /// <summary>Material the next pieces of your plans need, on it: it keeps it (Work.Keeps) instead of putting it away with its other finds.</summary>
        public static bool Needed(Humanoid h, ItemDrop.ItemData i)
        {
            BrainState st = Brain.Get(h);
            return st != null && Time.time < st.BuildKeepUntil && st.BuildKeep.Contains(i.m_shared.m_name);
        }

        private static float Reach(Humanoid me) => Work.RadiusOf(me) + 10f;

        private static readonly Dictionary<long, KeyValuePair<float, int>> Counted = new Dictionary<long, KeyValuePair<float, int>>();

        /// <summary>The planned pieces within its home that are left (looked up every few seconds).</summary>
        public static int Pending(Humanoid me)
        {
            if (!Available) return 0;
            long id = Companion.IdOf(me);
            if (Counted.TryGetValue(id, out var hit) && Time.time - hit.Key < 5f) return hit.Value;
            int n = 0;
            try { n = CountFn(Work.Center(me), Reach(me)); } catch (Exception) { }
            Counted[id] = new KeyValuePair<float, int>(Time.time, n);
            return n;
        }

        // ---- what a piece costs ------------------------------------------------------------------------------

        private static Piece PieceOf(string prefab) => ZNetScene.instance?.GetPrefab(prefab)?.GetComponent<Piece>();

        /// <summary>Item name to how many one piece costs (nothing when the world builds free). Null: not a piece.</summary>
        private static Dictionary<string, int> Needs(string prefab)
        {
            Piece piece = PieceOf(prefab);
            if (piece == null) return null;
            var needs = new Dictionary<string, int>();
            if (ZoneSystem.instance != null && ZoneSystem.instance.GetGlobalKey(piece.FreeBuildKey())) return needs;
            foreach (Piece.Requirement r in piece.m_resources)
            {
                int n = r.m_resItem != null ? r.GetAmount(1) : 0;
                if (n > 0) needs[r.m_resItem.m_itemData.m_shared.m_name] = (needs.TryGetValue(r.m_resItem.m_itemData.m_shared.m_name, out int had) ? had : 0) + n;
            }
            return needs;
        }

        private static Dictionary<string, GameObject> _byName;

        /// <summary>The item prefab whose shared name this is ("$item_wood": Wood).</summary>
        private static GameObject ItemByName(string sharedName)
        {
            if (_byName == null || _byName.Count == 0)
            {
                _byName = new Dictionary<string, GameObject>();
                if (ObjectDB.instance != null)
                    foreach (GameObject g in ObjectDB.instance.m_items)
                    {
                        ItemDrop d = g != null ? g.GetComponent<ItemDrop>() : null;
                        if (d != null && !_byName.ContainsKey(d.m_itemData.m_shared.m_name)) _byName[d.m_itemData.m_shared.m_name] = g;
                    }
            }
            return _byName.TryGetValue(sharedName, out GameObject prefab) ? prefab : null;
        }

        private static Dictionary<string, int> Missing(Dictionary<string, int> have, Dictionary<string, int> needs) =>
            needs.Where(kv => !have.TryGetValue(kv.Key, out int n) || n < kv.Value).ToDictionary(kv => kv.Key, kv => kv.Value - (have.TryGetValue(kv.Key, out int n2) ? n2 : 0));

        private static string Say(Dictionary<string, int> items) =>
            string.Join(", ", items.Select(kv => $"{kv.Value} {Localization.instance.Localize(kv.Key).ToLowerInvariant()}"));

        /// <summary>The chests it may take materials from: its own, then yours near home (the duty is your ask to build, so yours are open to it).</summary>
        private static readonly Dictionary<long, KeyValuePair<float, List<Container>>> SourceCache = new Dictionary<long, KeyValuePair<float, List<Container>>>();

        private static List<Container> Sources(Humanoid me)
        {
            long id = Companion.IdOf(me);
            if (SourceCache.TryGetValue(id, out var hit) && Time.time - hit.Key < 4f) return hit.Value.Where(c => c != null).ToList();
            List<Container> found = Home.Chests(me).Where(c => !Containers.InUse(c)).Concat(Work.YourChests(me, Work.Center(me), Work.RadiusOf(me) + 20f)).Distinct().ToList();
            SourceCache[id] = new KeyValuePair<float, List<Container>>(Time.time, found);
            return found;
        }

        private static Dictionary<string, int> Count(IEnumerable<Inventory> inventories)
        {
            var total = new Dictionary<string, int>();
            foreach (Inventory inv in inventories)
                foreach (ItemDrop.ItemData i in inv.GetAllItems())
                    total[i.m_shared.m_name] = (total.TryGetValue(i.m_shared.m_name, out int had) ? had : 0) + i.m_stack;
            return total;
        }

        private static bool Covers(Dictionary<string, int> have, Dictionary<string, int> needs) => needs.All(kv => have.TryGetValue(kv.Key, out int n) && n >= kv.Value);

        // ---- choosing the next thing --------------------------------------------------------------------------------

        /// <summary>
        /// The next task for the build duty: build a piece it has the materials for in its bag, else a trip to a chest for materials, else
        /// nothing (and why, in the work note). Null with no plan to build.
        /// </summary>
        public static Work.Task Next(BrainState st)
        {
            Humanoid me = st.Body;
            Goal prevGoal = st.PlanGoal;   // (worked out every twenty seconds, not every look)
            st.PlanGoal = null;
            if (!Available) { st.BuildNote = st.WorkNote = "BuildOrders is not installed"; return null; }
            Vector3 center = Work.Center(me);
            if (Time.time >= st.NextBuildLook)
            {
                st.NextBuildLook = Time.time + 10f;
                st.BuildList = Parse(NextFn(center, Reach(me), 24));
            }
            List<Cand> cands = st.BuildList.Where(c => !(st.BuildSkip.TryGetValue(c.Id, out float until) && until > Time.time)).ToList();
            if (cands.Count == 0)
            {
                st.BuildNote = st.WorkNote = Pending(me) > 0 ? Explain(st) : "no plans near home";
                return null;
            }
            st.BuildKeep.Clear();
            foreach (Cand c in cands.Take(12)) { var cost = Needs(c.Prefab); if (cost != null) foreach (string name in cost.Keys) st.BuildKeep.Add(name); }
            st.BuildKeepUntil = Time.time + 120f;
            if (Mending.Hammer(me) == null)
            {
                Mending.MakeHammer(st);
                if (Mending.Hammer(me) == null)
                {
                    Talk.Tell(me, "I need a hammer to build your plans. Give me one, or the wood and stone to make one.", "nohammer2", 30f);
                    st.BuildNote = st.WorkNote = "needs a hammer to build";
                    return null;
                }
            }

            Dictionary<string, int> bag = Count(new[] { me.GetInventory() });
            foreach (Cand c in cands)
            {
                Dictionary<string, int> needs = Needs(c.Prefab);
                if (needs != null && Covers(bag, needs)) return BuildTask(st, c);
            }

            // None it can pay from its bag: a trip to the chests for what several of the next pieces cost, as much as it can carry.
            List<Container> sources = Sources(me);
            Dictionary<string, int> store = Count(sources.Select(c => c.GetInventory()));
            var have = new Dictionary<string, int>(bag);
            foreach (var kv in store) have[kv.Key] = (have.TryGetValue(kv.Key, out int n) ? n : 0) + kv.Value;
            var want = new Dictionary<string, int>();   // what to take out of the chests
            var budget = new Dictionary<string, int>(have);
            var bagLeft = new Dictionary<string, int>(bag);
            float room = Carry.Max(me) * 0.6f - Carry.Weight(me);
            int picked = 0;
            foreach (Cand c in cands)
            {
                Dictionary<string, int> needs = Needs(c.Prefab);
                if (needs == null || needs.Count == 0 || !Covers(budget, needs)) continue;
                float weight = needs.Sum(kv => kv.Value * (ItemByName(kv.Key)?.GetComponent<ItemDrop>()?.m_itemData.m_shared.m_weight ?? 1f));
                if (weight > room && picked > 0) break;
                foreach (var kv in needs)
                {
                    budget[kv.Key] -= kv.Value;
                    int fromBag = Mathf.Min(kv.Value, bagLeft.TryGetValue(kv.Key, out int b0) ? b0 : 0);
                    bagLeft[kv.Key] = (bagLeft.TryGetValue(kv.Key, out int b1) ? b1 : 0) - fromBag;
                    if (kv.Value - fromBag > 0) want[kv.Key] = (want.TryGetValue(kv.Key, out int w0) ? w0 : 0) + kv.Value - fromBag;
                }
                room -= weight;
                if (++picked >= 10) break;
            }
            if (picked == 0 || want.Count == 0)
            {
                // Short of materials for what is next: what the next pieces need all told, less what it has, as a goal: it gathers it, smelts it,
                // makes it at a station near home, or asks you for what it cannot get (Work gathers toward st.PlanGoal while this duty is its work).
                Dictionary<string, int> total = new Dictionary<string, int>();
                foreach (Cand c in cands) { var cost = Needs(c.Prefab); if (cost != null) foreach (var kv in cost) total[kv.Key] = (total.TryGetValue(kv.Key, out int had) ? had : 0) + kv.Value; }
                Dictionary<string, int> missing = Missing(have, total);
                if (prevGoal != null && Time.time - st.PlanGoalAt < 20f) st.PlanGoal = prevGoal;
                else { st.PlanGoal = Short(st, missing, sources); st.PlanGoalAt = Time.time; }
                if (st.PlanGoal != null && st.PlanGoal.Raw.Count > 0)
                {
                    st.BuildNote = st.WorkNote = $"getting {st.PlanGoal.RawText()} for your plan";
                    Talk.Mention(me, $"Your plan needs {Say(missing)} more than I have. I'll go and get {st.PlanGoal.RawText()}.", "planget", 600f);
                }
                else st.BuildNote = st.WorkNote = missing.Count > 0 ? $"your plan needs {Say(missing)} more" : "nothing to build with";
                if (st.PlanGoal != null && st.PlanGoal.Ask.Count > 0)
                    Talk.Tell(me, $"For your plan I need {string.Join(" and ", st.PlanGoal.Ask)}, and I can't get that myself. Put some in a chest near home, or give it to me.", "planask", 600f);
                return null;
            }
            Container chest = sources.Where(c => c.GetInventory().GetAllItems().Any(i => want.ContainsKey(i.m_shared.m_name)) && !Skipped(st, c))
                                     .OrderBy(c => Vector3.Distance(c.transform.position, me.transform.position)).FirstOrDefault(c => Brain.CanReach(me, c.transform.position));
            if (chest == null) { st.BuildNote = st.WorkNote = "the materials for its plans are in chests it cannot get to"; return null; }
            st.BuildWant = want;
            return Work.New(Work.Kind.Supply, chest, Job.None);
        }

        /// <summary>What a plan is short of, as a goal (null when it is nothing it needs to do something about).</summary>
        private static Goal Short(BrainState st, Dictionary<string, int> missing, List<Container> sources)
        {
            if (missing.Count == 0) return null;
            Humanoid me = st.Body;
            var items = missing.Select(kv => new KeyValuePair<ItemDrop, int>(ItemByName(kv.Key)?.GetComponent<ItemDrop>(), kv.Value));
            return Goals.ForNeeds(me, Work.Center(me), Work.RadiusOf(me), st, items, sources, "plan");
        }

        /// <summary>
        /// Away (CatchUp): what the next pieces of your plan still need that it does not have, as a goal to gather, make or ask for. Null when it
        /// has it all (or there is no plan).
        /// </summary>
        public static Goal ShortFor(BrainState st)
        {
            Humanoid me = st.Body;
            if (!Available || Pending(me) == 0) return null;
            List<Cand> cands = Parse(NextFn(Work.Center(me), Reach(me), 24));
            if (cands.Count == 0) return null;
            List<Container> sources = Sources(me);
            Dictionary<string, int> have = Count(sources.Select(c => c.GetInventory()).Concat(new[] { me.GetInventory() }));
            var total = new Dictionary<string, int>();
            foreach (Cand c in cands) { var cost = Needs(c.Prefab); if (cost != null) foreach (var kv in cost) total[kv.Key] = (total.TryGetValue(kv.Key, out int had) ? had : 0) + kv.Value; }
            return Short(st, Missing(have, total), sources);
        }

        /// <summary>Why nothing of the plan can be built, said to its player now and then, and for its menu.</summary>
        private static string Explain(BrainState st)
        {
            Humanoid me = st.Body;
            var parts = new List<string>();
            try
            {
                foreach (string line in BlockersFn?.Invoke(Work.Center(me), Reach(me)) ?? new string[0])
                {
                    string[] f = line.Split('|');
                    if (f.Length < 3) continue;
                    if (f[0] == "unlocked") parts.Add($"{f[1]} need pieces you have not unlocked yet ({f[2]})");
                    else if (f[0] == "station") parts.Add($"{f[1]} need a {f[2]} built near them");
                    else if (f[0] == "protected") parts.Add($"{f[1]} are on protected ground");
                }
            }
            catch (Exception) { }
            string why = parts.Count > 0 ? string.Join("; ", parts)
                       : "none of it will stand yet (what holds it up cannot be built: the ground under it may need levelling first)";
            Talk.Tell(me, $"I can't build your plan yet: {why}.", "planblock", 600f);
            return "your plan is stuck: " + why;
        }

        private static bool Skipped(BrainState st, Component c) => st.Skipped.TryGetValue(c.gameObject.GetInstanceID(), out float until) && Time.time < until;

        private static List<Cand> Parse(string[] lines)
        {
            var list = new List<Cand>();
            foreach (string line in lines ?? new string[0])
            {
                string[] f = line.Split('|');
                if (f.Length < 5) continue;
                if (!float.TryParse(f[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float x) || !float.TryParse(f[3], NumberStyles.Float, CultureInfo.InvariantCulture, out float y)
                    || !float.TryParse(f[4], NumberStyles.Float, CultureInfo.InvariantCulture, out float z)) continue;
                list.Add(new Cand { Id = f[0], Prefab = f[1], Pos = new Vector3(x, y, z) });
            }
            return list;
        }

        private static Work.Task BuildTask(BrainState st, Cand c)
        {
            // A spot to walk to: the task's target is a thing in the world, so a bare marker stands where the piece goes (gone after a minute or so).
            var marker = new GameObject("dhc_buildspot");
            marker.transform.position = c.Pos;
            UnityEngine.Object.Destroy(marker, 120f);
            Work.Task t = Work.New(Work.Kind.Build, marker.transform, Job.None);
            t.BuildId = c.Id; t.BuildPrefab = c.Prefab;
            return t;
        }

        // ---- doing it -----------------------------------------------------------------------------------------------------

        /// <summary>At a chest on a materials trip: what it wanted out of it into its bag.</summary>
        public static void Take(BrainState st, Container chest)
        {
            Humanoid me = st.Body;
            if (!Containers.Take(chest)) return; // (someone has it open; else what is really in it)
            var took = new Dictionary<string, int>();
            foreach (var want in st.BuildWant.ToList())
            {
                int left = want.Value;
                foreach (ItemDrop.ItemData item in chest.GetInventory().GetAllItems().Where(i => i.m_shared.m_name == want.Key).ToList())
                {
                    if (left <= 0) break;
                    int n = Mathf.Min(left, item.m_stack);
                    if (!Work.Move(chest, me, item, n)) continue;
                    left -= n;
                    took[want.Key] = (took.TryGetValue(want.Key, out int had) ? had : 0) + n;
                }
                if (left > 0) st.BuildWant[want.Key] = left; else st.BuildWant.Remove(want.Key);
            }
            if (took.Count == 0) { st.Skipped[chest.gameObject.GetInstanceID()] = Time.time + 300f; return; }
            Companion.SaveBag(me);
            st.NextBuildLook = 0f;
            st.Remember($"took {Say(took)} for building from {(Home.IdOn(chest) == 0L ? "your" : "its")} chest");
        }

        /// <summary>It was close enough with the materials in its bag: the piece goes up, and it pays for it. False when it did not.</summary>
        public static bool Place(BrainState st, Work.Task t)
        {
            Humanoid me = st.Body;
            Dictionary<string, int> needs = Needs(t.BuildPrefab);
            Dictionary<string, int> bag = Count(new[] { me.GetInventory() });
            if (needs == null || !Covers(bag, needs)) return false; // (the materials went somewhere else on the way)
            bool ok;
            try { ok = PlaceFn(t.BuildId); } catch (Exception e) { Plugin.Instance?.Warn("Building a plan piece: " + e.Message); ok = false; }
            if (!ok) { st.BuildSkip[t.BuildId] = Time.time + 600f; st.Remember($"could not build {Localization.instance.Localize(PieceOf(t.BuildPrefab)?.m_name ?? t.BuildPrefab)}: left for later"); return false; }
            foreach (var kv in needs) me.GetInventory().RemoveItem(kv.Key, kv.Value);
            Companion.SaveBag(me);
            int n = Journal.Count(me, "built");
            st.Built++;
            st.BuildList.RemoveAll(c => c.Id == t.BuildId);
            st.NextBuildLook = Time.time + 0.3f;
            string name = Localization.instance.Localize(PieceOf(t.BuildPrefab)?.m_name ?? t.BuildPrefab).ToLowerInvariant();
            if (st.Built == 1) Talk.Mention(me, "Started on your plan.", "buildstart", 600f);
            if (n % 10 == 1 || Pending(me) <= 1) st.Remember($"built {name} (it has put up {n} piece{(n == 1 ? "" : "s")} of your plans)");
            Counted.Remove(Companion.IdOf(me));
            if (Pending(me) <= 0) Talk.Mention(me, "Your plan is finished!", "builddone", 60f);
            return true;
        }

        /// <summary>It could not get to the piece: left alone for a while.</summary>
        public static void Failed(BrainState st, Work.Task t)
        {
            if (t.BuildId != null) st.BuildSkip[t.BuildId] = Time.time + 600f;
        }

        // ---- while you were away -----------------------------------------------------------------------------------------------

        /// <summary>
        /// Catching up (CatchUp): in the time it had, pieces of your plans put up one after another (a piece takes about thirty seconds of its
        /// time, with the walk), paid from its bag and the chests, as it would build them, until its time is down to "floor" (what is kept for its
        /// other duties). How many it built, and what it ran short of.
        /// </summary>
        public static int CatchUp(BrainState st, ref float budget, float floor, out string shortOf)
        {
            Humanoid me = st.Body;
            shortOf = null;
            if (!Available || Pending(me) == 0) return 0;
            if (Mending.Hammer(me) == null) { Mending.MakeHammer(st); if (Mending.Hammer(me) == null) { shortOf = "a hammer"; return 0; } }
            Vector3 center = Work.Center(me);
            int built = 0;
            // A batch at a time, in the order BuildOrders gives (each piece after what holds it up, as Build all does): asking again after every
            // piece would work out the whole plan's stability each time. A piece it cannot pay for ends the batch (what rests on it waits).
            for (int round = 0; round < 40 && budget >= 30f + floor; round++)
            {
                List<Cand> next = Parse(NextFn(center, Reach(me), 24)).Where(c => !(st.BuildSkip.TryGetValue(c.Id, out float until) && until > Time.time)).ToList();
                if (next.Count == 0) break;
                int before = built;
                foreach (Cand c in next)
                {
                    if (budget < 30f + floor) break;
                    Dictionary<string, int> needs = Needs(c.Prefab);
                    if (needs == null) continue;
                    if (!Pay(me, needs)) { shortOf = Say(Missing(Count(Sources(me).Select(x => x.GetInventory()).Concat(new[] { me.GetInventory() })), needs)); break; }
                    bool ok = false;
                    try { ok = PlaceFn(c.Id); } catch (Exception) { }
                    if (!ok) { Refund(me, needs); st.BuildSkip[c.Id] = Time.time + 600f; break; }
                    budget -= 30f; built++;
                    Journal.Count(me, "built");
                }
                if (built == before) break;   // nothing went up this round: no more to do
                if (shortOf != null) break;
            }
            if (built > 0) { shortOf = null; Counted.Remove(Companion.IdOf(me)); }
            Companion.SaveBag(me);
            return built;
        }

        /// <summary>Take what the piece costs from its bag, then its chests and yours. False (and nothing taken) when it is not all there.</summary>
        private static bool Pay(Humanoid me, Dictionary<string, int> needs)
        {
            // The chests holding any of it are taken over and loaded fresh first (another player may have just taken from one), and
            // counted again: what is paid is what is really there.
            List<Container> chests = Containers.TakeAll(Sources(me).Where(c => c != null && needs.Keys.Any(k => c.GetInventory().CountItems(k) > 0)));
            var inventories = new List<Inventory> { me.GetInventory() };
            inventories.AddRange(chests.Select(c => c.GetInventory()));
            if (!Covers(Count(inventories), needs)) return false;
            foreach (var kv in needs)
            {
                int left = kv.Value;
                for (int k = 0; k < inventories.Count && left > 0; k++)
                {
                    int n = Mathf.Min(left, inventories[k].CountItems(kv.Key));
                    if (n <= 0) continue;
                    inventories[k].RemoveItem(kv.Key, n);
                    left -= n;
                }
            }
            return true;
        }

        private static void Refund(Humanoid me, Dictionary<string, int> needs)
        {
            foreach (var kv in needs)
            {
                GameObject prefab = ItemByName(kv.Key);
                if (prefab != null) me.GetInventory().AddItem(prefab, kv.Value);
            }
        }
    }
}
