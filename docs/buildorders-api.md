# BuildOrders planning API v1

BuildOrders exposes a ghost-only interface for add-ons that generate curves, patterns, or other designs. `Plugin.PlanningApiVersion` is `1`.
Calls run synchronously on the Unity main thread, with `Player.m_localPlayer`, after the local world has loaded and BuildOrders is enabled.

`TryCreateGhostPlan(Player player, string title, string[] prefabs, Vector3[] positions, Quaternion[] rotations, out string planKey, out string error)`
accepts 1–256 pieces at **absolute world positions and rotations**. The entire input is validated before changes: matching array lengths,
bounded delimiter-free names, finite poses, nonzero rotations (normalized by the API), available buildable pieces, known recipes, an 80-metre
reach limit, wards, and no-build locations. Creation also refuses concurrent blueprint/bridge placement or an open Plans window. Menu-only Bridge and terrain-operation prefabs are rejected. Exact nearby duplicate orders are
skipped; a batch containing only duplicates returns false. No building resources are consumed until the ghosts are built normally.

Success returns a unique named plan key, using the existing orders, saving, sharing, material totals, and stability mechanisms. Failed
validation leaves orders unchanged. This API does not call blueprint placement/leveling or reuse a prior plan's terrain history.
Generated groups are excluded from the Plans window's Move and Level actions, which would otherwise level a blueprint's footprint.
Saving/sharing have the same best-effort error handling as ordinary BuildOrders; success means the local orders were accepted, not a
multiplayer acknowledgement or proof that the save reached disk.

`TryRemoveGhostPlan(Player player, string planKey, out int removed, out string error)` removes only the remaining ghosts of an API-created
group, with reach and ward checks before removal. Completed/removed groups return true with zero removed. Built pieces, materials, and terrain
remain intact. The API itself does not erase built-piece records; normal planner cleanup still applies. Keep the key associated with the
current world; discard session undo state on world changes.

Add-ons should declare a dependency on `com.dhack.buildorders`. ScriptEngine ignores dependency attributes and reloads each DLL with a new
assembly identity, so also check `BepInEx.Bootstrap.Chainloader.PluginInfos` for its live instance, verify `PlanningApiVersion`, and resolve
these public methods on that instance. Retry when the instance is temporarily null, and rebind when it changes. Avoid assembly references
to custom BuildOrders types: this interface uses only game, Unity, and standard .NET types. Normal BepInEx plugins may use direct calls.

Users building generated ghosts need BuildOrders and the referenced piece prefabs; they do not need the generator add-on itself.
Participants should use the API-supporting BuildOrders release. Older peers can receive/build the ghosts but treat these groups as ordinary
blueprints and still offer Move/Level, bypassing the add-on group's terrain protections.

Validation: `dotnet run --project tests/BuildOrders.Api.Tests -c Release` runs the linked API implementation against boundary doubles,
checking batch rejection before mutation, sharing/save calls, unique groups, ghost-only undo, and world changes. Unity rendering,
multiplayer RPC delivery, disk persistence, and fresh-launch/F6 behavior still need game testing.

## Optional selection and input queries (BuildOrders 1.9.5)

`IsPlanningInputAvailable(Player player)` reports whether an add-on may capture planning input in the loaded local world.
Blueprint placement, bridge drawing/options, the Plans window, typing/menus, and inventory return false. This query processes no input.
Add-ons should also use their own normal-play/tool/UI gates, stop or suspend when another planner mode takes input, and recheck after reload.

`TryGetGhostAtRay(Player player, Vector3 origin, Vector3 direction, out string orderId, out string prefab, out Vector3 position, out Quaternion rotation, out float distance)`
returns the closest rendered ghost intersecting a normalized world ray, within 80 metres along the ray and 80 metres of the local player.
The origin must be within 20 metres of the player; nonfinite/degenerate rays, hidden ghosts, and unavailable input are rejected.
Selection uses world-aligned render bounds, without ghost colliders. It does not test physical occlusion: callers should compare the hit
distance against a physical raycast. It queries current orders directly, so it does not depend on the prior frame's `Aimed` and never
processes G/Delete/E. The returned ID/pose is a snapshot for the current session, not a persistent object reference.

These additive methods preserve `PlanningApiVersion = 1`. Feature-detect their exact signatures on the live instance; older API v1
releases still support creation/removal but cannot provide ghost selection or planning-input exclusion.
