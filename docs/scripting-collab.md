# Editing a block script together (design and contract, 2026-10-12)

> **Status (2026-10-12):** design of live co-editing in **Quad's Blockscript**. The logic is built and tested (9159 checks, plus 217 for the network layer); **not yet tried with two real players**.

Several people open the same block script and change it at the same time, and see each other's changes as they happen. This file is the
exact design of the **pure logic** (`mods/Scripts.Engine/Collab/*.cs`, no Unity, tested by `tests/Scripts.Collab.Tests`). A later piece wires it
to the editor (`Editor/*`) and to the network (`Mp/Wire.cs`); what that piece must call is in "The surface".

Three steps, as asked: (1) a save never silently overwrites someone else's work, (2) everyone sees who is in the script and what they hold,
(3) real co-editing. Step 1 for **text** scripts and Paste is `Version`; for blocks it is built into step 3 (every change is an op against the
latest state, so there is no "whole file" save to be stale).

## 1. Ids

`Block.Uid` and `Program.Top.Uid`: 8 characters of base 36, from a cryptographic random source (never a counter: two editors adding blocks at
once must not both make "7"), collision-checked inside the program; the client never reuses an id the last committed state has had.

- Saved in the JSON as `"u"`, **only when there is one**. A program nobody co-edited has none, and its JSON is byte-for-byte what it was
  (examples, old files, the whole blocks test suite are unchanged). A session gives ids when it opens (`Uids.Ensure`), and the master's
  next save writes them.
- A **clone** (`Block.Clone`: palette drag, duplicate) starts with no ids: it is a new block, and gets ids at the next commit. A **move, undo,
  redo, save and load keep them**. `Program.Copy()` keeps them.
- Two blocks with the same id (a pasted copy that kept its ids): the first keeps it, the later ones get new ones (`Uids.Ensure`).
- `Block.Id` (the int the editor uses for problems and line maps) is untouched.

## 2. Ops (`Ops.cs`)

Ten small operations plus one move, all addressed by id, all JSON, and each means the same on every copy of the program whatever else has
changed meanwhile (that is what makes it safe to apply them in the master's order everywhere):

| op | meaning |
|---|---|
| `addTop {top json}` | a new stack (with its blocks and ids); always appended (the order of stacks is **not** shared) |
| `moveTop {top, x, y}` / `collapse {top, on}` / `removeTop {top}` | |
| `insert {top` or `block+key, after, before, blocks[]}` | blocks (with ids) into a stack's chain or a body, after the block `after` (`null` = the start, none = the end); if `after` is gone, before `before`; if that is gone, the end (Adjusted) |
| `move {block, top` or `to+key, after, before}` | **the block itself** (with everything inside it, edits and all) goes to another list; never into itself |
| `remove {block}` | one block with what is inside it; the ones after it stay |
| `setField {block, key, value, expect?}` | `value: null` takes the field away; with `expect` the op does nothing if the field is no longer that (undo) |
| `setInput {block, key, child json \| null}` | plug a block into a socket / take it out |
| `meta {name, about}` / `vars {list}` | |

`Ops.Apply(program, op) -> ApplyResult {Applied | Adjusted | Dropped(reason)}`. It **never throws**. Dropped: the target is gone, the id is taken,
a list key that is not a place, too many fields/sockets/stacks, too deep (`Tree.MaxDepth` = what `Block.FromJson` keeps), too many blocks
(`Program.MaxBlocks`), `expect` failed, a block into itself. The limits are the ones a saved program loads back within, so the master's
snapshot never loses a block.

`Ops.Gc(program)` sweeps stacks left with no blocks. Everybody calls it **at the end of each batch**, never between two ops of one batch (a
batch may empty a stack and refill it). `Canon` ignores empty stacks, so timing differences never show.

## 3. Diff (`Diff.Compute(old, new, by, expect)`)

The editor keeps changing its program the way `BlockView` always has; a commit hands the before and the after to `Diff`. By id:

- a block still in place (in a list: the longest run that kept its order, LCS with the shared ends trimmed first) is compared inside;
- a block that went to **another list of the same script** is a **`move`** (so somebody typing inside it meanwhile loses nothing);
- a new block is an `insert` (consecutive new blocks are one op) with the ids it was given; a new stack is an `addTop`;
- order: moves, removals, changes and inserts, new stacks. An op never needs something a later op makes.
- **The result is verified**: the ops are done to a copy of `old` and must give `new` (`Canon.Equal`). If not (a move that would put a block inside
  itself, say) it is made again without moves, and if that fails too the stacks are replaced whole. So `Apply(old, Diff(old, new)) = new` holds
  at run time, not only in the tests. (Costs a copy and a replay per commit: about 80 ms at 1470 blocks; commit on edit end, not per keystroke.)

`Diff.Compute(a, b, expect: true)` makes the inverse used by undo (field changes carry `expect`).

## 4. The master (`Master`) - the server's copy of one script

`Master(program, clock)`; `Submit(editor, seq, ops, out toEveryone) -> Batch`; `Join/Leave/UpdatePresence/Tick`; `Grab/Release`; `Snapshot()`;
`Heartbeat()`; `TakeSaveRequest()`; `Program`, `Rev`, `Dirty`.

- **Submit** does the ops in order against the current state (last writer wins on one field), numbers each op that took effect (`Rev`), and returns a
  `Batch {RevFrom, RevTo, By, Seq, Ops (the ones that took effect), Dropped, Reasons, Hash}`; the **hash** is `Canon.Hash` of the program after the batch.
  Everyone gets it if any op took effect; if none did only the sender does (it says what was dropped and why).
- **Presence** per editor: `{Name, Color 0-7 (the lowest unused), Page, Selected, X, Y, Grabbed, LastSeen}`. Anything the editor does counts as "still here";
  silent for 15 s and it is dropped (`Tick`).
- **Locks** (soft, for a drag): `Grab(editor, ids)` holds a block or a stack: nobody else's op may change it, remove it, insert into it, or change
  anything inside it; refused if someone else holds it, something inside it, or something around it. Released by `Release`, or after **10 s** without
  the holder being heard from. An op on something held is **Dropped with the holder's name** ("Ann is holding that").
- **Snapshot** `{rev, program (with ids), hash, editors}`; **Heartbeat** `{rev, hash}` (send it every second or two).
- **Save**: `TakeSaveRequest()` is true when the script is dirty and either nobody has changed it for **1 s** or it has been changing for **5 s**: the caller
  compiles and writes the blocks file and the code (the host restarts a running script on the changed file, as for a single editor).

## 5. The client (`Client`) - one editor's copy

Three states: the **master's** (`_base`, what the server has as of `Rev`, every batch done to it in order), the **committed** one (the master's plus this
editor's ops not yet acknowledged), and **Live** (the committed one plus what the editor changed since its last commit). `Live` is the object the editor
holds, for the whole session.

- `CommitChanges()` -> `Commit {Seq, Ops, Inverse}` or null: diffs committed against Live, keeps the ops as pending. New blocks get ids here.
- `OnBatch(batch)`: every batch in the order it arrived, own ones included (the acknowledgement; pops the pending commit with that `Seq`).
  - **Another editor's batch while this editor has unsent work -> rebase**: the master will have done the other batch first and ours after, so
    Live becomes *master state + our pending ops + our unsent edits*, and is brought there **in place** by a diff (a block in both stays the same
    object: selection and a drag in progress survive; a block that is only moved is not recreated). This one rule is why two people changing one
    field, or inserting at the same place, end the same everywhere.
  - An op of ours the master **dropped** is simply not in the rebuilt copy (the `Refused` event gives the reasons for a short message). No snapshot.
  - Nothing of ours in the way: the other editor's ops go straight into Live (fast path).
- A copy asks for a **snapshot** (`NeedsResync`, `TakeResyncRequest()`, then `OnSnapshot`) only when a batch **went missing** (a gap in `Rev`) or the
  master's hash does not match the master state this copy computed. `OnHeartbeat(rev, hash)` finds the same things when no batch comes. After a
  snapshot is asked for, **nothing is sent** until it arrives (it would reach the master after the snapshot was made); the editor's changes meanwhile
  are kept and go on top of the snapshot.
- `Undo()` / `Redo()` return a `Commit`: **this editor's own** last commit, as a new commit; each inverse field change carries `expect`, so another
  editor's later change to the same field is never overwritten; inverse ops whose targets are gone are skipped.
- Events: `Changed(ids, structural)` after other people's changes (the view redraws and keeps its selection by id), `Resynced`, `Refused(reasons)`.
- `Busy` is a flag for the editor's own use (a drag in progress); nothing here waits for it.

## 6. Stale saves for text (`Version`)

`Version.Hash(text)` (SHA-1, line ends and the last newline ignored), `Check(currentHash, baseHash) -> Ok | Stale`, `StaleMessage(lastBy)`. A write that
is not a live co-edit (Paste, the text editor) carries the hash of the version it began from; the server refuses a stale one with the message naming
who saved last. A save that does not say where it began (an older editor) is allowed.

## 7. What was proven (tests/Scripts.Collab.Tests, 9159 checks)

- ids unique and valid; JSON round trip; no `u` when there is none (old files and examples unchanged); clones get none; moves and copies keep them;
- every op and every Dropped case; every op survives the wire; **20,000 random garbage ops never throw and never leave a missing or repeated id**; limits (depth, blocks, fields, stacks, vars);
- `Apply(old, Diff(old, new)) = new` for 3,000 random edits (all ops apply cleanly); deleting or inserting one block among a hundred is one op; a move inside a list is one `move` op;
- master: numbering, hash, last writer wins, dropped ops (only the sender hears), locks (holder named; around/inside/next to), 10 s hold expiry, 15 s presence expiry, colours, snapshot, save debounce;
- client: commit/ack, remote ops done in place (the editor's block reference stays the block), unsent edits not overwritten, a dropped op leaves no trace and no snapshot, a missed batch is noticed, drift by hash, undo/redo of own work only (another editor's later change to the same field is not overwritten), undo of an insert, a redo whose place was removed;
- **convergence fuzz**: 80 seeds, 2 to 5 clients, a delaying and reordering in-memory network (one connection never reorders), 69,000 steps with the harder runs, 33,106 commits, 46,619 ops, 6,619 undo/redo: every copy equals the master after the network drains, **with no snapshot needed at all**;
- the harder runs: 15 seeds losing 3% of batches (found by the heartbeat: 249 snapshots, all converge) and 15 with random drags holding blocks (257 drags; ops dropped by holds, all converge);
- performance at 1470 blocks: commit about 80 ms, applying someone's change under an unsent edit about 90 ms, a rebase about 80 ms, tidying 30 stacks about 60 ms.

## 8. Limits, honestly

- **Two people changing the same field at once**: the one whose change reaches the master last wins, on every copy, with no snapshot. The other's change is simply overwritten (and shows as changed for them).
- **Two people inserting at the same place**: both blocks stay; their order is the master's order, the same everywhere.
- **Someone deletes a block (or its stack) while you change something in it**: your change is dropped (the block is gone); your copy puts itself right at once (no snapshot) and `Refused` says why.
- **A move and an edit inside the moved block at once**: both survive (a move is a real op). A block wrapped inside a *new* block (the move goes inside JSON of a new insert) is the exception: concurrent edits inside it may be lost.
- **Moving a block into a block that is itself being moved elsewhere at the same moment**: if the master would put a block inside itself, that op is dropped and the sender's copy is rebuilt.
- The **order of stacks** is not shared: new stacks are appended at the master; same-event handlers in different stacks run in the master's order.
- A reporter block dragged from one socket to another is a remove plus an insert (edits inside it at that moment can be lost; they are tiny).
- Diff verification costs a copy and a replay per commit (about 80 ms at the size limit): the editor should commit when an edit finishes (a drop, a text box closing), not per frame.
- Locks are soft: they stop others' ops while held, expire after 10 s of silence, and are only as good as the editor's use of `Grab` at drag start and `Release` at drop.
- The pure logic is now wired (section 10). Nothing has been tried in the game: only the in-memory tests have run it.

## 9. The surface the wiring calls

```
Uids.Ensure(Program, avoid?)            Uids.New(HashSet) / Clean / Valid / All(Program)
Op (Kind, By, Top, Block, Key, ...)     Op.ToJson() / Op.FromJson(JToken)        Ops.Apply(Program, Op) / Ops.Gc(Program)
Diff.Compute(old, new, by, expect)      Diff.Replays(old, new, ops)
Canon.Json / Hash / Equal               Tree.Find / Get / FindTop / Ancestors
Master(Program, clock)                  .Join(id, name) .Leave(id) .UpdatePresence(id, page, selected, x, y) .Tick()
                                        .Grab(id, uids) .Release(id, uids?) .Submit(id, seq, ops, out toEveryone) -> Batch
                                        .Snapshot() .Heartbeat() .TakeSaveRequest() .Program .Rev .Dirty .Editors
Batch.ToJson() / Batch.FromJson(JToken) Presence.ToJson() / FromJson(JToken)
Client(Live, id, rev)                   .CommitChanges() .Commit(Program) .Undo() .Redo() .OnBatch(Batch) .OnSnapshot(JObject)
                                        .OnHeartbeat(rev, hash) .TakeResyncRequest() .NeedsResync .Pending .HasUncommitted .Busy .Rev
                                        events Changed, Resynced, Refused
Version.Hash / Check / StaleMessage
```

Wiring notes for the next piece: on `Wire.ClientGot` / `ServerGot` carry `Batch`, `Commit.Ops`, snapshots and heartbeats as JSON; a client commits
(`CommitChanges`) and, in the same step, sends the result; when `NeedsResync` commit first, then `TakeResyncRequest()`, then ask the server for a
snapshot; the server runs `Master.Tick()` once a second, sends `Heartbeat()` to every editor every 1-2 s, and saves when `TakeSaveRequest()`;
the editor calls `Master.Grab` (through a `grab` message) at the start of a drag and `Release` at the drop; BlockView's own undo/redo is replaced
by `Client.Undo/Redo` while a session is open; `Block.Clone` gives ids-less blocks, so duplicates and palette drags are new blocks.

## 10. What is wired (2026-10-12)

**Files.** `Mp/CollabNet.cs` (message kinds `Co.*`, `ICollabHost`), `Mp/CollabServer.cs` (one Master per open script, no game in it),
`Mp/CollabClient.cs` (`CollabSession`: one editor's session), `Mp/CollabGame.cs` (the game's host: admin check, wire, disk, audit),
`Mp/LastWriter.cs` (who saved last), `Editor/CollabUi.cs` (the editor's side), edits in `BlockView.cs`, `EditorScreen.cs`, `Backend.cs`,
`RemoteBackend.cs`, `AdminServer.cs`, `Files.cs`, `HostBridge.cs`, `Core.cs`, `Tools.cs`.

**Messages** (all JSON, name the script as `f`, at most 256 KB):

| editor to server | meaning | server to editor | meaning |
|---|---|---|---|
| `co.open` | I want to edit it (admins only) | `co.snap` | whole state, your id, who is here |
| `co.ops {seq, ops}` | what I changed (a gesture's commit) | `co.batch` | the ops that took effect (also your acknowledgement) |
| `co.pres {page, sel, x, y}` | where I am, also "still here" (every 5 s) | `co.pres {editors}` | everyone's presence (at most 4 times a second) |
| `co.grab {uids}` / `co.release` | I am dragging these / I let go | `co.hb {rev, hash}` | heartbeat every 2 s |
| `co.resync` | I missed something | `co.refused {why}` | a message to show (a grab that failed) |
| `co.save` | write it now (before Run) | `co.closed {why}` | the session is over for you |
| `co.close` | I am leaving | | |

The sender of a message is the connection (`p<uid>`), never a field of the message; the host's own editor is `host` and talks to the server
in-process (no wire). Limits per editor: ops 20 a second, presence 8, grab/release/resync/save 10; over the limit the message is dropped
and counted. A message that is not JSON, too big, too deep, or names a file that is not a script is dropped.

**Server behaviour.** A session opens on the first `co.open` (ids are given to every block and saved once), ends when the last editor leaves
(saved on the way out), when the script is removed (`Files.Delete`), or when the server stops (`CollabGame.Shutdown` from `Core.Stop`).
`Tick` (4 a second): silent editors out after 15 s, holds after 10 s, presence broadcast, heartbeat, and the autosave a second after the last
change (at least every 5 s while it keeps changing); the server compiles and writes the blocks and the code, so the running script restarts as
it does for one editor. Audit: `co-edit: saved X (autosave), editors: Ann, Bob`. A block script that has a session cannot be written
whole by `writeBlocks` (older editors) or by the local save: they are refused with "Somebody is editing this script live".

**Editor.** A commit happens when a gesture ends (`BlockView.Changed`, never while a block is being dragged). A drag sends `co.grab` with the
held ids and `co.release` after the drop (after the move was sent). Other people's changes are done to the same program in place (selection,
scroll and a drag in progress survive). Undo and redo go through the session and undo only your own work. Presence: the others' selected block
has a coloured outline with their name, what they are moving is dimmed with "NAME is moving these", their pointer is a small coloured arrow with
their name, and "Editing now: ..." chips sit over the workspace. The library rows say "Bob editing now". Run asks the server to write first
(`co.save`) so what runs is what is on screen.

**Stale saves for plain code.** The editor keeps the hash of the version of a text script it has shown. **Replace** (clipboard over the picked
text script) and `Write` carry it as `base`; the server refuses a stale one with "Ann changed this script 2 minutes ago. Reload it first." and
the editor shows the message with a **Reload** button. Paste of a new script has no base and is always allowed. `LastWriter` is set by admin
writes, co-edit autosaves and the owner's own writes.

**Tests.** `tests/Scripts.CollabNet.Tests` (server and 3 editors through the real messages over a delaying in-memory network): joining, an
editor who is not an admin refused, ids given on first open, editing and two people on one field, drags with grab and release (the refused
edit is put right with the holder's name), an editor leaving mid-drag, a silent editor (hold 10 s, editor 15 s), a lost batch healed by the
heartbeat, autosave once per quiet second, stale-save words, malformed/oversized/unknown/flooded messages, session ending (last out, script
removed, server stopping), presence rate, and a fuzz of 30 seeds with 4500 random steps (all copies equal the master and the saved file).

**What only a real two-game session can verify:** the wire between a client's game and the server, `Wire.IsAdmin`, the look of the outlines,
names and pointers, dragging while someone else edits, and that a save by the server restarts the running script as expected.
