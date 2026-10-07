using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace AICompanion
{
    /// <summary>
    /// Sleeping, as a player sleeps: at night, living at home with a bed, it goes to its bed and lies down in it (the game's own sleeping
    /// pose); it gets up at dawn, or when something comes for it or its home. It never holds up your sleep (companions are not players).
    /// </summary>
    internal static class Sleep
    {
        private static readonly AccessTools.FieldRef<Character, ZSyncAnimation> Anim = AccessTools.FieldRefAccess<Character, ZSyncAnimation>("m_zanim");

        /// <summary>True while it is going to bed or asleep (the rest of its peaceful behaviour waits).</summary>
        public static bool Tick(BrainState st, Action<Vector3, float, bool> moveTo, Action stop)
        {
            Humanoid me = st.Body;
            if (!st.Asleep) OffBed(st);
            bool night = EnvMan.instance != null && EnvMan.IsNight();
            if (!night || Companion.OrderOf(me) != Order.Gather || !Companion.Zdo(me).GetBool(Keys.HasBed, false) || Food.Meals(me).Count == 0 && !me.GetInventory().GetAllItems().Any(Food.IsFood) && me.GetHealthPercentage() < 0.5f)
            {
                if (st.Asleep) Wake(st, "it is morning");
                return false;
            }
            Bed bed = st.SleepBed != null ? st.SleepBed : st.SleepBed = Home.BedOf(me);
            if (bed == null) return false;
            if (st.Asleep) { Hold(st, bed); return true; }
            Vector3 at = bed.GetSpawnPoint();
            // Close enough to get in, as a player who can reach the bed does: beside it (a bed up on chests is reached from the floor
            // below or the stack above it), or near it after trying for a while (it climbs in). Height counts less than distance across.
            Vector3 flat = at - me.transform.position;
            float dy = Mathf.Abs(flat.y);
            flat.y = 0f;
            if (st.BedSince <= 0f || Time.time - st.BedSince > 600f) st.BedSince = Time.time;
            bool reach = flat.magnitude <= 1.6f && dy <= 2.6f || flat.magnitude <= 3.5f && dy <= 3f && Time.time - st.BedSince > 20f;
            if (!reach)
            {
                float d = Vector3.Distance(at, me.transform.position);
                moveTo(at, 0.6f, d > 8f);
                Brain.Status(st, "going to bed");
                return true;
            }
            stop();
            st.BedSince = 0f;
            st.BedFrom = me.transform.position; // where it climbed in from: it got there, so it can walk away from there
            st.Asleep = true;
            Anim(me)?.SetBool("attach_bed", true);
            Rigidbody body = me.GetComponent<Rigidbody>();
            if (body != null) { body.linearVelocity = Vector3.zero; body.isKinematic = true; }
            st.Remember("went to bed");
            Hold(st, bed);
            return true;
        }

        private static void Hold(BrainState st, Bed bed)
        {
            Humanoid me = st.Body;
            Transform spot = bed.m_spawnPoint != null ? bed.m_spawnPoint : bed.transform;
            me.transform.SetPositionAndRotation(spot.position, spot.rotation);
            Rigidbody body = me.GetComponent<Rigidbody>();
            if (body != null) { body.position = spot.position; body.rotation = spot.rotation; } // (held: a kinematic body has no velocity to clear)
            Anim(me)?.SetBool("attach_bed", true); // kept up: anything that resets its animator must not stand it up in bed (free when already set)
            Brain.Status(st, "sleeping");
        }

        public static void Wake(BrainState st, string why)
        {
            Humanoid me = st.Body;
            if (!st.Asleep || me == null) return;
            st.Asleep = false;
            Anim(me)?.SetBool("attach_bed", false);
            Rigidbody body = me.GetComponent<Rigidbody>();
            if (body != null) body.isKinematic = false;
            // Out of bed onto the floor beside it (a bed up on chests: down to the floor, not onto the chests), else back where it climbed
            // in from, else just in front of it.
            Vector3? floor = st.SleepBed != null ? Beside(st.SleepBed.transform, me) : null;
            if (floor != null) me.transform.position = floor.Value;
            else if (st.BedFrom != Vector3.zero && st.SleepBed != null && Vector3.Distance(st.BedFrom, st.SleepBed.GetSpawnPoint()) < 5f) me.transform.position = st.BedFrom;
            else if (st.SleepBed != null) me.transform.position = st.SleepBed.GetSpawnPoint() + st.SleepBed.transform.forward * 1f;
            st.BedFrom = Vector3.zero;
            st.Remember($"got up ({why})");
        }

        private static float _nextOffBed;

        /// <summary>
        /// Awake but standing on a bed (one up on chests: it got out there and found no way down): it hops down beside it, as you would.
        /// </summary>
        private static void OffBed(BrainState st)
        {
            if (Time.time < _nextOffBed) return;
            _nextOffBed = Time.time + 2f;
            Humanoid me = st.Body;
            if (!Physics.Raycast(me.transform.position + Vector3.up * 0.3f, Vector3.down, out RaycastHit under, 1.2f, ~0, QueryTriggerInteraction.Ignore)) return;
            Bed bed = under.collider.GetComponentInParent<Bed>();
            if (bed == null) return;
            Vector3? down = Beside(bed.transform, me);
            if (down == null) return;
            me.transform.position = down.Value;
            Rigidbody body = me.GetComponent<Rigidbody>();
            if (body != null) { body.position = down.Value; body.linearVelocity = Vector3.zero; }
            st.Remember("hopped down off the bed");
        }

        /// <summary>
        /// The nearest spot round a bed to stand on that is not the bed itself: lower than the bed's top, with room to stand. Null if none.
        /// </summary>
        private static Vector3? Beside(Transform bed, Humanoid me)
        {
            Vector3 c = bed.position;
            Vector3? best = null;
            float bestScore = float.MaxValue;
            for (int i = 0; i < 16; i++)
            {
                float ang = i * 22.5f;
                foreach (float r in new[] { 1.3f, 1.8f, 2.4f })
                {
                    Vector3 p = c + Quaternion.Euler(0f, ang, 0f) * Vector3.forward * r;
                    if (!Physics.Raycast(p + Vector3.up * 2.0f, Vector3.down, out RaycastHit hit, 5f, ~0, QueryTriggerInteraction.Ignore)) continue;
                    if (hit.collider.GetComponentInParent<Bed>() != null || hit.collider.GetComponentInParent<Character>() != null || hit.collider.GetComponentInParent<Container>() != null) continue;
                    if (hit.point.y > c.y - 0.2f || hit.normal.y < 0.7f) continue;            // below the bed (the floor, not chests or beams round it), not a slope
                    Vector3 stand = hit.point + Vector3.up * 0.05f;
                    if (Physics.CheckCapsule(stand + Vector3.up * 0.5f, stand + Vector3.up * 1.6f, 0.3f, ~LayerMask.GetMask("character", "character_net", "character_ghost", "item"), QueryTriggerInteraction.Ignore)) continue; // room to stand
                    float score = r + (c.y - hit.point.y) * 0.3f;                              // near, and not a long drop
                    if (score < bestScore) { bestScore = score; best = stand; }
                }
            }
            return best;
        }
    }

    /// <summary>
    /// Pulling a cart: point at a cart (the command key) and it hitches itself up (stands at the cart's hitch and takes hold, as the game
    /// lets any humanoid do) and pulls it along wherever it goes, following you or going home. Point at the cart again and it lets go.
    /// </summary>
    internal static class Carts
    {
        public static void Toggle(BrainState st, Vagon cart)
        {
            Humanoid me = st.Body;
            if (st.Cart == cart && cart.IsAttached(me)) { cart.Interact(me, false, false); st.Cart = null; Talk.Say(me, "Letting go of the cart."); return; }
            st.Cart = cart;
            st.CartSince = Time.time;
            Talk.Say(me, "I'll pull the cart.");
        }

        /// <summary>True while it is getting hitched up (it walks to the cart and takes hold). Hitched, the rest of its behaviour goes on.</summary>
        public static bool Tick(BrainState st, Action<Vector3, float, bool> moveTo, Action stop)
        {
            Humanoid me = st.Body;
            Vagon cart = st.Cart;
            if (cart == null) return false;
            if (cart.IsAttached(me)) { st.CartSince = Time.time; return false; }
            if (Time.time - st.CartSince > 30f) { st.Cart = null; Talk.Say(me, "I couldn't get hold of the cart."); return false; }
            Vector3 hitch = cart.m_attachPoint.position - cart.m_attachOffset;
            float d = Vector3.Distance(hitch, me.transform.position);
            if (d > 1.2f) { moveTo(hitch, 0.4f, d > 6f); Brain.Status(st, "going to the cart"); return true; }
            stop();
            me.transform.position = hitch;
            Rigidbody body = me.GetComponent<Rigidbody>();
            if (body != null) { body.position = hitch; body.linearVelocity = Vector3.zero; }
            if (Time.time - st.CartAsked > 1.5f) { st.CartAsked = Time.time; cart.Interact(me, false, false); }
            Brain.Status(st, "hitching up the cart");
            return true;
        }
    }

    /// <summary>
    /// Food it took from one of your chests is marked with that chest's spot; what it did not eat goes back into the same chest once it has
    /// food of its own again (at home), so your pantry gets back what it did not need.
    /// </summary>
    internal static class Loans
    {
        public const string Tag = "dhc_loan";

        public static void Mark(ItemDrop.ItemData item, Container from)
        {
            if (item.m_customData == null) item.m_customData = new Dictionary<string, string>();
            Vector3 p = from.transform.position;
            item.m_customData[Tag] = $"{p.x:0.0},{p.y:0.0},{p.z:0.0}";
        }

        /// <summary>At home, with food of its own (or fed): borrowed food it did not eat back to the chest it came from. How many.</summary>
        public static int Return(BrainState st)
        {
            Humanoid me = st.Body;
            if (Work.UsesPantry(me)) return 0; // allowed to take food from your chests: it keeps what it took (to fill its food slots)
            Inventory inv = me.GetInventory();
            List<ItemDrop.ItemData> borrowed = inv.GetAllItems().Where(i => i.m_customData != null && i.m_customData.ContainsKey(Tag)).ToList();
            if (borrowed.Count == 0) return 0;
            int own = inv.GetAllItems().Where(i => Food.IsFood(i) && (i.m_customData == null || !i.m_customData.ContainsKey(Tag))).Sum(i => i.m_stack);
            if (own < 5 && Food.Meals(me).Count < 2) return 0;
            int back = 0;
            foreach (ItemDrop.ItemData item in borrowed)
            {
                string[] f = item.m_customData[Tag].Split(',');
                if (f.Length != 3 || !float.TryParse(f[0], out float x) || !float.TryParse(f[1], out float y) || !float.TryParse(f[2], out float z)) continue;
                var spot = new Vector3(x, y, z);
                Container chest = UnityEngine.Object.FindObjectsByType<Container>(FindObjectsSortMode.None).FirstOrDefault(c => Vector3.Distance(c.transform.position, spot) < 1f && !c.IsInUse());
                if (chest == null || Vector3.Distance(chest.transform.position, me.transform.position) > 60f) continue;
                ZNetView v = chest.GetComponent<ZNetView>();
                if (v != null && !v.IsOwner()) v.ClaimOwnership();
                int n = item.m_stack;
                item.m_customData.Remove(Tag);
                if (chest.GetInventory().AddItem(item)) { inv.RemoveItem(item); back += n; }
                else item.m_customData[Tag] = string.Join(",", f); // no room: it keeps it marked
            }
            if (back > 0) { Companion.SaveBag(me); st.Remember($"put back {back} food it borrowed from your chest"); }
            return back;
        }
    }

    /// <summary>Look at it and press a hotbar key: food or a potion it takes one of (and eats or drinks it when it needs to), gear the whole stack.</summary>
    [HarmonyPatch(typeof(Container), nameof(Container.UseItem))]
    internal static class Container_UseItem_Give
    {
        private static bool Prefix(Container __instance, Humanoid user, ItemDrop.ItemData item, ref bool __result)
        {
            if (!Companion.Is(__instance) || !(user is Player p) || item == null) return true;
            Humanoid c = __instance.GetComponent<Humanoid>();
            if (c == null || !Companion.CanCommand(c, p)) return true;
            __result = true;
            try
            {
                bool one = Food.IsFood(item) || item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Consumable;
                if (one)
                {
                    ItemDrop.ItemData copy = item.Clone();
                    copy.m_stack = 1;
                    if (!c.GetInventory().AddItem(copy)) { p.Message(MessageHud.MessageType.Center, $"{Companion.NameOf(c)} has no room"); return false; }
                    p.GetInventory().RemoveItem(item, 1);
                    Companion.SaveBag(c);
                    Talk.Say(c, Food.IsFood(item) ? "Thanks! I'll eat that." : "Thanks, I'll keep that for a bad moment.");
                    Idle.Queue(Brain.Get(c), "thumbsup", 0.3f, p);
                    return false;
                }
                if (!Companion.Give(c, p, item, out string why)) { if (why != null) p.Message(MessageHud.MessageType.Center, why); return false; }
                Brain.Get(c).NextGear = 0f; // wear it now if it is better
                Talk.Say(c, "Thanks!");
                Idle.Queue(Brain.Get(c), "thumbsup", 0.3f, p);
            }
            catch (Exception e) { Plugin.Instance?.Warn("Giving to the companion: " + e.Message); }
            return false;
        }
    }
}
