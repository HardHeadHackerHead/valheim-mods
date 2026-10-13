# Quad's Blockscript on a multiplayer server (design and contract, 2026-10-12)

> **Status (2026-10-12):** design and contract for **Quad's Blockscript** on a multiplayer server. Built and unit-tested; **not yet run between two real games**. See the status section at the end.

Today scripts run only on the **server** (single player, the host of a listen server, or a dedicated server) and can only change the
character whose game is the server's own. Other players' characters, inventories and screens belong to their own games, so a script can only
reach them by **asking their game to do it**. This file is the contract for making that work, for two agents building in parallel.
The existing mechanism to copy is `mods/Shared/ServerSettings.cs`: a **direct connection** per player (`ZNetPeer.m_rpc`, whose sender
cannot be forged, unlike a routed RPC), a hello handshake, the admin check on that connection, nobody ever kicked.

## 1. Who has what

| Game | Has the mod | What it is for |
|---|---|---|
| **Server** (single player, listen host, dedicated) | yes (the scripts run here) | runs scripts, owns the lists and limits, answers admins |
| **Player with the mod** (an *agent*) | yes | does what the server asks to its own character, after checking it; reports what the server cannot see; forwards events only its game can see |
| **Player without the mod** | no | still sees everything vanilla: spawned creatures, weather, messages (`Player.Message` is a vanilla RPC), the world. Actions that need their game (items, health, teleport...) are **refused** with a reason that names the player ("needs Quad's Blockscript on their game") |

A server without the mod: nothing happens (no scripts run). Players with the mod on a server without it: the editor says so and works
only as a local tool (single player rules).

## 2. The wire (`mods/Scripts.Engine/Mp/Wire.cs`, transport owned by the CORE agent)

All messages travel on the direct connection as one `ZPackage` holding a string (JSON, gzipped over 1 KB, 256 KB at most, split by the
transport if needed). One RPC name in each direction: `blockscript.m`. A message is `{"k": kind, "i": id, "d": {...}}`. `id` ties a reply to
its request (a counter). Everything is versioned by `Protocol` (an int in hello); a peer speaks to the lower of the two versions and ignores
kinds it doesn't know.

The public surface everyone codes against (the CORE agent writes the body; the ADMIN agent only uses it):

```csharp
internal static class Wire
{
    internal const int Protocol = 1;
    internal static bool IsServer { get; }                   // single player or host or dedicated: this game runs the scripts
    internal static bool ServerHasMod { get; }               // client: the server answered hello (always true when IsServer)
    internal static bool LocalIsAdmin { get; }               // client: the server told us we are an admin (server: always true)
    internal static string ServerVersion { get; }
    internal static event Action Changed;                    // any of the above changed (joined, left, answer arrived)

    // server side
    internal static IEnumerable<long> AgentPeers();          // peers whose game has the mod (uids)
    internal static bool HasAgent(long peer);
    internal static bool IsAdmin(long peer);                 // on the connection itself: ZNet.IsAdmin(host name)
    internal static void SendToPeer(long peer, string kind, string json, long id = 0);
    internal static void SendToAgents(string kind, string json);              // everyone who has the mod
    internal static event Action<long, string, string, long> ServerGot;       // (peer, kind, json, id)  a client's message reached the server

    // client side
    internal static void SendToServer(string kind, string json, long id = 0);
    internal static event Action<string, string, long> ClientGot;             // (kind, json, id)        the server's message reached this client
}
```

Rules for the transport: a client's message is never believed about *who it is* (the peer is the connection); rate limit per peer (a token
bucket: 60 messages a second, 1 MB a second, over it the message is dropped and counted); malformed JSON is dropped, never thrown; parse with
`JsonConvert` depth limit 16 and string length caps; unknown kinds ignored; a hello must come first.

## 3. Kinds

| kind | direction | what |
|---|---|---|
| `hello` | client to server | `{v, version}` I have the mod |
| `welcome` | server to client | `{v, version, admin, limits, lists}`: whether you are an admin, the server's limits (numbers the Limits panel shows and the client's own checks use) and the server's lists (item allow, status effects, ... by name) |
| `act` | server to client | an action for this client's character or screen: `{op, ...args}`; client replies `done` for ops that have an answer |
| `done` | client to server | `{ok, n, why}` the answer to an `act` with the same id |
| `state` | client to server | a snapshot of what only the player's game knows (section 5); every 2 s when changed, at most 8 KB |
| `event` | client to server | an event only the player's game saw (section 6) |
| `fx` | server to clients | a world-visible thing played on every game with the mod near a place: weather, wind, effect, sound, shake, banner, talk (section 4) |
| `req` / `res` | client to server / back | an admin's request to edit scripts remotely (section 7) and its answer |
| `limits` | server to clients | the limits changed (sent again when the owner edits the config) |

## 4. Actions on a player (`act`) and on the world (`fx`)

Server code today calls `Mine(s, p, what)` and refuses when the player is not the local one. The new rule: **a function that acts on a player
routes**: the local player runs `Local.X(...)` (what exists now); a remote player with an agent gets `act {op}`; one without gets a refusal.
Every remote op is **checked again on the client** with the client's own copy of the same lists and caps (so a rogue server cannot hand a
player what the mod would not: items only from `Allow`, never `NeverGive`; amounts capped; `hurt` never lethal; teleport lands on the
ground; effects only from the effects list; launch force at most 15; names and text lengths capped). The client also has settings (config)
for what servers may do to its character: `Servers may give items` (on), `heal and hurt` (on), `effects` (on), `take items` (**off**),
`teleport` (**off**), `launch` (**off**); an off op is answered `done {ok:false, why:"the player does not allow it"}` and the script's log says
so (a `LIMIT` line naming the player).

| op | args | answer | notes |
|---|---|---|---|
| `give` | prefab, n | n given | the bag's own rule: only what fits, the rest is never dropped or lost |
| `take` | prefab, n | n taken | the client removes and answers with what it really took; the server only counts hourly use from the answer (pitfall: take items only once the owner confirms) |
| `heal`, `hurt` | n | n done | `hurt` leaves 1 health |
| `stamina` | n | | |
| `effect+`, `effect-` | name, seconds | ok | |
| `launch` | dx, dy, dz, force | ok | |
| `teleport` | x, z | ok | the client puts the player on the ground there (never the script's y), refuses if the zone is not loaded; sets the fall height so it cannot kill |
| `banner` | text | | the big centre message |
| `talk`, `ping`, `shake`, `name` | ... | | `talk` and `ping` go as the game's own routed `ChatMessage` (every vanilla client sees them); `shake` is an `fx` |

`fx` (to every agent, each applies it if it is within range of the place): `env {name, seconds}` (weather: the game computes weather the same
on every client, so a forced weather must be forced on each), `wind {deg, strength, seconds}`, `effect {name, pos, scale}`,
`sound {name, pos}`, `shake {pos, range, strength}`, `banner {text}`. The effect validation (purely visual components only) is the same
code on the client. The server also plays the ones that apply to its own screen.

Creatures owned by another game (every creature near a player is owned by that player's game): damage, heal, launch, tame, remove,
setName on them first **take ownership** (`ZDO.SetOwner(ZDOMan.GetSessionID())`) and act next frame, as other mods do; if ownership does
not come within a second the call is refused with "that creature is busy".

## 5. Reads (`state`)

What the server cannot read from replicated data comes in the player's `state` snapshot, cached per player on the server: `inventory`
(prefab, name, count, quality), `equipped` (slots), `skills` (name to level), `deaths`, `stamina`, `maxStamina`, `isSwimming`, `isSitting`,
`isRunning`, `isBlocking`, `look` (the place the player is looking at, 100 m raycast, or null). Reads of a remote player come from
this cache (up to 2 s old; the docs say so); reads of the local player are live. Health, position, speed, facing, level, name, id come
from the replicated character as now. `count/has/inventory` on a player whose agent has not reported yet answer "unknown" (0, false, empty) and log one `WARN`.

## 6. Events only a player's game sees (`event`)

The client forwards these for what **it owns**; the server checks them (the subject must be owned by that connection; rate limited;
dedup by id for 60 s) and then fires them like its own:

| event | forwarded when | notes |
|---|---|---|
| `kill`, `bossDeath` | a creature the client owns dies | most creatures near a player are owned by that player's game, so without this the server misses nearly every kill. The server also fires the ones it owns; **dedup** by victim id |
| `playerDeath`, `playerRespawn` | the client's own player | |
| `itemPickup`, `eat`, `skillUp`, `pieceBuilt`, `tamed` | the client's own player | |
| `hit` | a creature or player the client owns is hit | **read-only for scripts** (the damage was settled on the owner's game; a script cannot change it): `e.damage` writes are ignored and counted as a refusal "hit damage can only be changed for things this server owns". The server's own targets keep the writable behaviour |
| `weatherChange` | server only | the server's own |
| `chat`, `playerJoin/Leave`, `bossSpawn`, `dayStart/nightStart` | server sees them already | no forwarding |

A forwarded event carries the ids and plain values the script's `e` needs (names, prefabs, positions, amounts), never anything
executable. Handles in `e` for a forwarded kill/death are snapshots (frozen), valid=false, as for a creature that no longer exists.

## 7. Remote editing (admins) and what everyone may see

The editor talks to **a backend**: `Local` (this game runs the scripts: today's `HostBridge`) or `Remote` (this game is connected to a server
that has the mod; its scripts are on the server). A `Remote` backend caches the last answer of each query and refreshes in the background
(a request every 0.5 s while the window is open), so the editor never blocks. Operations (`req {op, ...}` / `res`):
`list`, `source`, `logrows`, `logtext`, `logcounts`, `stats`, `refused`, `trace`, `start`, `stop`, `allow`, `write`, `writeBlocks`
(blocks json + generated js together), `new`, `delete`, `settings` (the limits), `logpath`.

- **Admins only** (`Wire.IsAdmin(peer)` on the connection, from the server's admin list) may do anything except `list` and `settings`.
- **Everyone with the mod** may open the window and see `list` (name, about, state, powers, whether it is blocks) and the limits, **read only**, so
  a player can see which scripts a server runs and what they can do to them. No logs, no code, no buttons.
- The server's `Files.Save`/`HostBridge.Write` run on the server's disk; the admin's own disk is not touched. A save arrives as one `writeBlocks`.
- The editor shows the origin ("Server: <name>" or "This game") in its title and a clear "read only" state for non-admins.
- Terminal: the Terminal's Scripts program is a screen in the world; it uses the Local backend only on a game that runs scripts, and
  shows its note elsewhere (unchanged).

## 8. Limits and lists sync

The server's numbers (`Limits.*`) and lists (`Lists.*` as names) go in `welcome` and again in `limits` when they change. A client keeps
them as `ServerLimits`; the editor's Limits panel shows the server's while connected (it says "this server's limits"), and the client's
own validation of acts uses them. Single player and the host use their own config as now. Files: `Limits.cs` gets
`Limits.Apply(json)`/`Limits.ToJson()`; `Lists` get the same for their data lists.

## 9. Safety summary

1. Direct connection only; the peer is the sender, never the payload.
2. Client re-validates every act with its own lists and caps; risky ops are off by default on the client; the client config wins.
3. Server validates every forwarded event (ownership, rate, dedup); nothing a client sends can start a script or change one without admin.
4. All JSON parsed with depth and length caps; every handler wrapped so a bad message is dropped, never thrown into the game.
5. Rate limits both ways; messages over 256 KB split; at most 8 KB of state; 1 MB a second a peer.
6. Never lose items: `give` only what fits, `take` only after the client confirmed, nothing is dropped on the ground without a place.
7. Vanilla clients and servers without the mod are never kicked and never broken.

## 10. Testing

1. `tests/Scripts.Mp.Tests`: the pure parts without Unity: message envelope and chunking, the client-side validators (lists, caps, lethal
   check), the state cache, the event dedup, the admin gate, the rate limiter, and a **loopback**: a fake server and a fake client wired by an
   in-memory `Wire` transport, running a message script of every op and every refusal.
2. In the game: single player and a listen server on one machine only exercise the local paths. The real test is a **dedicated server on the
   same machine with the game client joining it** (the client is a remote player to the server). That needs the free Steam tool "Valheim
   Dedicated Server" and BepInEx in it with only Scripts installed; the lead sets that up if the user agrees. Until it has been run, the
   multiplayer paths are **built and unit-tested, not verified in the game**, and the docs say so.

## 11. Status (2026-10-12, the CORE agent's part: sections 1 to 6 and 8 to 10)

Built (`mods/Scripts.Engine/Mp/`): `Wire.cs` (the transport: one RPC on the direct connection, hello and welcome with the admin flag from
the server's admin list, parts and gzip, per-connection limiter, clean on leave, handlers wrapped), `Frames.cs`, `Basics.cs` (limiter, dedup,
pending questions, the state cache), `Validate.cs` and `ActEngine.cs` (what a player's game checks and does), `RemoteCore.cs` (the server's
bookkeeping and the event gate), `Remote.cs` (the server's routing, the broadcasts, the welcome, the reports and events in), `Agent.cs` (the
player's game: acts, the report, the forwarded events, what the server shows). Routing and forwarding are in `Players.cs`, `Handles.cs`
(through `Players`), `Effects.cs`, `ActorExtras.cs`, `WorldExtras.cs`, `Events.cs` and `NewEvents.cs`; `Limits.cs` and `Lists.cs` carry the
server's numbers and lists and the player's own settings.

Unit-tested without the game (`tests/Scripts.Mp.Tests`, 115 checks): packing, gzip, parts, bombs, depth, floods, the limiter, dedup, pending
questions, every validator, every op with every refusal through a loopback between a fake server and a fake player (a rogue server asking
for a never-give item, a lethal hurt, a bad teleport height, an oversized message, malformed JSON, a flood, a player who never answers, an
answer from the wrong player), the event gate, the state cache and the list rules.

**Not verified in the game**: the RPC registration and the hello over a real connection, `ZNet.IsAdmin` on a real connection, every Harmony
patch's forwarding on a real client, taking over a creature's ownership, the weather and wind on the players' screens, effects played on the
players' games, the report's contents (skills, the look raycast), `ZNetPeer.m_playerID` as the unforgeable id of a connection, the dedicated
server's chat registration, and anything about timing. Test with a **dedicated server on this machine and the game joining it** (the joining
game is then a remote player to the server).

Not built: remote editing of scripts by an admin (the ADMIN agent), a Limits panel that says it shows the server's (`Limits.FromServer`
tells it), `inventory()` beyond 100 items, the in-world "refused" message on a dedicated server (it goes only to a player on the server's own
game), the Proxy (the Terminal) in multiplayer, and any sharing of the Terminal's editor between players.
