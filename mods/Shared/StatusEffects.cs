using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Object = UnityEngine.Object;

namespace DHack.Shared
{
    /// <summary>
    /// A mod's own status effects (buffs and debuffs) made in code, kept in the game's list while you play and safe to hot-reload. The game
    /// finds an effect by the hash of its name in ObjectDB.m_StatusEffects, and a world load swaps that list for another ObjectDB's, so an
    /// effect must go in again on every ObjectDB.Awake and CopyOtherDB. On unload it comes off every character on this game first (a copy left
    /// on a character points at the destroyed effect, and the game then keeps it for ever or adds it twice), then out of ObjectDB; after a
    /// reload the new copy puts it back on the characters this game owns with the time they had left, so nobody keeps running unloaded code.
    ///
    /// Shared source (mods/Shared/StatusEffects.cs in the valheim-mods repo), compiled into each mod that links it in its csproj: Rainbows, CigarSmoking, SkalTavern.
    /// Claude Tools also ships it as BepInEx/claude/templates/StatusEffects.cs: copy it into your mod and change the namespace to yours. Then:
    ///
    ///   var e = StatusEffects.Create&lt;SE_MyBuff&gt;("MyMod_SE_Buff", "$mymod_buff", icon);   // once (needs no ObjectDB); duration etc. are yours
    ///   StatusEffects.Register();          // postfixes on ObjectDB.Awake, ObjectDB.CopyOtherDB and ZNetScene.Awake, and in your plugin's Awake
    ///   StatusEffects.Apply(character, hash);   // hash = "MyMod_SE_Buff".GetStableHashCode(); Has, Remaining, Remove too
    ///   StatusEffects.Unload();            // in OnDestroy (then destroy your icons, if you made them)
    ///
    /// Pitfalls:
    ///   - Each character gets StatusEffect.Clone(), a MemberwiseClone: a List, Dictionary or array field in your subclass (SE_Stats' own
    ///     m_mods too) is one object shared by everyone who has the effect. Make new ones in Setup before changing them.
    ///   - Effects aren't saved with the character (only the guardian power is): a buff meant to outlast a logout keeps its remaining time in
    ///     Player.m_customData (a key named after your mod) and is given again after the player spawns.
    ///   - The HUD shows only effects that have an m_icon and aren't m_hidden. m_name is the "$key" shown under the icon.
    ///   - Name it after your mod ("MyMod_SE_..."): every mod's effects share one list, found by name hash, and a same-named one replaces yours.
    ///   - Apply on a character this game doesn't own goes to its owner, which adds it only if it has your mod (a vanilla game ignores the
    ///     hash). Effects run only on the owner's game: Has and Remaining say "no" for anyone else's character, and Remove works only there.
    ///   - Only Create'd effects are carried over a reload, with their time left. Setup runs again for each, with PuttingBack true: skip there
    ///     what only a fresh one does (a drink's sip; its start message isn't shown again). Anything else a subclass keeps (how drunk you
    ///     are, the end time it told other games) starts again from Setup unless it implements StatusEffects.ICarry.
    /// </summary>
    internal static class StatusEffects
    {
        // What an unloading copy took off characters, for the next copy to put back: one AppDomain slot shared by every mod using this file,
        // holding only the game's and mscorlib's types (each copy has its own private classes). Each entry: { Character, effect name,
        // seconds left (-1: no end), Time.realtimeSinceStartup when taken, ICarry.Carry (a float, or null) }. A copy takes only its own
        // names, and reads the fifth only if it is there (copies from before it wrote four).
        private const string CarryKey = "DHack.Shared.StatusEffects.carry";
        private const float CarryFor = 60f;   // a copy loaded later than this doesn't put them back (the mod was turned off, not reloaded)

        private static readonly List<StatusEffect> Mine = new List<StatusEffect>();
        private static readonly FieldInfo TimeField = typeof(StatusEffect).GetField("m_time", BindingFlags.Instance | BindingFlags.NonPublic);

        /// <summary>
        /// For an effect that keeps more than its time: Carry is read from a character's copy as an unloading copy takes it off, and PutBack is
        /// called on the reloaded copy's effect just after it is back on (Setup done, time left set) with that value (NaN when the older copy
        /// gave none). Do there what Setup skipped while PuttingBack, such as telling other games how long it has left.
        /// </summary>
        public interface ICarry
        {
            float Carry { get; }
            void PutBack(float carry);
        }

        /// <summary>True while an effect is being put back after a reload (during its Setup): skip what only a fresh one does (a drink's sip).</summary>
        public static bool PuttingBack { get; private set; }

        /// <summary>
        /// Makes one of this mod's effects (once per name: making a name again replaces the old one). The name starts with the mod's; its hash
        /// (name.GetStableHashCode()) is what the game looks it up by. nameKey is the "$key" under its icon, tooltipKey the "$key" of its
        /// tooltip (or override GetTooltipString). Without an icon it doesn't show on the HUD. Set m_ttl and the rest yourself, here or in Setup.
        /// </summary>
        public static T Create<T>(string name, string nameKey, Sprite icon = null, string tooltipKey = null) where T : StatusEffect
        {
            if (string.IsNullOrEmpty(name)) throw new ArgumentException("A status effect needs a name (start it with your mod's).", nameof(name));
            StatusEffect old = Mine.Find(e => e != null && e.name == name);
            if (old != null) { TakeOff(old, false); RemoveFromDb(old); Mine.Remove(old); Object.Destroy(old); }
            T effect = ScriptableObject.CreateInstance<T>();
            effect.name = name;
            effect.m_name = nameKey ?? "";
            effect.m_tooltip = tooltipKey ?? "";
            effect.m_icon = icon;
            effect.NameHash();   // worked out now, while it has its name: each character's copy keeps it
            Mine.Add(effect);
            return effect;
        }

        /// <summary>
        /// Puts this mod's effects in ObjectDB (again: call it from ObjectDB.Awake, ObjectDB.CopyOtherDB and ZNetScene.Awake postfixes and the
        /// plugin's Awake), replacing any same-named entry (an older copy's after a hot reload), then gives characters this game owns the effects
        /// an older copy took off them, or had left on them. Does nothing until there is an ObjectDB.
        /// </summary>
        public static void Register()
        {
            ObjectDB db = ObjectDB.instance;
            if (db == null) return;
            Mine.RemoveAll(e => e == null);
            foreach (StatusEffect e in Mine)
            {
                if (db.m_StatusEffects.Contains(e)) continue;
                db.m_StatusEffects.RemoveAll(x => x == null || x.name == e.name);
                db.m_StatusEffects.Add(e);
            }
            if (Mine.Count > 0) PutBack();
        }

        /// <summary>For OnDestroy: off every character on this game (the ones it owns remembered for a reloaded copy), out of ObjectDB, destroyed.</summary>
        public static void Unload()
        {
            foreach (StatusEffect e in Mine) if (e != null) TakeOff(e, true);
            foreach (StatusEffect e in Mine) if (e != null) RemoveFromDb(e);
            foreach (StatusEffect e in Mine) if (e != null) Object.Destroy(e);
            Mine.Clear();
        }

        /// <summary>
        /// Gives the effect (resetTime: if it's already on, its time starts over). True when it was given here; false when the character
        /// belongs to another game (sent to its owner, which adds it only if it has this mod) or it can't be given.
        /// </summary>
        public static bool Apply(Character character, int hash, bool resetTime = true)
        {
            SEMan sem = character != null ? character.GetSEMan() : null;
            if (sem == null || ObjectDB.instance == null) return false;
            bool here = character.IsOwner();
            sem.AddStatusEffect(hash, resetTime);   // (on someone else's character this is an RPC to its owner)
            return here && sem.HaveStatusEffect(hash);
        }

        /// <summary>Whether the character has it, on this game: true only on the game that owns the character.</summary>
        public static bool Has(Character character, int hash) => character != null && character.GetSEMan() != null && character.GetSEMan().HaveStatusEffect(hash);

        /// <summary>Seconds left (0: not on, on this game; infinity: no end).</summary>
        public static float Remaining(Character character, int hash)
        {
            StatusEffect se = character != null ? character.GetSEMan()?.GetStatusEffect(hash) : null;
            if ((object)se == null) return 0f;
            return se.m_ttl > 0f ? Mathf.Max(0f, se.GetRemaningTime()) : float.PositiveInfinity;
        }

        /// <summary>Takes it off, quietly or with its stop message. Only works on the game that owns the character (nothing is sent).</summary>
        public static bool Remove(Character character, int hash, bool quiet = true)
        {
            SEMan sem = character != null ? character.GetSEMan() : null;
            return sem != null && sem.RemoveStatusEffect(hash, quiet);
        }

        // ---- inside ---------------------------------------------------------------------------------------------------

        private static void RemoveFromDb(StatusEffect e)
        {
            ObjectDB db = ObjectDB.instance;
            if (db != null) db.m_StatusEffects.RemoveAll(x => x == null || x == e);   // (never another copy's same-named one)
        }

        /// <summary>Off every character this game has (each game only ever removes its own copies); the owned ones remembered if carry.</summary>
        private static void TakeOff(StatusEffect effect, bool carry)
        {
            int hash = effect.NameHash();
            List<object[]> carried = carry ? Carried(true) : null;
            foreach (Character c in new List<Character>(Character.GetAllCharacters()))   // (a copy: an effect's start or stop may change the list)
            {
                SEMan sem = c != null ? c.GetSEMan() : null;
                StatusEffect se = sem?.GetStatusEffect(hash);
                if ((object)se == null) continue;
                if (carried != null && c.IsOwner())
                    carried.Add(new object[] { c, effect.name, se.m_ttl > 0f ? Mathf.Max(0f, se.GetRemaningTime()) : -1f, Time.realtimeSinceStartup, CarryOf(se) });
                try { sem.RemoveStatusEffect(hash, true); }
                catch (Exception ex) { Debug.LogWarning($"[{effect.name}] taking it off {c.name}: {ex.Message}"); }
            }
        }

        /// <summary>
        /// Characters this game owns get this copy's effects in place of an older copy's: the ones an unloading copy took off (with the time
        /// they had left), and any left on them by an older copy that didn't take them off (its type is the old copy's, or it is destroyed).
        /// </summary>
        private static void PutBack()
        {
            List<object[]> carried = Carried(false);
            float now = Time.realtimeSinceStartup;
            foreach (StatusEffect effect in Mine)
            {
                int hash = effect.NameHash();
                if (carried != null)
                    for (int i = carried.Count - 1; i >= 0; i--)
                    {
                        object[] entry = carried[i];
                        if (entry == null || entry.Length < 4 || !(entry[1] is string name) || name != effect.name) continue;
                        carried.RemoveAt(i);
                        if (now - (float)entry[3] > CarryFor) continue;
                        var c = entry[0] as Character;
                        float carry = entry.Length > 4 && entry[4] is float f ? f : float.NaN;
                        if (c != null && c.IsOwner() && c.GetSEMan() != null && !c.GetSEMan().HaveStatusEffect(hash)) Give(c, effect, (float)entry[2], carry);
                    }
                foreach (Character c in new List<Character>(Character.GetAllCharacters()))
                {
                    SEMan sem = c != null && c.IsOwner() ? c.GetSEMan() : null;
                    StatusEffect se = sem?.GetStatusEffect(hash);
                    if ((object)se == null || (se != null && se.GetType() == effect.GetType())) continue;
                    float left = se.m_ttl > 0f ? Mathf.Max(0f, se.GetRemaningTime()) : -1f;   // (plain fields: readable on a destroyed copy)
                    try { sem.RemoveStatusEffect(hash, true); }
                    catch (Exception ex) { Debug.LogWarning($"[{effect.name}] taking an old copy off {c.name}: {ex.Message}"); }
                    if (!sem.HaveStatusEffect(hash)) Give(c, effect, left, float.NaN);
                }
            }
        }

        /// <summary>
        /// Adds it on this game (the character is this game's) without its start message, sets the time left (if it has an end), and hands an
        /// ICarry effect what it carried.
        /// </summary>
        private static void Give(Character c, StatusEffect effect, float left, float carry)
        {
            string message = effect.m_startMessage;
            effect.m_startMessage = "";   // (the copy made from it keeps this: it was only reloaded, nothing started)
            StatusEffect se;
            PuttingBack = true;
            try { se = c.GetSEMan().AddStatusEffect(effect, false); }
            catch (Exception ex) { Debug.LogWarning($"[{effect.name}] putting it back on {c.name}: {ex.Message}"); return; }
            finally { PuttingBack = false; effect.m_startMessage = message; }
            if (se == null) return;
            if (left >= 0f && se.m_ttl > 0f && TimeField != null) TimeField.SetValue(se, Mathf.Max(0f, se.m_ttl - left));
            if (se is ICarry k)
                try { k.PutBack(carry); }
                catch (Exception ex) { Debug.LogWarning($"[{effect.name}] handing back what it carried: {ex.Message}"); }
        }

        private static object CarryOf(StatusEffect se)
        {
            if (!(se is ICarry k)) return null;
            try { return k.Carry; }
            catch (Exception ex) { Debug.LogWarning($"[{se.name}] what it carries over a reload: {ex.Message}"); return null; }
        }

        private static List<object[]> Carried(bool make)
        {
            var list = AppDomain.CurrentDomain.GetData(CarryKey) as List<object[]>;
            if (list == null && make) { list = new List<object[]>(); AppDomain.CurrentDomain.SetData(CarryKey, list); }
            return list;
        }
    }
}
