# Capri desktop companion. Windows PowerShell 5.1 / WPF.
#   Capri.vbs          start without a console window
#   Start-Capri.bat    start with a console (shows errors)
#   Run-SelfTest.bat   check everything and write selftest-report.txt
#   tools\Build-Exe.ps1  build Capri.exe
# The code lives in lib\: window, data, screens, speech, memory, states, awareness, menu, input, loop, selftest.
param([switch]$SelfTest)
$ErrorActionPreference = 'Stop'
# The script's folder, or the .exe's folder when built with tools\Build-Exe.ps1.
$hostExe = [Diagnostics.Process]::GetCurrentProcess().MainModule.FileName
$script:IsExe = [IO.Path]::GetFileNameWithoutExtension($hostExe) -notin @('powershell','pwsh','powershell_ise')
$script:AppRoot = if($script:IsExe -or -not $PSScriptRoot){Split-Path -Parent $hostExe}else{$PSScriptRoot}
$script:SelfTestMode = [bool]$SelfTest
$script:LaunchPath = if($script:IsExe){$hostExe}else{$PSCommandPath}

# Startup failures used to vanish with a hidden console: show and log them instead.
trap {
 $detail = ($_ | Out-String)
 try { Add-Content -LiteralPath (Join-Path $script:AppRoot 'startup-error.log') -Value ((Get-Date -Format o)+"`r`n"+$detail) -Encoding UTF8 } catch { }
 try { [void][Windows.MessageBox]::Show("Capri could not start:`n`n"+$_.Exception.Message+"`n`nDetails were written to startup-error.log.",'Capri') } catch { }
 exit 1
}

Add-Type -AssemblyName @('PresentationFramework', 'PresentationCore', 'WindowsBase', 'System.Xaml', 'System.Windows.Forms', 'System.Drawing')

# One Capri at a time (the startup shortcut plus a manual launch would otherwise make two).
# The self-test runs alongside a normal Capri.
$script:instanceIsNew = $false
$mutexName = if($script:SelfTestMode){'Local\CapriSelfTest'}else{'Local\CapriDesktopCompanion'}
$script:instanceMutex = [Threading.Mutex]::new($true, $mutexName, [ref]$script:instanceIsNew)
if(-not $script:instanceIsNew){
 [void][Windows.MessageBox]::Show('Capri is already running. Look for her icon in the system tray.','Capri')
 exit 0
}

# Native helpers (C# 5: Windows PowerShell 5.1 compiles with the legacy compiler).
Add-Type -TypeDefinition ([IO.File]::ReadAllText((Join-Path $script:AppRoot 'lib\native.cs')))
# Per-frame effect geometry and the summon hotkey need WPF references; without them Capri falls back to PowerShell.
$script:fxNative = $false
try {
 $wpfRefs = @([Windows.Window].Assembly.Location, [Windows.Media.Visual].Assembly.Location, [Windows.DependencyObject].Assembly.Location, [System.Xaml.XamlSchemaContext].Assembly.Location)
 Add-Type -ReferencedAssemblies $wpfRefs -TypeDefinition ([IO.File]::ReadAllText((Join-Path $script:AppRoot 'lib\effects.cs')))
 $script:fxNative = $true
} catch {
 try { Add-Content -LiteralPath (Join-Path $script:AppRoot 'effects-error.log') -Value ('Native effects unavailable, using PowerShell fallback: '+$_.Exception.Message) -Encoding UTF8 } catch { }
}

# Experimental, off by default: per-monitor DPI (tuning.json "perMonitorDpi": true). Must happen
# before any window exists.
$script:perMonitorDpi = $false
try {
 $tuningFile = Join-Path $script:AppRoot 'tuning.json'
 if((Test-Path -LiteralPath $tuningFile) -and [bool](Get-Content -LiteralPath $tuningFile -Raw | ConvertFrom-Json).perMonitorDpi){
  [AppContext]::SetSwitch('Switch.System.Windows.DoNotScaleForDpiChanges', $false)
  $script:perMonitorDpi = [CapriDesktop]::EnablePerMonitorDpi()
 }
} catch { }

# Order matters: later parts use what earlier ones define. Out-Null keeps stray output from
# becoming pop-ups in the .exe build.
foreach($part in @('window','data','screens','speech','memory','states','awareness','play','settings','menu','input','loop','selftest')){
 . (Join-Path $script:AppRoot ('lib\'+$part+'.ps1')) | Out-Null
}

$script:activeSince=$clock.Elapsed.TotalSeconds
Start-MemorySession
Begin-State 'arrive'
if(-not $script:SelfTestMode){$timer.Start(); Show-Status 'right-click me for controls'}
[void]$w.ShowDialog()
try { $script:instanceMutex.ReleaseMutex() } catch { }
