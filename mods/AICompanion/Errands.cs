using System.Linq;
using UnityEngine;

namespace AICompanion
{
    internal enum Errand { PutAway, Restock, BetterGear, RepairGear, RepairBase, Fires, Cook }

    /// <summary>
    /// Errands you send it on from its menu (Orders tab), one click each: the things it does by itself at home now and then, done now. It
    /// goes, does it, and goes back to what it was doing (following you, its patch, its life at home). Near its home only.
    /// </summary>
    internal static class Errands
    {
        public static readonly (Errand What, string Label, string Help)[] All =
        {
            (Errand.PutAway, "Put things away", "What it carries but does not keep goes into the chests: yours by your chest rules (QualityOfLife), its stock into its own."),
            (Errand.Restock, "Restock food", "Fills its three food slots from its chests, else yours."),
            (Errand.BetterGear, "Check for better gear", "Looks through your chests for a better weapon, armour, shield, bow, arrows or tool, wears it and puts its old one back."),
            (Errand.RepairGear, "Repair its gear", "Takes its worn gear to the workbench or forge that can fix it."),
            (Errand.RepairBase, "Repair the base", "Fixes damaged walls, floors and the rest with its hammer (makes one if it has none)."),
            (Errand.Fires, "Feed the fires", "Tops up the fires that are running low with wood."),
            (Errand.Cook, "Cook", "Puts the raw food it carries on a cooking station near home."),
        };

        /// <summary>Send it. What happened, for the menu: what it is off to do, or why it cannot.</summary>
        public static string Run(BrainState st, Errand what)
        {
            Humanoid me = st?.Body;
            if (me == null) return "It is not here.";
            if (!Work.HasHome(me)) return "It has no home yet: give it a bed (Home tab) or set its home.";
            Vector3 center = Work.Center(me);
            float radius = Work.RadiusOf(me);
            if (Vector3.Distance(center, me.transform.position) > radius + 40f) return "It is too far from home for that.";
            string said = Do(st, what, center, radius, out bool going);
            if (said != null) Talk.Say(me, said);
            Activity.Log(me, $"errand ({what}): {said ?? "on its way"}");
            return going ? $"{Companion.NameOf(me)}: {said ?? "on my way."}" : said;
        }

        private static bool Reach(Humanoid me, Component c) => c != null && Brain.CanReach(me, c.transform.position);

        private static bool Go(BrainState st, Work.Kind kind, Component target, Job job = Job.None) =>
            target != null && Work.Ordered(st, Work.New(kind, target, job));

        /// <summary>
        /// You called it out from home with less than twelve food on it: it fetches some first, from its own chests (or from yours, within its
        /// ration, when it may take from them), then comes after you. Null when it has enough, is not at home, or finds none.
        /// </summary>
        public static string ForTheRoad(BrainState st)
        {
            Humanoid me = st?.Body;
            if (me == null || !Work.HasHome(me)) return null;
            Vector3 center = Work.Center(me);
            float radius = Work.RadiusOf(me);
            if (Vector3.Distance(center, me.transform.position) > radius) return null;
            if (me.GetInventory().GetAllItems().Where(Food.IsFood).Sum(i => i.m_stack) >= 12) return null;
            Container mine = Home.Chests(me).Where(c => !Containers.InUse(c) && c.GetInventory().GetAllItems().Any(i => Gear.WantsFood(me, i)))
                                 .OrderBy(c => Vector3.Distance(c.transform.position, me.transform.position)).FirstOrDefault(c => Reach(me, c));
            if (mine != null && Go(st, Work.Kind.Store, mine)) { Talk.Say(me, "I'll grab some food for the road."); return "food from its chest"; }
            if (Work.UsesPantry(me) && Work.RationLeft(me) > 0)
            {
                Container yours = Work.YourFood(me, center, radius + 20f);
                if (yours != null && Go(st, Work.Kind.Fetch, yours)) { Talk.Say(me, "I'll grab some food for the road."); return "food from your chest"; }
            }
            return null;
        }

        private static string Do(BrainState st, Errand what, Vector3 center, float radius, out bool going)
        {
            Humanoid me = st.Body;
            going = false;
            switch (what)
            {
                case Errand.PutAway:
                {
                    int sorted = Work.SortHome(st); // (QualityOfLife: into your chests by your rules, at once)
                    bool rest = me.GetInventory().GetAllItems().Any(i => !Work.Keeps(me, i));
                    if (!rest) return sorted > 0 ? $"Put {sorted} things away in your chests." : "I've nothing to put away.";
                    Container chest = Home.Chests(me).Where(c => !Containers.InUse(c) && c.GetInventory().HaveEmptySlot()).OrderBy(c => Vector3.Distance(c.transform.position, me.transform.position)).FirstOrDefault(c => Reach(me, c))
                                      ?? (Work.Stows(me) ? Work.YourChests(me, center, radius + 20f).Where(c => c.GetInventory().HaveEmptySlot()).Take(6).FirstOrDefault(c => Reach(me, c)) : null);
                    if (chest == null) return "There's no chest with room I can get to.";
                    going = Go(st, Work.Kind.Store, chest);
                    return going ? "Putting my things away." : "Someone has my things open.";
                }
                case Errand.Restock:
                {
                    if (!Gear.FoodLow(me)) return "My food slots are full.";
                    Container mine = Home.Chests(me).Where(c => !Containers.InUse(c) && c.GetInventory().GetAllItems().Any(i => Gear.WantsFood(me, i)))
                                         .OrderBy(c => Vector3.Distance(c.transform.position, me.transform.position)).FirstOrDefault(c => Reach(me, c));
                    if (mine != null) { going = Go(st, Work.Kind.Store, mine); return "Getting food from my chest."; }
                    Container yours = Work.YourFood(me, center, radius + 20f); // (you sent it: from your chests even with that setting off)
                    if (yours != null) { going = Go(st, Work.Kind.Fetch, yours); return "Getting some food from your chest."; }
                    return "There's no food in any chest I can get to.";
                }
                case Errand.BetterGear:
                {
                    Container chest = Armory.Find(me, center, radius + 20f, force: true);
                    if (chest == null) return "Nothing in your chests beats what I have.";
                    going = Go(st, Work.Kind.Armory, chest);
                    return "Going to look at the gear in your chest.";
                }
                case Errand.RepairGear:
                    return Repair.Order(st, out going);
                case Errand.RepairBase:
                {
                    WearNTear damaged = Mending.Damaged(me, center, radius, c => Brain.CanReach(me, c.transform.position, 4f));
                    if (damaged == null) return "Nothing in the base needs fixing.";
                    if (Mending.Hammer(me) == null) Mending.MakeHammer(st);
                    if (Mending.Hammer(me) == null) return "I need a hammer for that.";
                    going = Go(st, Work.Kind.Mend, damaged);
                    return "Going round the base with my hammer.";
                }
                case Errand.Fires:
                {
                    Fireplace fire = Fires.Low(me, center, radius);
                    if (fire == null) return "The fires are all well fed.";
                    going = Go(st, Work.Kind.Fuel, fire);
                    return "Putting wood on the fire.";
                }
                case Errand.Cook:
                {
                    if (!me.GetInventory().GetAllItems().Any(Work.IsCookable)) return "I've nothing raw to cook.";
                    CookingStation stove = Kitchen.Find(me, center, radius + 20f);
                    if (stove == null) return "There's no cooking station by a fire I can use.";
                    going = Go(st, Work.Kind.Cook, stove, Job.Cook);
                    return "Off to cook.";
                }
            }
            return null;
        }
    }
}
