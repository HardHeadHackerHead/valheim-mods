"""Regenerates the Ledger Chest's C# model, build-menu icon and cover picture. Run from this folder."""
import os
from modelkit import render, icon
import ledgerchest

out = os.path.join("..", "..", "mods", "LedgerChest")
os.makedirs(out, exist_ok=True)
m = ledgerchest.make()
open(os.path.join(out, "ModelData.cs"), "w", encoding="utf-8").write(m.to_csharp("LedgerChest"))
icon(m, os.path.join(out, "icon.png"), px=256, yaw=28, pitch=24, target=(0, 0.55, 0), dist=4.0, palette=ledgerchest.PAL)
cover = render(m, size=(640, 360), yaw=30, pitch=18, target=(0, 0.55, 0), dist=3.5, scale=2, palette=ledgerchest.PAL,
               bg=((70, 62, 50), (26, 22, 18)), floor=(74, 66, 52))
cover.convert("RGB").save(os.path.join(out, "cover.png"), optimize=True)
print("parts:", len(m.parts))
