"""
Piece palettes for dungeon authors (python palette_tool.py [theme ...]): for each theme (giant, dvergr, crypt, shared), the game's prefabs
that are known to work in our rooms, each with what an author needs to place it without trying it in the game first, written to
palettes/<theme>.json. Everything but the curation (role, look, mount, scale range, tier, notes: CURATED below) is read from the game's own
files, so it's true for this game version:

  exists        in the game's ZNetScene (a pack may only name these: dungeon_parts.py refuses others)
  size, min/max its shape's box in its own frame (prefab_bounds.json); origin: where its origin sits on that box (foot, top, centre...);
                hollow: a list of boxes in prefab_shapes.json for things you walk inside
  colliders     its non-trigger colliders by layer, and whether a character bumps into it in our rooms: the game's collision matrix
                (PhysicsManager, globalgamemanagers) says which layers characters pass; an item's collider ("item" layer, a small box) is
                made solid and fitted by the Builder (mods/Dungeons/BuildSolid.cs)
  lights        each light: range, intensity, colour, whether it shines once the part is stripped (a Fireplace's burning objects are
                switched on by Strip), LightLod; fire (a Fireplace), particles. Lights and particles grow with the part's scale
                (BuildScale.cs: by the mean of the three factors)
  behaviours    what the game would make it do (an item, a pickable, a chest, a breakable that drops things, a door, a spawner, a seat):
                all stripped when it's scenery (it gives nothing, opens nothing); a networked one may be a Saved part instead
  render        a picture from two sides, in the mesh cache (game_meshes.CACHE, never committed: the game's assets), and one contact
                sheet per theme (<theme>_sheet.png) to see the whole palette at once

Exits 1 when a curated prefab isn't in the game, or a structural piece (wall, floor, ceiling, stair...) has nothing a player bumps into.

    python palette_tool.py                  every theme
    python palette_tool.py giant dvergr     these
    python palette_tool.py --no-render      without pictures (faster)
    (a pack team's own theme: palettes/src/<theme>.json, see load_sources; then python palette_tool.py <theme>)
    python palette_tool.py --probe Rock_ "^piece_jute"    the same facts for any prefabs (names or regular expressions), one JSON
                                            line each, with a picture: for choosing pieces before they're curated into a palette
"""
import json, math, os, sys
import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import game_meshes as gm  # noqa: E402
from dungeon_parts import bounds  # noqa: E402

OUT_DIR = os.path.join(HERE, "palettes")
RENDERS = os.path.join(gm.CACHE, "palettes")
SHAPES = os.path.join(HERE, "prefab_shapes.json")
GAME = gm.GAME

# Roles: what a part is for. Structural ones must be solid.
STRUCTURAL = {"wall", "floor", "ceiling", "block", "pillar", "stair", "ramp", "arch", "rail", "gate", "door", "platform"}

# Mounts: how it must be placed so it doesn't float (prop_check.py checks "ground" ones; "hang" ones need something above, or a chain
# whose foot is on their top; "wall" ones need a wall behind; "span" ones rest on two supports; "structure" ones are what others stand on).
# ---- the curation: role, look, mount, scale range, the land it looks like it belongs to (1 Meadows ... 8 Deep North), notes

def E(prefab, role, look, mount="ground", scale=(0.5, 2.0), tier=None, notes=""):
    return {"prefab": prefab, "role": role, "look": look, "mount": mount, "scale": list(scale), "tier": tier, "notes": notes}


CURATED = {
    "giant": {
        "about": "The Giants' clockwork vault (Shifting Halls): ancient Jotunn machinery at giant scale. Huge grey grausten blocks with black "
                 "marble, bronze gears and iron chains, giant bones and statues, cold blue light. Rooms on an 8 m grid, 12 m a storey, "
                 "doorways 5 x 8 m (tools/dungeons/giant_kit.py has the style sheet and the helpers).",
        "pieces": [
            E("Ashlands_floor_large", "floor", "10 x 2 x 10 grausten slab: floors, ceilings (euler x 180), and stood up, walls", "structure", (0.5, 3), 7),
            E("blackmarble_floor_large", "floor", "8 x 2 x 8 black marble slab: lintels, plinths, inlays", "structure", (0.5, 3), 6),
            E("Ashlands_Boss_Pillar", "pillar", "carved grausten pillar 4.6 x 8 x 4.6, origin at its foot", "ground", (0.5, 2), 7),
            E("Ashlands_Pillar4", "pillar", "plain grausten column", "ground", (0.5, 3), 7),
            E("blackmarble_column_3", "pillar", "2 x 8 x 2 black marble column: door posts", "ground", (0.5, 2), 6),
            E("Piece_flametal_pillar", "pillar", "dark metal pillar", "ground", (0.5, 3), 7),
            E("Morkhalla_Block", "block", "a big carved stone block", "ground", (0.5, 3), 8),
            E("Ashlands_WallBlock", "wall", "grausten wall block", "structure", (0.5, 3), 7),
            E("Ashlands_Fortress_Floor", "floor", "fortress floor tile", "structure", (0.5, 3), 7),
            E("Morkhalla_Floor_4x4", "floor", "Mórk halla floor tile 4 x 4", "structure", (0.5, 3), 8),
            E("piece_grausten_stonestair", "stair", "2 x 1 x 2 grausten stair block, climbs -z", "structure", (1, 1), 7,
              "scale changes the step height: keep 1 (stack them for a wider stair)"),
            E("Morkhalla_Stairs_giant_short", "stair", "giant stone stair with steps a person can climb", "structure", (1, 1), 8),
            E("Ashlands_Arch1", "arch", "grausten arch", "structure", (0.5, 2), 7),
            E("Ashlands_Ruins_twist_ArchBig", "arch", "a big twisted grausten arch", "structure", (0.5, 2), 7),
            E("blackmarble_head_big01", "decor", "a 2 m carved head: keystones", "wall", (0.5, 3), 6),
            E("CharredCogwheel", "gear", "a bronze cogwheel 1.1 m across, lying flat: scaled 5-17 times into the machine's gears", "free", (1, 17), 7,
              "an ITEM prefab: its collider is a small box on the 'item' layer players pass through; the Builder moves it to static_solid and "
              "gives it a disc collider of its shape (BuildSolid.cs). In loot it would be the Ashlands item: never Saved."),
            E("Morkhalla_MetalBar", "beam", "studded iron bar 20 x 1 x 1, centred, along x: axles, lever handles, grilles", "span", (0.2, 2), 8),
            E("Piece_flametal_beam", "beam", "dark metal beam 2 x 0.56: girders, rails, gear spokes", "span", (0.5, 4), 7),
            E("iron_grate", "gate", "iron grate 2 x 3: portcullis", "structure", (0.5, 3), 3),
            E("Ashlands_Fortress_Gate_Door", "door", "studded iron gate leaf 4 x 5.6: sealed doors, the vault gate", "structure", (0.5, 2), 7,
              "as scenery it doesn't open: a gate mechanism moves it (Runtime)"),
            E("Morkhalla_WallChain1", "chain", "an iron chain hanging 4.4 m from a wall anchor", "wall", (0.5, 3), 8),
            E("Morkhalla_Chain", "chain", "a 20 m chain span between two anchors", "span", (0.25, 2), 8),
            E("Morkhalla_giant_railing", "rail", "giant stone balustrade 4 m long", "ground", (0.5, 2), 8),
            E("Morkhalla_giant_railing_corner", "rail", "the balustrade's corner", "ground", (0.5, 2), 8),
            E("Morkhalla_giant_railing_torch", "light", "stone post with a blue flame (range 10)", "ground", (0.5, 3), 8),
            E("MountainKit_brazier_blue", "light", "blue brazier on legs (range 5)", "ground", (0.5, 3), 4),
            E("dverger_demister", "light", "a blue wisp light (range 10), its mesh below its origin: hang it", "hang", (0.5, 2), 6),
            E("blackmarble_altar_crystal", "light", "a cyan crystal on a stand (range 3): accents", "ground", (0.5, 2), 6),
            E("Morkhalla_firepit", "light", "a stone fire pit", "ground", (0.5, 2), 8),
            E("piece_walltorch", "light", "a wall sconce torch, warm orange (the giants' only wall light: an accent against the blue)", "wall", (0.5, 3), 1),
            E("giant_skull", "remains", "a giant's skull", "ground", (0.3, 2), 6),
            E("giant_ribs", "remains", "a giant's ribcage you can walk inside (hollow: prefab_shapes.json)", "ground", (0.3, 1.5), 6),
            E("giant_sword1", "remains", "a giant's sword", "ground", (0.3, 2), 6, "may also hang on a wall (prop_check MOUNTED)"),
            E("giant_helmet1", "remains", "a giant's helmet", "ground", (0.3, 2), 6, "may also hang on a wall (prop_check MOUNTED)"),
            E("giant_arm", "remains", "a giant's arm bone", "ground", (0.3, 2), 6),
            E("ancient_skull", "remains", "a large ancient skull", "ground", (0.3, 2), 6),
            E("Morkhalla_StatuePieceFace_big", "decor", "a Jotunn statue's face", "wall", (0.3, 2), 8),
            E("Morkhalla_StatueSword_hanging", "decor", "a statue's sword, hanging", "hang", (0.3, 2), 8),
            E("Morkhalla_Rubble1", "decor", "stone rubble", "ground", (0.5, 2), 8),
            E("Morkhalla_Rubble2", "decor", "more stone rubble", "ground", (0.5, 2), 8),
            E("Morkhalla_Stonepile", "decor", "a pile of cut stones", "ground", (0.5, 2), 8),
            E("Morkhalla_Bench", "prop", "a giant's stone bench", "ground", (0.5, 2), 8),
            E("Morkhalla_Table", "prop", "a giant's stone table", "ground", (0.5, 2), 8),
            E("Morkhalla_WeaponStand", "prop", "a giant's weapon stand", "ground", (0.5, 2), 8),
            E("Morkhalla_coal_pile_memorial", "decor", "a heap of coal", "ground", (0.5, 2), 8),
            E("MemorialStone_Large", "decor", "a large carved memorial stone", "ground", (0.3, 2), 8),
            E("giant_helmet2", "remains", "a giant's helmet, second look", "ground", (0.3, 2), 6, "may also hang on a wall (prop_check MOUNTED)"),
            E("giant_brain", "remains", "a giant's brain (the Mistlands' remains)", "ground", (0.3, 2), 6),
            E("grausten_pile", "decor", "a pile of grausten blocks", "ground", (0.5, 2), 7),
            E("Morkhalla_Banner1", "decor", "a Mórk halla banner", "wall", (0.5, 2), 8),
        ],
    },
    "dvergr": {
        "about": "A Dvergr hold, the contrast to the giants: human-scale black marble halls held up by Yggdrasil timber, lanterns on hooks "
                 "and chains, crates and tools of a mine; warm orange light against blue mist light. Rooms on a 2 m grid, doorways 2-4 m.",
        "pieces": [
            E("blackmarble_floor", "floor", "black marble floor 2 x 2", "structure", (0.5, 2), 6),
            E("blackmarble_floor_large", "floor", "black marble floor 8 x 8", "structure", (0.5, 2), 6),
            E("blackmarble_tile_floor_2x2", "floor", "tiled black marble floor 2 x 2", "structure", (0.5, 2), 6),
            E("blackmarble_tile_wall_2x2", "wall", "tiled black marble wall 2 x 2", "structure", (0.5, 2), 6),
            E("blackmarble_tile_wall_2x4", "wall", "tiled black marble wall 2 x 4", "structure", (0.5, 2), 6),
            E("blackmarble_2x2x2", "block", "black marble block 2 x 2 x 2", "structure", (0.5, 2), 6),
            E("blackmarble_2x2x1", "block", "black marble block 2 x 2 x 1", "structure", (0.5, 2), 6),
            E("blackmarble_2x1x1", "block", "black marble block 2 x 1 x 1: trim", "structure", (0.5, 2), 6),
            E("blackmarble_1x1", "block", "black marble block 1 x 1", "structure", (0.5, 2), 6),
            E("blackmarble_column_1", "pillar", "black marble column, short", "ground", (0.5, 2), 6),
            E("blackmarble_column_2", "pillar", "black marble column, medium", "ground", (0.5, 2), 6),
            E("blackmarble_column_3", "pillar", "black marble column 8 m", "ground", (0.5, 2), 6),
            E("blackmarble_base_1", "pillar", "column base", "ground", (0.5, 2), 6),
            E("blackmarble_tip", "pillar", "column capital", "ground", (0.5, 2), 6),
            E("blackmarble_arch", "arch", "black marble arch", "structure", (0.5, 2), 6),
            E("blackmarble_stair", "stair", "black marble stair", "structure", (1, 1), 6, "scale changes the step height: keep 1"),
            E("blackmarble_stair_corner", "stair", "black marble corner stair", "structure", (1, 1), 6, "keep 1"),
            E("blackmarble_slope_1x2", "ramp", "black marble slope", "structure", (0.5, 2), 6),
            E("blackmarble_out_1", "decor", "carved black marble edge", "structure", (0.5, 2), 6),
            E("blackmarble_head01", "decor", "a carved Dvergr head", "wall", (0.5, 2), 6),
            E("blackmarble_altar_crystal", "light", "a cyan crystal on a stand (range 3)", "ground", (0.5, 2), 6),
            E("dvergrtown_arch", "arch", "a Dvergr stone and timber arch", "structure", (0.5, 2), 6),
            E("dvergrtown_wood_beam", "beam", "a Yggdrasil timber beam", "span", (0.5, 2), 6),
            E("dvergrtown_wood_pole", "pillar", "a Yggdrasil timber post", "ground", (0.5, 2), 6),
            E("dvergrtown_wood_support", "beam", "a timber prop with braces", "ground", (0.5, 2), 6),
            E("dvergrtown_wood_wall01", "wall", "a Dvergr timber wall", "structure", (0.5, 2), 6),
            E("dvergrtown_wood_stakewall", "decor", "one sharp timber stake (despite the name: not a wall)", "ground", (0.5, 2), 6),
            E("dvergrtown_wood_wall02", "wall", "a Dvergr timber wall, second look", "structure", (0.5, 2), 6),
            E("dvergrtown_wood_wall03", "wall", "a Dvergr timber wall, third look", "structure", (0.5, 2), 6),
            E("dvergrtown_wood_crane", "machine", "a timber crane arm: looks mechanical", "ground", (0.5, 2), 6),
            E("dvergrprops_wood_beam", "beam", "a plain timber beam", "span", (0.5, 3), 6),
            E("dvergrprops_wood_pole", "pillar", "a plain timber pole", "ground", (0.5, 3), 6),
            E("dvergrprops_wood_wall", "wall", "a plain timber wall", "structure", (0.5, 2), 6),
            E("piece_dvergr_spiralstair_right", "stair", "a Dvergr spiral stair, turning right", "structure", (1, 1), 6, "keep 1"),
            E("dvergrtown_secretdoor", "door", "a secret stone door that looks like wall", "structure", (1, 1), 6, "as scenery it doesn't open"),
            E("blackmarble_post01", "pillar", "a black marble post", "ground", (0.5, 2), 6),
            E("blackmarble_tile_wall_1x1", "wall", "tiled black marble wall 1 x 1", "structure", (0.5, 2), 6),
            E("blackmarble_creep_4x2x1", "block", "a black marble block overgrown with creep", "structure", (0.5, 2), 6),
            E("blackmarble_pile", "decor", "a pile of black marble rubble", "ground", (0.5, 2), 6),
            E("dverger_demister_large", "light", "a large blue wisp light on a stand", "ground", (0.5, 2), 6),
            E("piece_blackmarble_table", "prop", "a black marble table", "ground", (0.5, 2), 6),
            E("piece_blackmarble_bench", "prop", "a black marble bench", "ground", (0.5, 2), 6),
            E("dvergrprops_wood_floor", "floor", "a timber floor", "structure", (0.5, 2), 6),
            E("dvergrprops_wood_stair", "stair", "a timber stair", "structure", (1, 1), 6, "keep 1"),
            E("piece_dvergr_spiralstair", "stair", "a Dvergr spiral stair", "structure", (1, 1), 6, "keep 1"),
            E("piece_dvergr_metal_wall_2x2", "wall", "a Dvergr metal wall panel", "structure", (0.5, 2), 6),
            E("piece_dvergr_pole", "pillar", "a Dvergr metal pole", "ground", (0.5, 3), 6),
            E("dvergrtown_slidingdoor", "door", "a 4 x 4 sliding stone door: the theme's door between rooms (Pack.doors)", "structure", (1, 1), 6,
              "placed by the Builder at joints as a saved door; as scenery it doesn't open"),
            E("piece_dvergr_wood_door", "door", "a Dvergr wooden door", "structure", (1, 1), 6, "as scenery it doesn't open: Pack.doors or a gate"),
            E("dvergrprops_lantern", "light", "a hanging Dvergr lantern", "hang", (0.5, 2), 6,
              "hangs from its top: needs a beam or ceiling right above, or a chain (dvergrprops_hooknchain) whose foot is on its top"),
            E("dvergrprops_lantern_standing", "light", "a Dvergr lantern on a stand", "ground", (0.5, 2), 6),
            E("piece_dvergr_lantern", "light", "a Dvergr wall lantern", "wall", (0.5, 2), 6),
            E("piece_dvergr_lantern_pole", "light", "a Dvergr lantern on a pole", "ground", (0.5, 2), 6),
            E("dverger_demister", "light", "a blue wisp light (range 10), its mesh below its origin: hang it", "hang", (0.5, 2), 6),
            E("dvergrprops_hooknchain", "chain", "a hook on a chain: hangs lanterns", "hang", (0.5, 2), 6),
            E("dvergrprops_banner", "decor", "a Dvergr banner", "wall", (0.5, 2), 6),
            E("dvergrprops_curtain", "decor", "a Dvergr curtain", "wall", (0.5, 2), 6),
            E("dvergrprops_barrel", "prop", "a barrel", "ground", (0.5, 2), 6),
            E("dvergrprops_crate", "prop", "a crate", "ground", (0.5, 2), 6),
            E("dvergrprops_crate_long", "prop", "a long crate", "ground", (0.5, 2), 6),
            E("dvergrprops_table", "prop", "a table", "ground", (0.5, 2), 6),
            E("dvergrprops_chair", "prop", "a chair (stays a seat)", "ground", (1, 1), 6),
            E("dvergrprops_stool", "prop", "a stool", "ground", (0.5, 2), 6),
            E("dvergrprops_shelf", "prop", "a shelf", "ground", (0.5, 2), 6),
            E("dvergrprops_bed", "prop", "a bed", "ground", (0.5, 2), 6),
            E("dvergrprops_pickaxe", "prop", "a pickaxe leaning", "ground", (0.5, 2), 6),
            E("CreepProp_hanging01", "decor", "Mistlands creep hanging from a ceiling", "hang", (0.5, 2), 6),
            E("CreepProp_wall01", "decor", "Mistlands creep on a wall", "wall", (0.5, 2), 6),
            E("YggdrasilRoot", "decor", "a great Yggdrasil root, 44 m long", "ground", (0.05, 0.5), 6),
        ],
    },
    "crypt": {
        "about": "Stone crypts (the game's forest and sunken crypts, joined by type \"\"): grey stone walls, arches and pillars, torches, "
                 "bones and roots, iron gates. Human scale: rooms on a 2-4 m grid, doorways 2-3 m.",
        "pieces": [
            E("stone_wall_4x2", "wall", "stone wall 4 x 2", "structure", (0.5, 2), 2),
            E("stone_wall_2x1", "wall", "stone wall 2 x 1", "structure", (0.5, 2), 2),
            E("stone_wall_1x1", "wall", "stone wall 1 x 1", "structure", (0.5, 2), 2),
            E("stone_wall_ruin", "wall", "a ruined stone wall", "structure", (0.5, 2), 2),
            E("stone_floor_2x2", "floor", "stone floor 2 x 2", "structure", (0.5, 2), 2),
            E("stone_floor", "floor", "stone floor 4 x 4", "structure", (0.5, 2), 2),
            E("stone_pillar", "pillar", "stone pillar", "ground", (0.5, 2), 2),
            E("stone_arch", "arch", "stone arch", "structure", (0.5, 2), 2),
            E("stone_stair", "stair", "stone stair", "structure", (1, 1), 2, "keep 1"),
            E("Piece_grausten_pillar_arch", "arch", "grausten pillar arch (a darker crypt)", "structure", (0.5, 2), 7),
            E("dungeon_forestcrypt_door", "door", "the forest crypt's wooden door (Pack.doors for type \"\")", "structure", (1, 1), 2,
              "placed by the Builder at joints as a saved door; as scenery it doesn't open"),
            E("dungeon_sunkencrypt_irongate", "gate", "the sunken crypt's iron gate", "structure", (1, 1), 3, "as scenery it doesn't open"),
            E("CastleKit_groundtorch", "light", "a standing iron torch (orange)", "ground", (0.5, 2), 2),
            E("CastleKit_groundtorch_green", "light", "a standing iron torch, green fire", "ground", (0.5, 2), 3),
            E("CastleKit_groundtorch_blue", "light", "a standing iron torch, blue fire", "ground", (0.5, 2), 4),
            E("piece_walltorch", "light", "a wall sconce torch", "wall", (0.5, 2), 1),
            E("piece_groundtorch_green", "light", "a green standing torch", "ground", (0.5, 2), 3),
            E("piece_brazierfloor01", "light", "a floor brazier", "ground", (0.5, 3), 2),
            E("piece_brazierceiling01", "light", "a brazier hanging from a ceiling", "hang", (0.5, 3), 2),
            E("Candle_resin", "light", "a resin candle", "ground", (0.5, 2), 1),
            E("Skull1", "remains", "a skull", "ground", (0.5, 2), 2),
            E("Skull2", "remains", "a skull", "ground", (0.5, 2), 2),
            E("root07", "decor", "roots through a wall", "wall", (0.5, 2), 2),
            E("root11", "decor", "roots through a ceiling", "hang", (0.5, 2), 2),
            E("vertical_web", "decor", "a spider web, upright", "free", (0.5, 2), 2),
            E("horizontal_web", "decor", "a spider web, flat", "free", (0.5, 2), 2),
            E("cloth_hanging_long", "decor", "a long hanging cloth", "hang", (0.5, 2), 2),
            E("mudpile", "decor", "a pile of mud (sunken crypts)", "ground", (0.5, 2), 3),
        ],
    },
    "roothollow": {
        "about": "Rootdeep (pack-rootdeep): inside the roots of a dying world-tree. Walls of upright trunks packed side by side over a hidden "
                 "marble backing, floors of root ribs over a slab, ceilings under arches of great roots; pale root lattice, moss, rot and tar; "
                 "cold fungal light with warm amber marks on what you use.",
        "pieces": [
            E("Oak_log", "wall", "an oak trunk, 13.7 m long, already upright in its own frame (origin in its middle): walls of trunks side by "
              "side, pillars, floor ribs laid down", "structure", (0.3, 1.5), 1,
              "a physics log in the game (it rolls): stripped it stays put; never Saved"),
            E("SwampTree1_log", "floor", "a swamp tree log, 6.7 m along z: floor ribs, a second log look", "structure", (0.5, 2), 3,
              "a physics log in the game: never Saved"),
            E("yggashoot_log", "pillar", "a slim Yggdrasil shoot log, 10.6 m along y: columns", "ground", (0.3, 1.5), 6,
              "a physics log in the game: never Saved"),
            E("SwampTree1_Stub", "pillar", "a swamp tree's stump with its roots: the foot of a trunk pillar", "ground", (0.5, 1.5), 3),
            E("FirTree_oldLog", "decor", "an old mossy fir log, 7 m along z: trim, fallen trunks", "ground", (0.5, 2), 2),
            E("HugeRoot1", "arch", "a huge root, 29 m: arches under the ceiling", "structure", (0.2, 1.2), 2),
            E("HoleRock_rootWall1", "grate", "a pale root lattice 10 x 8 m, solid: grates, root-mass doors, accents", "structure", (0.3, 1.5), 1),
            E("SwampTree2_log", "decor", "a giant swamp log, 9 x 11 x 24 m: a fallen world-tree root", "ground", (0.2, 1), 3),
            E("BigBranch", "decor", "a great fallen branch, 21 m", "ground", (0.2, 1), 2),
            E("AshlandsTreeStump1", "decor", "a dark gnarled stump, 7 m: rot", "ground", (0.3, 1.5), 7),
            E("tarlump1", "decor", "a lump of tar, 28 x 8 x 24 m: rot pools (scale it down)", "ground", (0.05, 0.5), 5),
            E("GlowingMushroom", "light", "a glowing mushroom, cold grey-blue light (range 10)", "ground", (0.5, 2), 6),
            E("GuckSack", "light", "a hanging guck sack, green glow (range 5.4)", "hang", (0.5, 1.5), 3),
            E("GuckSack_small", "light", "a small guck sack, green glow (range 4)", "hang", (0.5, 1.5), 3),
            E("piece_Lavalantern", "light", "a lava lantern, warm orange (range 12): an amber mark on what you use", "ground", (0.5, 2), 7),
            E("Candle_resin", "light", "a resin candle, amber (range 1): small marks on knobs and levers", "ground", (0.5, 3), 1),
            E("vines", "decor", "hanging vines (walk-through)", "wall", (0.5, 2), 2),
            E("Bush01", "decor", "a bush", "ground", (0.5, 1.5), 1),
            E("mudpile_old", "decor", "a heap of old mud", "ground", (0.5, 2), 3),
            E("root08", "decor", "roots across a floor", "ground", (0.5, 2), 2),
            E("root12", "decor", "roots along a floor", "ground", (0.5, 2), 2),
            E("blackmarble_floor_large", "floor", "8 x 2 x 8 black marble slab: the hidden backing behind trunk walls, under rib floors, and turned "
              "over, the ceiling", "structure", (0.5, 2), 6),
            E("YggdrasilRoot", "decor", "a great Yggdrasil root, 44 m long: arches", "ground", (0.05, 0.6), 6),
            E("dvergrprops_wood_stair", "stair", "a timber stair (with log treads over it)", "structure", (1, 1), 6, "keep 1"),
            E("piece_groundtorch_green", "light", "a green standing torch, on a wall bracket of roots", "wall", (0.5, 2), 3),
            E("TreasureChest_swamp", "container", "a swamp chest (Saved: refilled by the Builder)", "ground", (1, 1), 3, "Saved only, scale 1"),
        ],
    },
    "shared": {
        "about": "Pieces any theme uses: saved parts (chests, spawners, treasure), lights and props that fit anywhere.",
        "pieces": [
            E("Morkhalla_ChestAncient", "container", "the Jotunn's ancient chest, 3.2 x 2.1 x 2.2 m, 32 slots: the biggest real chest (Saved: refilled by the Builder)",
              "ground", (1, 1), 8, "Saved only, scale 1 (as scenery Strip takes its Container off: it opens nothing)"),
            E("TreasureChest_morkhalla", "container", "the Jotunn chest, 2.6 x 1.1 x 1.6 m, 8 slots (Saved)", "ground", (1, 1), 8, "Saved only; keep scale 1"),
            E("TreasureChest_charredfortress", "container", "the Ashlands charred chest, 2.7 x 1.75 x 1.76 m, 8 slots (Saved)", "ground", (1, 1), 7,
              "Saved only; keep scale 1"),
            E("piece_chest_grausten", "container", "a grausten chest, 1.7 x 1.2 x 1.2 m, 40 slots (Saved)", "ground", (1, 1), 7, "Saved only; keep scale 1"),
            E("TreasureChest_dvergr_loose_stone", "container", "a Dvergr stone chest, 2 x 1 x 2 m, 8 slots (Saved)", "ground", (1, 1), 6,
              "Saved only; keep scale 1"),
            E("TreasureChest_trollcave", "container", "a stone-lidded chest: vault chests (Saved: refilled by the Builder from the pack's loot)",
              "ground", (1, 1), 2, "Saved only (as scenery it opens nothing); keep scale 1"),
            E("TreasureChest_forestcrypt", "container", "a crypt chest (Saved)", "ground", (1, 1), 2, "Saved only; keep scale 1"),
            E("TreasureChest_dvergrtown", "container", "a Dvergr chest (Saved)", "ground", (1, 1), 6, "Saved only; keep scale 1"),
            E("crypt_skeleton_chest", "remains", "a skeleton slumped over a chest: NOT a container but a breakable that drops loot and may hatch "
              "a creature (EggHatch)", "ground", (1, 1), 2, "Saved: its drops and what it hatches are fitted to the biome; as scenery a prop"),
            E("Spawner_Draugr", "spawner", "an invisible spawn point: melee (Saved; swapped for the biome's melee)", "ground", (1, 1), 3,
              "Saved only; invisible; stands 0.5 m above its floor"),
            E("Spawner_Draugr_Ranged", "spawner", "an invisible spawn point: ranged (Saved; swapped for the biome's ranged)", "ground", (1, 1), 3,
              "Saved only"),
            E("Spawner_Ghost", "spawner", "an invisible spawn point: flying (Saved; swapped for the biome's flyer)", "ground", (1, 1), 2,
              "Saved only"),
            E("BonePileSpawner", "spawner", "a bone pile that spawns skeletons until broken (Saved)", "ground", (1, 1), 2,
              "Saved only; what it spawns is fitted to the biome"),
            E("Pickable_DvergrMineTreasure", "pickable", "a Dvergr treasure pile, one fixed item (Saved: swapped for a fitting pickable, or left "
              "out, below the Mistlands)", "ground", (1, 1), 6, "Saved only; as scenery it shows loot that can't be taken"),
            E("skull_pile", "remains", "a pile of skulls", "ground", (0.5, 2), 2),
            E("bone_stack", "remains", "a stack of bones", "ground", (0.5, 2), 2),
            E("rug_fur", "decor", "a fur rug", "ground", (0.5, 2), 2),
            E("piece_banner01", "decor", "a banner", "wall", (0.5, 2), 1),
            E("bar_iron_stack", "prop", "a stack of iron bars", "ground", (0.5, 2), 3),
            E("wood_core_stack", "prop", "a stack of logs", "ground", (0.5, 2), 2),
            E("stone_pile", "prop", "a pile of stones", "ground", (0.5, 2), 1),
        ],
    },
}

# The same pieces by the roles a theme recipe asks for (dungeon-content's vocabulary): the first is the default, the others variants for
# variety. HOW says how a role's pieces are placed when it isn't plain (a floor slab stood up as a wall...). A role with an empty list has
# nothing in the theme's look: the recipe leaves it out or borrows from another theme.
ROLES = {
    "giant": {
        "wall": ["Ashlands_floor_large", "Ashlands_WallBlock"],
        "wall_alt": ["blackmarble_floor_large"],
        "floor": ["Ashlands_floor_large", "Morkhalla_Floor_4x4", "Ashlands_Fortress_Floor"],
        "ceiling": ["Ashlands_floor_large"],
        "trim": ["blackmarble_floor_large", "blackmarble_column_3", "blackmarble_head_big01"],
        "pillar": ["Ashlands_Boss_Pillar", "Ashlands_Pillar4", "Morkhalla_Block"],
        "column": ["blackmarble_column_3", "Piece_flametal_pillar"],
        "beam": ["Piece_flametal_beam", "Morkhalla_MetalBar"],
        "rail": ["Morkhalla_giant_railing", "Morkhalla_giant_railing_corner"],
        "grate": ["iron_grate"],
        "door_leaf": ["Ashlands_Fortress_Gate_Door", "iron_grate"],
        "stair": ["piece_grausten_stonestair", "Morkhalla_Stairs_giant_short"],
        "light_stand": ["Morkhalla_giant_railing_torch", "MountainKit_brazier_blue", "blackmarble_altar_crystal", "Morkhalla_firepit"],
        "light_hang": ["dverger_demister"],
        "light_wall": ["piece_walltorch"],
        "gear": ["CharredCogwheel"],
        "machine": ["Morkhalla_MetalBar", "Piece_flametal_beam", "CharredCogwheel"],
        "chain": ["Morkhalla_WallChain1", "Morkhalla_Chain"],
        "chest": ["Morkhalla_ChestAncient", "TreasureChest_morkhalla", "piece_chest_grausten", "TreasureChest_charredfortress", "TreasureChest_trollcave"],
        "decor_small": ["grausten_pile", "Morkhalla_Stonepile", "Morkhalla_WeaponStand", "bar_iron_stack", "Morkhalla_coal_pile_memorial"],
        "decor_large": ["Morkhalla_Bench", "Morkhalla_Table", "MemorialStone_Large", "Morkhalla_StatuePieceFace_big", "Morkhalla_StatueSword_hanging",
                        "Morkhalla_Banner1"],
        "rubble": ["Morkhalla_Rubble1", "Morkhalla_Rubble2", "grausten_pile"],
        "bones": ["giant_skull", "giant_ribs", "giant_arm", "giant_sword1", "giant_helmet1", "giant_helmet2", "giant_brain", "ancient_skull",
                  "skull_pile", "bone_stack"],
    },
    "dvergr": {
        "wall": ["piece_dvergr_metal_wall_2x2", "blackmarble_tile_wall_2x2"],
        "wall_alt": ["dvergrtown_wood_wall01", "dvergrtown_wood_wall02", "dvergrtown_wood_wall03", "dvergrprops_wood_wall", "blackmarble_tile_wall_2x4"],
        "floor": ["blackmarble_tile_floor_2x2", "blackmarble_floor", "dvergrprops_wood_floor"],
        "ceiling": ["blackmarble_floor", "dvergrprops_wood_floor"],
        "trim": ["blackmarble_2x1x1", "blackmarble_out_1", "blackmarble_base_1", "blackmarble_tip", "blackmarble_head01"],
        "pillar": ["blackmarble_column_3", "dvergrtown_wood_pole", "blackmarble_2x2x2"],
        "column": ["blackmarble_column_1", "blackmarble_column_2", "blackmarble_post01", "piece_dvergr_pole", "dvergrprops_wood_pole"],
        "beam": ["dvergrtown_wood_beam", "dvergrprops_wood_beam", "dvergrtown_wood_support"],
        "rail": [],
        "grate": [],
        "door_leaf": ["dvergrtown_slidingdoor", "piece_dvergr_wood_door", "dvergrtown_secretdoor"],
        "stair": ["blackmarble_stair", "dvergrprops_wood_stair", "blackmarble_stair_corner"],
        "stair_spiral": ["piece_dvergr_spiralstair", "piece_dvergr_spiralstair_right"],
        "light_stand": ["dvergrprops_lantern_standing", "piece_dvergr_lantern_pole", "dverger_demister_large", "blackmarble_altar_crystal"],
        "light_hang": ["dvergrprops_lantern", "dverger_demister"],
        "light_wall": ["piece_dvergr_lantern"],
        "gear": [],
        "machine": ["dvergrtown_wood_crane", "dvergrtown_wood_support"],
        "chain": ["dvergrprops_hooknchain"],
        "chest": ["TreasureChest_dvergr_loose_stone", "TreasureChest_dvergrtown"],
        "decor_small": ["dvergrprops_crate", "dvergrprops_barrel", "dvergrprops_stool", "dvergrprops_pickaxe", "dvergrprops_chair"],
        "decor_large": ["dvergrprops_table", "dvergrprops_shelf", "dvergrprops_bed", "dvergrprops_crate_long", "piece_blackmarble_table",
                        "piece_blackmarble_bench", "dvergrprops_banner", "dvergrprops_curtain"],
        "rubble": ["blackmarble_pile", "dvergrtown_wood_stakewall"],
        "bones": [],
    },
    "roothollow": {
        "wall": ["Oak_log"],
        "wall_alt": ["blackmarble_floor_large"],
        "floor": ["Oak_log", "SwampTree1_log", "blackmarble_floor_large"],
        "ceiling": ["blackmarble_floor_large"],
        "trim": ["HoleRock_rootWall1", "FirTree_oldLog", "SwampTree1_log"],
        "pillar": ["Oak_log", "SwampTree1_Stub"],
        "column": ["yggashoot_log"],
        "beam": ["HugeRoot1"],
        "rail": [],
        "grate": ["HoleRock_rootWall1"],
        "door_leaf": ["HoleRock_rootWall1", "Oak_log"],
        "stair": ["dvergrprops_wood_stair"],
        "light_stand": ["GlowingMushroom", "piece_Lavalantern"],
        "light_hang": ["GuckSack", "GuckSack_small"],
        "light_wall": ["piece_groundtorch_green"],
        "chain": [],
        "chest": ["TreasureChest_swamp"],
        "decor_small": ["Candle_resin", "mudpile_old", "root08", "root12", "Bush01", "skull_pile", "bone_stack"],
        "decor_large": ["SwampTree2_log", "BigBranch", "AshlandsTreeStump1", "tarlump1", "YggdrasilRoot", "HugeRoot1", "vines"],
        "rubble": ["mudpile_old", "root08"],
        "bones": ["skull_pile", "bone_stack"],
    },
    "crypt": {
        "wall": ["stone_wall_4x2", "stone_wall_2x1"],
        "wall_alt": ["stone_wall_ruin", "stone_wall_1x1"],
        "floor": ["stone_floor_2x2", "stone_floor"],
        "ceiling": ["stone_floor_2x2"],
        "trim": ["stone_arch", "Piece_grausten_pillar_arch"],
        "pillar": ["stone_pillar"],
        "column": ["stone_pillar"],
        "beam": [],
        "rail": [],
        "grate": ["dungeon_sunkencrypt_irongate"],
        "door_leaf": ["dungeon_forestcrypt_door", "dungeon_sunkencrypt_irongate"],
        "stair": ["stone_stair"],
        "light_stand": ["CastleKit_groundtorch", "CastleKit_groundtorch_green", "piece_groundtorch_green", "piece_brazierfloor01", "Candle_resin"],
        "light_hang": ["piece_brazierceiling01"],
        "light_wall": ["piece_walltorch"],
        "chest": ["TreasureChest_forestcrypt"],
        "decor_small": ["Skull1", "Skull2", "Candle_resin"],
        "decor_large": ["cloth_hanging_long", "root07", "root11", "vertical_web", "horizontal_web"],
        "rubble": ["mudpile", "stone_pile"],
        "bones": ["skull_pile", "bone_stack", "Skull1", "Skull2", "crypt_skeleton_chest"],
    },
}

HOW = {
    "wall": "a floor slab stood up: euler (-90, side yaw, 0), its top face inwards (dungeon_parts.wall); a wall piece (tile wall, metal wall, "
            "timber wall) stands as it is, turned by yaw",
    "wall_alt": "as wall",
    "ceiling": "a floor slab turned over: euler (180, 0, 0) (dungeon_parts.slab with ceiling=True); its origin is in the slab's middle",
    "floor": "flat as it is; most slabs have their origin in their middle (see each piece's origin and min/max)",
    "door_leaf": "as scenery it doesn't open: Pack.doors (saved, at joints) or a gate mechanism moves it",
    "stair": "keep scale 1 (step height); stack or repeat for a wider or longer stair",
    "stair_spiral": "keep scale 1",
    "light_hang": "hang it from the first part above it (ceiling, balcony, a mechanism's lid) on the theme's chain, the chain's foot on the "
                  "light's top: giant, a thinned Morkhalla_WallChain1 (giant_kit.wisp); Dvergr, dvergrprops_lantern on dvergrprops_hooknchain. "
                  "Stood at a point it floats (dverger_demister's mesh is below its origin)",
    "light_wall": "against a wall, its back to it",
    "chain": "a wall chain hangs from its anchor at the top; a span rests on two anchors",
    "gear": "CharredCogwheel is an item: the Builder makes it solid with a disc collider; scale it up, never make it a Saved part",
    "chest": "Saved parts only, scale 1: the Builder refills them from the pack's loot for the biome",
    "decor_small": "pick by the room's dressing set (each piece's \"sets\")",
    "decor_large": "pick by the room's dressing set (each piece's \"sets\")",
    "bones": "giant bones are huge (a skull is 10 x 12 x 17 m): scale them down for human-scale rooms",
}

# Known trouble, by prefab (kept with every palette that names it, and listed in shared.json)
TROUBLE = {
    "Morkhalla_MetalPillar": "not in the game's ZNetScene (it's only part of Mórk halla rooms): dungeon_parts.py refuses it. Use Piece_flametal_pillar or Morkhalla_MetalBar stood up.",
    "CharredCogwheel": "an item prefab: players walked through the giant gears (its collider is a 1.14 x 0.4 x 1.14 box on the 'item' layer). The Builder now moves it to static_solid and fits a disc collider (BuildSolid.cs). Never Saved: it would be an Ashlands item.",
    "dvergrprops_lantern": "floated in the first Shifting Halls (hanging lanterns with nothing above): hang it from a beam, a ceiling or dvergrprops_hooknchain / Morkhalla_WallChain1 with the chain's foot on its top (prop_check.py).",
    "piece_brazierceiling01": "hangs: needs a ceiling or beam right above its origin.",
    "dverger_guardstone": "a ward (PrivateArea): stripped it's only a look, but it reads as a player's ward. Don't use.",
    "MistArea": "the Mistlands mist: it hides everything around it in a dungeon. Don't use (also MistArea_small, MistArea_edge).",
    "Morkhalla_Rubble1_fall": "falls (physics) in the game: stripped it's frozen mid-air. Use Morkhalla_Rubble1.",
    "dverger_demister": "floated in 35 places in Shifting Halls: its origin is at its top (the mesh hangs below it), so stood at a point it's in the air. Hang it on a chain from the first part above it (giant_kit.wisp).",
    "dvergrprops_banner": "its origin is in its middle (2 m above its foot): stood at a point it reaches 2 m higher than it seems, and left a 6 m room's box.",
    "blackmarble_2x2x1": "stretched, its bevels become spikes: don't use it as a stretched wall (blackmarble_tile_wall_2x4 instead).",
    "Ashlands_floor_large_fractured": "looks the same as Ashlands_floor_large: no use for a cracked floor.",
    "giant_ribs": "huge (0.75 scale is still about 14 m long), and hollow: its collider is boxes in prefab_shapes.json. Scale giant remains by their box.",
    "Lantern_DN": "an item: its lights are off in the prefab (lit only while carried), so as scenery it's dark. Use deepnorth_lantern_standing or a Dvergr lantern.",
    "LargeBone": "carries an EggHatch (it hatches something): scenery only (Strip takes the hatching off); Saved, it would hatch.",
    "*_log": "Oak_log, SwampTree1_log, yggashoot_log and other tree logs are physics logs in the game (a live Rigidbody: they roll): fine as stripped scenery, never Saved.",
    "*_frac": "fractured debris (physics pieces of something broken): don't use as scenery.",
    "Pickable_*": "as scenery a pickable shows its item but can't be taken: confusing. Use only as Saved parts.",
    "TreasureChest_*": "as scenery a chest opens nothing: Saved only, scale 1.",
    "Spawner_*": "invisible and does nothing as scenery: Saved only.",
}

# The dressing sets a piece suits (dungeon-content's: giant ossuary, ruin, forge, archive, engine, armoury; Dvergr store, workshop, tavern,
# barracks)
SETS = {
    "skull_pile": ["ossuary"], "bone_stack": ["ossuary"], "giant_skull": ["ossuary"], "giant_ribs": ["ossuary"], "giant_arm": ["ossuary"],
    "giant_brain": ["ossuary"], "ancient_skull": ["ossuary"],
    "giant_sword1": ["armoury", "ossuary"], "giant_helmet1": ["armoury", "ossuary"], "giant_helmet2": ["armoury", "ossuary"],
    "Morkhalla_WeaponStand": ["armoury"], "Morkhalla_StatueSword_hanging": ["armoury", "archive"],
    "Morkhalla_Rubble1": ["ruin"], "Morkhalla_Rubble2": ["ruin"], "grausten_pile": ["ruin", "forge"], "Morkhalla_Stonepile": ["ruin", "archive"],
    "Morkhalla_firepit": ["forge"], "Morkhalla_coal_pile_memorial": ["forge"], "bar_iron_stack": ["forge", "store"],
    "MemorialStone_Large": ["archive"], "Morkhalla_StatuePieceFace_big": ["archive", "ruin"], "Morkhalla_Bench": ["archive"],
    "Morkhalla_Table": ["archive", "forge"], "Morkhalla_Banner1": ["archive", "armoury"],
    "CharredCogwheel": ["engine"], "Morkhalla_MetalBar": ["engine"], "Piece_flametal_beam": ["engine"], "Morkhalla_Chain": ["engine"],
    "Morkhalla_WallChain1": ["engine", "ossuary"],
    "dvergrprops_crate": ["store", "workshop"], "dvergrprops_crate_long": ["store"], "dvergrprops_barrel": ["store", "tavern"],
    "dvergrprops_shelf": ["store", "workshop"], "dvergrprops_pickaxe": ["workshop"], "dvergrtown_wood_crane": ["workshop"],
    "dvergrprops_table": ["tavern", "workshop"], "dvergrprops_chair": ["tavern", "barracks"], "dvergrprops_stool": ["tavern"],
    "piece_blackmarble_table": ["tavern"], "piece_blackmarble_bench": ["tavern", "barracks"], "dvergrprops_bed": ["barracks"],
    "dvergrprops_banner": ["barracks"], "dvergrprops_curtain": ["barracks", "tavern"], "blackmarble_pile": ["workshop", "store"],
}

# Panels: the longest a stretched piece may get (m) before its texture smears (dungeon-content's testing)
MAX_SPAN = {"Ashlands_floor_large": 10.0, "blackmarble_floor_large": 10.0, "blackmarble_tile_wall_2x4": 4.0, "blackmarble_tile_wall_2x2": 4.0,
            "blackmarble_tile_floor_2x2": 4.0}

# Themes curated by their own pack teams: palettes/src/<theme>.json, the same curation as CURATED and ROLES here, in a file each team owns
# (so five teams never edit this one at once):
#   {"about": "...", "pieces": [{"prefab": "...", "role": "...", "look": "...", "mount": "ground", "scale": [0.5, 2], "tier": 6,
#                                "notes": "..."}],
#    "roles": {"wall": ["...", ...], ...}, "how": {"wall": "...", ...}}
# mount, scale, tier and notes may be left out (ground, [0.5, 2], none, none). Everything measured is filled in by this tool, as for the rest.
SRC = os.path.join(OUT_DIR, "src")


def load_sources():
    if not os.path.isdir(SRC):
        return
    for f in sorted(os.listdir(SRC)):
        if not f.endswith(".json"):
            continue
        theme = f[:-5]
        try:
            d = json.load(open(os.path.join(SRC, f), encoding="utf-8"))
        except Exception as ex:
            print(f"PROBLEM palettes/src/{f} can't be read: {ex}")
            continue
        pieces = [E(p["prefab"], p.get("role", "decor"), p.get("look", ""), p.get("mount", "ground"), tuple(p.get("scale", (0.5, 2.0))),
                    p.get("tier"), p.get("notes", "")) for p in d.get("pieces", []) if p.get("prefab")]
        CURATED[theme] = {"about": d.get("about", ""), "pieces": pieces, "source": "palettes/src/" + f}
        ROLES[theme] = d.get("roles", {})
        for k, v in (d.get("how") or {}).items():
            HOW_BY_THEME.setdefault(theme, {})[k] = v


HOW_BY_THEME = {}

# Of those, the ones not to use at all (the others are fine placed as their note says)
AVOID = {"Morkhalla_MetalPillar", "dverger_guardstone", "MistArea", "MistArea_small", "MistArea_edge", "Morkhalla_Rubble1_fall",
         "Morkhalla_Rubble2_fall"}

# ---- reading the game's files

_layers = None


def layers():
    """(layer index -> name, set of layer indices characters pass through), from globalgamemanagers."""
    global _layers
    if _layers is None:
        import UnityPy
        env = UnityPy.load(os.path.join(GAME, "valheim_Data", "globalgamemanagers"))
        names, matrix = {}, None
        for o in env.objects:
            if o.type.name == "TagManager":
                names = {i: n for i, n in enumerate(o.read_typetree()["layers"]) if n}
            elif o.type.name == "PhysicsManager":
                matrix = o.read_typetree()["m_LayerCollisionMatrix"]
        character = next(i for i, n in names.items() if n == "character")
        passes = {i for i in range(32) if not (matrix[character] >> i) & 1}
        _layers = (names, passes)
    return _layers


# A MonoBehaviour's kind, told by the fields it has (its script can't be read from the bundle)
KINDS = [
    ("ZNetView", {"m_persistent", "m_syncInitialScale"}), ("ItemDrop", {"m_itemData"}), ("Piece", {"m_canBeRemoved", "m_category"}),
    ("WearNTear", {"m_noRoofWear"}), ("Fireplace", {"m_enabledObject", "m_startFuel"}), ("LightLod", {"m_lightDistance", "m_shadowDistance"}),
    ("Container", {"m_defaultItems", "m_width", "m_height"}), ("PickableItem", {"m_randomItemPrefabs"}),
    ("Pickable", {"m_respawnTimeMinutes", "m_amount"}), ("Destructible", {"m_destructibleType"}), ("DropOnDestroyed", {"m_dropWhenDestroyed"}),
    ("MineRock", {"m_dropItems", "m_minToolTier"}), ("CreatureSpawner", {"m_creaturePrefab", "m_respawnTimeMinuts"}),
    ("SpawnArea", {"m_spawnIntervalSec", "m_prefabs"}), ("TriggerSpawner", {"m_creaturePrefabs"}), ("Door", {"m_keyItem"}),
    ("EffectArea", {"m_statusEffect", "m_playerOnly"}), ("Chair", {"m_sittingAnimation"}), ("Ladder", {"m_targetPos"}),
    ("EggHatch", {"m_chanceToHatch"}), ("LootSpawner", {"m_spawnWhenEnemiesCleared"}),
    ("CraftingStation", {"m_craftRequireRoof"}), ("Smelter", {"m_maxOre"}), ("Fermenter", {"m_fermentationDuration"}),
    ("Bed", {"m_spawnPoint"}), ("TeleportWorld", {"m_activationRange"}), ("ItemStand", {"m_supportedTypes"}), ("Sign", {"m_characterLimit"}),
    ("Ship", {"m_sailForceFactor"}),
]
# what a player could use in the game; as scenery the Builder strips all of it (Builder.Usable, BuildParts.cs), and `dungeon check` proves it
USABLE = {"CraftingStation", "Smelter", "Fermenter", "Bed", "TeleportWorld", "ItemStand", "Sign", "Ship", "Door", "Container", "Pickable",
          "PickableItem", "Chair", "Ladder"}
GIVES = {"ItemDrop", "Pickable", "PickableItem", "Container", "Destructible", "DropOnDestroyed", "MineRock", "LootSpawner"}


def kind_of(mb):
    keys = set(vars(mb).keys())         # (UnityPy gives a MonoBehaviour its serialized fields as attributes)
    for name, need in KINDS:
        if need <= keys:
            return name, mb
    return None, None


def inspect(name):
    """What the prefab is made of (see the module's doc), or None if the main bundle hasn't it."""
    sys.path.insert(0, gm.BLUEPRINT_TOOLS)
    from extract_pieces import components
    root = gm._main_bundle().get(name)
    if root is None:
        return None
    names, passes = layers()
    go = root.read()
    tr = next(x for x in components(go) if x.__class__.__name__ == "Transform")
    kinds, colliders, lights, particles = set(), {}, [], 0
    lit_ids = set()                     # objects a Fireplace switches on when it burns (Strip does)
    meshes_readable, rigid = None, False

    def visit(t, on, depth):
        nonlocal particles, rigid
        g = t.m_GameObject.read()
        gid = t.m_GameObject.path_id
        here_on = on and (g.m_IsActive or gid in lit_ids)
        comps = list(components(g))
        for c in comps:
            if c.__class__.__name__ == "MonoBehaviour":
                k, mb = kind_of(c)
                if k:
                    kinds.add(k)
                    if k == "Fireplace":
                        for f in ("m_enabledObject", "m_enabledObjectHigh", "m_fullObject"):
                            pid = getattr(getattr(mb, f, None), "path_id", 0)
                            if pid:
                                lit_ids.add(pid)
        for c in comps:
            k = c.__class__.__name__
            if k.endswith("Collider") and not getattr(c, "m_IsTrigger", False) and getattr(c, "m_Enabled", True):
                layer = names.get(g.m_Layer, str(g.m_Layer))
                colliders[layer] = colliders.get(layer, 0) + 1
            elif k == "Light":
                col = c.m_Color
                lights.append({"range": round(float(c.m_Range), 2), "intensity": round(float(c.m_Intensity), 2),
                               "colour": "#%02x%02x%02x" % tuple(int(max(0, min(1, getattr(col, ch))) * 255) for ch in ("r", "g", "b")),
                               "lit": bool(here_on and c.m_Enabled), "_obj": gid})
            elif k == "ParticleSystem" and here_on:
                particles += 1
            elif k == "Rigidbody" and depth == 0 and not getattr(c, "m_IsKinematic", False):
                rigid = True
        for ch in t.m_Children:
            visit(ch.read(), here_on, depth + 1)

    visit(tr, True, 0)
    # LightLod sits on the light's own object: note which lights fade with distance
    has_lod = "LightLod" in kinds
    for lt in lights:
        lt.pop("_obj", None)
        lt["lod"] = has_lod
    solid_layers = {l: n for l, n in colliders.items() if l == "item" or not any(names.get(i) == l for i in passes)}
    item = "ItemDrop" in kinds
    return {
        "kinds": sorted(kinds),
        "colliders": colliders,
        "solid": bool(solid_layers) or item,
        "solid_note": ("an item: made solid and fitted to its shape by the Builder (BuildSolid.cs)" if item else
                       "its 'item'-layer collider is made solid by the Builder (BuildSolid.cs)" if "item" in solid_layers else
                       "" if solid_layers else "players walk through it" if colliders else "no collider: players walk through it"),
        "lights": lights,
        "fire": "Fireplace" in kinds,
        "particles": particles,
        "item": item,
        "networked": "ZNetView" in kinds,
        "gives": sorted(kinds & GIVES),
        "physics": rigid and not item,
        "usable": sorted(kinds & USABLE),
    }



# ---- what a player can climb

# A character jumps only from ground (Character.OnCollisionStay counts a contact under its feet facing up, |normal.y| > 0.1), but it slides
# off anything steeper than 38 degrees (Character.GetSlideAngle, a player) and its jump pushes along (ground normal + up): off a steep face
# it's thrown away from it, gaining under a metre. So a foothold is a face it can stand on: normal.y >= cos 38 degrees, with room for its
# feet (at least FOOT m2 of such faces, seen from above, within a BAND m height band). A smooth upright column or bar has only its top.
# The jump, from the game's files: gravity -20 m/s2 (PhysicsManager), the Player's m_jumpForce 8, times 1 + 0.4 x the Jump skill factor
# (Character.Jump): feet rise 8^2/40 = 1.6 m at skill 0, 11.2^2/40 = 3.1 m at skill 100. STEP is the first, STEP_SKILLED the second.
GRAVITY, JUMP_FORCE = 20.0, 8.0
STEP = round(JUMP_FORCE ** 2 / (2 * GRAVITY), 2)
STEP_SKILLED = round((JUMP_FORCE * 1.4) ** 2 / (2 * GRAVITY), 2)
STANDABLE = 0.788          # cos 38 degrees
BAND, FOOT = 0.2, 0.1


def _box_faces(m, centre, size):
    """The 6 faces of a box collider (centre, size in its object's frame), in the prefab's frame: (y min, y max, normal y) each."""
    c, h = np.array(centre, float), np.array(size, float) / 2
    out = []
    for axis in range(3):
        for sign in (-1, 1):
            corners = []
            for a in (-1, 1):
                for b in (-1, 1):
                    v = np.zeros(3)
                    v[axis] = sign * h[axis]
                    o1, o2 = [i for i in range(3) if i != axis]
                    v[o1], v[o2] = a * h[o1], b * h[o2]
                    corners.append((m @ np.append(c + v, 1.0))[:3])
            p = np.array(corners)
            n = np.cross(p[1] - p[0], p[2] - p[0])
            ln = np.linalg.norm(n)
            if ln < 1e-9:
                continue
            n /= ln
            centre_w = (m @ np.append(c, 1.0))[:3]
            if np.dot(n, p.mean(0) - centre_w) < 0:
                n = -n
            area = float(np.linalg.norm(np.cross(p[1] - p[0], p[2] - p[0])))       # (a box face: the parallelogram on its corners)
            out.append((float(p[:, 1].min()), float(p[:, 1].max()), float(n[1]), area * abs(float(n[1]))))
    return out


def climb(name):
    """Where a player can stand on the prefab, from its solid colliders (the layers characters bump into, and "item", made solid by the
    Builder): its footholds (faces it can stand on, with room for its feet: see above) as merged height ranges above its foot, and the
    highest one reachable from the floor at its foot, each within a jump of the last: top at Jump skill 0, top_skilled at 100 (0: nothing
    above the floor can be reached). None if the main bundle hasn't it."""
    sys.path.insert(0, gm.BLUEPRINT_TOOLS)
    from extract_pieces import components, local_matrix, vec
    from extract_meshes import mesh_triangles
    root = gm._main_bundle().get(name)
    if root is None:
        return None
    names, passes = layers()
    solid = {i for i in names if i not in passes} | {i for i, n in names.items() if n == "item"}
    faces = []                                  # (y min, y max, area seen from above) of every face a player can stand on

    def visit(t, parent, depth):
        g = t.m_GameObject.read()
        if not g.m_IsActive:
            return
        m = parent @ local_matrix(t) if depth else np.eye(4)
        for c in components(g):
            k = c.__class__.__name__
            if not k.endswith("Collider") or getattr(c, "m_IsTrigger", False) or not getattr(c, "m_Enabled", True) or g.m_Layer not in solid:
                continue
            if k == "BoxCollider":
                faces.extend((a, b, ar) for a, b, ny, ar in _box_faces(m, vec(c.m_Center), vec(c.m_Size)) if ny >= STANDABLE)
            elif k == "MeshCollider" and c.m_Mesh.path_id:
                try:
                    v, groups = mesh_triangles(c.m_Mesh.read())
                except Exception:
                    continue
                w = (m @ np.c_[v, np.ones(len(v))].T).T[:, :3]
                tris = np.array([f for grp in groups for f in grp], dtype=np.int64)
                if not len(tris):
                    continue
                t3 = w[tris]
                n = np.cross(t3[:, 1] - t3[:, 0], t3[:, 2] - t3[:, 0])
                ln = np.linalg.norm(n, axis=1)
                ok = ln > 1e-9
                ny = np.where(ok, n[:, 1] / np.where(ok, ln, 1), 0)
                up = np.abs(ny) >= STANDABLE             # (either winding: a mesh collider's triangles face both ways to a capsule)
                ys = t3[up][:, :, 1]
                areas = (0.5 * ln * np.abs(ny))[up]
                faces.extend(zip(ys.min(1).tolist(), ys.max(1).tolist(), areas.tolist()))
            elif k == "SphereCollider":
                cy = (m @ np.append(vec(c.m_Center), 1.0))[1]
                r = float(c.m_Radius) * float(np.linalg.norm(m[:3, 1]))
                faces.append((cy + r * STANDABLE, cy + r, math.pi * (r * 0.62) ** 2))
            elif k == "CapsuleCollider":
                cy = (m @ np.append(vec(c.m_Center), 1.0))[1]
                sy = float(np.linalg.norm(m[:3, 1]))
                r, h = float(c.m_Radius) * sy, float(c.m_Height) * sy
                faces.append((cy + h / 2 - r + r * STANDABLE, cy + h / 2, math.pi * (r * 0.62) ** 2) if int(c.m_Direction) == 1 else
                             (cy + r * STANDABLE, cy + r, h * r * 1.2))
        for ch in t.m_Children:
            visit(ch.read(), m, depth + 1)

    go = root.read()
    tr = next(x for x in components(go) if x.__class__.__name__ == "Transform")
    visit(tr, np.eye(4), 0)
    if not faces:
        return {"ledges": [], "top": 0.0, "top_skilled": 0.0}
    lo, _ = bounds(name)
    foot = float(lo[1])
    # room for the feet: the faces' area seen from above, gathered in height bands; a band with enough is a foothold
    bands = {}
    for a, b, ar in faces:
        k = int(math.floor(((a + b) / 2 - foot) / BAND))
        bands[k] = bands.get(k, 0.0) + ar
    held = sorted(k for k, ar in bands.items() if ar >= FOOT)
    merged = []
    for k in held:
        a, b = k * BAND, (k + 1) * BAND
        if merged and a <= merged[-1][1] + 1e-6:
            merged[-1][1] = b
        else:
            merged.append([a, b])

    def reach(step):
        cur = 0.0
        for a, b in merged:
            if a <= cur + step:
                cur = max(cur, b)
        return round(cur, 2) if cur > 0.3 else 0.0
    return {"ledges": [[round(max(a, 0.0), 2), round(b, 2)] for a, b in merged], "top": reach(STEP), "top_skilled": reach(STEP_SKILLED)}


CATALOGUE = os.path.join(GAME, "BepInEx", "claude", "dungeon-rooms.json")
# the catalogue writes a room shared by two themes as their sum (Room.Theme flags)
THEMES = {"520": ["ForestCrypt", "ForestCryptHildir"], "1028": ["Cave", "CaveHildir"], "12288": ["AshlandRuins", "FortressRuins"]}
_dungeon_parts = None


def game_dungeons(name):
    """The game's dungeon themes whose rooms hold this prefab as a part (the catalogue's networked parts): a piece that says "that's the
    forest crypt" or "that's Mórk halla" to a player. Empty for a piece no game dungeon uses."""
    global _dungeon_parts
    if _dungeon_parts is None:
        import re
        _dungeon_parts = {}
        try:
            for r in json.load(open(CATALOGUE, encoding="utf-8")):
                t = str(r.get("theme"))
                themes = THEMES.get(t, [t if not t.isdigit() else "theme " + t])
                for n in r.get("networked", []):
                    _dungeon_parts.setdefault(re.sub(r" x\d+$", "", n), set()).update(themes)
        except Exception:
            pass
    return sorted(_dungeon_parts.get(name, ()))


def origin_of(lo, hi):
    """Where the prefab's origin sits on its box: the word an author needs to place it."""
    def axis(a, b):
        if abs(a) < 0.15:
            return "low"
        if abs(b) < 0.15:
            return "high"
        if abs(a + b) < 0.25 * max(b - a, 0.01):
            return "middle"
        return "inside" if a < 0 < b else "outside"
    y = axis(lo[1], hi[1])
    word = {"low": "foot", "high": "top", "middle": "centre", "inside": "inside", "outside": "off the box"}[y]
    return word


def render(name, out):
    from dungeon_view import Scene
    s = Scene()
    s.add(name)
    lo, hi = bounds(name)
    s.render(out, views=((35, 22), (-145, 18)), size=(560, 400), title=f"{name}  {hi[0]-lo[0]:.1f} x {hi[1]-lo[1]:.1f} x {hi[2]-lo[2]:.1f} m")


def sheet(theme, entries, out):
    """One picture with every piece of a theme (the first view of each), labelled."""
    from PIL import Image, ImageDraw
    thumbs = []
    for e in entries:
        p = os.path.join(RENDERS, e["prefab"] + ".png")
        if os.path.exists(p):
            im = Image.open(p).convert("RGB")
            w, h = im.size
            thumbs.append((e, im.crop((0, 0, w // 2, h)).resize((260, 186))))
    if not thumbs:
        return None
    cols = 6
    rows = math.ceil(len(thumbs) / cols)
    S = Image.new("RGB", (cols * 264, rows * 210 + 30), (18, 20, 26))
    d = ImageDraw.Draw(S)
    d.text((8, 8), f"palette: {theme} ({len(thumbs)} pieces)", fill=(230, 230, 230))
    for i, (e, im) in enumerate(thumbs):
        x, y = (i % cols) * 264 + 2, (i // cols) * 210 + 30
        S.paste(im, (x, y))
        d.text((x + 3, y + 188), f"{e['prefab']} ({e['role']})"[:44], fill=(220, 220, 160))
    S.save(out)
    return out


# ---- making the palettes

def build(theme, do_render=True):
    known = gm.znetscene_names()
    shapes = json.load(open(SHAPES)) if os.path.exists(SHAPES) else {}
    cur = CURATED[theme]
    problems, entries = [], []
    for c in cur["pieces"]:
        name = c["prefab"]
        e = dict(c)
        e["exists"] = name in known
        if not e["exists"]:
            problems.append(f"{theme}: {name} isn't in the game's ZNetScene")
            entries.append(e)
            continue
        try:
            lo, hi = bounds(name)
            e["size"] = [round(float(x), 2) for x in hi - lo]
            e["min"], e["max"] = [round(float(x), 2) for x in lo], [round(float(x), 2) for x in hi]
            e["origin"] = origin_of(lo, hi)
        except Exception as ex:
            problems.append(f"{theme}: {name}: no shape ({ex})")
        notes = [e["notes"]] if e.get("notes") else []
        e["hollow"] = bool(shapes.get(name))   # (a list of boxes; [] is a walk-through piece, not a hollow one)
        e["game_dungeon"] = game_dungeons(name)
        if e["game_dungeon"]:
            notes.append(f"recognisable: a part of the game's own {' and '.join(e['game_dungeon'])} dungeons")
        if name in SETS:
            e["sets"] = SETS[name]
        if name in MAX_SPAN:
            e["max_span"] = MAX_SPAN[name]
        info = inspect(name)
        if info:
            e.update(info)
        if e.get("size"):
            sz = e["size"]
            e["height"] = sz[1]
            thin = int(np.argmin(sz))
            e["upright"] = "flat" if thin == 1 and sz[1] < 0.5 * max(sz[0], sz[2]) else \
                           "upright" if thin != 1 and sz[thin] < 0.35 * max(sz) else "block"
        saved_only = e["role"] in ("container", "chest", "pickable", "spawner") or bool(info and info["kinds"] and
                     {"Container", "Pickable", "PickableItem", "CreatureSpawner", "SpawnArea"} & set(info["kinds"]))
        e["saved_only"] = saved_only
        e["safe"] = bool(e["exists"] and not saved_only and name not in AVOID and not (info and info["physics"])
                         and not (e["role"] in STRUCTURAL and info and not info["solid"]))
        if e["role"] in STRUCTURAL and info and not info["solid"]:
            problems.append(f"{theme}: {name} is a {e['role']} but players walk through it ({info['solid_note']})")
        if info and info["solid_note"]:
            notes.append(info["solid_note"])
        if info and info["lights"]:
            notes.append("its light range, fade distance and fire grow with its scale (BuildScale.cs)")
        if info and info["gives"] and e["role"] not in ("container", "chest", "pickable", "spawner"):
            notes.append(f"as scenery its {', '.join(info['gives'])} is stripped: it gives nothing")
        if info and info["physics"]:
            notes.append("has a physics body in the game: stripped, it stays where it's put")
        if info and [k for k in info["usable"] if k not in ("Chair", "Ladder")] and not saved_only:
            notes.append(f"usable in the game ({', '.join(k for k in info['usable'] if k not in ('Chair', 'Ladder'))}): as scenery the Builder "
                         "strips it, so it only looks the part")
        if e.get("size") and min(e["size"]) < 0.15 and max(e["size"]) > 0.5:
            notes.append(f"thin ({min(e['size'])} m): a single face may not show from behind (one-sided): check its render from both sides")
        try:
            e["climb"] = climb(name)
        except Exception as ex:
            problems.append(f"{theme}: {name}: climb not read ({ex})")
        top = (e.get("climb") or {}).get("top", 0.0)
        if top > 0 and e["mount"] != "structure":
            notes.append(f"climbable: a player can stand {top} m above its foot (at scale 1; {e['climb']['top_skilled']} m with Jump skill 100), "
                         f"so it's a stepping stone to a level up to about {top + STEP:.1f} m above its foot "
                         f"({e['climb']['top_skilled'] + STEP_SKILLED:.1f} m with Jump 100)")
        if name in TROUBLE:
            notes.append("TROUBLE: " + TROUBLE[name])
        e["notes"] = "; ".join(n for n in notes if n)
        if do_render:
            os.makedirs(RENDERS, exist_ok=True)
            out = os.path.join(RENDERS, name + ".png")
            if not os.path.exists(out):
                try:
                    render(name, out)
                except Exception as ex:
                    problems.append(f"{theme}: {name}: render failed ({ex})")
            e["render"] = "palettes/" + name + ".png"
        entries.append(e)
    data = {
        "theme": theme,
        "about": cur["about"],
        "made_by": "tools/dungeons/palette_tool.py (curation in " + cur.get("source", "CURATED there") + "; everything else read from the game's files)",
        "fields": {
            "role": "what it's for; structural roles (" + ", ".join(sorted(STRUCTURAL)) + ") must be solid",
            "mount": "ground: stands on a floor (prop_check.py); hang: needs something right above its origin or a chain on its top; "
                     "wall: against a wall; span: rests on two supports; structure: what others stand on; free: anywhere",
            "scale": "[min, max] that looks right and stays solid; stairs keep 1 (step height)",
            "tier": "the land it looks like it belongs to (1 Meadows .. 8 Deep North): a look hint, not gameplay (enemies and loot follow "
                    "the entrance's biome)",
            "size/min/max/origin": "its shape's box in its own frame (m); origin: where its origin sits on that box (foot, top, centre...)",
            "solid": "a character bumps into it in our rooms (the game's collision matrix; items fixed by BuildSolid.cs)",
            "lights": "range, intensity, colour; lit: shines once stripped (a Fireplace's burning objects are switched on); lod: fades with distance",
            "gives": "what the game would make it give or do: stripped as scenery; a networked prefab (networked) may be a Saved part instead",
            "height": "its height at scale 1 (m)",
            "climb": "ledges: its footholds (faces a player can stand on, no steeper than 38 degrees, with at least " + str(FOOT) + " m2 "
                     "of them seen from above in a " + str(BAND) + " m height band) as height ranges above its foot at scale 1, upright as "
                     "the prefab stands; top: the highest reachable from the floor, each within a jump (" + str(STEP) + " m: gravity 20, "
                     "jump force 8, Jump skill 0) of the last; top_skilled: the same with Jump skill 100 (" + str(STEP_SKILLED) + " m); 0: "
                     "none. Scale the ledges by the part's y scale; a part turned off upright exposes other faces (treat a smooth bar or "
                     "column as only its top end)",
            "upright": "flat (thin along y: a floor tile, stand it up for a wall), upright (thin along x or z: a wall piece, use as it is), block",
            "safe": "fine as stripped scenery; saved_only: works only as a Saved part (chests, pickables, spawners)",
            "by_role": "{role: [default, variants...]}: the roles below without their notes",
            "sets": "the dressing sets it suits (giant: ossuary, ruin, forge, archive, engine, armoury; Dvergr: store, workshop, tavern, barracks)",
            "max_span": "a panel's longest side (m) before its stretched texture smears",
            "game_dungeon": "the game's own dungeon themes whose rooms use it: a player knows it from there (a new kind should lean on pieces "
                            "with none, or mix them so no room reads as that dungeon)",
            "roles": "the same pieces by a theme recipe's roles (first = default, others = variants), with how each role's pieces are placed",
            "render": "a picture in the mesh cache (" + gm.CACHE + "); the theme's contact sheet is palettes/" + theme + "_sheet.png there",
        },
        "pieces": entries,
    }
    if theme in ROLES:
        here = {e["prefab"] for e in entries} | {e["prefab"] for e in CURATED["shared"]["pieces"]}
        roles = {}
        for role, names in ROLES[theme].items():
            for n in names:
                if n not in here:
                    problems.append(f"{theme}: role {role} names {n}, which isn't in this palette or shared")
            roles[role] = {"pieces": names}
            how = HOW_BY_THEME.get(theme, {}).get(role) or HOW.get(role)
            if how:
                roles[role]["how"] = how
        data["roles"] = roles
        data["by_role"] = {role: list(names) for role, names in ROLES[theme].items()}
    if theme == "shared":
        data["trouble"] = TROUBLE
    os.makedirs(OUT_DIR, exist_ok=True)
    path = os.path.join(OUT_DIR, theme + ".json")
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        json.dump(data, f, indent=1, ensure_ascii=False)
        f.write("\n")
    if do_render:
        sheet(theme, entries, os.path.join(RENDERS, theme + "_sheet.png"))
    return path, problems, entries


def probe(patterns, do_render):
    """The measured facts of any prefabs (exact names or regular expressions over ZNetScene's names), one JSON object each: what a pack team
    needs to choose pieces before they go into a palette."""
    import re
    known = sorted(gm.znetscene_names())
    names = []
    for pat in patterns:
        if pat in known:
            names.append(pat)
        else:
            names += [n for n in known if re.search(pat, n)]
    for name in dict.fromkeys(names):
        e = {"prefab": name}
        try:
            lo, hi = bounds(name)
            e["size"] = [round(float(x), 2) for x in hi - lo]
            e["origin"] = origin_of(lo, hi)
            e["height"] = e["size"][1]
        except Exception as ex:
            e["size"] = None
            e["error"] = str(ex)
        info = inspect(name)
        if info:
            e.update({k: info[k] for k in ("kinds", "solid", "solid_note", "lights", "fire", "item", "networked", "gives", "physics", "usable")})
        try:
            e["climb"] = climb(name)
        except Exception:
            pass
        e["game_dungeon"] = game_dungeons(name)
        e["avoid"] = name in AVOID
        if do_render and e.get("size"):
            out = os.path.join(RENDERS, name + ".png")
            os.makedirs(RENDERS, exist_ok=True)
            if not os.path.exists(out):
                try:
                    render(name, out)
                except Exception:
                    pass
            e["render"] = out
        print(json.dumps(e, ensure_ascii=False))
    return 0


def sync_walkthrough(entries):
    """Pieces players walk through (no solid collider) get no boxes in prefab_shapes.json ("name": []), so the offline walk and gap checks
    don't take their whole box for a wall (kelp curtains, cloth, webs, rugs). A prefab already there (a real shape) is never changed."""
    path = SHAPES
    shapes = json.load(open(path)) if os.path.exists(path) else {}
    added = []
    for e in entries:
        name = e["prefab"]
        if e.get("exists") and e.get("solid") is False and name not in shapes and e.get("size") and max(e["size"]) > 0:
            shapes[name] = []
            added.append(name)
    if added:
        json.dump(shapes, open(path, "w"), separators=(",", ":"))   # (prefab_shapes.py's own format)
        print("walk-through, no boxes in prefab_shapes.json:", ", ".join(added))


def main(argv):
    load_sources()
    do_render = "--no-render" not in argv
    if "--probe" in argv:
        return probe([a for a in argv if not a.startswith("--")], do_render)
    themes = [a for a in argv if not a.startswith("--")] or list(CURATED)
    all_problems = []
    for t in themes:
        if t not in CURATED:
            print(f"unknown theme {t}: {', '.join(CURATED)}")
            return 2
        path, problems, entries = build(t, do_render)
        sync_walkthrough(entries)
        lights = sum(1 for e in entries if e.get("lights"))
        items = [e["prefab"] for e in entries if e.get("item")]
        print(f"{t}: {len(entries)} pieces -> {path} ({lights} with lights{'; items: ' + ', '.join(items) if items else ''})")
        all_problems += problems
    for p in all_problems:
        print("PROBLEM", p)
    if do_render:
        print("renders:", RENDERS)
    return 1 if all_problems else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
