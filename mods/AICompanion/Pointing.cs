using System.Linq;
using UnityEngine;

namespace AICompanion
{
    /// <summary>
    /// Point at something and press the command key (H): your companion does the obvious thing with it (the idea from Offline Companions).
    ///   an enemy -> every companion of yours near you goes for it;     a tree, log, rock or ore -> it works it;   a plant -> it picks it;
    ///   one of your chests -> it puts what it carries in there;        its tombstone -> it goes for its things;   a free bed -> its bed now;
    ///   a cart -> it pulls it (again: it lets go);                      the ground -> it goes there and waits;    the sky (nothing) -> back to you.
    /// The companion nearest to the spot does it (all of them for an enemy). Up to 50 m, from where you look.
    /// </summary>
    internal static class Pointing
    {
        private static readonly int Mask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "piece_nonsolid", "terrain", "vehicle",
                                                             "character", "character_net", "character_ghost", "character_noenv", "hitbox", "item");

        public static void Command(Player p)
        {
            var mine = Companion.All().Where(c => Companion.CanCommand(c, p) && !c.IsDead() && Vector3.Distance(c.transform.position, p.transform.position) < 60f).ToList();
            if (mine.Count == 0) { Plugin.Tell("No companion of yours near you."); return; }
            Transform eye = GameCamera.instance != null ? GameCamera.instance.transform : p.transform;
            RaycastHit[] hits = Physics.RaycastAll(eye.position, eye.forward, 50f, Mask, QueryTriggerInteraction.Ignore).OrderBy(h => h.distance).ToArray();
            foreach (RaycastHit hit in hits)
            {
                Character ch = hit.collider.GetComponentInParent<Character>();
                if (ch == p || (ch != null && Companion.Is(ch))) continue; // you, or one of them: look past it
                if (Handle(p, mine, hit, ch)) return;
                return;
            }
            // Nothing: back to you.
            foreach (Humanoid c in mine) if (Home.Follow(c)) Talk.Say(c, "Coming!");
            Plugin.Tell(mine.Count == 1 ? $"{Companion.NameOf(mine[0])} comes back to you" : "Your companions come back to you");
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
                    Talk.Say(c, "On it!");
                }
                Plugin.Tell($"{(mine.Count == 1 ? Companion.NameOf(mine[0]) : "Your companions")} go for the {Localization.instance.Localize(ch.m_name)}");
                return true;
            }

            // Its tombstone: its things.
            TombStone tomb = go.GetComponentInParent<TombStone>();
            long tombOf = tomb != null ? Companion.Zdo(tomb)?.GetLong(Grave.OfKey, 0L) ?? 0L : 0L;
            Humanoid owner = tombOf != 0L ? mine.FirstOrDefault(c => Companion.IdOf(c) == tombOf) : null;
            if (owner != null) { Grave.Fetch(Brain.Get(owner), tomb); Talk.Say(owner, "My things! I'll get them."); return true; }

            Humanoid who = Nearest(mine, hit.point);
            BrainState w = Brain.Get(who);

            // A free bed: its bed now.
            Bed bed = go.GetComponentInParent<Bed>();
            if (bed != null) { Home.ToggleBed(bed, who); return true; }

            // A cart: it pulls it (again: it lets go).
            Vagon cart = go.GetComponentInParent<Vagon>();
            if (cart != null) { Carts.Toggle(w, cart); return true; }

            // One of your chests: put its things there.
            Container chest = go.GetComponentInParent<Container>();
            if (chest != null && Home.IsChest(chest))
            {
                if (Work.Ordered(w, Work.Kind.Store, chest)) { Talk.Say(who, "I'll put my things in there."); return true; }
            }

            // Something to work: a tree, log, rock, ore, a plant.
            Work.Task work = Work.Workable(w, go);
            if (work != null) { Work.Ordered(w, work); Talk.Say(who, work.Kind == Work.Kind.Pick ? "I'll pick that." : "On it."); return true; }

            // The ground (or anything else): go there and wait.
            Vector3 spot = hit.point;
            if (Companion.Write(who, z => { z.Set(Keys.Order, (int)Order.Guard); z.Set(Keys.Post, spot); }))
            {
                w.ManualOrderAt = Time.time;
                Talk.Say(who, "I'll wait there.");
                Plugin.Tell($"{Companion.NameOf(who)} waits there. Press {Plugin.CommandKey.Value} at the sky to call them back.");
            }
            return true;
        }
    }
}
