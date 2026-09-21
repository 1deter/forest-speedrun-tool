<#
    Builds the plugin and copies it into BepInEx/plugins.

    Usage:
        ./scripts/deploy.ps1
        ./scripts/deploy.ps1 -GameRoot "G:\SteamLibrary\steamapps\common\The Forest"

    Set FOREST_ROOT as an environment variable to avoid passing -GameRoot.
#>
param(
    [string]$GameRoot = $env:FOREST_ROOT,
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

if (-not $GameRoot) {
    throw "No game root. Pass -GameRoot or set the FOREST_ROOT environment variable."
}

$managed = Join-Path $GameRoot "TheForest_Data\Managed"
if (-not (Test-Path $managed)) {
    throw "Managed folder not found at: $managed"
}

$pluginDir = Join-Path $GameRoot "BepInEx\plugins"
if (-not (Test-Path $pluginDir)) {
    throw "BepInEx plugins folder not found at: $pluginDir. Is BepInEx installed?"
}

$root = Split-Path $PSScriptRoot -Parent
Push-Location $root
try {
    Write-Host "Building against $managed" -ForegroundColor Cyan
    dotnet build -c $Configuration -p:ForestManagedPath="$managed"
    if ($LASTEXITCODE -ne 0) { throw "Build failed." }

    $dll = Join-Path $root "bin\$Configuration\net35\ForestOverlay.dll"
    if (-not (Test-Path $dll)) { throw "Built DLL not found at $dll" }

    Copy-Item $dll $pluginDir -Force
    Write-Host "Deployed -> $pluginDir" -ForegroundColor Green
}
finally {
    Pop-Location
}
