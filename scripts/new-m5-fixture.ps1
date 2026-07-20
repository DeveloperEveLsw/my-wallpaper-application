param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$fixtureId = Get-Date -Format 'yyyyMMdd-HHmmss'
$fixtureRoot = Join-Path $env:LOCALAPPDATA "MyWallpaperApplication\Fixtures\m5-$fixtureId"

$workFolder = New-Item -ItemType Directory -Path (Join-Path $fixtureRoot 'Work') -Force
$archiveFolder = New-Item -ItemType Directory -Path (Join-Path $fixtureRoot 'Archive') -Force
$emptyFolder = New-Item -ItemType Directory -Path (Join-Path $fixtureRoot 'Empty') -Force

$rootFile = Join-Path $fixtureRoot 'root-shell-menu.txt'
$workFile = Join-Path $workFolder.FullName 'item-shell-menu.txt'
$archiveFile = Join-Path $archiveFolder.FullName 'native-command-check.md'

Set-Content -LiteralPath $rootFile -Value 'Use this root file to validate the native Windows item menu.'
Set-Content -LiteralPath $workFile -Value 'Use this folder file to validate item hit testing and Shell commands.'
Set-Content -LiteralPath $archiveFile -Value '# M5 native command fixture'

[PSCustomObject]@{
    RootPath = $fixtureRoot
    RootFilePath = $rootFile
    WorkPath = $workFolder.FullName
    WorkFilePath = $workFile
    ArchivePath = $archiveFolder.FullName
    ArchiveFilePath = $archiveFile
    EmptyPath = $emptyFolder.FullName
}
