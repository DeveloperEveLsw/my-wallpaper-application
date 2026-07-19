$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$fixtureId = Get-Date -Format 'yyyyMMdd-HHmmss'
$fixtureRoot = Join-Path $env:LOCALAPPDATA "MyWallpaperApplication\Fixtures\mvp-$fixtureId"

$workFolder = New-Item -ItemType Directory -Path (Join-Path $fixtureRoot 'Work') -Force
$photosFolder = New-Item -ItemType Directory -Path (Join-Path $fixtureRoot 'Photos') -Force
$nestedFolder = New-Item -ItemType Directory -Path (Join-Path $workFolder.FullName 'Nested-Ignored') -Force

Set-Content -LiteralPath (Join-Path $workFolder.FullName 'report.txt') -Value 'MVP fixture report'
Set-Content -LiteralPath (Join-Path $workFolder.FullName 'notes.md') -Value '# MVP fixture notes'
Set-Content -LiteralPath (Join-Path $photosFolder.FullName 'image-placeholder.txt') -Value 'Use new-m2-fixture.ps1 for thumbnail validation.'
Set-Content -LiteralPath (Join-Path $nestedFolder.FullName 'not-visible.txt') -Value 'Nested content is intentionally ignored.'
Set-Content -LiteralPath (Join-Path $fixtureRoot 'loose-file.txt') -Value 'This file appears inside the virtual ellipsis card.'

Write-Output $fixtureRoot
