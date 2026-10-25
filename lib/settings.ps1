# Settings window: every setting in one tabbed window. Each entry below is defined once (tab, label,
# how to read it, how to change it); the window is built from this list. It is not modal, so changes
# show on Capri right away.
function New-BoolSetting([string]$tab,[string]$label,[scriptblock]$get,[scriptblock]$set,[string]$hint=''){
 return @{tab=$tab; type='bool'; label=$label; get=$get; set=$set; hint=$hint}
}
function New-ChoiceSetting([string]$tab,[string]$label,[object[]]$options,[scriptblock]$get,[scriptblock]$set){
 return @{tab=$tab; type='choice'; label=$label; options=$options; get=$get; set=$set}
}
function New-SliderSetting([string]$tab,[string]$label,[double]$min,[double]$max,[double]$step,[scriptblock]$get,[scriptblock]$set,[string]$unit='%'){
 return @{tab=$tab; type='slider'; label=$label; min=$min; max=$max; step=$step; get=$get; set=$set; unit=$unit}
}
function New-ButtonSetting([string]$tab,[string]$label,[scriptblock]$click){
 return @{tab=$tab; type='button'; label=$label; click=$click}
}
$script:settingDefs=@(
 # General
 (New-SliderSetting 'General' 'Size' 65 160 5 {$script:petSize} {param($v) Set-PetSize $v})
 (New-BoolSetting 'General' 'Start with Windows' {Test-Path -LiteralPath $script:startupLink} {param($v) Set-StartWithWindows $v})
 (New-ChoiceSetting 'General' 'When a fullscreen app is open' @(@('Hide until it closes','hide'),@('Sit quietly in the corner','busy'),@('Stay on top','ignore')) {$script:fullscreenMode} {param($v) Set-FullscreenMode $v})
 (New-BoolSetting 'General' ('Summon hotkey ('+$script:tuning.hotkey+')') {$script:hotkeyEnabled} {param($v) $script:hotkeyEnabled=$v; Register-SummonHotkey} 'Calls her to the pointer; press again next to her to hide her.')
 (New-ChoiceSetting 'General' 'Sleep after inactivity' @(@('2 minutes',120.0),@('5 minutes',300.0),@('10 minutes',600.0),@('Never',0.0)) {$script:idleSleepAfter} {param($v) $script:idleSleepAfter=[double]$v})
 (New-ChoiceSetting 'General' 'Break reminders' @(@('Off',0),@('Every 25 minutes',25),@('Every 50 minutes',50),@('Every 90 minutes',90)) {$script:breakMinutes} {param($v) $script:breakMinutes=[int]$v; $script:activeSince=$clock.Elapsed.TotalSeconds})
 # Voice
 (New-ChoiceSetting 'Voice' 'Language' @(@('English','en'),@('Japanese','ja')) {$script:language} {param($v) Stop-Voice; $script:language=[string]$v; $script:lastClip=''; $script:voiceNext=0; Schedule-Yap})
 (New-SliderSetting 'Voice' 'Volume' 0 100 5 {[Math]::Round($script:volume*100)} {param($v) Set-Volume ($v/100.0)})
 (New-BoolSetting 'Voice' 'Mute' {$script:muted} {param($v) Set-Muted $v})
 (New-ChoiceSetting 'Voice' 'Random yapping' @(@('Off (reactions only)','off'),@('Quiet: 100-180 seconds','quiet'),@('Normal: 45-90 seconds','normal'),@('Chatty: 18-35 seconds','chatty')) {$script:chatter} {param($v) $script:chatter=[string]$v; Schedule-Yap})
 (New-BoolSetting 'Voice' 'Speech subtitles' {$script:subtitles} {param($v) $script:subtitles=$v})
 (New-BoolSetting 'Voice' 'Character remarks' {$script:remarksEnabled} {param($v) $script:remarksEnabled=$v})
 (New-BoolSetting 'Voice' 'Talking mouth' {$script:talkingMouth} {param($v) $script:talkingMouth=$v})
 (New-ButtonSetting 'Voice' 'Open voice library...' {$script:pendingAction='voice-library'})
 # Look
 (New-BoolSetting 'Look' 'Eyes follow the cursor' {$script:eyesFollow} {param($v) $script:eyesFollow=$v})
 (New-BoolSetting 'Look' 'Smooth pose changes' {$script:smoothPoses} {param($v) $script:smoothPoses=$v})
 (New-BoolSetting 'Look' 'Shadow when perched' {$script:shadowEnabled} {param($v) $script:shadowEnabled=$v})
 (New-BoolSetting 'Look' 'Idle blinking' {$script:facialMotion} {param($v) $script:facialMotion=$v})
 (New-BoolSetting 'Look' 'Blackwall effects' {$script:blackwallEnabled} {param($v) $script:blackwallEnabled=$v})
 (New-BoolSetting 'Look' 'Sound effects (swat, head pat)' {$script:sfxEnabled} {param($v) $script:sfxEnabled=$v; if(-not $v){$sfxPlayer.Close()}})
 # Behaviour
 (New-BoolSetting 'Behaviour' 'Cursor chasing' {$script:chase} {param($v) $script:chase=$v; if(-not $v){$script:nextRetry=[double]::PositiveInfinity; if($script:spec.tags.Contains('needsChase')){Begin-State 'float'}}; $script:autoBag.Clear()})
 (New-BoolSetting 'Behaviour' 'Cursor knockback (moves your pointer)' {$script:cursorKnockback} {param($v) $script:cursorKnockback=$v} 'Not saved: always starts off.')
 (New-BoolSetting 'Behaviour' 'Sit on windows' {$script:windowPerch} {param($v) $script:windowPerch=$v})
 (New-BoolSetting 'Behaviour' 'Throwing physics' {$script:throwPhysics} {param($v) $script:throwPhysics=$v})
 (New-BoolSetting 'Behaviour' 'Time-of-day moods' {$script:timeOfDay} {param($v) $script:timeOfDay=$v})
 (New-BoolSetting 'Behaviour' 'Battery reactions (laptops)' {$script:batteryReactions} {param($v) $script:batteryReactions=$v})
 (New-SliderSetting 'Behaviour' 'Attacks' 0 200 25 {$script:activityRates.attacks} {param($v) $script:activityRates.attacks=[int]$v; $script:autoBag.Clear(); if($v -eq 0){$script:nextRetry=[double]::PositiveInfinity}})
 (New-SliderSetting 'Behaviour' 'Wandering' 0 200 25 {$script:activityRates.wandering} {param($v) $script:activityRates.wandering=[int]$v; $script:autoBag.Clear()})
 (New-SliderSetting 'Behaviour' 'Resting' 0 200 25 {$script:activityRates.resting} {param($v) $script:activityRates.resting=[int]$v; $script:autoBag.Clear()})
 (New-SliderSetting 'Behaviour' 'Antics' 0 200 25 {$script:activityRates.antics} {param($v) $script:activityRates.antics=[int]$v; $script:autoBag.Clear()})
 (New-SliderSetting 'Behaviour' 'Talking' 0 200 25 {$script:activityRates.talking} {param($v) $script:activityRates.talking=[int]$v; Schedule-Yap})
 (New-SliderSetting 'Behaviour' 'Rare antics' 0 200 25 {$script:activityRates.rare} {param($v) $script:activityRates.rare=[int]$v})
 # Music and apps
 (New-BoolSetting 'Music and apps' 'React to music' {$script:musicEnabled} {param($v) $script:musicEnabled=$v})
 (New-BoolSetting 'Music and apps' 'Bob to the real beat' {$script:beatSync} {param($v) $script:beatSync=$v} 'Listens to what your PC is playing to find the beat. Nothing is recorded or saved.')
 (New-BoolSetting 'Music and apps' 'Notice which app I''m using' {$script:appAware} {param($v) $script:appAware=$v; $script:appCategory=''; $script:appAnnounced=''} 'Reads only the name and title of the window in front. Nothing leaves this PC.')
 # Fun
 (New-ButtonSetting 'Fun' 'Play catch me (30 s)' {$script:pendingAction='catchgame'})
 (New-ButtonSetting 'Fun' 'Our history and unlocks...' {[void][Windows.MessageBox]::Show((Get-MemorySummary)+"`n`nBest catch-me score: "+$script:memory.bestCatch+"`n`n"+(Get-UnlockSummary),'What Capri remembers')})
 (New-ButtonSetting 'Fun' 'Run self-test' {$script:pendingAction='selftest'})
)
$script:settingsWindow=$null
function Show-SettingsWindow {
 if($null -ne $script:settingsWindow){try{[void]$script:settingsWindow.Activate()}catch{}; return}
 $brush={param($hex) [Windows.Media.BrushConverter]::new().ConvertFromString($hex)}
 $dialog=[Windows.Window]::new(); $dialog.Title='Capri // Settings'; $dialog.Width=520; $dialog.Height=560
 $dialog.WindowStartupLocation='CenterScreen'; $dialog.Background=(& $brush '#111722'); $dialog.Foreground=[Windows.Media.Brushes]::White
 $tabs=[Windows.Controls.TabControl]::new(); $tabs.Background=(& $brush '#151c2a'); $tabs.Margin=[Windows.Thickness]::new(10)
 $dialog.Content=$tabs
 $pages=[ordered]@{}
 for($i=0;$i -lt $script:settingDefs.Count;$i++){
  $def=$script:settingDefs[$i]
  if(-not $pages.Contains($def.tab)){
   $tab=[Windows.Controls.TabItem]::new(); $tab.Header=$def.tab
   $scroll=[Windows.Controls.ScrollViewer]::new(); $scroll.VerticalScrollBarVisibility='Auto'
   $stack=[Windows.Controls.StackPanel]::new(); $stack.Margin=[Windows.Thickness]::new(14,12,14,12)
   $scroll.Content=$stack; $tab.Content=$scroll; [void]$tabs.Items.Add($tab); $pages[$def.tab]=$stack
  }
  $stack=$pages[$def.tab]
  $row=[Windows.Controls.StackPanel]::new(); $row.Margin=[Windows.Thickness]::new(0,0,0,12)
  switch($def.type){
   'bool' {
    $box=[Windows.Controls.CheckBox]::new(); $box.Content=$def.label; $box.Foreground=[Windows.Media.Brushes]::White; $box.Tag=$i
    $box.IsChecked=[bool](& $def.get)
    $box.Add_Click({param($sender,$event) $d=$script:settingDefs[[int]$sender.Tag]; & $d.set ([bool]$sender.IsChecked); Save-VoiceSettings; $sender.IsChecked=[bool](& $d.get)})
    [void]$row.Children.Add($box)
   }
   'choice' {
    $label=[Windows.Controls.TextBlock]::new(); $label.Text=$def.label; [void]$row.Children.Add($label)
    $combo=[Windows.Controls.ComboBox]::new(); $combo.Tag=$i; $combo.Margin=[Windows.Thickness]::new(0,4,0,0); $combo.MaxWidth=320; $combo.HorizontalAlignment='Left'; $combo.MinWidth=260
    $current=& $def.get
    foreach($option in $def.options){
     $item=[Windows.Controls.ComboBoxItem]::new(); $item.Content=$option[0]; $item.Tag=$option[1]; [void]$combo.Items.Add($item)
     if([string]$option[1] -eq [string]$current){$combo.SelectedItem=$item}
    }
    $combo.Add_SelectionChanged({param($sender,$event) if($null -eq $sender.SelectedItem){return}; $d=$script:settingDefs[[int]$sender.Tag]; & $d.set $sender.SelectedItem.Tag; Save-VoiceSettings})
    [void]$row.Children.Add($combo)
   }
   'slider' {
    $label=[Windows.Controls.TextBlock]::new(); $label.Text=$def.label+': '+[int](& $def.get)+$def.unit; [void]$row.Children.Add($label)
    $slider=[Windows.Controls.Slider]::new(); $slider.Minimum=$def.min; $slider.Maximum=$def.max; $slider.TickFrequency=$def.step; $slider.IsSnapToTickEnabled=$true
    $slider.Value=[double](& $def.get); $slider.Tag=$i; $slider.MaxWidth=320; $slider.HorizontalAlignment='Left'; $slider.Width=320
    $slider.Add_ValueChanged({param($sender,$event) $d=$script:settingDefs[[int]$sender.Tag]; & $d.set ([double]$sender.Value); $sender.Parent.Children[0].Text=$d.label+': '+[int]$sender.Value+$d.unit})
    $slider.Add_LostMouseCapture({Save-VoiceSettings})
    [void]$row.Children.Add($slider)
   }
   'button' {
    $button=[Windows.Controls.Button]::new(); $button.Content=$def.label; $button.Tag=$i; $button.HorizontalAlignment='Left'; $button.Padding=[Windows.Thickness]::new(12,5,12,5)
    $button.Add_Click({param($sender,$event) & $script:settingDefs[[int]$sender.Tag].click})
    [void]$row.Children.Add($button)
   }
  }
  if($def.hint){$note=[Windows.Controls.TextBlock]::new(); $note.Text=$def.hint; $note.FontSize=11; $note.Opacity=0.65; $note.TextWrapping='Wrap'; $note.Margin=[Windows.Thickness]::new(20,2,0,0); [void]$row.Children.Add($note)}
  [void]$stack.Children.Add($row)
 }
 $dialog.Add_Closed({$script:settingsWindow=$null; Save-VoiceSettings; Register-Interaction})
 $script:settingsWindow=$dialog
 $dialog.Show()
}

# Starts a second, silent Capri that runs the self-test.
function Start-SelfTestRun {
 try {
  if($script:IsExe){Start-Process -FilePath $script:LaunchPath -ArgumentList '-SelfTest'}
  else{Start-Process -FilePath (Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe') -ArgumentList @('-NoProfile','-STA','-ExecutionPolicy','Bypass','-WindowStyle','Hidden','-File',('"'+$script:LaunchPath+'"'),'-SelfTest')}
  Show-Status 'Self-test started. A second me will appear briefly, then show the results.' 5
 } catch { Show-Problem ('Could not start the self-test: '+$_.Exception.Message) }
}
