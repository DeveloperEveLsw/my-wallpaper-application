$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
$dotnet = if ([string]::IsNullOrWhiteSpace($env:WALLPAPER_DOTNET)) { 'dotnet' } else { $env:WALLPAPER_DOTNET }
Push-Location $repoRoot

try {
    & $dotnet restore Wallpaper.slnx
    if ($LASTEXITCODE -ne 0) { throw "dotnet restore failed with exit code $LASTEXITCODE" }
    & $dotnet build Wallpaper.slnx --configuration Release --no-restore
    if ($LASTEXITCODE -ne 0) { throw "dotnet build failed with exit code $LASTEXITCODE" }
    & $dotnet test Wallpaper.slnx --configuration Release --no-build
    if ($LASTEXITCODE -ne 0) { throw "dotnet test failed with exit code $LASTEXITCODE" }
}
finally {
    Pop-Location
}
