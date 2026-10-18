using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;
using static DHack.Shared.Translations;

namespace Skateboards
{
    /// <summary>
    /// The tricks, the combo and the score, by the rules of the classic skateboarding games (THPS 2 and 3 as their players document them).
    ///
    ///   In the air: Attack is a flip trick, Block (held) a grab, each picked by the move key held as you press: none, W, S, A, D.
    ///     Flips   Kickflip, Impossible, Pop Shove-It, Heelflip, Hardflip. A flip takes a moment (FlipSeconds): land before it's done and
    ///             you bail. Attack again during the same flip: a Double (twice the points, a little longer).
    ///     Grabs   Indy, Nosegrab, Tailgrab, Melon, Method: points for as long as you hold on (it lets go by itself just above the ground).
    ///     Spins   every half turn (180) adds half a point to the multiplier, its name in front of the air's last trick ("FS 540 Kickflip").
    ///   Landing straight (the board within 12 degrees of the way you go) is Perfect: that air's tricks count half again. Landing near the
    ///   limit (past two thirds of LandingAngle) is Sloppy: they count three quarters. Past it you bail.
    ///   Grinds (the key held as you land on it): 50-50, Nosegrind, 5-0, Boardslide, Crooked. Manual (Crouch held; W at the start: Nose Manual).
    ///   Revert: Crouch right after landing on a ramp: the board pivots, the combo goes on (hold Crouch on into a manual).
    ///   Gaps, anywhere: Big Air, Rail 2 Rail, Long Grind, Transfer (from one ramp to another), Mega Ollie (1260 degrees and more).
    /// Each trick and gap adds its points and one to the multiplier; the same trick again in one combo scores 100, 75, 50, 25, then 10%.
    /// The combo banks (points times multiplier) when you roll away clean for a moment; a bail loses it.
    ///
    /// The special meter fills with what you land, drains slowly (faster when full) and empties when you bail. Full, it glows gold, you
    /// push faster and pop higher, and the specials open: two move keys tapped then the button. A then W, then Attack: Ragnarök Flip;
    /// D then S, then Block: Valkyrie Air; D then S just before landing on a rail: Bifröst Grind.
    /// The session's score and the best combo are kept on the character (m_customData "com.quad.skateboards.best"), the best for good.
    /// </summary>
    internal static class Tricks
    {
        public enum Flip { None, Kickflip, Impossible, PopShoveIt, Heelflip, Hardflip, Ragnarok }
        public enum Grab { None, Indy, Nosegrab, Tailgrab, Melon, Method, Valkyrie }
        public enum Grind { None, FiftyFifty, Nosegrind, FiveO, Boardslide, Crooked, Lipslide, Bifrost }

        private sealed class Entry { public string Key; public string Name; public float Points; public float Factor = 1f; }

        private static readonly List<Entry> Combo = new List<Entry>();
        private static float _spinMultiplier;
        public static bool HasCombo => Combo.Count > 0;
        public static float Multiplier => Combo.Count + _spinMultiplier;
        public static float Points => Combo.Sum(e => e.Points * e.Factor);

        // now
        public static Flip FlipNow;
        public static bool FlipDouble;
        public static float FlipStart = -10f, FlipLength = 0.5f;
        public static int Flips;   // every flip started, shared so the others play it
        public static Grab GrabNow;
        public static Grind GrindNow;
        public static bool Manual, NoseManual;
        private static Entry _live;      // the grab, grind or manual earning points now
        private static Entry _airLast;   // the last trick of this air (the spin's name goes on it)
        private static Entry _flipEntry;
        private static int _airFrom;     // where this air's tricks start in the combo
        private static float _liveRate;
        private static int _manualsInARow;

        // the special meter
        public static float Special;
        public static bool SpecialOn;

        // shown
        public static string Note = "";
        public static float NoteAt = -10f;
        public static Color NoteColor = Color.white;

        // banked
        public static long Session;
        public static long BestCombo;
        public static float LastBankAt = -10f, LastBailAt = -10f;
        public static long LastBankPoints;
        public static string LastBailText = "";
        private const string BestKey = Plugin.Guid + ".best";

        public static bool Flipping => FlipNow != Flip.None && Time.time - FlipStart < FlipLength;
        /// <summary>How much of the flip going on is still to turn (0 to 1).</summary>
        public static float FlipLeft => FlipNow == Flip.None ? 0f : Mathf.Clamp01(1f - (Time.time - FlipStart) / Mathf.Max(0.05f, FlipLength));

        /// <summary>How much harder balancing is now: manuals one after another with nothing to reset them, the same rail again.</summary>
        public static float BalanceHarder(bool sameRail) => 1f + 0.25f * Mathf.Max(0, _manualsInARow - 1) + (sameRail ? 0.3f : 0f);

        public static void Reset()
        {
            Clear();
            Session = 0;
            Special = 0f;
            SpecialOn = false;
        }

        /// <summary>No combo, no trick going on (getting on or off the board).</summary>
        public static void Clear()
        {
            Combo.Clear();
            _spinMultiplier = 0f;
            FlipNow = Flip.None;
            FlipDouble = false;
            GrabNow = Grab.None;
            GrindNow = Grind.None;
            Manual = NoseManual = false;
            _live = _airLast = _flipEntry = null;
            _manualsInARow = 0;
        }

        // ---- names and points ----

        private static string Key(object trick) => "skateboards_trick_" + trick.ToString().ToLowerInvariant();

        private static float FlipPoints(Flip f)
        {
            switch (f)
            {
                case Flip.Impossible: return 250f;
                case Flip.Hardflip: return 250f;
                case Flip.Ragnarok: return 1500f;
                default: return 100f;
            }
        }

        private static float GrabPoints(Grab g) => g == Grab.Valkyrie ? 1000f : g == Grab.Method ? 200f : 150f;
        private static float GrabRate(Grab g) => g == Grab.Valkyrie ? 400f : 150f;
        private static float GrindPoints(Grind g) => g == Grind.Bifrost ? 800f : g == Grind.FiftyFifty ? 100f : g == Grind.Crooked ? 200f : 150f;
        private static float GrindRate(Grind g) => g == Grind.Bifrost ? 600f : g == Grind.FiftyFifty ? 200f : g == Grind.Crooked ? 300f : 250f;

        /// <summary>The trick for the move key held: none, W, S, A, D.</summary>
        public static Flip PickFlip(Vector3 move)
        {
            if (move.z > 0.5f) return Flip.Impossible;
            if (move.z < -0.5f) return Flip.PopShoveIt;
            if (move.x < -0.5f) return Flip.Heelflip;
            if (move.x > 0.5f) return Flip.Hardflip;
            return Flip.Kickflip;
        }

        public static Grab PickGrab(Vector3 move)
        {
            if (move.z > 0.5f) return Grab.Nosegrab;
            if (move.z < -0.5f) return Grab.Tailgrab;
            if (move.x < -0.5f) return Grab.Melon;
            if (move.x > 0.5f) return Grab.Method;
            return Grab.Indy;
        }

        public static Grind PickGrind(Vector3 move)
        {
            if (SpecialOn && Controls.Tapped('D', 'S', 0.7f)) return Grind.Bifrost;
            if (move.z > 0.5f) return Grind.Nosegrind;
            if (move.z < -0.5f) return Grind.FiveO;
            if (move.x < -0.5f) return Grind.Boardslide;
            if (move.x > 0.5f) return Grind.Crooked;
            return Grind.FiftyFifty;
        }

        private static readonly float[] Repeats = { 1f, 0.75f, 0.5f, 0.25f, 0.1f };

        private static Entry Add(string key, string name, float points)
        {
            int before = Combo.Count(e => e.Key == key);
            var e = new Entry { Key = key, Name = name, Points = points, Factor = Repeats[Mathf.Min(before, Repeats.Length - 1)] };
            Combo.Add(e);
            Fill(points * e.Factor);
            return e;
        }

        private static void Tell(string text, Color colour)
        {
            Note = text;
            NoteAt = Time.time;
            NoteColor = colour;
        }

        // ---- the special meter ----

        private static void Fill(float points)
        {
            Special = Mathf.Clamp01(Special + points / 4000f);
            if (Special >= 1f && !SpecialOn)
            {
                SpecialOn = true;
                Tell(T("skateboards_special_ready"), GameUi.Gold);
                Skate.Note("special on");
                if (Player.m_localPlayer != null) Look.Hint(Player.m_localPlayer, "skateboards_hint_special");
            }
        }

        private static void Drain(float dt)
        {
            if (!Skate.Active) return;
            Special = Mathf.Max(0f, Special - (SpecialOn ? 0.045f : 0.015f) * dt);
            if (SpecialOn && Special < 0.02f) { SpecialOn = false; Skate.Note("special over"); }
        }

        // ---- what the riding tells us ----

        public static void TookOff(bool ollie)
        {
            _airLast = _flipEntry = null;
            _airFrom = Combo.Count;
            FlipNow = Flip.None;
            FlipDouble = false;
            GrabNow = Grab.None;
        }

        /// <summary>Every physics step while riding: flips and grabs in the air, the points of a grind, grab or manual held, the meter.</summary>
        public static void Step(Player p, float dt)
        {
            Drain(dt);
            bool attack = Controls.TakeAttack(), grab = Controls.TakeGrab();
            if (Skate.State == Ride.Air)
            {
                if (attack && Flipping && !FlipDouble && FlipNow != Flip.Ragnarok && _flipEntry != null && Time.time - FlipStart < FlipLength * 0.8f)
                {
                    // the same flip again: a double
                    FlipDouble = true;
                    FlipLength *= 1.45f;
                    _flipEntry.Name = T("skateboards_double", _flipEntry.Name);
                    _flipEntry.Points *= 2.2f;
                    _flipEntry.Key += "_double";
                    Flips++;
                    Skate.Note("double " + FlipNow);
                    Sounds.Play(Sfx.Flick, p.transform.position, 0.7f);
                }
                else if (attack && !Flipping && GrabNow == Grab.None)
                {
                    bool special = SpecialOn && Controls.Tapped('A', 'W', 0.7f);
                    FlipNow = special ? Flip.Ragnarok : PickFlip(Controls.Move);
                    FlipDouble = false;
                    FlipStart = Time.time;
                    FlipLength = Mathf.Lerp(Plugin.FlipSeconds.Value, Plugin.FlipSecondsAt100.Value, Skill.Factor(p)) * (special ? 1.4f : 1f);
                    Flips++;
                    _flipEntry = _airLast = Add(Key(FlipNow), T(Key(FlipNow)), FlipPoints(FlipNow));
                    Skate.Note("flip " + FlipNow);
                    Sounds.Play(Sfx.Flick, p.transform.position, 0.7f);
                }
                if (grab && GrabNow == Grab.None && !Flipping)
                {
                    GrabNow = SpecialOn && Controls.Tapped('D', 'S', 0.7f) ? Grab.Valkyrie : PickGrab(Controls.Move);
                    _live = _airLast = Add(Key(GrabNow), T(Key(GrabNow)), GrabPoints(GrabNow));
                    _liveRate = GrabRate(GrabNow);
                    Skate.Note("grab " + GrabNow);
                }
                if (GrabNow != Grab.None)
                {
                    // let go: by the key, or by itself just above the ground
                    bool near = Skate.Probe(p, Vector3.down, 0.6f, out RaycastHit _, out float gap) && gap < 0.45f && Skate.BodyOf(p).linearVelocity.y < 0f;
                    if (!Controls.GrabHeld || near) EndLive();
                }
            }
            else if (FlipNow != Flip.None && !Flipping) { FlipNow = Flip.None; FlipDouble = false; }

            if (_live != null && (Skate.State == Ride.Grind || Skate.State == Ride.Manual || GrabNow != Grab.None))
            {
                _live.Points += _liveRate * dt;
                Fill(_liveRate * dt * 0.5f);
            }
        }

        private static void EndLive()
        {
            _live = null;
            GrabNow = Grab.None;
        }

        /// <summary>
        /// Landed clean on the ground: the spin goes on the multiplier, the landing's quality on this air's tricks (angle: how far the board
        /// pointed off the way it went; limit: past it, a bail), and the gaps.
        /// </summary>
        public static void Landed(Player p, float spin, float air, bool vert, float angle)
        {
            if (GrabNow != Grab.None) EndLive();
            FlipNow = Flip.None;
            FlipDouble = false;
            Spun(spin);
            if (vert && Combo.Count == _airFrom && Mathf.Abs(spin) < 90f && air > 0.6f) _airLast = Add(Key("air"), T(Key("air")), 150f);   // a plain vert air counts
            bool tricks = Combo.Count > _airFrom || Mathf.Abs(spin) >= 170f;
            if (tricks && angle <= 12f)
            {
                for (int i = _airFrom; i < Combo.Count; i++) Combo[i].Points *= 1.5f;
                Tell(T("skateboards_perfect"), GameUi.Good);
            }
            else if (tricks && angle >= Plugin.SloppyAngle.Value)
            {
                for (int i = _airFrom; i < Combo.Count; i++) Combo[i].Points *= 0.75f;
                Tell(T("skateboards_sloppy"), GameUi.Warn);
            }
            if (air >= 1.3f) Gap("bigair", 250f);
            if (Mathf.Abs(spin) >= 1250f) Gap("megaollie", 1000f);
        }

        public static void LandedOnRail(Player p, float spin, float air)
        {
            if (GrabNow != Grab.None) EndLive();
            FlipNow = Flip.None;
            FlipDouble = false;
            Spun(spin);
        }

        private static void Spun(float spin)
        {
            int halves = Mathf.RoundToInt(Mathf.Abs(spin) / 180f);
            if (halves <= 0) return;
            // frontside: turning toward the chest (regular riders face to the right of the nose: clockwise from above)
            bool front = (spin > 0f) != Plugin.Goofy.Value;
            string deg = (halves * 180).ToString(CultureInfo.InvariantCulture);
            string side = T(front ? "skateboards_frontside" : "skateboards_backside");
            _spinMultiplier += 0.5f * halves;
            if (_airLast != null)
            {
                _airLast.Name = side + " " + deg + " " + _airLast.Name;
                _airLast.Key += "_" + deg;
            }
            else _airLast = Add("skateboards_spin_" + deg, side + " " + deg, 50f * halves);   // (a spin on its own: a few points, and the multiplier)
            Fill(halves * 100f);
        }

        /// <summary>A gap: its points and one more on the multiplier.</summary>
        public static void Gap(string key, float points)
        {
            string name = T("skateboards_gap_" + key);
            Add("skateboards_gap_" + key, name, points);
            Tell(name + "!", GameUi.Gold);
            Skate.Note("gap " + key);
        }

        public static void Revert()
        {
            Add(Key("revert"), T(Key("revert")), 100f);
            _manualsInARow = 0;
            Skate.Note("revert");
        }

        public static void StartGrind(Grind kind, bool ledge)
        {
            GrindNow = kind;
            _manualsInARow = 0;
            _live = Add(Key(kind), T(Key(kind)), GrindPoints(kind));
            _liveRate = GrindRate(kind);
        }

        public static void EndGrind()
        {
            _live = null;
            GrindNow = Grind.None;
        }

        public static void StartManual(bool nose)
        {
            Manual = true;
            NoseManual = nose;
            _manualsInARow++;
            string key = nose ? "skateboards_trick_nosemanual" : "skateboards_trick_manual";
            _live = Add(key, T(key), nose ? 150f : 100f);
            _liveRate = nose ? 200f : 150f;
        }

        public static void EndManual()
        {
            Manual = NoseManual = false;
            _live = null;
        }

        /// <summary>Rolled away clean: the combo's points times its multiplier go on the score.</summary>
        public static void Bank(Player p)
        {
            if (Combo.Count == 0) return;
            long total = (long)Mathf.Round(Points * Multiplier);
            LastBankPoints = total;
            LastBankAt = Time.time;
            Session += total;
            bool best = total > LoadBest(p);
            if (best) { BestCombo = total; SaveBest(p, total); }
            Skate.Note($"banked {total} ({Combo.Count} tricks, x{Multiplier:0.#}){(best ? " best" : "")}");
            Skill.Raise(p, Mathf.Clamp(total / 1500f, 0.1f, 3f));
            if (best && total >= 2000) p.Message(MessageHud.MessageType.Center, T("skateboards_best_combo", Format(total)));
            Combo.Clear();
            _spinMultiplier = 0f;
            _live = _airLast = _flipEntry = null;
            _manualsInARow = 0;
        }

        public static void Lose(Player p, string why)
        {
            LastBailText = why;
            LastBailAt = Time.time;
            Skate.Note($"lost {Mathf.Round(Points)} x {Multiplier:0.#}");
            Clear();
            Special = 0f;
            SpecialOn = false;
        }

        // ---- shown ----

        /// <summary>The combo's tricks joined (the last few), as the score shows them.</summary>
        public static string Text(int last = 6)
        {
            if (Combo.Count == 0) return "";
            IEnumerable<Entry> shown = Combo.Count > last ? Combo.Skip(Combo.Count - last) : Combo;
            string s = string.Join(" + ", shown.Select(e => e.Name));
            return Combo.Count > last ? "... + " + s : s;
        }

        public static string Format(long n) => n.ToString("#,0", CultureInfo.InvariantCulture);

        private static long LoadBest(Player p)
        {
            if (p == null) return BestCombo;
            Dictionary<string, string> data = p.m_customData;
            if (data != null && data.TryGetValue(BestKey, out string s) && long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out long v)) BestCombo = System.Math.Max(BestCombo, v);
            return BestCombo;
        }

        private static void SaveBest(Player p, long v)
        {
            if (p?.m_customData != null) p.m_customData[BestKey] = v.ToString(CultureInfo.InvariantCulture);
        }

        public static long Best(Player p) => LoadBest(p);
    }
}
