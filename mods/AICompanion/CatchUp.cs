using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace AICompanion
{
    /// <summary>
    /// Living at home while nobody is there. Valheim only runs the world near players, so a companion left at home is frozen while you are
    /// away. Instead of keeping its area running (deep in the game's world code, costly, and where lost-item bugs come from), it catches up
    /// when someone comes back and its area loads: it works out how long it was away (world time) and does that much work at once, on the
    /// real world around it, then tells you what it did:
    ///   * something to eat: it cooks its raw food, takes food from its chests (a little from yours when it has none), and stays fed;
    ///   * hunting (when hungry, or for its goal's hides), felling trees (a stump is left), mining rocks and ore, picking wild berries and
    ///     mushrooms, at a player's pace with its tools, what its goal needs first; the game's own drop tables decide what it gets;
    ///   * making and upgrading gear toward its goal (and bronze and the like on the way), and repairs, at its stations;
    ///   * its food burns down over that time and it eats from its bag, and heals with what it ate, as a player does;
    ///   * with encounters on (Companion, WhileAway: Mild), it meets the creatures of its biome now and then, more at night and in harder
    ///     places, fights them off and keeps what they drop (Mild: it never falls while you are away).
    /// One game does this (the one that runs the companion), once per return.
    /// </summary>
    internal static class CatchUp
    {
        public const string LastKey = "dhc_lastsim";
        private const float MinAway = 120f, MaxAway = 3f * 1800f; // at most three in-game days of work at once

        private static readonly Dictionary<Heightmap.Biome, string[]> Creatures = new Dictionary<Heightmap.Biome, string[]>
        {
            [Heightmap.Biome.Meadows] = new[] { "Boar", "Neck", "Greyling" },
            [Heightmap.Biome.BlackForest] = new[] { "Greydwarf", "Greydwarf", "Greydwarf_Elite", "Skeleton" },
            [Heightmap.Biome.Swamp] = new[] { "Draugr", "Leech", "Skeleton_Poison", "Blob" },
            [Heightmap.Biome.Mountain] = new[] { "Wolf", "Fenring", "Hatchling" },
            [Heightmap.Biome.Plains] = new[] { "Goblin", "Deathsquito", "Lox" },
            [Heightmap.Biome.Mistlands] = new[] { "Seeker", "Tick" },
            [Heightmap.Biome.AshLands] = new[] { "Charred_Melee", "Asksvin" },
        };

        private static readonly Dictionary<Heightmap.Biome, float> FightsPerHour = new Dictionary<Heightmap.Biome, float>
        {
            [Heightmap.Biome.Meadows] = 0.4f, [Heightmap.Biome.BlackForest] = 1.2f, [Heightmap.Biome.Swamp] = 2f, [Heightmap.Biome.Mountain] = 1.5f,
            [Heightmap.Biome.Plains] = 2f, [Heightmap.Biome.Mistlands] = 2f, [Heightmap.Biome.AshLands] = 3f,
        };

        // How hard the creatures of each biome are, for Real (compared with its weapon, skill, armour and health).
        private static readonly System.Collections.Generic.Dictionary<Heightmap.Biome, float> Threat = new System.Collections.Generic.Dictionary<Heightmap.Biome, float>
        {
            [Heightmap.Biome.Meadows] = 40f, [Heightmap.Biome.BlackForest] = 90f, [Heightmap.Biome.Swamp] = 160f, [Heightmap.Biome.Mountain] = 200f,
            [Heightmap.Biome.Plains] = 280f, [Heightmap.Biome.Mistlands] = 350f, [Heightmap.Biome.AshLands] = 450f,
        };

        public static float ThreatOf(Heightmap.Biome b) => Threat.TryGetValue(b, out float t) ? t : 100f;

        internal static float Power(Humanoid me)
        {
            ItemDrop.ItemData w = Companion.BestMelee(me) ?? Companion.BestRanged(me);
            float weapon = w != null ? w.GetDamage().GetTotalDamage() * Mathf.Lerp(0.4f, 1f, Skill.Get(me, w.m_shared.m_skillType) / 100f) * 3f : 5f;
            return weapon + Companion.Armor(me) * 2f + me.GetMaxHealth();
        }

        /// <summary>Real: it lost a fight while you were away. As a player: its things into a tombstone where it fell (near home: it goes back for
        /// them), its food gone, 5% off its skills; it woke at home.</summary>
        private static Vector3 Fall(Humanoid me, Vector3 center, float radius)
        {
            Vector2 r = UnityEngine.Random.insideUnitCircle * radius * 0.6f;
            Vector3 at = center + new Vector3(r.x, 0f, r.y);
            if (ZoneSystem.instance != null && ZoneSystem.instance.GetSolidHeight(at, out float h)) at.y = h + 0.5f;
            Inventory inv = me.GetInventory();
            var worn = new HashSet<ItemDrop.ItemData>(inv.GetAllItems().Where(Gear.InSlot));
            foreach (ItemDrop.ItemData i in inv.GetAllItems()) i.m_equipped = worn.Contains(i); // it keeps its gear slots: the move leaves them out
            bool any = inv.NrOfItems() > worn.Count;
            if (any)
            {
                GameObject scene = ZNetScene.instance.GetPrefab("Player");
                GameObject tombPrefab = scene != null ? scene.GetComponent<Player>()?.m_tombstone : null;
                if (tombPrefab != null)
                {
                    GameObject tomb = UnityEngine.Object.Instantiate(tombPrefab, at, Quaternion.identity);
                    tomb.GetComponent<Container>().GetInventory().MoveInventoryToGrave(inv);
                    tomb.GetComponent<TombStone>()?.Setup(Companion.NameOf(me), Companion.MasterId(me));
                    tomb.GetComponent<ZNetView>().GetZDO().Set(Net.CrateKey, Companion.NameOf(me));
                    tomb.GetComponent<ZNetView>().GetZDO().Set(Grave.OfKey, Companion.IdOf(me));
                }
            }
            foreach (ItemDrop.ItemData i in inv.GetAllItems()) i.m_equipped = false;
            Companion.SaveBag(me);
            ZDO z = Companion.Zdo(me);
            z.Set(Grave.HasKey, any);
            z.Set(Grave.PosKey, at);
            z.Set(Skill.Key, Skill.AfterDeath(z.GetString(Skill.Key, "")));
            Skill.Forget(me);
            Food.Clear(me);
            if (any) Net.AnnounceFall(me, at, true);
            return at;
        }

        /// <summary>Every few seconds while it lives at home on this game: "it was here and working at this world time".</summary>
        public static void Stamp(Humanoid c) => Companion.Zdo(c)?.Set(LastKey, (long)ZNet.instance.GetTimeSeconds());

        /// <summary>The first time this game runs it after it was away (its area just loaded): catch up, then report.</summary>
        public static void OnArrive(BrainState st)
        {
            Humanoid me = st.Body;
            st.CaughtUp = true;
            ZDO z = Companion.Zdo(me);
            long last = z.GetLong(LastKey, 0L);
            Stamp(me);
            if (last <= 0L || Companion.OrderOf(me) != Order.Gather) return;
            float away = (float)(ZNet.instance.GetTimeSeconds() - last);
            if (away < MinAway) return;
            Plugin.Instance.StartCoroutine(Run(st, Mathf.Min(away, MaxAway), away));
        }

        internal static string Stage = ""; // the step of a catch-up it is on (for the log, if one goes wrong)

        /// <summary>Claude Tools: a catch-up of this many seconds now, as if it had been away; what went wrong, or null.</summary>
        internal static string Test(BrainState st, float seconds)
        {
            Talk.Hush = true;
            try { Day(st, seconds, seconds); return null; }
            catch (Exception e) { return $"at {Stage}: {e}"; }
            finally { Talk.Hush = false; }
        }

        private static IEnumerator Run(BrainState st, float seconds, float away)
        {
            yield return new WaitForSeconds(3f); // let the trees, rocks and chests around it finish loading
            Humanoid me = st.Body;
            if (me == null || me.IsDead()) yield break;
            Talk.Hush = true; // what it did goes into one report, not a chat line for each thing
            try { Day(st, seconds, away); }
            catch (Exception e) { Plugin.Instance?.Warn($"Companion catch-up (at {Stage}): " + e); }
            finally { Talk.Hush = false; }
        }

        /// <summary>
        /// Its time at home, in the order a player's day would go: something to eat (cook raw food, take food from its chests, a little from
        /// yours when starving), creatures that came by, hunting, gathering (what its goal needs first, food first when hungry), storing,
        /// cooking what it hunted, making and upgrading gear toward its goal, repairs, then the time passing: its food burns down, it eats,
        /// and it heals with what it ate.
        /// </summary>
        private static void Day(BrainState st, float seconds, float away)
        {
            Humanoid me = st.Body;
            var got = new Dictionary<string, int>();
            var notes = new List<string>();
            var also = new List<string>();
            var did0 = new List<string>();   // what its duties did (for the report)
            float budget = seconds;
            int felled = 0, mined = 0, picked = 0, fights = 0;
            string fellTo = null;
            Vector3 center = Work.Center(me);
            float radius = Work.RadiusOf(me);

            Stage = "food";
            // 1. Something to eat for the time away.
            int cooked = Kitchen.CookAll(me, center, radius);
            string fromYou = Provision(me, center, radius);

            Stage = "creatures";
            // 2. Creatures that came by (Mild: it always comes through).
            if (Plugin.WhileAway.Value != AwayMode.Off)
            {
                Heightmap.Biome biome = WorldGenerator.instance != null ? WorldGenerator.instance.GetBiome(me.transform.position) : Heightmap.Biome.Meadows;
                float rate = FightsPerHour.TryGetValue(biome, out float r) ? r : 1f;
                int expected = Mathf.Min(8, Mathf.FloorToInt(Hours(seconds) / 24f * rate * 3f + UnityEngine.Random.value)); // a few a day, more in harder places
                for (int i = 0; i < expected; i++)
                {
                    string prefab = Creatures.TryGetValue(biome, out string[] list) ? list[UnityEngine.Random.Range(0, list.Length)] : null;
                    GameObject go = prefab != null ? ZNetScene.instance.GetPrefab(prefab) : null;
                    if (go == null) continue;
                    fights++;
                    budget -= 60f;
                    if (Plugin.WhileAway.Value == AwayMode.Real)
                    {
                        float threat = Threat.TryGetValue(biome, out float th) ? th : 100f;
                        float power = Power(me);
                        if (UnityEngine.Random.value > power / (power + threat * 0.4f))
                        {
                            fellTo = Localization.instance.Localize(go.GetComponent<Character>()?.m_name ?? prefab);
                            Fall(me, center, radius);
                            budget = 0f; // the rest of the time it was making its way back home
                            break;
                        }
                    }
                    Drops(got, go);
                    Wear(me, 0.03f);
                    ItemDrop.ItemData weapon = me.GetCurrentWeapon();
                    if (weapon != null) Skill.Raise(me, weapon.m_shared.m_skillType, 3f);
                    string name = Localization.instance.Localize(go.GetComponent<Character>()?.m_name ?? prefab);
                    if (!notes.Contains(name)) notes.Add(name);
                }
            }

            Stage = "hunting";
            // 3. Hunting: when hungry, or when its goal needs hides and the like (and it has something to hunt with).
            bool hungry = !me.GetInventory().GetAllItems().Any(Food.IsFood);
            st.Goal = fellTo == null ? Goals.Pick(me, center, radius, st) : null;
            Job goalJobs = Goals.JobsFor(st.Goal, out HashSet<string> goalPrey);
            var hunted = new Dictionary<string, int>();
            if (fellTo == null && (Companion.BestRanged(me) != null || Companion.BestMelee(me) != null) && (hungry || goalPrey.Count > 0 || Duties.Open(me, Duty.Food)))
                Hunt(me, got, hunted, goalPrey, ref budget, seconds);

            Stage = "gathering";
            // 4. Gathering, at a player's pace, on what is really there: what its goal needs first, food first when hungry.
            Job jobs = Work.JobsOf(me);
            if (Duties.Configured(me)) jobs = Duties.OpenJobs(me) | (goalJobs & ~(Job.Loot | Job.Hunt)); // its home duties still short of their stockpile, then its goal
            else if (jobs == Job.None) jobs = Work.AutoJobs(me) | (goalJobs & ~(Job.Loot | Job.Hunt));
            ItemDrop.ItemData axe = Work.Axe(me), pick = Work.Pickaxe(me);
            var seen = new HashSet<GameObject>();
            var targets = new List<Component>();
            foreach (Collider col in Physics.OverlapSphere(center, radius, ~0, QueryTriggerInteraction.Collide))
            {
                GameObject go = col.attachedRigidbody != null ? col.attachedRigidbody.gameObject : col.transform.root.gameObject;
                if (!seen.Add(go) || go.GetComponent<Piece>() != null || go.GetComponent<Character>() != null) continue;
                Component t = (Component)go.GetComponent<TreeLog>() ?? (Component)go.GetComponent<TreeBase>() ?? (Component)go.GetComponent<MineRock5>() ?? (Component)go.GetComponent<MineRock>()
                              ?? (Component)go.GetComponent<Destructible>() ?? go.GetComponent<Pickable>();
                if (t != null) targets.Add(t);
            }
            targets = targets.OrderBy(t => hungry && Work.Edible(t) ? 0 : st.Goal != null && Work.Drops(t).Any(st.Goal.Wants) ? 1 : 2)
                             .ThenBy(t => Vector3.Distance(t.transform.position, center)).ToList();

            // One pass over what is around it, with the jobs set in "jobs", until its time is up or "done" says it has enough.
            void Gather(Func<bool> done)
            {
            foreach (Component t in targets)
            {
                if (budget <= 0f || done != null && done()) break;
                if (Free(me) <= 1) { notes.Add("its bag and chests are full"); break; }
                switch (t)
                {
                    case Pickable p when (jobs & Job.Forage) != 0 && p.m_respawnTimeMinutes > 0f && p.m_itemPrefab != null && !(Companion.Zdo(p)?.GetBool(ZDOVars.s_picked, false) ?? true)
                                         && (!hungry || Work.Edible(p) || st.Goal != null && st.Goal.Wants(p.m_itemPrefab.name)):
                        Add(got, p.m_itemPrefab, p.m_amount);
                        foreach (GameObject extra in p.m_extraDrops.GetDropList()) Add(got, extra, 1);
                        p.GetComponent<ZNetView>()?.InvokeRPC(ZNetView.Everybody, "RPC_SetPicked", true);
                        budget -= 15f; picked++;
                        break;
                    case TreeLog log when (jobs & Job.Wood) != 0 && axe != null && axe.m_shared.m_toolTier >= log.m_minToolTier:
                        if (!Chop(me, axe, log.m_health, ref budget)) break;
                        AddTable(got, log.m_dropWhenDestroyed); AddSubLogs(got, log.m_subLogPrefab, 3);
                        Remove(log.gameObject); felled++;
                        break;
                    case TreeBase tree when (jobs & Job.Wood) != 0 && axe != null && axe.m_shared.m_toolTier >= tree.m_minToolTier:
                        if (!Chop(me, axe, tree.m_health, ref budget)) break;
                        AddTable(got, tree.m_dropWhenDestroyed);
                        TreeLog fallen = tree.m_logPrefab != null ? tree.m_logPrefab.GetComponent<TreeLog>() : null;
                        if (fallen != null) { AddTable(got, fallen.m_dropWhenDestroyed); AddSubLogs(got, fallen.m_subLogPrefab, 3); budget -= 40f; }
                        if (tree.m_stubPrefab != null) UnityEngine.Object.Instantiate(tree.m_stubPrefab, tree.transform.position, tree.transform.rotation); // the stump it leaves
                        Remove(tree.gameObject); felled++;
                        break;
                    case MineRock5 rock5 when pick != null && pick.m_shared.m_toolTier >= rock5.m_minToolTier && (jobs & (Work.IsOre(rock5.m_dropItems) ? Job.Ore : Job.Stone)) != 0:
                        int areas = (AccessTools.Field(typeof(MineRock5), "m_hitAreas").GetValue(rock5) as IList)?.Count ?? 4;
                        if (!Chop(me, pick, rock5.m_health * areas, ref budget)) break;
                        for (int i = 0; i < areas; i++) AddTable(got, rock5.m_dropItems);
                        Remove(rock5.gameObject); mined++;
                        break;
                    case MineRock rock when pick != null && pick.m_shared.m_toolTier >= rock.m_minToolTier && (jobs & (Work.IsOre(rock.m_dropItems) ? Job.Ore : Job.Stone)) != 0:
                        if (!Chop(me, pick, rock.m_health, ref budget)) break;
                        AddTable(got, rock.m_dropItems);
                        Remove(rock.gameObject); mined++;
                        break;
                    case Destructible des:
                        DropTable table = des.GetComponent<DropOnDestroyed>()?.m_dropWhenDestroyed;
                        if (table == null || table.m_drops.Count == 0) break;
                        bool wood = des.m_destructibleType == DestructibleType.Tree;
                        ItemDrop.ItemData tool = wood ? axe : pick;
                        if (tool == null || tool.m_shared.m_toolTier < des.m_minToolTier) break;
                        if ((jobs & (wood ? Job.Wood : Work.IsOre(table) ? Job.Ore : Job.Stone)) == 0) break;
                        if (!Chop(me, tool, des.m_health, ref budget)) break;
                        AddTable(got, table);
                        Remove(des.gameObject);
                        if (wood) felled++; else mined++;
                        break;
                }
            }
            }

            // Its home duties, in the order you set, each with its share of the time (a stockpile that reaches its target, your plans built),
            // then its own life with what is left; without duties, as before. Each one shows progress for the time it was away.
            Job jobs0 = jobs;   // (what it works without duties)
            int builtHere = 0;
            string shortOf = null;
            Goal planGoal = null;   // what your plan was still short of: gathered, made and fetched below, then built with what comes of it

            // Your plans: what it can pay for put up first; short of materials, a pass over what is around it for what the plan needs (a share
            // of its time), the rest kept for putting up what it brings in (below, once it is stored).
            int BuildSlice(float floor)
            {
                float from = budget;
                int n = Building.CatchUp(st, ref budget, floor, out string sh);
                shortOf = shortOf ?? sh;
                Goal plan = Building.ShortFor(st);
                if (plan == null) return n;
                planGoal = plan;
                if (plan.Raw.Count == 0) return n;
                float keep = floor + (from - floor) * 0.4f;
                jobs = Goals.JobsFor(plan, out HashSet<string> prey) & ~(Job.Loot | Job.Hunt);
                targets = targets.OrderBy(t => Work.Drops(t).Any(plan.Wants) ? 0 : 1).ThenBy(t => Vector3.Distance(t.transform.position, center)).ToList();
                Gather(() => plan.Raw.All(kv => got.TryGetValue(kv.Key, out int have) && have >= kv.Value) || budget <= keep);
                if (prey.Count > 0 && budget > keep && (Companion.BestRanged(me) != null || Companion.BestMelee(me) != null)) Hunt(me, got, hunted, prey, ref budget, seconds);
                return n;
            }

            if (Duties.Configured(me))
            {
                var open = Duties.OnEntries(me).Where(e => e.Duty == Duty.Build ? Building.Pending(me) > 0 : Duties.Have(me, e.Duty) < e.Target).ToList();
                float slice = open.Count > 0 ? budget / open.Count : 0f;
                foreach (Duties.Entry e in open)
                {
                    float floor = Mathf.Max(0f, budget - slice);
                    if (e.Duty == Duty.Build) { builtHere += BuildSlice(floor); continue; }
                    int baseHave = Duties.Have(me, e.Duty);
                    jobs = e.Jobs;
                    Gather(() => baseHave + Duties.Accrued(e.Duty, got) >= e.Target || budget <= floor);
                }
                jobs = goalJobs & ~(Job.Loot | Job.Hunt);
                Gather(null);
            }
            else
            {
                if (Duties.Open(me, Duty.Build)) builtHere += BuildSlice(budget / 2f); // (half its time at most: the rest for its own work)
                jobs = jobs0;
                Gather(null);
            }

            Stage = "trip";
            // 4b. What its goal still needs and nothing near home has: a trip further out, through the saved world (it is not loaded): flint on
            //     a shore 300 m off, and the like.
            string trip = fellTo == null ? AwayTrip(me, center, radius, got, ref budget) : null;
            if (trip == null && fellTo == null && planGoal != null && planGoal.Raw.Count > 0)
                trip = AwayTrip(me, center, radius, got, ref budget, Remaining(planGoal, got)); // (what the plan still lacks after what it gathered near home)
            if (trip != null) Journal.Trip(me, $"While you were away it {trip}.");

            Stage = "storing";
            // 5. Into its chests (and its bag); then it cooks what it hunted and picked.
            int total = got.Values.Sum();
            int stored = Store(me, got);
            Work.SortHome(st); // (with QualityOfLife: what it carries into your chests by your rules)
            Armory.CatchUp(st, center, radius); // (better gear from your chests, and its old gear back in them)
            cooked += Kitchen.CookAll(me, center, radius);

            Stage = "plan";
            // What it brought in for your plan is in the chests now: the in-between things made (nails and the like), then everything it can
            // put up with the time it kept.
            if (planGoal != null && fellTo == null)
            {
                for (int k = 0; k < 20; k++)
                {
                    var step = Goals.StepReady(me, planGoal);
                    if (step == null || Upgrades.Craft(st, step.Value.Key, step.Value.Value, true) == null) break;
                }
                int more = Building.CatchUp(st, ref budget, 0f, out string sh2);
                builtHere += more;
                if (more > 0) shortOf = null; else shortOf = shortOf ?? sh2;
            }

            Stage = "crafting";
            // 6. Making and upgrading gear: what is ready, and its goal's in-between materials (bronze), as long as there is material.
            var made = new List<string>();
            for (int round = 0; round < 12 && fellTo == null; round++)
            {
                string done = null;
                var up = Upgrades.Find(me, center, radius);
                if (up != null) done = Upgrades.Do(st, up.Value.Key, up.Value.Value);
                if (done == null) { var craft = Upgrades.FindCraft(me, center, radius); if (craft != null) done = Upgrades.Craft(st, craft.Value.Key, craft.Value.Value); }
                if (done == null)
                {
                    st.Goal = Goals.Pick(me, center, radius, st);
                    var step = Goals.StepReady(me, st.Goal);
                    if (step != null) done = Upgrades.Craft(st, step.Value.Key, step.Value.Value, true);
                }
                if (done == null) break;
                made.Add(done);
            }

            Stage = "repairs";
            // 7. Repairs at its stations, as a player would before heading out again; and your base, with its hammer.
            int repaired = Repair.All(me, center, radius);
            int mended = Mending.Hammer(me) != null ? Mending.FixAll(st, center, radius) : 0;

            Stage = "time";
            // 8. The time passing: its food burns down and it eats from its bag; fed, it heals as a player does. Then food on it for later.
            Food.PassTime(me, st, seconds);
            if (Food.Meals(me).Count > 0 && fellTo == null) me.Heal(me.GetMaxHealth(), false);
            string later = Provision(me, center, radius);
            if (fromYou == null) fromYou = later;
            Companion.SaveBag(me); // its tools' and armour's wear, and what it made
            Portraits.Dirty(me);

            Stage = "report";
            // The report.
            var did = new List<string>(did0);
            if (builtHere > 0) did.Add($"built {builtHere} piece{(builtHere == 1 ? "" : "s")} of your plan");
            else if (planGoal != null && planGoal.Ask.Count > 0) did.Add($"could not finish your plan: it needs {string.Join(" and ", planGoal.Ask)} that it cannot get itself");
            else if (shortOf != null) did.Add($"could not build your plan: it needs {shortOf}");
            if (felled > 0) did.Add($"felled {felled} tree{(felled == 1 ? "" : "s")}");
            if (mined > 0) did.Add($"mined {mined} rock{(mined == 1 ? "" : "s")}");
            if (picked > 0) did.Add($"picked {picked} bush{(picked == 1 ? "" : "es")}");
            if (hunted.Count > 0) did.Add("hunted " + string.Join(", ", hunted.Select(kv => $"{kv.Value} {kv.Key}")));
            if (fights > 0) did.Add($"fought off {fights} creature{(fights == 1 ? "" : "s")} ({string.Join(", ", notes.Where(n => !n.Contains("full")).Take(3))})");
            if (cooked > 0) did.Add($"cooked {cooked} meal{(cooked == 1 ? "" : "s")}");
            if (made.Count > 0) did.Add(string.Join(", ", made));
            if (repaired > 0) did.Add($"repaired {repaired} thing{(repaired == 1 ? "" : "s")}");
            if (mended > 0) did.Add($"repaired {mended} damaged piece{(mended == 1 ? "" : "s")} of your base");
            if (trip != null) did.Add(trip);
            if (did.Count == 0 && fellTo == null && fromYou == null) return;
            string items = string.Join(", ", got.OrderByDescending(kv => kv.Value).Take(8).Select(kv => $"{kv.Value} {kv.Key}"));
            string line = $"While you were away ({Mathf.RoundToInt(away / 60f)} min) {Companion.NameOf(me)} {(did.Count > 0 ? string.Join(", ", did) : "rested at home")}" +
                          (items.Length > 0 ? $". Brought in: {items}" : "") + (stored < total ? " (kept the rest in their bag)" : "") +
                          (notes.Any(n => n.Contains("full")) ? ". Their chests are full" : "") + "." +
                          (fromYou != null ? $" Took {fromYou} from your chests to eat." : "") +
                          (Food.Meals(me).Count == 0 && fellTo == null ? " They have nothing left to eat." : "") +
                          (fellTo != null ? $" Then they fell to a {fellTo} and woke at home; their things are in their tombstone nearby (they will go and get them)." : "");
            Talk.Hush = false;
            st.Remember(line);
            Plugin.Instance?.Note(line);
            Player master = Companion.Master(me);
            if (master != null && master == Player.m_localPlayer) master.Message(MessageHud.MessageType.Center, line); // (its player not here: both were null, and equal)
            Talk.Tell(me, line);
        }

        /// <summary>
        /// A trip while you were away for what its goal still needs: the wild pickables that give it (never crops), anywhere within 400 m of
        /// home in the world as saved, nearest first. Each one picked is marked picked in the world (when its area loads, the game hides or
        /// removes it, as if picked there). The walk there and back takes its time. What it did ("went 260 m south for 4 flint"), or null.
        /// </summary>
        /// <summary>The goal less what it has already gathered (prefab name to number), for a trip for what is still missing.</summary>
        private static Goal Remaining(Goal g, Dictionary<string, int> got)
        {
            var rest = new Goal { What = g.What };
            foreach (var kv in g.Raw)
            {
                int left = kv.Value - (got.TryGetValue(kv.Key, out int have) ? have : 0);
                if (left <= 0) continue;
                rest.Raw[kv.Key] = left;
                rest.Names[kv.Key] = g.Names.TryGetValue(kv.Key, out string name) ? name : kv.Key;
            }
            return rest;
        }

        private static string AwayTrip(Humanoid me, Vector3 center, float radius, Dictionary<string, int> got, ref float budget, Goal over = null)
        {
            if (budget < 300f || ZDOMan.instance == null) return null;
            Goal g = over ?? Goals.Pick(me, center, radius); // (what it still needs, near home or not)
            if (g == null || g.Raw.Count == 0) return null;
            var brought = new Dictionary<string, int>();
            float farthest = 0f;
            Vector3 farWay = Vector3.zero;
            var list = new List<ZDO>();
            foreach (var need in g.Raw)
            {
                int left = need.Value;
                GameObject item = ObjectDB.instance.GetItemPrefab(need.Key);
                if (item == null) continue;
                foreach (string source in Goals.PickablesFor(need.Key))
                {
                    if (left <= 0) break;
                    GameObject sourcePrefab = ZNetScene.instance.GetPrefab(source);
                    Pickable kind = sourcePrefab != null ? sourcePrefab.GetComponent<Pickable>() : null;
                    if (kind == null) continue;
                    list.Clear();
                    int index = 0;
                    for (int guard = 0; guard < 1000 && !ZDOMan.instance.GetAllZDOsWithPrefabIterative(source, list, ref index); guard++) { }
                    foreach (ZDO z in list.Where(z => z != null && !z.GetBool(ZDOVars.s_picked, false) && Vector3.Distance(z.GetPosition(), center) < 400f)
                                          .OrderBy(z => Vector3.Distance(z.GetPosition(), center)))
                    {
                        if (left <= 0 || budget <= 0f) break;
                        Vector3 at = z.GetPosition();
                        ZNetView loaded = ZNetScene.instance.FindInstance(z);
                        if (loaded != null) { if (loaded.GetComponent<Pickable>() == null) continue; loaded.InvokeRPC(ZNetView.Everybody, "RPC_SetPicked", true); }
                        else
                        {
                            z.SetOwner(ZDOMan.GetSessionID());
                            z.Set(ZDOVars.s_picked, true);
                            z.Set(ZDOVars.s_pickedTime, ZNet.instance.GetTime().Ticks);
                        }
                        int n = Mathf.Max(1, kind.m_amount);
                        Add(got, item, n);
                        string name = Localization.instance.Localize(item.GetComponent<ItemDrop>().m_itemData.m_shared.m_name).ToLowerInvariant();
                        brought[name] = (brought.TryGetValue(name, out int had) ? had : 0) + n;
                        left -= n;
                        budget -= 20f;
                        float d = Vector3.Distance(at, center);
                        if (d > farthest) { farthest = d; farWay = at - center; }
                    }
                }
            }
            if (brought.Count == 0) return null;
            budget -= farthest * 2f / 3f; // there and back, at a walk
            string[] names = { "north", "north-east", "east", "south-east", "south", "south-west", "west", "north-west" };
            string dir = names[Mathf.FloorToInt(((Mathf.Atan2(farWay.x, farWay.z) * Mathf.Rad2Deg + 360f + 22.5f) % 360f) / 45f) % 8];
            return $"went {farthest:0} m {dir} for {string.Join(", ", brought.Select(kv => $"{kv.Value} {kv.Key}"))}";
        }

        private static float Hours(float seconds) => seconds / (EnvMan.instance != null ? EnvMan.instance.m_dayLengthSec / 24f : 75f); // in-game hours

        /// <summary>What a creature drops, by the game's own drop list.</summary>
        private static void Drops(Dictionary<string, int> got, GameObject creature)
        {
            CharacterDrop drops = creature.GetComponent<CharacterDrop>();
            if (drops == null) return;
            foreach (CharacterDrop.Drop d in drops.m_drops)
                if (d.m_prefab != null && UnityEngine.Random.value <= d.m_chance) Add(got, d.m_prefab, UnityEngine.Random.Range(d.m_amountMin, d.m_amountMax + 1));
        }

        private static void Wear(Humanoid me, float share)
        {
            foreach (ItemDrop.ItemData worn in Companion.Worn(me).Where(w => w.m_shared.m_useDurability))
                worn.m_durability = Mathf.Max(worn.GetMaxDurability() * 0.1f, worn.m_durability - worn.GetMaxDurability() * share); // wear, never broken
        }

        private static readonly Dictionary<Heightmap.Biome, string[]> Quarry = new Dictionary<Heightmap.Biome, string[]>
        {
            [Heightmap.Biome.Meadows] = new[] { "Deer", "Deer", "Boar", "Neck" },
            [Heightmap.Biome.BlackForest] = new[] { "Deer", "Deer", "Boar" },
            [Heightmap.Biome.Mountain] = new[] { "Wolf" },
            [Heightmap.Biome.Plains] = new[] { "Lox", "Deer" },
        };

        /// <summary>
        /// Hunting near home: about two kills a day (three with a bow), of its biome's game, or what its goal needs (deer for hides); each takes a
        /// while and wears its gear a little. The game's own drop lists decide what it brings in.
        /// </summary>
        private static void Hunt(Humanoid me, Dictionary<string, int> got, Dictionary<string, int> hunted, HashSet<string> goalPrey, ref float budget, float seconds)
        {
            Heightmap.Biome biome = WorldGenerator.instance != null ? WorldGenerator.instance.GetBiome(me.transform.position) : Heightmap.Biome.Meadows;
            string[] kinds = goalPrey.Count > 0 ? goalPrey.ToArray() : Quarry.TryGetValue(biome, out string[] k) ? k : null;
            if (kinds == null || kinds.Length == 0) return;
            bool bow = Companion.BestRanged(me) != null;
            int kills = Mathf.Min(6, Mathf.FloorToInt(Hours(seconds) / 24f * (bow ? 3f : 2f) + UnityEngine.Random.value));
            ItemDrop.ItemData weapon = Companion.BestRanged(me) ?? Companion.BestMelee(me);
            for (int i = 0; i < kills && budget > 0f; i++)
            {
                GameObject go = ZNetScene.instance.GetPrefab(kinds[UnityEngine.Random.Range(0, kinds.Length)]);
                if (go == null) continue;
                Drops(got, go);
                Wear(me, 0.01f);
                if (weapon != null) Skill.Raise(me, weapon.m_shared.m_skillType, 2f);
                budget -= 180f;
                string name = Localization.instance.Localize(go.GetComponent<Character>()?.m_name ?? go.name).ToLowerInvariant();
                hunted[name] = (hunted.TryGetValue(name, out int had) ? had : 0) + 1;
            }
        }

        /// <summary>
        /// Food on it for a while (ten): from its own chests first; with nothing at all to eat, a little from yours at home (when it may: Orders
        /// tab). Returns what it took from yours ("3 cooked meat"), or null.
        /// </summary>
        private static string Provision(Humanoid me, Vector3 center, float radius)
        {
            Inventory mine = me.GetInventory();
            int have = mine.GetAllItems().Where(Food.IsFood).Sum(i => i.m_stack);
            foreach (Container chest in Home.Chests(me).Where(c => !c.IsInUse()))
            {
                if (have >= 10) break;
                foreach (ItemDrop.ItemData food in chest.GetInventory().GetAllItems().Where(Food.IsFood).OrderByDescending(i => i.m_shared.m_food + i.m_shared.m_foodStamina).ToList())
                {
                    if (have >= 10) break;
                    int n = Mathf.Min(10 - have, food.m_stack);
                    if (Work.Move(chest, me, food, n)) have += n;
                }
            }
            if (have > 0 || Food.Meals(me).Count > 0 || !Work.UsesPantry(me)) return null;
            Container yours = Work.YourFood(me, center, radius);
            return yours != null ? Work.TakeFoodFrom(me, yours, 5) : null;
        }

        /// <summary>The time and wear of working one tree or rock with this tool (as a player swings it); false when the tool would break.</summary>
        private static bool Chop(Humanoid me, ItemDrop.ItemData tool, float health, ref float budget)
        {
            float perHit = Mathf.Max(1f, (tool.m_shared.m_damages.m_chop + tool.m_shared.m_damages.m_pickaxe) * Mathf.Lerp(0.4f, 1f, Skill.Get(me, tool.m_shared.m_skillType) / 100f));
            int hits = Mathf.CeilToInt(health / perHit);
            float wear = hits * tool.m_shared.m_useDurabilityDrain;
            if (tool.m_shared.m_useDurability && tool.m_durability - wear <= 0f) return false;
            if (tool.m_shared.m_useDurability) tool.m_durability -= wear;
            budget -= hits * 1.3f + 20f; // swings, plus walking and picking up
            for (int i = 0; i < hits; i++) Skill.Raise(me, tool.m_shared.m_skillType, 1f);
            return true;
        }

        private static void Add(Dictionary<string, int> got, GameObject prefab, int amount)
        {
            if (prefab == null || amount <= 0 || prefab.GetComponent<ItemDrop>() == null) return;
            string key = prefab.name;
            got[key] = (got.TryGetValue(key, out int had) ? had : 0) + amount;
        }

        private static void AddTable(Dictionary<string, int> got, DropTable table)
        {
            if (table == null) return;
            foreach (GameObject go in table.GetDropList()) Add(got, go, 1);
        }

        private static void AddSubLogs(Dictionary<string, int> got, GameObject sub, int depth)
        {
            if (sub == null || depth <= 0) return;
            TreeLog log = sub.GetComponent<TreeLog>();
            if (log == null) return;
            AddTable(got, log.m_dropWhenDestroyed);
            AddSubLogs(got, log.m_subLogPrefab, depth - 1);
        }

        private static void Remove(GameObject go)
        {
            ZNetView v = go.GetComponent<ZNetView>();
            if (v == null || !v.IsValid()) return;
            if (!v.IsOwner()) v.ClaimOwnership();
            ZNetScene.instance.Destroy(go);
        }

        private static int Free(Humanoid me) => me.GetInventory().GetEmptySlots() + Home.Chests(me).Sum(c => c.GetInventory().GetEmptySlots())
                                                + (Work.Stows(me) ? Work.YourChests(me, Work.Center(me), Work.RadiusOf(me) + 20f).Sum(c => c.GetInventory().GetEmptySlots()) : 0);

        /// <summary>Into its chests first (stacking onto what is there), then its bag. Returns how many went into chests. Translated names in the report.</summary>
        private static int Store(Humanoid me, Dictionary<string, int> got)
        {
            int stored = 0;
            var named = new Dictionary<string, int>();
            foreach (var kv in got)
            {
                GameObject prefab = ObjectDB.instance.GetItemPrefab(kv.Key);
                ItemDrop drop = prefab != null ? prefab.GetComponent<ItemDrop>() : null;
                if (drop == null) continue;
                int left = kv.Value;
                List<Container> own = Home.Chests(me).Where(c => !c.IsInUse()).ToList();
                List<Container> yours = Work.Stows(me) ? Work.YourChests(me, Work.Center(me), Work.RadiusOf(me) + 20f).ToList() : new List<Container>();
                // Its duties work for you: what it brings goes into your chests first (its own keep a little food for itself); without duties,
                // into its own first, then yours (only put in).
                bool forYou = Duties.Configured(me) && Duties.ToYours(me) && yours.Count > 0;
                IEnumerable<Container> chests = forYou ? yours.Concat(own) : own.Concat(yours);
                if (forYou && Food.IsFood(drop.m_itemData))
                {
                    int larder = own.Sum(c => c.GetInventory().GetAllItems().Where(Food.IsFood).Sum(i => i.m_stack));
                    int toLarder = Mathf.Clamp(12 - larder, 0, left);
                    foreach (Container chest in own)
                    {
                        if (toLarder <= 0) break;
                        ZNetView lv = chest.GetComponent<ZNetView>();
                        if (lv != null && !lv.IsOwner()) lv.ClaimOwnership();
                        int had = chest.GetInventory().CountItems(drop.m_itemData.m_shared.m_name);
                        chest.GetInventory().AddItem(prefab, toLarder);
                        int put = chest.GetInventory().CountItems(drop.m_itemData.m_shared.m_name) - had;
                        toLarder -= put; left -= put; stored += put;
                    }
                }
                foreach (Container chest in chests)
                {
                    if (left <= 0) break;
                    ZNetView v = chest.GetComponent<ZNetView>();
                    if (v != null && !v.IsOwner()) v.ClaimOwnership();
                    int before = chest.GetInventory().CountItems(drop.m_itemData.m_shared.m_name);
                    chest.GetInventory().AddItem(prefab, left);
                    int added = chest.GetInventory().CountItems(drop.m_itemData.m_shared.m_name) - before;
                    left -= added; stored += added;
                }
                if (left > 0) me.GetInventory().AddItem(prefab, left);
                named[Localization.instance.Localize(drop.m_itemData.m_shared.m_name)] = kv.Value;
            }
            got.Clear();
            foreach (var kv in named) got[kv.Key] = kv.Value;
            return stored;
        }
    }

    public enum AwayMode { Off, Mild, Real }
}
