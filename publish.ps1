# Builds the mods and writes .\dist: each mod's DLL and .pdb, its cover and dist\manifest.json (what the in-game mod manager reads), then
# makes each built mod's page again. Commit and push dist/ to release.
#
#   .\publish.ps1                                  every mod
#   .\publish.ps1 -Mods BetterCraftingStations     only these (comma-separated): every other mod's files and manifest entry stay exactly
#                                                  as they are, so players see no update for a mod that didn't change
param(
    [string[]]$Mods = @()
)
$root = $PSScriptRoot
$dist = Join-Path $root "dist"
New-Item -ItemType Directory -Force $dist | Out-Null
$manifestFile = Join-Path $dist "manifest.json"

# (-Mods A,B from another PowerShell arrives as one "A,B" string)
$Mods = @($Mods | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ })
$projects = @(Get-ChildItem (Join-Path $root "mods") -Directory | Where-Object { Test-Path (Join-Path $_.FullName "$($_.Name).csproj") })
$unknown = @($Mods | Where-Object { $projects.Name -notcontains $_ })
if ($unknown.Count -gt 0) { Write-Error "No such mod: $($unknown -join ', ') (the folder names in mods/)"; exit 1 }

# Only some mods: the others keep the entry the manifest already has for them (found by their DLL's name)
$kept = @{}
if ($Mods.Count -gt 0) {
    if (-not (Test-Path $manifestFile)) { Write-Error "No dist\manifest.json yet: run .\publish.ps1 once without -Mods"; exit 1 }
    foreach ($m in (Get-Content $manifestFile -Raw -Encoding UTF8 | ConvertFrom-Json).mods) { $kept[[string]$m.files[0]] = $m }
}

$manifest = @()
foreach ($proj in $projects) {
    if ($Mods.Count -gt 0 -and $Mods -notcontains $proj.Name) {
        $entry = $kept["$($proj.Name).dll"]
        if ($entry) { $manifest += $entry } else { Write-Warning "$($proj.Name) isn't in the manifest yet and wasn't built: it's left out" }
        continue
    }

    Write-Host "Building $($proj.Name)..."
    dotnet build (Join-Path $proj.FullName "$($proj.Name).csproj") -c Release --nologo -v q -p:DeployToGame=false
    if ($LASTEXITCODE -ne 0) { Write-Error "Build failed for $($proj.Name)"; exit 1 }

    $out = Join-Path $proj.FullName "bin/Release/net48"
    Copy-Item (Join-Path $out "$($proj.Name).dll") $dist -Force
    Copy-Item (Join-Path $out "$($proj.Name).pdb") $dist -Force   # ScriptEngine needs the .pdb beside the DLL

    # Read guid/name/version from the mod's [BepInPlugin] constants, description from DESCRIPTION.txt (optional).
    $src = (Get-ChildItem $proj.FullName -Filter *.cs -Recurse | Where-Object { $_.FullName -notmatch '[\\/](obj|bin)[\\/]' } |
            ForEach-Object { Get-Content $_.FullName -Raw -Encoding UTF8 }) -join "`n"
    $guid = [regex]::Match($src, 'const string Guid\s*=\s*"([^"]+)"').Groups[1].Value
    $name = [regex]::Match($src, 'const string Name\s*=\s*"([^"]+)"').Groups[1].Value
    $ver  = [regex]::Match($src, 'const string Version\s*=\s*"([^"]+)"').Groups[1].Value
    # The mod's GUID before it changed (com.dhack.* until 2026-10): the manager treats the old one as an older copy of this mod.
    $oldGuid = [regex]::Match($src, 'const string OldGuid\s*=\s*"([^"]+)"').Groups[1].Value
    if (-not $guid -or -not $ver) { Write-Error "Couldn't find Guid/Version constants in $($proj.Name)"; exit 1 }
    $descFile = Join-Path $proj.FullName "DESCRIPTION.txt"
    $desc = if (Test-Path $descFile) { (Get-Content $descFile -Raw -Encoding UTF8).Trim() } else { "" }

    # "What's new" shown in the manager when an update is waiting: the top section of CHANGELOG.txt (up to the first blank line).
    $notes = ""
    $logFile = Join-Path $proj.FullName "CHANGELOG.txt"
    if (Test-Path $logFile) {
        $lines = @()
        foreach ($line in (Get-Content $logFile -Encoding UTF8)) {
            if ($line.Trim() -eq "") { if ($lines.Count -gt 0) { break } else { continue } }
            $lines += $line.Trim()
        }
        $notes = ($lines -join "  ")
    }

    # Optional RESTART_REQUIRED.txt: the mod can't be hot-reloaded safely, so the manager asks for a game restart. The file's text is the reason.
    # (The release that moved every mod to its com.quad.* GUID also asked for one; that's out, and "was" alone tells the manager now.)
    $restartFile = Join-Path $proj.FullName "RESTART_REQUIRED.txt"
    $restart = if (Test-Path $restartFile) { (Get-Content $restartFile -Raw -Encoding UTF8).Trim() } else { "" }

    # Optional cover image (cover.png or cover.jpg in the mod folder), shown on the mod's card in the manager. Keep it small: about 640x360, under 1 MB.
    $cover = ""
    foreach ($ext in @("png", "jpg", "jpeg")) {
        $coverSrc = Join-Path $proj.FullName "cover.$ext"
        if (Test-Path $coverSrc) {
            $cover = "$($proj.Name).cover.$ext"
            Copy-Item $coverSrc (Join-Path $dist $cover) -Force
            if ((Get-Item $coverSrc).Length -gt 1MB) { Write-Warning "$($proj.Name): the cover image is over 1 MB, so the manager will skip it" }
            break
        }
    }

    $manifest += [ordered]@{
        guid = $guid; name = $name; version = $ver; description = $desc; notes = $notes; restart = $restart; cover = $cover
        files = @("$($proj.Name).dll", "$($proj.Name).pdb")
        was = @($oldGuid | Where-Object { $_ })
    }
}

$json = ConvertTo-Json -InputObject ([ordered]@{ mods = @($manifest) }) -Depth 5
[IO.File]::WriteAllText($manifestFile, $json, (New-Object Text.UTF8Encoding($false)))

# Each built mod's own page (mods/<Mod>/README.md): made again from its DESCRIPTION.txt, CHANGELOG.txt and settings (tools/modpages)
$py = Get-Command python -ErrorAction SilentlyContinue
if ($py) { & $py.Source (Join-Path $root "tools/modpages/make_pages.py") @Mods }
else { Write-Warning "Python not found: the mods' own pages were not made again (run tools/modpages/make_pages.py)" }
$built = if ($Mods.Count -gt 0) { " (built: $($Mods -join ', '); every other mod as it was)" } else { "" }
Write-Host "`nReady in $dist$built"
Write-Host "Next: git add dist; git commit; git push"
Write-Host "Thunderstore (each mod its own package, team Quads_Lab): .\thunderstore.ps1 -Mods <names> to package and check, add -Publish to upload."
