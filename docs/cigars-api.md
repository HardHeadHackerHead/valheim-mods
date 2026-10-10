# Quad's Cigars add-on API (v1)

The live BepInEx plugin `com.quad.cigarsmoking` (`com.dhack.cigarsmoking` before October 2026: look up either) exposes `SmokingApiVersion = 1` and these instance methods:

```csharp
bool RegisterSmokingEffect(string effectName);
void UnregisterSmokingEffect(string effectName);
bool StopOtherSmoking(Character character, string keepEffectName);
```

Register each add-on status effect by its `StatusEffect.name` (not its translated display name). Registration is idempotent, allows at most 32 names, and rejects empty names or names over 128 characters. Call `StopOtherSmoking` before applying your registered effect; `false` means the character or keep name is invalid. It stops other cigars and registered add-on effects, leaving the selected smoke and unrelated status effects alone. Cigars also call it when lit, so they stop a pipe in the other direction.

Find the current plugin through `Chainloader.PluginInfos` and check the API version and exact method signatures. Avoid a hard CLR reference to the Cigars assembly: ScriptEngine loads a new identity on F6. Registrations belong to each live instance; register again when the instance changes and unregister your names when your add-on unloads.

Tobacco item prefabs remain `dh_dried_meadow`, `dh_dried_forest`, `dh_dried_plains` and `dh_aged_meadow`, `dh_aged_forest`, `dh_aged_plains`. The crafting station prefab is `dh_cigar_table`. Resolve these through the current game registries, and only publish recipes when every required resource and station exists.
