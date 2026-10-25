# Self-test: Capri.ps1 -SelfTest (or Run-SelfTest.bat, or Debug and help > Run self-test).
# Runs every state for a few frames and checks data, audio, volume, menus and Windows features,
# then writes selftest-report.txt. It never saves settings or memory, and plays no sound.
$script:selfTestErrors=[Collections.Generic.List[string]]::new()
function Invoke-SelfTest {
 $report=[Collections.Generic.List[string]]::new()
 $counts=@{PASS=0; WARN=0; FAIL=0}
 function Add-Result([string]$level,[string]$name,[string]$detail=''){
  $counts[$level]++
  $line=('{0,-4}  {1}' -f $level,$name); if($detail){$line+=' - '+$detail}
  $report.Add($line)
 }
 # Returns nothing to pass, a string to fail, or 'WARN: ...' to warn.
 function Test-Step([string]$name,[scriptblock]$check){
  try {
   $result=& $check
   if($result -is [string] -and $result.StartsWith('WARN: ')){Add-Result 'WARN' $name $result.Substring(6)}
   elseif($result -is [string] -and $result){Add-Result 'FAIL' $name $result}
   else{Add-Result 'PASS' $name}
  } catch { Add-Result 'FAIL' $name $_.Exception.Message }
 }
 # Quiet, safe conditions for the duration of the test (none of this is saved).
 $script:muted=$true; $script:cursorKnockback=$false; $script:fullscreenMode='ignore'; $script:breakMinutes=0
 $script:hiddenFullscreen=$false; $script:hiddenManual=$false; Apply-Visibility

 # --- Files and data
 Test-Step 'Required files' {
  $missing=@('behaviours.json','remarks.json','voice-pack.json','menu-theme.xaml','tuning.json','app-reactions.json','unlocks.json','sounds\cartoon-swat.wav','pose-shaken.png',
   'poses-clean.png','poses-extra-clean.png','poses-life-clean.png','poses-faces-clean.png','poses-casting-clean.png','poses-motion.png','poses-daily.png','poses-interaction.png') |
   Where-Object {-not (Test-Path -LiteralPath (Join-Path $script:AppRoot $_))}
  if(@($missing).Count -gt 0){'missing: '+(@($missing) -join ', ')}
 }
 Test-Step 'Sprite frames' {if($poses.Count -lt 33){'expected at least 33 frames, found '+$poses.Count}}
 Test-Step 'Talking-mouth frames' {if($script:mouthPoses.Count -eq 0){'WARN: no mouth frames (sprites\mouth-*.png); her mouth stays still while she talks'}}
 Test-Step 'Eye frames' {if($script:eyePoses.Count -eq 0){'WARN: no eye frames (sprites\eye-*.png); her eyes stay still'}}
 Test-Step 'Headphone poses' {if($script:hpPoses.Count -eq 0){'WARN: no headphone poses (sprites\hp-*.png)'}}
 Test-Step 'Music frames' {if($script:musicPoseStart -lt 0){'WARN: no headphone frames (sprites\music-0..3.png); she sways to music instead'}}
 Test-Step 'Normalised sprites' {
  if($null -eq $script:spriteFeet){return 'WARN: sprites\ folder missing, so frames are cut from the sheets (clipped hair, size jumps)'}
  $sizes=@($poses | ForEach-Object {''+$_.PixelWidth+'x'+$_.PixelHeight} | Select-Object -Unique)
  if($sizes.Count -ne 1){'frames have different sizes: '+($sizes -join ', ')}
  elseif($script:spriteFeet.Count -ne $poses.Count){'frames.json lists '+$script:spriteFeet.Count+' feet positions for '+$poses.Count+' frames'}
 }
 Test-Step 'Files are read from the Capri folder' {if((Split-Path -Leaf $settingsPath) -ne 'voice-settings.json' -or (Split-Path -Parent $settingsPath) -ne $script:AppRoot){'settings path is '+$settingsPath}}
 Test-Step 'Sprite cache is writable' {
  $probe=Join-Path $script:spriteCache 'write-test.tmp'
  try{Set-Content -LiteralPath $probe -Value 'ok'; Remove-Item -LiteralPath $probe}catch{'WARN: '+$_.Exception.Message+' (startup will be slower)'}
 }
 Test-Step 'tuning.json' {if($script:tuningError){$script:tuningError}}
 Test-Step 'Settings file' {
  if(Test-Path -LiteralPath $settingsPath){$null=Get-Content -LiteralPath $settingsPath -Raw | ConvertFrom-Json}
  if($script:volume -le 0){'WARN: saved volume is 0, so she is silent. Pick a level in Settings > Voice and subtitles > Volume.'}
 }
 Test-Step 'State definitions' {
  $problems=@()
  foreach($spec in $script:specs.Values){
   $indices=@(); if($null -ne $spec.pose){$indices+=$spec.pose}; if($null -ne $spec.timeline){$indices+=@($spec.timeline | ForEach-Object {$_[0]})}
   foreach($i in $indices){if($i -lt 0 -or $i -ge $poses.Count){$problems+="$($spec.id): pose $i does not exist"}}
   if($spec.path -and -not $paths.ContainsKey($spec.path)){$problems+="$($spec.id): unknown path '$($spec.path)'"}
   foreach($next in @($spec.next,$spec.nextNoChase)){if($next -and $next -notin @('choose','@attack') -and -not $script:specs.Contains($next)){$problems+="$($spec.id): next state '$next' does not exist"}}
   if($spec.remark -and $null -eq $remarkPack.($spec.remark)){$problems+="$($spec.id): remark pool '$($spec.remark)' missing"}
   if($spec.menu -and -not $spec.title){$problems+="$($spec.id): in a menu but has no title"}
  }
  foreach($name in $script:routines.Keys){foreach($step in $script:routines[$name]){if(-not $script:specs.Contains($step)){$problems+="routine '$name': unknown state '$step'"}}}
  if($problems.Count -gt 0){$problems -join '; '}
 }
 Test-Step 'Remark pools used by the code' {
  $used=@(Get-ChildItem -LiteralPath (Join-Path $script:AppRoot 'lib') -Filter '*.ps1' | ForEach-Object {
   [regex]::Matches((Get-Content -LiteralPath $_.FullName -Raw),"Show-Remark '([a-z]+)'") | ForEach-Object {$_.Groups[1].Value}
  }) + @('morning','latenight','returned','firstmeet','longtime','anniversary','appgames','appcode','appvideo','applate') | Select-Object -Unique
  $missing=@($used | Where-Object {@($remarkPack.$_ | Where-Object {$_}).Count -eq 0})
  if($missing.Count -gt 0){'empty or missing in remarks.json: '+($missing -join ', ')}
 }

 # --- Every state, a few frames each
 $stateFailures=@()
 foreach($spec in @($script:specs.Values)){
  if($spec.id -eq 'depart'){continue}
  $script:selfTestErrors.Clear()
  $script:paused=$false; $script:busyMode=$false; $script:idleRest='awake'; $script:routine.Clear(); $script:pendingAction=''
  try {
   Begin-State $spec.id
   if($spec.id -eq 'thrown'){$script:throwVX=600; $script:throwVY=-400}
   for($frame=0;$frame -lt 4;$frame++){& $script:tickBody; Start-Sleep -Milliseconds 15}
  } catch { $script:selfTestErrors.Add($spec.id+': '+$_.Exception.Message) }
  if($script:poseIndex -lt 0 -or $script:poseIndex -ge $poses.Count){$script:selfTestErrors.Add($spec.id+': ended on missing pose '+$script:poseIndex)}
  if($script:selfTestErrors.Count -gt 0){$stateFailures+=@($script:selfTestErrors)}
 }
 if($stateFailures.Count -gt 0){foreach($f in ($stateFailures | Select-Object -Unique)){Add-Result 'FAIL' 'State runs' $f}}
 else{Add-Result 'PASS' ('All '+($script:specs.Count-1)+' states run')}
 Test-Step 'Busy, paused and hidden frames' {
  $script:selfTestErrors.Clear()
  Set-BusyMode $true; & $script:tickBody; & $script:tickBody; Set-BusyMode $false
  $script:paused=$true; & $script:tickBody; $script:paused=$false
  $script:hiddenManual=$true; Apply-Visibility; & $script:tickBody; $script:hiddenManual=$false; Apply-Visibility
  if($script:selfTestErrors.Count -gt 0){$script:selfTestErrors -join '; '}
 }
 Test-Step 'Unlocks' {
  if($script:unlocksError){return 'unlocks.json: '+$script:unlocksError}
  if($script:unlocks.Count -eq 0){return 'WARN: unlocks.json missing or empty'}
  $bad=@(foreach($u in $script:unlocks){foreach($s in @($u.routine)){if($s -and -not $script:specs.Contains([string]$s)){$u.id+': unknown state '+$s}}; if($u.remarks -and $null -eq $remarkPack.($u.remarks)){$u.id+': remark pool '+$u.remarks+' missing'}})
  if($bad.Count -gt 0){$bad -join '; '}else{[void](Get-UnlockSummary)}
 }
 Test-Step 'Catch-me game' {
  $script:selfTestErrors.Clear()
  Start-CatchGame; & $script:tickBody; [void](Register-GameCatch); & $script:tickBody
  if($script:game.score -ne 1){return 'a click did not count'}
  Stop-CatchGame; & $script:tickBody
  if($script:game.active){return 'the game did not end'}
  if($script:selfTestErrors.Count -gt 0){$script:selfTestErrors -join '; '}
 }
 Test-Step 'Settings window' {
  Show-SettingsWindow
  if($null -eq $script:settingsWindow){return 'window did not open'}
  $script:settingsWindow.Close()
 }
 Test-Step 'Eyes follow the cursor' {$c=Get-LocalCursor; [void](Get-EyeDirection ($w.Left+$c.X) ($w.Top+$c.Y))}
 Test-Step 'Battery check' {$script:nextBatteryPoll=0; Update-Battery 1.0}
 Test-Step 'Beat sync' {
  if(-not $script:beatSync){return 'WARN: turned off in settings'}
  [CapriBeat]::Start()
  $deadline=[DateTime]::Now.AddSeconds(2.5)
  while([DateTime]::Now -lt $deadline -and -not [CapriBeat]::LastError){Start-Sleep -Milliseconds 100}
  $problem=[CapriBeat]::LastError; [CapriBeat]::Stop()
  if($problem){'WARN: cannot listen to the PC''s sound ('+$problem+'); she bobs at a fixed tempo instead'}
 }
 Test-Step 'Routines start' {foreach($name in @($script:routines.Keys)){Start-Routine $name}; $script:routine.Clear()}
 Test-Step 'File drop scan' {Start-FileScan 'C:\example\test file.txt'; & $script:tickBody; if($script:state -ne 'filescan'){'scan did not start'}}

 # --- Sound
 Test-Step 'Volume keeps its value' {
  $original=$script:volume; $bad=@()
  foreach($level in @(0.25,0.5,0.75,1.0)){Set-Volume $level; if([Math]::Abs($script:volume-$level) -gt 0.0001){$bad+="$level became $($script:volume)"}}
  Set-Volume $original
  if($bad.Count -gt 0){$bad -join '; '}
 }
 Test-Step 'Live volume control' {if(-not $script:liveVolume){'WARN: Windows refused per-app volume; volume changes apply from the next line instead'}}
 Test-Step 'Voice clips are readable 16-bit WAV' {
  $bad=@()
  foreach($clip in $voicePack){
   $path=Join-Path $script:AppRoot ([string]$clip.file)
   if(-not (Test-Path -LiteralPath $path -PathType Leaf)){$bad+="$($clip.id): missing"; continue}
   try{$bytes=[IO.File]::ReadAllBytes($path); [void][CapriPcmAudio]::WithVolume($bytes,1.0); if([CapriPcmAudio]::Envelope($bytes,30.0).Length -eq 0){$bad+="$($clip.id): no loudness data for the mouth"}}catch{$bad+="$($clip.id): $($_.Exception.Message)"}
  }
  if($bad.Count -gt 0){(@($bad | Select-Object -First 8) -join '; ')+$(if($bad.Count -gt 8){" (+$($bad.Count-8) more)"}else{''})}
 }
 Test-Step 'Sound playback loads' {
  $clip=@($voicePack)[0]
  if($null -eq $clip){return 'WARN: no voice clips'}
  $player=[System.Media.SoundPlayer]::new((Join-Path $script:AppRoot ([string]$clip.file))); try{$player.Load()}finally{$player.Dispose()}
 }
 Test-Step 'Voice clips switched on' {
  $on=@($voicePack | Where-Object {$_.language -eq $script:language -and $script:approvedVoices -contains ($_.language+':'+$_.id)})
  if($on.Count -eq 0){"WARN: no $($script:language) clips are on for random chatter (Voice library > Enable established only)"}
 }

 # --- Menus
 Test-Step 'Menu checkmarks' {foreach($sync in $script:menuSync){[void](& $sync[1] $sync[0])}}
 Test-Step 'Menu items' {
  $count=0; $stack=[Collections.Generic.Stack[object]]::new(); $stack.Push($menu)
  while($stack.Count -gt 0){$node=$stack.Pop(); foreach($child in $node.Items){if($child -is [Windows.Controls.MenuItem]){$count++; $stack.Push($child)}}}
  if($count -lt 60){"only $count menu items were built"}
 }
 Test-Step 'Tray icon' {if($null -eq $script:tray){'WARN: no tray icon; she keeps a taskbar button instead'}}

 # --- Windows features
 Test-Step 'Monitors' {$areas=@(Get-AllWorkAreas); if($areas.Count -eq 0){'no monitors reported'}}
 Test-Step 'Work area' {$a=Get-WorkArea; if($a.Width -lt 300 -or $a.Height -lt 300){"work area looks wrong: $a"}}
 Test-Step 'Fullscreen detection' {[void][CapriDesktop]::FullscreenBlocks($script:petHandle)}
 Test-Step 'Idle time' {$idle=[CapriDesktop]::IdleSeconds(); if($idle -lt 0){"negative idle time $idle"}}
 Test-Step 'Native effects' {if(-not $script:fxNative){'WARN: using the slower PowerShell effects (see effects-error.log)'}}
 Test-Step 'Summon hotkey' {
  if(-not $script:hotkeyEnabled -or $script:hotkeyOk){return}
  # The self-test runs next to a normal Capri, which already holds the hotkey: that's fine.
  $running=$null
  if([Threading.Mutex]::TryOpenExisting('Local\CapriDesktopCompanion',[ref]$running)){$running.Dispose(); return}
  'WARN: '+$script:hotkeyError
 }
 Test-Step 'Music reactions' {
  if(-not $script:musicAvailable){return 'WARN: not available ('+$script:musicError+')'}
  $deadline=[DateTime]::Now.AddSeconds(3)
  while($null -ne $script:musicManagerTask -and -not $script:musicManagerTask.IsCompleted -and [DateTime]::Now -lt $deadline){Start-Sleep -Milliseconds 50}
  if($script:musicManagerTask.IsFaulted){'WARN: Windows media sessions did not respond'}
 }
 Test-Step 'App awareness' {
  $app=[CapriDesktop]::ForegroundApp()
  if($null -ne $app){[void](Get-AppCategory $app[0] $app[1])}
  if($null -eq $script:appRules){'WARN: app-reactions.json missing or invalid'}
 }

 # --- Report
 $header=@("Capri self-test  $(Get-Date -Format 'yyyy-MM-dd HH:mm')","$($counts.PASS) passed, $($counts.WARN) warnings, $($counts.FAIL) failed",
  "Windows $([Environment]::OSVersion.Version)  PowerShell $($PSVersionTable.PSVersion)  $(if($script:IsExe){'exe build'}else{'script'})",'')
 $reportPath=Join-Path $script:AppRoot 'selftest-report.txt'
 try{Set-Content -LiteralPath $reportPath -Value (@($header)+@($report)) -Encoding UTF8}catch{}
 $failed=@($report | Where-Object {$_ -notlike 'PASS*'} | Select-Object -First 10)
 $text=$header[1]+$(if($failed.Count -gt 0){"`n`n"+($failed -join "`n")}else{"`n`nEverything checks out."})+"`n`nFull report: selftest-report.txt"
 [void][Windows.MessageBox]::Show($text,'Capri self-test')
 $script:allowClose=$true; $w.Close()
}
$w.Add_ContentRendered({if($script:SelfTestMode){Invoke-SelfTest}})
