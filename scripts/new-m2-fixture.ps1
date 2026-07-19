param(
    [ValidateRange(0, 5000)]
    [int]$BulkFileCount = 1200
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$fixtureId = Get-Date -Format 'yyyyMMdd-HHmmss'
$fixtureRoot = Join-Path $env:LOCALAPPDATA "MyWallpaperApplication\Fixtures\m2-$fixtureId"

$workFolder = New-Item -ItemType Directory -Path (Join-Path $fixtureRoot 'Work') -Force
$photosFolder = New-Item -ItemType Directory -Path (Join-Path $fixtureRoot 'Photos') -Force
$emptyFolder = New-Item -ItemType Directory -Path (Join-Path $fixtureRoot 'Empty') -Force
$bulkFolder = New-Item -ItemType Directory -Path (Join-Path $fixtureRoot 'Bulk') -Force
$nestedFolder = New-Item -ItemType Directory -Path (Join-Path $workFolder.FullName 'Nested-Ignored') -Force

Set-Content -LiteralPath (Join-Path $workFolder.FullName 'report.txt') -Value 'M2 fixture report'
Set-Content -LiteralPath (Join-Path $workFolder.FullName 'notes.md') -Value '# M2 fixture notes'
Set-Content -LiteralPath (Join-Path $nestedFolder.FullName 'not-visible.txt') -Value 'Nested content is intentionally ignored.'
Set-Content -LiteralPath (Join-Path $fixtureRoot 'loose-file.txt') -Value 'This file appears inside the virtual ellipsis card.'

Add-Type -AssemblyName System.Drawing
$bitmap = [System.Drawing.Bitmap]::new(320, 200)
try {
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.Clear([System.Drawing.Color]::FromArgb(35, 62, 116))
        $brush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(236, 164, 199))
        $font = [System.Drawing.Font]::new('Segoe UI', 34, [System.Drawing.FontStyle]::Bold)
        try {
            $graphics.FillEllipse($brush, 32, 28, 144, 144)
            $graphics.DrawString('M2', $font, [System.Drawing.Brushes]::White, 194, 68)
        }
        finally {
            $font.Dispose()
            $brush.Dispose()
        }
    }
    finally {
        $graphics.Dispose()
    }

    $bitmap.Save(
        (Join-Path $photosFolder.FullName 'thumbnail-source.png'),
        [System.Drawing.Imaging.ImageFormat]::Png)
}
finally {
    $bitmap.Dispose()
}

for ($index = 1; $index -le $BulkFileCount; $index++) {
    Set-Content -LiteralPath (Join-Path $bulkFolder.FullName "File$index.txt") -Value "M2 bulk fixture $index"
}

[PSCustomObject]@{
    RootPath = $fixtureRoot
    EmptyFolderPath = $emptyFolder.FullName
    BulkFileCount = $BulkFileCount
}
