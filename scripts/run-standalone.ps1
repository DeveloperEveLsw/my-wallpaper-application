$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
Push-Location $repoRoot

try {
    dotnet run --project src/Wallpaper.App/Wallpaper.App.csproj --configuration Debug
}
finally {
    Pop-Location
}
