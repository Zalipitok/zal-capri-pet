# Mouse and keyboard.
# ---------------------------------------------------------------------------
# Mouse: a click is less than 5 pixels of travel; anything more is a drag (and a fast release is a throw).
$script:mouseDown=$false; $script:downPoint=$null
$script:shakeAngry=$false; $script:shakeNext=0.0
$script:shakeDirection=0; $script:shakeTravel=0.0; $script:shakeTurns=0
$script:shakeWindow=0.0; $script:shakeLastX=0.0
$w.Add_LocationChanged({
 if(-not $script:dragging){return}
 $now=$clock.Elapsed.TotalSeconds
 $script:dragSamples.Add([double[]]@($now,$w.Left,$w.Top))
 while($script:dragSamples.Count -gt 12){$script:dragSamples.RemoveAt(0)}
 if($now-$script:shakeWindow -gt 1.4){$script:shakeWindow=$now; $script:shakeTurns=0}
 $delta=$w.Left-$script:shakeLastX; $script:shakeLastX=$w.Left
 if([Math]::Abs($delta) -lt 2){return}
 $dir=[Math]::Sign($delta)
 if($dir -ne $script:shakeDirection){
  if($script:shakeTravel -ge 35){$script:shakeTurns++}
  $script:shakeTravel=0.0; $script:shakeDirection=$dir
 }
 $script:shakeTravel += [Math]::Abs($delta)
 if($script:shakeTurns -ge 4 -and $now -ge $script:shakeNext){
  $script:shakeAngry=$true; $script:shakeNext=$now+8; [void](Add-Memory 'shakes')
  $script:nextHoldRemark=$now+8; $script:messageUntil=0
  Set-Pose 20; [void](Show-Remark 'shaken')
 }
})
$w.Add_PreviewMouseLeftButtonDown({
 param($sender,$event)
 Register-Interaction
 if(Register-GameCatch){$script:mouseDown=$false; $event.Handled=$true; return}
 if(([Windows.Input.Keyboard]::Modifiers -band [Windows.Input.ModifierKeys]::Shift) -ne 0){
  Invoke-Swat; $script:mouseDown=$false; $event.Handled=$true; return
 }
 if(([Windows.Input.Keyboard]::Modifiers -band [Windows.Input.ModifierKeys]::Control) -ne 0){
  Invoke-HeadPat; $script:mouseDown=$false; $event.Handled=$true; return
 }
 $script:mouseDown=$true; $script:downPoint=$event.GetPosition($w)
 $script:heldLongEnough=$false; $script:nextHoldRemark=$clock.Elapsed.TotalSeconds+0.35
 if($event.ClickCount -eq 2){Begin-State 'dance'; Play-Voice 'click'; $script:mouseDown=$false}
})
$w.Add_MouseMove({
 param($sender,$event)
 if(-not $script:mouseDown -or $event.LeftButton -ne 'Pressed'){return}
 $point=$event.GetPosition($w)
 if([Math]::Abs($point.X-$script:downPoint.X)+[Math]::Abs($point.Y-$script:downPoint.Y) -lt 5){return}
 $script:mouseDown=$false; $script:dragging=$true; $script:dragSamples.Clear()
 $script:shakeAngry=$false; $script:shakeLastX=$w.Left; $script:shakeDirection=0; $script:shakeTravel=0.0; $script:shakeTurns=0; $script:shakeWindow=$clock.Elapsed.TotalSeconds
 Set-Pose 2; Play-Voice 'drag'
 $script:heldLongEnough=$true
 [void](Show-Remark 'hold'); $script:nextHoldRemark=$clock.Elapsed.TotalSeconds+5
 try{$w.DragMove()}finally{
  Register-Interaction; $script:dragging=$false
  $velocity=if($script:throwPhysics){Get-ReleaseVelocity}else{$null}
  $speed=if($null -ne $velocity){[Math]::Sqrt($velocity[0]*$velocity[0]+$velocity[1]*$velocity[1])}else{0}
  if($speed -gt $script:tuning.throwSpeed){
   Start-Throw ([Math]::Max(-4000.0,[Math]::Min(4000.0,$velocity[0]))) ([Math]::Max(-4000.0,[Math]::Min(4000.0,$velocity[1]))) 'thrown'
  }else{
   Clamp-Pet
   if($script:shakeAngry){$script:shakeAngry=$false; Begin-State 'shaken'; $script:caughtCooldown=$script:until+1}else{Begin-State 'smug'; [void](Show-Remark 'release')}
  }
 }
})
$w.Add_MouseLeftButtonUp({
 Register-Interaction
 if($script:mouseDown){$script:mouseDown=$false; if($script:state -ne 'wakeglare'){Begin-State 'wave'}; if($script:heldLongEnough){[void](Show-Remark 'release')}else{Play-Voice 'click'}}
})
$w.Add_MouseLeave({if([Windows.Input.Mouse]::LeftButton -ne 'Pressed'){$script:mouseDown=$false}})
$w.Add_KeyDown({ param($sender,$event) if($event.Key -eq 'Escape'){$w.Close()} })
$w.Add_SourceInitialized({$script:petHandle=[Windows.Interop.WindowInteropHelper]::new($w).Handle; Register-SummonHotkey})
$a = Get-WorkArea
Set-PetSize $script:petSize
$w.Left=$a.Right-$w.Width-40; $w.Top=$a.Bottom-$w.Height-40

# A bug in an event handler is logged and reported instead of taking Capri down.
$w.Dispatcher.Add_UnhandledException({param($sender,$event)
 try { Add-Content -LiteralPath (Join-Path $script:AppRoot 'runtime-error.log') -Value ((Get-Date -Format o)+' '+($event.Exception | Out-String)) -Encoding UTF8 } catch { }
 Show-Problem ('Something went wrong: '+$event.Exception.Message+' (see runtime-error.log)')
 $event.Handled=$true
})
