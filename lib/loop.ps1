# Main loop and shutdown.
# ---------------------------------------------------------------------------
# Main loop
$timer = [Windows.Threading.DispatcherTimer]::new()
$timer.Interval=[TimeSpan]::FromMilliseconds(33)
$script:lastRuntimeError=-60.0
# Calm states draw at 20 fps and hidden ones at 4 fps; movement uses real elapsed time either way.
function Update-TickRate {
 $ms=33
 if($script:isHidden){$ms=250}
 elseif($script:busyMode -or $script:paused -or $script:idleRest -eq 'sleeping'){$ms=50}
 if($ms -ne $script:tickMs){$script:tickMs=$ms; $timer.Interval=[TimeSpan]::FromMilliseconds($ms)}
}
# The frame function. The self-test calls it directly.
$script:tickBody={
 try {
 if($script:pendingAction -and -not $menu.IsOpen){
  $action=$script:pendingAction; $script:pendingAction=''
  if($action -eq 'voice-library'){Show-VoiceLibrary}
  elseif($action -eq 'swat'){Invoke-Swat}
  elseif($action -eq 'pet'){Invoke-HeadPat}
  elseif($action -eq 'hide'){
   Set-Hidden $true
   if($null -ne $script:tray){try{$script:tray.ShowBalloonTip(4000,'Capri','Hiding. Double-click the tray icon to bring me back.',[Windows.Forms.ToolTipIcon]::Info)}catch{}}
  }
  elseif($action.StartsWith('state:')){Prepare-ManualAction; $script:routine.Clear(); Begin-State ($action.Substring(6))}
  elseif($action.StartsWith('routine:')){Prepare-ManualAction; Start-Routine ($action.Substring(8))}
  elseif($action -eq 'random'){Prepare-ManualAction; $script:routine.Clear(); Choose-State}
  elseif($action -eq 'speak'){Prepare-ManualAction; $script:voiceNext=0; Play-Voice 'random' $true}
  elseif($action -eq 'windowperch'){
   Prepare-ManualAction; $script:routine.Clear()
   if($null -eq (Find-PerchWindow)){Show-Status 'No window to sit on. Click one first, and leave room above it.'}else{Begin-State 'windowperch'}
  }
  elseif($action -eq 'cursor-test'){
   Prepare-ManualAction
   $buttonsUp=[Windows.Input.Mouse]::LeftButton -eq 'Released' -and [Windows.Input.Mouse]::RightButton -eq 'Released' -and [Windows.Input.Mouse]::MiddleButton -eq 'Released'
   if($buttonsUp){
    [void][CapriCursor]::Nudge(75,-20)
    Add-Content -LiteralPath (Join-Path $script:AppRoot 'cursor-error.log') -Value ((Get-Date -Format s)+' test: '+[CapriCursor]::LastResult) -ErrorAction SilentlyContinue
    [void][Windows.MessageBox]::Show([CapriCursor]::LastResult,'Capri - cursor test')
   }else{[void][Windows.MessageBox]::Show('Release the mouse buttons and try the test again.','Capri - cursor test')}
  }
  elseif($action -eq 'wander-test'){
   Prepare-ManualAction; $script:routine.Clear(); Begin-State 'wander'
   $a=Get-WorkArea
   $span=[Math]::Max(0.0,$a.Width-$w.Width)
   $script:targetX=if($w.Left -lt $a.Left+$span/2){$a.Left+$span*0.85}else{$a.Left+$span*0.15}
   $script:targetY=$w.Top
   $script:until=$clock.Elapsed.TotalSeconds+[Math]::Abs($script:targetX-$w.Left)/65+2
   $script:caughtCooldown=$script:until
  }
  elseif($action -eq 'attack'){
   Prepare-ManualAction; $script:routine.Clear(); $script:chase=$true
   $script:blackwallEnabled=$true
   $script:caughtCooldown=$clock.Elapsed.TotalSeconds+12
   Begin-State 'watch'
  }
 }
 Update-TickRate
 Update-PoseFade
 # Effects must never prevent movement, state transitions, or interaction rendering.
 try {Update-Blackwall; Update-AttackArcs} catch {
  if($clock.Elapsed.TotalSeconds-$script:lastFxError -gt 5){
   $script:lastFxError=$clock.Elapsed.TotalSeconds
   $errorText=($_ | Out-String)
   Add-Content -LiteralPath (Join-Path $script:AppRoot 'effects-error.log') -Value $errorText -ErrorAction SilentlyContinue
  }
 }
 $sabotagePanel.Visibility='Collapsed'
 if($script:exiting){
  $progress=[Math]::Min(1.0,($clock.Elapsed.TotalSeconds-$script:stateStart)/1.2)
  $sprite.Opacity=1-$progress; $shift.X=[Math]::Sin($progress*24)*6
  if($progress -ge 1){$script:allowClose=$true; $w.Close()}
  return
 }
 $t=$clock.Elapsed.TotalSeconds; $elapsedTick=[Math]::Max([double]0,[double]($t-$script:last)); $dt=[Math]::Min([double]0.08,[double]$elapsedTick); $script:last=$t
 if($script:voiceBusy -and $null -ne $script:voiceJob -and $script:voiceJob.IsCompleted){
  $audioFailure=$script:voiceJob.Error; $script:voiceJob=$null
  $script:voiceBusy=$false; End-SpeechPose
  $script:subtitleUntil=$t+1.5; $script:messageUntil=$script:subtitleUntil
  if($audioFailure){Report-AudioError $audioFailure}
 }
 if($t -gt $script:messageUntil){$bubble.Visibility='Collapsed'}
 # Once a second: fullscreen apps, and remember the last app window (clicking Capri makes her the foreground).
 if($t -ge $script:nextFullscreenCheck){
  $script:nextFullscreenCheck=$t+1
  Update-FullscreenState
  try{$fg=[CapriDesktop]::ForegroundWindow(); if($null -ne $fg){$script:lastAppWindow=[long]$fg[0]}}catch{}
 }
 if($script:hotkeyOk -and [CapriHotkey]::TakePressed()){Invoke-Summon}
 if($script:memoryDirty -and $t -ge $script:nextMemorySave){$script:nextMemorySave=$t+30; Save-Memory}
 Update-Music $t
 Update-AppAwareness $t
 if($script:isHidden){
  $script:lastInteraction=$t
  $script:stateStart+=$elapsedTick; $script:until+=$elapsedTick
  $script:nextRetry+=$elapsedTick; $script:nextMood+=$elapsedTick
  return
 }
 # Recover a release delivered outside our window; a stale hold freezes the timer.
 if($script:mouseDown -and -not $script:dragging -and [Windows.Input.Mouse]::LeftButton -eq [Windows.Input.MouseButtonState]::Released){
  $script:mouseDown=$false; $script:downPoint=$null; Register-Interaction
 }
 if(($script:mouseDown -or $script:dragging) -and $t -ge $script:nextHoldRemark){
  $script:heldLongEnough=$true; [void](Show-Remark 'hold')
  $script:nextHoldRemark=$t+5
 }
 if($script:dragging){
  if($script:shakeAngry){Set-Pose 20}else{Set-Pose 30}
  $rotation.Angle=[Math]::Sin($t*16)*9
  $scale.ScaleX=$script:direction*(1+[Math]::Sin($t*14)*0.025)
  $scale.ScaleY=1+[Math]::Sin($t*18)*0.04
  $shift.X=[Math]::Sin($t*22)*3; $shift.Y=[Math]::Sin($t*16)*3
  $sprite.Opacity=1
  return
 }
 if($script:paused -or $menu.IsOpen -or $script:voiceChooserOpen -or $script:mouseDown){
  $script:lastInteraction=$t
  $script:stateStart+=$elapsedTick; $script:until+=$elapsedTick
  $script:nextRetry+=$elapsedTick; $script:nextMood+=$elapsedTick
  Apply-SpeechPose; return
 }
 # You: system-wide idle time (any keyboard or mouse input), not just interaction with Capri.
 $sysIdle=0.0; try{$sysIdle=[CapriDesktop]::IdleSeconds()}catch{}
 $away=$sysIdle -ge 60
 if($sysIdle -ge 300){$script:activeSince=$t}
 if($script:breakMinutes -gt 0 -and $t-$script:activeSince -ge $script:breakMinutes*60 -and $script:idleRest -eq 'awake' -and -not $script:voiceBusy -and -not $script:spec.tags.Contains('noCaught')){
  $script:activeSince=$t
  if(-not $script:busyMode){$script:routine.Clear(); Begin-State 'breaktime'}
  [void](Show-Remark 'break' -Force -Value $script:breakMinutes)
 }
 if($script:busyMode){
  $script:lastInteraction=$t
  $a=Get-WorkArea; $w.Left=$a.Right-$w.Width-12; $w.Top=$a.Bottom-$w.Height
  if($t -ge $script:busyUntil){
   if($script:busyBag.Count -eq 0){
    Add-ShuffledItems $script:busyBag @('gaming','dance','sip','stretch','peek','rest')
    if($script:busyBag.Peek() -eq $script:busyActivity){$script:busyBag.Enqueue($script:busyBag.Dequeue())}
   }
   $script:busyActivity=$script:busyBag.Dequeue(); $script:busyStart=$t
   $script:busyUntil=$t+$rng.Next(9,19)
  }
  $bp=$t-$script:busyStart
  $rotation.Angle=0; $scale.ScaleX=$script:direction; $scale.ScaleY=1
  $shift.X=0; $shift.Y=[Math]::Sin($bp)*1.5; $sprite.Opacity=1
  $wall.Visibility='Collapsed'; $effect.Text=''
  switch($script:busyActivity){
   'gaming' {Set-Pose 25; $rotation.Angle=[Math]::Sin($bp*3)*2; $shift.Y=[Math]::Sin($bp*5)*1.5}
   'dance' {Set-Pose (21+[int][Math]::Floor($bp/0.55)%2); $rotation.Angle=[Math]::Sin($bp*3)*7; $shift.X=[Math]::Sin($bp*3)*5; $shift.Y=-[Math]::Abs([Math]::Sin($bp*3))*5}
   'sip' {if(($bp%4) -lt 2.5){Set-Pose 27}else{Set-Pose 8}; $rotation.Angle=-3+2*[Math]::Sin($bp)}
   'stretch' {Set-Pose 11; $scale.ScaleY=1+0.025*[Math]::Sin($bp*1.5)}
   'peek' {Set-Pose 7; $rotation.Angle=[Math]::Sin($bp)*3}
   'rest' {Set-Pose 5}
  }
  return
 }
 if($t -ge $script:nextMood){
  $moods=@('bored','curious','smug','irritated') | Where-Object {$_ -ne $script:mood}
  $script:mood=$moods[$rng.Next($moods.Count)]; $script:nextMood=$t+$rng.Next(90,181)
 }
 if($script:activityRates.attacks -gt 0 -and $t -ge $script:nextRetry -and $script:spec.tags.Contains('calm') -and $script:chase -and $script:idleRest -eq 'awake' -and -not $script:voiceBusy -and -not $away){
  $script:nextRetry=[double]::PositiveInfinity; $script:rivalRetries++
  Begin-State 'rivalsneak'
 }
 # Absolute cursor position in WPF units: distinguish user movement from the pet moving.
 $mouse=(Get-LocalCursor)
 $cursorX=$w.Left+$mouse.X; $cursorY=$w.Top+$mouse.Y
 $cursorMoved=$false
 if($null -ne $script:cursorLastX){$cursorMoved=([Math]::Abs($cursorX-$script:cursorLastX)+[Math]::Abs($cursorY-$script:cursorLastY) -gt 2)}
 $script:cursorLastX=$cursorX; $script:cursorLastY=$cursorY
 # Coming back to the computer wakes her with a greeting.
 if($script:idleRest -eq 'sleeping' -and $script:sleepReason -eq 'away' -and $sysIdle -lt 2){Wake-FromAway}
 if($sprite.IsMouseOver -and $cursorMoved){Register-Interaction}
 # Finish active speech, yawn once, then stay asleep until direct interaction (or until you come back).
 if($script:idleSleepAfter -gt 0 -and $script:idleRest -eq 'awake' -and $t-$script:lastInteraction -ge $script:idleSleepAfter -and -not $script:spec.tags.Contains('noSleep') -and -not $script:voiceBusy){
  $script:idleRest='yawning'; $script:sleepReason=if($away){'away'}else{'ignored'}
  Begin-State 'yawn'
  $bubble.Visibility='Collapsed'
 }
 if($script:idleRest -ne 'awake' -and $script:sleepReason -eq 'ignored' -and $sysIdle -ge 120){$script:sleepReason='away'}
 if($script:idleRest -eq 'yawning' -and $t -gt $script:until){
  $script:idleRest='sleeping'; Begin-State 'nap'
  $script:until=[double]::PositiveInfinity
 }
 $spec=$script:spec; $tags=$spec.tags
 if(-not $sprite.IsMouseOver){$script:hoverLatched=$false}
 if($sprite.IsMouseOver -and -not $script:hoverLatched -and $cursorMoved -and $t -gt $script:caughtCooldown -and -not $tags.Contains('noCaught')){
  $script:hoverLatched=$true; Begin-State 'caught'
 }
 if($script:idleRest -eq 'awake' -and $t -gt $script:until -and -not $script:voiceBusy){
  $spec=$script:spec
  if($spec.setMood){$script:mood=$spec.setMood}
  $next=$spec.next
  if(-not $script:chase -and $spec.nextNoChase){$next=$spec.nextNoChase}
  if($next -eq '@attack'){Finish-CursorAttack}
  elseif($next -and $next -ne 'choose'){Begin-State $next}
  else{Choose-State}
 }
 # This session's greeting (first meeting, a long absence, an anniversary).
 if($null -ne $script:greeting -and $t -gt 3 -and $script:state -ne 'arrive' -and -not $script:voiceBusy){
  [void](Show-Remark $script:greeting[0] -Value $script:greeting[1]); $script:greeting=$null
 }
 if($script:idleRest -eq 'awake' -and -not $away -and -not (Test-QuietApp) -and $script:chatter -ne 'off' -and $t -gt $script:nextYap -and -not $script:spec.tags.Contains('noYap')){
  Play-Voice 'random'
  if($t -gt $script:nextYap){Schedule-Yap}
 }
 if($script:idleRest -eq 'awake' -and -not $away -and $t -ge $script:nextRemark -and $t -gt $script:messageUntil -and -not $script:spec.tags.Contains('noRemark')){
  if(-not $script:voiceBusy -and ($script:chatter -eq 'off' -or $script:muted -or $script:nextYap-$t -gt 6)){
   $kind=$script:spec.remark
   if($kind -eq 'idle' -and $script:timeOfDay -and $rng.NextDouble() -lt 0.2){
    $hour=(Get-Date).Hour
    if($hour -lt 5){$kind='latenight'}elseif($hour -lt 10){$kind='morning'}
   }
   [void](Show-Remark $kind)
  }
 }

 # Pose and animation for the current state, all driven by its behaviours.json entry.
 $spec=$script:spec; $tags=$spec.tags
 $phase=$t-$script:stateStart
 $pose=Get-SpecPose $spec $phase
 if($script:facialMotion -and $tags.Contains('blink')){
  if($t -ge $script:nextBlink){$script:blinkUntil=$t+0.14; $script:nextBlink=$t+2.5+$rng.NextDouble()*3.5}
  if($t -lt $script:blinkUntil -and ($script:state -ne 'caught' -or $phase -gt 0.7)){$pose=13}
 }
 $moodPose=$tags.Contains('mood') -and -not $script:voiceBusy
 if($moodPose -and $null -eq $spec.timeline){
  if($script:mood -eq 'smug'){$pose=5}elseif($script:mood -eq 'irritated'){$pose=2}
 }
 $script:musicFrameActive=$false
 if($script:musicPoseStart -ge 0 -and ($pose -eq 12 -or $pose -eq 13) -and (Test-MusicGroove)){
  $pose=Get-MusicPose $t; $script:musicFrameActive=$true
 }
 if($null -ne $pose){Set-Pose $pose}
 $scale.ScaleX=$script:direction; $scale.ScaleY=1; $rotation.Angle=0
 $shift.X=0; $shift.Y=[Math]::Sin($t*2)*5; $sprite.Opacity=1
 $area=Get-WorkArea
 $stateBefore=$script:state
 if($spec.path -and $paths.ContainsKey($spec.path)){& $paths[$spec.path]}
 if($script:state -ne $stateBefore){return} # the path changed state (fell off a window, landed)
 if($spec.clampTarget){
  $script:targetX=[Math]::Max($area.Left,[Math]::Min($script:targetX,$area.Right-$w.Width))
  $script:targetY=[Math]::Max($area.Top,[Math]::Min($script:targetY,$area.Bottom-$w.Height))
 }
 if($null -ne $spec.speed){
  $dx=$script:targetX-$w.Left; $dy=$script:targetY-$w.Top
  $distance=[Math]::Sqrt($dx*$dx+$dy*$dy)
  if($distance -gt 3){
   $step=[Math]::Min([double]$distance,[double]($spec.speed*$dt))
   $w.Left += $dx/$distance*$step; $w.Top += $dy/$distance*$step
   if($step -gt 0.05){$script:travelFxUntil=$t+0.12; $script:travelFxDirection=if($dx -ge 0){1.0}else{-1.0}}
   if([Math]::Abs($dx) -gt 4){$script:direction= if($dx -lt 0){1.0}else{-1.0}}
  }
 }
 $effect.Opacity=1
 if($motions.ContainsKey($spec.motion)){& $motions[$spec.motion]}
 Apply-MusicGroove $t
 if($script:voiceBusy -and -not $tags.Contains('noWobble')){
  $scale.ScaleY += [Math]::Sin($t*16)*0.015
 }
 if($moodPose -and $script:mood -eq 'bored'){$rotation.Angle=-3}
 if($script:state -eq 'pounce' -and $phase -gt 0.12 -and (Test-CursorContact)){
  $script:attackContact=$true; $script:until=$t
 }
 Apply-SpeechPose
 Clamp-Pet
 if(-not $script:spec.tags.Contains('noFace')){
  $faceDelta=$cursorX-($w.Left+$w.Width/2)
  # Six-pixel dead zone prevents rapid flipping while the cursor crosses center.
  if($faceDelta -lt -6){$script:direction=1.0}
  elseif($faceDelta -gt 6){$script:direction=-1.0}
  $scale.ScaleX=[Math]::Abs($scale.ScaleX)*$script:direction
 }
 } catch {
  if($clock.Elapsed.TotalSeconds-$script:lastRuntimeError -gt 5){
   $script:lastRuntimeError=$clock.Elapsed.TotalSeconds
   Add-Content -LiteralPath (Join-Path $script:AppRoot 'runtime-error.log') -Value ($_ | Out-String) -ErrorAction SilentlyContinue
  }
  if($script:SelfTestMode){$script:selfTestErrors.Add(($script:state+': '+$_.Exception.Message+' ('+[IO.Path]::GetFileName([string]$_.InvocationInfo.ScriptName)+' line '+$_.InvocationInfo.ScriptLineNumber+')')); return}
  $script:paused=$true
  $wall.Visibility='Collapsed'
  if($cursorFx.IsVisible){$cursorFx.Hide()}
  Show-Problem 'I paused after an animation error (see runtime-error.log). Right-click me > Interact > Do something! to retry.'
 }
}
$timer.Add_Tick($script:tickBody)
$w.Add_Closing({param($sender,$event)
 if(-not $script:allowClose){
  $event.Cancel=$true
  if(-not $script:exiting){
   Stop-Voice; $script:paused=$false; $script:busyMode=$false; $script:exiting=$true
   $script:hiddenManual=$false; $script:hiddenFullscreen=$false; Apply-Visibility
   $menu.IsOpen=$false; Begin-State 'depart'
  }
 }
})
$w.Add_Closed({
 $timer.Stop(); Save-VoiceSettings; Save-Memory; Stop-Voice; $sfxPlayer.Close(); $cursorFx.Close()
 try{if($script:fxNative){[CapriHotkey]::Unregister($script:petHandle)}}catch{}
 if($null -ne $script:tray){$script:tray.Visible=$false; $script:tray.Dispose()}
 if($script:trayIconHandle -ne [IntPtr]::Zero){[void][CapriDesktop]::DestroyIcon($script:trayIconHandle)}
})
