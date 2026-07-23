[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
$widgetsRoot = Join-Path $repoRoot 'spikes\seelen-m0\widgets'
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

function Invoke-SluResourceUnload {
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
        -ArgumentList @('resource', 'unload', 'widget', $quotedWidgetPath) `
        -NoNewWindow `
        -PassThru `
        -Wait
    if ($process.ExitCode -ne 0) {
        throw "Widget unload failed with exit code $($process.ExitCode): $WidgetPath"
    }
}

Invoke-SluResourceUnload -WidgetPath (Join-Path $widgetsRoot 'desktop')
Invoke-SluResourceUnload -WidgetPath (Join-Path $widgetsRoot 'popup')

Write-Host 'M0 위젯을 현재 Seelen 세션에서 unload했습니다. Companion 설치 파일은 보존했습니다.'
