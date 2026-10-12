using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace DHack.Shared
{
    /// <summary>
    /// A skill of the mod's own in the player's skills window (trained, lowered on death and saved like the game's), that survives a
    /// session without the mod. The game's skills are an enum, so a mod's skill is a number: a hash of a fixed id string, far above the
    /// game's 0-999. On its own the game drops it the first time the character loads without the mod (Skills.Load keeps only enum values:
    /// a second mod-manager profile, the mod switched off, a patch failing after a game update), and the next save makes that final. So:
    ///   - its def (icon, description, increase step) on every player's Skills, and handed out when the game looks it up;
    ///   - Skills.IsSkillValid accepting the number, so Skills.Load keeps the saved level;
    ///   - a backup in the character's m_customData ("&lt;id&gt;.skill": level and accumulator), written on every save and put back on load
    ///     when the skill is missing or at 0 and the backup has more (the game keeps keys it doesn't know, with or without the mod);
    ///   - its name where the game shows "$skill_" + the number (the skills window, "skill up" messages, status effect tooltips): the
    ///     mod's own name key is copied there, in every language;
    ///   - the game's raiseskill / resetskill commands (with tab completion, and "all") find it by its id or its English name.
    /// No library, patches through its own Harmony id (unpatched by Dispose), hot-reloads in a world.
    ///
    ///   MySkill = new DHack.Shared.Skill("com.you.yourmod", "Tailoring", "$yourmod_skill", "$yourmod_skill_description", icon);
    ///   p.RaiseSkill(MySkill.Type, 1f);    MySkill.Factor(p)  (0 to 1)    MySkill.Level(p)  (0 to 100)
    ///   MySkill.Gain = SkillGain.Value;    // optional: how fast it rises, live (a synced setting)
    ///   void OnDestroy() => MySkill.Dispose();
    ///   (translations: "yourmod_skill": "Tailoring", "yourmod_skill_description": "...")
    ///
    /// Mind:
    ///   - Never change the id once released: the number is its hash, and a new number starts every player at 0 (the backup too).
    ///     A mod moving off blaxxun's SkillManager keeps its number: type: Skill.SkillManagerType("English name") (its Math.Abs hash).
    ///   - Dying lowers it like the game's skills (Skills.LowerAllSkills walks every skill the player has), and with the world's skill cap
    ///     on, it counts in the total and is rebalanced with the rest.
    ///   - Make it in Awake after Translations.Load: its name is copied from the mod's words (again on every spawn and language change).
    ///
    /// Shared source (mods/Shared/Skill.cs in the valheim-mods repo), compiled into each mod that uses it (Parkour). Claude Tools also
    /// ships it as BepInEx/claude/templates/Skill.cs: copy it into your mod and change the namespace to yours.
    /// </summary>
    internal sealed class Skill : IDisposable
    {
        /// <summary>The fixed id the number is made from (also the backup's key: id + ".skill").</summary>
        public readonly string Id;
        public readonly string EnglishName;
        /// <summary>The mod's key for the skill's name (without the $), or null: the English name everywhere.</summary>
        public readonly string NameKey;
        public readonly Skills.SkillType Type;
        public readonly Skills.SkillDef Def;
        /// <summary>The key the game shows the name under: "skill_" + the number.</summary>
        public readonly string GameNameKey;
        /// <summary>The word for it in the console (the English name without spaces): raiseskill Parkour 10.</summary>
        public readonly string ConsoleName;

        private readonly string _backupKey;
        private readonly float _baseStep;
        private float _gain = 1f;

        /// <summary>
        /// id: fixed forever (the mod's GUID is a good one). nameKey/descriptionKey: the mod's translation keys ("$yourmod_skill"), or null.
        /// step: how fast it rises (1: like the game's skills). type: only to keep an existing number (SkillManagerType); 0 makes it from id.
        /// </summary>
        public Skill(string id, string englishName, string nameKey, string descriptionKey, Sprite icon, float step = 1f, Skills.SkillType type = Skills.SkillType.None)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentException("A skill needs a fixed id", nameof(id));
            Id = id;
            EnglishName = string.IsNullOrEmpty(englishName) ? id : englishName;
            NameKey = string.IsNullOrEmpty(nameKey) ? null : nameKey.TrimStart('$');
            Type = type != Skills.SkillType.None ? type : TypeFor(id);
            GameNameKey = "skill_" + Type.ToString().ToLower();   // (as the game makes it: the number, for a value the enum doesn't name)
            ConsoleName = EnglishName.Replace(" ", "");
            _backupKey = id + ".skill";
            _baseStep = step;
            Def = new Skills.SkillDef
            {
                m_skill = Type,
                m_icon = icon,
                m_description = string.IsNullOrEmpty(descriptionKey) ? "" : (descriptionKey.StartsWith("$") ? descriptionKey : "$" + descriptionKey),
                m_increseStep = step,
            };

            foreach (Skill other in Live)
                if (other.Type == Type) Debug.LogWarning($"[{id}] skill number {(int)Type} is also {other.Id}'s");
            Live.Add(this);
            PatchOnce(id);
            ApplyName();
            Register(Player.m_localPlayer);   // a hot reload while in a world (or the mod switched on there)
        }

        /// <summary>How fast the skill rises, on top of the step it was made with (1: as made). Live: set it from a synced setting.</summary>
        public float Gain
        {
            get => _gain;
            set { _gain = Mathf.Max(0f, value); Def.m_increseStep = _baseStep * _gain; }
        }

        /// <summary>0 to 1: the player's level over 100 (with status effects that change skills).</summary>
        public float Factor(Player p) => p != null && p.GetSkills() != null ? p.GetSkillFactor(Type) : 0f;

        /// <summary>0 to 100, whole levels (with status effects that change skills).</summary>
        public float Level(Player p) => p != null && p.GetSkills() != null ? p.GetSkills().GetSkillLevel(Type) : 0f;

        /// <summary>The number a fixed id makes: its stable hash, positive (the key "skill_&lt;n&gt;" has no minus sign) and above the game's 0-999.</summary>
        public static Skills.SkillType TypeFor(string id)
        {
            int hash = id.GetStableHashCode() & 0x7FFFFFFF;
            if (hash < 1000) hash += 1000;
            return (Skills.SkillType)hash;
        }

        /// <summary>The number blaxxun's SkillManager gives a skill: Math.Abs of its English name's stable hash. For a mod moving off it.</summary>
        public static Skills.SkillType SkillManagerType(string englishName)
        {
            int hash = englishName.GetStableHashCode();
            return (Skills.SkillType)(hash == int.MinValue ? int.MaxValue : Math.Abs(hash));   // (int.MinValue: SkillManager itself would throw)
        }

        /// <summary>Stops the patches (once this mod's last skill goes). The def stays on the player, so the level shows and saves until the game restarts.</summary>
        public void Dispose()
        {
            if (!Live.Remove(this)) return;
            if (Live.Count == 0 && _harmony != null)
            {
                _harmony.UnpatchSelf();
                _harmony = null;
            }
        }

        // ---- the player's skill ----

        private static readonly AccessTools.FieldRef<Skills, Dictionary<Skills.SkillType, Skills.Skill>> SkillData =
            AccessTools.FieldRefAccess<Skills, Dictionary<Skills.SkillType, Skills.Skill>>("m_skillData");
        private static readonly AccessTools.FieldRef<Skills, Player> SkillsPlayer = AccessTools.FieldRefAccess<Skills, Player>("m_player");
        private static readonly MethodInfo GetSkillMethod = AccessTools.Method(typeof(Skills), "GetSkill");
        private static readonly MethodInfo Rebalance = AccessTools.Method(typeof(Skills), "RebalanceSkills");

        private Skills.Skill Entry(Skills skills, bool make)
        {
            if (skills == null) return null;
            if (SkillData(skills).TryGetValue(Type, out Skills.Skill s)) return s;
            return make ? GetSkillMethod?.Invoke(skills, new object[] { Type }) as Skills.Skill : null;
        }

        private void Register(Player p)
        {
            if (p == null || p.GetSkills() == null) return;
            Register(p.GetSkills());
            Restore(p);
        }

        /// <summary>Our def in the player's list, in place of an earlier copy's (after a reload: its icon, its step), and the level pointed at it.</summary>
        private void Register(Skills skills)
        {
            int at = skills.m_skills.FindIndex(d => d != null && d.m_skill == Type);
            if (at < 0) skills.m_skills.Add(Def);
            else skills.m_skills[at] = Def;
            if (SkillData(skills).TryGetValue(Type, out Skills.Skill s)) s.m_info = Def;
        }

        private void Backup(Player p)
        {
            Skills.Skill s = Entry(p.GetSkills(), false);
            if (s == null && !p.m_customData.ContainsKey(_backupKey)) return;   // (never trained: nothing to keep)
            float level = s?.m_level ?? 0f, acc = s?.m_accumulator ?? 0f;   // (missing with a backup: reset by resetskill, so the backup says 0)
            p.m_customData[_backupKey] = level.ToString("R", CultureInfo.InvariantCulture) + ";" + acc.ToString("R", CultureInfo.InvariantCulture);
        }

        private void Restore(Player p)
        {
            if (!p.m_customData.TryGetValue(_backupKey, out string saved) || string.IsNullOrEmpty(saved)) return;
            string[] parts = saved.Split(';');
            if (!float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float level)) return;
            float acc = 0f;
            if (parts.Length > 1) float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out acc);
            level = Mathf.Clamp(level, 0f, 100f);
            if (level <= 0f && acc <= 0f) return;
            Skills.Skill s = Entry(p.GetSkills(), false);
            if (s != null && (s.m_level > 0f || (level <= 0f && s.m_accumulator >= acc))) return;   // (the save has it: the save wins)
            s = Entry(p.GetSkills(), true);
            if (s == null) return;
            s.m_level = level;
            s.m_accumulator = Mathf.Max(0f, acc);
            Debug.Log($"[{Id}] {EnglishName} skill put back at {level:0.#} from the character's backup (it was dropped by a load without the mod)");
        }

        // ---- the name ----

        private static readonly MethodInfo AddWord = AccessTools.Method(typeof(Localization), "AddWord");

        /// <summary>The game's "$skill_&lt;number&gt;" gets the mod's own name for it, in the current language.</summary>
        private void ApplyName()
        {
            Localization loc = Localization.instance;
            if (loc == null || AddWord == null) return;
            AddWord.Invoke(loc, new object[] { GameNameKey, ShownName() });
        }

        private string ShownName()
        {
            if (NameKey == null || Localization.instance == null) return EnglishName;
            string text = Localization.instance.Localize("$" + NameKey);
            return string.IsNullOrEmpty(text) || text == "[" + NameKey + "]" ? EnglishName : text;
        }

        // ---- console ----

        private bool Named(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            name = name.Trim();
            return string.Equals(name, Id, StringComparison.OrdinalIgnoreCase) || string.Equals(name, EnglishName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, ConsoleName, StringComparison.OrdinalIgnoreCase) || string.Equals(name, ((int)Type).ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
                || string.Equals(name, ShownName().Replace(" ", ""), StringComparison.OrdinalIgnoreCase);
        }

        private void CheatRaise(Skills skills, float value, bool showMessage)
        {
            Skills.Skill s = Entry(skills, true);
            if (s == null) return;
            s.m_level = Mathf.Clamp(s.m_level + value, 0f, 100f);
            if (skills.m_useSkillCap) Rebalance?.Invoke(skills, new object[] { Type });
            if (!showMessage) return;
            SkillsPlayer(skills)?.Message(MessageHud.MessageType.TopLeft, $"Skill increased {ShownName()}: {(int)s.m_level}", 0, Def.m_icon);
            global::Console.instance?.Print($"Skill {ConsoleName} = {s.m_level}");
        }

        // ---- patches ----

        private static readonly List<Skill> Live = new List<Skill>();   // this mod's skills (each mod has its own copy of this class)
        private static Harmony _harmony;

        private static void PatchOnce(string id)
        {
            if (_harmony != null) return;
            _harmony = new Harmony(id + ".skill");
            Patch(typeof(Skills), nameof(Skills.Awake), postfix: nameof(Skills_Awake));
            Patch(typeof(Skills), "IsSkillValid", postfix: nameof(Skills_IsSkillValid));
            Patch(typeof(Skills), "GetSkillDef", postfix: nameof(Skills_GetSkillDef));
            Patch(typeof(Skills), nameof(Skills.CheatRaiseSkill), prefix: nameof(Skills_CheatRaiseSkill));
            Patch(typeof(Skills), nameof(Skills.CheatResetSkill), prefix: nameof(Skills_CheatResetSkill));
            Patch(typeof(Player), nameof(Player.Save), prefix: nameof(Player_Save));
            Patch(typeof(Player), nameof(Player.Load), postfix: nameof(Player_Load));
            Patch(typeof(Terminal.ConsoleCommand), nameof(Terminal.ConsoleCommand.GetTabOptions), postfix: nameof(ConsoleCommand_GetTabOptions));
            Patch(typeof(Localization), nameof(Localization.SetLanguage), postfix: nameof(Localization_SetLanguage));
        }

        private static void Patch(System.Type type, string method, string prefix = null, string postfix = null)
        {
            try
            {
                MethodInfo target = AccessTools.Method(type, method);
                if (target == null) { Debug.LogWarning($"[Skill] {type.Name}.{method} not found: the game changed"); return; }
                _harmony.Patch(target,
                    prefix: prefix == null ? null : new HarmonyMethod(AccessTools.Method(typeof(Skill), prefix)),
                    postfix: postfix == null ? null : new HarmonyMethod(AccessTools.Method(typeof(Skill), postfix)));
            }
            catch (Exception e) { Debug.LogWarning($"[Skill] couldn't patch {type.Name}.{method}: {e.Message}"); }
        }

        private static void Skills_Awake(Skills __instance)
        {
            foreach (Skill s in Live) { s.Register(__instance); s.ApplyName(); }
        }

        private static void Skills_IsSkillValid(Skills.SkillType type, ref bool __result)
        {
            if (__result) return;
            foreach (Skill s in Live) if (s.Type == type) { __result = true; return; }
        }

        // The game makes a skill's entry from its def: never let ours be made without one (a missing def breaks the skills window).
        private static void Skills_GetSkillDef(Skills __instance, Skills.SkillType type, ref Skills.SkillDef __result)
        {
            if (__result != null) return;
            foreach (Skill s in Live)
                if (s.Type == type) { s.Register(__instance); __result = s.Def; return; }
        }

        // The game's raiseskill walks its enum ("Skill not found Parkour"): ours by name; "all" raises ours too.
        private static bool Skills_CheatRaiseSkill(Skills __instance, string name, float value, bool showMessage, bool __runOriginal)
        {
            if (!__runOriginal || name == null) return __runOriginal;
            if (name.Trim().ToLower() == "all") { foreach (Skill s in Live) s.CheatRaise(__instance, value, false); return true; }
            foreach (Skill s in Live)
                if (s.Named(name)) { s.CheatRaise(__instance, value, showMessage); return false; }
            return true;
        }

        private static bool Skills_CheatResetSkill(Skills __instance, string name, bool __runOriginal)
        {
            if (!__runOriginal || name == null) return __runOriginal;
            if (name.Trim().ToLower() == "all") { foreach (Skill s in Live) __instance.ResetSkill(s.Type); return true; }
            foreach (Skill s in Live)
                if (s.Named(name)) { __instance.ResetSkill(s.Type); global::Console.instance?.Print($"Skill {s.ConsoleName} reset"); return false; }
            return true;
        }

        // Written before the game writes m_customData (Player.Save: skills, then every custom key): only our key, never the dictionary.
        private static void Player_Save(Player __instance)
        {
            if (__instance == null || __instance.m_customData == null || __instance.GetSkills() == null) return;
            foreach (Skill s in Live)
                try { s.Backup(__instance); }
                catch (Exception e) { Debug.LogWarning($"[{s.Id}] skill backup: {e.Message}"); }
        }

        // Player.Load reads the skills, then the custom keys: both are in by now.
        private static void Player_Load(Player __instance)
        {
            if (__instance == null || __instance.m_customData == null || __instance.GetSkills() == null) return;
            foreach (Skill s in Live)
                try { s.Register(__instance.GetSkills()); s.Restore(__instance); }
                catch (Exception e) { Debug.LogWarning($"[{s.Id}] skill restore: {e.Message}"); }
        }

        // Tab completion for raiseskill / resetskill: a new list each time (the game keeps its own; ours must go with the mod).
        private static void ConsoleCommand_GetTabOptions(Terminal.ConsoleCommand __instance, ref List<string> __result)
        {
            if (__result == null || Live.Count == 0) return;
            string command = __instance.Command?.ToLower();
            if (command != "raiseskill" && command != "resetskill") return;
            var options = new List<string>(__result);
            foreach (Skill s in Live) if (!options.Contains(s.ConsoleName)) options.Add(s.ConsoleName);
            __result = options;
        }

        // After the game's OnLanguageChange (where the mod's Translations put the new language's words in): the name in the new language.
        private static void Localization_SetLanguage()
        {
            foreach (Skill s in Live) s.ApplyName();
        }
    }
}
