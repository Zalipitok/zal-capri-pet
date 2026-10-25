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
