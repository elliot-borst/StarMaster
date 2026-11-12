# Generates StarMaster.ico (multi-resolution: 16/24/32/48/64/128/256), GDI+ offline.
# v92 "H3" mark: a rounded-square tile with a pink-to-violet diagonal gradient and one dark concave four-point star.
# Same 64-unit geometry as MainWindow.LogoMark (which recolours it per theme at runtime; this file is the Orchid
# default used by the exe, installer and pinned shortcuts).
Add-Type -AssemblyName System.Drawing
$d = $PSScriptRoot
$icoPath = Join-Path $d 'StarMaster.ico'

function C([int]$a,[int]$r,[int]$g,[int]$b) { return [System.Drawing.Color]::FromArgb($a,$r,$g,$b) }
function New-RoundRect([single]$x,[single]$y,[single]$w,[single]$h,[single]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $dia = 2 * $r
    $p.AddArc($x, $y, $dia, $dia, 180, 90); $p.AddArc($x + $w - $dia, $y, $dia, $dia, 270, 90)
    $p.AddArc($x + $w - $dia, $y + $h - $dia, $dia, $dia, 0, 90); $p.AddArc($x, $y + $h - $dia, $dia, $dia, 90, 90)
    $p.CloseFigure(); return $p
}
# quadratic (p0, q, p2) as the cubic GDI+ wants
function Add-Quad($path, $x0, $y0, $qx, $qy, $x2, $y2) {
    $path.AddBezier([single]$x0, [single]$y0, [single]($x0 + 2/3*($qx-$x0)), [single]($y0 + 2/3*($qy-$y0)),
                    [single]($x2 + 2/3*($qx-$x2)), [single]($y2 + 2/3*($qy-$y2)), [single]$x2, [single]$y2)
}

function Draw-Icon([int]$s) {
    $bmp = New-Object System.Drawing.Bitmap($s, $s, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb))
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)
    $k = $s / 64.0
    function Q([double]$u) { return [single]($u * $k) }

    # tile: 60x60 at (2,2), radius 14, pink -> violet diagonal
    $tile = New-RoundRect (Q 2) (Q 2) (Q 60) (Q 60) (Q 14)
    $rect = New-Object System.Drawing.RectangleF((Q 2), (Q 2), (Q 60), (Q 60))
    $gb = New-Object System.Drawing.Drawing2D.LinearGradientBrush($rect, (C 255 255 159 216), (C 255 155 123 255), ([System.Drawing.Drawing2D.LinearGradientMode]::ForwardDiagonal))
    $g.FillPath($gb, $tile); $gb.Dispose(); $tile.Dispose()

    # dark concave star (R 25, waist 4.5)
    $sp = New-Object System.Drawing.Drawing2D.GraphicsPath
    Add-Quad $sp (Q 32) (Q 7) (Q 36.5) (Q 27.5) (Q 57) (Q 32)
    Add-Quad $sp (Q 57) (Q 32) (Q 36.5) (Q 36.5) (Q 32) (Q 57)
    Add-Quad $sp (Q 32) (Q 57) (Q 27.5) (Q 36.5) (Q 7) (Q 32)
    Add-Quad $sp (Q 7) (Q 32) (Q 27.5) (Q 27.5) (Q 32) (Q 7)
    $sp.CloseFigure()
    $sb = New-Object System.Drawing.SolidBrush((C 255 28 15 27)); $g.FillPath($sb, $sp); $sb.Dispose(); $sp.Dispose()

    $g.Dispose()
    return $bmp
}

$sizes = @(256,128,64,48,32,24,16)
$pngs = @()
foreach ($s in $sizes) {
    $bmp = Draw-Icon $s
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $pngs += ,($ms.ToArray())
    $ms.Dispose(); $bmp.Dispose()
}

$fs = New-Object System.IO.FileStream($icoPath, [System.IO.FileMode]::Create)
$bw = New-Object System.IO.BinaryWriter($fs)
$bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $len = $pngs[$i].Length
    $bw.Write([byte]($s % 256)); $bw.Write([byte]($s % 256))
    $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([UInt16]1); $bw.Write([UInt16]32)
    $bw.Write([UInt32]$len); $bw.Write([UInt32]$offset)
    $offset += $len
}
foreach ($p in $pngs) { $bw.Write($p) }
$bw.Flush(); $bw.Close(); $fs.Close()
Write-Output ("ICO written: " + $icoPath + " (" + (Get-Item $icoPath).Length + " bytes, sizes " + ($sizes -join '/') + ")")
