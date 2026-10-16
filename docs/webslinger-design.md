# Quad's Webslinger: the design (started 2026-10-15)

A Spider-Man mod for Valheim: swing through the forests on webs, zip to rooftops, crawl up cliffs and longhouses, feel danger coming,
and fight with webs. New mod in the valheim-mods repo, `E:\valhiem\mods\Webslinger` (folder, csproj and DLL all `Webslinger`), GUID
`com.quad.webslinger`, page title "Quad's Webslinger".

**The name.** "Spider-Man" is Marvel's trademark: Thunderstore can take down a mod named after it. The mod is called Webslinger, and its
page can say "Spider-Man style" in the description (search words) without using the name as the title. Rename before the first release if
wanted: only the page title and the words change (never the GUID once released).

## The fantasy, in Valheim's terms

You are a Viking bitten by something in the Black Forest. You can:
1. **Swing** from webs: trees, cliffs, rock arches, your own longhouse. Momentum is everything: fall, swing, release at the top, fly.
2. **Zip** to a point: aim at a roof edge, a branch, a cliff top, and pull yourself there in an instant (Spider-Man's point launch).
3. **Crawl** on walls: stick to any solid surface you hit in the air, crawl along it, kick off it.
4. **Sense danger**: your spider-sense tingles a moment before an attack lands or an arrow hits.
5. **Fight with webs** (phase 2): web a draugr's feet, yank a troll's club away, pull a greyling to you, cocoon a boar.

It must feel like the games: forgiving anchors (you never think about where the web sticks), speed that builds, never scraping the
ground, and a release that flings you upward.

## Controls (no new keys)

Folded into the game's own actions, as our other mods do (memory: no extra hotkeys). The game does nothing with these in the air.

| Action | Key | Where |
|---|---|---|
| **Swing** | Press and hold **Jump** in the air | A web shoots up and ahead to the best anchor; keep holding and the next web fires by itself at the top of each arc. Let go to fly off with a boost. Nothing solid in reach: no web, the press stays the game's. A jump off the ground is a **Spider-Man jump** (higher, no web). |
| **Zip** | **Sprint** pressed in the air (or while swinging / on a wall) | Pulls you to the point under the crosshair (a top: land on it; a wall: stick to it). Nothing under the crosshair in reach: a short web-zip forward. |
| **Crawl** | Move into a wall while in the air | Stick to it. W/S up and down, A/D along it, Sprint faster, **Jump** kicks off (away and up, the way you look), **Crouch** drops. Up over the top: pulled onto it. |
| **Combat** (phase 2) | The **Web Shooters** in your hands | Attack: web blast. Secondary: web yank. Block: web shield. |

Gamepad: the same buttons (JoyJump, JoyRun = left stick press).

## Research and the rework (2026-10-15, after the first try)

How the games do it: Spider-Man 2 (2004), Ultimate Spider-Man (2005) and Insomniac's games attach webs to real buildings and trees;
The Amazing Spider-Man games swing from thin air. Insomniac's is "hold R2 to go": hold to keep swinging, strong steering mid-swing
(weight shift), hitting a building turns into a wall run, no fall damage, web-zip catches a fall.

The first build's swings ended in "landed" at 25-30 m/s (and the player died): the rope started at full length, so a web to a roof
15 m up and 28 m away swung the body through an arc 13 m underground. Now each anchor gets a rope length that keeps the bottom of its
arc 2 m above the ground under it, and the rope reels in to it (the yank). Also: hold to keep swinging, steering (SwingSteer),
a forward push when a web sticks, wall hit mid-swing = crawl, no damage landing from web moves (WebLandingDamagePercent 0), no web
when nothing is in reach (SkyWebs off: the player prefers real anchors), the Spider-Man jump (SpiderJumpPercent), and no stamina for
now (all costs 0 by default, at the player's request).

### Second pass (2026-10-16, from the records of the user's second try)
The records showed swings at 31-35 m/s within half a second (the pump pushed 6-12 m/s each second with no limit but MaxSpeed, and the
reel-in pulled at up to 18 m/s), a swing flown straight into a tree, and Jump presses in the air with nothing to web to. Changed:
- **Cruising speed** (SwingSpeed 20, 26 at skill 100): pumping builds speed up to it and eases off near it; faster (a dive) the air
  brings it back slowly (20% of the excess a second on a web, gentler in flight above cruise + 4). MaxSpeed stays the hard ceiling.
- **Gentler reel**: 10 m/s (was 16), the rope's pull back onto its length at most 10 m/s (was 18). Anchors needing a long reel score lower.
- **Swing pivot out from the surface** (Spot.Hold): 0.8 m out from a trunk or wall, so the body hanging under it misses it.
- **Virtual pivot** (Anchors.Virtual, Bend 0.65): the body swings under a point moved most of the way toward its own path; the web is
  still drawn to the real spot. An anchor to the side swings you on (as down a street between buildings), not round it.
- **Arc check** (Anchors.ArcIsClear): the body's way down to the bottom of the arc and on past it, swept with a 0.35 m ball; a swing
  with something in the way scores 30 lower (still taken when it is the only one: hitting the wall turns into a crawl).
- **Web marks** (each player's WebMarks, Marks.cs): in the air, a small web drawn over the spot a swing would stick to (no mark: nothing
  in reach); a ring over the zip spot under the crosshair when a zip reaches it.

### Third pass (2026-10-16): sounds and taps
- **Own sounds** (Sounds.cs), made in code at start (no recordings): thwip (shot), a longer one for zips, thunk (stick), cling, snap
  (let go), whoosh (perfect release), kick, spider-sense tingle, and a wind loop that rises with speed. Through the game's SFX mixer
  group; SoundVolume setting. `web sound [name]` plays them.
- **A tap swings**: records showed swings let go 0.1 s after the press (before the web stuck). Now a released Jump keeps the web until
  the body rises out of the bottom of the arc (after 0.35 s) or 1 s; the next web of a held swing only while Jump was held all along.
- **Lower spots count**: Clear 1.5 m (was 2), MinRope 5 m (was 6): a spot about 8 m above the ground is enough (was about 10).
- **Why no swing**: `web status` gives whyNoSwing, and refusals in lastMoves say why (no surface in reach, only spots behind,
  spots too low).

### Parkour took Space (2026-10-16)
Parkour's records showed Space presses in the air beside buildings taken as its wall kick (and its auto landing rolls, which only act
on landing). Now our SetControls prefix runs first (Priority.High, 600, against Parkour's 200): a Jump in the air that can be a web is
ours; any other goes on to Parkour and the game; nothing is ours while a Parkour move runs (Compat.Busy). (Jump zipping to the ring when no swing spot was there was tried and taken
out the same day: the user found swings turning into zips. Jump only ever swings; zips are Sprint's.)

## Phase 1: movement (this build)

### Swing
- **Anchor**: casts in a fan up and ahead of the camera's look (30°-75° up, ±45° around the look), up to the web's reach (40 m at
  skill 0, 60 m at 100). The best hit is the one highest ahead along the look; anything solid that stays put counts (trees, rocks,
  cliffs, built pieces; never creatures, water, triggers or things that move). Nothing in reach: no web (a "nothing to web to" puff), unless
  the server turns on **SkyWebs** (an anchor 25 m up and ahead, for the open plains; off by default).
- **Physics**: a rope that only pulls (it never pushes). Gravity does the swinging; each physics step the part of the velocity that would
  stretch the rope is taken away. Holding forward pumps the swing (a few m/s² along the arc, the way you look), A/D steer the swing's
  plane. The rope shortens a little at the start (a tug) and whenever the bottom of the arc would scrape the ground (keeps 2 m clear).
  Speed is capped (server setting, 35 m/s).
- **Release**: let go of Jump: the speed carries on, plus a fling (up 4 m/s, and 10% more along the swing). A release while rising near
  the top of the arc is a **perfect release**: a bigger fling, a whoosh, and more skill.
- **Chain**: press Jump again in the air for the next web. Each web raises the Webslinging skill.
- **Body**: leans along the rope (feet out toward the swing, head toward the anchor), arm up (the game's own "grappling" pose, which the
  game shares with every player).

### Zip (point launch)
- Aim at a spot within 35 m (50 m at skill 100). Pulled straight to it at 30 m/s (gravity off), web drawn from the hand.
- Arrive on a top (a roof, a rock, a branch): a small hop and you stand there (**perch**). On a wall: stick to it (into the crawl).
- Nothing in reach: a forward web-zip (a burst along the look, 2 per air time, 3 from skill 50).
- Fall height starts again at each zip and each web (the web catches you).

### Crawl
- Moving into a steep surface (within 35° of upright) in the air: stick, facing the wall, gravity off.
- Move along the wall's plane at 3 m/s (5 sprinting); stays a body's width off the wall by short casts each step; follows the wall round
  corners up to 45°; at a top, pulled up onto it as Parkour's mantle does.
- Stamina: a little per second while moving (none while still). Out of stamina: you slide off.
- Jump: kick off, away from the wall and toward the look. Crouch: let go.
- **With Parkour installed**: Parkour's moves run first (sprint + jump at a wall is its wall run / climb); the crawl takes walls only when
  Parkour isn't in a move.

### Landing
- Superhero agility: falls hurt less (server setting, half the game's damage by default).

### Spider-sense
- A hostile creature within 30 m starting an attack aimed at you, or a projectile flying at you that would arrive within 1 s: a
  **tingle**. Wavy lines around your head (as in the comics), a soft vignette on the side it comes from, a sound.
- Once per threat; not for attacks from in front of you that you can see (setting: "Only from behind / everywhere").
- Gives a short **perfect-dodge** window (phase 1.5): a dodge within 0.5 s after the tingle costs no stamina.

### Webslinging skill
A new skill (Shared/Skill.cs, id `com.quad.webslinger`). Every web, zip and crawl raises it. Its factor gives longer webs, faster zips,
cheaper moves, more air zips, bigger perfect releases.

### Stamina
| Move | Cost |
|---|---|
| A swing web | 4 to start, 2 per second |
| A zip | 8 |
| Crawl | 3 per second moving |
| Air zip | 10 |
The skill cuts them by up to a third (server setting). Out of stamina: no new webs; a swing in progress lasts until Jump is let go.

### Multiplayer
Movement is each player's own game's job: no server part. Others see the body (rotation and position sync) and the "grappling" arm pose
without the mod. With the mod, they also see the web: the anchor is shared on the player's ZDO (`webslinger_web`: a version-tagged state,
anchor and kind), written only when a web starts or ends; "no web" is written, never removed (pitfall: removed keys stay on other games).
Strengths and costs are the server's (Shared/ServerSettings.cs); looks and feel each player's own.

### Limits
No webs while dead, swimming, attached (chair, cart, zipline), riding, encumbered, in a dungeon (closed rooms: setting), building (hammer
out), or in debug fly. Not while typing or in a menu.

## Look pass (2026-10-16)
- **Poses** (Body.cs): two-bone reaches over the animation (humanoid bones, found: LeftArm/LeftForeArm/LeftHand, LeftUpLeg/LeftLeg/
  LeftFoot): swing = web arm straight up the web, the other out and back, legs trailing and tucked as the swing rises; zip = both arms
  up the web; fly = arms out; crawl = hand over hand, knees out. Flips on a perfect release (front flip, a corkscrew every third), the
  visual turned round the body's middle. Shared: `webslinger_move` (version*10 + Move) and `webslinger_flips` on the ZDO.
- **Hands**: the web leaves from the hand on the anchor's side (straight ahead: the other hand from the last web). `web hand auto|left|right`.
- **Web**: thicker at the hand (a width curve). **Splats** (Fx.cs): a web patch where a web sticks, 5 s then fading (WebSplats).
- **Speed lines** from 15 m/s, streaming from where you're heading (SpeedLines). **Camera**: roll into turns and toward the web, a dip
  at the bottom of a swing (CameraMotion; a postfix on GameCamera.UpdateCamera, which places the camera afresh each frame).
- Test commands: `web bones`, `web flip`, `web splat` (looks only, nothing moves).
- **More webs** (the user: "I want to see more spider webs"): a web let go stays stuck and hangs from its spot as a loose swaying rope
  (12-point verlet, up to 10 m, 24 at most), fading after HangingWebSeconds (10); WebsLeftHanging setting. Webs about twice as thick (0.065 m) and brighter (emission 0.55).
  Splats last 12 s, 30 at most.

## Movement backlog (the user wants these later, 2026-10-16: "keep those on the back burner but don't forget them")
1. **Keep your speed on landing**: a fast landing goes into a run (or a forward roll that keeps the speed) instead of stopping dead.
2. **Zip launch** (Insomniac's point launch): Jump just as a zip arrives at an edge fires you up and forward off it.
3. **Reel in on the rise**: still holding Jump as the swing climbs, the rope shortens a little: a higher fling at the top.
4. **Wall run with Sprint**: Sprint held on a wall turns the crawl into a fast run up it, flipping over the top edge.

## Phase 2: combat (the Web Shooters)
A craftable item (Workbench: leather scraps, resin, linen thread / bronze), equipped in the hands like a fist weapon:
- **Web blast** (attack): a fast web ball; on a creature: **Webbed** (slowed 60% for 4 s, stacks to **Stuck**: rooted 2 s); on the
  ground: a sticky patch for 10 s.
- **Web yank** (secondary): small creatures (boar, neck, greyling, deer) are pulled to you and staggered; big ones (troll, abomination)
  pull **you** to them, ending in a flying kick; a creature's weapon... the game's creatures have no droppable weapons, so a yank on an
  armed humanoid (draugr, fuling) staggers and disarms it for 3 s instead.
- **Web shield** (block): a web wall that catches arrows and spit.
- **Cocoon** finisher: a webbed creature under 20% health, hit with a web blast: wrapped and helpless (kills at no risk; trophies drop as
  usual).
- **Web bomb** (phase 2.5): crafted from resin + web fluid, thrown; webs everything in 4 m.
- Status effects must be registered (reload level `menu`, the pitfalls on registering items): a creature owned by a player without the mod
  wouldn't get a custom effect, so web effects ride on the game's own slow (the tar "Tared" effect) as a fallback.

## Phase 3: the rest of being Spider-Man
- **Suits**: a Spider-Viking armour set (red and blue tunic, mask helmet; set bonus: webs reach further, spider-sense range); the black
  suit (Mistlands: night vision, stronger yanks); the stealth suit. Made with the game's own armour meshes and new materials.
- **Web building**: web bridges between two points (a rope you walk on, crafted with the hammer), web hammock (rest bonus), web net (catches
  birds and fish), web parachute (crouch while falling: glide).
- **Web wings**: a glide (as the Feather Cape, steerable) after a perfect release.
- **Wall ceilings**: crawl on ceilings and the undersides of arches (needs the body upside down and the camera to cope).
- **Swing tricks**: flips and spins on release (the dodge roll in the air), trick combo that feeds the skill.
- **Challenges**: swing races through rings (checkpoints placed by a hammer piece; times shared on a board, with Arena), collectible
  "lost web cartridges" hidden on high places.
- **Friends**: web a friend and pull them to you (with their consent: they must be in your party / group), web-tether two players.
- **Custom animations** later (the game has no swing animation: the grappling pose for now).

## Clash notes
- **Parkour** (ours): stand down while a Parkour move runs; our SetControls prefix runs before Parkour's (Priority.High): a jump in
  the air that can be a web is ours, any other is Parkour's (its wall kick).
- **Grappling hook** (the game's): no web while `GrapplingPoint.m_localGrappler` is set.
- Double-jump mods (EpicLoot's Double Jump, others): they take Jump in the air too. Detect and leave the first air press to them? Decide
  after a test; a setting "Swing on: Jump / Sprint" may be needed.
- SmoothClimbing, Just_Vault, Climbing: the crawl could fight their wall code: stand down when they are in a climb (look them up with
  valheim-compat before release).

## Tests
In the ClaudeTest world, near trees and a tall build. `web status`, `web try swing|zip|crawl` (pressing the keys through the same input
path), `web watch` for page shots.
