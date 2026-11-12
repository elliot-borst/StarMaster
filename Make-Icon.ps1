# Generates StarMaster.ico (multi-resolution: 16/24/32/48/64/128/256), GDI+ offline.
# v90 "Star lock" mark on a plum tile, Orchid palette: mauve targeting brackets, a bone ring and an orchid
# concave four-point star. Same 64-unit geometry as MainWindow.LogoMark so the window, tray and taskbar match.
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

function Draw-Small([int]$s) {
    # v91: 16-32 px (Explorer small icons, the tray before the app redraws it) - no tile, no brackets: a bold ring
    # and a star reaching the edge, the same TrayMark the app draws at runtime. Detail at this size is just noise.
    $bmp = New-Object System.Drawing.Bitmap($s, $s, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb))
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)
    $k = $s / 64.0
    function Q([double]$u) { return [single]($u * $k) }
    $rf = New-Object System.Drawing.SolidBrush((C 255 25 20 28)); $g.FillEllipse($rf, (Q 7), (Q 7), (Q 50), (Q 50)); $rf.Dispose()
    $rp = New-Object System.Drawing.Pen((C 255 239 230 240), [single][Math]::Max(1.0, 6 * $k)); $g.DrawEllipse($rp, (Q 7), (Q 7), (Q 50), (Q 50)); $rp.Dispose()
    $sp = New-Object System.Drawing.Drawing2D.GraphicsPath
    Add-Quad $sp (Q 32) (Q 2) (Q 36.6) (Q 27.4) (Q 62) (Q 32)
    Add-Quad $sp (Q 62) (Q 32) (Q 36.6) (Q 36.6) (Q 32) (Q 62)
    Add-Quad $sp (Q 32) (Q 62) (Q 27.4) (Q 36.6) (Q 2) (Q 32)
    Add-Quad $sp (Q 2) (Q 32) (Q 27.4) (Q 27.4) (Q 32) (Q 2)
    $sp.CloseFigure()
    $sb = New-Object System.Drawing.SolidBrush((C 255 228 122 208)); $g.FillPath($sb, $sp); $sb.Dispose(); $sp.Dispose()
    $g.Dispose(); return $bmp
}

function Draw-Icon([int]$s) {
    if ($s -le 32) { return Draw-Small $s }
    $bmp = New-Object System.Drawing.Bitmap($s, $s, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb))
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)

    # plum tile (rounded square), hairline edge on the larger sizes
    $m = [single]($s * 0.03); $w = [single]($s - 2 * $m); $rad = [single]($s * 0.2)
    $tile = New-RoundRect $m $m $w $w $rad
    $tb = New-Object System.Drawing.SolidBrush((C 255 25 20 28)); $g.FillPath($tb, $tile); $tb.Dispose()
    if ($s -ge 32) { $tp = New-Object System.Drawing.Pen((C 255 61 50 69), [single][Math]::Max(1.0, $s / 128.0)); $g.DrawPath($tp, $tile); $tp.Dispose() }

    # map the 64-unit mark into the tile (a touch more margin on big sizes, edge-to-edge on 16 so it stays legible)
    $fill = 0.9
    $k = $s * $fill / 64.0; $o = ($s - 64 * $k) / 2.0
    function P([double]$u) { return [single]($o + $u * $k) }

    # brackets
    $bp = New-Object System.Drawing.Pen((C 255 139 133 146), [single][Math]::Max(1.2, 4.5 * $k))
    $bp.StartCap = [System.Drawing.Drawing2D.LineCap]::Round; $bp.EndCap = [System.Drawing.Drawing2D.LineCap]::Round; $bp.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    foreach ($seg in @(@(8,20, 8,8, 20,8), @(44,8, 56,8, 56,20), @(56,44, 56,56, 44,56), @(20,56, 8,56, 8,44))) {
        $pts = [System.Drawing.PointF[]]@((New-Object System.Drawing.PointF((P $seg[0]),(P $seg[1]))), (New-Object System.Drawing.PointF((P $seg[2]),(P $seg[3]))), (New-Object System.Drawing.PointF((P $seg[4]),(P $seg[5]))))
        $g.DrawLines($bp, $pts)
    }
    $bp.Dispose()

    # ring (filled with the panel colour so the star sits on a disc)
    $rr = 15.5; $rx = P (32 - $rr); $rw = [single](2 * $rr * $k)
    $rf = New-Object System.Drawing.SolidBrush((C 255 25 20 28)); $g.FillEllipse($rf, $rx, $rx, $rw, $rw); $rf.Dispose()
    $rp = New-Object System.Drawing.Pen((C 255 239 230 240), [single][Math]::Max(1.0, 3 * $k)); $g.DrawEllipse($rp, $rx, $rx, $rw, $rw); $rp.Dispose()

    # concave four-point star (orchid)
    $sp = New-Object System.Drawing.Drawing2D.GraphicsPath
    Add-Quad $sp (P 32) (P 10) (P 35.8) (P 28.2) (P 54) (P 32)
    Add-Quad $sp (P 54) (P 32) (P 35.8) (P 35.8) (P 32) (P 54)
    Add-Quad $sp (P 32) (P 54) (P 28.2) (P 35.8) (P 10) (P 32)
    Add-Quad $sp (P 10) (P 32) (P 28.2) (P 28.2) (P 32) (P 10)
    $sp.CloseFigure()
    $sb = New-Object System.Drawing.SolidBrush((C 255 228 122 208)); $g.FillPath($sb, $sp); $sb.Dispose(); $sp.Dispose()

    $g.Dispose(); $tile.Dispose()
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
