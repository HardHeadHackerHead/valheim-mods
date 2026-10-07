"""
The Bird Trap: a Viking fowler's box trap. A slatted wooden cage on a plank base, with a drop-door at the front held up by a forked
prop stick tied to the bait pedal inside. A bird hops in for the bait, knocks the pedal, the prop falls and the door drops.

Groups the game moves:
  door  - the slatted drop-door (raised when the trap is set, down when it has sprung)
  prop  - the forked stick holding the door up (shown only while set)
  bait  - berries and seeds on a leaf inside (shown while baited)
  bird  - the caught bird (shown while there is one)
The front faces -z. About 0.9 m wide, 0.6 m deep, 0.55 m tall.
"""
from modelkit import Model

W, D = 0.84, 0.58          # outer width (x) and depth (z) of the cage
BASE_Y = 0.07              # top of the plank base
H = 0.42                   # cage height above the base
STICK = 0.022              # slat thickness


PAL = {"Wicker": (0.68, 0.52, 0.31), "Grey": (0.64, 0.66, 0.70)}   # colours the kit does not have yet
LIFT = 0.3                 # how far the door is raised when the trap is set


def make(state="game"):
    """
    state: 'game' (what the mod builds: door up, prop, bait and the bird inside; the game shows and hides them), 'set' (door up, prop,
    bait), 'caught' (door down, the bird inside, the bait eaten), 'icon' (set, with a bird perched on the lid looking in).
    """
    m = Model()
    x0, x1, z0, z1 = -W / 2, W / 2, -D / 2, D / 2
    top = BASE_Y + H

    # --- runners and the plank base
    for z in (z0 + 0.07, z1 - 0.07):
        m.cyl("runner", 0, 0.03, z, 0.06, W / 2 + 0.04, 0.06, "Dark", rz=90)
    n = 5
    for i in range(n):
        x = x0 + (i + 0.5) * W / n
        m.box("basePlank", x, BASE_Y - 0.018, 0, W / n - 0.008, 0.036, D + 0.04, "Planks")
    for z in (z0 + 0.03, z1 - 0.03):
        m.box("baseBatten", 0, BASE_Y + 0.004, z, W - 0.02, 0.012, 0.035, "Dark")

    # --- corner posts and the top frame
    for x in (x0 + 0.02, x1 - 0.02):
        for z in (z0 + 0.02, z1 - 0.02):
            m.box("post", x, BASE_Y + H / 2, z, 0.045, H, 0.045, "Dark")
    for z in (z0 + 0.02, z1 - 0.02):
        m.box("frameX", 0, top, z, W, 0.04, 0.045, "Dark")
    for x in (x0 + 0.02, x1 - 0.02):
        m.box("frameZ", x, top, 0, 0.045, 0.04, D, "Dark")
    # a mid rail round the sides and back, for the slats to be lashed to
    for x in (x0 + 0.02, x1 - 0.02):
        m.box("midZ", x, BASE_Y + H * 0.5, 0, 0.03, 0.028, D - 0.04, "Dark")
    m.box("midBack", 0, BASE_Y + H * 0.5, z1 - 0.02, W - 0.04, 0.028, 0.03, "Dark")

    # --- slats: sides, back and top (light withies)
    wick = lambda k: "Wicker" if k % 2 else "Planks"      # withies of two shades, as cut from different saplings
    for k in range(7):
        z = z0 + 0.075 + k * (D - 0.15) / 6
        for x in (x0 + 0.02, x1 - 0.02):
            m.box("slatSide", x, BASE_Y + H / 2, z, STICK, H - 0.02, STICK, wick(k))
    for k in range(10):
        x = x0 + 0.075 + k * (W - 0.15) / 9
        m.box("slatBack", x, BASE_Y + H / 2, z1 - 0.02, STICK, H - 0.02, STICK, wick(k))
    for k in range(10):
        x = x0 + 0.075 + k * (W - 0.15) / 9
        m.box("slatTop", x, top + 0.012, 0, STICK, STICK, D - 0.03, wick(k + 1))
    # rope lashings on the top corners
    for x in (x0 + 0.02, x1 - 0.02):
        for z in (z0 + 0.02, z1 - 0.02):
            m.box("cornerLash", x, top - 0.01, z, 0.06, 0.05, 0.06, "Leather")
    # lashings where the slats cross the mid rail (little leather wraps)
    for z in (z0 + 0.075, z1 - 0.075):
        for x in (x0 + 0.02, x1 - 0.02):
            m.box("lash", x, BASE_Y + H * 0.5, z, 0.04, 0.04, 0.03, "Leather")

    # --- the door guides at the front: two grooved uprights that stand taller than the cage
    for x in (x0 + 0.02, x1 - 0.02):
        m.box("guide", x, BASE_Y + (H + LIFT + 0.04) / 2, z0 - 0.005, 0.05, H + LIFT + 0.04, 0.035, "Dark")
        m.sph("guideKnob", x, BASE_Y + H + LIFT + 0.06, z0 - 0.005, 0.07, 0.06, 0.07, "Dark")
    m.box("guideTop", 0, BASE_Y + H + LIFT + 0.04, z0 - 0.005, W + 0.02, 0.04, 0.05, "Dark")

    # --- the drop-door: a slatted panel sliding in the guides; its origin is its bottom edge
    door_up = state != "caught"
    m.begin_group("door", 0, BASE_Y + (LIFT if door_up else 0.0), z0 - 0.03)
    dh = H - 0.03
    for k in range(9):
        x = x0 + 0.085 + k * (W - 0.17) / 8
        m.box("doorSlat", x, dh / 2, 0, STICK + 0.004, dh - 0.01, STICK, "Wicker" if k % 2 else "Planks")
    for y in (0.025, dh - 0.025):
        m.box("doorBar", 0, y, -0.006, W - 0.08, 0.04, 0.03, "Dark")
    for sx in (-1, 1):
        m.box("doorBrace", sx * 0.17, dh / 2, -0.01, 0.025, dh * 0.9, 0.02, "Dark", rz=sx * 52)
    m.cyl("doorRing", 0, dh + 0.005, -0.012, 0.05, 0.006, 0.05, "Iron", rx=90)
    m.end_group()

    # --- the prop: a forked stick under the door's bottom bar, its foot on the base, its cord running back to the bait pedal
    if state != "caught":
        m.begin_group("prop", 0.0, BASE_Y, z0 - 0.07)
        m.cyl("propStick", 0, LIFT / 2 - 0.01, 0, 0.026, LIFT / 2 - 0.01, 0.026, "Planks", rx=-6)
        m.cyl("propFork", -0.018, LIFT - 0.01, -0.005, 0.016, 0.035, 0.016, "Planks", rz=30)
        m.cyl("propFork", 0.018, LIFT - 0.01, -0.005, 0.016, 0.035, 0.016, "Planks", rz=-30)
        m.cyl("cord", 0, 0.16, 0.12, 0.008, 0.2, 0.008, "Leather", rx=58)
        m.end_group()

    # --- the bait pedal (always there) and the bait on it
    m.box("pedal", 0, BASE_Y + 0.012, 0.03, 0.18, 0.016, 0.12, "Planks")
    m.cyl("pedalPin", 0, BASE_Y + 0.025, 0.09, 0.02, 0.012, 0.02, "Dark")
    if state != "caught":
        bait(m)

    # --- the bird: a little grey-and-white gull, inside (caught) or perched on the lid (icon)
    if state in ("game", "caught"):
        bird(m, 0.02, BASE_Y + 0.004, -0.02, 160)
    elif state == "icon":
        bird(m, 0.16, BASE_Y + H + LIFT + 0.06, -D / 2 - 0.005, 205)   # perched on the door frame's crossbar, looking down at the bait

    # --- dressing: flat stones weighing the lid down
    m.sph("weightStone", 0.1, top + 0.045, 0.1, 0.22, 0.075, 0.16, "Stone", ry=20)
    m.sph("weightStone2", 0.2, top + 0.04, 0.16, 0.12, 0.05, 0.1, "StoneDark", ry=-30)
    return m


def bait(m):
    m.begin_group("bait", 0, BASE_Y + 0.022, 0.03)
    m.sph("leaf", 0, 0, 0, 0.16, 0.012, 0.1, "Green")
    berries = [(-0.03, 0.01), (0.02, -0.015), (0.035, 0.02), (-0.01, -0.03), (0.0, 0.025)]
    for i, (bx, bz) in enumerate(berries):
        m.sph("berry", bx, 0.018, bz, 0.03, 0.03, 0.03, "Red")
    for bx, bz in ((-0.05, -0.01), (0.055, -0.02), (-0.045, 0.03), (0.06, 0.03)):
        m.sph("seed", bx, 0.01, bz, 0.018, 0.012, 0.026, "Leather")
    m.end_group()


def bird(m, x, y, z, yaw, k=1.3):
    """A small grey-and-white gull, standing; (x, y, z) is where its feet are; k scales it."""
    def sph(n, a, b, c, sx, sy, sz, mat, **r): m.sph(n, a * k, b * k, c * k, sx * k, sy * k, sz * k, mat, **r)
    def box(n, a, b, c, sx, sy, sz, mat, **r): m.box(n, a * k, b * k, c * k, sx * k, sy * k, sz * k, mat, **r)
    def cyl(n, a, b, c, sx, sy, sz, mat, **r): m.cyl(n, a * k, b * k, c * k, sx * k, sy * k, sz * k, mat, **r)
    m.begin_group("bird", x, y, z, ry=yaw)
    m.begin_group("birdBody", 0, 0, 0)      # (the part that bobs and turns its head)
    sph("body", 0, 0.085, 0.005, 0.12, 0.1, 0.2, "Cream", rx=-12)
    sph("back", 0, 0.105, 0.03, 0.115, 0.06, 0.16, "Grey", rx=-12)
    sph("head", 0, 0.16, -0.075, 0.075, 0.072, 0.08, "Cream")
    box("beak", 0, 0.153, -0.122, 0.018, 0.018, 0.045, "Gold", rx=6)
    box("beakTip", 0, 0.149, -0.142, 0.014, 0.016, 0.01, "RedDark")
    for sx in (-1, 1):
        sph("eye", sx * 0.03, 0.17, -0.098, 0.014, 0.014, 0.014, "Black")
        sph("wing", sx * 0.055, 0.1, 0.03, 0.03, 0.07, 0.17, "Grey", rx=-14)
        sph("wingTip", sx * 0.045, 0.112, 0.115, 0.022, 0.03, 0.08, "Black", rx=-20)
    box("tail", 0, 0.105, 0.115, 0.06, 0.014, 0.06, "Black", rx=-16)
    m.end_group()
    for sx in (-1, 1):
        cyl("leg", sx * 0.022, 0.02, 0.0, 0.011, 0.024, 0.011, "Gold")
        box("foot", sx * 0.022, 0.003, -0.012, 0.03, 0.006, 0.035, "Gold")
    m.end_group()


if __name__ == "__main__":
    import sys, os
    from modelkit import contact_sheet
    out = sys.argv[1] if len(sys.argv) > 1 else "."
    states = sys.argv[2:] or ["set", "caught", "icon"]
    for st in states:
        contact_sheet(make(st), os.path.join(out, f"birdtrap_{st}.png"), size=(520, 520), target=(0, 0.32, 0), dist=2.6,
                      views=((28, 22), (-35, 30)), palette=PAL)
    print("ok")
