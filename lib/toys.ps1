# Toys: a ball you can throw around the desktop. She chases it, bats it back, and now and then
# steals it and stashes it in a corner. Interact > Toy ball shows or puts it away.
[xml]$ballXaml = @'
<Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
 Title="Capri's ball" Width="44" Height="44" WindowStyle="None" ResizeMode="NoResize"
 AllowsTransparency="True" Background="Transparent" Topmost="True" ShowInTaskbar="False" ShowActivated="False">
 <Grid Name="Ball" RenderTransformOrigin="0.5,0.5" Cursor="Hand">
  <Grid.RenderTransform><TransformGroup><ScaleTransform/><RotateTransform/></TransformGroup></Grid.RenderTransform>
  <Ellipse Stroke="#0A0A0A" StrokeThickness="2">
   <Ellipse.Fill><RadialGradientBrush GradientOrigin="0.35,0.3">
    <GradientStop Color="#FFFBC8" Offset="0"/><GradientStop Color="#FCEE0A" Offset="0.45"/><GradientStop Color="#B89C00" Offset="1"/>
   </RadialGradientBrush></Ellipse.Fill>
  </Ellipse>
  <Path Data="M 4,22 Q 22,10 40,22" Stroke="#0A0A0A" StrokeThickness="3"/>
  <Path Data="M 6,27 Q 22,16 38,27" Stroke="#00E5FF" StrokeThickness="1.5"/>
  <Ellipse Width="7" Height="7" Fill="#FF2A6D" HorizontalAlignment="Right" VerticalAlignment="Bottom" Margin="0,0,9,8"/>
 </Grid>
</Window>
'@
$ballWin=[Windows.Markup.XamlReader]::Load([System.Xml.XmlNodeReader]::new($ballXaml))
$ballGrid=$ballWin.FindName('Ball')
$ballSquash=$ballGrid.RenderTransform.Children[0]; $ballSpin=$ballGrid.RenderTransform.Children[1]
$script:ball=@{shown=$false; x=0.0; y=0.0; vx=0.0; vy=0.0; carried=$false; dragging=$false; squash=0.0; hits=0; hitsAllowed=3; lastHit=-10.0}
$script:ballSamples=[Collections.Generic.List[double[]]]::new()
$script:ballSize=44.0
$script:ballStashX=0.0; $script:ballStashY=0.0

function Test-BallAvailable {return $script:ball.shown -and -not $script:ball.carried -and -not $script:ball.dragging}
function Get-BallArea {
 $m=Get-DeviceScale
 if($null -eq $m){return Get-WorkArea}
 $r=$null; try{$r=[CapriDesktop]::WorkAreaAt([int](($script:ball.x+$script:ballSize/2)*$m.M11),[int](($script:ball.y+$script:ballSize/2)*$m.M22))}catch{}
 if($null -eq $r){return Get-WorkArea}
 return ConvertFrom-DeviceRect $r $m
}
function Set-BallShown([bool]$on){
 $script:ball.shown=$on; $script:ball.carried=$false
 if($on){
  # Dropped in from above her, with a little sideways toss.
  $script:ball.x=$w.Left+$w.Width/2-$script:ballSize/2; $script:ball.y=$w.Top+90
  $script:ball.vx=($rng.NextDouble()-0.5)*500; $script:ball.vy=-300
  $ballWin.Left=$script:ball.x; $ballWin.Top=$script:ball.y
  if(-not $script:isHidden){$ballWin.Show()}
  [void](Show-Remark 'ballout')
 }else{
  $ballWin.Hide()
  if($script:state -in @('chaseball','ballbat','ballsteal')){Choose-State}
 }
}
# Her body, in screen units: where a ball counts as touching her.
function Test-BallTouch {
 $cx=$script:ball.x+$script:ballSize/2; $cy=$script:ball.y+$script:ballSize/2
 $feet=$w.Top+$w.Height-(Get-SpriteFoot ([int]$script:poseIndex))
 $top=$feet-$sprite.ActualHeight*0.75
 return [Math]::Abs($cx-($w.Left+$w.Width/2)) -lt [Math]::Max(30.0,$w.Width*0.3) -and $cy -gt $top -and $cy -lt $feet+$script:ballSize/2
}
function Start-BallChase {
 if(-not (Test-BallAvailable)){return}
 $script:ball.hits=0; $script:ball.hitsAllowed=$rng.Next(2,5)
 Begin-State 'chaseball'
}
function Hit-Ball {
 $now=$clock.Elapsed.TotalSeconds
 if($now-$script:ball.lastHit -lt 0.4){return}
 $script:ball.lastHit=$now; $script:ball.hits++
 $cx=$script:ball.x+$script:ballSize/2; $side=if($cx -ge $w.Left+$w.Width/2){1.0}else{-1.0}
 if($script:ball.hits -ge $script:ball.hitsAllowed -and $rng.NextDouble() -lt 0.45){
  # Mine now.
  $script:ball.carried=$true; $script:ball.vx=0; $script:ball.vy=0
  $a=Get-WorkArea
  $script:ballStashX=if($rng.Next(2) -eq 0){$a.Left}else{$a.Right-$w.Width}
  $script:ballStashY=$a.Bottom-$w.Height
  Begin-State 'ballsteal'
  [void](Show-Remark 'ballsteal' -Force)
  return
 }
 $script:ball.vx=$side*(450+$rng.NextDouble()*550); $script:ball.vy=-(600+$rng.NextDouble()*600); $script:ball.squash=1.0
 Play-Sfx 'ballhit.wav' 0.6
 if($script:ball.hits -ge $script:ball.hitsAllowed+2){Begin-State 'smug'; [void](Show-Remark 'ballbored')}
 else{Begin-State 'ballbat'; if($rng.NextDouble() -lt 0.3){[void](Show-Remark 'ballbat')}}
}
function Get-BallVelocity {
 $samples=$script:ballSamples
 if($samples.Count -lt 2){return @(0.0,0.0)}
 $now=$clock.Elapsed.TotalSeconds; $last=$samples[$samples.Count-1]
 if($now-$last[0] -gt 0.09){return @(0.0,0.0)}
 $first=$samples[0]
 for($i=$samples.Count-2;$i -ge 0;$i--){$first=$samples[$i]; if($last[0]-$samples[$i][0] -ge 0.06){break}}
 $span=$last[0]-$first[0]; if($span -le 0.005){return @(0.0,0.0)}
 return @((($last[1]-$first[1])/$span),(($last[2]-$first[2])/$span))
}
$ballWin.Add_LocationChanged({
 if(-not $script:ball.dragging){return}
 $script:ballSamples.Add([double[]]@($clock.Elapsed.TotalSeconds,$ballWin.Left,$ballWin.Top))
 while($script:ballSamples.Count -gt 12){$script:ballSamples.RemoveAt(0)}
})
$ballWin.Add_MouseLeftButtonDown({param($sender,$event)
 if($script:ball.carried){
  # Taking it back from her.
  $script:ball.carried=$false
  if(Test-CanReact){Begin-State 'grumpy'}
  [void](Show-Remark 'balltaken' -Force)
 }
 $script:ball.dragging=$true; $script:ballSamples.Clear()
 try{$ballWin.DragMove()}catch{}finally{
  $script:ball.dragging=$false
  $script:ball.x=$ballWin.Left; $script:ball.y=$ballWin.Top
  $v=Get-BallVelocity
  $script:ball.vx=[Math]::Max(-3500.0,[Math]::Min(3500.0,[double]$v[0])); $script:ball.vy=[Math]::Max(-3500.0,[Math]::Min(3500.0,[double]$v[1]))
  $speed=[Math]::Sqrt($script:ball.vx*$script:ball.vx+$script:ball.vy*$script:ball.vy)
  # A proper throw: she fetches it.
  if($speed -gt 300 -and (Test-CanReact) -and $script:state -notin @('chaseball','ballbat','catchgame')){
   Register-Interaction; Prepare-ManualAction; $script:routine.Clear(); Start-BallChase
   if($rng.NextDouble() -lt 0.5){[void](Show-Remark 'ballfetch')}
  }
 }
})
$ballWin.Add_MouseRightButtonUp({Set-BallShown $false})
$ballWin.Add_SourceInitialized({try{$h=[Windows.Interop.WindowInteropHelper]::new($ballWin).Handle; [CapriCursor]::SetWindowLong($h,-20,[CapriCursor]::GetWindowLong($h,-20) -bor 0x08000000 -bor 0x80) | Out-Null}catch{}})

function Update-Ball([double]$t,[double]$dt){
 if(-not $script:ball.shown){return}
 if($script:isHidden -or $script:exiting){if($ballWin.IsVisible){$ballWin.Hide()}; return}
 if(-not $ballWin.IsVisible){$ballWin.Show()}
 if($script:ball.dragging){return}
 $b=$script:ball; $size=$script:ballSize
 if($b.carried -and $script:state -ne 'ballsteal' -and -not $script:dragging){$b.carried=$false; $b.vx=0; $b.vy=0}
 if($b.carried){
  # In her hands, on the side she's facing.
  $feet=$w.Top+$w.Height-(Get-SpriteFoot ([int]$script:poseIndex))
  $b.x=$w.Left+$w.Width/2-$script:direction*$w.Width*0.2-$size/2
  $b.y=$feet-$sprite.ActualHeight*0.42
  $ballWin.Left=$b.x; $ballWin.Top=$b.y; $ballSpin.Angle=0
  return
 }
 $a=Get-BallArea
 $bounce=[double]$script:tuning.ballBounce
 $b.vy+=[double]$script:tuning.gravity*0.8*$dt
 $nx=$b.x+$b.vx*$dt; $ny=$b.y+$b.vy*$dt
 if($nx -lt $a.Left){$nx=$a.Left; $b.vx=[Math]::Abs($b.vx)*$bounce; $b.squash=0.6}
 elseif($nx -gt $a.Right-$size){$nx=$a.Right-$size; $b.vx=-[Math]::Abs($b.vx)*$bounce; $b.squash=0.6}
 if($ny -lt $a.Top){$ny=$a.Top; $b.vy=[Math]::Abs($b.vy)*$bounce}
 $floor=$a.Bottom-$size
 if($ny -ge $floor){
  $ny=$floor
  if($b.vy -gt 140){$b.squash=[Math]::Min(1.0,$b.vy/900); $b.vy=-$b.vy*$bounce}else{$b.vy=0}
  $b.vx*=[Math]::Max(0.0,1-1.4*$dt) # rolling friction
  if([Math]::Abs($b.vx) -lt 4){$b.vx=0}
 }
 $b.x=$nx; $b.y=$ny
 $ballSpin.Angle=($ballSpin.Angle+$b.vx*$dt/($size/2)*180/[Math]::PI)%360
 $b.squash=[Math]::Max(0.0,$b.squash-$dt*6)
 $ballSquash.ScaleY=1-0.22*$b.squash; $ballSquash.ScaleX=1+0.15*$b.squash
 $ballWin.Left=$b.x; $ballWin.Top=$b.y
}

# She goes for it: aim so the ball meets her body, swat it on contact.
$paths['chaseball']={
 if(-not (Test-BallAvailable)){Begin-State 'smug'; return}
 $cx=$script:ball.x+$script:ballSize/2; $cy=$script:ball.y+$script:ballSize/2
 $mid=$w.Left+$w.Width/2
 $side=if($mid -lt $cx){-1.0}else{1.0}
 $script:targetX=$cx-$w.Width/2+$side*$w.Width*0.12
 $script:targetY=$cy+30-($w.Height-(Get-SpriteFoot ([int]$script:poseIndex)))
 if(Test-BallTouch){Hit-Ball}
}
$paths['ballsteal']={
 $script:targetX=$script:ballStashX; $script:targetY=$script:ballStashY
 if(-not $script:ball.carried){Begin-State 'grumpy'; return}
 if([Math]::Abs($script:targetX-$w.Left)+[Math]::Abs($script:targetY-$w.Top) -lt 8 -or $phase -gt 6){
  # Stashed: it drops beside her and she guards it.
  $script:ball.carried=$false; $script:ball.vx=0; $script:ball.vy=0
  Begin-State 'ballguard'
  [void](Show-Remark 'ballhide' -Force)
 }
}
$motions['chaseball']={$rotation.Angle=-$script:direction*14; $scale.ScaleX=$script:direction*1.04; $scale.ScaleY=0.95}
$motions['ballsteal']={$rotation.Angle=-$script:direction*6; $shift.Y=-[Math]::Abs([Math]::Sin($phase*6))*5}
$motions['ballguard']={$rotation.Angle=-6+[Math]::Sin($phase*1.5)*2}
