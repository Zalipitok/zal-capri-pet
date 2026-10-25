# State machine, movement paths, animations, throws, visibility and modes.
# ---------------------------------------------------------------------------
# State machine
$script:routine=[Collections.Generic.Queue[string]]::new()
$script:autoBag=[Collections.Generic.Queue[string]]::new()
$script:routineBag=[Collections.Generic.Queue[string]]::new()
$script:hoverLatched=$false
function Start-Routine([string]$name){
 $script:routine.Clear()
 foreach($step in $script:routines[$name]){$script:routine.Enqueue($step)}
 $script:paused=$false
 Choose-State
}

function Begin-State([string]$name) {
 if($script:exiting -and $name -ne 'depart'){return}
 $perch=$null
 if($name -eq 'perch' -and $script:windowPerch -and $rng.NextDouble() -lt $script:tuning.perchChance){$perch=Find-PerchWindow; if($null -ne $perch){$name='windowperch'}}
 elseif($name -eq 'windowperch'){$perch=Find-PerchWindow; if($null -eq $perch){$name='perch'}}
 $a=Get-WorkArea
 $others=@()
 if($name -eq 'roam'){$others=@(Get-AllWorkAreas | Where-Object {-not $_.Equals($a)}); if($others.Count -eq 0){$name='wander'}}

 $spec=Get-Spec $name
 $script:state=$name; $script:spec=$spec; $script:stateStart=$clock.Elapsed.TotalSeconds
 $seconds=$spec.seconds
 $script:until=$script:stateStart+$(if($seconds -is [array]){$rng.Next($seconds[0],$seconds[1]+1)}else{$seconds})
 $script:anchorX=$w.Left; $script:anchorY=$w.Top
 $script:targetX=$a.Left+$rng.NextDouble()*[Math]::Max(1.0,$a.Width-$w.Width)
 $script:targetY=$a.Top+$rng.NextDouble()*[Math]::Max(1.0,$a.Height-$w.Height)
 switch($spec.target){
  'bottom' {$script:targetY=$a.Bottom-$w.Height}
  'taskbar' {$script:targetX=$w.Left; $script:targetY=$a.Bottom-$w.Height}
  'edge' {$script:targetX=if($rng.Next(2) -eq 0){$a.Left}else{$a.Right-$w.Width}}
  'cursor' {
   $cursor=(Get-LocalCursor)
   $script:targetX=$w.Left+$cursor.X-$w.Width/2
   $script:targetY=$w.Top+$cursor.Y-($w.Height+100)/2
  }
  'cursorAnchor' {
   $cursor=(Get-LocalCursor)
   $script:anchorX=$w.Left+$cursor.X-$w.Width/2
   $script:anchorY=$w.Top+$cursor.Y-$w.Height/2
  }
  'otherMonitor' {
   $b=$others[$rng.Next($others.Count)]
   $script:targetX=$b.Left+$rng.NextDouble()*[Math]::Max(1.0,$b.Width-$w.Width)
   $script:targetY=$b.Top+$rng.NextDouble()*[Math]::Max(1.0,$b.Height-$w.Height)
   $distance=[Math]::Sqrt([Math]::Pow($script:targetX-$w.Left,2)+[Math]::Pow($script:targetY-$w.Top,2))
   $script:until=$script:stateStart+$distance/[Math]::Max(1.0,$spec.speed)+1.5
  }
  'window' {
   $script:perchHandle=[long]$perch[0]; $r=$perch[1]; $script:perchArrived=$false
   $script:perchOffsetX=($r.Width-$w.Width)*(0.1+0.8*$rng.NextDouble())
   $script:targetX=$r.Left+$script:perchOffsetX; $script:targetY=$r.Top-$w.Height+(Get-SpriteFoot)+$script:tuning.perchSink
  }
 }
 switch($name){
  'teleport' {$script:teleported=$false}
  'pounce' {$script:attackContact=$false}
  'caught' {$script:caughtCooldown=$script:stateStart+5}
  'scanverdict' {[void](Show-Remark 'scanverdict' -Value $script:scanScore)}
 }
 $effect.Text=$spec.effect
}
# Shuffle without replacement: each solo activity gets one slot per cycle.
function Add-ShuffledItems($queue, [object[]]$items) {
 for($i=$items.Count-1; $i -gt 0; $i--){
  $j=$rng.Next($i+1); $swap=$items[$i]; $items[$i]=$items[$j]; $items[$j]=$swap
 }
 # Avoid a duplicate at the boundary between cycles where possible.
 if($items.Count -gt 1 -and $items[0] -eq $script:state){
  $swap=$items[0]; $items[0]=$items[1]; $items[1]=$swap
 }
 foreach($item in $items){$queue.Enqueue([string]$item)}
}
function Get-ActivityCategory([string]$name){return (Get-Spec $name).category}
function Choose-State {
 if($script:busyMode){Begin-State 'taskbar'; return}
 if($script:routine.Count -gt 0){Begin-State ($script:routine.Dequeue()); return}
 $now=$clock.Elapsed.TotalSeconds
 if($script:activityRates.rare -gt 0 -and $now -gt $script:nextRare -and -not $script:voiceBusy){
  $script:nextRare=$now+$rng.Next(180,361)*100.0/$script:activityRates.rare
  $rare=@($script:specs.Values | Where-Object {$_.rare} | ForEach-Object {$_.id})
  if($rare.Count -gt 0){Begin-State $rare[$rng.Next($rare.Count)]; return}
 }
 # Music playing: dance a bit more often.
 if($script:musicEnabled -and $script:musicPlaying -and $script:activityRates.antics -gt 0 -and $rng.NextDouble() -lt 0.2){Begin-State 'dance'; return}
 # Time of day: an afternoon drink break once a day, and drowsier antics after midnight.
 if($script:timeOfDay){
  $date=Get-Date
  if($date.Hour -ge 14 -and $date.Hour -lt 17 -and $script:drinkBreakDay -ne $date.Date -and $now -gt 600 -and $script:activityRates.antics -gt 0 -and $script:routines.Contains('Drink break')){
   $script:drinkBreakDay=$date.Date; Start-Routine 'Drink break'; return
  }
  if($date.Hour -lt 5 -and $script:activityRates.resting -gt 0 -and $rng.NextDouble() -lt 0.25){
   $sleepy=@('yawn','sleepy','meditate'); Begin-State $sleepy[$rng.Next($sleepy.Count)]; return
  }
 }

 while($true){
  if($script:autoBag.Count -eq 0){
   $choices=@($script:specs.Values | Where-Object {$_.auto -and ($script:chase -or -not $_.tags.Contains('needsChase'))} | ForEach-Object {$_.id})
   # One full routine per cycle; routines also rotate without replacement.
   $choices+=@('__routine')
   $weighted=@()
   foreach($choice in $choices){
    $category=if($choice -eq '__routine'){'antics'}else{Get-ActivityCategory $choice}; $rate=[int]$script:activityRates[$category]
    if($rate -ge 100){$weighted+=@($choice)}
    if($rng.Next(100) -lt ($rate%100)){$weighted+=@($choice)}
    if($rate -eq 200){$weighted+=@($choice)}
   }
   if($weighted.Count -eq 0){$weighted=@('float')}
   Add-ShuffledItems $script:autoBag $weighted
  }
  # Preserve shuffled order so mood cannot repeatedly prioritize a small subset.
  $next=$script:autoBag.Dequeue()
  if($next -eq '__routine'){
   if($script:activityRates.antics -le 0){continue}
   if($script:routineBag.Count -eq 0){Add-ShuffledItems $script:routineBag @($script:routines.Keys)}
   Start-Routine ($script:routineBag.Dequeue()); return
  }
  if($next -ne 'float' -and $script:activityRates[(Get-ActivityCategory $next)] -le 0){continue}
  if(-not $script:chase -and (Get-Spec $next).tags.Contains('needsChase')){continue}
  # With several monitors, some wanders become a trip to another screen.
  if($next -eq 'wander' -and $rng.NextDouble() -lt $script:tuning.roamChance -and @(Get-AllWorkAreas).Count -gt 1){$next='roam'}
  Begin-State $next; return
 }
}

# Per-frame target updaters ("path" in behaviours.json). Locals come from the tick: $t, $dt, $phase, $area, $mouse, $cursorX, $cursorY.
$paths=@{
 'watch'={
  $dxToCursor=$cursorX-($w.Left+$w.Width/2)
  if([Math]::Abs($dxToCursor) -gt 12){$script:direction=if($dxToCursor -lt 0){1.0}else{-1.0}}
 }
 'creep'={
  $dxToCursor=$cursorX-($w.Left+$w.Width/2)
  if([Math]::Abs($dxToCursor) -gt 12){$script:direction=if($dxToCursor -lt 0){1.0}else{-1.0}}
  $side=if($dxToCursor -lt 0){1}else{-1}
  if($script:rivalRetries -gt 0 -or ($script:escapeStreak%2) -eq 1){$side=-$side}
  $script:targetX=$cursorX-$w.Width/2+$side*85
  $script:targetY=$cursorY-($w.Height+100)/2+30
 }
 'chase'={$script:targetX=$w.Left+$mouse.X-$w.Width/2; $script:targetY=$w.Top+$mouse.Y+25}
 'orbit'={$script:targetX=$script:anchorX+[Math]::Cos($phase*1.4)*85; $script:targetY=$script:anchorY+[Math]::Sin($phase*1.4)*60}
 'patrol'={$script:targetX=$area.Left+($area.Width-$w.Width)*(0.5+0.45*[Math]::Sin($phase*0.8)); $script:targetY=$area.Bottom-$w.Height}
 'zigzag'={$script:targetX=$script:anchorX+[Math]::Sin($phase*2)*140; $script:targetY=$script:anchorY+[Math]::Sin($phase*3)*65}
 'figure8'={$script:targetX=$script:anchorX+[Math]::Sin($phase)*110; $script:targetY=$script:anchorY+[Math]::Sin($phase*2)*50}
 'bounce'={$script:targetX=$script:anchorX+[Math]::Sin($phase)*120; $script:targetY=$script:anchorY-[Math]::Abs([Math]::Sin($phase*2))*70}
 'rocket'={$script:targetX=$script:anchorX; $script:targetY=$script:anchorY-180}
 'landing'={$script:targetX=$script:anchorX; $script:targetY=$area.Bottom-$w.Height}
 # Sit on a window's title bar and ride along when it moves; fall off when it goes away.
 'windowperch'={
  $r=Get-WindowRectDip $script:perchHandle
  if($null -eq $r -or $r.Top-($w.Height-(Get-SpriteFoot)) -lt $area.Top-2){Start-Throw 0 0 'fell'; return}
  $script:targetX=$r.Left+$script:perchOffsetX; $script:targetY=$r.Top-$w.Height+(Get-SpriteFoot)+$script:tuning.perchSink
  if(-not $script:perchArrived -and [Math]::Abs($script:targetX-$w.Left)+[Math]::Abs($script:targetY-$w.Top) -le 4){$script:perchArrived=$true}
  if($script:perchArrived){$w.Left=$script:targetX; $w.Top=$script:targetY}
 }
 # Gravity, wall bounces and a floor at the bottom of the current monitor's work area.
 'thrown'={
  $script:throwVY+=$script:tuning.gravity*$dt
  $script:throwVX*=[Math]::Max(0.0,1-0.5*$dt)
  $nx=$w.Left+$script:throwVX*$dt; $ny=$w.Top+$script:throwVY*$dt
  $maxX=$area.Right-$w.Width; $floor=$area.Bottom-$w.Height
  if($nx -lt $area.Left){$nx=$area.Left; $script:throwVX=[Math]::Abs($script:throwVX)*$script:tuning.wallBounce; $script:throwSquash=1}
  elseif($nx -gt $maxX){$nx=$maxX; $script:throwVX=-[Math]::Abs($script:throwVX)*$script:tuning.wallBounce; $script:throwSquash=1}
  if($ny -lt $area.Top){$ny=$area.Top; $script:throwVY=[Math]::Abs($script:throwVY)*0.4}
  $landed=$false
  if($ny -ge $floor){
   $ny=$floor
   if($script:throwVY -gt 420){$script:throwVY=-$script:throwVY*$script:tuning.floorBounce; $script:throwVX*=0.7; $script:throwSquash=1}else{$landed=$true}
  }
  $w.Left=$nx; $w.Top=$ny
  if([Math]::Abs($script:throwVX) -gt 4){$script:direction=if($script:throwVX -lt 0){1.0}else{-1.0}}
  $script:throwSquash=[Math]::Max(0.0,$script:throwSquash-$dt*4)
  if($landed -or $phase -gt 6){Finish-Throw}
 }
}

# Per-frame animation ("motion" in behaviours.json; defaults to the state id).
# Runs after the base pose and the default hover bob are applied.
$motions=@{
 'headpat'={
  $pat=[Math]::Sin([Math]::Min([double]1,[double]($phase/3.2))*[Math]::PI)
  $rotation.Angle=$script:direction*5*$pat
  $shift.Y=3*[Math]::Abs([Math]::Sin($phase*7))*$pat
  $scale.ScaleY=1-0.025*$pat
 }
 'swat'={
  if($phase -lt 0.65){
   $kick=[Math]::Sin([Math]::Min(1.0,$phase/0.65)*[Math]::PI)
   $shift.X=-$script:direction*$kick*22; $shift.Y=-$kick*20
   $rotation.Angle=$script:direction*$kick*20; $scale.ScaleY=1-$kick*0.08
  }elseif($phase -lt 2.1){$rotation.Angle=-$script:direction*7; $shift.Y=0}
  else{$rotation.Angle=-$script:direction*3; $shift.Y=0}
 }
 'attentionbait'={
  $rotation.Angle=$script:direction*(2+[Math]::Sin($phase*4)*2)
  $point=Get-LocalCursor; $gap=[Math]::Sqrt([Math]::Pow($point.X-$w.Width/2,2)+[Math]::Pow($point.Y-($w.Height+100)/2,2))
  if($phase -gt 0.8 -and $gap -lt 125){Begin-State 'baitjudge'}
 }
 'baitjudge'={$shift.Y=0; $rotation.Angle=-$script:direction*4}
 'scanwindow'={
  $rotation.Angle=-$script:direction*5; $shift.Y=[Math]::Sin($phase*4)*2
  $sabotagePanel.Visibility='Visible'; $sabotageText.Text=('WINDOW SCAN // SIMULATION  '+[Math]::Min(100.0,[int]($phase/6*100))+'%')
 }
 'teleport'={
  $sprite.Opacity=[Math]::Abs([Math]::Cos($phase*9)); $shift.X=[Math]::Sin($phase*40)*8
  if($phase -gt 0.8 -and -not $script:teleported){$script:teleported=$true; $w.Left=$script:targetX; $w.Top=$script:targetY}
 }
 'upsidedown'={$rotation.Angle=180+[Math]::Sin($phase*2)*5; $shift.Y=[Math]::Sin($phase*2)*5}
 'failedattack'={$rotation.Angle=[Math]::Min(1.0,$phase/1.2)*210; $shift.X=$script:direction*[Math]::Sin([Math]::Min($phase/3,1.0)*[Math]::PI)*35; $shift.Y=[Math]::Sin($phase*3)*12}
 'windup'={$scale.ScaleY=0.86; $scale.ScaleX=$script:direction*1.05; $rotation.Angle=$script:direction*12; $shift.X=-$script:direction*10}
 'impact'={$scale.ScaleY=0.94; $rotation.Angle=-$script:direction*8; $shift.X=[Math]::Sin($phase*20)*5*[Math]::Exp(-$phase*5)}
 'watch'={$rotation.Angle=$script:direction*3; $shift.Y=[Math]::Sin($phase*1.2)*2}
 'creep'={$rotation.Angle=$script:direction*5; $scale.ScaleY=0.96; $shift.Y=-[Math]::Abs([Math]::Sin($phase*4))*3}
 'pounce'={$rotation.Angle=-$script:direction*12; $scale.ScaleY=0.92; $shift.Y=-[Math]::Sin([Math]::Min(1.0,$phase/1.0)*[Math]::PI)*18}
 'caught'={$rotation.Angle=0; $shift.X=0; $shift.Y=0}
 'beckon'={$rotation.Angle=-3+[Math]::Sin($phase*3)*2; $shift.X=$script:direction*[Math]::Sin($phase*3)*3}
 'casting'={
  $c=$phase%3.0
  if($c -lt 1.2){$shift.X=-$script:direction*6}else{$shift.X=$script:direction*8*[Math]::Exp(-($c-1.2)*4)}
  $rotation.Angle=$script:direction*3*[Math]::Sin($phase*2)
 }
 'sideglance'={$rotation.Angle=-$script:direction*3; $shift.Y=[Math]::Sin($phase*1.2)*2}
 'sip'={$rotation.Angle=-4+3*[Math]::Sin($phase*1.2); $shift.Y=[Math]::Sin($phase)*3; $effect.Opacity=0.4+0.5*[Math]::Abs([Math]::Sin($phase))}
 'gaming'={$rotation.Angle=[Math]::Sin($phase*3)*3; $shift.Y=[Math]::Sin($phase*2)*3; $effect.Text=('LEVEL '+(1+[int][Math]::Floor($phase/3))); $scale.ScaleY=1+[Math]::Sin($phase*12)*0.008}
 'laughing'={$rotation.Angle=[Math]::Sin($phase*7)*6; $shift.Y=-[Math]::Abs([Math]::Sin($phase*9))*7; $scale.ScaleY=0.97+0.03*[Math]::Sin($phase*9)}
 'yawn'={$scale.ScaleY=0.96+0.06*[Math]::Sin([Math]::Min([Math]::PI,$phase*0.6)); $rotation.Angle=[Math]::Sin($phase)*3}
 'cheers'={$shift.Y=-[Math]::Abs([Math]::Sin($phase*2))*10; $rotation.Angle=-8+4*[Math]::Sin($phase*2)}
 'bossfight'={$rotation.Angle=[Math]::Sin($phase*8)*6; $shift.X=[Math]::Sin($phase*18)*2; $effect.Text=('BOSS HP '+[Math]::Max(0.0,100-[int]($phase*10)))}
 'sleepy'={$rotation.Angle=[Math]::Sin($phase*0.7)*8; $shift.Y=[Math]::Sin($phase)*4}
 'gamerwin'={$shift.Y=-[Math]::Abs([Math]::Sin($phase*4))*12; $rotation.Angle=[Math]::Sin($phase*4)*6}
 'hop'={$shift.Y=-[Math]::Abs([Math]::Sin($phase*5))*20; $scale.ScaleY=1-0.06*[Math]::Cos($phase*10)}
 'tiptoe'={$rotation.Angle=[Math]::Sin($phase*5)*4; $shift.Y=-[Math]::Abs([Math]::Sin($phase*5))*5}
 'patrol'={$rotation.Angle=[Math]::Sin($phase*4)*3}
 'zigzag'={$rotation.Angle=[Math]::Cos($phase*2)*15}
 'figure8'={$rotation.Angle=[Math]::Cos($phase)*12}
 'bounce'={$scale.ScaleY=0.94+0.06*[Math]::Cos($phase*4)}
 'rocket'={$scale.ScaleX=$script:direction*0.8; $scale.ScaleY=1.04; $shift.X=[Math]::Sin($phase*30)*2}
 'landing'={$scale.ScaleY=0.88+0.12*(1-[Math]::Exp(-$phase*2)); $scale.ScaleX=$script:direction*(1+0.06*[Math]::Exp(-$phase*2))}
 'stretch'={$scale.ScaleY=0.98+0.06*[Math]::Sin($phase*0.6); $rotation.Angle=[Math]::Sin($phase)*5}
 'bow'={$rotation.Angle=16*[Math]::Sin([Math]::Min([Math]::PI,$phase)); $scale.ScaleY=1-0.12*[Math]::Sin([Math]::Min([Math]::PI,$phase))}
 'giggle'={$shift.Y=-[Math]::Abs([Math]::Sin($phase*10))*6; $rotation.Angle=[Math]::Sin($phase*8)*3}
 'grumpy'={$shift.Y=-[Math]::Abs([Math]::Sin($phase*5))*7; $rotation.Angle=[Math]::Sign([Math]::Sin($phase*5))*4}
 'dizzy'={$rotation.Angle=[Math]::Sin($phase*3)*17; $shift.X=[Math]::Cos($phase*3)*8}
 'scan'={$rotation.Angle=[Math]::Sin($phase)*9; $effect.Text=('[ '+('.'*([int]($phase*3)%5))+' ]')}
 'charge'={$shift.X=[Math]::Sin($phase*32)*[Math]::Min(4.0,$phase); $effect.Text=('CHARGE '+[Math]::Min(100.0,[int]($phase*20))+'%')}
 'spin'={$rotation.Angle=$phase*150; $scale.ScaleX=$script:direction*0.52; $scale.ScaleY=0.52}
 'heartbeat'={$scale.ScaleY=1+0.025*[Math]::Sin($phase*8); $effect.Opacity=0.5+0.5*[Math]::Abs([Math]::Sin($phase*4))}
 'rain'={$effect.Text=if(([int]($phase*3)%2) -eq 0){'1 0 1 0'}else{'0 1 0 1'}; $rotation.Angle=-7}
 'celebrate'={$shift.Y=-[Math]::Abs([Math]::Sin($phase*5))*14; $rotation.Angle=[Math]::Sin($phase*5)*9}
 'sneak'={$scale.ScaleY=0.85; $rotation.Angle=$script:direction*9; $sprite.Opacity=0.75}
 'meditate'={$shift.Y=[Math]::Sin($phase*0.8)*9; $scale.ScaleY=0.98+0.015*[Math]::Sin($phase*1.2)}
 'shiver'={$shift.X=[Math]::Sin($phase*40)*3; $rotation.Angle=[Math]::Sin($phase*30)*2}
 'pendulum'={$rotation.Angle=[Math]::Sin($phase*1.8)*15; $shift.X=[Math]::Sin($phase*1.8)*8}
 'jelly'={$scale.ScaleY=0.94+0.06*[Math]::Sin($phase*8); $scale.ScaleX=$script:direction*(1-0.05*[Math]::Sin($phase*8))}
 'wave'={$rotation.Angle=[Math]::Sin($phase*7)*6; $shift.Y=[Math]::Sin($phase*3)*5}
 'smug'={$rotation.Angle=-6+[Math]::Sin($phase*1.5)*2; $scale.ScaleY=1+[Math]::Sin($phase*2)*0.012}
 'tumble'={$rotation.Angle=($phase*200)%360; $scale.ScaleX=$script:direction*0.55; $scale.ScaleY=0.55; $shift.Y=[Math]::Sin($phase*4)*10}
 'peek'={$rotation.Angle=$script:direction*12; $shift.X=[Math]::Sin($phase*2.5)*7}
 'dash'={$rotation.Angle=-$script:direction*15; $scale.ScaleX=$script:direction*1.06; $scale.ScaleY=0.92}
 'orbit'={$rotation.Angle=[Math]::Sin($phase*2)*15}
 'dance'={$rotation.Angle=-3+[Math]::Sin($phase*1.3)*2; $scale.ScaleY=1+[Math]::Sin($phase*1.5)*0.008; $shift.Y=[Math]::Sin($phase*1.6)*3}
 'glitch'={if($rng.NextDouble() -lt 0.28){$shift.X=$rng.Next(-9,10); $sprite.Opacity=0.55; $scale.ScaleX=$script:direction*1.08}}
 'nap'={$rotation.Angle=[Math]::Sin($t*0.8)*2; $scale.ScaleY=0.94+[Math]::Sin($t*1.6)*0.02; $shift.Y=0; $sprite.Opacity=0.8}
 'float'={$rotation.Angle=[Math]::Sin($t*1.7)*4; $scale.ScaleY=1+[Math]::Sin($t*2)*0.015}
 'arrive'={$sprite.Opacity=[Math]::Min(1.0,$phase/1.6); $shift.X=[Math]::Sin($phase*18)*4*(1-[Math]::Min(1.0,$phase/1.6))}
 'dock'={$shift.Y=[Math]::Sin($phase)*1.5; $rotation.Angle=0}
 'glare'={$rotation.Angle=-$script:direction*4; $shift.Y=0}
 'sabotage'={
  $sabotagePanel.Visibility='Visible'
  $sabotageText.Text=if($script:state -eq 'sabotage'){'SIGNAL LOST  //  SIMULATION'}else{'SIGNAL RESTORED  //  SIMULATION'}
 }
 'filescan'={
  $rotation.Angle=-$script:direction*5; $shift.Y=[Math]::Sin($phase*4)*2
  $progress=[Math]::Min(1.0,$phase/3.6); $bars=[int]($progress*10)
  $sabotagePanel.Visibility='Visible'
  $sabotageText.Text=('FILE SCAN // SIMULATION'+[Environment]::NewLine+$script:scanName+[Environment]::NewLine+'['+('#'*$bars)+('.'*(10-$bars))+'] '+[int]($progress*100)+'%')
 }
 'scanverdict'={
  $rotation.Angle=-6+[Math]::Sin($phase*1.5)*2
  $sabotagePanel.Visibility='Visible'
  $sabotageText.Text=('THREAT LEVEL '+$script:scanScore+'/10  //  SIMULATION'+[Environment]::NewLine+$script:scanName)
 }
 'windowperch'={if($script:perchArrived){$shift.Y=[Math]::Sin($phase*1.5)*1.2; $rotation.Angle=0}}
 'thrown'={
  if($script:shakeAngry){Set-Pose 20}
  $rotation.Angle=[Math]::Max(-28.0,[Math]::Min(28.0,-$script:throwVX/45)); $shift.Y=0
  $scale.ScaleY=1-0.12*$script:throwSquash; $scale.ScaleX=$script:direction*(1+0.08*$script:throwSquash)
 }
}

# Throws and falls
function Start-Throw([double]$vx,[double]$vy,[string]$reason){
 $script:throwVX=$vx; $script:throwVY=$vy; $script:throwSquash=0.0; $script:throwReason=$reason
 $script:routine.Clear(); Begin-State 'thrown'
}
function Finish-Throw {
 $rotation.Angle=0
 if($script:shakeAngry){
  $script:shakeAngry=$false
  Begin-State 'shaken'; $script:caughtCooldown=$script:until+1
 }else{
  Begin-State 'landing'
  if($script:throwReason -eq 'fell'){[void](Show-Remark 'fell')}
  else{
   $count=Add-Memory 'throws'
   if(Test-Milestone $count @(3,10,25)){[void](Show-Remark 'memorythrows' -Value $count)}else{[void](Show-Remark 'thrown')}
  }
 }
}
function Get-ReleaseVelocity {
 $samples=$script:dragSamples
 if($samples.Count -lt 2){return $null}
 $now=$clock.Elapsed.TotalSeconds; $lastSample=$samples[$samples.Count-1]
 if($now-$lastSample[0] -gt 0.09){return $null} # she was held still before release
 $first=$samples[0]
 for($i=$samples.Count-2;$i -ge 0;$i--){$first=$samples[$i]; if($lastSample[0]-$samples[$i][0] -ge 0.06){break}}
 $span=$lastSample[0]-$first[0]; if($span -le 0.005){return $null}
 return @((($lastSample[1]-$first[1])/$span),(($lastSample[2]-$first[2])/$span))
}

# Visibility: hidden manually from the tray, or automatically for fullscreen apps.
function Apply-Visibility {
 $hidden=$script:hiddenManual -or $script:hiddenFullscreen
 if($hidden -eq $script:isHidden){return}
 $script:isHidden=$hidden
 if($hidden){
  Stop-Voice; $sfxPlayer.Close(); $menu.IsOpen=$false
  if($cursorFx.IsVisible){$cursorFx.Hide()}
  $wall.Visibility='Collapsed'; $bubble.Visibility='Collapsed'
  $w.Topmost=$false; $w.Opacity=0; $w.IsHitTestVisible=$false
 }else{
  $w.Opacity=1; $w.IsHitTestVisible=$true; $w.Topmost=$true
  Clamp-Pet
  if(-not $script:busyMode){Begin-State 'arrive'}
 }
}
function Set-Hidden([bool]$hidden){
 $script:hiddenManual=$hidden
 if(-not $hidden -and $script:hiddenFullscreen){$script:hiddenFullscreen=$false; $script:fullscreenOverride=$true}
 Apply-Visibility
}
function Update-FullscreenState {
 if($script:fullscreenMode -eq 'ignore' -or $script:exiting){
  if($script:hiddenFullscreen){$script:hiddenFullscreen=$false; Apply-Visibility}
  if($script:busyByFullscreen){$script:busyByFullscreen=$false; if($script:busyMode){Set-BusyMode $false -Auto}}
  return
 }
 $blocked=$false
 try{$blocked=[CapriDesktop]::FullscreenBlocks($script:petHandle)}catch{}
 if(-not $blocked){$script:fullscreenOverride=$false}
 if($script:fullscreenMode -eq 'hide'){
  $want=$blocked -and -not $script:fullscreenOverride
  if($want -ne $script:hiddenFullscreen){$script:hiddenFullscreen=$want; Apply-Visibility}
 }else{
  if($blocked -and -not $script:busyMode -and -not $script:fullscreenOverride){$script:busyByFullscreen=$true; Set-BusyMode $true -Auto}
  elseif(-not $blocked -and $script:busyByFullscreen){$script:busyByFullscreen=$false; if($script:busyMode){Set-BusyMode $false -Auto}}
 }
}
function Set-FullscreenMode([string]$mode){
 $script:fullscreenMode=$mode; $script:fullscreenOverride=$false
 if($mode -ne 'hide' -and $script:hiddenFullscreen){$script:hiddenFullscreen=$false; Apply-Visibility}
 if($mode -ne 'busy' -and $script:busyByFullscreen){$script:busyByFullscreen=$false; if($script:busyMode){Set-BusyMode $false -Auto}}
 $script:nextFullscreenCheck=0
 Save-VoiceSettings
}

# Away from the computer: sleep while you're gone, greet you when you're back.
function Wake-FromAway {
 $now=$clock.Elapsed.TotalSeconds
 $script:idleRest='awake'; $script:sleepReason=''; $script:lastInteraction=$now
 $script:mood='curious'; $script:nextMood=$now+60; $script:caughtCooldown=$now+4
 $script:activeSince=$now
 [void](Add-Memory 'returns')
 Begin-State 'welcomeback'
 $kind='returned'
 if($script:timeOfDay){$hour=(Get-Date).Hour; if($hour -ge 5 -and $hour -lt 11){$kind='morning'}elseif($hour -lt 5){$kind='latenight'}}
 [void](Show-Remark $kind)
 $script:nextRemark=$now+40
 Schedule-Yap
}

function Set-BusyMode([bool]$on,[switch]$Auto){
 # Turning busy off by hand during a fullscreen app keeps it off until that app closes.
 if(-not $Auto -and -not $on -and $script:busyByFullscreen){$script:fullscreenOverride=$true}
 $script:busyMode=$on; Register-Interaction
 if($on){
  Stop-Voice; $sfxPlayer.Close(); $script:nextRetry=[double]::PositiveInfinity
  $a=Get-WorkArea; $w.Left=$a.Right-$w.Width-12; $w.Top=$a.Bottom-$w.Height
  $script:busyUntil=0.0; $script:busyBag.Clear()
  $script:activeSince=$clock.Elapsed.TotalSeconds # a focus session starts the break timer
  Begin-State 'taskbar'
 }else{$script:busyByFullscreen=$false; Schedule-Yap; Begin-State 'stretch'}
}
function Set-Paused([bool]$on){
 $script:paused=$on
 if($on){Stop-Voice; $sfxPlayer.Close(); Show-Status 'chilling.'}
}
function Set-Muted([bool]$on){
 $script:muted=$on; Stop-Voice; if($on){$sfxPlayer.Close()}; Save-VoiceSettings
}
$script:busyBag=[Collections.Generic.Queue[string]]::new(); $script:busyActivity='gaming'; $script:busyUntil=0.0; $script:busyStart=0.0
$script:pendingAction=''; $script:lastFxError=-60.0
function Prepare-ManualAction {
 Register-Interaction
 if($script:busyByFullscreen){$script:fullscreenOverride=$true}
 $script:busyMode=$false; $script:busyByFullscreen=$false
 $script:paused=$false; $script:mouseDown=$false; $script:hoverLatched=$true
 $script:caughtCooldown=$clock.Elapsed.TotalSeconds+3
 $script:nextRetry=[double]::PositiveInfinity
 $script:nextYap=$clock.Elapsed.TotalSeconds+15
 $script:nextRemark=$clock.Elapsed.TotalSeconds+15
}
$script:nextPet=0.0
function Invoke-HeadPat {
 if($clock.Elapsed.TotalSeconds -lt $script:nextPet){return}
 Prepare-ManualAction; $script:routine.Clear(); Begin-State 'headpat'
 $script:caughtCooldown=$script:until+1
 $script:nextPet=$clock.Elapsed.TotalSeconds+2
 $count=Add-Memory 'pets'
 if(Test-Milestone $count @(3,10,25,50,100)){[void](Show-Remark 'memorypets' -Value $count)}else{[void](Show-Remark 'pet')}
}
function Get-LocalCursor {
 $screen=[CapriCursor]::ReadPosition()
 return $w.PointFromScreen([Windows.Point]::new([double]$screen.X,[double]$screen.Y))
}
function Test-CursorContact {
 $point=Get-LocalCursor
 $bounds=$sprite.TransformToAncestor($w).TransformBounds([Windows.Rect]::new(0,0,$sprite.ActualWidth,$sprite.ActualHeight))
 $rx=[Math]::Max($bounds.Left-$point.X,[Math]::Max(0.0,$point.X-$bounds.Right))
 $ry=[Math]::Max($bounds.Top-$point.Y,[Math]::Max(0.0,$point.Y-$bounds.Bottom))
 return ([Math]::Sqrt($rx*$rx+$ry*$ry) -lt 32)
}
function Finish-CursorAttack {
 # Only a nearby hit can move the pointer, never while a mouse button is held.
 $point=(Get-LocalCursor)
 $dx=$point.X-$w.Width/2
 $near=$script:attackContact -or (Test-CursorContact)
 if($near -and $script:chase -and -not $script:busyMode -and $script:cursorKnockback -and $clock.Elapsed.TotalSeconds -ge $script:nextCursorHit -and
    [Windows.Input.Mouse]::LeftButton -eq 'Released' -and [Windows.Input.Mouse]::RightButton -eq 'Released' -and [Windows.Input.Mouse]::MiddleButton -eq 'Released'){
  $push=if($dx -lt 0){-75}else{75}
  $moved=[CapriCursor]::Nudge($push,-20)
  if($moved){$script:nextCursorHit=$clock.Elapsed.TotalSeconds+20}
  Add-Content -LiteralPath (Join-Path $script:AppRoot 'cursor-error.log') -Value ((Get-Date -Format s)+' attack: '+[CapriCursor]::LastResult) -ErrorAction SilentlyContinue
  # Do not interpret our own pointer movement as user interaction.
  $script:cursorLastX=$null; $script:cursorLastY=$null
 }
 if($near){
  $script:rivalWins++; $script:escapeStreak=0; Save-VoiceSettings
  $script:mood='smug'; $script:rivalRetries=0; $script:nextRetry=[double]::PositiveInfinity
  Begin-State 'impact'
 }else{
  $script:rivalEscapes++; $script:escapeStreak=[Math]::Min(5.0,$script:escapeStreak+1); Save-VoiceSettings
  $script:mood='irritated'
  if($script:rivalRetries -lt 1){$script:nextRetry=$clock.Elapsed.TotalSeconds+[Math]::Max(15.0,30-$script:escapeStreak*3)}else{$script:nextRetry=[double]::PositiveInfinity; $script:rivalRetries=0}
  Begin-State 'rivalglare'
 }
 $script:nextMood=$clock.Elapsed.TotalSeconds+60
}
$script:travelFxUntil=0.0; $script:travelFxDirection=1.0
function Update-Blackwall {
 $travelling=$clock.Elapsed.TotalSeconds -lt $script:travelFxUntil
 $tags=$script:spec.tags
 if(-not $script:blackwallEnabled -or $script:isHidden -or $script:paused -or $script:dragging -or $script:busyMode -or $menu.IsOpen -or (-not $travelling -and -not $tags.Contains('blackwall'))){
  if($wall.Visibility -ne 'Collapsed'){$wall.Visibility='Collapsed'}
  return
 }
 $wall.Visibility='Visible'
 $elapsed=$clock.Elapsed.TotalSeconds-$script:stateStart
 $strong=$tags.Contains('strong')
 $wall.Opacity=if($strong){0.95}else{0.3}
 $ww=[Math]::Max(1.0,$wall.ActualWidth); $hh=[Math]::Max(1.0,$wall.ActualHeight)
 $trail=$travelling -and -not $strong
 if($script:fxNative){[CapriFx]::UpdateBars($wallBars,$ww,$hh,$elapsed,$trail,$script:travelFxDirection); return}
 for($i=0;$i -lt $wallBars.Count;$i++){
  $bar=$wallBars[$i]; $bar.Width=$ww*(0.12+0.025*($i%5))
  [Windows.Controls.Canvas]::SetLeft($bar,($ww-$bar.Width)*(0.5+0.45*[Math]::Sin($i*2.3+$elapsed*2)))
  [Windows.Controls.Canvas]::SetTop($bar,$hh*(0.12+0.075*$i)+[Math]::Sin($elapsed*3+$i)*5)
  if($trail){
   $bar.Width=$ww*(0.08+0.035*($i%4))
   $tail=if($script:travelFxDirection -gt 0){0.08}else{0.7}
   [Windows.Controls.Canvas]::SetLeft($bar,$ww*($tail+0.08*[Math]::Sin($elapsed*9+$i)))
   $bar.Opacity=0.4+0.6*[Math]::Abs([Math]::Sin($elapsed*8+$i*1.7))
  }else{$bar.Opacity=1}
 }
}
function Set-PetSize([double]$percent){
 $script:petSize=[Math]::Max(65.0,[Math]::Min(160.0,$percent))
 $factor=$script:petSize/100
 $oldBottom=$w.Top+$w.Height; $oldCenter=$w.Left+$w.Width/2
 $w.Width=260*$factor; $w.Height=130+300*$factor
 if(-not [double]::IsNaN($oldBottom)){$w.Top=$oldBottom-$w.Height}
 if(-not [double]::IsNaN($oldCenter)){$w.Left=$oldCenter-$w.Width/2}
 Clamp-Pet
}
function Invoke-Swat {
 Prepare-ManualAction
 $now=$clock.Elapsed.TotalSeconds
 if($now -lt $script:swatNext){return}
 $script:swatNext=$now+3; $script:caughtCooldown=$now+3
 $script:paused=$false; Begin-State 'swat'
 $count=Add-Memory 'swats'
 if(Test-Milestone $count @(10,30,60)){[void](Show-Remark 'memoryswats' -Value $count)}
 if($script:sfxEnabled -and -not $script:muted -and -not $script:busyMode){
  $sfxPlayer.Close(); Update-SfxVolume
  $sfxPlayer.Open([Uri]::new((Join-Path $script:AppRoot 'sounds/cartoon-swat.wav'),[UriKind]::Absolute)); $sfxPlayer.Play()
 }
}
# Brings Capri next to the pointer, on whichever monitor it is on.
function Invoke-CallOver {
 Set-Hidden $false
 Prepare-ManualAction; $script:routine.Clear()
 try {
  $cursor=Get-LocalCursor
  $w.Left=$w.Left+$cursor.X-$w.Width/2; $w.Top=$w.Top+$cursor.Y-$w.Height+40
 } catch { }
 Clamp-Pet; Begin-State 'arrive'
}

# Start with Windows: a Startup-folder shortcut that launches without a console window.
$script:startupLink=Join-Path ([Environment]::GetFolderPath('Startup')) 'Capri.lnk'
function Set-StartWithWindows([bool]$on){
 try {
  if($on){
   $shell=New-Object -ComObject WScript.Shell
   $link=$shell.CreateShortcut($script:startupLink)
   if($script:IsExe){$link.TargetPath=$script:LaunchPath; $link.Arguments=''}
   else{
    $link.TargetPath=Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
    $link.Arguments='-NoProfile -STA -ExecutionPolicy Bypass -WindowStyle Hidden -File "'+$script:LaunchPath+'"'
   }
   $link.WorkingDirectory=$script:AppRoot; $link.WindowStyle=7; $link.Description='Capri desktop companion'
   $icon=Join-Path $script:AppRoot 'capri.ico'; if(Test-Path -LiteralPath $icon){$link.IconLocation=$icon}
   $link.Save()
   Show-Status 'I will be here when you log in.'
  }elseif(Test-Path -LiteralPath $script:startupLink){Remove-Item -LiteralPath $script:startupLink}
 } catch { Show-Problem ('Could not change the startup shortcut: '+$_.Exception.Message) }
}
