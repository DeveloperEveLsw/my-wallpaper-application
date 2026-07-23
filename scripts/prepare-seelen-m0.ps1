[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [string]$RuntimeIdentifier = 'win-x64',
    [switch]$SkipResourceLoad
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
$companionProject = Join-Path `
    $repoRoot `
    'spikes\seelen-m0\companion\Wallpaper.Seelen.M0.Companion\Wallpaper.Seelen.M0.Companion.csproj'
$widgetsRoot = Join-Path $repoRoot 'spikes\seelen-m0\widgets'
$desktopWidget = Join-Path $widgetsRoot 'desktop'
$popupWidget = Join-Path $widgetsRoot 'popup'
$installRoot = Join-Path $env:LOCALAPPDATA 'WallpaperSeelenM0'
$dotnet = if ([string]::IsNullOrWhiteSpace($env:WALLPAPER_DOTNET)) {
    'dotnet'
}
else {
    $env:WALLPAPER_DOTNET
}
$npm = if ([string]::IsNullOrWhiteSpace($env:WALLPAPER_NPM)) {
    'npm'
}
else {
    $env:WALLPAPER_NPM
}

if ($repoRoot.StartsWith('\\', [StringComparison]::Ordinal)) {
    throw 'Windows 로컬 파일 시스템 checkout에서 실행하세요. WSL UNC 경로 publish는 지원하지 않습니다.'
}

$runningCompanion = Get-Process -Name 'Wallpaper.Seelen.M0.Companion' -ErrorAction SilentlyContinue
if ($null -ne $runningCompanion) {
    throw 'Companion이 실행 중입니다. 종료 후 prepare-seelen-m0.ps1을 다시 실행하세요.'
}

New-Item -ItemType Directory -Force -Path $installRoot | Out-Null

Push-Location $repoRoot
try {
    & $dotnet publish `
        $companionProject `
        --configuration $Configuration `
        --runtime $RuntimeIdentifier `
        --self-contained true `
        --output $installRoot `
        -p:PublishSingleFile=true
    if ($LASTEXITCODE -ne 0) {
        throw "Companion publish failed with exit code $LASTEXITCODE"
    }

    & $npm --prefix $widgetsRoot ci --ignore-scripts
    if ($LASTEXITCODE -ne 0) {
        throw "Widget npm ci failed with exit code $LASTEXITCODE"
    }

    & $npm --prefix $widgetsRoot run check
    if ($LASTEXITCODE -ne 0) {
        throw "Widget build failed with exit code $LASTEXITCODE"
    }

    if (-not $SkipResourceLoad) {
        $slu = Get-Command `
            'slu.exe' `
            -CommandType Application `
            -ErrorAction SilentlyContinue
        if ($null -eq $slu) {
            $seelenPackage = Get-AppxPackage 'Seelen.SeelenUI' -ErrorAction SilentlyContinue
            $appxSlu = if ($null -eq $seelenPackage) {
                $null
            }
            else {
                Join-Path $seelenPackage.InstallLocation 'slu.exe'
            }

            if ($null -eq $appxSlu -or -not (Test-Path -LiteralPath $appxSlu -PathType Leaf)) {
                throw 'Seelen slu.exe를 PATH 또는 Seelen Appx 설치 폴더에서 찾을 수 없습니다.'
            }

            $sluPath = $appxSlu
        }
        else {
            $sluPath = $slu.Source
        }

        function Invoke-SluResourceLoad {
            param(
                [Parameter(Mandatory)]
                [string]$WidgetPath
            )

            if ($WidgetPath.IndexOf('"', [StringComparison]::Ordinal) -ge 0) {
                throw 'Widget path cannot contain a quotation mark.'
            }

            $quotedWidgetPath = '"' + $WidgetPath + '"'
            $process = Start-Process `
                -FilePath $sluPath `
                -ArgumentList @('resource', 'load', 'widget', $quotedWidgetPath) `
                -NoNewWindow `
                -PassThru `
                -Wait
            if ($process.ExitCode -ne 0) {
                throw "Widget load failed with exit code $($process.ExitCode): $WidgetPath"
            }
        }

        Invoke-SluResourceLoad -WidgetPath $desktopWidget
        Invoke-SluResourceLoad -WidgetPath $popupWidget
    }
}
finally {
    Pop-Location
}

$companionPath = Join-Path $installRoot 'Wallpaper.Seelen.M0.Companion.exe'
if (-not (Test-Path -LiteralPath $companionPath -PathType Leaf)) {
    throw "Published Companion was not found: $companionPath"
}

[pscustomobject]@{
    CompanionPath = $companionPath
    DesktopWidget = $desktopWidget
    PopupWidget = $popupWidget
    ResourcesLoaded = -not $SkipResourceLoad
}
