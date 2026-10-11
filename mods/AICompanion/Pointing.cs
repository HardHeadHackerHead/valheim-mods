using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static AICompanion.Words;

namespace AICompanion
{
    /// <summary>
    /// Point at something and press the command key (H): your companion does the obvious thing with it (the idea from Offline Companions).
    ///   an enemy -> every companion of yours near you goes for it;     a tree, log, rock or ore -> it works it;   a plant -> it picks it;
    ///   one of your chests -> it puts what it carries in there;        its tombstone -> it goes for its things;   a free bed -> its bed now;
    ///   a cart -> it pulls it (again: it lets go);                      the ground -> it goes there and waits;    the sky (nothing) -> back to you.
    /// The companion nearest to the spot does it (all of them for an enemy). Up to 150 m, from where you look.
    /// </summary>
    internal static class Pointing
    {
        private static readonly int Mask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "piece_nonsolid", "terrain", "vehicle",
                                                             "character", "character_net", "character_ghost", "character_noenv", "hitbox", "item");

        /// <summary>How far you can point (about as far as the world around you is loaded).</summary>
        public const float Reach = 150f;

        public static void Command(Player p)
        {
            // Yours, however far (any the game has loaded: one far off in a part of the world nobody is near is not running anywhere).
            var mine = Companion.All().Where(c => Companion.CanCommand(c, p) && !c.IsDead()).ToList();
            if (mine.Count == 0) { Plugin.Tell(P("aicompanion_pointing_none_near")); return; }
            Transform eye = GameCamera.instance != null ? GameCamera.instance.transform : p.transform;
            RaycastHit[] hits = Physics.RaycastAll(eye.position, eye.forward, Reach, Mask, QueryTriggerInteraction.Ignore).OrderBy(h => h.distance).ToArray();
            foreach (RaycastHit hit in hits)
            {
                Character ch = hit.collider.GetComponentInParent<Character>();
                if (ch == p || (ch != null && Companion.Is(ch))) continue; // you, or one of them: look past it
                if (Handle(p, mine, hit, ch)) return;
                return;
            }
            // Nothing: back to you.
            foreach (Humanoid c in mine) { Brain.Get(c).Area = null; if (Home.Follow(c)) Talk.Say(c, P("aicompanion_talk_reply_come")); }
            Plugin.Tell(mine.Count == 1 ? P("aicompanion_pointing_comes_back", Companion.NameOf(mine[0])) : P("aicompanion_pointing_all_come_back"));
        }

        /// <summary>The ground, or a floor you built: somewhere to go and wait.</summary>
        private static bool IsGround(RaycastHit hit) =>
            hit.collider.GetComponentInParent<Heightmap>() != null || hit.collider.GetComponentInParent<Piece>() != null || LayerMask.LayerToName(hit.collider.gameObject.layer) == "terrain";

        /// <summary>The job it was just given, marked on its target for as long as it is on it.</summary>
        private static void MarkTask(BrainState st, Humanoid who, string doing)
        {
            Work.Task t = st.Task;
            if (t?.Target == null) return;
            Marks.Put(t.Target, who, doing, () => st.Task == t && t.Target != null);
        }

        /// <summary>The workable part of the world the ray hit (a tree, log, rock, ore vein, plant), however deep it sits in its object.</summary>
        private static GameObject WorkObject(Collider col)
        {
            Component c = col.GetComponentInParent<TreeLog>() as Component ?? col.GetComponentInParent<TreeBase>() as Component
                          ?? col.GetComponentInParent<MineRock5>() as Component ?? col.GetComponentInParent<MineRock>() as Component
                          ?? col.GetComponentInParent<Pickable>() as Component ?? col.GetComponentInParent<Destructible>() as Component;
            return c != null ? c.gameObject : null;
        }

        /// <summary>(Packed text, Words.P.) Why it cannot work that (no axe, too weak a pickaxe, a crop...), or null when it is nothing it works.</summary>
        private static string WhyNot(Humanoid who, GameObject go)
        {
            if (go == null || go.GetComponent<Piece>() != null) return null;
            ItemDrop.ItemData axe = Work.Axe(who), pick = Work.Pickaxe(who);
            int treeTier = go.GetComponent<TreeBase>()?.m_minToolTier ?? go.GetComponent<TreeLog>()?.m_minToolTier ?? -1;
            int rockTier = go.GetComponent<MineRock5>()?.m_minToolTier ?? go.GetComponent<MineRock>()?.m_minToolTier ?? -1;
            Destructible des = go.GetComponent<Destructible>();
            if (des != null)
            {
                DropTable drops = go.GetComponent<DropOnDestroyed>()?.m_dropWhenDestroyed;
                if (drops == null || drops.m_drops.Count == 0) return P("aicompanion_pointing_nothing_worth");
                bool wood = des.m_destructibleType == DestructibleType.Tree || des.m_damages.m_chop != HitData.DamageModifier.Immune && des.m_damages.m_pickaxe == HitData.DamageModifier.Immune;
                if (wood) treeTier = des.m_minToolTier; else rockTier = des.m_minToolTier;
            }
            if (treeTier >= 0) return axe == null ? P("aicompanion_pointing_need_axe") : P("aicompanion_pointing_axe_too_weak");
            if (rockTier >= 0) return pick == null ? P("aicompanion_pointing_need_pickaxe") : P("aicompanion_pointing_pickaxe_too_weak");
            Pickable p = go.GetComponent<Pickable>();
            if (p != null)
            {
                if (Companion.Zdo(p)?.GetBool(ZDOVars.s_picked, false) ?? false) return P("aicompanion_pointing_nothing_to_pick");
                return P("aicompanion_pointing_crop");
            }
            return null;
        }

        private static Humanoid Nearest(System.Collections.Generic.List<Humanoid> mine, Vector3 at) => mine.OrderBy(c => Vector3.Distance(c.transform.position, at)).First();

        private static bool Handle(Player p, System.Collections.Generic.List<Humanoid> mine, RaycastHit hit, Character ch)
        {
            GameObject go = hit.collider.attachedRigidbody != null ? hit.collider.attachedRigidbody.gameObject : hit.collider.transform.root.gameObject;

            // An enemy: all of them, at it.
            if (ch != null && !ch.IsDead() && !ch.IsPlayer() && BaseAI.IsEnemy(mine[0], ch))
            {
                foreach (Humanoid c in mine)
                {
                    BrainState st = Brain.Get(c);
                    st.Focus = ch;
                    st.FocusUntil = Time.time + 30f;
                    st.NextAsk = 0f;
                    Talk.Say(c, P("aicompanion_pointing_on_it"));
                }
                BrainState first = Brain.Get(mine[0]);
                Marks.Put(ch, mine[0], P(mine.Count == 1 ? "aicompanion_pointing_mark_going_for" : "aicompanion_pointing_mark_going_for_all"), () => first.Focus == ch && Time.time < first.FocusUntil && !ch.IsDead());
                Plugin.Tell(mine.Count == 1 ? P("aicompanion_pointing_go_for", Companion.NameOf(mine[0]), ch.m_name) : P("aicompanion_pointing_all_go_for", ch.m_name));
                return true;
            }

            // Its tombstone: its things.
            TombStone tomb = go.GetComponentInParent<TombStone>();
            long tombOf = tomb != null ? Companion.Zdo(tomb)?.GetLong(Grave.OfKey, 0L) ?? 0L : 0L;
            Humanoid owner = tombOf != 0L ? mine.FirstOrDefault(c => Companion.IdOf(c) == tombOf) : null;
            if (owner != null)
            {
                BrainState os = Brain.Get(owner);
                Grave.Fetch(os, tomb);
                Talk.Say(owner, P("aicompanion_pointing_my_things"));
                Marks.Put(tomb, owner, P("aicompanion_pointing_mark_getting_things"), () => os.GraveOrdered == tomb || os.GraveOn == tomb);
                return true;
            }

            Humanoid who = Nearest(mine, hit.point);
            BrainState w = Brain.Get(who);
            w.Area = null; // (a new order: the patch it was working is left)

            // Things lying on the ground (the one you pointed at, or right beside where you pointed): it picks up all of them there.
            ItemDrop item = hit.collider.GetComponentInParent<ItemDrop>();
            Vector3 heap = item != null ? item.transform.position : hit.point;
            if ((item != null || IsGround(hit)) && Work.OrderPickUp(w, heap, item != null ? 3f : 1.5f))
            {
                Talk.Say(who, P("aicompanion_pointing_pick_those_up"));
                Marks.PutSpot(heap, who, P("aicompanion_pointing_mark_picking_up"), () => w.PickQueue.Count > 0 || (w.Task != null && w.Task.Kind == Work.Kind.PickUp && w.Task.Ordered));
                return true;
            }
            Activity.Log(who, $"you pointed at {Utils.GetPrefabName(go)} ({hit.collider.name}, layer {LayerMask.LayerToName(hit.collider.gameObject.layer)}, {hit.distance:0} m)");

            // A free bed: its bed now.
            Bed bed = go.GetComponentInParent<Bed>();
            if (bed != null) { Home.ToggleBed(bed, who); return true; }

            // A cart: it pulls it (again: it lets go).
            Vagon cart = go.GetComponentInParent<Vagon>();
            if (cart != null) { Carts.Toggle(w, cart); if (w.Cart == cart) Marks.Put(cart, who, P("aicompanion_pointing_mark_pulling"), () => w.Cart == cart); return true; }

            // One of your chests: put its things there.
            Container chest = go.GetComponentInParent<Container>();
            if (chest != null && Home.IsChest(chest))
            {
                bool sorts = AppDomain.CurrentDomain.GetData("DHack.QoL.StackInventory") != null; // (QualityOfLife: into the right chests around it)
                if (Work.Ordered(w, Work.Kind.Store, chest))
                {
                    Talk.Say(who, P(sorts ? "aicompanion_pointing_will_sort" : "aicompanion_pointing_will_put"));
                    MarkTask(w, who, P(sorts ? "aicompanion_pointing_mark_sorting" : "aicompanion_pointing_mark_putting"));
                    return true;
                }
            }

            // Something to work: a tree, log, rock, ore, a plant (the part of the world you hit, not what it sits in: a rock in a ruin).
            GameObject thing = WorkObject(hit.collider) ?? go;
            Work.Task work = Work.Workable(w, thing);
            if (work != null && Work.Ordered(w, work))
            {
                // Its job now: this and the like of it around here (the trees about, the rocks, the bushes), each shown.
                List<Component> patch = Work.OrderArea(w, work);
                Work.Area area = w.Area;
                foreach (Component c in patch) { Component cc = c; Marks.Put(cc, who, null, () => w.Area == area && cc != null); }
                string doing = P(work.Kind == Work.Kind.Pick ? "aicompanion_pointing_mark_picking" : work.Job == Job.Wood ? "aicompanion_pointing_mark_chopping" : "aicompanion_pointing_mark_mining");
                int more = patch.Count(c => c != work.Target);
                Talk.Say(who, P(more == 0 ? (work.Kind == Work.Kind.Pick ? "aicompanion_pointing_will_pick" : "aicompanion_pointing_on_it_calm")
                    : work.Kind == Work.Kind.Pick ? "aicompanion_pointing_will_pick_all" : work.Job == Job.Wood ? "aicompanion_pointing_will_chop" : "aicompanion_pointing_will_mine"));
                MarkTask(w, who, doing);
                return true;
            }
            // Something it would work if it could: it says why, rather than walking off to wait there.
            string why = WhyNot(who, thing);
            if (why != null)
            {
                Talk.Say(who, why);
                Plugin.Tell($"{Companion.NameOf(who)}: {why}");
                return true;
            }

            // Something that is neither the ground nor a floor you built (a boulder that cannot be mined, scenery): nothing to do with it.
            if (!IsGround(hit))
            {
                string name = Utils.GetPrefabName(go).ToLowerInvariant();
                string what = P(name.Contains("rock") || name.Contains("stone") || name.Contains("boulder") ? "aicompanion_pointing_rock_cant" : "aicompanion_pointing_cant_do");
                Talk.Say(who, what);
                Plugin.Tell($"{Companion.NameOf(who)}: {what} " + P("aicompanion_pointing_ground_hint"));
                return true;
            }

            // The ground: go there and wait.
            Vector3 spot = hit.point;
            if (Companion.Write(who, z => { z.Set(Keys.Order, (int)Order.Guard); z.Set(Keys.Post, spot); }))
            {
                w.ManualOrderAt = Time.time;
                Marks.PutSpot(spot, who, P("aicompanion_pointing_mark_waiting"), () => who != null && Companion.OrderOf(who) == Order.Guard && Vector3.Distance(Companion.Zdo(who).GetVec3(Keys.Post, Vector3.zero), spot) < 0.5f);
                Talk.Say(who, P("aicompanion_pointing_will_wait"));
                Plugin.Tell(P("aicompanion_pointing_waits_there", Companion.NameOf(who), Plugin.CommandKey.Value));
            }
            return true;
        }
    }
}
