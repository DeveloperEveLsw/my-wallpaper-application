$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
$dotnet = if ([string]::IsNullOrWhiteSpace($env:WALLPAPER_DOTNET)) { 'dotnet' } else { $env:WALLPAPER_DOTNET }
$node = if ([string]::IsNullOrWhiteSpace($env:WALLPAPER_NODE)) { 'node' } else { $env:WALLPAPER_NODE }
Push-Location $repoRoot

try {
    & $dotnet restore Wallpaper.slnx
    if ($LASTEXITCODE -ne 0) { throw "dotnet restore failed with exit code $LASTEXITCODE" }
    & $dotnet build Wallpaper.slnx --configuration Release --no-restore
    if ($LASTEXITCODE -ne 0) { throw "dotnet build failed with exit code $LASTEXITCODE" }
    & $dotnet test Wallpaper.slnx --configuration Release --no-build
    if ($LASTEXITCODE -ne 0) { throw "dotnet test failed with exit code $LASTEXITCODE" }
    $visualizerScripts = Get-ChildItem `
        (Join-Path $repoRoot 'src\Wallpaper.Rendering.WebView\WebAssets') `
        -Filter '*.js' `
        -File `
        -Recurse |
        Where-Object { $_.FullName -notlike '*\vendor\*' } |
        Sort-Object FullName
    foreach ($visualizerScript in $visualizerScripts) {
        & $node --check $visualizerScript.FullName
        if ($LASTEXITCODE -ne 0) {
            throw "visualizer syntax check failed for $($visualizerScript.FullName)"
        }
    }
}
finally {
    Pop-Location
}
