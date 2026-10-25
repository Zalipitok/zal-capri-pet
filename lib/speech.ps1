# Notices, remarks, settings and voice playback.
# Console output only when there is a console: in the .exe build, Write-Host would become a pop-up.
function Write-CapriLog([string]$text,[switch]$Problem){
 if($script:IsExe){return}
 if($Problem){Write-Warning ('Capri: '+$text)}else{Write-Host ('Capri: '+$text)}
}
# ---------------------------------------------------------------------------
# Notices: operational messages show in her bubble; problems also show as a tray notification.
function Show-Status([string]$text,[double]$seconds=3.5){
 Write-CapriLog $text
 if($script:isHidden -or $script:voiceBusy){return}
 $words.Text=$text; $bubble.Visibility='Visible'
 $script:messageUntil=$clock.Elapsed.TotalSeconds+$seconds
}
function Show-Problem([string]$text){
 Write-CapriLog $text -Problem
 if(-not $script:voiceBusy -and -not $script:isHidden){$words.Text=$text; $bubble.Visibility='Visible'; $script:messageUntil=$clock.Elapsed.TotalSeconds+6}
 if($null -ne $script:tray -and $clock.Elapsed.TotalSeconds -ge $script:nextNoticeAt){
  $script:nextNoticeAt=$clock.Elapsed.TotalSeconds+30
  try{$script:tray.ShowBalloonTip(6000,'Capri',$text,[Windows.Forms.ToolTipIcon]::Warning)}catch{}
 }
}

function Register-Interaction {
 $script:lastInteraction=$clock.Elapsed.TotalSeconds
 if($script:idleRest -ne 'awake'){
  $script:idleRest='awake'; $script:sleepReason=''
  $script:mood='irritated'; $script:nextMood=$script:lastInteraction+45
  Begin-State 'wakeglare'
  $script:caughtCooldown=$script:lastInteraction+5
  $script:nextRemark=$script:lastInteraction+30
  Schedule-Yap
 }
}
$script:subtitleUntil = 0.0
$script:subtitles = $true
$script:activeCaption = ''

# Original character-inspired text, separate from the recorded-dialogue captions.
$script:remarksEnabled=$true
$script:lastRemark=''
$script:remarkBags=@{}
$script:nextRemark=22.0
$script:nextHoldRemark=0.0
$script:heldLongEnough=$false
$remarkPack=Get-Content -LiteralPath (Join-Path $script:AppRoot 'remarks.json') -Raw -Encoding UTF8 | ConvertFrom-Json
# -Force is for reminders the user opted into: they show in Busy mode and with remarks off.
function Show-Remark([string]$kind,[switch]$Force,$Value=$null){
 if(-not $Force -and ($script:busyMode -or -not $script:remarksEnabled)){return $false}
 if($script:paused -or $script:isHidden -or $menu.IsOpen){return $false}
 $now=$clock.Elapsed.TotalSeconds
 # Speech always wins, even when its subtitles have been switched off.
 if($script:voiceBusy -or $now -lt $script:subtitleUntil){return $false}
 if(-not $script:remarkBags.ContainsKey($kind)){$script:remarkBags[$kind]=[Collections.Generic.Queue[string]]::new()}
 $bag=$script:remarkBags[$kind]
 if($bag.Count -eq 0){
  $lines=@($remarkPack.$kind | Where-Object {$_})
  if($lines.Count -eq 0){return $false}
  Add-ShuffledItems $bag $lines
  if($bag.Count -gt 1 -and $bag.Peek() -eq $script:lastRemark){$bag.Enqueue($bag.Dequeue())}
 }
 $script:lastRemark=$bag.Dequeue()
 $text=$script:lastRemark
 if($null -ne $Value){try{$text=[string]::Format($text,$Value)}catch{}}
 $words.Text=$text; $bubble.Visibility='Visible'
 $script:messageUntil=$now+4.5
 $script:nextRemark=$now+35+$rng.NextDouble()*30
 return $true
}

# ---------------------------------------------------------------------------
# Local voice packs. Missing audio never falls back to another language.
$script:language = 'en'; $script:muted = $false; $script:volume = 0.5; $script:liveVolume=$false
$script:voiceNext = 0.0; $script:voiceBusy = $false
$script:lastClip = ''
$script:voiceLastPlayed=@{}
$script:chatter = 'normal'
$script:approvedVoices=@()
$script:nextYap = 12.0; $script:stateStart = 0.0
$script:anchorX = 0.0; $script:anchorY = 0.0
# Windows PowerShell 5.1 emits a JSON array as a single pipeline object.
# Assign first, then explicitly enumerate entries instead of wrapping that pipeline.
$decodedPack = Get-Content -LiteralPath (Join-Path $script:AppRoot 'voice-pack.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$voicePack = @(foreach($entry in $decodedPack) {
 if($entry.file -isnot [string]) { throw 'Invalid voice pack: file must be a single path.' }
 $entry
})
$script:approvedVoices=@($voicePack | Where-Object {-not $_.needs_review} | ForEach-Object {$_.language+':'+$_.id})
$settingsPath = Join-Path $script:AppRoot 'voice-settings.json'
try {
 if(Test-Path -LiteralPath $settingsPath){
  $saved = Get-Content -LiteralPath $settingsPath -Raw | ConvertFrom-Json
  foreach($key in @('attacks','wandering','resting','antics','talking','rare')){
   if($null -ne $saved.activityRates.$key){$script:activityRates[$key]=[Math]::Max(0.0,[Math]::Min(200.0,[int]$saved.activityRates.$key))}
  }
  if($null -ne $saved.rivalEscapes){$script:rivalEscapes=[Math]::Max(0.0,[int]$saved.rivalEscapes)}
  if($null -ne $saved.rivalWins){$script:rivalWins=[Math]::Max(0.0,[int]$saved.rivalWins)}
  if($null -ne $saved.escapeStreak){$script:escapeStreak=[Math]::Max(0.0,[Math]::Min(5.0,[int]$saved.escapeStreak))}
  if($null -ne $saved.petSize){$script:petSize=[Math]::Max(65.0,[Math]::Min(160.0,[double]$saved.petSize))}
  foreach($setting in @('facialMotion','chase','blackwallEnabled','sfxEnabled','windowPerch','throwPhysics','timeOfDay','remarksEnabled','subtitles','muted','smoothPoses','musicEnabled','appAware','hotkeyEnabled')){
   if($saved.$setting -is [bool]){Set-Variable -Name $setting -Scope Script -Value $saved.$setting}
  }
  if($saved.idleSleepAfter -in @(0,120,300,600)){$script:idleSleepAfter=[double]$saved.idleSleepAfter}
  if($saved.language -in @('en','ja')){$script:language=$saved.language}
  if($saved.chatter -in @('off','quiet','normal','chatty')){$script:chatter=$saved.chatter}
  if($saved.fullscreenMode -in @('hide','busy','ignore')){$script:fullscreenMode=[string]$saved.fullscreenMode}
  if($saved.breakMinutes -in @(0,25,50,90)){$script:breakMinutes=[int]$saved.breakMinutes}
  if($saved.voicePackRevision -in @(1,2,3) -and $null -ne $saved.approvedVoices){$script:approvedVoices=@($saved.approvedVoices | Where-Object {$_ -is [string]})}
  if($saved.voicePackRevision -notin @(2,3)){
   foreach($key in @('ja:your_turn','ja:had_fun')){if($script:approvedVoices -notcontains $key){$script:approvedVoices+=@($key)}}
  }
  if($null -ne $saved.volume){$script:volume=[Math]::Max(0.0,[Math]::Min(1.0,[double]$saved.volume))}
 }
} catch { } # Invalid settings reset to defaults.
function Save-VoiceSettings {
 if($script:SelfTestMode){return} # the self-test never overwrites your settings
 try { @{activityRates=$script:activityRates; rivalEscapes=$script:rivalEscapes; rivalWins=$script:rivalWins; escapeStreak=$script:escapeStreak; facialMotion=$script:facialMotion; chase=$script:chase; blackwallEnabled=$script:blackwallEnabled; sfxEnabled=$script:sfxEnabled; idleSleepAfter=$script:idleSleepAfter; petSize=$script:petSize; language=$script:language; muted=$script:muted; volume=$script:volume; chatter=$script:chatter; subtitles=$script:subtitles; approvedVoices=@($script:approvedVoices); voicePackRevision=3; remarksEnabled=$script:remarksEnabled; fullscreenMode=$script:fullscreenMode; windowPerch=$script:windowPerch; throwPhysics=$script:throwPhysics; timeOfDay=$script:timeOfDay; breakMinutes=$script:breakMinutes; smoothPoses=$script:smoothPoses; musicEnabled=$script:musicEnabled; appAware=$script:appAware; hotkeyEnabled=$script:hotkeyEnabled} | ConvertTo-Json | Set-Content -LiteralPath $settingsPath -Encoding UTF8 }
 catch { Show-Problem 'Settings could not be saved.' }
}
# Volume is applied to Capri's audio session, so changes reach a clip that is already playing.
function Update-SfxVolume {
 $sfxPlayer.Volume=if($script:liveVolume -and $script:volume -gt 0){[Math]::Min(1.0,0.5/$script:volume)}else{[Math]::Min(0.5,$script:volume)}
}
function Set-Volume([double]$level){
 $script:volume=[Math]::Max(0.0,[Math]::Min(1.0,$level))
 $script:liveVolume=$false
 try{$script:liveVolume=[CapriDesktop]::SetAppVolume($script:volume)}catch{}
 Update-SfxVolume
}
Set-Volume $script:volume

$script:speechPose=-1; $script:speechMotion='still'; $script:speechTempo=1.0; $script:speechStarted=0.0
$script:speechVisible=$false; $script:speechRestorePose=0
function End-SpeechPose {
 if($script:speechVisible -and $script:poseIndex -eq $script:speechPose){Set-Pose $script:speechRestorePose}
 $script:speechVisible=$false; $script:speechPose=-1
}
function Apply-SpeechPose {
 if(-not $script:speechVisible -or -not $script:voiceBusy -or $script:speechPose -lt 0){return}
 if($script:dragging -or $script:mouseDown -or $script:spec.tags.Contains('noSpeechPose')){return}
 Set-Pose $script:speechPose
 $v=($clock.Elapsed.TotalSeconds-$script:speechStarted)*$script:speechTempo
 # Replace unrelated spin/squash transforms while this line is speaking.
 $scale.ScaleX=$script:direction; $scale.ScaleY=1
 $rotation.Angle=0; $shift.X=0; $shift.Y=0; $sprite.Opacity=1
 switch($script:speechMotion){
  'beckon' {$rotation.Angle=$script:direction*(2+[Math]::Sin($v*4)*2)}
  'tilt' {$rotation.Angle=$script:direction*4; $shift.Y=[Math]::Sin($v*2)*1.5}
  'nod' {$shift.Y=[Math]::Sin($v*5)*2}
  'laugh' {$shift.Y=-[Math]::Abs([Math]::Sin($v*11))*4; $rotation.Angle=[Math]::Sin($v*7)*2}
 }
}
$script:voiceJob=$null; $script:lastAudioError=''
function Report-AudioError([string]$detail) {
 $script:lastAudioError=$detail
 try { Add-Content -LiteralPath (Join-Path $script:AppRoot 'audio-error.log') -Value ((Get-Date -Format o)+' '+$detail) -Encoding UTF8 } catch { }
 Show-Problem ('Audio problem: '+$detail)
}
function Stop-Voice {
 if($null -ne $script:voiceJob){$script:voiceJob.Stop(); $script:voiceJob=$null}
 $script:voiceBusy=$false; End-SpeechPose
 $script:subtitleUntil=0; $script:messageUntil=0; $bubble.Visibility='Collapsed'
}
function Schedule-Yap {
 $seconds = switch($script:chatter) { 'quiet' {$rng.Next(100,181)}
 'chatty' {$rng.Next(18,36)}
 default {$rng.Next(45,91)} }
 if($script:activityRates.talking -le 0){$script:nextYap=[double]::PositiveInfinity}else{$script:nextYap=$clock.Elapsed.TotalSeconds+$seconds*100.0/$script:activityRates.talking}
}
function Play-Voice([string]$eventName, [bool]$preview=$false, [string]$clipId='') {
 if(($script:busyMode -or $script:isHidden -or $script:voiceChooserOpen -or $script:activityRates.talking -le 0) -and -not $preview){return}
 if($script:muted){if($preview){Show-Status 'Voice is muted.'}; return}
 $now=$clock.Elapsed.TotalSeconds
 # An explicit preview replaces current speech; automatic chatter never interrupts.
 if($script:voiceBusy){if($preview){Stop-Voice}else{return}}
 if(-not $preview -and ($script:paused -or $now -lt $script:voiceNext)){return}
 $clips=@($voicePack | Where-Object { $_.language -eq $script:language -and ($eventName -eq 'random' -or $preview -or $_.events -contains $eventName) })
 if(-not $preview -or -not $clipId){$clips=@($clips | Where-Object {($script:approvedVoices -contains ($_.language+':'+$_.id))})}
 if($clipId){$clips=@($clips | Where-Object {$_.id -eq $clipId})}
 if($clips.Count -eq 0){if($preview){Show-Status 'That voice clip was not found.'}; return}
 if(-not $preview){
  # Apply across chatter and reactions, including single-clip event pools.
  $clips=@($clips | Where-Object {
   $key=$_.language+':'+$_.id
   $repeatDelay=if($key -eq 'en:problem'){180}else{90}
   -not $script:voiceLastPlayed.ContainsKey($key) -or $now-$script:voiceLastPlayed[$key] -ge $repeatDelay
  })
  if($clips.Count -eq 0){return}
  # Randomize unseen lines; afterwards choose the least recently played.
  $unheard=@($clips | Where-Object {-not $script:voiceLastPlayed.ContainsKey($_.language+':'+$_.id)})
  if($unheard.Count -gt 0){$clips=$unheard}
  else {
   $clips=@($clips | Sort-Object {$script:voiceLastPlayed[$_.language+':'+$_.id]})
   $clips=@($clips[0])
  }
 }
 $clip=$clips[$rng.Next($clips.Count)]
 try {
  Stop-Voice
  $script:activeCaption=[string]$clip.subtitle
  $audioPath = Join-Path $script:AppRoot ([string]$clip.file)
  if(-not (Test-Path -LiteralPath $audioPath -PathType Leaf)){throw ('Missing clip: '+$audioPath+'. Extract the entire ZIP first.')}
  $audioPath = (Get-Item -LiteralPath $audioPath).FullName
  $script:speechRestorePose=$script:poseIndex
  $script:speechPose=if($null -ne $clip.speech_pose){[int]$clip.speech_pose}else{12}
  $script:speechMotion=[string]$clip.speech_motion
  $script:speechTempo=if($null -ne $clip.speech_tempo){[double]$clip.speech_tempo}else{1.0}
  # All bundled recordings are 16-bit PCM WAV: use native playback directly.
  $bytes=[IO.File]::ReadAllBytes($audioPath)
  if(-not $script:liveVolume){$bytes=[CapriPcmAudio]::WithVolume($bytes,$script:volume)}
  $script:voiceJob=[CapriVoicePlayback]::new($bytes)
  $script:voiceBusy=$true
  $script:speechVisible=$true; $script:speechStarted=$clock.Elapsed.TotalSeconds
  if($script:subtitles){$words.Text=$script:activeCaption; $bubble.Visibility='Visible'; $script:subtitleUntil=[double]::PositiveInfinity; $script:messageUntil=[double]::PositiveInfinity}
  $script:voiceJob.Start()
  $script:lastAudioError=''
  $script:lastClip=$clip.id; $script:voiceNext=$now+8
  $script:voiceLastPlayed[$clip.language+':'+$clip.id]=$now
  Schedule-Yap
 } catch { $detail=$_.Exception.Message; Stop-Voice; Report-AudioError $detail }
}
