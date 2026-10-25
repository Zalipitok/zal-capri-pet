# Reminders: "remind me in 20 minutes" from the menu, the tray or the chat box. She flies over and
# tells you, with a chime and a Windows notification. Saved in reminders.json, so a restart keeps them.
$script:reminders=[Collections.Generic.List[object]]::new()
$script:remindersPath=Join-Path $script:AppRoot 'reminders.json'
$script:lastReminder=$null; $script:nextReminderCheck=0.0; $script:reminderWindow=$null
try {
 if(Test-Path -LiteralPath $script:remindersPath){
  $decodedReminders=Get-Content -LiteralPath $script:remindersPath -Raw -Encoding UTF8 | ConvertFrom-Json
  foreach($entry in @(foreach($item in $decodedReminders){$item})){
   if($entry.text -and $entry.at){$script:reminders.Add([pscustomobject]@{at=[DateTime]::Parse([string]$entry.at,[Globalization.CultureInfo]::InvariantCulture,[Globalization.DateTimeStyles]::RoundtripKind); text=[string]$entry.text})}
  }
 }
} catch { $script:remindersError=$_.Exception.Message }
function Save-Reminders {
 if($script:SelfTestMode -or $script:FriendMode){return}
 try {
  $list=@($script:reminders | Sort-Object at | ForEach-Object {[ordered]@{at=$_.at.ToString('o'); text=$_.text}})
  if($list.Count -eq 0){if(Test-Path -LiteralPath $script:remindersPath){Remove-Item -LiteralPath $script:remindersPath}; return}
  ConvertTo-Json -InputObject @($list) | Set-Content -LiteralPath $script:remindersPath -Encoding UTF8
 } catch { Show-Problem ('Reminders could not be saved: '+$_.Exception.Message) }
}
# "20" (minutes), "20m", "1h30", "1 hour 15 min", "half an hour", "17:45", "5pm", "tomorrow 9:00".
# Returns the moment, or $null when the text isn't understood.
function ConvertFrom-WhenText([string]$text,[DateTime]$now=(Get-Date)){
 $s=$text.Trim().ToLowerInvariant() -replace '^(in|at)\s+','' -replace '\s+from now$',''
 if(-not $s){return $null}
 $dayOffset=0
 if($s -match '^tomorrow\s*(at\s+)?(.*)$'){$dayOffset=1; $s=$matches[2].Trim(); if(-not $s){$s='9:00'}}
 if($s -match '^(\d{1,2})(?:[:.](\d{2}))?\s*(am|pm)$' -or $s -match '^(\d{1,2})[:.](\d{2})()$'){
  $hour=[int]$matches[1]; $minute=if($matches[2]){[int]$matches[2]}else{0}
  if($matches[3] -eq 'pm' -and $hour -lt 12){$hour+=12}elseif($matches[3] -eq 'am' -and $hour -eq 12){$hour=0}
  if($hour -gt 23 -or $minute -gt 59){return $null}
  $at=$now.Date.AddDays($dayOffset).AddHours($hour).AddMinutes($minute)
  if($dayOffset -eq 0 -and $at -le $now){$at=$at.AddDays(1)}
  return $at
 }
 if($dayOffset -gt 0){return $null}
 if($s -match '^\d{1,4}$'){return $now.AddMinutes([int]$s)}
 if($s -match '^(\d{1,3})\s*h\s*(\d{1,2})$'){return $now.AddHours([int]$matches[1]).AddMinutes([int]$matches[2])}
 $s=$s -replace '\bhalf an? hour\b','30 min' -replace '\ban? hour\b','1 hour' -replace '\ban? minute\b','1 min' -replace '\ba couple of\b','2' -replace '\ba few\b','3'
 $pattern='(\d+(?:[.,]\d+)?)\s*(hours|hour|hrs|hr|h|minutes|minute|mins|min|m|seconds|second|secs|sec|s)\b'
 $found=[regex]::Matches($s,$pattern)
 if($found.Count -eq 0){return $null}
 # Anything left besides the amounts (other than "and" and commas) means we didn't understand it.
 if(([regex]::Replace($s,$pattern,'') -replace '\band\b','' -replace '[,\s]','') -ne ''){return $null}
 $seconds=0.0
 foreach($m in $found){
  $n=[double]($m.Groups[1].Value -replace ',','.')
  $unit=$m.Groups[2].Value
  if($unit.StartsWith('h')){$seconds+=$n*3600}elseif($unit.StartsWith('m')){$seconds+=$n*60}else{$seconds+=$n}
 }
 if($seconds -le 0){return $null}
 return $now.AddSeconds($seconds)
}
function Format-ReminderTime([DateTime]$at){
 $now=Get-Date
 $clockText=$at.ToString('HH:mm')
 if($at.Date -eq $now.Date){return $clockText}
 if($at.Date -eq $now.Date.AddDays(1)){return 'tomorrow '+$clockText}
 return $at.ToString('ddd d MMM HH:mm')
}
function Add-Reminder([DateTime]$at,[string]$text){
 $text=$text.Trim(); if(-not $text){$text='Time''s up.'}
 if($text.Length -gt 140){$text=$text.Substring(0,140)}
 $script:reminders.Add([pscustomobject]@{at=$at; text=$text})
 Save-Reminders; & $script:refreshReminderList
 Show-Status ('Got it. '+(Format-ReminderTime $at)+': '+$text) 4
 return $true
}
function Update-Reminders([double]$t){
 if($script:FriendMode -or $t -lt $script:nextReminderCheck -or $script:reminders.Count -eq 0){return}
 $script:nextReminderCheck=$t+1
 $now=Get-Date
 $due=@($script:reminders | Where-Object {$_.at -le $now} | Sort-Object at)
 if($due.Count -eq 0){return}
 $next=$due[0]; [void]$script:reminders.Remove($next); Save-Reminders; & $script:refreshReminderList
 Send-Reminder $next ($now-$next.at).TotalMinutes
}
# Delivery: she comes to the pointer (unless hidden or in the quiet corner) and says it, and Windows
# shows it as a notification too, so it isn't missed when she's hidden.
function Send-Reminder($reminder,[double]$minutesLate=0){
 $script:lastReminder=@{text=$reminder.text; at=Get-Date}
 if($null -ne $script:tray -and -not $script:SelfTestMode){try{$script:tray.ShowBalloonTip(10000,'Capri reminder',$reminder.text,[Windows.Forms.ToolTipIcon]::Info)}catch{}}
 Play-Sfx 'reminder.wav' 0.9 -Important
 if($script:isHidden){return}
 Stop-Voice
 if($script:idleRest -ne 'awake'){$script:idleRest='awake'; $script:sleepReason=''; $script:lastInteraction=$clock.Elapsed.TotalSeconds}
 if(-not $script:busyMode -and -not $script:dragging -and $script:state -ne 'catchgame'){$script:routine.Clear(); Begin-State 'remind'}
 if($minutesLate -ge 5){$shown=Show-Remark 'reminderlate' -Force -Value @($reminder.text,$reminder.at.ToString('HH:mm'))}
 else{$shown=Show-Remark 'reminder' -Force -Value $reminder.text}
 if($shown){$script:messageUntil=$clock.Elapsed.TotalSeconds+15; $script:nextRemark=$clock.Elapsed.TotalSeconds+40}
}
function Invoke-SnoozeReminder([int]$minutes=10){
 if($null -eq $script:lastReminder){return}
 [void](Add-Reminder ((Get-Date).AddMinutes($minutes)) $script:lastReminder.text)
 $script:lastReminder=$null
}
function Test-CanSnooze {return $null -ne $script:lastReminder -and ((Get-Date)-$script:lastReminder.at).TotalMinutes -lt 30}

# The reminders window: what, when, and the list of what's coming up.
$script:refreshReminderList={
 if($null -eq $script:remList){return}
 $script:remList.Items.Clear()
 foreach($r in @($script:reminders | Sort-Object at)){$item=[Windows.Controls.ListBoxItem]::new(); $item.Content=(Format-ReminderTime $r.at)+'   '+$r.text; $item.Tag=$r; [void]$script:remList.Items.Add($item)}
 if($script:remList.Items.Count -eq 0){[void]$script:remList.Items.Add('(nothing yet)')}
}
function Show-ReminderWindow {
 if($null -ne $script:reminderWindow){try{[void]$script:reminderWindow.Activate()}catch{}; return}
 $brush={param($hex) [Windows.Media.BrushConverter]::new().ConvertFromString($hex)}
 $dialog=[Windows.Window]::new(); $dialog.Title='Capri // Reminders'; $dialog.Width=440; $dialog.SizeToContent='Height'
 $dialog.WindowStartupLocation='CenterScreen'; $dialog.Background=(& $brush '#111722'); $dialog.Foreground=[Windows.Media.Brushes]::White
 $dialog.ResizeMode='NoResize'
 $stack=[Windows.Controls.StackPanel]::new(); $stack.Margin=[Windows.Thickness]::new(16); $dialog.Content=$stack
 $label={param($text) $b=[Windows.Controls.TextBlock]::new(); $b.Text=$text; $b.Margin=[Windows.Thickness]::new(0,8,0,3); $b}
 [void]$stack.Children.Add((& $label 'Remind me to:'))
 $what=[Windows.Controls.TextBox]::new(); $script:remWhat=$what; $what.Padding=[Windows.Thickness]::new(4); [void]$stack.Children.Add($what)
 [void]$stack.Children.Add((& $label 'When:'))
 $when=[Windows.Controls.TextBox]::new(); $script:remWhen=$when; $when.Padding=[Windows.Thickness]::new(4); $when.Text='20'; [void]$stack.Children.Add($when)
 $hint=[Windows.Controls.TextBlock]::new(); $hint.Text='20 = in 20 minutes.  Also: 1h30, 45 min, 17:45, 5pm, tomorrow 9:00'; $hint.FontSize=11; $hint.Opacity=0.65; $hint.Margin=[Windows.Thickness]::new(0,3,0,6)
 [void]$stack.Children.Add($hint)
 $quick=[Windows.Controls.WrapPanel]::new(); [void]$stack.Children.Add($quick)
 foreach($q in @('5','10','25','1h','2h')){
  $b=[Windows.Controls.Button]::new(); $b.Content=$q; $b.Tag=$q; $b.Margin=[Windows.Thickness]::new(0,0,6,6); $b.Padding=[Windows.Thickness]::new(10,3,10,3)
  $b.Add_Click({param($sender,$event) $script:remWhen.Text=[string]$sender.Tag}); [void]$quick.Children.Add($b)
 }
 $status=[Windows.Controls.TextBlock]::new(); $script:remStatus=$status; $status.TextWrapping='Wrap'; $status.Margin=[Windows.Thickness]::new(0,4,0,4); $status.Foreground=(& $brush '#FCEE0A')
 $add=[Windows.Controls.Button]::new(); $add.Content='Add reminder'; $add.IsDefault=$true; $add.HorizontalAlignment='Left'; $add.Padding=[Windows.Thickness]::new(14,5,14,5); $add.Margin=[Windows.Thickness]::new(0,6,0,0)
 [void]$stack.Children.Add($add); [void]$stack.Children.Add($status)
 [void]$stack.Children.Add((& $label 'Coming up:'))
 $list=[Windows.Controls.ListBox]::new(); $script:remList=$list; $list.Height=130; [void]$stack.Children.Add($list)
 $remove=[Windows.Controls.Button]::new(); $remove.Content='Remove selected'; $remove.HorizontalAlignment='Left'; $remove.Padding=[Windows.Thickness]::new(10,4,10,4); $remove.Margin=[Windows.Thickness]::new(0,6,0,0)
 [void]$stack.Children.Add($remove)
 & $script:refreshReminderList
 $add.Add_Click({
  $at=ConvertFrom-WhenText $script:remWhen.Text
  if($null -eq $at){$script:remStatus.Text='I don''t understand "'+$script:remWhen.Text+'". Try 20, 1h30 or 17:45.'; return}
  [void](Add-Reminder $at $script:remWhat.Text)
  $script:remStatus.Text='Added for '+(Format-ReminderTime $at)+'.'; $script:remWhat.Text=''; & $script:refreshReminderList; [void]$script:remWhat.Focus()
 })
 $remove.Add_Click({
  $item=$script:remList.SelectedItem
  if($item -is [Windows.Controls.ListBoxItem]){[void]$script:reminders.Remove($item.Tag); Save-Reminders; & $script:refreshReminderList}
 })
 $dialog.Add_Closed({$script:reminderWindow=$null; $script:remList=$null; Register-Interaction})
 $script:reminderWindow=$dialog
 $dialog.Show(); [void]$what.Focus()
}
