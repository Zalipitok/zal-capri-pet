' Starts Capri. (Capri.exe can also be started directly; this file is kept for old shortcuts.)
Set fso = CreateObject("Scripting.FileSystemObject")
dir = fso.GetParentFolderName(WScript.ScriptFullName)
Set shell = CreateObject("WScript.Shell")
shell.CurrentDirectory = dir
shell.Run """" & dir & "\Capri.exe""", 1, False
