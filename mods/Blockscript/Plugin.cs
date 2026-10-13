using System;
using System.Reflection;
using BepInEx;
using DHack.Shared;

[assembly: AssemblyMetadata("AI_Assisted_Creation", "This assembly was partially or fully created with the assistance of Generative AI.")]
[assembly: AssemblyMetadata("AI_Model_Vendor", "Anthropic")]
[assembly: AssemblyMetadata("ClaudeTools.Reload", "world")]

namespace Scripts
{
    /// <summary>
    /// Quad's Blockscript (design: docs/scripting-design.md, API: docs/scripting-api.md): players write short JavaScript programs that react
    /// to the game and run in a sandbox.
    ///
    /// This DLL is only a shell, on purpose. The code that names Jint (the engine) is in Scripts.Engine, carried inside this DLL and loaded
    /// from it once Jint and its helpers can be found (Deps.cs). A single DLL does not work: the dev loader and the F7 manager scan every
    /// type of a plugin before its first line runs, every type that names Jint then fails to load, and the runtime remembers the failure.
    /// </summary>
    [BepInPlugin(Guid, Name, Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.quad.blockscript";
        public const string Name = "Quad's Blockscript";
        public const string Version = "0.1.1";

        private Type _entry;

        private void Awake()
        {
            Translations.Load(this);
            Deps.Hook();
            Assembly engine = Deps.LoadEngine();
            _entry = engine?.GetType("Scripts.Entry", false);
            if (_entry == null) { Logger.LogError("The scripting engine could not be loaded: Scripts is off."); return; }
            _entry.GetMethod("Start").Invoke(null, new object[] { Logger, this });
        }

        private void Update() => _entry?.GetMethod("Update")?.Invoke(null, null);

        private void OnDestroy()
        {
            try { _entry?.GetMethod("Stop")?.Invoke(null, null); } catch (Exception e) { Logger.LogWarning("Stopping the scripts: " + e.Message); }
            Deps.Unhook();
            Translations.Unload();
        }
    }
}
