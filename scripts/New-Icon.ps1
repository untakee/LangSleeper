$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$taskOutput = Join-Path $PSScriptRoot '..\src\LangSleeper\Assets\LangSleeper.ico'
New-Item -ItemType Directory -Path (Split-Path $taskOutput) -Force | Out-Null
$taskImages = @()
foreach ($taskSize in @(16, 32, 48, 64, 256)) {
    $taskBitmap = New-Object System.Drawing.Bitmap($taskSize, $taskSize)
    $taskGraphics = [System.Drawing.Graphics]::FromImage($taskBitmap)
    $taskGraphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $taskGraphics.ScaleTransform(($taskSize / 64.0), ($taskSize / 64.0))
    $taskPath = New-Object System.Drawing.Drawing2D.GraphicsPath
    $taskPath.AddArc(0, 0, 28, 28, 180, 90)
    $taskPath.AddArc(36, 0, 28, 28, 270, 90)
    $taskPath.AddArc(36, 36, 28, 28, 0, 90)
    $taskPath.AddArc(0, 36, 28, 28, 90, 90)
    $taskPath.CloseFigure()
    $taskBase = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(25,28,31))
    $taskMoon = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(248,250,251))
    $taskGraphics.FillPath($taskBase, $taskPath)
    $taskGraphics.FillEllipse($taskMoon, 15, 13, 34, 38)
    $taskGraphics.FillEllipse($taskBase, 27, 7, 30, 34)
    $taskGraphics.FillEllipse($taskMoon, 46, 16, 5, 5)
    $taskStream = New-Object System.IO.MemoryStream
    $taskBitmap.Save($taskStream, [System.Drawing.Imaging.ImageFormat]::Png)
    $taskImages += ,@($taskSize, $taskStream.ToArray())
    $taskStream.Dispose(); $taskGraphics.Dispose(); $taskBitmap.Dispose()
    $taskPath.Dispose(); $taskBase.Dispose(); $taskMoon.Dispose()
}
$taskWriter = New-Object System.IO.BinaryWriter([System.IO.File]::Create($taskOutput))
try {
    $taskWriter.Write([uint16]0); $taskWriter.Write([uint16]1); $taskWriter.Write([uint16]$taskImages.Count)
    $taskOffset = 6 + 16 * $taskImages.Count
    foreach ($taskImage in $taskImages) {
        $taskDimension = if ($taskImage[0] -eq 256) { 0 } else { $taskImage[0] }
        $taskWriter.Write([byte]$taskDimension); $taskWriter.Write([byte]$taskDimension)
        $taskWriter.Write([byte]0); $taskWriter.Write([byte]0)
        $taskWriter.Write([uint16]1); $taskWriter.Write([uint16]32)
        $taskWriter.Write([uint32]$taskImage[1].Length); $taskWriter.Write([uint32]$taskOffset)
        $taskOffset += $taskImage[1].Length
    }
    foreach ($taskImage in $taskImages) { $taskWriter.Write([byte[]]$taskImage[1]) }
}
finally { $taskWriter.Dispose() }
Write-Output $taskOutput
