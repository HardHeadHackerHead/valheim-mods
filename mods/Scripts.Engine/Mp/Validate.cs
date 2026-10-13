using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Scripts.Mp
{
    /// <summary>The answer of a check: fine (with the number to use, possibly cut down) or not (with why, in words).</summary>
    internal struct Verdict
    {
        internal bool Ok;
        internal double N;
        internal string Why;
        internal static Verdict Yes(double n) => new Verdict { Ok = true, N = n, Why = "" };
        internal static Verdict No(string why) => new Verdict { Ok = false, N = 0.0, Why = why };
    }

    /// <summary>
    /// The checks every action on a player goes through, on the server before it asks and again on the player's own game when it is
    /// asked (docs/scripting-multiplayer.md section 4): the player's game never trusts the server to have been careful, so a server that
    /// is not ours still cannot hand a player what the mod would not. No Unity in here, so it is tested on its own. The lists it reads are
    /// the mod's own on a player's game (plus what an owner's server sent, which can only narrow the never-lists, see Lists.Apply).
    /// </summary>
    internal static class Validate
    {
        internal const int MaxTake = 20;
        internal const double MaxHeal = 500.0, MaxHurt = 50.0, MaxForce = 15.0;

        internal static string Text(string s, int max)
        {
            if (s == null) return "";
            s = s.Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ');
            return s.Length > max ? s.Substring(0, max) : s;
        }

        internal static Verdict Give(string prefab, int n)
        {
            if (string.IsNullOrEmpty(prefab) || prefab.Length > 60) return Verdict.No("no such item");
            if (!Lists.Allow.TryGetValue(prefab, out int most) || Lists.NeverGive.Contains(prefab) || Lists.Trophy(prefab)) return Verdict.No("'" + prefab + "' can't be given");
            return Verdict.Yes(Math.Max(1, Math.Min(most, n)));
        }

        internal static Verdict Take(string prefab, int n)
        {
            if (string.IsNullOrEmpty(prefab) || prefab.Length > 60) return Verdict.No("no such item");
            if (Lists.NeverTake.Contains(prefab)) return Verdict.No("'" + prefab + "' can't be taken");
            return Verdict.Yes(Math.Max(1, Math.Min(MaxTake, n)));
        }

        internal static Verdict Heal(double n) => double.IsNaN(n) || n <= 0.0 ? Verdict.No("nothing to heal") : Verdict.Yes(Math.Min(MaxHeal, n));

        /// <summary>Damage to a player never takes the last point of health and is never more than 50 at once.</summary>
        internal static Verdict Hurt(double n, double health)
        {
            if (double.IsNaN(n) || n <= 0.0) return Verdict.No("nothing to do");
            double amount = Math.Min(Math.Min(MaxHurt, n), health - 1.0);
            return amount > 0.0 ? Verdict.Yes(amount) : Verdict.No("that would be the last point of health");
        }

        internal static Verdict Stamina(double v, double max) => double.IsNaN(v) ? Verdict.No("not a number") : Verdict.Yes(Math.Max(0.0, Math.Min(max, v)));

        internal static Verdict Effect(string name, double seconds)
        {
            if (string.IsNullOrEmpty(name) || !Lists.StatusEffects.TryGetValue(name, out int most)) return Verdict.No("'" + Text(name, 30) + "' is not a status effect a script may hand out");
            return Verdict.Yes(Math.Max(5.0, Math.Min(most, double.IsNaN(seconds) ? 30.0 : seconds)));
        }

        internal static Verdict EffectOff(string name) => !string.IsNullOrEmpty(name) && Lists.StatusEffects.ContainsKey(name) ? Verdict.Yes(0.0) : Verdict.No("'" + Text(name, 30) + "' is not a status effect a script may handle");

        /// <summary>A shove: a direction that points somewhere, a force of 0 to 15 metres a second (the verdict's number is the force).</summary>
        internal static Verdict Launch(double dx, double dy, double dz, double force)
        {
            if (double.IsNaN(dx + dy + dz + force) || double.IsInfinity(dx + dy + dz + force)) return Verdict.No("not numbers");
            if (dx * dx + dy * dy + dz * dz < 0.0001) return Verdict.No("no direction");
            double f = Math.Max(0.0, Math.Min(MaxForce, force));
            return f > 0.0 ? Verdict.Yes(f) : Verdict.No("no force");
        }

        /// <summary>A teleport lands on the ground, whatever height was asked for: a landing a few metres off kills.</summary>
        internal static Verdict Teleport(bool zoneLoaded, bool hasGround, double groundY)
        {
            if (!zoneLoaded || !hasGround) return Verdict.No("no ground to land on there");
            return Verdict.Yes(groundY + 1.0);
        }

        internal static Verdict Banner(string text) => string.IsNullOrWhiteSpace(text) ? Verdict.No("no words") : Verdict.Yes(0.0);

        /// <summary>The game's name for a weather a script may ask for, or "" when there is none.</summary>
        internal static string EnvOf(string friendly)
        {
            switch (friendly)
            {
                case "clear": return "Clear";
                case "rain": return "Rain";
                case "storm": return "ThunderStorm";
                case "mist": return "Misty";
                case "snow": return "Snow";
                default: return "";
            }
        }

        internal static Verdict Env(string friendly, double seconds)
        {
            string env = EnvOf(friendly);
            return env.Length == 0 ? Verdict.No("no weather called '" + Text(friendly, 20) + "'") : Verdict.Yes(Math.Max(10.0, Math.Min(1800.0, double.IsNaN(seconds) ? 60.0 : seconds)));
        }

        internal static Verdict Wind(double degrees, double strength, double seconds)
        {
            if (double.IsNaN(degrees + strength + seconds)) return Verdict.No("not numbers");
            return Verdict.Yes(Math.Max(10.0, Math.Min(600.0, seconds)));
        }

        internal static Verdict Shake(double range, double strength)
        {
            if (double.IsNaN(range + strength)) return Verdict.No("not numbers");
            return Verdict.Yes(Math.Max(0.0, Math.Min(3.0, strength)));
        }

        /// <summary>An effect or sound name must be on the lists (the game-side check of what the prefab carries is made where Unity is).</summary>
        internal static Verdict Fx(string name, bool sound) => !string.IsNullOrEmpty(name) && (sound ? Lists.Sounds : Lists.VisualEffects).Contains(name) ? Verdict.Yes(0.0) : Verdict.No("'" + Text(name, 40) + "' is not on the list of " + (sound ? "sounds" : "effects"));

        internal static double Scale(double s) => double.IsNaN(s) ? 1.0 : Math.Max(0.5, Math.Min(3.0, s));
        internal static double Range(double r) => double.IsNaN(r) ? 60.0 : Math.Max(1.0, Math.Min(60.0, r));
    }

    /// <summary>
    /// What a player allows servers to do to their character. The risky ones start off: taking items, teleporting and shoving. Set from the
    /// player's own config on their game; an op that is off is answered "the player does not allow it", and the script's log says so.
    /// </summary>
    internal sealed class ClientPolicy
    {
        internal bool Give = true, HealHurt = true, Effects = true, Banner = true, Take = false, Teleport = false, Launch = false;

        internal bool Allows(string op)
        {
            switch (op)
            {
                case "give": return Give;
                case "take": return Take;
                case "heal": case "hurt": case "stamina": return HealHurt;
                case "effect+": case "effect-": return Effects;
                case "launch": return Launch;
                case "teleport": return Teleport;
                case "banner": return Banner;
                default: return false;
            }
        }
    }
}
