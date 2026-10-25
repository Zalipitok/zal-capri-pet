# Her powers from the show, played for fun:
#  - Possession (opt-in, Settings > Behaviour): she "takes over" the window you're using for two
#    seconds: a red Blackwall flicker over it and a small shake, then it goes back exactly where it was.
#  - Repairs: she "upgrades" your cursor with a red glitch trail for a minute. Only a picture follows
#    the pointer; the real cursor is never changed.
#  - Peeking: she hides behind the window you're using and peeks over its top edge.

# --- Possession --------------------------------------------------------------------------------
[xml]$possessXaml = @'
<Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
 WindowStyle="None" ResizeMode="NoResize" AllowsTransparency="True" Background="Transparent"
 Topmost="True" ShowInTaskbar="False" ShowActivated="False" IsHitTestVisible="False">
 <Grid>
  <Rectangle Name="Tint" Fill="#40FF1040"/>
  <Rectangle>
   <Rectangle.Fill>
    <DrawingBrush TileMode="Tile" Viewport="0,0,4,4" ViewportUnits="Absolute">
     <DrawingBrush.Drawing><GeometryDrawing Brush="#55B50025"><GeometryDrawing.Geometry><RectangleGeometry Rect="0,0,4,1"/></GeometryDrawing.Geometry></GeometryDrawing></DrawingBrush.Drawing>
    </DrawingBrush>
   </Rectangle.Fill>
  </Rectangle>
  <Border BorderBrush="#FF2246" BorderThickness="3"/>
  <TextBlock Name="Label" Text="SIGNAL HIJACKED // CAPRI" Foreground="#FF4564" FontFamily="Consolas" FontSize="22" FontWeight="Bold" HorizontalAlignment="Center" VerticalAlignment="Center"/>
 </Grid>
</Window>
'@
$possessWin=[Windows.Markup.XamlReader]::Load([System.Xml.XmlNodeReader]::new($possessXaml))
$possessLabel=$possessWin.FindName('Label')
$possessWin.Add_SourceInitialized({try{[CapriCursor]::MakeOverlay([Windows.Interop.WindowInteropHelper]::new($possessWin).Handle)}catch{}})
$script:possession=$null   # @{handle; x; y} while a window is possessed
function Start-Possession {
 $target=$null
 try{$fg=[CapriDesktop]::ForegroundWindow(); if($null -ne $fg){$target=[long]$fg[0]}}catch{}
 if($null -eq $target -and $script:lastAppWindow -ne 0){$target=$script:lastAppWindow}
 $buttonsUp=[Windows.Input.Mouse]::LeftButton -eq 'Released' -and [Windows.Input.Mouse]::RightButton -eq 'Released'
 if($null -eq $target -or -not $buttonsUp -or [CapriWindows]::Maximized($target)){return $false}
 $r=[CapriWindows]::Rect($target); if($null -eq $r){return $false}
 try{if([CapriDesktop]::FullscreenBlocks([IntPtr]::Zero)){return $false}}catch{}
 $script:possession=@{handle=$target; x=$r[0]; y=$r[1]; w=$r[2]-$r[0]; h=$r[3]-$r[1]}
 $m=Get-DeviceScale
 if($null -ne $m){
  $possessWin.Left=$r[0]/$m.M11; $possessWin.Top=$r[1]/$m.M22; $possessWin.Width=[Math]::Max(50.0,($r[2]-$r[0])/$m.M11); $possessWin.Height=[Math]::Max(50.0,($r[3]-$r[1])/$m.M22)
  $possessWin.Show()
 }
 return $true
}
function Stop-Possession {
 if($null -eq $script:possession){return}
 $p=$script:possession; $script:possession=$null
 try{[void][CapriWindows]::MoveTo($p.handle,$p.x,$p.y)}catch{}
 if($possessWin.IsVisible){$possessWin.Hide()}
}
function Invoke-Possession([switch]$Manual){
 if(-not $Manual -and -not $script:possessWindows){return $false}
 if($Manual){Prepare-ManualAction; $script:routine.Clear()}
 if(-not (Start-Possession)){if($Manual){Show-Status 'No window to possess. Click one first (not maximized).'}; return $false}
 Begin-State 'possess'
 [void](Show-Remark 'possess' -Force)
 return $true
}
$motions['possess']={
 $rotation.Angle=$script:direction*6+[Math]::Sin($phase*30)*2; $shift.X=[Math]::Sin($phase*40)*3
 $p=$script:possession
 if($null -eq $p){return}
 $buttonsUp=[Windows.Input.Mouse]::LeftButton -eq 'Released'
 if($phase -ge 2.2 -or -not $buttonsUp){Stop-Possession; return}
 # A shake that dies down, and a flickering tint over the window.
 $k=[Math]::Max(0.0,1-$phase/2.2)
 $dx=[int]([Math]::Sin($phase*47)*7*$k); $dy=[int]([Math]::Cos($phase*39)*4*$k)
 try{[void][CapriWindows]::MoveTo($p.handle,$p.x+$dx,$p.y+$dy)}catch{}
 $possessWin.Opacity=0.55+0.45*[Math]::Abs([Math]::Sin($phase*23))
 $possessLabel.Margin=[Windows.Thickness]::new([Math]::Sin($phase*31)*12,0,0,0)
}

# --- Cursor repairs ----------------------------------------------------------------------------
# A trail of small glitch squares follows the pointer: eight click-through windows that replay where
# the pointer was a moment ago.
$script:trailWindows=@(); $script:trailUntil=0.0; $script:trailHistory=[Collections.Generic.List[double[]]]::new()
function New-TrailWindow([int]$i){
 $win=[Windows.Window]::new(); $win.WindowStyle='None'; $win.ResizeMode='NoResize'; $win.AllowsTransparency=$true
 $win.Background=[Windows.Media.Brushes]::Transparent; $win.Topmost=$true; $win.ShowInTaskbar=$false; $win.ShowActivated=$false
 $size=14-$i; $win.Width=$size+6; $win.Height=$size+6; $win.IsHitTestVisible=$false
 $box=[Windows.Shapes.Rectangle]::new(); $box.Width=$size; $box.Height=[Math]::Max(3,$size*0.55)
 $box.Fill=[Windows.Media.BrushConverter]::new().ConvertFromString($(if($i%3 -eq 1){'#E000E5FF'}else{'#E0FF2246'}))
 $win.Content=$box; $win.Opacity=1.0-$i*0.1
 $win.Add_SourceInitialized({param($sender,$event) try{[CapriCursor]::MakeOverlay([Windows.Interop.WindowInteropHelper]::new($sender).Handle)}catch{}})
 return $win
}
function Start-CursorRepair([switch]$Manual){
 if($Manual){Prepare-ManualAction; $script:routine.Clear()}
 if($script:trailWindows.Count -eq 0){$script:trailWindows=@(for($i=0;$i -lt 8;$i++){New-TrailWindow $i})}
 $script:trailUntil=$clock.Elapsed.TotalSeconds+60; $script:trailHistory.Clear()
 Begin-State 'repaircursor'
 [void](Show-Remark 'cursorrepair' -Force)
}
function Update-CursorTrail([double]$t){
 if($script:trailWindows.Count -eq 0){return}
 $active=$t -lt $script:trailUntil -and -not $script:isHidden -and -not $script:exiting
 if(-not $active){
  if($script:trailWindows[0].IsVisible){foreach($win in $script:trailWindows){$win.Hide()}; if(-not $script:isHidden -and -not $script:exiting){[void](Show-Remark 'cursorrepairend')}}
  return
 }
 $m=Get-DeviceScale; if($null -eq $m){return}
 $p=$null; try{$p=[CapriCursor]::ReadPosition()}catch{return}
 $script:trailHistory.Insert(0,[double[]]@($p.X/$m.M11,$p.Y/$m.M22))
 while($script:trailHistory.Count -gt 26){$script:trailHistory.RemoveAt($script:trailHistory.Count-1)}
 for($i=0;$i -lt $script:trailWindows.Count;$i++){
  $win=$script:trailWindows[$i]; $index=[Math]::Min($script:trailHistory.Count-1,2+$i*3)
  $spot=$script:trailHistory[$index]
  $jitter=if($rng.NextDouble() -lt 0.15){$rng.Next(-6,7)}else{0}
  $win.Left=$spot[0]+8+$jitter; $win.Top=$spot[1]+10+$i
  if(-not $win.IsVisible){$win.Show()}
 }
}
$motions['repaircursor']={
 $rotation.Angle=$script:direction*3*[Math]::Sin($phase*2)
 if($phase -lt 1.2){$shift.X=-$script:direction*6}else{$shift.X=$script:direction*8*[Math]::Exp(-($phase-1.2)*4)}
}

# --- Peeking from behind a window --------------------------------------------------------------
$script:behindWindow=$false; $script:peekHandle=0L; $script:peekOffsetX=0.0
function Get-VisorOffset {
 try {
  $origin=$scene.TransformToAncestor($w).Transform([Windows.Point]::new(0,0))
  $offset=[Windows.Media.VisualTreeHelper]::GetOffset($sprite)
  return $origin.Y+$offset.Y+$sprite.ActualHeight*$script:visorFraction
 } catch { return $w.Height*0.45 }
}
function Restore-InFront {
 if(-not $script:behindWindow){return}
 $script:behindWindow=$false
 $w.Topmost=$false; $w.Topmost=$true
}
$paths['peekbehind']={
 if($script:peekHandle -eq 0){
  $perch=Find-PerchWindow
  if($null -eq $perch){Choose-State; return}
  $script:peekHandle=[long]$perch[0]; $script:peekOffsetX=($perch[1].Width-$w.Width)*(0.15+0.7*$rng.NextDouble())
 }
 $r=Get-WindowRectDip $script:peekHandle
 if($null -eq $r -or [CapriWindows]::Maximized($script:peekHandle)){Restore-InFront; $script:peekHandle=0; Choose-State; return}
 # Duck down and pop up again, eyes just over the edge.
 $bob=if($phase -lt 1.6){0.0}else{18.0*(0.5+0.5*[Math]::Cos(($phase-1.6)*1.7))}
 $script:targetX=$r.Left+$script:peekOffsetX
 $script:targetY=$r.Top-(Get-VisorOffset)-14+$bob
 $close=[Math]::Abs($script:targetX-$w.Left)+[Math]::Abs($script:targetY-$w.Top) -lt 10
 if(-not $script:behindWindow -and ($close -or $phase -gt 1.6)){
  $script:behindWindow=$true
  $w.Topmost=$false
  try{[void][CapriWindows]::PlaceBehind($script:petHandle,$script:peekHandle)}catch{}
 }
 if($script:behindWindow){$w.Left=$script:targetX; $w.Top=$script:targetY}
}
$motions['peekbehind']={$rotation.Angle=[Math]::Sin($phase*1.3)*4}
# Leaving the state for any reason brings her back on top.
function Update-Powers([double]$t){
 if($null -ne $script:possession -and ($script:paused -or $menu.IsOpen -or $script:busyMode -or $script:isHidden -or $script:dragging -or $script:mouseDown)){Stop-Possession}
 if($script:state -ne 'peekbehind'){$script:peekHandle=0; Restore-InFront}
 if($null -ne $script:possession -and $script:state -ne 'possess'){Stop-Possession}
 Update-CursorTrail $t
}
