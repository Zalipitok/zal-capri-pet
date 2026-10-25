# Two Capris: Interact > Invite a friend starts a second Capri (Capri.ps1 -Friend) in different
# colours. They share their positions through a little shared memory (CapriLink) and now and then do
# something together: meet up for a chat, play tag, dance when music plays, or wave.
# The friend saves nothing and leaves when the first Capri quits.
$script:linkOk=$false; $script:linkSlot=if($script:FriendMode){1}else{0}; $script:linkSeq=0
$script:friendPresent=$false; $script:friendSeenSeq=-1; $script:friendInfo=$null; $script:friendEverSeen=$false
$script:nextFriendPublish=0.0; $script:nextFriendPlay=40.0; $script:friendSay=[Collections.Generic.List[object]]::new()
$script:friendMeetX=0.0; $script:friendMeetY=0.0; $script:friendPlaced=$false; $script:friendGoneSince=0.0
try { $script:linkOk=[CapriLink]::Open() } catch { $script:linkError=$_.Exception.Message }
if($script:FriendMode){$script:chatter='quiet'}
$script:friendMessages=@{hello=1; talk=2; chase=3; dance=4; wave=5; bye=6; tagged=7}

function Send-FriendMessage([string]$type,[double]$arg=0){
 $script:linkSeq++; $script:linkMessage=@($script:friendMessages[$type],$arg)
 Publish-Friend $true
}
function Publish-Friend([bool]$now=$false){
 if(-not $script:linkOk -or $script:SelfTestMode){return}
 $available=(Test-CanReact) -and $script:spec.tags.Contains('calm') -and -not $script:voiceBusy -and $script:state -ne 'catchgame'
 $flags=0; if($available){$flags+=1}; if($script:musicPlaying){$flags+=2}
 $message=if($null -ne $script:linkMessage){$script:linkMessage}else{@(0,0)}
 [CapriLink]::Write($script:linkSlot,[double[]]@([CapriLink]::Clock(),($w.Left+$w.Width/2),($w.Top+$w.Height),$w.Width,$w.Height,$flags,$script:linkSeq,$message[0],$message[1],$PID,$script:direction,$w.Left,$w.Top))
}
# The other Capri, as seen in the shared memory (or $null when she isn't around).
function Read-Friend {
 if(-not $script:linkOk){return $null}
 $v=[CapriLink]::Read(1-$script:linkSlot)
 if($v[9] -le 0 -or [CapriLink]::Clock()-$v[0] -gt 4 -or $v[9] -eq $PID){return $null}
 return @{cx=$v[1]; feet=$v[2]; width=$v[3]; height=$v[4]; available=(([int]$v[5]) -band 1) -ne 0; music=(([int]$v[5]) -band 2) -ne 0; seq=[int]$v[6]; type=[int]$v[7]; arg=$v[8]; left=$v[11]; top=$v[12]}
}
function Start-FriendCapri {
 if($script:friendPresent){Show-Status 'She''s already here.'; return}
 try {
  if($script:IsExe){Start-Process -FilePath $script:LaunchPath -ArgumentList '-Friend'}
  else{Start-Process -FilePath (Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe') -ArgumentList @('-NoProfile','-STA','-ExecutionPolicy','Bypass','-WindowStyle','Hidden','-File',('"'+$script:LaunchPath+'"'),'-Friend')}
  Show-Status 'Calling a friend...' 4
 } catch { Show-Problem ('Could not start her friend: '+$_.Exception.Message) }
}
function Stop-FriendCapri {if($script:friendPresent){Send-FriendMessage 'bye'}}
function Show-FriendLine([string]$text){
 if($script:voiceBusy -or $script:isHidden -or -not $text){return}
 $words.Text=$text; $bubble.Visibility='Visible'; $script:messageUntil=$clock.Elapsed.TotalSeconds+4.5; $script:nextRemark=$clock.Elapsed.TotalSeconds+30
}
function Get-FriendPair([int]$index){
 $pairs=@($remarkPack.friendtalk | Where-Object {$_})
 if($pairs.Count -eq 0){return @('','')}
 $parts=([string]$pairs[$index%$pairs.Count]) -split '\|',2
 return @($parts[0].Trim(),$(if($parts.Count -gt 1){$parts[1].Trim()}else{''}))
}
# Where to stand for a chat: my side of the point between us, at the same height as her.
function Set-FriendMeetPoint($friend){
 $myCx=$w.Left+$w.Width/2
 $side=if($myCx -lt $friend.cx){-1.0}elseif($myCx -gt $friend.cx){1.0}elseif($script:FriendMode){1.0}else{-1.0}
 $mid=($myCx+$friend.cx)/2
 $script:friendMeetX=$mid+$side*[Math]::Max(110.0,$w.Width*0.5)-$w.Width/2
 $script:friendMeetY=(($w.Top+$w.Height)+$friend.feet)/2-$w.Height
}
function Start-FriendActivity([string]$type,[double]$arg,$friend){
 Prepare-ManualAction; $script:routine.Clear()
 $now=$clock.Elapsed.TotalSeconds
 switch($type){
  'talk' {
   Set-FriendMeetPoint $friend; Begin-State 'friendmeet'
   $pair=Get-FriendPair ([int][Math]::Floor($arg/2))
   $iSpeakFirst=(([int]$arg)%2 -eq 0) -ne [bool]$script:FriendMode
   $script:friendSay.Add(@($(if($iSpeakFirst){$now+2.2}else{$now+5.4}),$(if($iSpeakFirst){$pair[0]}else{$pair[1]})))
  }
  'chase' {if(($arg -eq 1) -ne [bool]$script:FriendMode){Begin-State 'friendchase'; [void](Show-Remark 'friendchase' -Force)}else{Begin-State 'friendflee'}}
  'dance' {Begin-State 'dance'; [void](Show-Remark 'frienddance' -Force)}
  'wave' {Begin-State 'friendwave'; if(-not $script:FriendMode){[void](Show-Remark 'friendwave' -Force)}}
 }
}
function Update-Friend([double]$t){
 if(-not $script:linkOk -or $script:SelfTestMode){return}
 foreach($line in @($script:friendSay)){if($t -ge $line[0]){[void]$script:friendSay.Remove($line); Show-FriendLine $line[1]}}
 if($t -lt $script:nextFriendPublish){return}
 $script:nextFriendPublish=$t+0.2; Publish-Friend
 $friend=Read-Friend; $script:friendInfo=$friend
 $was=$script:friendPresent; $script:friendPresent=$null -ne $friend
 if($script:friendPresent -and -not $was){
  $script:friendSeenSeq=$friend.seq; $script:friendGoneSince=0.0
  if($script:FriendMode -and -not $script:friendPlaced){
   # Arrive next to her, on whichever side has room.
   $script:friendPlaced=$true; $a=Get-WorkArea
   $x=$friend.cx-$w.Width/2-[Math]::Max(220.0,$w.Width*1.1)
   if($x -lt $a.Left){$x=$friend.cx-$w.Width/2+[Math]::Max(220.0,$w.Width*1.1)}
   $w.Left=$x; $w.Top=$friend.feet-$w.Height; Clamp-Pet
  }
  if(Test-CanReact){Begin-State 'friendwave'}
  [void](Show-Remark $(if($script:FriendMode){'friendarrive'}else{'friendhello'}) -Force)
  $script:friendEverSeen=$true; $script:nextFriendPlay=$t+25
 }
 if(-not $script:friendPresent){
  if($was){
   if($script:FriendMode){$w.Close(); return} # she went home: so do I
   if(Test-CanReact){[void](Show-Remark 'friendgone')}
  }
  if($script:FriendMode -and $t -gt 20 -and -not $script:friendEverSeen){$w.Close()} # nobody to visit
  return
 }
 # Something from her.
 if($friend.seq -ne $script:friendSeenSeq){
  $script:friendSeenSeq=$friend.seq
  switch($friend.type){
   1 {}
   2 {if(Test-CanReact){Start-FriendActivity 'talk' $friend.arg $friend}}
   3 {if(Test-CanReact){Start-FriendActivity 'chase' $friend.arg $friend}}
   4 {if(Test-CanReact){Start-FriendActivity 'dance' 0 $friend}}
   5 {if(Test-CanReact){Start-FriendActivity 'wave' 0 $friend}}
   6 {if($script:FriendMode){[void](Show-Remark 'friendbye' -Force); $w.Close(); return}}
   7 {if($script:state -eq 'friendflee'){Begin-State 'grumpy'; [void](Show-Remark 'friendtagged' -Force)}}
  }
 }
 # The first Capri decides what they do together, when both are free.
 if($script:FriendMode -or $t -lt $script:nextFriendPlay){return}
 $script:nextFriendPlay=$t+50+$rng.NextDouble()*60
 $mine=(Test-CanReact) -and $script:spec.tags.Contains('calm') -and -not $script:voiceBusy
 if(-not $mine -or -not $friend.available){$script:nextFriendPlay=$t+15; return}
 $roll=$rng.NextDouble()
 if($script:musicPlaying -and $roll -lt 0.4){$type='dance'; $arg=0}
 elseif($roll -lt 0.55){$type='talk'; $arg=$rng.Next(0,200)}
 elseif($roll -lt 0.85){$type='chase'; $arg=$rng.Next(0,2)}
 else{$type='wave'; $arg=0}
 Send-FriendMessage $type $arg
 Start-FriendActivity $type $arg $friend
}
function Get-FriendDirection {
 if($null -eq $script:friendInfo){return $script:direction}
 if($script:friendInfo.cx -lt $w.Left+$w.Width/2){return 1.0}else{return -1.0}
}
$paths['friendmeet']={
 $script:targetX=$script:friendMeetX; $script:targetY=$script:friendMeetY
 if($null -eq $script:friendInfo){Choose-State; return}
 if([Math]::Abs($script:targetX-$w.Left)+[Math]::Abs($script:targetY-$w.Top) -lt 6){Begin-State 'friendtalk'}
}
$paths['friendface']={
 if($null -eq $script:friendInfo){Choose-State; return}
 $script:direction=Get-FriendDirection
}
$paths['friendchase']={
 $f=$script:friendInfo; if($null -eq $f){Choose-State; return}
 $script:targetX=$f.left; $script:targetY=$f.top
 if([Math]::Abs($f.cx-($w.Left+$w.Width/2)) -lt $w.Width*0.35 -and [Math]::Abs($f.feet-($w.Top+$w.Height)) -lt 90){
  Send-FriendMessage 'tagged'; Begin-State 'laughing'; [void](Show-Remark 'friendtag' -Force)
 }
}
$paths['friendflee']={
 $f=$script:friendInfo; if($null -eq $f){Choose-State; return}
 $myCx=$w.Left+$w.Width/2; $away=if($myCx -lt $f.cx){-1.0}else{1.0}
 # Cornered: dash past her to the other side.
 if(($away -lt 0 -and $w.Left -le $area.Left+12) -or ($away -gt 0 -and $w.Left+$w.Width -ge $area.Right-12)){$away=-$away}
 $script:targetX=$w.Left+$away*240; $script:targetY=$area.Top+($area.Height-$w.Height)*(0.45+0.4*[Math]::Sin($phase*1.3))
}
