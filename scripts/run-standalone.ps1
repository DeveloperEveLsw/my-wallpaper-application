param(
    [string]$RootPath,
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
$previousSettingsDirectory = $env:WALLPAPER_SETTINGS_DIRECTORY
$dotnet = if ([string]::IsNullOrWhiteSpace($env:WALLPAPER_DOTNET)) { 'dotnet' } else { $env:WALLPAPER_DOTNET }
Push-Location $repoRoot

try {
    if (-not [string]::IsNullOrWhiteSpace($RootPath)) {
        $settingsDirectory = Join-Path $env:TEMP "wallpaper-standalone-$([Guid]::NewGuid().ToString('N'))"
        New-Item -ItemType Directory -Path $settingsDirectory -Force | Out-Null
        @{
            schemaVersion = 1
            rootPath = [IO.Path]::GetFullPath($RootPath)
            folderOrder = @()
        } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $settingsDirectory 'settings.json') -Encoding UTF8
        $env:WALLPAPER_SETTINGS_DIRECTORY = $settingsDirectory
    }

    & $dotnet run --project src/Wallpaper.App/Wallpaper.App.csproj --configuration $Configuration
    if ($LASTEXITCODE -ne 0) { throw "dotnet run failed with exit code $LASTEXITCODE" }
}
finally {
    $env:WALLPAPER_SETTINGS_DIRECTORY = $previousSettingsDirectory
    Pop-Location
}
