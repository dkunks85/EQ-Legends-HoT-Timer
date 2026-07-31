Option Explicit
Dim shell, fso, appDir, ps1, command
Set shell = CreateObject("WScript.Shell")
Set fso = CreateObject("Scripting.FileSystemObject")
appDir = fso.GetParentFolderName(WScript.ScriptFullName)
ps1 = fso.BuildPath(appDir, "EQSpellTimer.ps1")
command = "powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -STA -WindowStyle Hidden -File """ & ps1 & """"
shell.Run command, 0, False
