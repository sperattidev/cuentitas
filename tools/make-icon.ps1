# Genera el ícono de Cuentitas (Assets\cuentitas.ico y Assets\AppIcon.png).
# Diseño: cuadrado redondeado con degradé teal → azul petróleo y una moneda con "$".
# Uso: powershell -ExecutionPolicy Bypass -File tools\make-icon.ps1
Add-Type -AssemblyName System.Drawing

$assets = Join-Path $PSScriptRoot '..\src\Cuentitas.App\Assets'
New-Item -ItemType Directory -Force $assets | Out-Null

function New-IconBitmap([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.TextRenderingHint = 'AntiAliasGridFit'
    $g.Clear([System.Drawing.Color]::Transparent)

    $r = [single]($size * 0.22)
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $w = [single]($size - 1)
    $path.AddArc(0, 0, $r * 2, $r * 2, 180, 90)
    $path.AddArc($w - $r * 2, 0, $r * 2, $r * 2, 270, 90)
    $path.AddArc($w - $r * 2, $w - $r * 2, $r * 2, $r * 2, 0, 90)
    $path.AddArc(0, $w - $r * 2, $r * 2, $r * 2, 90, 90)
    $path.CloseFigure()

    $rect = New-Object System.Drawing.RectangleF 0, 0, $size, $size
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect,
        ([System.Drawing.Color]::FromArgb(255, 0x14, 0xB0, 0x9A)),
        ([System.Drawing.Color]::FromArgb(255, 0x0B, 0x5E, 0x82)), 45
    $g.FillPath($brush, $path)

    # Moneda
    $m = [single]($size * 0.2)
    $coin = New-Object System.Drawing.RectangleF $m, $m, ($size - 2 * $m), ($size - 2 * $m)
    $g.FillEllipse((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 255, 255, 255))), $coin)
    $penW = [single][Math]::Max(1, $size * 0.035)
    $inner = New-Object System.Drawing.RectangleF ($m + $size * 0.06), ($m + $size * 0.06), ($size - 2 * $m - $size * 0.12), ($size - 2 * $m - $size * 0.12)
    $g.DrawEllipse((New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(255, 0x7F, 0xD6, 0xC9)), $penW), $inner)

    $font = New-Object System.Drawing.Font 'Segoe UI', ([single]($size * 0.36)), ([System.Drawing.FontStyle]::Bold), ([System.Drawing.GraphicsUnit]::Pixel)
    $fmt = New-Object System.Drawing.StringFormat
    $fmt.Alignment = 'Center'; $fmt.LineAlignment = 'Center'
    $textRect = New-Object System.Drawing.RectangleF 0, ([single]($size * 0.01)), $size, $size
    $g.DrawString('$', $font, (New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 0x0B, 0x7A, 0x6C))), $textRect, $fmt)
    $g.Dispose()
    return $bmp
}

# PNG para la barra de título
$png = New-IconBitmap 64
$png.Save((Join-Path $assets 'AppIcon.png'), [System.Drawing.Imaging.ImageFormat]::Png)

# ICO multi-tamaño (entradas PNG, soportado desde Windows Vista)
$sizes = 16, 24, 32, 48, 64, 128, 256
$images = foreach ($s in $sizes) {
    $ms = New-Object System.IO.MemoryStream
    (New-IconBitmap $s).Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    , $ms.ToArray()
}
$out = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter $out
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]
    $bw.Write([byte]($(if ($s -ge 256) { 0 } else { $s })))
    $bw.Write([byte]($(if ($s -ge 256) { 0 } else { $s })))
    $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([uint16]1); $bw.Write([uint16]32)
    $bw.Write([uint32]$images[$i].Length); $bw.Write([uint32]$offset)
    $offset += $images[$i].Length
}
foreach ($img in $images) { $bw.Write($img) }
$bw.Flush()
[System.IO.File]::WriteAllBytes((Join-Path $assets 'cuentitas.ico'), $out.ToArray())
Write-Output "Iconos generados en $assets"
