# Thunderstore packages for our mods, each released on its own under the Quads_Lab team. The same sources as publish.ps1 (which builds the
# dist/ feed that our own mod manager reads from GitHub): both channels ship the same builds.
#
#   .\thunderstore.ps1                          build, package and check every mod (nothing is uploaded)
#   .\thunderstore.ps1 -Mods Arena,Recycler     only these
#   .\thunderstore.ps1 -Mods Arena -Publish     ...and upload them to Thunderstore and Hexium (needs TCLI_AUTH_TOKEN, the Quads_Lab
#                                               service account token, and HEXIUM_AUTH_TOKEN, the Quads_Lab team's Hexium API token)
#   .\thunderstore.ps1 -Mods Arena -Publish -Sites hexium     one site only (a mod already on Thunderstore going up on Hexium too)
#
# Per mod it runs `modkit package` (Claude Tools' modkit: writes mods/<Mod>/thunderstore.toml and checks everything an upload needs), then
# `tcli build` (the zip, in mods/<Mod>/thunderstore-build) and `modkit package check` on the zip. A mod with problems is not built or uploaded.
# The same zip goes to Thunderstore (tcli publish) and to Hexium (tools/hexium_publish.py: Hexium's own categories, install location).
# Publishing needs the mods named: a version on Thunderstore can never be changed or deleted.
param(
    [string[]]$Mods = @(),
    [switch]$Publish,
    [ValidateSet("thunderstore", "hexium")][string[]]$Sites = @("thunderstore", "hexium"),
    [string]$Team = "Quads_Lab"
)
$ErrorActionPreference = "Continue"  # (native tools write to stderr; each step checks $LASTEXITCODE)
$root = $PSScriptRoot
$repo = "https://github.com/HardHeadHackerHead/valheim-mods"

if ($Publish -and $Mods.Count -eq 0) { Write-Error "Name the mods to publish: -Mods Arena,Recycler -Publish"; exit 1 }
# (a token saved with setx reaches only terminals opened after it: read it from the user's environment too)
foreach ($v in "TCLI_AUTH_TOKEN", "HEXIUM_AUTH_TOKEN") {
    if (-not [Environment]::GetEnvironmentVariable($v)) { [Environment]::SetEnvironmentVariable($v, [Environment]::GetEnvironmentVariable($v, "User")) }
}
if ($Publish -and $Sites -contains "thunderstore" -and -not $env:TCLI_AUTH_TOKEN) { Write-Error "Set TCLI_AUTH_TOKEN to the Quads_Lab service account token first (in your own terminal, never in a file)"; exit 1 }
if ($Publish -and $Sites -contains "hexium" -and -not $env:HEXIUM_AUTH_TOKEN) { Write-Error "Set HEXIUM_AUTH_TOKEN to the Quads_Lab team's Hexium API token first (in your own terminal, never in a file)"; exit 1 }
$py = Get-Command python -ErrorAction SilentlyContinue
if ($Publish -and $Sites -contains "hexium" -and -not $py) { Write-Error "Publishing on Hexium needs Python (tools/hexium_publish.py)"; exit 1 }

$tcli = Get-Command tcli -ErrorAction SilentlyContinue
if (-not $tcli) {
    $local = Join-Path $env:USERPROFILE ".dotnet\tools\tcli.exe"
    if (Test-Path $local) { $tcli = Get-Command $local } else { Write-Error "The Thunderstore CLI isn't installed: dotnet tool install -g tcli (then a new terminal)"; exit 1 }
}

# modkit comes from Claude Tools' repo (valheim-claude-tools, beside this one), or the copy Claude Tools writes into the game's BepInEx/claude
$toolkit = Join-Path $root "..\valheim-claude-tools\tools\ModKit"
$modkit = Join-Path $toolkit "bin\Release\net48\modkit.exe"
if (-not (Test-Path $modkit) -and (Test-Path $toolkit)) {
    dotnet build $toolkit -c Release --nologo -v q
    if ($LASTEXITCODE -ne 0) { Write-Error "modkit didn't build"; exit 1 }
}
if (-not (Test-Path $modkit) -and $env:VALHEIM_DIR) { $modkit = Join-Path $env:VALHEIM_DIR "BepInEx\claude\modkit\modkit.exe" }
if (-not (Test-Path $modkit)) { Write-Error "modkit not found: clone https://github.com/HardHeadHackerHead/valheim-claude-tools beside this repo, or run the game once with Claude Tools"; exit 1 }

$all = Get-ChildItem (Join-Path $root "mods") -Directory | Where-Object { (Test-Path (Join-Path $_.FullName "$($_.Name).csproj")) -and $_.Name -notin @("Scripts.Engine") }   # (Scripts.Engine is carried inside Blockscript.dll: never a mod of its own)
$chosen = if ($Mods.Count -gt 0) { $all | Where-Object { $Mods -contains $_.Name } } else { $all }
$missing = $Mods | Where-Object { $all.Name -notcontains $_ }
if ($missing) { Write-Error "No such mod: $($missing -join ', ')"; exit 1 }

$results = @()
foreach ($mod in $chosen) {
    $name = $mod.Name
    Write-Host "`n== $name" -ForegroundColor Cyan
    # its page goes up as it is now and stays until the next version: no "Screenshot to come" cards, no missing pictures
    if ($py) {
        & $py.Source (Join-Path $root "tools/check_pages.py") $name
        if ($LASTEXITCODE -ne 0) { $results += [pscustomobject]@{ Mod = $name; Package = ""; Result = "page pictures to take (tools/check_pages.py)" }; continue }
    }
    dotnet build (Join-Path $mod.FullName "$name.csproj") -c Release --nologo -v q -p:DeployToGame=false
    if ($LASTEXITCODE -ne 0) { $results += [pscustomobject]@{ Mod = $name; Package = ""; Result = "build failed" }; continue }

    $json = & $modkit package $mod.FullName --namespace $Team --website "$repo/tree/main/mods/$name" --json
    if ($LASTEXITCODE -ne 0) { $results += [pscustomobject]@{ Mod = $name; Package = ""; Result = "modkit package failed" }; continue }
    $pkg = $json | Out-String | ConvertFrom-Json
    foreach ($f in $pkg.findings) { Write-Host ("  {0}: {1}" -f $f.level, $f.what) -ForegroundColor $(if ($f.level -eq "problem") { "Red" } elseif ($f.level -eq "warning") { "Yellow" } else { "Gray" }) }
    if ($pkg.problems -gt 0) { $results += [pscustomobject]@{ Mod = $name; Package = $pkg.package; Result = "$($pkg.problems) problem(s)" }; continue }

    $toml = Join-Path $mod.FullName "thunderstore.toml"
    Push-Location $mod.FullName
    try { & $tcli.Source build --config-path $toml | Out-Null } finally { Pop-Location }
    $zip = Join-Path $mod.FullName "thunderstore-build\$($pkg.package).zip"
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path $zip)) { $results += [pscustomobject]@{ Mod = $name; Package = $pkg.package; Result = "tcli build failed" }; continue }

    $check = & $modkit package check $zip --json | Out-String | ConvertFrom-Json
    if ($check.problems -gt 0) {
        foreach ($f in $check.findings) { Write-Host ("  zip {0}: {1}" -f $f.level, $f.what) -ForegroundColor Red }
        $results += [pscustomobject]@{ Mod = $name; Package = $pkg.package; Result = "zip has $($check.problems) problem(s)" }; continue
    }

    $result = "ready ($($pkg.warnings) warning(s))"
    if ($Publish) {
        $done = @()
        if ($Sites -contains "thunderstore") {
            Push-Location $mod.FullName
            try { & $tcli.Source publish --config-path $toml --file $zip } finally { Pop-Location }
            $done += if ($LASTEXITCODE -eq 0) { "Thunderstore: published" } else { "Thunderstore: FAILED" }
        }
        if ($Sites -contains "hexium") {
            # the same zip; skips a version already there, sets Hexium's categories and install location
            $env:PYTHONIOENCODING = "utf-8"
            & $py.Source (Join-Path $root "tools/hexium_publish.py") $name --publish
            $done += if ($LASTEXITCODE -eq 0) { "Hexium: published" } else { "Hexium: FAILED" }
        }
        $result = $done -join ", "
    }
    $results += [pscustomobject]@{ Mod = $name; Package = $pkg.package; Result = $result }
}

Write-Host ""
$results | Format-Table -AutoSize
if (-not $Publish) { Write-Host "Test a zip before publishing: r2modman > a new, empty profile > Settings > Import local mod > the zip from mods\<Mod>\thunderstore-build." }
