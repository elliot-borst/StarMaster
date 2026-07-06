# Builds StarMaster.exe (csc, no NuGet/MSBuild) + dist\StarMaster-Setup.exe (Inno Setup 6),
# gated on the WholeVersion tests. Single source of truth for the version is the
# MainWindow.Version const in StarMaster.cs - this script reads it, checks the assembly
# attributes agree, and passes it to ISCC via /DMyAppVersion.
#
# Release:  gh release create vN dist\StarMaster.exe dist\StarMaster-Setup.exe --title "StarMaster vN" --notes "..."
# The setup asset MUST keep "Setup" in its filename - the in-app updater matches on it.
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot   # csc/ISCC args below are repo-relative

# ---- version (single source of truth: StarMaster.cs) ----
$src = Get-Content (Join-Path $PSScriptRoot 'StarMaster.cs') -Raw
$version = [regex]::Match($src, 'public const string Version = "(\d+)"').Groups[1].Value
if (-not $version) { throw 'Could not read MainWindow.Version from StarMaster.cs' }
foreach ($attr in 'AssemblyFileVersion', 'AssemblyVersion') {
    if ($src -notmatch [regex]::Escape("$attr(`"$version.0.0.0`")")) {
        throw "[assembly: $attr] does not match Version $version - bump it in StarMaster.cs"
    }
}
Write-Host "Building StarMaster v$version..."

# ---- close the repo copy so csc can overwrite it (leave any installed copy running) ----
Get-Process StarMaster -ErrorAction SilentlyContinue |
    Where-Object { $_.Path -like "$PSScriptRoot\*" } |
    ForEach-Object { $_.Kill(); $_.WaitForExit() }

# ---- compile (code-only WPF on the framework csc; no PDBs are emitted) ----
$fw = "$env:windir\Microsoft.NET\Framework64\v4.0.30319"
$csc = "$fw\csc.exe"
& $csc /nologo /target:winexe /win32icon:StarMaster.ico /win32manifest:app.manifest /out:StarMaster.exe `
    /reference:"$fw\WPF\PresentationFramework.dll" /reference:"$fw\WPF\PresentationCore.dll" /reference:"$fw\WPF\WindowsBase.dll" `
    /reference:"$fw\System.Xaml.dll" /reference:System.Core.dll /reference:System.Windows.Forms.dll /reference:System.Drawing.dll `
    /reference:System.IO.Compression.dll /reference:System.IO.Compression.FileSystem.dll `
    StarMaster.cs BackupForm.cs WholeVersion.cs
if ($LASTEXITCODE -ne 0) { throw 'csc failed' }

# ---- tests (dependency-free runner; a failure stops the release build) ----
New-Item -ItemType Directory -Force (Join-Path $PSScriptRoot 'dist') | Out-Null
& $csc /nologo /target:exe /out:dist\StarMaster.Tests.exe WholeVersion.cs Tests.cs
if ($LASTEXITCODE -ne 0) { throw 'test build failed' }
& (Join-Path $PSScriptRoot 'dist\StarMaster.Tests.exe')
if ($LASTEXITCODE -ne 0) { throw 'tests failed' }

# ---- installer ----
$iscc = @(
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw 'Inno Setup 6 (ISCC.exe) not found - install from https://jrsoftware.org/isdl.php' }
& $iscc /Q "/DMyAppVersion=$version" (Join-Path $PSScriptRoot 'installer.iss')
if ($LASTEXITCODE -ne 0) { throw 'ISCC failed' }

# the portable exe ships alongside the installer on the release
Copy-Item (Join-Path $PSScriptRoot 'StarMaster.exe') (Join-Path $PSScriptRoot 'dist\StarMaster.exe') -Force

Write-Host "Done: dist\StarMaster.exe + dist\StarMaster-Setup.exe (v$version)"
Write-Host "Release: gh release create v$version dist\StarMaster.exe dist\StarMaster-Setup.exe --title `"StarMaster v$version`" --notes `"...`""
