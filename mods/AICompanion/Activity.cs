using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using UnityEngine;

namespace AICompanion
{
    /// <summary>
    /// A running record of what each companion does, for finding out why it did something: every change in what it is doing, what it
    /// remembers (ate, made, gave up on), every hit it takes and what dealt it, what it said, and a snapshot of its health, stamina, food
    /// and bag every 30 seconds. Kept in memory (the last 400 lines each, for Claude Tools: "companion debug") and written to
    /// BepInEx/AICompanion/activity.log (started again when it passes 2 MB, the old one kept as activity.old.log). On the game that runs it.
    /// </summary>
    internal static class Activity
    {
        private static readonly Dictionary<long, List<string>> Lines = new Dictionary<long, List<string>>();
        private static string _file;
        private static float _nextSizeCheck;

        public static void Forget() => Lines.Clear();

        public static void Log(Humanoid c, string text)
        {
            if (c == null || string.IsNullOrEmpty(text)) return;
            long id = Companion.IdOf(c);
            string line = $"{DateTime.Now:HH:mm:ss} {text}";
            if (!Lines.TryGetValue(id, out List<string> list)) Lines[id] = list = new List<string>();
            list.Add(line);
            if (list.Count > 400) list.RemoveRange(0, list.Count - 400);
            Write($"{Companion.NameOf(c)}: {line}");
        }

        public static List<string> Recent(Humanoid c, int n) =>
            Lines.TryGetValue(Companion.IdOf(c), out List<string> list) ? list.Skip(Math.Max(0, list.Count - n)).ToList() : new List<string>();

        private static void Write(string line)
        {
            try
            {
                if (_file == null)
                {
                    string dir = Path.Combine(Paths.BepInExRootPath, "AICompanion");
                    Directory.CreateDirectory(dir);
                    _file = Path.Combine(dir, "activity.log");
                }
                if (Time.time >= _nextSizeCheck)
                {
                    _nextSizeCheck = Time.time + 30f;
                    var info = new FileInfo(_file);
                    if (info.Exists && info.Length > 2_000_000)
                    {
                        string old = Path.Combine(info.DirectoryName, "activity.old.log");
                        if (File.Exists(old)) File.Delete(old);
                        File.Move(_file, old);
                    }
                }
                File.AppendAllText(_file, line + Environment.NewLine);
            }
            catch (Exception) { } // a log must never break the companion
        }

        /// <summary>Its vitals in one line: "hp 12/25, st 40/75, food 2 (cooked meat 12m, raspberries 3m), bag 6/32 18kg, goal flint axe".</summary>
        public static string Vitals(Humanoid c, BrainState st)
        {
            var meals = Food.Meals(c);
            Inventory inv = c.GetInventory();
            string food = meals.Count == 0 ? "none" : $"{meals.Count} ({string.Join(", ", meals.Select(m => $"{Localization.instance.Localize(m.Item.m_shared.m_name)} {Mathf.CeilToInt(m.Time / 60f)}m"))})";
            int carriedFood = inv.GetAllItems().Where(Food.IsFood).Sum(i => i.m_stack);
            return $"hp {c.GetHealth():0}/{c.GetMaxHealth():0}, st {Stamina.Get(c):0}/{Stamina.Max(c):0}, food {food}, food in bag {carriedFood}, " +
                   $"bag {inv.NrOfItems()}/{inv.GetWidth() * inv.GetHeight()} {Carry.Weight(c):0}kg, armour {Companion.Armor(c):0}, weapon {Name(Companion.BestMelee(c) ?? Companion.BestRanged(c))}, " +
                   $"order {Companion.OrderOf(c)}, goal {st.Goal?.What ?? "-"}";
        }

        private static string Name(ItemDrop.ItemData i) => i == null ? "none" : Localization.instance.Localize(i.m_shared.m_name);

        /// <summary>Every 30 s on the game running it: a snapshot line.</summary>
        public static void Tick(BrainState st)
        {
            if (Time.time < st.NextSnapshot) return;
            st.NextSnapshot = Time.time + 30f;
            Log(st.Body, "· " + Vitals(st.Body, st));
        }
    }
}
