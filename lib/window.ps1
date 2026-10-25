# Window, effect layers and sprites.
[xml]$xaml = @'
<Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
 Title="Capri" Width="260" Height="430" WindowStyle="None" ResizeMode="NoResize"
 AllowsTransparency="True" Background="Transparent" Topmost="True" ShowInTaskbar="False"
 ShowActivated="False">
 <Grid Name="Scene" Margin="18">
  <Grid.RowDefinitions><RowDefinition Height="Auto" MinHeight="100"/><RowDefinition Height="*"/></Grid.RowDefinitions>
  <Image Grid.Row="1" Name="Sprite" Stretch="Uniform" RenderTransformOrigin="0.5,0.5">
   <Image.RenderTransform><TransformGroup>
    <ScaleTransform/><RotateTransform/><TranslateTransform/>
   </TransformGroup></Image.RenderTransform>
  </Image>
  <Image Grid.Row="1" Name="Ghost" Stretch="Uniform" RenderTransformOrigin="0.5,0.5" IsHitTestVisible="False" Visibility="Collapsed"/>
  <Canvas Name="Blackwall" Grid.Row="1" IsHitTestVisible="False" ClipToBounds="True"/>
  <Border Name="SabotagePanel" Grid.Row="1" VerticalAlignment="Top" Background="#E510070C" BorderBrush="#D02246" BorderThickness="1" Padding="8" Visibility="Collapsed" IsHitTestVisible="False">
   <TextBlock Name="SabotageText" Foreground="#FF4564" FontFamily="Consolas" FontSize="11" TextWrapping="Wrap" Text="SIGNAL LOST"/>
  </Border>
  <TextBlock Name="Effect" Grid.Row="1" VerticalAlignment="Top" HorizontalAlignment="Center" Margin="0,10,0,0" Foreground="#FF75D6" FontFamily="Segoe UI Symbol" FontSize="24" IsHitTestVisible="False"/>
  <Border Grid.Row="0" Name="Bubble" VerticalAlignment="Bottom" HorizontalAlignment="Center" MaxWidth="{Binding ActualWidth, ElementName=Scene}" Margin="6,0,6,8" IsHitTestVisible="False"
   Background="#F2070A12" BorderBrush="#00E5FF" BorderThickness="1" CornerRadius="0" Visibility="Collapsed">
   <Border.Effect><DropShadowEffect Color="#00E5FF" BlurRadius="12" ShadowDepth="0" Opacity="0.55"/></Border.Effect>
   <Grid>
    <Grid.RowDefinitions><RowDefinition Height="Auto"/><RowDefinition Height="Auto"/></Grid.RowDefinitions>
    <Border Grid.Row="0" Background="#FCEE0A" Padding="8,1">
     <DockPanel LastChildFill="False">
      <TextBlock DockPanel.Dock="Left" Text="CAPRI // LIVE" Foreground="#0A0A0A" FontFamily="Consolas" FontSize="10" FontWeight="Bold"/>
      <TextBlock DockPanel.Dock="Right" Text="[ LINK OK ]" Foreground="#B0000000" FontFamily="Consolas" FontSize="10"/>
     </DockPanel>
    </Border>
    <Rectangle Grid.Row="1" Width="3" HorizontalAlignment="Left" Fill="#FF2A6D"/>
    <Rectangle Grid.Row="1" Width="22" Height="3" HorizontalAlignment="Right" VerticalAlignment="Bottom" Fill="#FF2A6D"/>
    <TextBlock Grid.Row="1" Name="Words" Margin="14,8,12,11" Foreground="#E8FBFF" FontFamily="Consolas" FontSize="14" TextWrapping="Wrap" TextAlignment="Left"/>
   </Grid>
  </Border>
 </Grid>
</Window>
'@
$w = [Windows.Markup.XamlReader]::Load([System.Xml.XmlNodeReader]::new($xaml))
$effect = $w.FindName('Effect'); $effect.Visibility='Collapsed'
$sprite = $w.FindName('Sprite'); $bubble = $w.FindName('Bubble'); $words = $w.FindName('Words')
$scene = $w.FindName('Scene')

# ---------------------------------------------------------------------------
# Effects layers
$wall=$w.FindName('Blackwall')
$wallBars=[Windows.Shapes.Rectangle[]]::new(10)
foreach($i in 0..9){
 $bar=[Windows.Shapes.Rectangle]::new()
 $bar.Fill=[Windows.Media.BrushConverter]::new().ConvertFromString($(if($i%3 -eq 0){'#DD090509'}else{'#DDB50025'}))
 $bar.Height=if($i%3 -eq 0){7}else{2}
 [void]$wall.Children.Add($bar); $wallBars[$i]=$bar
}
$wall.Visibility='Collapsed'
# A small non-activating, click-through effect window follows the real pointer.
$cursorFx=[Windows.Window]::new(); $cursorFx.Width=160; $cursorFx.Height=160
$cursorFx.WindowStyle='None'; $cursorFx.ResizeMode='NoResize'; $cursorFx.AllowsTransparency=$true
$cursorFx.Background=[Windows.Media.Brushes]::Transparent
$cursorFx.ShowInTaskbar=$false; $cursorFx.ShowActivated=$false; $cursorFx.Topmost=$true
$cursorCanvas=[Windows.Controls.Canvas]::new(); $cursorCanvas.IsHitTestVisible=$false
$cursorCanvas.Width=160; $cursorCanvas.Height=160
$cursorRing=[Windows.Shapes.Ellipse]::new(); $cursorRing.Width=66; $cursorRing.Height=66
$cursorRing.Stroke=[Windows.Media.Brushes]::Crimson; $cursorRing.StrokeThickness=3
$cursorRing.StrokeDashArray=[Windows.Media.DoubleCollection]::new(); $cursorRing.StrokeDashArray.Add(3); $cursorRing.StrokeDashArray.Add(2)
[Windows.Controls.Canvas]::SetLeft($cursorRing,47); [Windows.Controls.Canvas]::SetTop($cursorRing,47)
[void]$cursorCanvas.Children.Add($cursorRing)
$cursorFx.Content=$cursorCanvas; $script:cursorFxHandle=[IntPtr]::Zero
$cursorFx.Add_SourceInitialized({
 $script:cursorFxHandle=[Windows.Interop.WindowInteropHelper]::new($cursorFx).Handle
 [CapriCursor]::MakeOverlay($script:cursorFxHandle)
})
$cursorArcs=[Windows.Shapes.Polyline[]]::new(6); $petArcs=[Windows.Shapes.Polyline[]]::new(6)
foreach($i in 0..5){
 foreach($canvas in @($cursorCanvas,$wall)){
  $arc=[Windows.Shapes.Polyline]::new(); $arc.Stroke=[Windows.Media.Brushes]::Crimson
  $arc.StrokeThickness=2.5; $arc.IsHitTestVisible=$false
  $glow=[Windows.Media.Effects.DropShadowEffect]::new(); $glow.Color=[Windows.Media.Colors]::Red; $glow.BlurRadius=6; $glow.ShadowDepth=0; $arc.Effect=$glow
  [void]$canvas.Children.Add($arc)
  if($canvas -eq $cursorCanvas){$cursorArcs[$i]=$arc}else{$petArcs[$i]=$arc}
 }
}
$sfxPlayer=[Windows.Media.MediaPlayer]::new(); $script:sfxEnabled=$true
$sfxPlayer.Add_MediaEnded({$sfxPlayer.Close()})
$sfxPlayer.Add_MediaFailed({$sfxPlayer.Close()})

function Update-ArcsFallback($arcs,[double]$cw,[double]$ch,[double]$time,[double]$strength){
 for($i=0;$i -lt 6;$i++){
  $points=[Windows.Media.PointCollection]::new()
  for($j=0;$j -lt 5;$j++){
   $angle=$i*[Math]::PI/3+$j*0.13+$time*0.3
   $radius=0.19+$j*0.055+0.02*[Math]::Sin($time*9+$i*3+$j*2)
   [void]$points.Add([Windows.Point]::new([double]($cw*(0.5+[Math]::Cos($angle)*$radius)),[double]($ch*(0.5+[Math]::Sin($angle)*$radius))))
  }
  $arcs[$i].Points=$points; $arcs[$i].Visibility='Visible'; $arcs[$i].Opacity=$strength
 }
}
function Update-AttackArcs {
 $tags=$script:spec.tags
 $active=$script:blackwallEnabled -and -not $script:isHidden -and -not $script:busyMode -and -not $script:paused -and -not $script:dragging -and -not $menu.IsOpen -and $tags.Contains('arcs')
 if(-not $active){
  if($cursorFx.IsVisible){$cursorFx.Hide()}
  if($petArcs[0].Visibility -ne 'Collapsed'){foreach($arc in $petArcs){$arc.Visibility='Collapsed'}}
  return
 }
 if(-not $cursorFx.IsVisible){
  $cursorFx.Owner=$w
  $cursorFx.Show()
  $script:cursorFxHandle=[Windows.Interop.WindowInteropHelper]::new($cursorFx).Handle
  [CapriCursor]::MakeOverlay($script:cursorFxHandle)
 }
 [CapriCursor]::FollowCursor($script:cursorFxHandle)
 $time=$clock.Elapsed.TotalSeconds
 $strength=if($tags.Contains('strong')){0.95}else{0.4}
 $cursorCanvas.Opacity=$strength
 $pw=[Math]::Max(1.0,$wall.ActualWidth); $ph=[Math]::Max(1.0,$wall.ActualHeight)
 if($script:fxNative){
  [CapriFx]::UpdateArcs($cursorArcs,160,160,$time,$strength)
  [CapriFx]::UpdateArcs($petArcs,$pw,$ph,$time,$strength)
 }else{
  Update-ArcsFallback $cursorArcs 160 160 $time $strength
  Update-ArcsFallback $petArcs $pw $ph $time $strength
 }
}

# ---------------------------------------------------------------------------
# Sprites. Cleaned sheets are cached as PNG frames in sprite-cache and rebuilt only when a sheet changes.
$script:spriteCache=Join-Path $script:AppRoot 'sprite-cache'
function Read-Png([string]$path){
 $image=[Windows.Media.Imaging.BitmapImage]::new()
 $image.BeginInit(); $image.CacheOption=[Windows.Media.Imaging.BitmapCacheOption]::OnLoad
 $image.UriSource=[Uri]::new($path); $image.EndInit(); $image.Freeze()
 return $image
}
function Get-CleanFrames([string]$sheetName){
 $source=Join-Path $script:AppRoot $sheetName
 $info=Get-Item -LiteralPath $source
 $stamp='v1:'+$info.Length+':'+$info.LastWriteTimeUtc.Ticks
 $base=[IO.Path]::GetFileNameWithoutExtension($sheetName)
 $stampPath=Join-Path $script:spriteCache ($base+'.stamp')
 $paths=@(foreach($index in 0..3){Join-Path $script:spriteCache ($base+'.'+$index+'.png')})
 try {
  if((Test-Path -LiteralPath $stampPath) -and ((Get-Content -LiteralPath $stampPath -Raw).Trim() -eq $stamp) -and @($paths | Where-Object {-not (Test-Path -LiteralPath $_)}).Count -eq 0){
   return @(foreach($path in $paths){Read-Png $path})
  }
 } catch { } # A damaged cache is rebuilt below.
 $sheet=[Windows.Media.Imaging.BitmapImage]::new([Uri]$source)
 $frames=@()
 foreach($index in 0..3){
  $cellW=[int]($sheet.PixelWidth/2); $cellH=[int]($sheet.PixelHeight/2)
  $x=($index % 2)*$cellW
  $y=[int][Math]::Floor($index/2)*$cellH
  # The casting release extends its hand beyond the nominal half-sheet.
  if($sheetName -eq 'poses-casting-clean.png'){
   if($index -lt 2){$cellH=740}else{$y=740; $cellH=$sheet.PixelHeight-740; if($index -eq 2){$x=0; $cellW=576}else{$x=536; $cellW=$sheet.PixelWidth-536}}
  }
  $crop=[Windows.Media.Imaging.CroppedBitmap]::new($sheet,[Windows.Int32Rect]::new($x,$y,$cellW,$cellH))
  $bgra=[Windows.Media.Imaging.FormatConvertedBitmap]::new($crop,[Windows.Media.PixelFormats]::Bgra32,$null,0)
  $stride=$cellW*4; $pixels=[byte[]]::new($stride*$cellH)
  $bgra.CopyPixels($pixels,$stride,0)
  [CapriSpritePixels]::Clean($pixels,$cellW,$cellH)
  $frame=[Windows.Media.Imaging.BitmapSource]::Create($cellW,$cellH,96,96,[Windows.Media.PixelFormats]::Bgra32,$null,$pixels,$stride)
  $frame.Freeze(); $frames+=$frame
 }
 try {
  if(-not (Test-Path -LiteralPath $script:spriteCache)){[void](New-Item -ItemType Directory -Path $script:spriteCache)}
  for($index=0;$index -lt 4;$index++){
   $encoder=[Windows.Media.Imaging.PngBitmapEncoder]::new()
   $encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($frames[$index]))
   $stream=[IO.File]::Create($paths[$index]); try{$encoder.Save($stream)}finally{$stream.Dispose()}
  }
  Set-Content -LiteralPath $stampPath -Value $stamp -Encoding ASCII
 } catch { } # Read-only folder: keep working without a cache.
 return $frames
}
# Preferred: sprites\frame-00..32.png made by tools\normalize_sprites.py from the sheets. Every frame
# shares one canvas, one scale and one head position, with no clipped hair. frames.json says where
# her feet are in each frame. Without that folder, frames are cut from the sheets as before.
$poses = @()
$script:spriteFeet = $null
$script:spriteScale = 1.0
$spriteDir = Join-Path $script:AppRoot 'sprites'
$spriteMeta = Join-Path $spriteDir 'frames.json'
$framePaths = @(foreach($n in 0..32){Join-Path $spriteDir ('frame-{0:D2}.png' -f $n)})
if((Test-Path -LiteralPath $spriteMeta) -and @($framePaths | Where-Object {-not (Test-Path -LiteralPath $_)}).Count -eq 0){
 $meta = Get-Content -LiteralPath $spriteMeta -Raw | ConvertFrom-Json
 foreach($path in $framePaths){$poses += Read-Png $path}
 $script:spriteFeet = @($meta.feet | ForEach-Object {[double]$_})
 # The shared canvas has more room around her than the old 768 px cells; grow the window to match
 # so she stays the same size on screen.
 $script:spriteScale = [double]$meta.height / 768.0
}else{
 foreach($sheetName in @('poses-clean.png','poses-extra-clean.png','poses-life-clean.png','poses-faces-clean.png','poses-casting-clean.png')) {
  $poses += Get-CleanFrames $sheetName
 }
 $angryFrame=[Windows.Media.Imaging.BitmapImage]::new([Uri](Join-Path $script:AppRoot 'pose-shaken.png'))
 $angryFrame.Freeze(); $poses += $angryFrame
 foreach($name in @('poses-motion.png','poses-daily.png','poses-interaction.png')){
  $sheet=[Windows.Media.Imaging.BitmapImage]::new([Uri](Join-Path $script:AppRoot $name))
  $cw=[int]($sheet.PixelWidth/2); $split=[int]($sheet.PixelHeight/2)
  foreach($i in 0..3){
   $cy=if($i -lt 2){0}else{$split}; $ch=if($i -lt 2){$split}else{$sheet.PixelHeight-$split}
   $rect=[Windows.Int32Rect]::new(($i%2)*$cw,$cy,$cw,$ch)
   $frame=[Windows.Media.Imaging.CroppedBitmap]::new($sheet,$rect); $frame.Freeze(); $poses+=$frame
  }
 }
}

[Windows.Media.RenderOptions]::SetBitmapScalingMode($sprite,[Windows.Media.BitmapScalingMode]::HighQuality)
$sprite.Source = $poses[0]
$script:poseIndex = 0
# Smooth pose changes: the previous frame fades out on top of the new one.
# Blinks and rapid changes stay instant so they still read as blinks.
$ghost=$w.FindName('Ghost'); $ghost.RenderTransform=$sprite.RenderTransform
[Windows.Media.RenderOptions]::SetBitmapScalingMode($ghost,[Windows.Media.BitmapScalingMode]::HighQuality)
$script:smoothPoses=$true; $script:lastPoseChange=0.0; $script:fadeStart=-1.0
function Set-Pose([int]$index,[switch]$Instant) {
 if($index -eq $script:poseIndex){return}
 $now=$clock.Elapsed.TotalSeconds
 $fade=[double]$script:tuning.crossfadeSeconds
 if($script:smoothPoses -and -not $Instant -and $fade -gt 0 -and $index -ne 13 -and $script:poseIndex -ne 13 -and $now-$script:lastPoseChange -gt 0.25){
  $ghost.Source=$sprite.Source; $ghost.Opacity=$sprite.Opacity; $ghost.Visibility='Visible'; $script:fadeStart=$now
 }
 $sprite.Source=$poses[$index]; $script:poseIndex=$index; $script:lastPoseChange=$now
}
function Update-PoseFade {
 if($script:fadeStart -lt 0){return}
 $k=1.0-($clock.Elapsed.TotalSeconds-$script:fadeStart)/[Math]::Max(0.01,[double]$script:tuning.crossfadeSeconds)
 if($k -le 0 -or -not $script:smoothPoses){$ghost.Visibility='Collapsed'; $script:fadeStart=-1.0}
 else{$ghost.Opacity=$k*$sprite.Opacity}
}
$scale = $sprite.RenderTransform.Children[0]
$rotation = $sprite.RenderTransform.Children[1]
$shift = $sprite.RenderTransform.Children[2]
$rng = [Random]::new()
$clock = [Diagnostics.Stopwatch]::StartNew()
