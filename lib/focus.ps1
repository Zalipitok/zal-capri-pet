# Focus timer: "25 minutes of work". She waits quietly in her corner with a countdown under her,
# cheers when you finish, and keeps count of your sessions and your daily streak (memory.json).
# Turning Busy off before the end gives up the session.
$script:focus=@{active=$false; minutes=0; ends=[DateTime]::MinValue; started=[DateTime]::MinValue}
$script:nextFocusTick=0.0
function Start-Focus([int]$minutes,[switch]$Quiet){
 if($script:FriendMode){return}
 $minutes=[Math]::Max(1,[Math]::Min(180,$minutes))
 $script:focus.active=$true; $script:focus.minutes=$minutes
 $script:focus.started=Get-Date; $script:focus.ends=(Get-Date).AddMinutes($minutes)
 # The session owns Busy mode now, whatever turned it on before.
 $script:busyByDnd=$false; $script:busyByFullscreen=$false
 if(-not $script:busyMode){Set-BusyMode $true -Auto}
 $script:nextFocusTick=0
 if(-not $Quiet){[void](Show-Remark 'focusstart' -Force -Value $minutes)}
}
function Stop-Focus([switch]$GaveUp){
 if(-not $script:focus.active){return}
 $script:focus.active=$false
 $focusBadge.Visibility='Collapsed'
 if($null -ne $script:tray){try{$script:tray.Text=if($script:FriendMode){'Capri (friend)'}else{'Capri'}}catch{}}
 if($GaveUp){[void](Show-Remark 'focusquit' -Force); return}
 $done=[int][Math]::Round(((Get-Date)-$script:focus.started).TotalMinutes)
 [void](Add-Memory 'focusSessions'); $script:memory.focusMinutes=[int]$script:memory.focusMinutes+$done
 # Streak: days in a row with at least one finished session.
 $today=(Get-Date).ToString('yyyy-MM-dd'); $yesterday=(Get-Date).AddDays(-1).ToString('yyyy-MM-dd')
 if($script:memory.lastFocusDay -ne $today){
  $script:memory.focusStreak=if($script:memory.lastFocusDay -eq $yesterday){[int]$script:memory.focusStreak+1}else{1}
  $script:memory.lastFocusDay=$today
 }
 $script:memoryDirty=$true
 if($script:busyMode){Set-BusyMode $false -Auto}
 Play-Sfx 'reminder.wav' 0.8 -Important
 if($null -ne $script:tray -and -not $script:SelfTestMode){try{$script:tray.ShowBalloonTip(6000,'Focus done',''+$done+' minutes. Streak: '+$script:memory.focusStreak+' day(s).',[Windows.Forms.ToolTipIcon]::Info)}catch{}}
 if(Test-CanReact){$script:routine.Clear(); Begin-State 'celebrate'}
 [void](Show-Remark 'focusdone' -Force -Value @($done,$script:memory.focusStreak))
 $script:messageUntil=$clock.Elapsed.TotalSeconds+8
}
function Update-Focus([double]$t){
 if(-not $script:focus.active -or $t -lt $script:nextFocusTick){return}
 $script:nextFocusTick=$t+0.5
 if(-not $script:busyMode){Stop-Focus -GaveUp; return} # you turned Busy off
 $left=$script:focus.ends-(Get-Date)
 if($left.TotalSeconds -le 0){Stop-Focus; return}
 $text='FOCUS '+[int][Math]::Floor($left.TotalMinutes)+':'+$left.Seconds.ToString('00')
 $focusText.Text=$text
 if($focusBadge.Visibility -ne 'Visible'){$focusBadge.Visibility='Visible'}
 if($null -ne $script:tray){try{$script:tray.Text='Capri - '+$text.ToLowerInvariant()+' left'}catch{}}
}
