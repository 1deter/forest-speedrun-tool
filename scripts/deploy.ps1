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

    # Contributed location sets live in the repo; the plugin reads them
    # from the BepInEx config folder. Copy them across so a merged pull
    # request actually appears in game.
    #
    # my-spots.txt is the player's own captured spots and is deliberately
    # never in the repo, so this loop cannot overwrite it.
    $repoLocations = Join-Path $root "locations"
    if (Test-Path $repoLocations) {
        $configLocations = Join-Path $GameRoot "BepInEx\config\ForestOverlay\locations"
        New-Item -ItemType Directory -Force -Path $configLocations | Out-Null

        $copied = 0
        Get-ChildItem $repoLocations -Filter *.txt -File | ForEach-Object {
            if ($_.Name -ne "my-spots.txt") {
                Copy-Item $_.FullName $configLocations -Force
                $copied++
            }
        }
        Write-Host "Location sets synced: $copied file(s) -> $configLocations" -ForegroundColor Green
    }

    # The 100% checklist is admin-decided data and ships with the repo.
    $repoCollectibles = Join-Path $root "collectibles"
    if (Test-Path $repoCollectibles) {
        $configCollectibles = Join-Path $GameRoot "BepInEx\config\ForestOverlay\collectibles"
        New-Item -ItemType Directory -Force -Path $configCollectibles | Out-Null

        $n = 0
        Get-ChildItem $repoCollectibles -Filter *.txt -File | ForEach-Object {
            Copy-Item $_.FullName $configCollectibles -Force
            $n++
        }
        Write-Host "Checklists synced: $n file(s) -> $configCollectibles" -ForegroundColor Green
    }
}
finally {
    Pop-Location
}
