# Generates SC-KeepAlive.ico (multi-resolution: 16/32/48/64/128/256) from the
# "HUD heartbeat pulse on a dark panel" design, using GDI+ (offline, no dependencies).
Add-Type -AssemblyName System.Drawing
$d = $PSScriptRoot
$icoPath = Join-Path $d 'SC-KeepAlive.ico'

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

$basePts = @(@(40,146),@(98,146),@(116,146),@(130,146),@(146,84),@(160,202),@(176,116),@(190,146),@(216,146))

function Draw-Icon([int]$s) {
    $sc = $s / 256.0
    $bmp = New-Object System.Drawing.Bitmap($s, $s, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb))
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)

    $m = 12 * $sc; $rw = 232 * $sc; $rr = 54 * $sc
    $path = New-RoundRect $m $m $rw $rw $rr
    $rectF = New-Object System.Drawing.RectangleF($m, $m, $rw, $rw)
    $top = [System.Drawing.Color]::FromArgb(255,36,53,81)
    $bot = [System.Drawing.Color]::FromArgb(255,10,14,22)
    $bg = New-Object System.Drawing.Drawing2D.LinearGradientBrush($rectF, $top, $bot, ([System.Drawing.Drawing2D.LinearGradientMode]::Vertical))
    $g.FillPath($bg, $path)
    $bpen = New-Object System.Drawing.Pen(([System.Drawing.Color]::FromArgb(255,67,89,127)), ([single][Math]::Max(1.0, 3 * $sc)))
    $g.DrawPath($bpen, $path)

    if ($s -ge 32) {
        $ip = New-RoundRect (30*$sc) (30*$sc) (196*$sc) (196*$sc) (38*$sc)
        $ipen = New-Object System.Drawing.Pen(([System.Drawing.Color]::FromArgb(255,44,62,94)), ([single][Math]::Max(1.0, 2 * $sc)))
        $g.DrawPath($ipen, $ip); $ip.Dispose(); $ipen.Dispose()
    }

    $pts = New-Object 'System.Drawing.PointF[]' ($basePts.Count)
    for ($i = 0; $i -lt $basePts.Count; $i++) { $pts[$i] = New-Object System.Drawing.PointF(([single]($basePts[$i][0]*$sc)), ([single]($basePts[$i][1]*$sc))) }

    $glow = New-Object System.Drawing.Pen(([System.Drawing.Color]::FromArgb(72,245,166,35)), ([single][Math]::Max(2.0, 22 * $sc)))
    $glow.StartCap = [System.Drawing.Drawing2D.LineCap]::Round; $glow.EndCap = [System.Drawing.Drawing2D.LineCap]::Round; $glow.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    $g.DrawLines($glow, $pts)

    $prect = New-Object System.Drawing.RectangleF(([single](40*$sc)), 0, ([single](176*$sc)), ([single]$s))
    $a1 = [System.Drawing.Color]::FromArgb(255,255,192,99)
    $a2 = [System.Drawing.Color]::FromArgb(255,245,144,30)
    $pb = New-Object System.Drawing.Drawing2D.LinearGradientBrush($prect, $a1, $a2, ([System.Drawing.Drawing2D.LinearGradientMode]::Horizontal))
    $mp = New-Object System.Drawing.Pen($pb, ([single][Math]::Max(1.5, 11 * $sc)))
    $mp.StartCap = [System.Drawing.Drawing2D.LineCap]::Round; $mp.EndCap = [System.Drawing.Drawing2D.LineCap]::Round; $mp.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    $g.DrawLines($mp, $pts)

    $dr = [single][Math]::Max(1.5, 8 * $sc)
    $db = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255,255,213,145))
    $g.FillEllipse($db, ([single](216*$sc - $dr)), ([single](146*$sc - $dr)), ([single](2*$dr)), ([single](2*$dr)))

    $g.Dispose(); $bg.Dispose(); $bpen.Dispose(); $glow.Dispose(); $pb.Dispose(); $mp.Dispose(); $db.Dispose(); $path.Dispose()
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
