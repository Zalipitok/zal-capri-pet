# Voice library, context menu and tray icon.
# ---------------------------------------------------------------------------
# Voice library window (also where clips are switched on or off for random chatter)
$script:voiceChooserOpen=$false
function Show-VoiceLibrary {
 if($script:voiceChooserOpen){return}
 Stop-Voice; Register-Interaction; $script:voiceChooserOpen=$true
 $dialog=[Windows.Window]::new(); $dialog.Title='Capri // Voice library'; $dialog.Width=650; $dialog.Height=500
 $dialog.Owner=$w; $dialog.WindowStartupLocation='CenterScreen'; $dialog.Background=[Windows.Media.BrushConverter]::new().ConvertFromString('#111722')
 $stack=[Windows.Controls.StackPanel]::new(); $stack.Margin=[Windows.Thickness]::new(16); $dialog.Content=$stack
 $title=[Windows.Controls.TextBlock]::new(); $title.Foreground=[Windows.Media.Brushes]::Yellow; $title.FontSize=20
 $title.Text=if($script:language -eq 'ja'){'JAPANESE // SELECT A LINE'}else{'ENGLISH // SELECT A LINE'}; [void]$stack.Children.Add($title)
 $hint=[Windows.Controls.TextBlock]::new(); $hint.Foreground=[Windows.Media.Brushes]::White; $hint.TextWrapping='Wrap'
 $hint.Text='Select a line, then Play selected. [ON] lines can come up in random chatter; [OFF] lines can still be previewed.'
 $hint.Margin=[Windows.Thickness]::new(0,8,0,8); [void]$stack.Children.Add($hint)
 $script:voiceLibraryList=[Windows.Controls.ListBox]::new(); $script:voiceLibraryList.Height=245
 foreach($clip in @($voicePack | Where-Object {$_.language -eq $script:language})){
  $entry=[Windows.Controls.ListBoxItem]::new(); $entry.Tag=$clip
  $key=$clip.language+':'+$clip.id; $prefix=if($script:approvedVoices -contains $key){'[ON] '}else{'[OFF] '}
  $entry.Content=$prefix+[string]$clip.subtitle; [void]$script:voiceLibraryList.Items.Add($entry)
 }
 [void]$stack.Children.Add($script:voiceLibraryList)
 $script:voiceLibraryStatus=[Windows.Controls.TextBlock]::new(); $script:voiceLibraryStatus.Foreground=[Windows.Media.Brushes]::White; $script:voiceLibraryStatus.TextWrapping='Wrap'; $script:voiceLibraryStatus.Margin=[Windows.Thickness]::new(0,10,0,8)
 $script:voiceLibraryStatus.Text=([string]$script:voiceLibraryList.Items.Count+' clips available. Review clips have unverified speakers/subtitles.')
 [void]$stack.Children.Add($script:voiceLibraryStatus)
 $buttons=[Windows.Controls.WrapPanel]::new(); [void]$stack.Children.Add($buttons)
 foreach($action in @('Play selected','Stop','Enable selected','Disable selected','Enable all listed','Enable established only','Open voice folder')){
  $button=[Windows.Controls.Button]::new(); $button.Content=$action; $button.Tag=$action; $button.Margin=[Windows.Thickness]::new(0,0,8,8); $button.Padding=[Windows.Thickness]::new(8,5,8,5)
  $button.Add_Click({param($sender,$event)
   $action=[string]$sender.Tag
   if($action -eq 'Stop'){Stop-Voice; $script:voiceLibraryStatus.Text='Stopped.'; return}
   if($action -eq 'Open voice folder'){Invoke-Item -LiteralPath (Join-Path $script:AppRoot ('voices/'+$script:language)); return}
   if($action -in @('Enable all listed','Enable established only')){
    $established=$action -eq 'Enable established only'
    foreach($item in $script:voiceLibraryList.Items){
     $clip=$item.Tag; $key=$clip.language+':'+$clip.id
     $on=(-not $established) -or (-not $clip.needs_review)
     $script:approvedVoices=@($script:approvedVoices | Where-Object {$_ -ne $key}); if($on){$script:approvedVoices+=@($key)}
     $item.Content=$(if($on){'[ON] '}else{'[OFF] '})+[string]$clip.subtitle
    }
    Save-VoiceSettings; Schedule-Yap
    $script:voiceLibraryStatus.Text=if($established){'Only established clips are on for random chatter.'}else{'All listed clips enabled for random chatter, including review clips.'}
    return
   }
   $item=$script:voiceLibraryList.SelectedItem
   if($null -eq $item){$script:voiceLibraryStatus.Text='Select a line first.'; return}
   $clip=$item.Tag; $key=$clip.language+':'+$clip.id
   if($action -eq 'Play selected'){
    if($script:muted){$script:voiceLibraryStatus.Text='Voice is muted. Turn off Mute in the Capri menu.'; return}
    if($script:volume -le 0){$script:voiceLibraryStatus.Text='Volume is zero. Raise it in Settings > Voice > Volume.'; return}
    Stop-Voice; Play-Voice 'random' $true ([string]$clip.id)
    if($script:voiceBusy){$script:voiceLibraryStatus.Text='Playing: '+[string]$clip.subtitle+' ('+[string]$clip.id+')'}else{$script:voiceLibraryStatus.Text='Could not play: '+$script:lastAudioError}
    return
   }
   $script:approvedVoices=@($script:approvedVoices | Where-Object {$_ -ne $key})
   if($action -eq 'Enable selected'){$script:approvedVoices+=@($key); $item.Content='[ON] '+[string]$clip.subtitle}else{$item.Content='[OFF] '+[string]$clip.subtitle}
   Save-VoiceSettings; Schedule-Yap; $script:voiceLibraryStatus.Text=[string]$item.Content
  })
  [void]$buttons.Children.Add($button)
 }
 if($script:voiceLibraryList.Items.Count -gt 0){$script:voiceLibraryList.SelectedIndex=0}
 try{[void]$dialog.ShowDialog()}finally{$script:voiceChooserOpen=$false; Register-Interaction; Save-VoiceSettings; Schedule-Yap}
}

# ---------------------------------------------------------------------------
# Context menu, built directly in its final shape. Checkmarks are refreshed every time it opens.
$menu = [Windows.Controls.ContextMenu]::new()
$script:menuSync=[Collections.Generic.List[object]]::new()
function New-MenuEntry($parent,[string]$header,[scriptblock]$onClick=$null,[scriptblock]$isChecked=$null,$tag=$null){
 $item=[Windows.Controls.MenuItem]::new(); $item.Header=$header
 if($null -ne $tag){$item.Tag=$tag}
 if($null -ne $isChecked){$item.IsCheckable=$true; $script:menuSync.Add(@($item,$isChecked))}
 if($null -ne $onClick){$item.Add_Click($onClick)}
 [void]$parent.Items.Add($item)
 return $item
}
function New-SubMenu($parent,[string]$header){
 $item=[Windows.Controls.MenuItem]::new(); $item.Header=$header
 [void]$parent.Items.Add($item); return $item
}
function Add-MenuSeparator($parent){[void]$parent.Items.Add([Windows.Controls.Separator]::new())}
# A labelled slider inside a submenu.
function New-MenuSlider($parent,[string]$label,[double]$min,[double]$max,[double]$tick,[double]$value,$tag,[scriptblock]$onChange){
 $panel=[Windows.Controls.StackPanel]::new(); $panel.Width=210
 $text=[Windows.Controls.TextBlock]::new(); $text.Text=$label; [void]$panel.Children.Add($text)
 $slider=[Windows.Controls.Slider]::new()
 $slider.Minimum=$min; $slider.Maximum=$max; $slider.TickFrequency=$tick; $slider.IsSnapToTickEnabled=$true
 $slider.Value=$value; $slider.Tag=$tag; $slider.Margin=[Windows.Thickness]::new(4,10,4,10)
 [void]$panel.Children.Add($slider)
 $slider.Add_ValueChanged($onChange)
 $hostItem=[Windows.Controls.MenuItem]::new(); $hostItem.StaysOpenOnClick=$true; $hostItem.Header=$panel
 [void]$parent.Items.Add($hostItem)
 return $text
}
$stateClick={param($sender,$event) $script:pendingAction='state:'+[string]$sender.Tag}

$menuTitle=[Windows.Controls.MenuItem]::new(); $menuTitle.Header='CAPRI  //  NEURAL LINK'; $menuTitle.IsEnabled=$false
$menuTitle.Foreground=[Windows.Media.Brushes]::Yellow; $menuTitle.FontWeight='Bold'
[void]$menu.Items.Add($menuTitle)
[void](New-MenuEntry $menu 'Pause' {Set-Paused (-not $script:paused)} {$script:paused})
[void](New-MenuEntry $menu 'Busy, Capri (quiet corner)' {Set-BusyMode (-not $script:busyMode)} {$script:busyMode})
[void](New-MenuEntry $menu 'Mute' {Set-Muted (-not $script:muted)} {$script:muted})
$hideItem=New-MenuEntry $menu 'Hide (tray icon brings her back)' {$script:pendingAction='hide'}
Add-MenuSeparator $menu

$interact=New-SubMenu $menu 'Interact'
[void](New-MenuEntry $interact 'Do something!' {$script:pendingAction='random'})
[void](New-MenuEntry $interact 'Pet her (Ctrl-click)' {$script:pendingAction='pet'})
[void](New-MenuEntry $interact 'Play catch me (30 s)' {$script:pendingAction='catchgame'})
[void](New-MenuEntry $interact 'Cartoon swat (Shift-click)' {$script:pendingAction='swat'})
[void](New-MenuEntry $interact 'Say a line now' {$script:pendingAction='speak'})
[void](New-MenuEntry $interact 'Attack cursor (Blackwall)' {$script:pendingAction='attack'})
[void](New-MenuEntry $interact 'Sit on the active window' {$script:pendingAction='windowperch'})
[void](New-MenuEntry $interact 'Perch at taskbar' $stateClick $null 'taskbar')
[void](New-MenuEntry $interact 'Fake sabotage / repair' $stateClick $null 'sabotage')

$animations=New-SubMenu $menu 'Animations'
$menuGroups=[ordered]@{}
foreach($spec in $script:specs.Values){
 if(-not $spec.menu){continue}
 if(-not $menuGroups.Contains($spec.menu)){$menuGroups[$spec.menu]=New-SubMenu $animations $spec.menu}
 [void](New-MenuEntry $menuGroups[$spec.menu] $spec.title $stateClick $null $spec.id)
}
Add-MenuSeparator $animations
$routineMenu=New-SubMenu $animations 'Little routines'
foreach($name in $script:routines.Keys){
 [void](New-MenuEntry $routineMenu $name {param($sender,$event) $script:pendingAction='routine:'+[string]$sender.Tag} $null $name)
}
[void](New-MenuEntry $animations 'Stop routine' $stateClick $null 'float')

[void](New-MenuEntry $menu 'Settings window...' {$script:pendingAction='settings'})
$settings=New-SubMenu $menu 'Settings'
$voiceMenu=New-SubMenu $settings 'Voice and subtitles'
foreach($entry in @(@('English','en'),@('Japanese','ja'))){
 [void](New-MenuEntry $voiceMenu $entry[0] {param($sender,$event)
  Stop-Voice; $script:language=[string]$sender.Tag; $script:lastClip=''; $script:voiceNext=0
  Schedule-Yap; Save-VoiceSettings
 } {param($item) $item.Tag -eq $script:language} $entry[1])
}
Add-MenuSeparator $voiceMenu
$volumeMenu=New-SubMenu $voiceMenu 'Volume'
foreach($level in @(25,50,75,100)){
 [void](New-MenuEntry $volumeMenu ([string]$level+'%') {param($sender,$event) Set-Volume ([double]$sender.Tag/100); Save-VoiceSettings} {param($item) [int]$item.Tag -eq [int][Math]::Round($script:volume*100)} $level)
}
$chatterMenu=New-SubMenu $voiceMenu 'Random yapping'
foreach($entry in @(@('Off (reactions only)','off'),@('Quiet: 100-180 seconds','quiet'),@('Normal: 45-90 seconds','normal'),@('Chatty: 18-35 seconds','chatty'))){
 [void](New-MenuEntry $chatterMenu $entry[0] {param($sender,$event) $script:chatter=[string]$sender.Tag; Schedule-Yap; Save-VoiceSettings} {param($item) $item.Tag -eq $script:chatter} $entry[1])
}
[void](New-MenuEntry $voiceMenu 'Speech subtitles' {
 $script:subtitles=-not $script:subtitles
 if($script:subtitles -and $script:voiceBusy){
  $words.Text=$script:activeCaption; $bubble.Visibility='Visible'
  $script:subtitleUntil=[double]::PositiveInfinity; $script:messageUntil=[double]::PositiveInfinity
 }else{$script:subtitleUntil=0; $script:messageUntil=0; $bubble.Visibility='Collapsed'}
 Save-VoiceSettings
} {$script:subtitles})
[void](New-MenuEntry $voiceMenu 'Character remarks' {
 $script:remarksEnabled=-not $script:remarksEnabled
 if(-not $script:voiceBusy -and $clock.Elapsed.TotalSeconds -ge $script:subtitleUntil){$bubble.Visibility='Collapsed'; $script:messageUntil=0}
 $script:nextRemark=$clock.Elapsed.TotalSeconds+5
 Save-VoiceSettings
} {$script:remarksEnabled})
Add-MenuSeparator $voiceMenu
$lineMenu=New-MenuEntry $voiceMenu 'Voice library...' {param($sender,$event) $event.Handled=$true; $script:pendingAction='voice-library'}

$appearance=New-SubMenu $settings 'Appearance'
$sizeMenu=New-SubMenu $appearance 'Size'
$sizeLabel=New-MenuSlider $sizeMenu ('Size: '+[int]$script:petSize+'%') 65 160 5 $script:petSize $null {param($sender,$event)
 Register-Interaction; Set-PetSize ([double]$sender.Value)
 $sizeLabel.Text=('Size: '+[int]$script:petSize+'%')
}
[void](New-MenuEntry $appearance 'Shadow when perched' {$script:shadowEnabled=-not $script:shadowEnabled; Save-VoiceSettings} {$script:shadowEnabled})
[void](New-MenuEntry $appearance 'Eyes follow the cursor' {$script:eyesFollow=-not $script:eyesFollow; Save-VoiceSettings} {$script:eyesFollow})
[void](New-MenuEntry $appearance 'Talking mouth' {$script:talkingMouth=-not $script:talkingMouth; Save-VoiceSettings} {$script:talkingMouth})
[void](New-MenuEntry $appearance 'Smooth pose changes' {$script:smoothPoses=-not $script:smoothPoses; Save-VoiceSettings} {$script:smoothPoses})
[void](New-MenuEntry $appearance 'Idle blinking' {$script:facialMotion=-not $script:facialMotion} {$script:facialMotion})
[void](New-MenuEntry $appearance 'Blackwall effects' {$script:blackwallEnabled=-not $script:blackwallEnabled} {$script:blackwallEnabled})
[void](New-MenuEntry $appearance 'Swat sound' {$script:sfxEnabled=-not $script:sfxEnabled; if(-not $script:sfxEnabled){$sfxPlayer.Close()}} {$script:sfxEnabled})

$behaviour=New-SubMenu $settings 'Behaviour'
[void](New-MenuEntry $behaviour 'Cursor chasing' {
 $script:chase=-not $script:chase
 if(-not $script:chase){$script:nextRetry=[double]::PositiveInfinity; if($script:spec.tags.Contains('needsChase')){Begin-State 'float'}}
 $script:autoBag.Clear(); $script:until=0
 Show-Status ('Cursor chasing '+$(if($script:chase){'on.'}else{'off.'}))
} {$script:chase})
[void](New-MenuEntry $behaviour 'Cursor knockback (moves your pointer)' {$script:cursorKnockback=-not $script:cursorKnockback} {$script:cursorKnockback})
[void](New-MenuEntry $behaviour 'Sit on windows' {$script:windowPerch=-not $script:windowPerch; Save-VoiceSettings} {$script:windowPerch})
[void](New-MenuEntry $behaviour 'Throwing physics' {$script:throwPhysics=-not $script:throwPhysics; Save-VoiceSettings} {$script:throwPhysics})
[void](New-MenuEntry $behaviour 'Time-of-day moods' {$script:timeOfDay=-not $script:timeOfDay; Save-VoiceSettings} {$script:timeOfDay})
[void](New-MenuEntry $behaviour 'Battery reactions (laptops)' {$script:batteryReactions=-not $script:batteryReactions; Save-VoiceSettings} {$script:batteryReactions})
[void](New-MenuEntry $behaviour 'Bob to the real beat (listens to your PC''s sound)' {$script:beatSync=-not $script:beatSync; Save-VoiceSettings} {$script:beatSync})
[void](New-MenuEntry $behaviour 'React to music' {$script:musicEnabled=-not $script:musicEnabled; Save-VoiceSettings; if($script:musicEnabled -and -not $script:musicAvailable){Show-Status ('Music reactions are not available here: '+$script:musicError)}} {$script:musicEnabled})
[void](New-MenuEntry $behaviour 'Notice which app I''m using (stays on this PC)' {$script:appAware=-not $script:appAware; $script:appCategory=''; $script:appAnnounced=''; Save-VoiceSettings; if($script:appAware){Show-Status 'I''ll only look at which app is in front. Nothing leaves this PC.' 5}} {$script:appAware})
$hotkeyItem=New-MenuEntry $behaviour 'Summon hotkey' {
 $script:hotkeyEnabled=-not $script:hotkeyEnabled; Register-SummonHotkey; Save-VoiceSettings
 if($script:hotkeyEnabled){if($script:hotkeyOk){Show-Status ($script:tuning.hotkey+' calls me over. Press it again next to me to hide me.') 5}else{Show-Problem ('Hotkey not set: '+$script:hotkeyError)}}
} {$script:hotkeyEnabled}
Add-MenuSeparator $behaviour
$sleepMenu=New-SubMenu $behaviour 'Sleep after inactivity'
foreach($minutes in @(2,5,10,0)){
 $label=if($minutes -eq 0){'Never'}else{"$minutes minutes"}
 [void](New-MenuEntry $sleepMenu $label {param($sender,$event) Register-Interaction; $script:idleSleepAfter=[double]$sender.Tag} {param($item) [double]$item.Tag -eq $script:idleSleepAfter} ([double]($minutes*60)))
}
$breakMenu=New-SubMenu $behaviour 'Break reminders'
foreach($minutes in @(0,25,50,90)){
 $label=if($minutes -eq 0){'Off'}else{"Every $minutes minutes of screen time"}
 [void](New-MenuEntry $breakMenu $label {param($sender,$event) $script:breakMinutes=[int]$sender.Tag; $script:activeSince=$clock.Elapsed.TotalSeconds; Save-VoiceSettings} {param($item) [int]$item.Tag -eq $script:breakMinutes} $minutes)
}
$fullscreenMenu=New-SubMenu $behaviour 'When a fullscreen app is open'
foreach($entry in @(@('Hide until it closes','hide'),@('Sit quietly in the corner','busy'),@('Stay on top','ignore'))){
 [void](New-MenuEntry $fullscreenMenu $entry[0] {param($sender,$event) Set-FullscreenMode ([string]$sender.Tag)} {param($item) $item.Tag -eq $script:fullscreenMode} $entry[1])
}
$ratesMenu=New-SubMenu $behaviour 'Activity frequency'
foreach($entry in @(@('Attacks','attacks'),@('Wandering','wandering'),@('Resting','resting'),@('Antics','antics'),@('Talking','talking'),@('Rare antics','rare'))){
 $rateMenu=New-SubMenu $ratesMenu $entry[0]
 [void](New-MenuSlider $rateMenu ([string]$script:activityRates[$entry[1]]+'%  (0 = off, 100 = normal)') 0 200 25 $script:activityRates[$entry[1]] $entry[1] {param($sender,$event)
  $key=[string]$sender.Tag; $script:activityRates[$key]=[int]$sender.Value
  $sender.Parent.Children[0].Text=([string][int]$sender.Value+'%  (0 = off, 100 = normal)')
  $script:autoBag.Clear(); if($key -eq 'talking'){Schedule-Yap}; if($key -eq 'attacks' -and $sender.Value -eq 0){$script:nextRetry=[double]::PositiveInfinity}
 })
}
Add-MenuSeparator $settings
[void](New-MenuEntry $settings 'Start with Windows' {Set-StartWithWindows (-not (Test-Path -LiteralPath $script:startupLink))} {Test-Path -LiteralPath $script:startupLink})

$debugMenu=New-SubMenu $menu 'Debug and help'
[void](New-MenuEntry $debugMenu 'Movement status' {
 $areas=@(Get-AllWorkAreas); $idle=0; try{$idle=[int][CapriDesktop]::IdleSeconds()}catch{}
 $status="State: $($script:state)`nPaused: $($script:paused)`nBusy mode: $($script:busyMode)`nHidden: $($script:isHidden) (fullscreen: $($script:hiddenFullscreen))`nSleep: $($script:idleRest) $($script:sleepReason)`nHeld: $($script:mouseDown)`nDragging: $($script:dragging)`nVoice playing: $($script:voiceBusy)`nLive volume: $($script:liveVolume)`nMonitors: $($areas.Count)`nYou idle for: $idle s`nNative effects: $($script:fxNative)`nBeat: $(try{if([CapriBeat]::Running){'{0:0} BPM, confidence {1:0.00}' -f [CapriBeat]::Bpm,[CapriBeat]::Confidence}else{'not listening'}}catch{'unavailable'})`nFrame time: $($script:tickMs) ms`nWandering: $($script:activityRates.wandering)%"
 [void][Windows.MessageBox]::Show($status,'Capri movement status')
})
[void](New-MenuEntry $debugMenu 'Our history' {[void][Windows.MessageBox]::Show((Get-MemorySummary)+"`n`nBest catch-me score: "+$script:memory.bestCatch+"`n`n"+(Get-UnlockSummary),'What Capri remembers')})
[void](New-MenuEntry $debugMenu 'Run self-test' {$script:pendingAction='selftest'})
[void](New-MenuEntry $debugMenu 'Reload tuning.json' {
 Import-Tuning; Register-SummonHotkey
 if($script:tuningError){Show-Problem ('tuning.json has a problem: '+$script:tuningError); $script:tuningError=''}else{Show-Status 'Tuning reloaded.'}
})
[void](New-MenuEntry $debugMenu 'Wander now' {$script:pendingAction='wander-test'})
[void](New-MenuEntry $debugMenu 'Test cursor movement (75 px)' {$script:pendingAction='cursor-test'})
Add-MenuSeparator $debugMenu
foreach($entry in @(@('Controls and troubleshooting','README.txt'),@('Open Capri folder',''))){
 [void](New-MenuEntry $debugMenu $entry[0] {param($sender,$event) $path=$script:AppRoot; if($sender.Tag){$path=Join-Path $path ([string]$sender.Tag)}; Invoke-Item -LiteralPath $path} $null $entry[1])
}
[void](New-MenuEntry $debugMenu 'Open voice folder' {Invoke-Item -LiteralPath (Join-Path $script:AppRoot ('voices/'+$script:language))})

Add-MenuSeparator $menu
$moodItem=[Windows.Controls.MenuItem]::new(); $moodItem.IsEnabled=$false; [void]$menu.Items.Add($moodItem)
$rivalItem=[Windows.Controls.MenuItem]::new(); $rivalItem.IsEnabled=$false; [void]$menu.Items.Add($rivalItem)
[void](New-MenuEntry $menu 'Quit Capri' {$w.Close()})

$menu.Add_Opened({
 Register-Interaction
 foreach($sync in $script:menuSync){$sync[0].IsChecked=[bool](& $sync[1] $sync[0])}
 $moodItem.Header=('Mood: '+$script:mood)
 $rivalItem.Header=('Rivalry: '+$script:rivalWins+' catches / '+$script:rivalEscapes+' escapes')
 $hideItem.IsEnabled=($null -ne $script:tray)
 $hotkeyItem.Header=('Summon hotkey ('+$script:tuning.hotkey+')')
})
$menu.Add_Closed({Register-Interaction; Save-VoiceSettings})
$themePath=Join-Path $script:AppRoot 'menu-theme.xaml'
$theme=[Windows.Markup.XamlReader]::Load([System.Xml.XmlReader]::Create($themePath))
$menu.Resources.MergedDictionaries.Add($theme)
$menu.Style=$theme[[Windows.Controls.ContextMenu]]
$w.ContextMenu = $menu

# ---------------------------------------------------------------------------
# Tray icon: always reachable, even when she is asleep in a corner, busy, or hidden.
$script:tray=$null; $script:trayIconHandle=[IntPtr]::Zero
function New-TrayIconImage {
 $custom=Join-Path $script:AppRoot 'capri.ico'
 if(Test-Path -LiteralPath $custom){return [Drawing.Icon]::new($custom)}
 try {
  $size=32; $source=$poses[12]
  if($null -ne $script:spriteFeet){
   # Head and hair only: the whole figure is too small to read at tray size.
   $side=[int]($source.PixelWidth*0.42); $left=[int](($source.PixelWidth-$side)/2); $top=[int]([Math]::Max(0.0,$source.PixelHeight*0.41-$side*0.55))
   $source=[Windows.Media.Imaging.CroppedBitmap]::new($source,[Windows.Int32Rect]::new($left,$top,$side,[Math]::Min($side,$source.PixelHeight-$top)))
  }
  $ratio=[Math]::Min($size/$source.Width,$size/$source.Height); $dw=$source.Width*$ratio; $dh=$source.Height*$ratio
  $visual=[Windows.Media.DrawingVisual]::new(); $context=$visual.RenderOpen()
  $context.DrawImage($source,[Windows.Rect]::new(($size-$dw)/2,($size-$dh)/2,$dw,$dh)); $context.Close()
  $bitmap=[Windows.Media.Imaging.RenderTargetBitmap]::new($size,$size,96,96,[Windows.Media.PixelFormats]::Pbgra32); $bitmap.Render($visual)
  $encoder=[Windows.Media.Imaging.PngBitmapEncoder]::new(); $encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
  $stream=[IO.MemoryStream]::new(); $encoder.Save($stream); $stream.Position=0
  $drawing=[Drawing.Bitmap]::new($stream)
  $script:trayIconHandle=$drawing.GetHicon(); $drawing.Dispose(); $stream.Dispose()
  return [Drawing.Icon]::FromHandle($script:trayIconHandle)
 } catch { return [Drawing.Icon]::ExtractAssociatedIcon((Get-Process -Id $PID).Path) }
}
try {
 [Windows.Forms.Application]::EnableVisualStyles()
 $tray=[Windows.Forms.NotifyIcon]::new()
 $tray.Icon=New-TrayIconImage; $tray.Text='Capri'
 $trayMenu=[Windows.Forms.ContextMenuStrip]::new()
 $trayShow=$trayMenu.Items.Add('Hide Capri'); $trayShow.Add_Click({Set-Hidden (-not $script:isHidden)})
 $trayCall=$trayMenu.Items.Add('Call her over here'); $trayCall.Add_Click({Invoke-CallOver})
 [void]$trayMenu.Items.Add([Windows.Forms.ToolStripSeparator]::new())
 $trayPause=$trayMenu.Items.Add('Pause'); $trayPause.Add_Click({Set-Paused (-not $script:paused)})
 $trayBusy=$trayMenu.Items.Add('Busy (quiet corner)'); $trayBusy.Add_Click({Set-BusyMode (-not $script:busyMode)})
 $trayMute=$trayMenu.Items.Add('Mute'); $trayMute.Add_Click({Set-Muted (-not $script:muted)})
 [void]$trayMenu.Items.Add([Windows.Forms.ToolStripSeparator]::new())
 $traySettings=$trayMenu.Items.Add('Settings...'); $traySettings.Add_Click({Show-SettingsWindow})
 $trayFull=$trayMenu.Items.Add('Capri menu...'); $trayFull.Add_Click({
  Set-Hidden $false
  try{[void]$w.Activate()}catch{}
  $menu.PlacementTarget=$w; $menu.Placement=[Windows.Controls.Primitives.PlacementMode]::MousePoint; $menu.IsOpen=$true
 })
 $trayQuit=$trayMenu.Items.Add('Quit'); $trayQuit.Add_Click({$w.Close()})
 $trayMenu.Add_Opening({
  $trayShow.Text=if($script:isHidden){'Show Capri'}else{'Hide Capri'}
  $trayPause.Checked=$script:paused; $trayBusy.Checked=$script:busyMode; $trayMute.Checked=$script:muted
 })
 $tray.ContextMenuStrip=$trayMenu
 $tray.Add_DoubleClick({Set-Hidden (-not $script:isHidden)})
 $tray.Visible=$true
 $script:tray=$tray
} catch {
 # Without a tray icon, keep a taskbar button so she can always be found.
 $w.ShowInTaskbar=$true
 try { Add-Content -LiteralPath (Join-Path $script:AppRoot 'runtime-error.log') -Value ('Tray icon unavailable: '+$_.Exception.Message) -Encoding UTF8 } catch { }
}
