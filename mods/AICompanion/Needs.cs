using System.Linq;
using UnityEngine;
using static AICompanion.Words;

namespace AICompanion
{
    /// <summary>
    /// Looking after itself, as a player does. Out on an adventure with you it watches its bag, its gear and its food: when one runs out
    /// (a full bag, something about to break, nothing left to eat) it tells you, and when its home is near (its bed within 200 m) it goes
    /// home by itself to sort it out (store, repair, eat); hold the menu key to call it back. When its player has left the world for a
    /// minute it goes home too, rather than stand waiting. At home the rest is Work's: it fetches food from its chests, cooks, forages and
    /// hunts, repairs at its workbench, and stores what it carries.
    /// </summary>
    internal static class Needs
    {
        public static void Tick(BrainState st, Player master)
        {
            if (Time.time < st.NextNeedLook) return;
            st.NextNeedLook = Time.time + 5f;
            Humanoid me = st.Body;
            ZDO z = Companion.Zdo(me);
            if (z == null) return;
            Order order = Companion.OrderOf(me);
            bool bed = z.GetBool(Keys.HasBed, false);

            // Its player left: home, after a minute.
            if (order != Order.Gather && master == null)
            {
                if (st.MasterGoneSince == 0f) st.MasterGoneSince = Time.time;
                else if (Time.time - st.MasterGoneSince > 60f && bed)
                {
                    st.MasterGoneSince = 0f;
                    if (Home.GoHome(me, true, false)) { st.Remember(P("aicompanion_needs_player_left")); Plugin.Instance?.Note($"{Companion.NameOf(me)} went home: its player left"); }
                }
                return;
            }
            st.MasterGoneSince = 0f;
            if (order != Order.Follow) return;
            if ((Time.time < st.CommandUntil || st.Area != null) && (st.Task != null || st.PickQueue.Count > 0 || st.Area != null) && me.GetHealthPercentage() > 0.4f) return; // what you pointed it at first (the whole patch), then home; badly hurt: home now

            Inventory inv = me.GetInventory();
            string topic, item = null;   // (each need its own sentences: "aicompanion_needs_<topic>_home", "_ask", "_went")
            ItemDrop.ItemData breaking = inv.GetAllItems().FirstOrDefault(i => (me.IsItemEquiped(i) || Work.IsTool(i)) && i.m_shared.m_useDurability && i.m_shared.m_canBeReparied
                                                                              && i.GetMaxDurability() > 0f && i.m_durability < i.GetMaxDurability() * 0.1f);
            if (inv.GetEmptySlots() == 0 || Carry.Weight(me) > Carry.Max(me) * 0.95f) topic = "bag";
            else if (breaking != null) { topic = "worn"; item = Low(breaking.m_shared.m_name); }
            else if (!inv.GetAllItems().Any(Food.IsFood) && Food.Meals(me).Count == 0) topic = "food";
            else return;

            bool homeNear = Work.HasHome(me) && Vector3.Distance(Work.Center(me), me.transform.position) < 200f;
            if (homeNear)
            {
                if (!Home.GoHome(me, true, false)) return;
                string key = Plugin.MenuKey.Value.ToString();
                Talk.Tell(me, item != null ? P("aicompanion_needs_" + topic + "_home", item, key) : P("aicompanion_needs_" + topic + "_home", key), "need:" + topic, 3f);
                st.Remember(item != null ? P("aicompanion_needs_" + topic + "_went", item) : P("aicompanion_needs_" + topic + "_went"));
            }
            else Talk.Tell(me, item != null ? P("aicompanion_needs_" + topic + "_ask", item) : P("aicompanion_needs_" + topic + "_ask"), "need:" + topic, 5f);
        }
    }
}
