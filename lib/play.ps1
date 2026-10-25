# Play: the "catch me" mini-game and unlockables earned through memory.

# --- Catch me --------------------------------------------------------------
# 30 seconds: she darts around her screen, faster with every catch. Click her to score.
$script:game=@{active=$false; score=0; ends=0.0; nextJump=0.0; length=30.0}
function Start-CatchGame {
 Prepare-ManualAction; $script:routine.Clear()
 $now=$clock.Elapsed.TotalSeconds
 $script:game.active=$true; $script:game.score=0
 $script:game.ends=$now+$script:game.length; $script:game.nextJump=$now+0.9
 Begin-State 'catchgame'
 [void](Show-Remark 'gamestart' -Force)
}
function Move-GameTarget {
 $a=Get-WorkArea
 # Somewhere at least a third of the screen away, so she visibly dashes.
 for($try=0;$try -lt 8;$try++){
  $x=$a.Left+$rng.NextDouble()*[Math]::Max(1.0,$a.Width-$w.Width)
  $y=$a.Top+$rng.NextDouble()*[Math]::Max(1.0,$a.Height-$w.Height)
  if([Math]::Abs($x-$w.Left)+[Math]::Abs($y-$w.Top) -gt ($a.Width+$a.Height)/6.0){break}
 }
 $script:targetX=$x; $script:targetY=$y
 $script:game.nextJump=$clock.Elapsed.TotalSeconds+[Math]::Max(0.55,1.7-$script:game.score*0.09)
}
# Called from the mouse handler: returns $true when the click counted as a catch.
function Register-GameCatch {
 if(-not $script:game.active -or $script:state -ne 'catchgame'){return $false}
 $script:game.score++
 $script:game.flash=$clock.Elapsed.TotalSeconds
 if($script:sfxEnabled -and -not $script:muted){
  try{$sfxPlayer.Close(); Update-SfxVolume; $sfxPlayer.Open([Uri]::new((Join-Path $script:AppRoot 'sounds/cartoon-swat.wav'),[UriKind]::Absolute)); $sfxPlayer.Play()}catch{}
 }
 Move-GameTarget
 return $true
}
function Stop-CatchGame {
 $script:game.active=$false
 $score=[int]$script:game.score
 [void](Add-Memory 'games')
 $best=[int]$script:memory.bestCatch
 if($score -gt $best){
  $script:memory.bestCatch=$score; $script:memoryDirty=$true
  Begin-State 'grumpy'
  [void](Show-Remark 'gamenewbest' -Force -Value $score)
 }elseif($score -eq 0){
  Begin-State 'smug'
  [void](Show-Remark 'gamezero' -Force)
 }else{
  Begin-State 'smug'
  [void](Show-Remark 'gameover' -Force -Value @($score,$best))
 }
 Test-Unlocks
}
$paths['catchgame']={
 $now=$clock.Elapsed.TotalSeconds
 if(-not $script:game.active){Choose-State; return}
 if($now -ge $script:game.ends){Stop-CatchGame; return}
 if($now -ge $script:game.nextJump -or [Math]::Abs($script:targetX-$w.Left)+[Math]::Abs($script:targetY-$w.Top) -lt 6){Move-GameTarget}
 $script:until=$now+5 # the game decides when this state ends
 $effect.Text=('CATCH ME   '+$script:game.score+'   '+[int][Math]::Ceiling($script:game.ends-$now)+'s')
}
$motions['catchgame']={
 $rotation.Angle=-$script:direction*12; $scale.ScaleY=0.94
 $hit=$clock.Elapsed.TotalSeconds-[double]$script:game.flash
 if($hit -ge 0 -and $hit -lt 0.25){$sprite.Opacity=0.5+2*$hit; $shift.X=[Math]::Sin($hit*60)*6}
}
$script:game.flash=-10.0

# --- Unlockables -----------------------------------------------------------
# unlocks.json: each entry needs some stats (all must be met) and grants a routine or a set of lines.
$script:unlocks=@()
$script:unlockPools=@()   # remark pools that have been unlocked; mixed into her idle remarks
$script:nextUnlockCheck=20.0
try {
 $unlockPath=Join-Path $script:AppRoot 'unlocks.json'
 if(Test-Path -LiteralPath $unlockPath){$script:unlocks=@(Get-Content -LiteralPath $unlockPath -Raw -Encoding UTF8 | ConvertFrom-Json)}
} catch { $script:unlocksError=$_.Exception.Message }
$script:statNames=@{days='days together'; pets='head pats'; throws='throws'; swats='swats'; shakes='shakes'; returns='welcome-backs'; scans='file scans'; sessions='sessions'; bestCatch='best catch-me score'; games='catch-me games'; catches='cursor catches'}
function Get-Stat([string]$name){
 if($name -eq 'catches'){return [int]$script:rivalWins}
 if($script:memory.Contains($name)){return [int]$script:memory[$name]}
 return 0
}
function Test-UnlockMet($unlock){
 foreach($need in $unlock.need.PSObject.Properties){if((Get-Stat $need.Name) -lt [int]$need.Value){return $false}}
 return $true
}
function Get-UnlockedIds {return @(([string]$script:memory.unlocked) -split ',' | Where-Object {$_})}
# Makes an unlock's content available (at startup for earlier unlocks, or when it is earned).
function Enable-Unlock($unlock){
 if($null -ne $unlock.routine){
  $name=[string]$unlock.title
  if(-not $script:routines.Contains($name)){
   $script:routines[$name]=@($unlock.routine | ForEach-Object {[string]$_} | Where-Object {$script:specs.Contains($_)})
   $script:routineBag.Clear()
   if($null -ne $script:routineMenu){[void](New-MenuEntry $script:routineMenu $name {param($sender,$event) $script:pendingAction='routine:'+[string]$sender.Tag} $null $name)}
  }
 }
 if($unlock.remarks -and $script:unlockPools -notcontains [string]$unlock.remarks){$script:unlockPools+=@([string]$unlock.remarks)}
}
function Test-Unlocks {
 $have=@(Get-UnlockedIds)
 foreach($unlock in $script:unlocks){
  if($have -contains [string]$unlock.id -or -not (Test-UnlockMet $unlock)){continue}
  $have+=@([string]$unlock.id)
  $script:memory.unlocked=($have -join ','); $script:memoryDirty=$true
  Enable-Unlock $unlock
  [void](Show-Remark 'unlocked' -Force -Value ([string]$unlock.title))
  if($null -ne $script:tray){try{$script:tray.ShowBalloonTip(5000,'Capri unlocked something','New: '+[string]$unlock.title,[Windows.Forms.ToolTipIcon]::Info)}catch{}}
  return # one announcement at a time; the next check picks up the rest
 }
}
function Get-UnlockSummary {
 $have=@(Get-UnlockedIds)
 $lines=foreach($unlock in $script:unlocks){
  if($have -contains [string]$unlock.id){'  [x] '+$unlock.title}
  else{
   $hint=@(foreach($need in $unlock.need.PSObject.Properties){''+$need.Value+' '+$(if($script:statNames.ContainsKey($need.Name)){$script:statNames[$need.Name]}else{$need.Name})+' (now '+(Get-Stat $need.Name)+')'}) -join ', '
   '  [ ] ??? - '+$hint
  }
 }
 return 'Unlocked '+$have.Count+' of '+$script:unlocks.Count+":`n"+($lines -join "`n")
}
# Earlier unlocks are available right away.
foreach($unlock in $script:unlocks){if(@(Get-UnlockedIds) -contains [string]$unlock.id){Enable-Unlock $unlock}}
