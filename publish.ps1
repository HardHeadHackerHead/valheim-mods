# Builds every mod (including the ModUpdater mod manager, which updates itself like any other mod) and copies
# the results into .\dist plus a manifest.json, ready to commit and push to the private repo.
$root = $PSScriptRoot
$dist = Join-Path $root "dist"
New-Item -ItemType Directory -Force $dist | Out-Null

$manifest = @()   # becomes dist\manifest.json, which the in-game mod manager reads
$GuidSwitchRelease = $true   # see $restart below: $false once the com.dhack -> com.quad release is out

foreach ($proj in Get-ChildItem (Join-Path $root "mods") -Directory) {
    $csproj = Join-Path $proj.FullName "$($proj.Name).csproj"
    if (-not (Test-Path $csproj)) { continue }

    Write-Host "Building $($proj.Name)..."
    dotnet build $csproj -c Release --nologo -v q -p:DeployToGame=false
    if ($LASTEXITCODE -ne 0) { Write-Error "Build failed for $($proj.Name)"; exit 1 }

    $out = Join-Path $proj.FullName "bin/Release/net48"
    Copy-Item (Join-Path $out "$($proj.Name).dll") $dist -Force
    Copy-Item (Join-Path $out "$($proj.Name).pdb") $dist -Force   # ScriptEngine needs the .pdb beside the DLL

    # Read guid/name/version from the mod's [BepInPlugin] constants, description from DESCRIPTION.txt (optional).
    $src = (Get-ChildItem $proj.FullName -Filter *.cs -Recurse | Where-Object { $_.FullName -notmatch '[\\/](obj|bin)[\\/]' } |
            ForEach-Object { Get-Content $_.FullName -Raw }) -join "`n"
    $guid = [regex]::Match($src, 'const string Guid\s*=\s*"([^"]+)"').Groups[1].Value
    $name = [regex]::Match($src, 'const string Name\s*=\s*"([^"]+)"').Groups[1].Value
    $ver  = [regex]::Match($src, 'const string Version\s*=\s*"([^"]+)"').Groups[1].Value
    # The mod's GUID before it changed (com.dhack.* until 2026-10): the manager treats the old one as an older copy of this mod.
    $oldGuid = [regex]::Match($src, 'const string OldGuid\s*=\s*"([^"]+)"').Groups[1].Value
    if (-not $guid -or -not $ver) { Write-Error "Couldn't find Guid/Version constants in $($proj.Name)"; exit 1 }
    $descFile = Join-Path $proj.FullName "DESCRIPTION.txt"
    $desc = if (Test-Path $descFile) { (Get-Content $descFile -Raw).Trim() } else { "" }

    # "What's new" shown in the manager when an update is waiting: the top section of CHANGELOG.txt (up to the first blank line).
    $notes = ""
    $logFile = Join-Path $proj.FullName "CHANGELOG.txt"
    if (Test-Path $logFile) {
        $lines = @()
        foreach ($line in (Get-Content $logFile)) {
            if ($line.Trim() -eq "") { if ($lines.Count -gt 0) { break } else { continue } }
            $lines += $line.Trim()
        }
        $notes = ($lines -join "  ")
    }

    # Optional RESTART_REQUIRED.txt: the mod can't be hot-reloaded safely, so the manager asks for a game restart. The file's text is the reason.
    $restartFile = Join-Path $proj.FullName "RESTART_REQUIRED.txt"
    $restart = if (Test-Path $restartFile) { (Get-Content $restartFile -Raw).Trim() } else { "" }
    # The release that moved every mod to its com.quad.* GUID: a manager that doesn't know "was" would hot-load the new copy next to the
    # old one (two GUIDs, so both run), so this one release asks for a restart. Set $GuidSwitchRelease to $false after it.
    if ($GuidSwitchRelease -and $oldGuid -and -not $restart) { $restart = "This update gives the mod a new id ($guid; it was $oldGuid). Restart the game to finish: your settings move over by themselves." }

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
[IO.File]::WriteAllText((Join-Path $dist "manifest.json"), $json, (New-Object Text.UTF8Encoding($false)))

# Each mod's own page (mods/<Mod>/README.md): made again from its DESCRIPTION.txt, CHANGELOG.txt and settings (tools/modpages)
$py = Get-Command python -ErrorAction SilentlyContinue
if ($py) { & $py.Source (Join-Path $root "tools/modpages/make_pages.py") }
else { Write-Warning "Python not found: the mods' own pages were not made again (run tools/modpages/make_pages.py)" }
Write-Host "`nReady in $dist :"
Get-ChildItem $dist | Select-Object Name, Length, LastWriteTime
Write-Host "`nNext: git add dist; git commit -m 'Update mods'; git push"
Write-Host "Thunderstore (each mod its own package, team Quads_Lab): .	hunderstore.ps1 to package and check, .	hunderstore.ps1 -Mods <names> -Publish to upload."
