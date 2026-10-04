# Builds every mod and copies the results into .\dist, ready to commit and push to the private repo.
# ModUpdater is skipped: it's installed once by hand and shouldn't overwrite itself.
$root = $PSScriptRoot
$dist = Join-Path $root "dist"
New-Item -ItemType Directory -Force $dist | Out-Null

foreach ($proj in Get-ChildItem (Join-Path $root "mods") -Directory) {
    if ($proj.Name -eq "ModUpdater") { continue }
    $csproj = Join-Path $proj.FullName "$($proj.Name).csproj"
    if (-not (Test-Path $csproj)) { continue }

    Write-Host "Building $($proj.Name)..."
    dotnet build $csproj -c Release --nologo -v q
    if ($LASTEXITCODE -ne 0) { Write-Error "Build failed for $($proj.Name)"; exit 1 }

    $out = Join-Path $proj.FullName "bin\Release\net48"
    Copy-Item (Join-Path $out "$($proj.Name).dll") $dist -Force
    Copy-Item (Join-Path $out "$($proj.Name).pdb") $dist -Force   # ScriptEngine needs the .pdb beside the DLL
}

# The updater goes in installer\ (used by install.ps1), never dist\.
Write-Host "Building ModUpdater..."
$updater = Join-Path $root "mods\ModUpdater"
dotnet build (Join-Path $updater "ModUpdater.csproj") -c Release --nologo -v q
if ($LASTEXITCODE -ne 0) { Write-Error "Build failed for ModUpdater"; exit 1 }
Copy-Item (Join-Path $updater "bin\Release\net48\ModUpdater.dll") (Join-Path $root "installer") -Force

Write-Host "`nReady in $dist :"
Get-ChildItem $dist | Select-Object Name, Length, LastWriteTime
Write-Host "`nNext: git add dist; git commit -m 'Update mods'; git push"
