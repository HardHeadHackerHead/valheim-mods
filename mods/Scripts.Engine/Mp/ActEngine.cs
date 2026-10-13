using System;
using Newtonsoft.Json.Linq;

namespace Scripts.Mp
{
    /// <summary>What a player's own game can do to its character (Agent.cs fills it in with Unity; the tests with a fake).</summary>
    internal interface IGame
    {
        double Health { get; }
        double MaxHealth { get; }
        double MaxStamina { get; }
        bool KnowsItem(string prefab);
        bool KnowsEffect(string name);
        int AddItem(string prefab, int n);          // how many went into the bag: only what fits, the rest is never dropped or lost
        int RemoveItem(string prefab, int n);       // how many were taken out (never equipped ones)
        double Heal(double n);
        double Hurt(double n);
        void SetStamina(double value);
        bool AddEffect(string name, double seconds);
        bool RemoveEffect(string name);
        bool Launch(double dx, double dy, double dz, double force);
        bool ZoneLoaded(double x, double z);
        bool Ground(double x, double z, out double y);
        bool TeleportTo(double x, double y, double z);
        void Banner(string text);
    }

    /// <summary>
    /// The player's game answering a server's `act`: check the op against the player's own settings and the same lists and caps the server
    /// used, do it, and say what really happened (`done`). A server that asks for something it should not gets "no" and nothing happens.
    /// </summary>
    internal static class ActEngine
    {
        internal static Frame Handle(IGame g, ClientPolicy policy, Frame act)
        {
            string op = act.S("op");
            try
            {
                if (!policy.Allows(op))
                    return Done(act, false, 0.0, IsKnown(op) ? "the player does not allow it" : "unknown action");
                switch (op)
                {
                    case "give":
                        {
                            string prefab = act.S("p");
                            Verdict v = Validate.Give(prefab, (int)act.N("n", 1));
                            if (!v.Ok) return Done(act, false, 0.0, v.Why);
                            if (!g.KnowsItem(prefab)) return Done(act, false, 0.0, "no item called '" + Validate.Text(prefab, 30) + "'");
                            int got = g.AddItem(prefab, (int)v.N);
                            return Done(act, got > 0, got, got > 0 ? "" : "the bag is full");
                        }
                    case "take":
                        {
                            Verdict v = Validate.Take(act.S("p"), (int)act.N("n", 1));
                            if (!v.Ok) return Done(act, false, 0.0, v.Why);
                            int taken = g.RemoveItem(act.S("p"), (int)v.N);
                            return Done(act, taken > 0, taken, taken > 0 ? "" : "they have none");
                        }
                    case "heal":
                        {
                            Verdict v = Validate.Heal(act.N("n"));
                            if (!v.Ok) return Done(act, false, 0.0, v.Why);
                            double done = g.Heal(v.N);
                            return Done(act, done > 0.0, done, done > 0.0 ? "" : "already at full health");
                        }
                    case "hurt":
                        {
                            Verdict v = Validate.Hurt(act.N("n"), g.Health);
                            if (!v.Ok) return Done(act, false, 0.0, v.Why);
                            double done = g.Hurt(v.N);
                            return Done(act, done > 0.0, done, "");
                        }
                    case "stamina":
                        {
                            Verdict v = Validate.Stamina(act.N("n"), g.MaxStamina);
                            if (!v.Ok) return Done(act, false, 0.0, v.Why);
                            g.SetStamina(v.N);
                            return Done(act, true, v.N, "");
                        }
                    case "effect+":
                        {
                            Verdict v = Validate.Effect(act.S("name"), act.N("s", 30.0));
                            if (!v.Ok) return Done(act, false, 0.0, v.Why);
                            if (!g.KnowsEffect(act.S("name"))) return Done(act, false, 0.0, "this game has no status effect called '" + Validate.Text(act.S("name"), 30) + "'");
                            bool ok = g.AddEffect(act.S("name"), v.N);
                            return Done(act, ok, ok ? v.N : 0.0, ok ? "" : "it did not take");
                        }
                    case "effect-":
                        {
                            Verdict v = Validate.EffectOff(act.S("name"));
                            if (!v.Ok) return Done(act, false, 0.0, v.Why);
                            bool ok = g.RemoveEffect(act.S("name"));
                            return Done(act, ok, 0.0, ok ? "" : "they did not have it");
                        }
                    case "launch":
                        {
                            Verdict v = Validate.Launch(act.N("dx"), act.N("dy"), act.N("dz"), act.N("f"));
                            if (!v.Ok) return Done(act, false, 0.0, v.Why);
                            bool ok = g.Launch(act.N("dx"), act.N("dy"), act.N("dz"), v.N);
                            return Done(act, ok, v.N, ok ? "" : "it did not move");
                        }
                    case "teleport":
                        {
                            double x = act.N("x"), z = act.N("z");
                            bool loaded = g.ZoneLoaded(x, z);
                            double gy = 0.0;
                            bool has = loaded && g.Ground(x, z, out gy);
                            Verdict v = Validate.Teleport(loaded, has, gy);   // (the height the server gave is never read: only the ground decides)
                            if (!v.Ok) return Done(act, false, 0.0, v.Why);
                            bool ok = g.TeleportTo(x, v.N, z);
                            return Done(act, ok, 0.0, ok ? "" : "the teleport did not start");
                        }
                    case "banner":
                        {
                            string text = Validate.Text(act.S("text"), 60);
                            Verdict v = Validate.Banner(text);
                            if (!v.Ok) return Done(act, false, 0.0, v.Why);
                            g.Banner(text);
                            return Done(act, true, 0.0, "");
                        }
                }
                return Done(act, false, 0.0, "unknown action");
            }
            catch (Exception e)
            {
                return Done(act, false, 0.0, "failed: " + Validate.Text(e.Message, 80));
            }
        }

        internal static bool IsKnown(string op)
        {
            switch (op)
            {
                case "give": case "take": case "heal": case "hurt": case "stamina": case "effect+": case "effect-": case "launch": case "teleport": case "banner": return true;
                default: return false;
            }
        }

        private static Frame Done(Frame act, bool ok, double n, string why) =>
            new Frame("done", act.I, new JObject { ["ok"] = ok, ["n"] = n, ["why"] = why ?? "" });
    }
}
