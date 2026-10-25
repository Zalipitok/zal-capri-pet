# State variables and behaviour data.
# ---------------------------------------------------------------------------
# State
$script:state = 'float'; $script:until = 0.0; $script:messageUntil = 0.0
$script:paused = $false; $script:dragging = $false; $script:chase = $true
$script:targetX = 0.0; $script:targetY = 0.0; $script:direction = 1.0
$script:last = 0.0
$script:nextBlink=3.0; $script:blinkUntil=0.0
$script:cursorLastX=$null; $script:cursorLastY=$null
$script:caughtCooldown=0.0
$script:facialMotion=$true
$script:lastInteraction=0.0
$script:idleSleepAfter=300.0
$script:petSize=100.0
$script:swatNext=0.0
$script:cursorKnockback=$false
$script:blackwallEnabled=$true
$script:nextCursorHit=0.0
$script:idleRest='awake'; $script:sleepReason=''
$script:busyMode=$false; $script:nextRetry=[double]::PositiveInfinity
$script:activityRates=@{attacks=100; wandering=100; resting=100; antics=100; talking=100; rare=100}
$script:rivalEscapes=0; $script:rivalWins=0; $script:escapeStreak=0; $script:nextRare=180.0; $script:teleported=$false
$script:rivalRetries=0; $script:mood='curious'; $script:nextMood=120.0
$script:allowClose=$false; $script:exiting=$false
# New in this version
$script:fullscreenMode='hide'      # hide | busy | ignore
$script:windowPerch=$true
$script:throwPhysics=$true
$script:timeOfDay=$true
$script:breakMinutes=0             # 0 = off
$script:hiddenManual=$false; $script:hiddenFullscreen=$false; $script:isHidden=$false
$script:fullscreenOverride=$false; $script:busyByFullscreen=$false; $script:nextFullscreenCheck=0.0
$script:petHandle=[IntPtr]::Zero; $script:lastAppWindow=0L; $script:nextWindowCheck=0.0
$script:perchHandle=0L; $script:perchOffsetX=0.0; $script:perchArrived=$false
$script:throwVX=0.0; $script:throwVY=0.0; $script:throwSquash=0.0; $script:throwReason=''
$script:dragSamples=[Collections.Generic.List[double[]]]::new()
$script:activeSince=0.0; $script:drinkBreakDay=[DateTime]::MinValue
$script:tickMs=33; $script:nextNoticeAt=0.0
# Newer settings (saved in voice-settings.json)
$script:musicEnabled=$true          # react to whatever is playing
$script:appAware=$false             # opt-in: react to the app in front
$script:hotkeyEnabled=$true         # global summon hotkey
$script:scanName=''; $script:scanScore=0
# Tunable numbers. Edit tuning.json (or Debug and help > Reload tuning.json) instead of the code.
$script:tuning=[ordered]@{
 throwSpeed=800.0        # release speed (WPF px/s) that counts as a throw
 gravity=2600.0          # px/s^2 while thrown or falling
 floorBounce=0.38        # share of speed kept when bouncing off the floor
 wallBounce=0.55         # share of speed kept when bouncing off a screen edge
 perchChance=0.6         # chance a taskbar perch becomes a window perch
 perchSink=0.0           # px to lower her onto a title bar (raise if she floats above it)
 roamChance=0.2          # chance a wander goes to another monitor
 crossfadeSeconds=0.12   # pose-change fade; 0 = off
 mouthHalf=0.25          # how readily her mouth half-opens while she speaks (0-1, lower = more)
 mouthWide=0.6           # how readily it opens wide (0-1, lower = more)
 mouthDelay=0.06         # seconds the mouth waits for the sound to start
 musicBob=1.0            # strength of her swaying to music (poses without music frames)
 musicBpm=108.0          # head-bob tempo with headphones on (Windows does not report a song's tempo)
 songRemarkMinutes=3.0   # at least this long between comments on songs
 appRemarkMinutes=2.0    # at least this long between comments on apps
 hotkey='Ctrl+Alt+C'     # summon / hide shortcut
 perMonitorDpi=0.0       # experimental: 1 = sharp on mixed-scale monitors (applies at next start)
}
$script:tuningPath=Join-Path $script:AppRoot 'tuning.json'
function Import-Tuning {
 if(-not (Test-Path -LiteralPath $script:tuningPath)){return}
 try {
  $saved=Get-Content -LiteralPath $script:tuningPath -Raw -Encoding UTF8 | ConvertFrom-Json
  foreach($key in @($script:tuning.Keys)){
   $value=$saved.$key; if($null -eq $value){continue}
   if($script:tuning[$key] -is [string]){$script:tuning[$key]=[string]$value}else{$script:tuning[$key]=[double]$value}
  }
 } catch { $script:tuningError=$_.Exception.Message }
}
Import-Tuning
$sabotagePanel=$w.FindName('SabotagePanel'); $sabotageText=$w.FindName('SabotageText')

# ---------------------------------------------------------------------------
# Behaviour data (behaviours.json): every state's pose, timing, movement and follow-up.
$behaviourDoc=Get-Content -LiteralPath (Join-Path $script:AppRoot 'behaviours.json') -Raw -Encoding UTF8 | ConvertFrom-Json
if($null -eq $behaviourDoc.states){throw 'behaviours.json is from an older Capri version. Use the behaviours.json that came with this Capri.ps1.'}
function ConvertTo-Spec($entry){
 $tags=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
 foreach($tag in @($entry.tags)){if($tag){[void]$tags.Add([string]$tag)}}
 $timeline=$null; $cycle=0.0
 if($null -ne $entry.poses){
  $timeline=@(foreach($step in $entry.poses){
   $length=if(@($step).Count -gt 1){[double]$step[1]}else{[double]::PositiveInfinity}
   $cycle+=$length
   ,@([int]$step[0],$length)
  })
 }
 $seconds=if($null -eq $entry.seconds){@(5,10)}elseif($entry.seconds -is [array]){@([int]$entry.seconds[0],[int]$entry.seconds[1])}else{[double]$entry.seconds}
 $id=[string]$entry.id
 return [pscustomobject]@{
  id=$id; title=[string]$entry.title; menu=[string]$entry.menu
  auto=[bool]$entry.auto; rare=[bool]$entry.rare
  category=if($entry.category){[string]$entry.category}else{'antics'}
  pose=if($null -ne $entry.pose){[int]$entry.pose}else{$null}
  timeline=$timeline; loop=[bool]$entry.loop; cycle=$cycle
  seconds=$seconds; effect=[string]$entry.effect
  target=if($entry.target){[string]$entry.target}else{'random'}
  path=[string]$entry.path
  speed=if($null -ne $entry.speed){[double]$entry.speed}else{$null}
  clampTarget=[bool]$entry.clampTarget
  next=[string]$entry.next; nextNoChase=[string]$entry.nextNoChase; setMood=[string]$entry.setMood
  motion=if($entry.motion){[string]$entry.motion}else{$id}
  remark=if($entry.remark){[string]$entry.remark}else{'idle'}
  tags=$tags
 }
}
$script:specs=[ordered]@{}
foreach($entry in @($behaviourDoc.states)){$spec=ConvertTo-Spec $entry; $script:specs[$spec.id]=$spec}
$script:defaultSpec=ConvertTo-Spec ([pscustomobject]@{id='?'; pose=0})
$script:spec=$script:defaultSpec
$script:routines=[ordered]@{}
foreach($property in $behaviourDoc.routines.PSObject.Properties){$script:routines[$property.Name]=@($property.Value | ForEach-Object {[string]$_})}
function Get-Spec([string]$name){if($script:specs.Contains($name)){return $script:specs[$name]}; return $script:defaultSpec}
function Get-SpecPose($spec,[double]$phase){
 if($null -eq $spec.timeline){return $spec.pose}
 $p=$phase
 if($spec.loop -and $spec.cycle -gt 0 -and -not [double]::IsInfinity($spec.cycle)){$p=$phase % $spec.cycle}
 foreach($step in $spec.timeline){if($p -lt $step[1]){return $step[0]}; $p-=$step[1]}
 return $spec.timeline[-1][0]
}
