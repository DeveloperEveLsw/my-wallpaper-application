[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [string]$RuntimeIdentifier = 'win-x64',
    [switch]$LoadDevelopmentResource
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
$companionProject = Join-Path `
    $repoRoot `
    'src\Wallpaper.Seelen.Companion\Wallpaper.Seelen.Companion.csproj'
$widgetsRoot = Join-Path $repoRoot 'src\Wallpaper.Seelen.Widgets'
$desktopWidget = Join-Path $widgetsRoot 'desktop'
$companionInstallRoot = Join-Path $env:LOCALAPPDATA 'WallpaperSeelen'
$resourceInstallRoot = Join-Path `
    $env:APPDATA `
    'com.seelen.seelen-ui\widgets\wallpaper-desktop'
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

$runningCompanion = Get-Process -Name 'Wallpaper.Seelen.Companion' -ErrorAction SilentlyContinue
if ($null -ne $runningCompanion) {
    throw '제품 Companion이 실행 중입니다. 종료 후 이 스크립트를 다시 실행하세요.'
}

New-Item -ItemType Directory -Force -Path $companionInstallRoot | Out-Null
Push-Location $repoRoot
try {
    & $dotnet publish `
        $companionProject `
        --configuration $Configuration `
        --runtime $RuntimeIdentifier `
        --self-contained true `
        --output $companionInstallRoot `
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

    New-Item -ItemType Directory -Force -Path $resourceInstallRoot | Out-Null
    foreach ($name in @('metadata.yml', 'index.html', 'index.css', 'index.js')) {
        Copy-Item `
            -LiteralPath (Join-Path $desktopWidget $name) `
            -Destination (Join-Path $resourceInstallRoot $name) `
            -Force
    }

    if ($LoadDevelopmentResource) {
        $slu = Get-Command 'slu.exe' -CommandType Application -ErrorAction SilentlyContinue
        if ($null -eq $slu) {
            $seelenPackage = Get-AppxPackage 'Seelen.SeelenUI' -ErrorAction SilentlyContinue
            $sluPath = if ($null -eq $seelenPackage) {
                $null
            }
            else {
                Join-Path $seelenPackage.InstallLocation 'slu.exe'
            }
        }
        else {
            $sluPath = $slu.Source
        }

        if ([string]::IsNullOrWhiteSpace($sluPath) -or
            -not (Test-Path -LiteralPath $sluPath -PathType Leaf)) {
            throw 'Seelen slu.exe를 PATH 또는 Seelen Appx 설치 폴더에서 찾을 수 없습니다.'
        }

        $quotedWidgetPath = '"' + $desktopWidget + '"'
        $process = Start-Process `
            -FilePath $sluPath `
            -ArgumentList @('resource', 'load', 'widget', $quotedWidgetPath) `
            -NoNewWindow `
            -PassThru `
            -Wait
        if ($process.ExitCode -ne 0) {
            throw "Widget load failed with exit code $($process.ExitCode)"
        }
    }
}
finally {
    Pop-Location
}

$companionPath = Join-Path $companionInstallRoot 'Wallpaper.Seelen.Companion.exe'
if (-not (Test-Path -LiteralPath $companionPath -PathType Leaf)) {
    throw "Published Companion was not found: $companionPath"
}

[pscustomobject]@{
    CompanionPath = $companionPath
    InstalledWidget = $resourceInstallRoot
    DevelopmentResourceLoaded = [bool]$LoadDevelopmentResource
    NextAction = if ($LoadDevelopmentResource) {
        'Seelen에서 @wallpaper/desktop 리소스를 활성화하세요.'
    }
    else {
        'Seelen을 재시작한 뒤 @wallpaper/desktop 리소스를 활성화하세요.'
    }
}
