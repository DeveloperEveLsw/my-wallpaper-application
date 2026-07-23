param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$fixtureId = '{0}-{1}' -f `
    (Get-Date -Format 'yyyyMMdd-HHmmss-fff'), `
    ([Guid]::NewGuid().ToString('N').Substring(0, 8))
$fixtureRoot = Join-Path $env:LOCALAPPDATA "MyWallpaperApplication\Fixtures\m4-$fixtureId"

$workFolder = New-Item -ItemType Directory -Path (Join-Path $fixtureRoot 'Work') -Force
$archiveFolder = New-Item -ItemType Directory -Path (Join-Path $fixtureRoot 'Archive') -Force
$photosFolder = New-Item -ItemType Directory -Path (Join-Path $fixtureRoot 'Photos') -Force

Set-Content -LiteralPath (Join-Path $fixtureRoot 'root-to-work.txt') -Value 'Move this file from the ellipsis card to Work.'
Set-Content -LiteralPath (Join-Path $workFolder.FullName 'work-to-root.txt') -Value 'Move this file from Work to the ellipsis card.'
Set-Content -LiteralPath (Join-Path $workFolder.FullName 'work-to-archive.txt') -Value 'Move this file from Work to Archive.'
Set-Content -LiteralPath (Join-Path $workFolder.FullName 'collision.txt') -Value 'This is the source collision file.'
Set-Content -LiteralPath (Join-Path $workFolder.FullName 'locked-move.txt') -Value 'Lock this file before attempting a move.'
Set-Content -LiteralPath (Join-Path $workFolder.FullName 'stale-source.txt') -Value 'Delete or rename this file while a collision dialog is open.'

Set-Content -LiteralPath (Join-Path $archiveFolder.FullName 'collision.txt') -Value 'Occupied collision name zero.'
Set-Content -LiteralPath (Join-Path $archiveFolder.FullName 'collision (1).txt') -Value 'Occupied collision name one.'
Set-Content -LiteralPath (Join-Path $archiveFolder.FullName 'stale-source.txt') -Value 'Keeps the stale-source collision dialog open.'
$null = New-Item -ItemType Directory -Path (Join-Path $archiveFolder.FullName 'collision (2).txt') -Force

Set-Content -LiteralPath (Join-Path $photosFolder.FullName 'photo-note.txt') -Value 'Additional valid destination fixture.'

[PSCustomObject]@{
    RootPath = $fixtureRoot
    RootToWorkPath = Join-Path $fixtureRoot 'root-to-work.txt'
    WorkToRootPath = Join-Path $workFolder.FullName 'work-to-root.txt'
    WorkToArchivePath = Join-Path $workFolder.FullName 'work-to-archive.txt'
    CollisionSourcePath = Join-Path $workFolder.FullName 'collision.txt'
    LockedFilePath = Join-Path $workFolder.FullName 'locked-move.txt'
    StaleSourcePath = Join-Path $workFolder.FullName 'stale-source.txt'
    ArchivePath = $archiveFolder.FullName
}
