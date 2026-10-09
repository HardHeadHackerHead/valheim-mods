"""Render one view of Layout.json: python view.py name yaw pitch tx ty tz dist [filter_z_lt]"""
import json, sys, os
sys.path.insert(0, r"D:\SteamLibrary\steamapps\common\Valheim\BepInEx\blueprints\tools")
from blueprint import Pieces
from preview import render
P = Pieces(r"D:\SteamLibrary\steamapps\common\Valheim\BepInEx\blueprints\_pieces.json")
L = json.load(open(os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'Layout.json')))
name, yaw, pitch, tx, ty, tz, dist = sys.argv[1], *map(float, sys.argv[2:8])
items = [it for it in L['pieces'] if P.has(it['p'])]
if len(sys.argv) > 8:
    zmax = float(sys.argv[8]); items = [it for it in items if it['z'] < zmax]
render(items, P, os.path.join('renders', name + '.png'), views=((yaw, pitch),), size=(1400, 800), target=(tx, ty, tz), dist=dist)
