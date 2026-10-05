"""Regenerates the Slot Machine's C# model, reel pictures, build-menu icon and cover picture. Run from this folder."""
import os, shutil
from modelkit import render, icon
import symbols, slotmachine

out = os.path.join("..", "..", "mods", "SlotMachine")
os.makedirs(out, exist_ok=True)
symbols.atlas("symbols.png")
shutil.copy("symbols.png", os.path.join(out, "symbols.png"))
m = slotmachine.make()
open(os.path.join(out, "ModelData.cs"), "w", encoding="utf-8").write(m.to_csharp("SlotMachine"))
icon(m, os.path.join(out, "icon.png"), px=256, yaw=20, pitch=10, target=(0, 1.3, 0), dist=8.2, atlas="symbols.png")
cover = render(m, size=(640, 360), yaw=26, pitch=9, target=(0, 1.3, 0), dist=6.2, scale=2, atlas="symbols.png", bg=((60, 34, 40), (20, 12, 16)))
cover.convert("RGB").save(os.path.join(out, "cover.png"), optimize=True)
print("parts:", len(m.parts))
