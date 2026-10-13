using System.Collections.Generic;
using System.Linq;

namespace Scripts.Blocks
{
    /// <summary>
    /// The example scripts that come with the mod, as block programs: the first gallery (docs/scripting-design.md). The file name is the
    /// script's; Examples.Install writes each as NAME.blocks.json and the NAME.js made from it, never over a file a player has changed.
    /// Built in code with the same Block calls the editor uses, so a change to a block is caught by the tests, not by a player.
    /// </summary>
    internal static class BlockExamples
    {
        internal static List<KeyValuePair<string, Program>> All()
        {
            return new List<KeyValuePair<string, Program>>
            {
                new KeyValuePair<string, Program>("lucky-boars.js", LuckyBoars()),
                new KeyValuePair<string, Program>("welcome-wagon.js", WelcomeWagon()),
                new KeyValuePair<string, Program>("boss-announcer.js", BossAnnouncer()),
                new KeyValuePair<string, Program>("kill-streaks.js", KillStreaks()),
                new KeyValuePair<string, Program>("weather-event.js", WeatherEvent()),
                new KeyValuePair<string, Program>("wild-hunt.js", WildHunt()),
                new KeyValuePair<string, Program>("meteor-shower.js", MeteorShower()),
                new KeyValuePair<string, Program>("treasure-hunt.js", TreasureHunt()),
            };
        }

        // ---- small builders ----

        private static Block B(string type, params string[] fields)
        {
            var b = new Block(type);
            for (int i = 0; i + 1 < fields.Length; i += 2) b.Fields[fields[i]] = fields[i + 1];
            return b;
        }

        private static Block With(Block b, string key, Block child) { b.Inputs[key] = child; return b; }

        private static Block Inside(Block b, params Block[] children) { b.Body("do").AddRange(children); return b; }

        private static Program Make(string name, string about, params Block[] hats)
        {
            var p = new Program { Name = name, About = about };
            float y = 30f;
            foreach (Block h in hats)
            {
                var top = new Program.Top { X = 30f, Y = y };
                top.Chain.Add(h);
                p.Tops.Add(top);
                y += 240f;
            }
            return p;
        }

        private static Block Join(Block a, Block b) => With(With(B("join"), "a", a), "b", b);
        private static Block Join(string a, Block b) => With(B("join", "a", a), "b", b);
        private static Block Words(Block a, string b) => With(B("join", "b", b), "a", a);

        private static Block Say(Block who, Block text) => With(With(B("say"), "who", who), "text", text);

        // ---- the examples ----

        private static Program LuckyBoars() => Make("Lucky Boars", "Boars sometimes drop an extra snack.",
            Inside(B("on_kill", "creature", "Boar"),
                Inside(With(B("if"), "cond", B("chance", "p", "10")),
                    With(B("give", "n", "2", "item", "Honey"), "who", B("ev_killer")),
                    With(B("say", "text", "Lucky boar!"), "who", B("ev_killer")))));

        private static Program WelcomeWagon() => Make("Welcome Wagon", "Greets players by name when they join, and says what day it is.",
            Inside(B("on_join"),
                Say(B("ev_player"),
                    Join("Welcome, ",
                        Join(With(B("name_of"), "who", B("ev_player")),
                            Join("! It is day ",
                                Join(B("day_number"),
                                    Join(", and the weather is ", Words(B("weather_now"), "."))))))))) ;

        private static Program BossAnnouncer() => Make("Boss Announcer", "Rings a bell in the chat when a boss appears or falls.",
            Inside(B("on_boss_appears"),
                With(B("announce"), "text",
                    Join(With(B("name_of"), "who", B("ev_boss")), Join(" has appeared in the ", B("ev_biome"))))),
            Inside(B("on_boss_falls"),
                With(B("announce"), "text", Words(With(B("name_of"), "who", B("ev_boss")), " has fallen!"))));

        private static Block Recall() => With(B("recall", "name", "kills"), "who", B("ev_killer"));

        private static Program KillStreaks() => Make("Kill Streaks", "Counts the creatures each player kills and cheers every five.",
            Inside(B("on_kill", "creature", ""),
                Inside(With(B("if"), "cond",
                        With(With(B("and"), "a", With(B("is_player"), "who", B("ev_killer"))), "b", With(B("not"), "a", With(B("is_player"), "who", B("ev_victim"))))),
                    With(With(B("remember", "name", "kills"), "who", B("ev_killer")), "val", With(B("math", "op", "+", "b", "1"), "a", Recall())),
                    Inside(With(B("if"), "cond", With(B("compare", "op", "=", "b", "0"), "a", With(B("math", "op", "mod", "b", "5"), "a", Recall()))),
                        Say(B("ev_killer"), Words(Recall(), " kills!"))))));

        private static Program WeatherEvent() => Make("Weather Event", "Every 20 minutes the weather turns for a while, and everyone is told.",
            Inside(B("every", "n", "20", "unit", "minutes"),
                B("weather_random", "n", "2"),
                B("announce", "text", "The weather is turning.")));

        // ---- shared builders for the showpieces ----

        private static Block V(string name) => B("var_get", "v", name);
        private static Block SetVar(string name, string value) => B("set_var", "v", name, "val", value);
        private static Block SetVarTo(string name, Block value) => With(B("set_var_any", "v", name), "val", value);
        private static Block SetText(string name, string text) => B("set_var_text", "v", name, "val", text);
        private static Block Change(string name, string by) => B("change_var", "v", name, "n", by);
        private static Block Cmp(Block a, string op, string b) => With(B("compare", "op", op, "b", b), "a", a);
        private static Block CmpV(Block a, string op, Block b) => With(With(B("compare", "op", op), "a", a), "b", b);
        private static Block Mod(Block a, string n) => With(B("math", "op", "mod", "b", n), "a", a);
        private static Block Plus(Block a, string n) => With(B("math", "op", "+", "b", n), "a", a);
        private static Block Each() => B("each_player");
        private static Block Score(Block who) => With(B("recall", "name", "hunt"), "who", who);
        private static Block And(Block a, Block b) => With(With(B("and"), "a", a), "b", b);
        private static Block Or(Block a, Block b) => With(With(B("or"), "a", a), "b", b);
        private static Block Not(Block a) => With(B("not"), "a", a);
        private static Block IsPlayer(Block who) => With(B("is_player"), "who", who);
        private static Block If(Block cond, params Block[] body) => Inside(With(B("if"), "cond", cond), body);
        private static Block Announce(Block text) => With(B("announce"), "text", text);
        private static Block Shout(string text) => B("announce", "text", text);
        private static Block Banner(string text) => B("banner_all", "text", text);
        private static Block Name(Block who) => With(B("name_of"), "who", who);
        private static Block After(string n, string unit, params Block[] body) => Inside(B("after", "n", n, "unit", unit), body);
        private static Block Wait(string seconds) => B("wait", "n", seconds);
        private static Block PosOf(Block who) => With(B("pos_of"), "who", who);
        private static Block Around(string radius, Block pos) => With(B("pos_random", "r", radius), "pos", pos);
        private static Block Lightning(Block pos) => With(B("lightning"), "pos", pos);
        private static Block Shake(Block pos, string range, string strength) => With(B("shake", "r", range, "s", strength), "pos", pos);
        private static Block Ping(Block pos, string text) => With(B("ping", "text", text), "pos", pos);
        private static Block Effect(string name, Block pos) => With(B("play_effect", "effect", name), "pos", pos);
        private static Block Sound(string name, Block pos) => With(B("play_sound", "sound", name), "pos", pos);
        private static Block Talk(Block who, string text) => With(B("talk", "text", text), "who", who);
        private static Block Heal(Block who, string n) => With(B("heal", "n", n), "who", who);
        private static Block Call(string proc, params Block[] args)
        {
            Block c = B("call_proc", "name", proc);
            for (int i = 0; i < args.Length; i++) c.Inputs["a" + (i + 1)] = args[i];
            return c;
        }
        private static Block Param(string name) => B("param", "p", name);
        private static Block Local(string name) => B("local_get", "name", name);
        private static Block LocalVar(string name, Block value) => With(B("local_var", "name", name), "val", value);

        private static Block Define(string name, string[] inputs, params Block[] body)
        {
            Block d = B("define", "name", name);
            for (int i = 0; i < inputs.Length; i++) d.Fields["p" + (i + 1)] = inputs[i];
            return Inside(d, body);
        }

        private static Block IfElse(Block cond, Block[] yes, params Block[] no)
        {
            Block b = Inside(With(B("if_else"), "cond", cond), yes);
            b.Body("else").AddRange(no);
            return b;
        }

        private static Block StarSpawn(string creature, Block pos, string stars = null)
        {
            Block spawn = B("spawn_star", "creature", creature);
            if (stars != null) spawn.Fields["stars"] = stars; else spawn.Inputs["stars"] = V("stars");
            return With(spawn, "pos", pos);
        }

        private static void Place(Program p, Block hat, float x, float y)
        {
            var top = new Program.Top { X = x, Y = y };
            top.Chain.Add(hat);
            p.Tops.Add(top);
        }

        // ---- The Wild Hunt: a five-minute event that starts the moment it runs ----

        private static readonly string[] Kinds = { "Neck", "Boar", "Greyling", "Greydwarf", "Skeleton" };

        /// <summary>The kinds take turns by wave: neck, boar, greyling, greydwarf, skeleton (a hunter near the player the my-block was given).</summary>
        private static Block Rung(int i) => i == Kinds.Length - 1
            ? StarSpawn(Kinds[i], Around("12", Param("who")))
            : IfElse(Cmp(Mod(V("wave"), "5"), "=", i.ToString()), new[] { StarSpawn(Kinds[i], Around("12", Param("who"))) }, Rung(i + 1));

        private static Program WildHunt()
        {
            // my blocks: the pieces the night is made of
            Block thunder = Define("thunder", new string[0],
                Inside(B("for_player"), Lightning(PosOf(Each())), Shake(PosOf(Each()), "40", "1.5")));

            Block findLeader = Define("find the leader", new string[0],
                SetVar("best", "0"),
                SetText("leader", "nobody"),
                Inside(B("for_player"),
                    If(CmpV(Score(Each()), ">", V("best")),
                        With(B("set_var", "v", "best"), "val", Score(Each())),
                        With(B("set_var_text", "v", "leader"), "val", Name(Each())))));

            Block saidLeader = Define("say who leads", new[] { "title" },
                Call("find the leader"),
                If(Cmp(V("best"), ">", "0"),
                    Announce(Join(Param("title"), Join(V("leader"), Join(" with ", Words(V("best"), " kills")))))));

            Block sendHunter = Define("send a hunter", new[] { "who" }, Rung(0));

            Block intro = Define("the intro", new string[0],
                Banner("THE WILD HUNT"),
                Call("thunder"),
                Wait("3"),
                Inside(B("for_player"), Say(Each(), B("join", "a", "Horns echo across the dark meadow..."))),
                Wait("3"),
                Shout("Hold the line. Dawn is three minutes away."),
                Call("thunder"),
                Wait("2"),
                SetVar("count", "3"),
                Inside(B("repeat", "n", "3"), Announce(V("count")), Change("count", "-1"), Wait("1")),
                Banner("THE HUNT BEGINS"));

            Block dawn = Define("dawn", new string[0],
                SetVar("active", "0"),
                Banner("DAWN BREAKS"),
                B("weather", "kind", "clear", "n", "1"),
                B("set_time", "t", "morning"),
                Wait("3"),
                Call("find the leader"),
                IfElse(Cmp(V("best"), ">", "0"),
                    new[] { Announce(Join("MVP: ", Join(V("leader"), Join(" with ", Words(V("best"), " kills!"))))) },
                    Shout("Nobody drew blood tonight.")),
                Wait("3"),
                IfElse(CmpV(V("best"), ">", With(B("recall", "name", "hunt_record"), "who", B("the_world"))),
                    new[]
                    {
                        With(With(B("remember", "name", "hunt_record"), "who", B("the_world")), "val", V("best")),
                        Announce(Join("NEW WORLD RECORD: ", Join(V("best"), Join(" kills by ", V("leader"))))),
                        Inside(B("for_player"),
                            Effect("vfx_Firework_Rocket_Blue", PosOf(Each())),
                            Effect("vfx_Firework_Rocket_Yellow", Around("4", Each())),
                            Sound("sfx_firework_explode", PosOf(Each()))),
                    },
                    Announce(Join("The record to beat: ", Words(With(B("recall", "name", "hunt_record"), "who", B("the_world")), " kills")))),
                Wait("3"),
                Inside(B("for_player"),
                    Say(Each(), Join("You slew ", Words(Score(Each()), " hunters tonight."))),
                    If(Cmp(Score(Each()), ">=", "5"),
                        With(B("give", "n", "5", "item", "Coins"), "who", Each()),
                        Say(Each(), B("join", "a", "Odin rewards you: 5 coins."))),
                    If(Cmp(Score(Each()), ">=", "10"),
                        With(B("give", "n", "5", "item", "Honey"), "who", Each()),
                        Say(Each(), B("join", "a", "A great hunter! 5 honey for the feast."))),
                    Heal(Each(), "100")));

            // when the script starts: the storm, the night, the first hunters, then the whole night's clock in one run of waits
            Block start = Inside(B("on_start"),
                B("weather", "kind", "storm", "n", "6"),
                B("set_time", "t", "midnight"),
                SetVar("wave", "0"), SetVar("active", "1"), SetVar("stars", "1"),
                Inside(B("for_player"),
                    With(B("remember", "name", "hunt", "val", "0"), "who", Each()),
                    Heal(Each(), "100"),
                    With(B("spawn", "creature", "Boar"), "pos", Around("10", Each()))),
                Call("the intro"),
                Wait("38"), Call("say who leads", B("join", "a", "LEADER: ")),
                Wait("40"), Shout("HALFWAY THROUGH THE NIGHT - 90 seconds until dawn!"),
                Wait("30"), Shout("ONE MINUTE until dawn!"),
                Wait("15"), Call("say who leads", B("join", "a", "LEADER: ")),
                Wait("15"), Shout("THIRTY SECONDS - hold on!"),
                Wait("30"), Call("dawn"));

            // every 12 seconds a wave; the pack grows and the stars climb as the night goes on; every fifth wave the Alpha comes with its pack
            Block wave = Inside(B("every", "n", "12", "unit", "seconds"),
                If(Cmp(V("active"), "=", "1"),
                    Change("wave", "1"),
                    SetVar("stars", "1"),
                    If(Cmp(V("wave"), ">=", "3"), SetVar("stars", "2")),
                    If(Cmp(V("wave"), ">=", "7"), SetVar("stars", "3")),
                    SetVar("count", "2"),
                    If(Cmp(V("wave"), ">=", "4"), SetVar("count", "3")),
                    If(Cmp(V("wave"), ">=", "8"), SetVar("count", "4")),
                    If(Cmp(V("wave"), ">=", "12"), SetVar("count", "5")),
                    IfElse(Cmp(Mod(V("wave"), "5"), "=", "0"),
                        new[]
                        {
                            Banner("THE ALPHA HAS ARRIVED"),
                            Inside(B("for_player"),
                                With(B("spawn_into", "stars", "3", "creature", "Greydwarf_Elite", "v", "alpha"), "pos", Around("14", Each())),
                                Effect("fx_summon_start", PosOf(V("alpha"))),
                                Talk(V("alpha"), "You dare enter my forest?"),
                                Ping(PosOf(V("alpha")), "THE ALPHA"),
                                Lightning(PosOf(V("alpha"))),
                                Shake(PosOf(V("alpha")), "40", "2")),
                        },
                        Announce(Join("WAVE ", Join(V("wave"), Join(" - ", Words(V("stars"), " star hunters")))))),
                    Inside(B("for_player"),
                        Inside(With(B("repeat"), "n", V("count")), Call("send a hunter", Each())),
                        If(Cmp(With(B("health_of"), "who", Each()), "<", "15"),
                            Heal(Each(), "60"),
                            Say(Each(), B("join", "a", "Odin's mercy: you are healed."))))));

            // kills count, and every fifth is called out to everyone
            Block kill = Inside(B("on_kill", "creature", ""),
                If(And(Cmp(V("active"), "=", "1"), And(IsPlayer(B("ev_killer")), Not(IsPlayer(B("ev_victim"))))),
                    With(With(B("remember", "name", "hunt"), "who", B("ev_killer")), "val", Plus(Score(B("ev_killer")), "1")),
                    Say(B("ev_killer"), Join("Hunt kills: ", Score(B("ev_killer")))),
                    If(Cmp(Mod(Score(B("ev_killer")), "5"), "=", "0"),
                        Announce(Join(Name(B("ev_killer")), Join(" has slain ", Words(Score(B("ev_killer")), " hunters!")))))));

            // the Alpha is worth three kills, and honey
            Block alpha = Inside(B("on_kill", "creature", "Greydwarf_Elite"),
                If(And(Cmp(V("active"), "=", "1"), IsPlayer(B("ev_killer"))),
                    With(With(B("remember", "name", "hunt"), "who", B("ev_killer")), "val", Plus(Score(B("ev_killer")), "2")),
                    Banner("THE ALPHA IS DEAD"),
                    Call("thunder"),
                    Effect("vfx_Firework_Rocket_Red", PosOf(B("ev_killer"))),
                    Sound("sfx_firework_explode", PosOf(B("ev_killer"))),
                    With(B("give", "n", "2", "item", "Honey"), "who", B("ev_killer")),
                    Say(B("ev_killer"), B("join", "a", "The Alpha is dead: 2 bonus kills and honey."))));

            Block death = Inside(B("on_death"),
                If(Cmp(V("active"), "=", "1"), Announce(Words(Name(B("ev_player")), " has fallen to the Wild Hunt!"))));

            Block join = Inside(B("on_join"),
                If(Cmp(V("active"), "=", "1"), Say(B("ev_player"), B("join", "a", "The Wild Hunt is on! Survive until dawn."))));

            var p = new Program { Name = "The Wild Hunt", About = "A three-minute event that starts the moment it runs: a storm at midnight, thunder and a countdown, waves of hunters that grow stronger, an Alpha every fourth wave, leaderboards, and a world record." };
            foreach (string v in new[] { "wave", "active", "stars", "count", "best", "leader", "alpha" }) p.Vars.Add(v);
            Place(p, start, 30f, 30f);
            Place(p, thunder, 30f, 1200f); Place(p, findLeader, 400f, 1200f); Place(p, saidLeader, 30f, 1400f); Place(p, sendHunter, 400f, 1400f);
            Place(p, intro, 30f, 1700f); Place(p, dawn, 400f, 1700f);
            Place(p, wave, 1000f, 30f); Place(p, kill, 1000f, 900f); Place(p, alpha, 1000f, 1150f); Place(p, death, 1000f, 1450f); Place(p, join, 1000f, 1580f);
            return p;
        }

        // ---- Meteor Shower: markers on the ground, a lightning strike two seconds later, and a score for staying out of the way ----

        private static Program MeteorShower()
        {
            // one meteor for one player: ping where it will land, strike two seconds later, hit whoever is close
            Block meteor = Define("meteor", new[] { "who" },
                LocalVar("spot", Around("10", Param("who"))),
                Ping(Local("spot"), "METEOR INCOMING!"),
                After("2", "seconds",
                    Lightning(Local("spot")),
                    Effect("vfx_FireballHit", Local("spot")),
                    Shake(Local("spot"), "40", "2"),
                    IfElse(With(With(B("list_has"), "x", Param("who")), "l", With(B("nearby_players", "r", "4"), "pos", Local("spot"))),
                        new[]
                        {
                            With(With(B("launch_away", "n", "9"), "who", Param("who")), "from", Local("spot")),
                            With(B("hurt", "n", "3"), "who", Param("who")),
                            Say(Param("who"), B("join", "a", "A meteor hit you!")),
                        },
                        With(With(B("remember", "name", "dodged"), "who", Param("who")), "val", Plus(With(B("recall", "name", "dodged"), "who", Param("who")), "1")))));

            Block start = Inside(B("on_start"),
                SetVar("active", "1"),
                Banner("METEOR SHOWER"),
                B("set_time", "t", "midnight"),
                B("set_wind", "deg", "90", "s", "0.9", "n", "70"),
                Inside(B("for_player"),
                    With(B("remember", "name", "dodged", "val", "0"), "who", Each()),
                    Heal(Each(), "100")),
                Wait("3"),
                Shout("Stay away from the pings. Every meteor you dodge counts."),
                Wait("55"),
                SetVar("active", "0"),
                Banner("THE SKY CLEARS"),
                B("set_time", "t", "morning"),
                Wait("3"),
                Inside(B("for_player"),
                    Say(Each(), Join("You dodged ", Words(With(B("recall", "name", "dodged"), "who", Each()), " meteors."))),
                    If(Cmp(With(B("recall", "name", "dodged"), "who", Each()), ">=", "8"),
                        With(B("give", "n", "5", "item", "Coins"), "who", Each()),
                        Talk(Each(), "Not a scratch!"),
                        Shake(PosOf(Each()), "20", "1"))));

            Block rain = Inside(B("every", "n", "4", "unit", "seconds"),
                If(Cmp(V("active"), "=", "1"),
                    Inside(B("for_player"), Call("meteor", Each()))));

            var p = new Program { Name = "Meteor Shower", About = "A minute of meteors: pings show where one will land, lightning strikes two seconds later, and anyone close is flung away and scratched. Dodge eight to win coins." };
            p.Vars.Add("active");
            Place(p, start, 30f, 30f); Place(p, rain, 700f, 30f); Place(p, meteor, 700f, 300f);
            return p;
        }

        // ---- Treasure Hunt: hot-and-cold hints to a hidden chest of gold ----

        private static Block FirstPlayer() => With(B("list_item", "n", "1"), "l", B("players_list"));

        private static Block IfChain(Block c1, Block[] b1, Block c2, Block[] b2, Block c3, Block[] b3, Block[] els)
        {
            Block c = With(B("if_chain"), "c1", c1);
            c.Body("do").AddRange(b1);
            if (c2 != null) { c.Inputs["c2"] = c2; c.Body("do2").AddRange(b2); }
            if (c3 != null) { c.Inputs["c3"] = c3; c.Body("do3").AddRange(b3); }
            c.Body("else").AddRange(els);
            return c;
        }

        private static Block SetLocal(string name, Block value) => With(B("set_local", "name", name), "val", value);

        private static Program TreasureHunt()
        {
            Block start = Inside(B("on_start"),
                SetVar("active", "1"),
                // a hidden place between 25 and 50 metres from the first player
                SetVarTo("spot", Around("50", FirstPlayer())),
                Inside(With(B("repeat_until"), "cond", Cmp(With(With(B("distance_between"), "a", V("spot")), "b", FirstPlayer()), ">=", "25")),
                    SetVarTo("spot", Around("50", FirstPlayer()))),
                Banner("TREASURE HUNT"),
                Shout("A chest of gold is hidden near you. Follow the hints!"),
                Wait("60"),
                If(Cmp(V("active"), "=", "1"),
                    Ping(V("spot"), "Somewhere around here..."),
                    Shout("A hint: the treasure lies near the marker on your map.")));

            // every few seconds each player is told how warm they are, and which way to go
            Block hint = Inside(B("every", "n", "4", "unit", "seconds"),
                If(Cmp(V("active"), "=", "1"),
                    Inside(B("for_player"),
                        LocalVar("d", With(With(B("distance_between"), "a", PosOf(Each())), "b", V("spot"))),
                        LocalVar("way", With(With(B("direction_from"), "a", PosOf(Each())), "b", V("spot"))),
                        LocalVar("compass", B("join", "a", "north")),
                        IfChain(And(Cmp(Local("way"), ">=", "45"), Cmp(Local("way"), "<", "135")), new[] { SetLocal("compass", B("join", "a", "east")) },
                            And(Cmp(Local("way"), ">=", "135"), Cmp(Local("way"), "<", "225")), new[] { SetLocal("compass", B("join", "a", "south")) },
                            And(Cmp(Local("way"), ">=", "225"), Cmp(Local("way"), "<", "315")), new[] { SetLocal("compass", B("join", "a", "west")) },
                            new Block[0]),
                        IfElse(Cmp(Local("d"), "<", "4"),
                            new[]
                            {
                                SetVar("active", "0"),
                                Lightning(V("spot")),
                                Shake(V("spot"), "40", "2"),
                                Effect("vfx_Firework_Rocket_Red", V("spot")),
                                Effect("vfx_Firework_Rocket_Yellow", V("spot")),
                                Sound("sfx_firework_explode", V("spot")),
                                Banner("TREASURE FOUND"),
                                Announce(Words(Name(Each()), " found the treasure!")),
                                With(B("drop_item", "n", "10", "item", "Coins"), "pos", V("spot")),
                                With(B("drop_item", "n", "5", "item", "Honey"), "pos", V("spot")),
                                Ping(V("spot"), "TREASURE!"),
                                With(With(B("remember", "name", "treasures"), "who", B("the_world")), "val", Plus(With(B("recall", "name", "treasures"), "who", B("the_world")), "1")),
                                B("stop_here"),
                            },
                            IfChain(Cmp(Local("d"), "<", "15"), new[] { Say(Each(), Join("Burning hot! Go ", Local("compass"))) },
                                Cmp(Local("d"), "<", "30"), new[] { Say(Each(), Join("Warm... go ", Local("compass"))) },
                                Cmp(Local("d"), "<", "60"), new[] { Say(Each(), Join("Cool. Try ", Local("compass"))) },
                                new[] { Say(Each(), Join("Freezing. Head ", Local("compass"))) })))));

            var p = new Program { Name = "Treasure Hunt", About = "Starts at once: a chest of gold is hidden near the first player. Hot-and-cold hints every few seconds, a map ping after a minute, lightning and a pile of coins when it is found." };
            p.Vars.Add("active"); p.Vars.Add("spot");
            Place(p, start, 30f, 30f); Place(p, hint, 700f, 30f);
            return p;
        }

        /// <summary>The starter script's twin (New blocks makes the same one), so it is not shipped as an example; the tests still run it.</summary>
        internal static Program Hello() => Make("Hello", "The smallest script: answers a player who says hello in chat.",
            Inside(B("on_chat", "text", "hello"),
                Say(B("ev_player"), Join("Hello, ", With(B("name_of"), "who", B("ev_player"))))));
    }
}
