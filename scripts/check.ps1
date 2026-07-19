$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
Push-Location $repoRoot

try {
    dotnet restore Wallpaper.slnx
    dotnet build Wallpaper.slnx --configuration Release --no-restore
    dotnet test Wallpaper.slnx --configuration Release --no-build
}
finally {
    Pop-Location
}
