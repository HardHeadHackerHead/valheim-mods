using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace AICompanion
{
    /// <summary>
    /// Who a companion is, kept with its player's character (Player.m_customData, saved with the character), one per world: name, settings,
    /// looks, bed, kills, and whether it has fallen. The companion's own ZDO holds the live copy; this one is what brings it back when it
    /// falls (the game destroys a dead character's ZDO), so it wakes in its bed like a player, with its name, looks and settings.
    /// Snapshotted from the ZDO every few seconds while the companion is near its player.
    /// </summary>
    internal class Profile
    {
        public long Id, World;
        public string Name = "Rádvar";
        public int Order, Style = 1, Retreat = 30, Kills, Jobs, Radius = 30, Model;
        public bool Potions = true, Protect = true, UseJev = true, Loot = true, Friends, Pantry, Chatty = true, Stow = true, AutoHome = true, HomeSet;
        public Vector3 HomeSpot;
        public string MissionText = "";
        public int PickList = (int)AICompanion.Loot.Default;
        public Vector3 Skin = Vector3.one, HairColor = Vector3.one;
        public int Hair, Beard;
        public float SkinT = 0.3f, HairT = 0.5f, HairL = 0.8f;
        public string Skills = "";
        public string Kept = "";
        public string JournalText = "", TallyText = "";
        public int Since = -1;   // the day it joined (Journal)   // the gear it wore when it fell, to put back on when it wakes (Companion.Pack)
        public bool HasBed;
        public Vector3 Bed, LastSeen;
        public bool Dead, HasGrave;
        public double DiedAt;     // world time (seconds) when it fell
        public Vector3 DiedPos;

        private const string Key = "dhc_companions";

        // ---- stored with the player's character --------------------------------------------------------------

        public static long WorldId => ZNet.instance != null ? ZNet.instance.GetWorldUID() : 0L;

        public static List<Profile> All(Player p)
        {
            var list = new List<Profile>();
            if (p == null || !p.m_customData.TryGetValue(Key, out string json) || string.IsNullOrEmpty(json)) return list;
            try { foreach (JToken t in JArray.Parse(json)) list.Add(FromJson((JObject)t)); }
            catch (Exception e) { Plugin.Instance?.Warn("Could not read the companion profiles: " + e.Message); }
            return list;
        }

        public static List<Profile> Here(Player p) => All(p).Where(x => x.World == WorldId).ToList();

        public static Profile Find(Player p, long id) => Here(p).FirstOrDefault(x => x.Id == id);

        public static void Save(Player p, Profile profile)
        {
            List<Profile> all = All(p);
            all.RemoveAll(x => x.Id == profile.Id);
            all.Add(profile);
            Write(p, all);
        }

        public static void Forget(Player p, long id)
        {
            List<Profile> all = All(p);
            if (all.RemoveAll(x => x.Id == id) > 0) Write(p, all);
        }

        private static void Write(Player p, List<Profile> all) => p.m_customData[Key] = new JArray(all.Select(x => (JToken)x.ToJson())).ToString(Newtonsoft.Json.Formatting.None);

        // ---- to and from the companion's ZDO ------------------------------------------------------------------

        /// <summary>The live companion's settings and looks, as a profile.</summary>
        public static Profile Of(Humanoid c)
        {
            ZDO z = Companion.Zdo(c);
            return new Profile
            {
                Id = Companion.IdOf(c), World = WorldId, Name = Companion.NameOf(c),
                Order = z.GetInt(Keys.Order, 0), Style = z.GetInt(Keys.Style, 1), Retreat = z.GetInt(Keys.Retreat, 30), Kills = z.GetInt(Keys.Kills, 0),
                Jobs = z.GetInt(Keys.Jobs, 0), Radius = z.GetInt(Keys.Radius, 30),
                Potions = z.GetBool(Keys.Potions, true), Protect = z.GetBool(Keys.Protect, true), UseJev = z.GetBool(Keys.UseJev, true),
                Model = z.GetInt(ZDOVars.s_modelIndex, 0), Skin = z.GetVec3(ZDOVars.s_skinColor, Vector3.one), HairColor = z.GetVec3(ZDOVars.s_hairColor, Vector3.one),
                Hair = z.GetInt(ZDOVars.s_hairItem, 0), Beard = z.GetInt(ZDOVars.s_beardItem, 0),
                SkinT = Looks.SkinTone(c), HairT = Looks.HairTone(c), HairL = Looks.HairShade(c), Skills = z.GetString(Skill.Key, ""),
                HasBed = z.GetBool(Keys.HasBed, false), Bed = z.GetVec3(Keys.BedPos, Vector3.zero), LastSeen = c.transform.position,
                HasGrave = z.GetBool(Grave.HasKey, false), DiedPos = z.GetVec3(Grave.PosKey, Vector3.zero), Loot = z.GetBool(AICompanion.Loot.Key, true), Friends = z.GetBool(Keys.Friends, false), Kept = z.GetString(Companion.KeptKey, ""), JournalText = z.GetString(Journal.EntriesKey, ""), TallyText = z.GetString(Journal.TallyKey, ""), Since = z.GetInt(Journal.SinceKey, -1), Pantry = z.GetBool(Work.PantryKey, false), HomeSet = z.GetBool(Work.HomeSetKey, false), HomeSpot = z.GetVec3(Work.HomeSpotKey, Vector3.zero), MissionText = z.GetString(Missions.Key, ""), Stow = z.GetBool(Work.StowKey, true), AutoHome = z.GetBool(Following.AutoHomeKey, true), Chatty = z.GetBool(Talk.ChattyKey, true), PickList = z.GetInt(AICompanion.Loot.ListKey, (int)AICompanion.Loot.Default),
            };
        }

        /// <summary>Give a freshly made companion this profile's id, name, settings and looks (its game must own it).</summary>
        public void ApplyTo(Humanoid c)
        {
            ZDO z = Companion.Zdo(c);
            z.Set(Keys.Id, Id);
            z.Set(Keys.Name, Name);
            z.Set(Keys.Order, Order); z.Set(Keys.Style, Style); z.Set(Keys.Retreat, Retreat); z.Set(Keys.Kills, Kills);
            z.Set(Keys.Jobs, Jobs); z.Set(Keys.Radius, Radius);
            z.Set(Keys.Potions, Potions); z.Set(Keys.Protect, Protect); z.Set(Keys.UseJev, UseJev);
            z.Set(Keys.HasBed, HasBed); z.Set(Keys.BedPos, Bed);
            z.Set(Skill.Key, Skills ?? "");
            z.Set(Grave.HasKey, HasGrave); z.Set(Grave.PosKey, DiedPos);
            z.Set(AICompanion.Loot.Key, Loot); z.Set(Keys.Friends, Friends); z.Set(AICompanion.Loot.ListKey, PickList); z.Set(Work.PantryKey, Pantry); z.Set(Work.HomeSetKey, HomeSet); z.Set(Work.HomeSpotKey, HomeSpot); z.Set(Missions.Key, MissionText ?? ""); z.Set(Work.StowKey, Stow); z.Set(Journal.EntriesKey, JournalText ?? ""); z.Set(Journal.TallyKey, TallyText ?? ""); z.Set(Journal.SinceKey, Since); z.Set(Following.AutoHomeKey, AutoHome); z.Set(Talk.ChattyKey, Chatty);
            VisEquipment vis = c.GetComponent<VisEquipment>();
            if (vis == null) return;
            vis.SetModel(Model);
            vis.SetSkinColor(Skin);
            vis.SetHairColor(HairColor);
            z.Set("dhc_skin_t", SkinT); z.Set("dhc_hair_t", HairT); z.Set("dhc_hair_l", HairL);
            Looks.SetHair(c, Hair);   // (also marks "no hair", or the game would give a bald one random hair)
            Looks.SetBeard(c, Beard);
        }

        // ---- json ---------------------------------------------------------------------------------------------

        private static JArray V(Vector3 v) => new JArray(Math.Round(v.x, 3), Math.Round(v.y, 3), Math.Round(v.z, 3));
        private static Vector3 V(JToken t) => t is JArray a && a.Count == 3 ? new Vector3((float)a[0], (float)a[1], (float)a[2]) : Vector3.zero;

        public JObject ToJson() => new JObject
        {
            ["id"] = Id.ToString(CultureInfo.InvariantCulture), ["world"] = World.ToString(CultureInfo.InvariantCulture), ["name"] = Name,
            ["order"] = Order, ["style"] = Style, ["retreat"] = Retreat, ["kills"] = Kills, ["jobs"] = Jobs, ["radius"] = Radius,
            ["potions"] = Potions, ["protect"] = Protect, ["jev"] = UseJev,
            ["model"] = Model, ["skin"] = V(Skin), ["haircolor"] = V(HairColor), ["hair"] = Hair, ["beard"] = Beard,
            ["skint"] = SkinT, ["hairt"] = HairT, ["hairl"] = HairL, ["skills"] = Skills ?? "",
            ["hasbed"] = HasBed, ["bed"] = V(Bed), ["seen"] = V(LastSeen),
            ["dead"] = Dead, ["diedat"] = DiedAt, ["diedpos"] = V(DiedPos), ["grave"] = HasGrave, ["loot"] = Loot, ["friends"] = Friends, ["pick"] = PickList, ["pantry"] = Pantry, ["homeset"] = HomeSet, ["homespot"] = V(HomeSpot), ["missions"] = MissionText ?? "", ["stow"] = Stow, ["autohome"] = AutoHome, ["kept"] = Kept ?? "", ["journal"] = JournalText ?? "", ["tally"] = TallyText ?? "", ["since"] = Since, ["chatty"] = Chatty,
        };

        private static Profile FromJson(JObject o) => new Profile
        {
            Id = long.Parse((string)o["id"], CultureInfo.InvariantCulture), World = long.Parse((string)o["world"], CultureInfo.InvariantCulture),
            Name = (string)o["name"] ?? "Rádvar",
            Order = (int?)o["order"] ?? 0, Style = (int?)o["style"] ?? 1, Retreat = (int?)o["retreat"] ?? 30, Kills = (int?)o["kills"] ?? 0,
            Jobs = (int?)o["jobs"] ?? 0, Radius = (int?)o["radius"] ?? 30,
            Potions = (bool?)o["potions"] ?? true, Protect = (bool?)o["protect"] ?? true, UseJev = (bool?)o["jev"] ?? true,
            Model = (int?)o["model"] ?? 0, Skin = V(o["skin"]), HairColor = V(o["haircolor"]), Hair = (int?)o["hair"] ?? 0, Beard = (int?)o["beard"] ?? 0,
            SkinT = (float?)o["skint"] ?? 0.3f, HairT = (float?)o["hairt"] ?? 0.5f, HairL = (float?)o["hairl"] ?? 0.8f, Skills = (string)o["skills"] ?? "",
            HasBed = (bool?)o["hasbed"] ?? false, Bed = V(o["bed"]), LastSeen = V(o["seen"]),
            Dead = (bool?)o["dead"] ?? false, DiedAt = (double?)o["diedat"] ?? 0, DiedPos = V(o["diedpos"]),
            HasGrave = (bool?)o["grave"] ?? false, Loot = (bool?)o["loot"] ?? true, Friends = (bool?)o["friends"] ?? false, Kept = (string)o["kept"] ?? "", JournalText = (string)o["journal"] ?? "", TallyText = (string)o["tally"] ?? "", Since = (int?)o["since"] ?? -1, Pantry = (bool?)o["pantry"] ?? false, HomeSet = (bool?)o["homeset"] ?? false, HomeSpot = V(o["homespot"]), MissionText = (string)o["missions"] ?? "", Stow = (bool?)o["stow"] ?? true, AutoHome = (bool?)o["autohome"] ?? true, Chatty = (bool?)o["chatty"] ?? true, PickList = (int?)o["pick"] ?? (int)AICompanion.Loot.Default,
        };
    }
}
