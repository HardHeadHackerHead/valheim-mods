---
name: valheim-thunderstore
description: Release a Valheim mod on Thunderstore (where r2modman and Thunderstore Mod Manager get mods), or publish an update to one - the package, its manifest, icon and README, Thunderstore's rules (AI disclosure included), testing the zip, and uploading with the Thunderstore CLI (tcli). Use when someone wants to publish, release, upload or share a mod on Thunderstore, or update one that is there.
---

# Releasing a mod on Thunderstore

> **Game closed?** The mod-maker commands (`modcheck`, `package`, `who`, `clashes`, `game`...) run as a program:
> `BepInEx/claude/modkit/modkit.exe <command>` on Windows, `dotnet BepInEx/claude/modkit/modkit.dll <command>` on Linux and Mac. Below,
> `modkit` means that.

Thunderstore (https://thunderstore.io/c/valheim/) is where most players get mods: r2modman and Thunderstore Mod Manager install from it
into `BepInEx/plugins`. A package is a zip with `manifest.json`, `icon.png`, `README.md` (and `CHANGELOG.md`) at its root and the DLL in
`plugins/`. **A published version can never be changed or deleted**, only followed by a higher one: every step before the upload is there
so the upload is right the first time.

Go through **valheim-prerelease** first. Then:

## 1. Once: the account, the team, the tools

Ask the player to do the account parts themselves (they're theirs, and the token is a password):

1. **Account**: sign in at https://thunderstore.io with Discord, GitHub or Overwolf.
2. **Team**: https://thunderstore.io/teams/ → create one. Every package belongs to a team, and the team name is part of every dependency
   string (`Team-Package-1.0.0`) and **can't be changed**: a solo modder usually takes their own name.
3. **Token**: the team's page → Service Accounts → Add service account → copy the token (`tss_...`). It is shown once. The player keeps it
   in an environment variable, typed in their own terminal, never in a file in the project and never pasted to you:
   Windows `setx TCLI_AUTH_TOKEN "tss_..."` (then a new terminal), Linux and Mac `export TCLI_AUTH_TOKEN=tss_...` in their shell profile.
   (In GitHub Actions, a repository secret.) If a token ever lands in a file or a chat, they delete that service account and make a new one.
4. **tcli**, the Thunderstore CLI, which builds the zip and uploads it. Check whether it's there: `tcli --version` (`modkit package` also
   says). If not: `dotnet tool install -g tcli` (the .NET SDK that builds mods brings `dotnet`), then a new terminal. If `tcli` still isn't
   found, the tools folder isn't on PATH: `~/.dotnet/tools/tcli` (Windows `%USERPROFILE%\.dotnet\tools\tcli.exe`) runs it directly.
   Update it now and then: `dotnet tool update -g tcli`.

## 2. Thunderstore's rules (they remove packages that break them)

- **Say AI was used.** If AI helped write the mod (Claude did): a line in the README ("Made with the help of Claude (Anthropic)."),
  the **ai-generated** category, and in the DLL. Put these two lines in any `.cs` file of the mod (the template's
  `Directory.Build.props` sets `GenerateAssemblyInfo` to false, which makes the csproj form `<AssemblyMetadata Include=...>` do nothing):
  ```csharp
  [assembly: System.Reflection.AssemblyMetadata("AI_Assisted_Creation", "This assembly was partially or fully created with the assistance of Generative AI.")]
  [assembly: System.Reflection.AssemblyMetadata("AI_Model_Vendor", "Anthropic")]
  ```
- **Tested and working.** "Untested or non-functional AI-generated mods" count as spam. Test the zip itself (step 4), on a clean profile.
- **The README says what the mod really does**, and its images are public `https://` links (not `images/x.png`: files in the package aren't
  shown). Push the images to GitHub and link `https://raw.githubusercontent.com/<you>/<repo>/main/<path>`.
- **No self-updating** (mod managers do updates), no obfuscated code, no downloading and running code, no collecting data about players.
- **Other people's work**: don't include game files or other authors' mods or assets unless their licence allows it; depend on their
  package instead.
- One mod per job: not five near-identical packages.

## 3. The package: `modkit package`

```
modkit package <mod folder> --namespace <Team> --website https://github.com/<you>/<repo>     (first time)
modkit package <mod folder>                                                                (every time after)
```

It writes `thunderstore.toml` beside the csproj (the CLI's own project file: name, version, description, dependencies, icon, README,
which files go where, categories), keeps its version and dependencies matching the built DLL, and checks what an upload needs:

| It checks | So that |
|---|---|
| the DLL is newer than every `.cs` file | the package holds the build you mean |
| name, version (`Major.Minor.Patch`, the plugin's `Version`), description (250 characters at most) | Thunderstore accepts it |
| dependencies: BepInExPack, and each `[BepInDependency]` (hard) mapped to its Thunderstore package | players get what it needs |
| the version isn't published yet and is higher than every published one | the upload isn't refused |
| the icon is a 256 x 256 PNG | Thunderstore accepts it (`render <your prefab> size=256x256 bg=dark` in the game makes one) |
| the README is UTF-8, says what it does, mentions AI and other mods, its images are pushed | the page is right and follows the rules |
| the AI category and `AssemblyMetadata` | Thunderstore's rule |
| `modcheck` on the DLL (no problems) and nothing uncommitted in the folder | nothing broken or unfinished ships |

It also makes, in `thunderstore-build/` (add it to `.gitignore`):
- **the package's README** from the mod's `README.md`, with relative links made absolute so they work on Thunderstore: images point at
  `raw.githubusercontent.com`, other links at `github.com`, from the folder's git remote and branch. So the README can keep relative
  links for GitHub; **push** the images before publishing (it warns about any that aren't on the remote).
- **CHANGELOG.md**: the mod's own `CHANGELOG.md` (a `## 1.0.1` heading per version, newest first, one line per change: not "various
  fixes"), or one made from `CHANGELOG.txt`, where a paragraph starting `1.0.1: ` gets that version's heading.

**The icon** is `thunderstore/icon.png` when there is one, or when the mod embeds an `icon.png` of its own (a build-menu icon): never
replace a mod's in-game icon with the package icon. Otherwise `icon.png` beside the csproj.

Read `thunderstore.toml` the first time: the description comes from `DESCRIPTION.txt` (or is marked TODO), categories start as
`mods` and `ai-generated` (add fitting ones: `building`, `crafting`, `gear`, `utility`, `tools`, `transportation`, `client-side` (only
when nobody else needs it), `server-side`... `modkit package` lists them if one is wrong). Edits to the toml are kept: modkit only
updates the version and dependencies. Fix every **problem** and run it again until there are none.

**A repository with several mods**: each mod is its own package (one `thunderstore.toml` per mod folder, the same team). A small script
that, for each mod, builds it, runs `modkit package`, `tcli build` and `modkit package check`, and publishes only the mods named on its
command line keeps releases consistent. Other channels (a GitHub feed, a mod manager of your own) can ship the same builds alongside.

## 4. Build the zip, check it, test it

```
tcli build --config-path <mod folder>/thunderstore.toml        writes <mod folder>/thunderstore-build/<Team>-<Name>-<version>.zip
modkit package check <that zip>
```

Then test **that zip**, not the DLL you built: ask the player to open r2modman (or Thunderstore Mod Manager), make a **new, empty profile**,
Settings → **Import local mod** → the zip, start the game from that profile, and use the mod. With Claude Tools in that profile too,
`errors` and the mod's own checks work as usual. A clean profile catches what your own setup hides: a missing dependency, a file in the
wrong place, a config it expects to exist.

Add `thunderstore-build/` to `.gitignore`.

## 5. Publish (ask first)

Show the player what will go up (package name, version, the changelog lines, categories) and ask. Then, with `TCLI_AUTH_TOKEN` set:

```
tcli publish --config-path <mod folder>/thunderstore.toml --file <that zip>
```

It can take a few minutes to show on the site. Never pass the token on the command line in a shared log, and never write it into
`thunderstore.toml`.

## 6. Updates

Raise the plugin's `Version` (semantic: 1.0.10 is higher than 1.0.9), add the changelog lines, rebuild, then `modkit package` (it updates
the toml), `tcli build`, check, test, ask, publish. Never change `name` (a new name is a new package) or the team. Players on
Thunderstore restart the game after updating, so the reload level doesn't matter there; it still matters for your own testing.

To retire a mod: deprecate it on its Thunderstore page (it stays installable for existing modpacks).
