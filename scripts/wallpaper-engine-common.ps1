$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Find-WallpaperEngineDirectory {
    param([string]$ExplicitPath)

    if (-not [string]::IsNullOrWhiteSpace($ExplicitPath)) {
        $resolved = [IO.Path]::GetFullPath($ExplicitPath)
        if (-not (Test-Path -LiteralPath (Join-Path $resolved 'wallpaper64.exe'))) {
            throw "Wallpaper Engine 실행 파일을 찾을 수 없습니다: $resolved"
        }

        return $resolved
    }

    $candidates = [Collections.Generic.List[string]]::new()
    $steam = Get-ItemProperty -Path 'HKCU:\Software\Valve\Steam' -ErrorAction SilentlyContinue
    if ($null -ne $steam -and -not [string]::IsNullOrWhiteSpace($steam.SteamPath)) {
        $steamPath = [IO.Path]::GetFullPath($steam.SteamPath)
        $candidates.Add((Join-Path $steamPath 'steamapps\common\wallpaper_engine'))

        $libraryFile = Join-Path $steamPath 'steamapps\libraryfolders.vdf'
        if (Test-Path -LiteralPath $libraryFile) {
            foreach ($line in Get-Content -LiteralPath $libraryFile) {
                if ($line -match '^\s*"path"\s+"(.+)"\s*$') {
                    $libraryPath = $Matches[1] -replace '\\\\', '\'
                    $candidates.Add([IO.Path]::Combine(
                        $libraryPath,
                        'steamapps\common\wallpaper_engine'))
                }
            }
        }
    }

    foreach ($drive in [IO.DriveInfo]::GetDrives()) {
        if ($drive.DriveType -eq [IO.DriveType]::Fixed) {
            $candidates.Add((Join-Path $drive.RootDirectory.FullName 'SteamLibrary\steamapps\common\wallpaper_engine'))
        }
    }

    foreach ($candidate in $candidates | Select-Object -Unique) {
        if (Test-Path -LiteralPath (Join-Path $candidate 'wallpaper64.exe')) {
            return [IO.Path]::GetFullPath($candidate)
        }
    }

    throw 'Wallpaper Engine 설치 경로를 찾지 못했습니다. -WallpaperEnginePath를 지정하세요.'
}

function Get-WallpaperEngineProjectDirectory {
    param(
        [Parameter(Mandatory)]
        [string]$WallpaperEngineDirectory,
        [Parameter(Mandatory)]
        [string]$ProjectName
    )

    if ($ProjectName -notmatch '^[a-zA-Z0-9._-]+$') {
        throw 'ProjectName에는 영문, 숫자, 점, 밑줄과 하이픈만 사용할 수 있습니다.'
    }

    return Join-Path $WallpaperEngineDirectory "projects\myprojects\$ProjectName"
}

function Stop-WallpaperEngineApplication {
    param(
        [Parameter(Mandatory)]
        [string]$ExecutablePath
    )

    $targetPath = [IO.Path]::GetFullPath($ExecutablePath)
    $targetName = [IO.Path]::GetFileName($targetPath)
    $stoppedProcessIds = [Collections.Generic.List[int]]::new()
    $processes = Get-CimInstance Win32_Process -Filter "Name = '$targetName'"
    foreach ($process in $processes) {
        if ([string]::IsNullOrWhiteSpace($process.ExecutablePath)) {
            continue
        }

        $processPath = [IO.Path]::GetFullPath($process.ExecutablePath)
        if (-not $processPath.Equals($targetPath, [StringComparison]::OrdinalIgnoreCase)) {
            continue
        }

        Stop-Process -Id $process.ProcessId
        $stoppedProcessIds.Add($process.ProcessId)
    }

    foreach ($processId in $stoppedProcessIds) {
        $process = Get-Process -Id $processId -ErrorAction SilentlyContinue
        if ($null -ne $process -and -not $process.WaitForExit(5000)) {
            throw "Wallpaper Application 프로세스가 종료되지 않았습니다: $processId"
        }
    }

    return $stoppedProcessIds.ToArray()
}

function Wait-WallpaperEngineApplication {
    param(
        [Parameter(Mandatory)]
        [string]$ExecutablePath,
        [int]$TimeoutSeconds = 15
    )

    $targetPath = [IO.Path]::GetFullPath($ExecutablePath)
    $targetName = [IO.Path]::GetFileName($targetPath)
    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    do {
        $process = Get-CimInstance Win32_Process -Filter "Name = '$targetName'" |
            Where-Object {
                -not [string]::IsNullOrWhiteSpace($_.ExecutablePath) -and
                [IO.Path]::GetFullPath($_.ExecutablePath).Equals(
                    $targetPath,
                    [StringComparison]::OrdinalIgnoreCase) -and
                $_.CommandLine -match '(?i)(^|\s)-parentHWND(\s|$)'
            } |
            Select-Object -First 1
        if ($null -ne $process) {
            return $process
        }

        Start-Sleep -Milliseconds 250
    } while ([DateTime]::UtcNow -lt $deadline)

    throw "Wallpaper Engine이 배포된 앱을 시작하지 않았습니다: $targetPath"
}
