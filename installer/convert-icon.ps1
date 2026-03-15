# ============================================================
#  convert-icon.ps1 — Convertit janot.png → janot.ico
#  Génère un .ico multi-résolution : 256, 64, 48, 32, 16 px
# ============================================================
param(
    [string]$Source      = "$PSScriptRoot\janot.png",
    [string]$Destination = "$PSScriptRoot\janot.ico"
)

if (-not (Test-Path $Source)) {
    Write-Error "Image source introuvable : $Source"
    Write-Host "  → Place l'image dans : $Source" -ForegroundColor Yellow
    exit 1
}

Add-Type -AssemblyName System.Drawing

$sizes  = @(256, 64, 48, 32, 16)
$pngs   = @()
$srcImg = [System.Drawing.Image]::FromFile((Resolve-Path $Source).Path)

foreach ($size in $sizes) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size)
    $g   = [System.Drawing.Graphics]::FromImage($bmp)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.SmoothingMode     = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $g.DrawImage($srcImg, 0, 0, $size, $size)
    $g.Dispose()

    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $pngs += $ms.ToArray()
    $ms.Dispose()
    $bmp.Dispose()
}
$srcImg.Dispose()

# ── Écriture du fichier ICO ───────────────────────────────────────────────────
$fs = [System.IO.File]::OpenWrite($Destination)
$bw = New-Object System.IO.BinaryWriter($fs)

$count  = $sizes.Count
$offset = 6 + $count * 16          # header + répertoire

# ICONDIR
$bw.Write([uint16]0)       # réservé
$bw.Write([uint16]1)       # type ICO
$bw.Write([uint16]$count)  # nb images

# ICONDIRENTRY × count
for ($i = 0; $i -lt $count; $i++) {
    $sz = $sizes[$i]
    $bw.Write([byte]$(if ($sz -ge 256) { 0 } else { $sz }))   # largeur (0 = 256)
    $bw.Write([byte]$(if ($sz -ge 256) { 0 } else { $sz }))   # hauteur
    $bw.Write([byte]0)        # nb couleurs (0 = pas de palette)
    $bw.Write([byte]0)        # réservé
    $bw.Write([uint16]1)      # plans couleur
    $bw.Write([uint16]32)     # bits par pixel
    $bw.Write([uint32]$pngs[$i].Length)
    $bw.Write([uint32]$offset)
    $offset += $pngs[$i].Length
}

# Données images (PNG natif dans ICO — compatible Vista+)
foreach ($png in $pngs) { $bw.Write($png) }

$bw.Close()
$fs.Close()

Write-Host "✓ Icône créée : $Destination ($($sizes -join ', ') px)" -ForegroundColor Green
