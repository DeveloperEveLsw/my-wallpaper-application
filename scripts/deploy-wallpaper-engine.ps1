param(
    [string]$WallpaperEnginePath,
    [string]$ProjectName = 'my-wallpaper-application-m6',
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [ValidateSet('win-x64', 'win-arm64')]
    [string]$RuntimeIdentifier = 'win-x64',
    [switch]$FrameworkDependent
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

. (Join-Path $PSScriptRoot 'wallpaper-engine-common.ps1')

$repoRoot = Split-Path -Parent $PSScriptRoot
if ($repoRoot.StartsWith('\\')) {
    throw @'
WPF publish는 Windows 로컬 파일 시스템 checkout에서 실행해야 합니다.
WSL UNC 경로 대신 Windows 드라이브의 작업 폴더에서 이 스크립트를 실행하세요.
'@
}

$engineDirectory = Find-WallpaperEngineDirectory -ExplicitPath $WallpaperEnginePath
$projectDirectory = Get-WallpaperEngineProjectDirectory `
    -WallpaperEngineDirectory $engineDirectory `
    -ProjectName $ProjectName
$applicationDirectory = Join-Path $projectDirectory 'app'
$applicationExecutable = Join-Path $applicationDirectory 'Wallpaper.App.exe'
$projectFile = Join-Path $projectDirectory 'project.json'
$manifestTemplate = Join-Path $repoRoot 'packaging\wallpaper-engine\project.json'
$dotnet = if ([string]::IsNullOrWhiteSpace($env:WALLPAPER_DOTNET)) { 'dotnet' } else { $env:WALLPAPER_DOTNET }

New-Item -ItemType Directory -Path $projectDirectory -Force | Out-Null
$stoppedProcessIds = Stop-WallpaperEngineApplication -ExecutablePath $applicationExecutable
if (Test-Path -LiteralPath $applicationDirectory) {
    Remove-Item -LiteralPath $applicationDirectory -Recurse -Force
}
New-Item -ItemType Directory -Path $applicationDirectory -Force | Out-Null

$publishArguments = @(
    'publish',
    (Join-Path $repoRoot 'src\Wallpaper.App\Wallpaper.App.csproj'),
    '--configuration', $Configuration,
    '--runtime', $RuntimeIdentifier,
    '--output', $applicationDirectory,
    '--nologo',
    '-p:PublishSingleFile=false'
)
if ($FrameworkDependent) {
    $publishArguments += '--no-self-contained'
} else {
    $publishArguments += '--self-contained'
}

& $dotnet @publishArguments | Out-Host
$publishExitCode = $LASTEXITCODE
if ($publishExitCode -ne 0) {
    throw "dotnet publish failed with exit code $publishExitCode"
}

$windowsSdkAssembly = Join-Path $applicationDirectory 'Microsoft.Windows.SDK.NET.dll'
if (-not (Test-Path -LiteralPath $windowsSdkAssembly)) {
    throw "WebView2 composition dependency was not published: $windowsSdkAssembly"
}

Copy-Item -LiteralPath $manifestTemplate -Destination $projectFile -Force

[pscustomobject]@{
    WallpaperEnginePath = $engineDirectory
    ProjectDirectory = $projectDirectory
    ProjectFile = $projectFile
    Executable = $applicationExecutable
    SelfContained = -not $FrameworkDependent
    StoppedProcessIds = $stoppedProcessIds
}
