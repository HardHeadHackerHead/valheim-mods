"""The Bone Choir's entrance look: writes mods/Dungeons/packs/looks/bonechoir.json and a picture of it (bin/gate/bonechoir/entrance_look.png)."""
import json, sys
sys.path.insert(0, r"E:\valhiem\tools\dungeons")
import numpy as np
from dungeon_parts import Room, box, bounds, r3
from dungeon_view import Scene

r = Room("look", (40, 30, 40))
STONE = "Ashlands_Ruins_Floor_6x6"
# the facade: a gabled wall of pale ruin stone with the doorway (4 x 6.5) in it
for x0, x1 in ((-6.0, -2.2), (2.2, 6.0)):
    box(r, STONE, ((x0 + x1) / 2, 4.0, -0.9), (x1 - x0, 1.0, 9.5), tilt=(-90, 0))
box(r, STONE, (0, 7.6, -0.9), (4.4, 1.0, 2.3), tilt=(-90, 0))
r.add("Ashlands_Ruins_Wall_Top_wHole", (0, 8.6, -0.9), (0, 0, 0), (2.0, 1.4, 1.0))            # the rose gable over it
# the door: a dark recess behind a wrought iron screen
box(r, "blackmarble_floor_large", (0, 3.25, -1.25), (4.4, 6.5, 0.3))
for x in (-1.0, 1.0):
    box(r, "iron_grate", (x, 3.25, -0.75), (2.0, 6.5, 0.12))
# the portal: long-bone posts, a tympanum, the pointed arch, a skull keystone
for x in (-2.6, 2.6):
    box(r, "LargeBone", (x, 2.3, -0.2), (0.9, 4.6, 0.9))
lo, hi = bounds("Ashlands_Arch2")
for yaw in (0, 180):
    r.add("Ashlands_Arch2", (0, 4.5, -0.25), (0, yaw, 0), (3.2 / hi[0], 3.6 / hi[1], 0.45))
box(r, "AsksvinCarrionSkull", (0, 8.2, 0.0), (1.2, 0.75, 0.9), yaw=180)
# the bone archway: a vast ribcage half buried, its ribs arching over the way to the door
r.add("giant_ribs", (0, -2.2, 3.4), (0, 0, 0), (1.0, 1.0, 0.8))
# buried behind: a mound of rock over the cathedral's back
r.add("rock4_heath", (0, -2.0, -13.0), (0, 0, 0), (0.85, 0.9, 0.6))
r.add("rock4_heath", (-9.0, -5.0, -6.0), (0, 70, 0), (0.45, 0.5, 0.45))
r.add("rock4_heath", (9.0, -5.0, -6.0), (0, -70, 0), (0.45, 0.5, 0.45))
# the cracked bell, hung under the ribs on a chain; its broken lip half buried by the path
lo, hi = bounds("Bell")
r.add("Bell", (0.3, 4.2, 4.0), (0, 20, 11), (2.4, 2.4, 2.4))
lo, hi = bounds("Morkhalla_WallChain1")
s = 2.0 / (hi[1] - lo[1])
r.add("Morkhalla_WallChain1", (0.3, 7.1 - hi[1] * s, 4.0), (0, 0, 0), (0.6, s, 0.6))
r.add("Bell", (3.6, -0.5, 4.8), (64, 35, 20), (1.4, 1.4, 1.4))
# the path of pale slabs, candles and torches either side, bones at the ribs' feet
for z in (2.5, 7.5):
    box(r, STONE, (0, -0.15, z), (4.5, 0.3, 4.8))
for x in (-2.8, 2.8):
    for i, (dx, dz, sc) in enumerate(((0, 0, 5.0), (0.45, 0.4, 4.0), (-0.35, 0.5, 3.2))):
        r.stand("Candle_resin", (x + dx * np.sign(x), 0, 1.2 + dz), scale=(sc, sc, sc))
    r.stand("piece_groundtorch_wood", (x * 1.15, 0, 2.4), scale=(1.2, 1.2, 1.2))
r.stand("skull_pile", (-5.2, 0, 3.0), yaw=40, scale=(1.4, 1.4, 1.4))
r.stand("bone_stack", (5.3, 0, 1.5), yaw=-30, scale=(1.5, 1.5, 1.5))
r.stand("Skull1", (-4.2, 0, 6.5), yaw=160, scale=(2.5, 2.5, 2.5))
look = {
    "name": "bonechoir",
    "about": ("The Cathedral of the Bone Choir: a pale ruin-stone facade with a rose gable, its doorway a wrought iron screen in a pointed "
              "arch on long-bone posts with a beast's skull for keystone, under the arching ribs of a vast half-buried ribcage. A great "
              "cracked bell hangs from the ribs over the way in, its broken lip half buried by the path; candles and torches either side, "
              "skulls and bones at the ribs' feet, a mound of rock over the back. The door is at z 0, 1 m inside the ribs' front."),
    "front": 5,
    "parts": [p.json() for p in r.parts],
    "inside": [
        {"prefab": "blackmarble_floor_large", "at": [0, 3.5, -0.45], "scale": [0.55, 3.5, 0.04]},
        {"prefab": "iron_grate", "at": [-1.0, 1.15, -0.15], "scale": [1.0, 2.33, 1.0]},
        {"prefab": "iron_grate", "at": [1.0, 1.15, -0.15], "scale": [1.0, 2.33, 1.0]},
    ],
}
json.dump(look, open(r"E:\valhiem\mods\Dungeons\packs\looks\bonechoir.json", "w", newline="\n"), indent=1)
sc = Scene(); r.scene(sc, marks=False, tint=False)
sc.render(r"E:\valhiem\tools\dungeons\bin\gate\bonechoir\entrance_look.png", views=((180, 12), (210, 25), (90, 10)), size=(800, 600))
