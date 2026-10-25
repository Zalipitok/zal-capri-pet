# Capri is a native app now (Capri.exe). This file only starts it, so old shortcuts, the
# "Start with Windows" entry of earlier versions and the PowerShell updater keep working.
param([switch]$SelfTest,[switch]$Friend)
$exe = Join-Path $PSScriptRoot 'Capri.exe'
$arguments = @()
if($SelfTest){$arguments += '--selftest'}
if($Friend){$arguments += '--friend'}
if(Test-Path -LiteralPath $exe){
 if($arguments.Count -gt 0){Start-Process -FilePath $exe -ArgumentList $arguments -WorkingDirectory $PSScriptRoot}
 else{Start-Process -FilePath $exe -WorkingDirectory $PSScriptRoot}
}else{
 Add-Type -AssemblyName PresentationFramework
 [void][Windows.MessageBox]::Show('Capri.exe is missing. Download Capri again from https://github.com/Zalipitok/zal-capri-pet','Capri')
}
