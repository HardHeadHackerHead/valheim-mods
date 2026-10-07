"""Regenerates the Bird Trap's C# model, build-menu icon and cover picture. Run from this folder."""
import os
from modelkit import render, icon
import birdtrap

out = os.path.join("..", "..", "mods", "BirdTrap")
os.makedirs(out, exist_ok=True)
game = birdtrap.make("game")
open(os.path.join(out, "ModelData.cs"), "w", encoding="utf-8").write(game.to_csharp("BirdTrap"))
pose = birdtrap.make("icon")
icon(pose, os.path.join(out, "icon.png"), px=256, yaw=30, pitch=20, target=(0, 0.46, 0), dist=3.1, palette=birdtrap.PAL)
cover = render(pose, size=(640, 360), yaw=32, pitch=16, target=(0, 0.44, 0), dist=2.9, scale=2, palette=birdtrap.PAL,
               bg=((86, 110, 92), (30, 40, 34)), floor=(74, 88, 56))
cover.convert("RGB").save(os.path.join(out, "cover.png"), optimize=True)
print("parts:", len(game.parts))
