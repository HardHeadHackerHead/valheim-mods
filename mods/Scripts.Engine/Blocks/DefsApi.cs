using System;
using System.Collections.Generic;
using System.Linq;

namespace Scripts.Blocks
{
    /// <summary>The blocks for the host's API: the newer events, what they tell, what a player or creature can be asked, and the actions (docs/scripting-expansion.md, sections 1 to 4 and 7).</summary>
    internal static partial class Defs
    {
        private static void Api()
        {
            // ---------------------------------------------------------------- events (hats)
            Grp("Fights");
            Hat("on_hit", "when something is hit", "", (g, b) => Handler(g, b, "on(\"hit\", e => {"));
            Hat("on_tamed", "when a creature is tamed", "", (g, b) => Handler(g, b, "on(\"tamed\", e => {"));
            Grp("Building and items");
            Hat("on_piece_built", "when a player builds something", "", (g, b) => Handler(g, b, "on(\"pieceBuilt\", e => {"));
            Hat("on_pickup", "when a player picks something up", "", (g, b) => Handler(g, b, "on(\"itemPickup\", e => {"));
            Hat("on_eat", "when a player eats", "", (g, b) => Handler(g, b, "on(\"eat\", e => {"));
            Hat("on_skill_up", "when a skill goes up", "", (g, b) => Handler(g, b, "on(\"skillUp\", e => {"));
            Grp("Trees and wood");
            Hat("on_tree_felled", "when a tree falls", "", (g, b) => Handler(g, b, "on(\"treeFelled\", e => {"));
            Hat("on_log_broken", "when a log breaks", "", (g, b) => Handler(g, b, "on(\"logBroken\", e => {"));
            Grp("The world");
            Hat("on_raid_start", "when a raid begins", "", (g, b) => Handler(g, b, "on(\"raidStart\", e => {"));
            Hat("on_raid_end", "when a raid ends", "", (g, b) => Handler(g, b, "on(\"raidEnd\", e => {"));
            Hat("on_weather_change", "when the weather changes", "", (g, b) => Handler(g, b, "on(\"weatherChange\", e => {"));
            Hat("on_respawn", "when a player comes back after dying", "", (g, b) => Handler(g, b, "on(\"playerRespawn\", e => {"));
            Grp("Commands");
            Hat("on_command", "when someone types /{name:text:hello}", "", (g, b) =>
            {
                string name = g.CommandName(b, "name");
                g.Open(b, "command(" + name + ", (_cp, _ca) => {");
                g.Line(b, "const e = { player: _cp, args: _ca, name: " + name + " };");
                g.Body(b, "do");
                g.Close("});");
            });

            // ---------------------------------------------------------------- what an event tells
            Grp("This event");
            Value("ev_attacker", "actor", "the attacker", null, "e.attacker", Under("on_hit"));
            Value("ev_target", "actor", "the one hit", null, "e.target", Under("on_hit"));
            Value("ev_damage", "number", "the damage", null, "e.damage", Under("on_hit"));
            Value("ev_weapon", "text", "the weapon used", null, "e.weapon", Under("on_hit"));
            Value("ev_piece", "piece", "the piece built", null, "e.piece", Under("on_piece_built"));
            Value("ev_item", "item", "the item", null, "e.item", Under("on_pickup", "on_eat"));
            Value("ev_skill", "text", "the skill", null, "e.skill", Under("on_skill_up"));
            Value("ev_level", "number", "the skill's new level", null, "e.level", Under("on_skill_up"));
            Value("ev_event_name", "text", "the raid's name", null, "e.name", Under("on_raid_start", "on_raid_end"));
            Value("ev_from", "text", "the weather before", null, "e.from", Under("on_weather_change"));
            Value("ev_to", "text", "the weather now", null, "e.to", Under("on_weather_change"));
            Value("ev_args", "list", "the words typed after the command", null, "e.args", Under("on_command"));
            Value("ev_arg", "text", "word {n:num:1:1:20} typed after the command", null, "_bItem(e.args, @n)", Under("on_command"));
            Value("ev_creature", "creature", "the creature tamed", null, "e.creature", Under("on_tamed"));
            Value("ev_wood_kind", "text", "the kind of tree (beech, birch, fir, oak, pine...)", null, "e.kind", Under("on_tree_felled", "on_log_broken"));
            Value("ev_wood", "text", "the wood it gives (Wood, FineWood, RoundLog...)", null, "e.wood", Under("on_tree_felled", "on_log_broken"));
            Value("ev_wood_name", "text", "its name in the game", null, "(e.tree || e.log)", Under("on_tree_felled", "on_log_broken"));
            Value("ev_splits", "bool", "it splits into smaller logs", null, "e.splits", Under("on_log_broken"));
            Value("item_of", "any", "{field:pick:itemfields} of {item:sock:item}", null, "_bField(@item, @field)", null, "values");
            Value("piece_kind", "text", "kind of {p:sock:piece}", null, "_bField(@p, \"prefab\")", null, "values");

            Add(new BlockDef
            {
                Id = "set_damage", Category = "actions", Group = "Fights", Shape = Shape.Stack, Needs = "combat", Scope = Under("on_hit"),
                Label = "set the damage to {n:num:0:0:1000}", Emit = (g, b) => g.Line(b, g.Tpl(b, "e.damage = @n;")),
            });
            Add(new BlockDef
            {
                Id = "multiply_damage", Category = "actions", Group = "Fights", Shape = Shape.Stack, Needs = "combat", Scope = Under("on_hit"),
                Label = "multiply the damage by {n:num:2:0:10}", Emit = (g, b) => g.Line(b, g.Tpl(b, "e.damage = _bNum(e.damage) * @n;")),
            });

            // ---------------------------------------------------------------- players and creatures: what they can be asked
            Grp("Stats");
            Fn("stamina_of", "players", "number", "stamina of {who:sock:actor}", null, (g, b) => g.Tpl(b, "_bStat(@who, \"stamina\")"));
            Fn("max_stamina_of", "players", "number", "most stamina of {who:sock:actor}", null, (g, b) => g.Tpl(b, "_bStat(@who, \"maxStamina\")"));
            Fn("speed_of", "players", "number", "speed of {who:sock:actor} (metres a second)", null, (g, b) => g.Tpl(b, "_bStat(@who, \"speed\")"));
            Fn("deaths_of", "players", "number", "times {who:sock:player} has died", null, (g, b) => g.Tpl(b, "_bStat(@who, \"deaths\")"));
            Fn("skill_of", "players", "number", "{skill:pick:skills} skill of {who:sock:player}", null, (g, b) => g.Tpl(b, "_bSkill(@who, @skill)"));
            Fn("faction_of", "players", "text", "side of {who:sock:actor} (players, wild, boss, tamed)", null, (g, b) => g.Tpl(b, "_bText(@who, \"faction\")"));
            Grp("What they are doing");
            Fn("is_swimming", "players", "bool", "{who:sock:actor} is swimming", null, (g, b) => g.Tpl(b, "_bFlag(@who, \"isSwimming\")"));
            Fn("is_in_water", "players", "bool", "{who:sock:actor} is in water", null, (g, b) => g.Tpl(b, "_bFlag(@who, \"isInWater\")"));
            Fn("is_sitting", "players", "bool", "{who:sock:actor} is sitting", null, (g, b) => g.Tpl(b, "_bFlag(@who, \"isSitting\")"));
            Fn("is_running", "players", "bool", "{who:sock:actor} is running", null, (g, b) => g.Tpl(b, "_bFlag(@who, \"isRunning\")"));
            Fn("is_blocking", "players", "bool", "{who:sock:actor} is blocking", null, (g, b) => g.Tpl(b, "_bFlag(@who, \"isBlocking\")"));
            Fn("facing_of", "players", "pos", "the way {who:sock:actor} faces", null, (g, b) => g.Tpl(b, "_bProp(@who, \"facing\")"));
            Fn("looking_at", "players", "pos", "the place {who:sock:player} is looking at", null, (g, b) => g.Tpl(b, "_bLook(@who)"));
            Fn("ahead_of", "players", "pos", "the place {m:num:5:-50:50} metres in front of {who:sock:actor}", null, (g, b) => g.Tpl(b, "_bAhead(@who, @m)"));
            Grp("What they carry");
            Fn("equipped_in", "players", "text", "what {who:sock:player} holds in {slot:pick:slots}", null, (g, b) => g.Tpl(b, "_bEquip(@who, @slot)"));
            Fn("inventory_of", "players", "list", "everything {who:sock:player} carries (a list)", "items", (g, b) => g.Tpl(b, "_bInv(@who)"));
            Grp("Time");
            Fn("real_hour", "world", "number", "the real hour (on the clock)", null, (g, b) => "clock().hour");
            Fn("real_minute", "world", "number", "the real minute (on the clock)", null, (g, b) => "clock().minute");

            // ---------------------------------------------------------------- what is nearby (lists)
            Grp("Nearby");
            Fn("nearby_creatures", "world", "list", "creatures within {r:num:20:1:50} of {pos:sock:pos}", null, (g, b) => g.Tpl(b, "_bNear(@pos, @r)"));
            Fn("nearby_players", "world", "list", "players within {r:num:20:1:100} of {pos:sock:pos}", null, (g, b) => g.Tpl(b, "_bNearP(@pos, @r)"));
            Fn("nearby_pieces", "world", "list", "built pieces within {r:num:20:1:50} of {pos:sock:pos}", null, (g, b) => g.Tpl(b, "_bNearPieces(@pos, @r)"));
            Fn("spawned_creature", "world", "creature", "the creature just spawned", null, (g, b) => "_bLastSpawn");

            // ---------------------------------------------------------------- actions on players and creatures
            Grp("Messages");
            Stack("talk", "players", "{who:sock:actor} says {text:text:Hello!} in a speech bubble", "messages", "_bTalk(@who, @text)");
            Stack("banner_to", "effects", "show {who:sock:player} the banner {text:text:Welcome}", "messages", "_bBannerTo(@who, @text)");
            Stack("banner_all", "effects", "show everyone the banner {text:text:The hunt begins}", "messages", "banner(@text)");
            Stack("ping", "effects", "ping the map at {pos:sock:pos} saying {text:text:Here!}", "messages", "_bPing(@pos, @text)");
            Stack("name_creature", "players", "call {who:sock:creature} {text:text:Fluffy}", "messages", "_bNameC(@who, @text)");
            Grp("Health and effects");
            Stack("set_stamina", "players", "set the stamina of {who:sock:player} to {n:num:100:0:1000}", "combat", "_bStam(@who, @n)");
            Stack("apply_effect", "players", "give {who:sock:actor} the effect {effect:pick:effectsAll} for {n:num:30:5:600} seconds", "combat", "_bApply(@who, @effect, @n)");
            Stack("remove_effect", "players", "take the effect {effect:pick:effectsAll} off {who:sock:actor}", "combat", "_bUnapply(@who, @effect)");
            Grp("Movement");
            Stack("launch", "players", "launch {who:sock:actor} {dir:pick:launchdirs} with force {n:num:8:0:15}", "combat", "_bLaunchDir(@who, @dir, @n)", danger: true);
            Stack("launch_away", "players", "launch {who:sock:actor} away from {from:sock:pos} with force {n:num:8:0:15}", "combat", "_bLaunchAway(@who, @from, @n)", danger: true);
            Grp("Creatures");
            Stack("tame", "players", "tame {who:sock:creature}", "spawn", "_bTame(@who)", danger: true);
            Stack("remove_creature", "players", "remove {who:sock:creature}", "spawn", "_bRemoveC(@who)");
            Add(new BlockDef
            {
                Id = "spawn_into", Category = "world", Group = "Creatures and items", Shape = Shape.Stack, Needs = "spawn",
                Label = "spawn a {stars:num:1:1:3}-star {creature:pick:creatures} near {pos:sock:pos} and keep it in {v:pick:var}",
                Emit = (g, b) => g.Line(b, g.VarId(b, "v") + " = " + g.Tpl(b, "_bSpawn(@creature, @pos, @stars)") + ";"),
            });

            // ---------------------------------------------------------------- sights and sounds
            Grp("Sights and sounds");
            Stack("play_effect", "effects", "play the effect {effect:pick:visualEffects} at {pos:sock:pos}", "effects", "_bFx(@effect, @pos)");
            Stack("play_sound", "effects", "play the sound {sound:pick:sounds} at {pos:sock:pos}", "effects", "_bSound(@sound, @pos)");
            Stack("lightning", "effects", "strike lightning (no damage) at {pos:sock:pos}", "effects", "_bLightning(@pos)");
            Stack("shake", "effects", "shake the screen near {pos:sock:pos} within {r:num:30:1:60} metres, strength {s:num:1:0:3}", "effects", "_bShake(@pos, @r, @s)");

            // ---------------------------------------------------------------- the world
            Grp("Time and weather");
            Stack("set_wind", "world", "set the wind to {deg:num:90:0:360} degrees, strength {s:num:0.8:0:1}, for {n:num:60:10:600} seconds", "weather", "world.setWind(@deg, @s, @n)");
            Grp("Events");
            Stack("start_event", "world", "start the event {name:pick:events}", "spawn", "world.startEvent(@name)", danger: true);
            Stack("stop_event", "world", "stop the current event", "spawn", "world.stopEvent()");
            Grp("Building");
            Stack("place_piece", "world", "place {piece:pick:pieces} at {pos:sock:pos}", "world", "_bPlace(@piece, @pos)", danger: true);
            Add(new BlockDef
            {
                Id = "place_piece_into", Category = "world", Group = "Building", Shape = Shape.Stack, Needs = "world", Danger = true,
                Label = "place {piece:pick:pieces} at {pos:sock:pos} and keep it in {v:pick:var}",
                Emit = (g, b) => g.Line(b, g.VarId(b, "v") + " = " + g.Tpl(b, "_bPlace(@piece, @pos)") + ";"),
            });
            Stack("remove_piece", "world", "remove {p:sock:piece} (only one this script placed)", "world", "_bRemoveP(@p)", danger: true);
            Grp("Outside the game");
            Stack("send_web", "actions", "send {text:text:Hello} to the web address named {endpoint:text:discord}", "net", "net.send(@endpoint, @text)", danger: true);
        }
    }
}
