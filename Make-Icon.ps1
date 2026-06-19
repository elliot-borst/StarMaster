# Generates StarMaster.ico (multi-resolution: 16/32/48/64/128/256) from the
# "Aurora sparkle-star on a dark tile" design (cyan->violet gradient), GDI+ offline.
Add-Type -AssemblyName System.Drawing
$d = $PSScriptRoot
$icoPath = Join-Path $d 'StarMaster.ico'

function New-RoundRect([single]$x,[single]$y,[single]$w,[single]$h,[single]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $dia = 2 * $r
    $p.AddArc($x, $y, $dia, $dia, 180, 90)
    $p.AddArc($x + $w - $dia, $y, $dia, $dia, 270, 90)
    $p.AddArc($x + $w - $dia, $y + $h - $dia, $dia, $dia, 0, 90)
    $p.AddArc($x, $y + $h - $dia, $dia, $dia, 90, 90)
    $p.CloseFigure()
    return $p
}

# 4-point sparkle: outer points on the axes (R), inner points on the diagonals (r)
function New-Star([single]$cx,[single]$cy,[single]$ro,[single]$ri) {
    $pts = New-Object 'System.Drawing.PointF[]' 8
    $pts[0] = New-Object System.Drawing.PointF($cx,         ($cy-$ro))
    $pts[1] = New-Object System.Drawing.PointF(($cx+$ri),   ($cy-$ri))
    $pts[2] = New-Object System.Drawing.PointF(($cx+$ro),   $cy)
    $pts[3] = New-Object System.Drawing.PointF(($cx+$ri),   ($cy+$ri))
    $pts[4] = New-Object System.Drawing.PointF($cx,         ($cy+$ro))
    $pts[5] = New-Object System.Drawing.PointF(($cx-$ri),   ($cy+$ri))
    $pts[6] = New-Object System.Drawing.PointF(($cx-$ro),   $cy)
    $pts[7] = New-Object System.Drawing.PointF(($cx-$ri),   ($cy-$ri))
    return $pts
}
function C([int]$a,[int]$r,[int]$g,[int]$b) { return [System.Drawing.Color]::FromArgb($a,$r,$g,$b) }

function Draw-Icon([int]$s) {
    $sc = $s / 256.0
    $bmp = New-Object System.Drawing.Bitmap($s, $s, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb))
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)

    # dark Aurora tile
    $m = 12 * $sc; $rw = 232 * $sc; $rr = 54 * $sc
    $path = New-RoundRect $m $m $rw $rw $rr
    $rectF = New-Object System.Drawing.RectangleF($m, $m, $rw, $rw)
    $bg = New-Object System.Drawing.Drawing2D.LinearGradientBrush($rectF, (C 255 22 26 48), (C 255 11 14 22), ([System.Drawing.Drawing2D.LinearGradientMode]::ForwardDiagonal))
    $g.FillPath($bg, $path)
    $bpen = New-Object System.Drawing.Pen((C 255 50 60 94), ([single][Math]::Max(1.0, 3 * $sc)))
    $g.DrawPath($bpen, $path)
    if ($s -ge 32) {
        $ip = New-RoundRect (30*$sc) (30*$sc) (196*$sc) (196*$sc) (38*$sc)
        $ipen = New-Object System.Drawing.Pen((C 255 44 58 102), ([single][Math]::Max(1.0, 2 * $sc)))
        $g.DrawPath($ipen, $ip); $ip.Dispose(); $ipen.Dispose()
    }

    $cx = 128 * $sc; $cy = 132 * $sc; $ro = 82 * $sc; $ri = 27 * $sc

    # cyan glow behind the star
    $glow = New-Star $cx $cy ($ro * 1.3) ($ri * 1.55)
    $gb = New-Object System.Drawing.SolidBrush((C 70 34 211 238))
    $g.FillPolygon($gb, $glow)

    # main sparkle, cyan -> violet diagonal gradient
    $star = New-Star $cx $cy $ro $ri
    $sbRect = New-Object System.Drawing.RectangleF(([single]($cx-$ro)), ([single]($cy-$ro)), ([single](2*$ro)), ([single](2*$ro)))
    $sbrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush($sbRect, (C 255 34 211 238), (C 255 168 85 247), ([System.Drawing.Drawing2D.LinearGradientMode]::ForwardDiagonal))
    $g.FillPolygon($sbrush, $star)

    # small accent sparkle, top-right
    if ($s -ge 32) {
        $mini = New-Star (190*$sc) (74*$sc) (22*$sc) (6*$sc)
        $mb = New-Object System.Drawing.SolidBrush((C 235 201 225 255))
        $g.FillPolygon($mb, $mini); $mb.Dispose()
    }

    $g.Dispose(); $bg.Dispose(); $bpen.Dispose(); $gb.Dispose(); $sbrush.Dispose(); $path.Dispose()
    return $bmp
}

$sizes = @(256,128,64,48,32,16)
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
