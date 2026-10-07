using System.Linq;
using UnityEngine;

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
                    if (Home.GoHome(me, true, false)) { st.Remember("its player left, so it went home"); Plugin.Instance?.Note($"{Companion.NameOf(me)} went home: its player left"); }
                }
                return;
            }
            st.MasterGoneSince = 0f;
            if (order != Order.Follow) return;

            Inventory inv = me.GetInventory();
            string need, topic, ask;
            ItemDrop.ItemData breaking = inv.GetAllItems().FirstOrDefault(i => (me.IsItemEquiped(i) || Work.IsTool(i)) && i.m_shared.m_useDurability && i.m_shared.m_canBeReparied
                                                                              && i.GetMaxDurability() > 0f && i.m_durability < i.GetMaxDurability() * 0.1f);
            if (inv.GetEmptySlots() == 0 || Carry.Weight(me) > Carry.Max(me) * 0.95f) { need = "My bag is full"; topic = "bag"; ask = "Take something from it, or send me home."; }
            else if (breaking != null) { need = $"My {Localization.instance.Localize(breaking.m_shared.m_name).ToLowerInvariant()} is about to break"; topic = "worn"; ask = "A workbench would fix it."; }
            else if (!inv.GetAllItems().Any(Food.IsFood) && Food.Meals(me).Count == 0) { need = "I'm out of food"; topic = "food"; ask = "Give me something to eat (my Gear tab)."; }
            else return;

            bool homeNear = bed && Vector3.Distance(z.GetVec3(Keys.BedPos, me.transform.position), me.transform.position) < 200f;
            if (homeNear)
            {
                if (!Home.GoHome(me, true, false)) return;
                Talk.Tell(me, $"{need}, so I'm going home to sort it out. Hold {Plugin.MenuKey.Value} to call me back.", "need:" + topic, 3f);
                st.Remember($"went home by itself ({need.ToLowerInvariant()})");
            }
            else Talk.Tell(me, $"{need}. {ask}", "need:" + topic, 5f);
        }
    }
}
