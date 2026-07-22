param(
    [string]$WallpaperEnginePath,
    [string]$ProjectName = 'my-wallpaper-application-m6',
    [int]$Monitor = 0,
    [switch]$Deploy,
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [switch]$FrameworkDependent,
    [switch]$Reload,
    [switch]$RestartEngine,
    [ValidateSet('Keep', 'Hide', 'Show')]
    [string]$DesktopIcons = 'Keep'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

. (Join-Path $PSScriptRoot 'wallpaper-engine-common.ps1')

if ($Deploy) {
    $deployment = & (Join-Path $PSScriptRoot 'deploy-wallpaper-engine.ps1') `
        -WallpaperEnginePath $WallpaperEnginePath `
        -ProjectName $ProjectName `
        -Configuration $Configuration `
        -FrameworkDependent:$FrameworkDependent
    $WallpaperEnginePath = $deployment.WallpaperEnginePath
}

$engineDirectory = Find-WallpaperEngineDirectory -ExplicitPath $WallpaperEnginePath
$projectDirectory = Get-WallpaperEngineProjectDirectory `
    -WallpaperEngineDirectory $engineDirectory `
    -ProjectName $ProjectName
$projectFile = Join-Path $projectDirectory 'project.json'
$applicationExecutable = Join-Path $projectDirectory 'app\Wallpaper.App.exe'
if (-not (Test-Path -LiteralPath $projectFile)) {
    throw "배포된 Wallpaper Engine project.json을 찾을 수 없습니다: $projectFile"
}
if (-not (Test-Path -LiteralPath $applicationExecutable)) {
    throw "배포된 Wallpaper Application 실행 파일을 찾을 수 없습니다: $applicationExecutable"
}

$stoppedProcessIds = @()
if ($Reload -or $RestartEngine) {
    $stoppedProcessIds = @(
        Stop-WallpaperEngineApplication -ExecutablePath $applicationExecutable
    )
}

$engineExecutable = Join-Path $engineDirectory 'wallpaper64.exe'
$stoppedEngineProcessIds = @()
if ($RestartEngine) {
    $stoppedEngineProcessIds = @(
        Get-CimInstance Win32_Process -Filter "Name = 'wallpaper64.exe'" |
            Where-Object {
                -not [string]::IsNullOrWhiteSpace($_.ExecutablePath) -and
                [IO.Path]::GetFullPath($_.ExecutablePath).Equals(
                    $engineExecutable,
                    [StringComparison]::OrdinalIgnoreCase)
            } |
            ForEach-Object {
                Stop-Process -Id $_.ProcessId
                $_.ProcessId
            }
    )

    foreach ($processId in $stoppedEngineProcessIds) {
        $process = Get-Process -Id $processId -ErrorAction SilentlyContinue
        if ($null -ne $process -and -not $process.WaitForExit(5000)) {
            throw "Wallpaper Engine 프로세스가 종료되지 않았습니다: $processId"
        }
    }

    Start-Process -FilePath $engineExecutable -ArgumentList @('-silent') | Out-Null
    $deadline = [DateTime]::UtcNow.AddSeconds(15)
    do {
        Start-Sleep -Milliseconds 250
        $runningEngine = Get-CimInstance Win32_Process -Filter "Name = 'wallpaper64.exe'" |
            Where-Object {
                -not [string]::IsNullOrWhiteSpace($_.ExecutablePath) -and
                [IO.Path]::GetFullPath($_.ExecutablePath).Equals(
                    $engineExecutable,
                    [StringComparison]::OrdinalIgnoreCase)
            } |
            Select-Object -First 1
    } while ($null -eq $runningEngine -and [DateTime]::UtcNow -lt $deadline)

    if ($null -eq $runningEngine) {
        throw 'Wallpaper Engine 재시작을 확인하지 못했습니다.'
    }

    Start-Sleep -Seconds 2
}

$applicationProcess = $null
$reusedAutoLoadedWallpaper = $false
if ($RestartEngine) {
    try {
        $applicationProcess = Wait-WallpaperEngineApplication `
            -ExecutablePath $applicationExecutable `
            -TimeoutSeconds 3
        $reusedAutoLoadedWallpaper = $true
    }
    catch {
        $applicationProcess = $null
    }
}

if ($null -eq $applicationProcess) {
    $quotedProjectFile = '"' + $projectFile + '"'
    $engineProcess = Start-Process `
        -FilePath $engineExecutable `
        -ArgumentList @('-control', 'openWallpaper', '-file', $quotedProjectFile, '-monitor', $Monitor) `
        -Wait `
        -PassThru
    if ($engineProcess.ExitCode -ne 0) {
        throw "Wallpaper Engine openWallpaper failed with exit code $($engineProcess.ExitCode)"
    }

    $applicationProcess = Wait-WallpaperEngineApplication `
        -ExecutablePath $applicationExecutable
}

if ($DesktopIcons -ne 'Keep') {
    $iconCommand = if ($DesktopIcons -eq 'Hide') { 'hideIcons' } else { 'showIcons' }
    $iconProcess = Start-Process `
        -FilePath $engineExecutable `
        -ArgumentList @('-control', $iconCommand) `
        -Wait `
        -PassThru
    if ($iconProcess.ExitCode -ne 0) {
        throw "Wallpaper Engine $iconCommand failed with exit code $($iconProcess.ExitCode)"
    }
}

[pscustomobject]@{
    ProjectFile = $projectFile
    Monitor = $Monitor
    ProcessId = $applicationProcess.ProcessId
    ParentProcessId = $applicationProcess.ParentProcessId
    ParentWindowArgument = $applicationProcess.CommandLine
    Reloaded = $Reload
    EngineRestarted = $RestartEngine
    ReusedAutoLoadedWallpaper = $reusedAutoLoadedWallpaper
    StoppedProcessIds = $stoppedProcessIds
    StoppedEngineProcessIds = $stoppedEngineProcessIds
    DesktopIcons = $DesktopIcons
}
