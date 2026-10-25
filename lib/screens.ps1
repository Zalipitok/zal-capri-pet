# Monitors, work areas and windows.
# ---------------------------------------------------------------------------
# Screens and windows. WPF units = physical pixels / system DPI scale.
function Get-DeviceScale {
 $source=$null; try{$source=[Windows.PresentationSource]::FromVisual($w)}catch{}
 if($null -eq $source -or $null -eq $source.CompositionTarget){return $null}
 return $source.CompositionTarget.TransformToDevice
}
function ConvertFrom-DeviceRect($r,$m){
 if($null -eq $r){return $null}
 return [Windows.Rect]::new($r[0]/$m.M11,$r[1]/$m.M22,[Math]::Max(1.0,($r[2]-$r[0])/$m.M11),[Math]::Max(1.0,($r[3]-$r[1])/$m.M22))
}
# Work area of the monitor Capri's centre is on (primary monitor before the window exists).
function Get-WorkArea {
 $m=Get-DeviceScale
 if($null -eq $m -or [double]::IsNaN($w.Left)){return [Windows.SystemParameters]::WorkArea}
 $r=$null; try{$r=[CapriDesktop]::WorkAreaAt([int](($w.Left+$w.Width/2)*$m.M11),[int](($w.Top+$w.Height/2)*$m.M22))}catch{}
 if($null -eq $r){return [Windows.SystemParameters]::WorkArea}
 return ConvertFrom-DeviceRect $r $m
}
function Get-AllWorkAreas {
 $m=Get-DeviceScale; if($null -eq $m){return @()}
 $flat=@(); try{$flat=@([CapriDesktop]::AllWorkAreas())}catch{}
 return @(for($i=0;$i+3 -lt $flat.Count;$i+=4){ConvertFrom-DeviceRect @($flat[$i],$flat[$i+1],$flat[$i+2],$flat[$i+3]) $m})
}
function Clamp-Pet {
 if($script:spec.tags.Contains('noClamp')){return}
 $a=Get-WorkArea
 $w.Left = [Math]::Max($a.Left, [Math]::Min($w.Left, [Math]::Max($a.Left,$a.Right-$w.Width)))
 $w.Top = [Math]::Max($a.Top, [Math]::Min($w.Top, [Math]::Max($a.Top,$a.Bottom-$w.Height)))
}
# Distance from the window's bottom edge to the bottom of the drawn sprite, in WPF units.
# With the normalised sprites this is where her feet are drawn in the given pose (default: hover, 12).
function Get-SpriteFoot([int]$pose=12) {
 try {
  $origin=$scene.TransformToAncestor($w).Transform([Windows.Point]::new(0,0))
  $offset=[Windows.Media.VisualTreeHelper]::GetOffset($sprite)
  $feet=if($null -ne $script:spriteFeet -and $pose -lt $script:spriteFeet.Count){$script:spriteFeet[$pose]}else{1.0}
  return [Math]::Max(0.0,$w.Height-($origin.Y+$offset.Y+$sprite.ActualHeight*$feet))
 } catch { return 18.0 }
}
# A soft shadow on whatever she is standing on: the title bar when sitting on a window, otherwise the
# bottom edge of the screen. It fades the higher her feet are above it.
$script:shadowEnabled=$true
function Update-Shadow([bool]$grounded,[bool]$onWindow){
 if(-not $script:shadowEnabled -or -not $grounded -or $script:isHidden){if($shadow.Visibility -ne 'Collapsed'){$shadow.Visibility='Collapsed'}; return}
 $foot=Get-SpriteFoot ([int]$script:poseIndex)
 $surface=if($onWindow){Get-SpriteFoot 12}else{8.0} # on the floor: just above the window's bottom edge
 $height=[Math]::Max(0.0,$foot-$surface)
 $width=[Math]::Max(20.0,$sprite.ActualWidth*0.42*(1.0-[Math]::Min(0.5,$height/400.0)))
 $shadow.Width=$width; $shadow.Height=$width*0.16
 $shadow.Margin=[Windows.Thickness]::new(0,0,0,$surface-18.0-$shadow.Height/2.0)
 $shadow.Opacity=0.45*[Math]::Max(0.0,1.0-$height/160.0)
 if($shadow.Visibility -ne 'Visible'){$shadow.Visibility='Visible'}
}
# Which of the 8 eye frames looks at the cursor (-1 = straight ahead). The visor's spot in the frame
# is the reference; when she is mirrored, left and right swap. Hysteresis stops flicker at sector edges.
$script:eyeDir=-1; $script:eyesFollow=$true
function Get-EyeDirection([double]$cursorX,[double]$cursorY){
 try {
  $origin=$scene.TransformToAncestor($w).Transform([Windows.Point]::new(0,0))
  $offset=[Windows.Media.VisualTreeHelper]::GetOffset($sprite)
  $headX=$w.Left+$origin.X+$offset.X+$sprite.ActualWidth*0.5
  $headY=$w.Top+$origin.Y+$offset.Y+$sprite.ActualHeight*$script:visorFraction
 } catch { return -1 }
 $dx=($cursorX-$headX)*[Math]::Sign($script:direction); $dy=$cursorY-$headY
 if([Math]::Sqrt($dx*$dx+$dy*$dy) -lt 45){$script:eyeDir=-1; return -1}
 $angle=[Math]::Atan2(-$dy,$dx)*180.0/[Math]::PI
 if($angle -lt 0){$angle+=360.0}
 if($script:eyeDir -ge 0){
  $diff=[Math]::Abs((($angle-$script:eyeDir*45.0)+540.0)%360.0-180.0)
  if($diff -le 30){return $script:eyeDir}
 }
 $script:eyeDir=[int][Math]::Round($angle/45.0)%8
 return $script:eyeDir
}
function Get-WindowRectDip([long]$handle){
 $m=Get-DeviceScale; if($null -eq $m -or $handle -eq 0){return $null}
 $r=$null; try{$r=[CapriDesktop]::WindowBounds($handle)}catch{}
 return ConvertFrom-DeviceRect $r $m
}
# A window with room above its title bar for Capri to sit on, or $null.
function Find-PerchWindow {
 $candidates=@()
 try{$fg=[CapriDesktop]::ForegroundWindow(); if($null -ne $fg){$candidates+=[long]$fg[0]}}catch{}
 if($script:lastAppWindow -ne 0){$candidates+=$script:lastAppWindow}
 $m=Get-DeviceScale; if($null -eq $m){return $null}
 foreach($handle in $candidates){
  $r=Get-WindowRectDip $handle
  if($null -eq $r -or $r.Width -lt 300 -or $r.Height -lt 150){continue}
  $area=$null; try{$area=ConvertFrom-DeviceRect ([CapriDesktop]::WorkAreaAt([int](($r.Left+$r.Width/2)*$m.M11),[int](($r.Top+5)*$m.M22))) $m}catch{}
  if($null -eq $area){continue}
  if($r.Top-($w.Height-(Get-SpriteFoot)) -lt $area.Top){continue}
  return @($handle,$r)
 }
 return $null
}
