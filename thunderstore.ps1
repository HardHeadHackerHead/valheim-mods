# Thunderstore packages for our mods, each released on its own under the Quads_Lab team. The same sources as publish.ps1 (which builds the
# dist/ feed that our own mod manager reads from GitHub): both channels ship the same builds.
#
#   .\thunderstore.ps1                          build, package and check every mod (nothing is uploaded)
#   .\thunderstore.ps1 -Mods Arena,Recycler     only these
#   .\thunderstore.ps1 -Mods Arena -Publish     ...and upload them (needs TCLI_AUTH_TOKEN: the Quads_Lab service account token)
#
# Per mod it runs `modkit package` (Claude Tools' modkit: writes mods/<Mod>/thunderstore.toml and checks everything an upload needs), then
# `tcli build` (the zip, in mods/<Mod>/thunderstore-build) and `modkit package check` on the zip. A mod with problems is not built or uploaded.
# Publishing needs the mods named: a version on Thunderstore can never be changed or deleted.
param(
    [string[]]$Mods = @(),
    [switch]$Publish,
    [string]$Team = "Quads_Lab"
)
$ErrorActionPreference = "Continue"  # (native tools write to stderr; each step checks $LASTEXITCODE)
$root = $PSScriptRoot
$repo = "https://github.com/HardHeadHackerHead/valheim-mods"

if ($Publish -and $Mods.Count -eq 0) { Write-Error "Name the mods to publish: -Mods Arena,Recycler -Publish"; exit 1 }
if ($Publish -and -not $env:TCLI_AUTH_TOKEN) { Write-Error "Set TCLI_AUTH_TOKEN to the Quads_Lab service account token first (in your own terminal, never in a file)"; exit 1 }

$tcli = Get-Command tcli -ErrorAction SilentlyContinue
if (-not $tcli) {
    $local = Join-Path $env:USERPROFILE ".dotnet\tools\tcli.exe"
    if (Test-Path $local) { $tcli = Get-Command $local } else { Write-Error "The Thunderstore CLI isn't installed: dotnet tool install -g tcli (then a new terminal)"; exit 1 }
}

$modkit = Join-Path $root "tools\ModKit\bin\Release\net48\modkit.exe"
if (-not (Test-Path $modkit)) {
    dotnet build (Join-Path $root "tools\ModKit") -c Release --nologo -v q
    if ($LASTEXITCODE -ne 0) { Write-Error "modkit didn't build"; exit 1 }
}

$all = Get-ChildItem (Join-Path $root "mods") -Directory | Where-Object { Test-Path (Join-Path $_.FullName "$($_.Name).csproj") }
$chosen = if ($Mods.Count -gt 0) { $all | Where-Object { $Mods -contains $_.Name } } else { $all }
$missing = $Mods | Where-Object { $all.Name -notcontains $_ }
if ($missing) { Write-Error "No such mod: $($missing -join ', ')"; exit 1 }

$results = @()
foreach ($mod in $chosen) {
    $name = $mod.Name
    Write-Host "`n== $name" -ForegroundColor Cyan
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
        Push-Location $mod.FullName
        try { & $tcli.Source publish --config-path $toml --file $zip } finally { Pop-Location }
        $result = if ($LASTEXITCODE -eq 0) { "published" } else { "publish failed" }
    }
    $results += [pscustomobject]@{ Mod = $name; Package = $pkg.package; Result = $result }
}

Write-Host ""
$results | Format-Table -AutoSize
if (-not $Publish) { Write-Host "Test a zip before publishing: r2modman > a new, empty profile > Settings > Import local mod > the zip from mods\<Mod>\thunderstore-build." }
