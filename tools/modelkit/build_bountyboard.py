"""Regenerates the Bounty Board's C# model, build-menu icon and cover picture. Run from this folder."""
import os
from modelkit import render, icon, contact_sheet
import bountyboard

out = os.path.join("..", "..", "mods", "BountyBoard")
os.makedirs(out, exist_ok=True)
m = bountyboard.make()
open(os.path.join(out, "ModelData.cs"), "w", encoding="utf-8").write(m.to_csharp("BountyBoard"))
icon(m, os.path.join(out, "icon.png"), px=256, yaw=22, pitch=12, target=(0, 1.45, 0), dist=8.0)
cover = render(m, size=(640, 360), yaw=24, pitch=10, target=(0, 1.35, 0), dist=7.4, scale=2,
               bg=((78, 92, 84), (24, 30, 28)))
cover.convert("RGB").save(os.path.join(out, "cover.png"), optimize=True)
print("parts:", len(m.parts))
