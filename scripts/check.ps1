$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
$dotnet = if ([string]::IsNullOrWhiteSpace($env:WALLPAPER_DOTNET)) { 'dotnet' } else { $env:WALLPAPER_DOTNET }
$node = if ([string]::IsNullOrWhiteSpace($env:WALLPAPER_NODE)) { 'node' } else { $env:WALLPAPER_NODE }
$npm = if ([string]::IsNullOrWhiteSpace($env:WALLPAPER_NPM)) { 'npm' } else { $env:WALLPAPER_NPM }
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

    $widgetsRoot = Join-Path $repoRoot 'spikes\seelen-m0\widgets'
    & $npm --prefix $widgetsRoot ci --ignore-scripts
    if ($LASTEXITCODE -ne 0) { throw "widget npm ci failed with exit code $LASTEXITCODE" }
    & $npm --prefix $widgetsRoot run check
    if ($LASTEXITCODE -ne 0) { throw "widget build failed with exit code $LASTEXITCODE" }

    $productWidgetsRoot = Join-Path $repoRoot 'src\Wallpaper.Seelen.Widgets'
    & $npm --prefix $productWidgetsRoot ci --ignore-scripts
    if ($LASTEXITCODE -ne 0) { throw "product widget npm ci failed with exit code $LASTEXITCODE" }
    & $npm --prefix $productWidgetsRoot run check
    if ($LASTEXITCODE -ne 0) { throw "product widget build failed with exit code $LASTEXITCODE" }
}
finally {
    Pop-Location
}
