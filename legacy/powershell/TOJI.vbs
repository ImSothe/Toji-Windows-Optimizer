' TOJI.vbs - lanzador silencioso (sin ventana de consola). Doble clic.
Set fso = CreateObject("Scripting.FileSystemObject")
Set sh  = CreateObject("WScript.Shell")
sh.CurrentDirectory = fso.GetParentFolderName(WScript.ScriptFullName)
sh.Run "powershell -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -Command ""Get-ChildItem *.ps1,*.json | Unblock-File; & '.\TOJI-GUI.ps1'""", 0, False
