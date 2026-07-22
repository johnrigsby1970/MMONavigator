Add-Type -AssemblyName System.Drawing

# Ensure script operates in the directory where it's saved/executed
$currentDir = Get-Location
$sourcePath = Join-Path $currentDir "source.png"

if (-not (Test-Path $sourcePath)) {
    Write-Error "Could not find source.png in $currentDir. Please ensure source.png is in this folder."
    return
}

function Save-ResizedImage ($srcImage, $outputPath, $width, $height) {
    $bmp = New-Object System.Drawing.Bitmap($width, $height)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    
    # High quality settings
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality

    $g.DrawImage($srcImage, 0, 0, $width, $height)
    
    $g.Dispose()
    
    # Save and clean up
    if (Test-Path $outputPath) { Remove-Item $outputPath -Force }
    $bmp.Save($outputPath, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
}

# Load source image into memory stream to prevent file locks
$bytes = [System.IO.File]::ReadAllBytes($sourcePath)
$ms = New-Object System.IO.MemoryStream(,$bytes)
$src = [System.Drawing.Image]::FromStream($ms)

try {
    # Generate Square Assets
    Save-ResizedImage $src (Join-Path $currentDir "Square44x44Logo.png") 44 44
    Save-ResizedImage $src (Join-Path $currentDir "Square150x150Logo.png") 150 150
    Save-ResizedImage $src (Join-Path $currentDir "StoreLogo.png") 50 50

    # Generate Wide Asset
    $wideBmp = New-Object System.Drawing.Bitmap(310, 150)
    $gWide = [System.Drawing.Graphics]::FromImage($wideBmp)
    $gWide.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $gWide.DrawImage($src, 95, 15, 120, 120)
    $gWide.Dispose()

    $widePath = Join-Path $currentDir "Wide310x150Logo.png"
    if (Test-Path $widePath) { Remove-Item $widePath -Force }
    $wideBmp.Save($widePath, [System.Drawing.Imaging.ImageFormat]::Png)
    $wideBmp.Dispose()

    Write-Host "Success! All Store assets generated in $currentDir." -ForegroundColor Green
}
finally {
    $src.Dispose()
    $ms.Dispose()
}