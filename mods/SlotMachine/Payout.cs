using System;
using System.Collections.Generic;

namespace SlotMachine
{
    /// <summary>
    /// What the reels can show and what each result pays. Six symbols with different odds (a coin is common, the Valknut is rare);
    /// three alike pay the most, and a pair of the better symbols pays a little. Over a long time the machine gives back about 93%
    /// of what is put in, so it is a fun way to lose a few coins, not a way to make them.
    /// </summary>
    internal static class Payout
    {
        internal static readonly string[] Names = { "Coin", "Boar", "Horn", "Axe", "Raven", "Valknut" };
        private static readonly int[] Weight = { 7, 5, 4, 2, 1, 1 };            // chance of each symbol on a reel (out of 20)
        private static readonly int[] Three = { 5, 10, 16, 40, 100, 300 };      // pay for three alike, times the bet
        private static readonly int[] Pair = { 1, 0, 0, 0, 5, 10 };             // pay for exactly two alike

        /// <summary>The symbol drawn on each of a reel's eight faces (matches the picture strip).</summary>
        internal static readonly int[] Faces = { 0, 1, 2, 3, 4, 5, 0, 1 };

        internal const int Jackpot = 5; // the Valknut

        /// <summary>Pick a face for each reel by the symbol odds.</summary>
        internal static int[] Roll(System.Random rng)
        {
            var cells = new int[3];
            for (int reel = 0; reel < 3; reel++)
            {
                int pick = rng.Next(20), symbol = 0;
                for (int s = 0; s < Weight.Length; s++) { pick -= Weight[s]; if (pick < 0) { symbol = s; break; } }
                var faces = new List<int>();
                for (int f = 0; f < Faces.Length; f++) if (Faces[f] == symbol) faces.Add(f);
                cells[reel] = faces[rng.Next(faces.Count)];
            }
            return cells;
        }

        /// <summary>How many times the bet comes back for these faces (0 for nothing).</summary>
        internal static int Multiplier(int[] cells)
        {
            int a = Faces[cells[0]], b = Faces[cells[1]], c = Faces[cells[2]];
            if (a == b && b == c) return Three[a];
            if (a == b) return Pair[a];
            if (a == c) return Pair[a];
            if (b == c) return Pair[b];
            return 0;
        }

        internal static bool IsJackpot(int[] cells) => Faces[cells[0]] == Jackpot && Faces[cells[1]] == Jackpot && Faces[cells[2]] == Jackpot;

        /// <summary>One line for the hover text: the best wins.</summary>
        internal static string Summary() =>
            $"3 Valknut {Three[5]}x  ·  3 Raven {Three[4]}x  ·  3 Axe {Three[3]}x  ·  3 Horn {Three[2]}x\n3 Boar {Three[1]}x  ·  3 Coin {Three[0]}x  ·  2 Valknut {Pair[5]}x  ·  2 Raven {Pair[4]}x  ·  2 Coin {Pair[0]}x";
    }
}
