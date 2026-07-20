param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$fixtureId = Get-Date -Format 'yyyyMMdd-HHmmss'
$fixtureRoot = Join-Path $env:LOCALAPPDATA "MyWallpaperApplication\Fixtures\m3-$fixtureId"

$workFolder = New-Item -ItemType Directory -Path (Join-Path $fixtureRoot 'Work') -Force
$renameFolder = New-Item -ItemType Directory -Path (Join-Path $fixtureRoot 'RenameFolder') -Force
$recycleFolder = New-Item -ItemType Directory -Path (Join-Path $fixtureRoot 'RecycleFolder') -Force
$nestedRecycleFolder = New-Item -ItemType Directory -Path (Join-Path $recycleFolder.FullName 'Nested-Not-Visible') -Force

Set-Content -LiteralPath (Join-Path $workFolder.FullName 'open-with-notepad.txt') -Value 'M3 default application fixture'
Set-Content -LiteralPath (Join-Path $workFolder.FullName 'rename-file.txt') -Value 'Rename this file from the Glass menu.'
Set-Content -LiteralPath (Join-Path $workFolder.FullName 'collision.txt') -Value 'This name is intentionally occupied.'
Set-Content -LiteralPath (Join-Path $renameFolder.FullName 'keep-after-folder-rename.txt') -Value 'Folder contents must survive rename.'
Set-Content -LiteralPath (Join-Path $recycleFolder.FullName 'visible-file.txt') -Value 'Recycle the parent folder only after confirmation.'
Set-Content -LiteralPath (Join-Path $nestedRecycleFolder.FullName 'nested-file.txt') -Value 'This nested file is intentionally absent from the UI.'
Set-Content -LiteralPath (Join-Path $fixtureRoot 'root-file.txt') -Value 'Root file shown by the virtual ellipsis card.'
Set-Content -LiteralPath (Join-Path $fixtureRoot 'recycle-file.txt') -Value 'Recycle this file after testing cancel.'

[PSCustomObject]@{
    RootPath = $fixtureRoot
    FileOpenPath = Join-Path $workFolder.FullName 'open-with-notepad.txt'
    RenameFilePath = Join-Path $workFolder.FullName 'rename-file.txt'
    RenameFolderPath = $renameFolder.FullName
    RecycleFilePath = Join-Path $fixtureRoot 'recycle-file.txt'
    RecycleFolderPath = $recycleFolder.FullName
}
