# Generates StarMaster.ico (multi-resolution: 16/32/48/64/128/256) from the
# "Command insignia" design (v77): a hex squadron badge with an Aurora (cyan->violet)
# gradient outline, a light sparkle star and two rank chevrons. No square tile - the hex
# is its own silhouette, corners stay transparent. GDI+, offline. Coordinates are on a
# 256 grid (same as the in-app header logo in MainWindow.Header). Run on Windows.
Add-Type -AssemblyName System.Drawing
$d = $PSScriptRoot
$icoPath = Join-Path $d 'StarMaster.ico'

function C([int]$a,[int]$r,[int]$g,[int]$b) { return [System.Drawing.Color]::FromArgb($a,$r,$g,$b) }
function P([double]$x,[double]$y,[double]$sc) { return New-Object System.Drawing.PointF(([single]($x*$sc)), ([single]($y*$sc))) }

# Curved 4-point sparkle (quadratic sides, converted to cubic beziers for GDI+)
function New-Sparkle([double]$cx,[double]$cy,[double]$ro,[double]$k,[double]$sc) {
    $tips = @(@($cx,($cy-$ro)), @(($cx+$ro),$cy), @($cx,($cy+$ro)), @(($cx-$ro),$cy))
    $ctrl = @(@(($cx+$k),($cy-$k)), @(($cx+$k),($cy+$k)), @(($cx-$k),($cy+$k)), @(($cx-$k),($cy-$k)))
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    for ($i = 0; $i -lt 4; $i++) {
        $a = $tips[$i]; $b = $tips[($i+1)%4]; $q = $ctrl[$i]
        $c1x = $a[0] + 2.0/3.0*($q[0]-$a[0]); $c1y = $a[1] + 2.0/3.0*($q[1]-$a[1])
        $c2x = $b[0] + 2.0/3.0*($q[0]-$b[0]); $c2y = $b[1] + 2.0/3.0*($q[1]-$b[1])
        $p.AddBezier((P $a[0] $a[1] $sc), (P $c1x $c1y $sc), (P $c2x $c2y $sc), (P $b[0] $b[1] $sc))
    }
    $p.CloseFigure()
    return $p
}

function Draw-Icon([int]$s) {
    $sc = $s / 256.0
    # small sizes get proportionally thicker strokes so the badge doesn't dissolve in the tray
    $boost = 1.0; if ($s -le 16) { $boost = 1.45 } elseif ($s -le 32) { $boost = 1.2 }
    $bmp = New-Object System.Drawing.Bitmap($s, $s, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb))
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)

    $aur = New-Object System.Drawing.Drawing2D.LinearGradientBrush((P 40 40 $sc), (P 216 216 $sc), (C 255 34 211 238), (C 255 168 85 247))

    # hex badge (pointy top): dark fill + gradient outline
    $hex = [System.Drawing.PointF[]]@((P 128 20 $sc), (P 222 74 $sc), (P 222 182 $sc), (P 128 236 $sc), (P 34 182 $sc), (P 34 74 $sc))
    $fill = New-Object System.Drawing.SolidBrush((C 255 13 17 32))
    $g.FillPolygon($fill, $hex)
    $hpen = New-Object System.Drawing.Pen($aur, ([single][Math]::Max(1.0, 12 * $sc * $boost)))
    $hpen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    $g.DrawPolygon($hpen, $hex)

    # sparkle star (light) above the chevrons
    $star = New-Sparkle 128 92 33 4 $sc
    $sb = New-Object System.Drawing.SolidBrush((C 255 224 236 255))
    $g.FillPath($sb, $star)

    # two rank chevrons
    $cpen = New-Object System.Drawing.Pen($aur, ([single][Math]::Max(1.0, 18 * $sc * $boost)))
    $cpen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $cpen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $cpen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    $g.DrawLines($cpen, [System.Drawing.PointF[]]@((P 84 144 $sc), (P 128 170 $sc), (P 172 144 $sc)))
    $g.DrawLines($cpen, [System.Drawing.PointF[]]@((P 84 178 $sc), (P 128 204 $sc), (P 172 178 $sc)))

    $g.Dispose(); $aur.Dispose(); $fill.Dispose(); $hpen.Dispose(); $sb.Dispose(); $star.Dispose(); $cpen.Dispose()
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
