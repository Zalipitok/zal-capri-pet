# Awareness: music that's playing, the app in front (opt-in), files dropped on her, and the summon hotkey.

# --- Music -----------------------------------------------------------------
# Windows' media sessions (Spotify, browsers, media players) via WinRT. Calls never block:
# each request is started, then picked up on a later frame once it has finished.
$script:musicAvailable=$false; $script:musicError=''; $script:musicManager=$null; $script:musicManagerTask=$null
$script:musicPropsTask=$null; $script:musicPlaying=$false; $script:musicTitle=''; $script:musicArtist=''; $script:musicApp=''
$script:musicPlayingSince=0.0; $script:lastSongRemark=-1000.0; $script:nextMusicPoll=3.0; $script:musicFailures=0
function Initialize-Music {
 try {
  Add-Type -AssemblyName System.Runtime.WindowsRuntime
  $managerType=[Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager,Windows.Media.Control,ContentType=WindowsRuntime]
  $script:musicPropsType=[Windows.Media.Control.GlobalSystemMediaTransportControlsSessionMediaProperties,Windows.Media.Control,ContentType=WindowsRuntime]
  $script:asTask=@([System.WindowsRuntimeSystemExtensions].GetMethods() | Where-Object {$_.Name -eq 'AsTask' -and $_.GetParameters().Count -eq 1 -and $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncOperation`1'})[0]
  $script:musicManagerTask=$script:asTask.MakeGenericMethod($managerType).Invoke($null,@($managerType::RequestAsync()))
  $script:musicAvailable=$true
 } catch { $script:musicAvailable=$false; $script:musicError=$_.Exception.Message }
}
function Update-Music([double]$t){
 if(-not $script:musicAvailable -or -not $script:musicEnabled -or $t -lt $script:nextMusicPoll){return}
 $script:nextMusicPoll=$t+2
 try {
  if($null -eq $script:musicManager){
   if(-not $script:musicManagerTask.IsCompleted){return}
   if($script:musicManagerTask.IsFaulted){$script:musicAvailable=$false; $script:musicError='media sessions unavailable'; return}
   $script:musicManager=$script:musicManagerTask.Result; return
  }
  if($null -ne $script:musicPropsTask){
   if(-not $script:musicPropsTask.IsCompleted){return}
   if(-not $script:musicPropsTask.IsFaulted -and $null -ne $script:musicPropsTask.Result){
    $props=$script:musicPropsTask.Result
    $title=[string]$props.Title; $artist=[string]$props.Artist
    if($title -and $title -ne $script:musicTitle){
     $script:musicTitle=$title; $script:musicArtist=$artist
     # New song: sometimes comment on it (not on every track).
     if($script:musicPlaying -and $t-$script:lastSongRemark -ge $script:tuning.songRemarkMinutes*60 -and $rng.NextDouble() -lt 0.6){
      $name=if($artist){$title+' by '+$artist}else{$title}
      if($name.Length -gt 60){$name=$name.Substring(0,57)+'...'}
      if(Show-Remark 'song' -Value $name){$script:lastSongRemark=$t}
     }
    }
   }
   $script:musicPropsTask=$null
  }
  $session=$script:musicManager.GetCurrentSession()
  $wasPlaying=$script:musicPlaying
  if($null -eq $session){$script:musicPlaying=$false}
  else{
   $info=$session.GetPlaybackInfo()
   $script:musicPlaying=($null -ne $info -and [string]$info.PlaybackStatus -eq 'Playing')
   $script:musicApp=[string]$session.SourceAppUserModelId
   $script:musicPropsTask=$script:asTask.MakeGenericMethod($script:musicPropsType).Invoke($null,@($session.TryGetMediaPropertiesAsync()))
  }
  if($script:musicPlaying -and -not $wasPlaying){$script:musicPlayingSince=$t}
  if($wasPlaying -and -not $script:musicPlaying -and $t-$script:musicPlayingSince -gt 60 -and $rng.NextDouble() -lt 0.5){[void](Show-Remark 'musicstop')}
  $script:musicFailures=0
 } catch {
  $script:musicFailures++; $script:musicPropsTask=$null
  if($script:musicFailures -ge 5){$script:musicAvailable=$false; $script:musicError=$_.Exception.Message}
 }
}
# Bob along while something plays and she's just hanging around.
function Test-MusicGroove {
 return $script:musicEnabled -and $script:musicPlaying -and -not $script:voiceBusy -and $script:spec.tags.Contains('calm')
}
# With headphones on: the hover pose becomes the four head-bob frames, one per half beat.
function Get-MusicPose([double]$t){
 $step=[int][Math]::Floor($t*[Math]::Max(30.0,[double]$script:tuning.musicBpm)/30.0)%4
 return $script:musicPoseStart+$step
}
function Apply-MusicGroove([double]$t){
 if($script:musicFrameActive -or -not (Test-MusicGroove)){return}
 $k=[double]$script:tuning.musicBob
 $shift.Y+=[Math]::Sin($t*11.3)*3.0*$k
 $rotation.Angle+=[Math]::Sin($t*5.65)*2.5*$k
}

# --- Apps (opt-in) ---------------------------------------------------------
# Reads only the name and title of the window in front, on this PC; nothing is stored or sent.
$script:appRules=$null; $script:appCategory=''; $script:appCategorySince=0.0; $script:appAnnounced=''
$script:lastAppRemark=-1000.0; $script:nextAppPoll=0.0
function Import-AppRules {
 $script:appRules=$null
 $path=Join-Path $script:AppRoot 'app-reactions.json'
 if(-not (Test-Path -LiteralPath $path)){return}
 try { $script:appRules=Get-Content -LiteralPath $path -Raw -Encoding UTF8 | ConvertFrom-Json } catch { }
}
function Get-AppCategory([string]$process,[string]$title){
 $r=$script:appRules; if($null -eq $r){return 'other'}
 foreach($category in @('games','code','video')){
  foreach($name in @($r.$category)){if($name -and $process -ieq [string]$name){return $category}}
 }
 if(@($r.browsers | Where-Object {$_ -and $process -ieq [string]$_}).Count -gt 0){
  foreach($site in @($r.videoSites)){if($site -and $title -like ('*'+[string]$site+'*')){return 'video'}}
  return 'browser'
 }
 return 'other'
}
function Update-AppAwareness([double]$t){
 if(-not $script:appAware -or $t -lt $script:nextAppPoll){return}
 $script:nextAppPoll=$t+2
 $app=$null; try{$app=[CapriDesktop]::ForegroundApp()}catch{}
 if($null -eq $app){return} # Capri herself, or nothing, is in front
 $category=Get-AppCategory $app[0] $app[1]
 if($category -ne $script:appCategory){$script:appCategory=$category; $script:appCategorySince=$t; return}
 # React once the app has stayed in front for a bit, and not too often.
 if($category -eq $script:appAnnounced -or $t-$script:appCategorySince -lt 15){return}
 $script:appAnnounced=$category
 if($category -notin @('games','code','video')){return}
 if($t-$script:lastAppRemark -lt $script:tuning.appRemarkMinutes*60 -or $rng.NextDouble() -gt 0.7){return}
 $kind='app'+$category
 if($script:timeOfDay -and (Get-Date).Hour -lt 5 -and $category -in @('games','video')){$kind='applate'}
 if(Show-Remark $kind){$script:lastAppRemark=$t}
}
# While you code, she keeps her random chatter to herself.
function Test-QuietApp {return $script:appAware -and $script:appCategory -eq 'code'}

# --- Files dropped on her --------------------------------------------------
# A mock "scan": she reads only the file name and never opens, moves or changes the file.
$w.AllowDrop=$true
$w.Add_DragOver({param($sender,$event)
 $event.Effects=if($event.Data.GetDataPresent([Windows.DataFormats]::FileDrop)){[Windows.DragDropEffects]::Copy}else{[Windows.DragDropEffects]::None}
 $event.Handled=$true
})
$w.Add_Drop({param($sender,$event)
 if(-not $event.Data.GetDataPresent([Windows.DataFormats]::FileDrop)){return}
 $files=@($event.Data.GetData([Windows.DataFormats]::FileDrop))
 if($files.Count -eq 0){return}
 Start-FileScan ([string]$files[0]) $files.Count
 $event.Handled=$true
})
function Start-FileScan([string]$path,[int]$count=1){
 $name=[IO.Path]::GetFileName($path.TrimEnd('\'))
 if(-not $name){$name=$path}
 if($name.Length -gt 34){$name=$name.Substring(0,31)+'...'}
 # Same file name, same verdict.
 $score=0; foreach($ch in $name.ToLowerInvariant().ToCharArray()){$score=($score*31+[int]$ch)%100003}
 $score=1+($score%10)
 if([IO.Path]::GetExtension($path) -in @('.exe','.bat','.cmd','.ps1','.vbs','.msi','.scr')){$score=[Math]::Min(10,$score+3)}
 $script:scanName=if($count -gt 1){$name+' (+'+($count-1)+' more)'}else{$name}
 $script:scanScore=$score
 [void](Add-Memory 'scans')
 Prepare-ManualAction; $script:routine.Clear(); Begin-State 'filescan'
}

# --- Summon hotkey ---------------------------------------------------------
$script:hotkeyOk=$false; $script:hotkeyError=''
function ConvertFrom-HotkeyText([string]$text){
 $modifiers=0; $key=0
 foreach($part in ($text -split '\+')){
  $p=$part.Trim().ToLowerInvariant()
  switch -regex ($p){
   '^(ctrl|control)$' {$modifiers=$modifiers -bor 2}
   '^alt$' {$modifiers=$modifiers -bor 1}
   '^shift$' {$modifiers=$modifiers -bor 4}
   '^win$' {$modifiers=$modifiers -bor 8}
   '^[a-z0-9]$' {$key=[int][char]$p.ToUpperInvariant()}
   '^f([1-9]|1[0-2])$' {$key=0x6F+[int]$p.Substring(1)}
  }
 }
 if($key -eq 0){return $null}
 return @($modifiers,$key)
}
function Register-SummonHotkey {
 $script:hotkeyOk=$false; $script:hotkeyError=''
 if(-not $script:fxNative -or $script:petHandle -eq [IntPtr]::Zero){$script:hotkeyError='unavailable'; return}
 try{[CapriHotkey]::Unregister($script:petHandle)}catch{}
 if(-not $script:hotkeyEnabled){return}
 $parsed=ConvertFrom-HotkeyText ([string]$script:tuning.hotkey)
 if($null -eq $parsed){$script:hotkeyError='could not read "'+$script:tuning.hotkey+'" in tuning.json'; return}
 try{$script:hotkeyOk=[CapriHotkey]::Register($script:petHandle,[uint32]$parsed[0],[uint32]$parsed[1])}catch{$script:hotkeyError=$_.Exception.Message}
 if(-not $script:hotkeyOk -and -not $script:hotkeyError){$script:hotkeyError=[string]$script:tuning.hotkey+' is already used by another app'}
}
# Hidden or far away: come here. Already next to the pointer: hide.
function Invoke-Summon {
 if($script:isHidden){Invoke-CallOver; return}
 $near=$false
 try{$c=Get-LocalCursor; $near=[Math]::Abs($c.X-$w.Width/2) -lt 220 -and [Math]::Abs($c.Y-$w.Height/2) -lt 260}catch{}
 if($near -and $null -ne $script:tray){Set-Hidden $true}else{Invoke-CallOver}
}

Import-AppRules
Initialize-Music
