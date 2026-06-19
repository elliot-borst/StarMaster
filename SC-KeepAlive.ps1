# SC-KeepAlive.ps1
# Tiny scheduled-keystroke "keep-alive" utility with a small GUI. No install needed.
# Add commands (key + interval), press Start, leave it running to avoid the idle logout.
# SAFETY: by default it only sends keystrokes while Star Citizen is the ACTIVE window.

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

Add-Type @"
using System;
using System.Runtime.InteropServices;
using System.Text;
public static class KA {
    [DllImport("user32.dll")] private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);
    private const uint UP = 0x2;
    public static string Active() {
        StringBuilder sb = new StringBuilder(256);
        GetWindowText(GetForegroundWindow(), sb, 256);
        return sb.ToString();
    }
    public static void Press(byte[] mods, byte key) {
        foreach (byte m in mods) keybd_event(m, 0, 0, UIntPtr.Zero);
        keybd_event(key, 0, 0, UIntPtr.Zero);
        System.Threading.Thread.Sleep(40);
        keybd_event(key, 0, UP, UIntPtr.Zero);
        for (int i = mods.Length - 1; i >= 0; i--) keybd_event(mods[i], 0, UP, UIntPtr.Zero);
    }
}
"@

$ConfigPath = Join-Path $PSScriptRoot 'config.json'

function Get-Vk([string]$k) {
    if ([string]::IsNullOrWhiteSpace($k)) { return 0 }
    $k = $k.Trim().ToUpper()
    if ($k.Length -eq 1) {
        $c = [int][char]$k
        if (($c -ge 48 -and $c -le 57) -or ($c -ge 65 -and $c -le 90)) { return $c }
    }
    switch ($k) {
        'SPACE' { return 0x20 }
        'ENTER' { return 0x0D }
        'TAB'   { return 0x09 }
        'ESC'   { return 0x1B }
        'F1'  { return 0x70 }
        'F2'  { return 0x71 }
        'F3'  { return 0x72 }
        'F4'  { return 0x73 }
        'F5'  { return 0x74 }
        'F6'  { return 0x75 }
        'F7'  { return 0x76 }
        'F8'  { return 0x77 }
        'F9'  { return 0x78 }
        'F10' { return 0x79 }
        'F11' { return 0x7A }
        'F12' { return 0x7B }
        default { return 0 }
    }
}

function Combo($c) {
    $m = @()
    if ($c.Shift) { $m += 'Shift' }
    if ($c.Ctrl)  { $m += 'Ctrl' }
    if ($c.Alt)   { $m += 'Alt' }
    return (($m + $c.Key) -join '+')
}

# ---- load config ----
$script:commands  = @()
$script:autostart = $false
$script:wintitle  = 'Star Citizen'
$script:focusguard = $true
if (Test-Path $ConfigPath) {
    try {
        $cfg = Get-Content $ConfigPath -Raw | ConvertFrom-Json
        $script:commands = @($cfg.commands)
        if ($null -ne $cfg.autostart)  { $script:autostart  = [bool]$cfg.autostart }
        if (-not [string]::IsNullOrWhiteSpace($cfg.wintitle)) { $script:wintitle = [string]$cfg.wintitle }
        if ($null -ne $cfg.focusguard) { $script:focusguard = [bool]$cfg.focusguard }
    } catch { }
}
if (-not $script:commands -or $script:commands.Count -eq 0) {
    $script:commands = @([pscustomobject]@{ Label='Wipe Visor'; Shift=$false; Ctrl=$false; Alt=$true; Key='X'; Interval=120; Enabled=$true })
}
# sanitize any persisted Interval into the 5..3600 range (heals a hand-edited config so row-select can't crash)
foreach ($c in $script:commands) {
    $iv = 0; [void][int]::TryParse("$($c.Interval)", [ref]$iv)
    if ($iv -lt 5) { $iv = 5 } elseif ($iv -gt 3600) { $iv = 3600 }
    $c | Add-Member -NotePropertyName Interval -NotePropertyValue $iv -Force
}

# ---- UI ----
$form = New-Object System.Windows.Forms.Form
$form.Text = 'SC Keep-Alive'
$form.Size = New-Object System.Drawing.Size(520,560)
$form.StartPosition = 'CenterScreen'
$form.FormBorderStyle = 'FixedDialog'
$form.MaximizeBox = $false
try { $form.Icon = New-Object System.Drawing.Icon((Join-Path $PSScriptRoot 'SC-KeepAlive.ico')) } catch { }

function NewLabel($t,$x,$y,$w=70) { $l=New-Object System.Windows.Forms.Label; $l.Text=$t; $l.Location=New-Object System.Drawing.Point($x,$y); $l.Size=New-Object System.Drawing.Size($w,20); $form.Controls.Add($l); return $l }

[void](NewLabel 'Label:' 10 15 45)
$txtLabel = New-Object System.Windows.Forms.TextBox; $txtLabel.Location='58,12'; $txtLabel.Size='120,22'; $form.Controls.Add($txtLabel)
$chkShift = New-Object System.Windows.Forms.CheckBox; $chkShift.Text='Shift'; $chkShift.Location='186,13'; $chkShift.Size='55,20'; $form.Controls.Add($chkShift)
$chkCtrl  = New-Object System.Windows.Forms.CheckBox; $chkCtrl.Text='Ctrl';  $chkCtrl.Location='243,13'; $chkCtrl.Size='48,20'; $form.Controls.Add($chkCtrl)
$chkAlt   = New-Object System.Windows.Forms.CheckBox; $chkAlt.Text='Alt';    $chkAlt.Location='293,13'; $chkAlt.Size='44,20'; $form.Controls.Add($chkAlt)
[void](NewLabel 'Key:' 340 15 32)
$txtKey = New-Object System.Windows.Forms.TextBox; $txtKey.Location='372,12'; $txtKey.Size='40,22'; $form.Controls.Add($txtKey)

[void](NewLabel 'Every (sec):' 10 47 75)
$numInt = New-Object System.Windows.Forms.NumericUpDown; $numInt.Location='88,44'; $numInt.Size='65,22'; $numInt.Minimum=5; $numInt.Maximum=3600; $numInt.Value=120; $form.Controls.Add($numInt)
$btnAdd = New-Object System.Windows.Forms.Button; $btnAdd.Text='Add / Update'; $btnAdd.Location='170,42'; $btnAdd.Size='110,26'; $form.Controls.Add($btnAdd)
$btnRemove = New-Object System.Windows.Forms.Button; $btnRemove.Text='Remove'; $btnRemove.Location='290,42'; $btnRemove.Size='90,26'; $form.Controls.Add($btnRemove)

$lst = New-Object System.Windows.Forms.ListBox; $lst.Location='10,80'; $lst.Size='486,150'; $form.Controls.Add($lst)
[void](NewLabel 'Tip: double-click a row to toggle it ON/off. Click a row then "Add / Update" to overwrite it.' 10 232 480)

$chkFocus = New-Object System.Windows.Forms.CheckBox; $chkFocus.Text='Only send while active window contains:'; $chkFocus.Location='10,260'; $chkFocus.Size='250,20'; $chkFocus.Checked=$script:focusguard; $form.Controls.Add($chkFocus)
$txtTitle = New-Object System.Windows.Forms.TextBox; $txtTitle.Location='262,258'; $txtTitle.Size='150,22'; $txtTitle.Text=$script:wintitle; $form.Controls.Add($txtTitle)
$chkAuto = New-Object System.Windows.Forms.CheckBox; $chkAuto.Text='Auto-start when launched (use this for run-on-boot)'; $chkAuto.Location='10,286'; $chkAuto.Size='400,20'; $chkAuto.Checked=$script:autostart; $form.Controls.Add($chkAuto)

$btnStart = New-Object System.Windows.Forms.Button; $btnStart.Text='Start'; $btnStart.Location='10,315'; $btnStart.Size='110,34'; $form.Controls.Add($btnStart)
$lblStatus = New-Object System.Windows.Forms.Label; $lblStatus.Text='Stopped'; $lblStatus.Location='130,323'; $lblStatus.Size='200,20'; $lblStatus.ForeColor=[System.Drawing.Color]::Firebrick; $form.Controls.Add($lblStatus)

[void](NewLabel 'Log:' 10 360 40)
$txtLog = New-Object System.Windows.Forms.TextBox; $txtLog.Location='10,380'; $txtLog.Size='486,130'; $txtLog.Multiline=$true; $txtLog.ReadOnly=$true; $txtLog.ScrollBars='Vertical'; $form.Controls.Add($txtLog)

$timer = New-Object System.Windows.Forms.Timer
$timer.Interval = 1000

function Log($m) { $ts=(Get-Date).ToString('HH:mm:ss'); $txtLog.AppendText("[$ts] $m`r`n") }

function Refresh {
    $lst.Items.Clear()
    foreach ($c in $script:commands) {
        $en = if ($c.Enabled) { 'ON ' } else { 'off' }
        [void]$lst.Items.Add("[$en] $($c.Label)  -  $(Combo $c)  every $($c.Interval)s")
    }
}

$btnAdd.Add_Click({
    $key = $txtKey.Text.Trim()
    if (-not $key) { Log '! enter a Key first'; return }
    if ((Get-Vk $key) -eq 0) { Log "! '$key' is not a recognized key (use A-Z, 0-9, F1-F12, Space, Enter, Tab, Esc)"; return }
    $label = if ($txtLabel.Text.Trim()) { $txtLabel.Text.Trim() } else { 'Command' }
    $obj = [pscustomobject]@{ Label=$label; Shift=$chkShift.Checked; Ctrl=$chkCtrl.Checked; Alt=$chkAlt.Checked; Key=$key; Interval=[int]$numInt.Value; Enabled=$true }
    $i = $lst.SelectedIndex
    if ($i -ge 0) { $script:commands[$i] = $obj } else { $script:commands += $obj }
    Refresh
})

$btnRemove.Add_Click({
    $i = $lst.SelectedIndex
    if ($i -ge 0) { $t=[System.Collections.ArrayList]@($script:commands); $t.RemoveAt($i); $script:commands=@($t); Refresh }
})

$lst.Add_DoubleClick({
    $i = $lst.SelectedIndex
    if ($i -ge 0) { $script:commands[$i].Enabled = -not $script:commands[$i].Enabled; Refresh }
})

$lst.Add_SelectedIndexChanged({
    $i = $lst.SelectedIndex
    if ($i -ge 0) { $c=$script:commands[$i]; $txtLabel.Text=$c.Label; $chkShift.Checked=$c.Shift; $chkCtrl.Checked=$c.Ctrl; $chkAlt.Checked=$c.Alt; $txtKey.Text=$c.Key; $iv=0; [void][int]::TryParse("$($c.Interval)",[ref]$iv); if($iv -lt $numInt.Minimum){$iv=[int]$numInt.Minimum}; if($iv -gt $numInt.Maximum){$iv=[int]$numInt.Maximum}; $numInt.Value=$iv }
})

$timer.Add_Tick({
    foreach ($c in $script:commands) {
        if (-not $c.Enabled) { continue }
        if (-not $c.PSObject.Properties['LastFire']) { $c | Add-Member -NotePropertyName LastFire -NotePropertyValue (Get-Date) -Force }
        if (((Get-Date) - $c.LastFire).TotalSeconds -lt $c.Interval) { continue }
        if ($chkFocus.Checked) {
            $t = $txtTitle.Text.Trim().ToLower()
            if (-not $t -or -not ([KA]::Active().ToLower().Contains($t))) { continue }
        }
        $c.LastFire = Get-Date
        $mods = New-Object System.Collections.Generic.List[byte]
        if ($c.Shift) { $mods.Add([byte]0xA0) }
        if ($c.Ctrl)  { $mods.Add([byte]0xA2) }
        if ($c.Alt)   { $mods.Add([byte]0xA4) }
        $vk = Get-Vk $c.Key
        if ($vk -eq 0) { Log "! bad key for $($c.Label)"; continue }
        [KA]::Press($mods.ToArray(), [byte]$vk)
        Log "sent $($c.Label) ($(Combo $c))"
    }
})

$btnStart.Add_Click({
    if ($timer.Enabled) {
        $timer.Stop(); $btnStart.Text='Start'; $lblStatus.Text='Stopped'; $lblStatus.ForeColor=[System.Drawing.Color]::Firebrick; Log 'stopped'
    } else {
        foreach ($c in $script:commands) { $c | Add-Member -NotePropertyName LastFire -NotePropertyValue (Get-Date) -Force }
        $timer.Start(); $btnStart.Text='Stop'; $lblStatus.Text='Running'; $lblStatus.ForeColor=[System.Drawing.Color]::ForestGreen; Log 'started'
    }
})

function Save-Config {
    $out = [pscustomobject]@{
        commands  = @($script:commands | Select-Object Label,Shift,Ctrl,Alt,Key,Interval,Enabled)
        autostart = $chkAuto.Checked
        wintitle  = $txtTitle.Text
        focusguard = $chkFocus.Checked
    }
    $out | ConvertTo-Json -Depth 4 | Set-Content -Path $ConfigPath -Encoding UTF8
}

$form.Add_FormClosing({ $timer.Stop(); Save-Config })

Refresh
if ($script:autostart) { $btnStart.PerformClick() }
[void]$form.ShowDialog()
