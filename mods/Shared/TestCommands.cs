namespace DHack.Shared
{
    /// <summary>
    /// Test commands a mod adds (through Claude Tools or the game's console) follow the game's own rule for cheats: they work in single player,
    /// for the host, or with devcommands on. A command that only reads (status, lists, checks) needs nothing; one that gives, moves, spawns,
    /// removes or changes rewards asks CheatsAllowed() first. Without it, any player on a server who can type the command could cheat with it.
    /// Shared source (mods/Shared/TestCommands.cs), linked into the mods whose test commands change the game: Arena, Rainbows, SkalTavern,
    /// Ziplines (each csproj has a Compile Include ... Link line for it).
    /// </summary>
    internal static class TestCommands
    {
        public static bool CheatsAllowed() =>
            ZNet.instance == null || ZNet.instance.IsServer() || (global::Console.instance != null && global::Console.instance.IsCheatsEnabled());

        public static string Refusal(string what) =>
            $"{what} changes the game, so as a test command it only works in single player, for the host, or with devcommands on (a server admin)";
    }
}
