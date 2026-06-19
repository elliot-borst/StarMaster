' StarMaster-Startup.vbs
' Launches the keep-alive app with NO PowerShell console window (just the small GUI).
' Put a shortcut to this file in your Startup folder to run it on boot.
Set sh = CreateObject("WScript.Shell")
sh.Run "powershell.exe -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File ""C:\GitHub\StarMaster\StarMaster.ps1""", 0, False
