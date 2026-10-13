# Quad's Arena API (v1)

A small read-only API for other mods, in Arena 0.4.4 and newer: whether a contest is on and who is fighting, where the arena and its stands are, and how the last contest ended. Any game with Arena can answer, not only the fighter's: the fighter's game tells everyone about its fight, near the arena or not.

```csharp
namespace Arena
{
    public static class Api
    {
        public const int Version = 1;
        public static bool IsContestRunning(out string fighter, out string kind);
        public static bool ArenaAt(out Vector3 centre);
        public static Vector3[] StandSpots(int max);
        public static int ResultCount { get; }
        public static bool LastResult(out string fighter, out string kind, out string outcome, out bool won, out bool crowned, out int coins, out double when);
    }
}
```

- `IsContestRunning`: a contest in the ring, yours or another player's (not a duel). `kind` is `road`, `champion`, `endless` or `trial`. Another player's fight is seen only if their Arena is 0.4.4 or newer.
- `ArenaAt`: the middle of the fighting floor; `false` until this game has found the arena.
- `StandSpots(max)`: up to `max` seats in the stands, spread round the ring, in world positions, each facing the middle. The crowd's figures sit on seats too, so stand beside one rather than on it. Empty until the arena is found.
- `ResultCount`: goes up by one each time this game hears of a contest's end. Poll it and call `LastResult` when it changes.
- `LastResult`: the last contest to end since this game started. `outcome` is `win`, `cashout`, `yield`, `died`, `timeup`, `leftring`, `carried`, `calledoff` or `aborted` (the fighter's game left mid-fight). `won` is true only for `win` (the whole contest). `crowned`: the run went into the Hall of Champions. `coins`: the purse paid, without the stake. `when`: the world's time in seconds (`ZNet.GetTimeSeconds`). Returns `false` while there is none yet.

Find it by reflection, with no reference to the Arena assembly: take the live plugin from `Chainloader.PluginInfos["com.quad.arena"]` (`com.dhack.arena` before 0.3.1) and read `Instance.GetType().Assembly.GetType("Arena.Api")`. After a reload (F6) there can be an old copy of the assembly still loaded, so don't search every assembly for the type name. Check `Version` before binding, and treat a missing method as "not available".

Results reach other games through Arena's own message, so players should update together; an older Arena ignores the new message, and a fighter on an older Arena sends no result.
