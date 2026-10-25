# Sticky notes she leaves on your screen: Interact > Leave a sticky note, or type "note: ..." in the
# chat. Drag a note by its header, type straight into it, x removes it. They stay where you put them,
# under other windows like paper notes, and come back after a restart (notes.json).
$script:notes=[ordered]@{}
$script:notesPath=Join-Path $script:AppRoot 'notes.json'
$script:noteSaveDue=0.0
function Save-Notes {
 if($script:SelfTestMode -or $script:FriendMode){return}
 try {
  $list=@(foreach($n in $script:notes.Values){[ordered]@{text=$n.box.Text; x=[Math]::Round($n.win.Left); y=[Math]::Round($n.win.Top)}})
  if($list.Count -eq 0){if(Test-Path -LiteralPath $script:notesPath){Remove-Item -LiteralPath $script:notesPath}; return}
  ConvertTo-Json -InputObject @($list) | Set-Content -LiteralPath $script:notesPath -Encoding UTF8
 } catch { }
}
function New-StickyNote([string]$text='',$x=$null,$y=$null,[switch]$Restore){
 if($script:notes.Count -ge 20){return $false}
 $brush={param($hex) [Windows.Media.BrushConverter]::new().ConvertFromString($hex)}
 $id=[guid]::NewGuid().ToString('N')
 $win=[Windows.Window]::new(); $win.WindowStyle='None'; $win.ResizeMode='NoResize'; $win.AllowsTransparency=$true
 $win.Background=[Windows.Media.Brushes]::Transparent; $win.ShowInTaskbar=$false; $win.Topmost=$false
 $win.Width=210; $win.SizeToContent='Height'; $win.Tag=$id; $win.Title='Capri note'
 $frame=[Windows.Controls.Border]::new(); $frame.Background=(& $brush '#F2101722'); $frame.BorderBrush=(& $brush '#FCEE0A'); $frame.BorderThickness=[Windows.Thickness]::new(1)
 $stack=[Windows.Controls.StackPanel]::new(); $frame.Child=$stack
 $header=[Windows.Controls.DockPanel]::new(); $header.Background=(& $brush '#FCEE0A'); $header.Cursor='SizeAll'; $header.Tag=$id
 $close=[Windows.Controls.TextBlock]::new(); $close.Text=' x '; $close.FontFamily='Consolas'; $close.FontWeight='Bold'; $close.Cursor='Hand'; $close.Tag=$id
 [Windows.Controls.DockPanel]::SetDock($close,'Right'); [void]$header.Children.Add($close)
 $title=[Windows.Controls.TextBlock]::new(); $title.Text='CAPRI // NOTE'; $title.FontFamily='Consolas'; $title.FontSize=10; $title.FontWeight='Bold'; $title.Padding=[Windows.Thickness]::new(6,2,0,2)
 [void]$header.Children.Add($title)
 [void]$stack.Children.Add($header)
 $box=[Windows.Controls.TextBox]::new(); $box.Text=$text; $box.TextWrapping='Wrap'; $box.AcceptsReturn=$true; $box.MinHeight=50
 $box.Background=[Windows.Media.Brushes]::Transparent; $box.BorderThickness=[Windows.Thickness]::new(0); $box.Foreground=(& $brush '#E8FBFF')
 $box.CaretBrush=(& $brush '#FCEE0A'); $box.FontFamily='Consolas'; $box.FontSize=13; $box.Padding=[Windows.Thickness]::new(8,6,8,8); $box.MaxLength=500
 [void]$stack.Children.Add($box)
 $win.Content=$frame
 $header.Add_MouseLeftButtonDown({param($sender,$event) $n=$script:notes[[string]$sender.Tag]; if($null -ne $n){try{$n.win.DragMove()}catch{}; Save-Notes}})
 $close.Add_MouseLeftButtonDown({param($sender,$event) $event.Handled=$true; Remove-StickyNote ([string]$sender.Tag)})
 $box.Add_TextChanged({$script:noteSaveDue=$clock.Elapsed.TotalSeconds+2})
 # Where: next to her, unless restoring a saved spot.
 if($null -eq $x){
  $a=Get-WorkArea
  $x=if($w.Left-230 -ge $a.Left){$w.Left-230}else{$w.Left+$w.Width+10}
  $y=[Math]::Max($a.Top,[Math]::Min($a.Bottom-160,$w.Top+$w.Height*0.45))
 }
 $win.Left=[double]$x; $win.Top=[double]$y
 $script:notes[$id]=@{win=$win; box=$box}
 $win.Show()
 if(-not $Restore){
  if(-not $text){[void]$win.Activate(); [void]$box.Focus()}
  Save-Notes
 }
 return $true
}
function Remove-StickyNote([string]$id){
 $n=$script:notes[$id]; if($null -eq $n){return}
 $script:notes.Remove($id); $n.win.Close(); Save-Notes
}
function Update-Notes([double]$t){
 if($script:noteSaveDue -gt 0 -and $t -ge $script:noteSaveDue){$script:noteSaveDue=0; Save-Notes}
}
function Invoke-LeaveNote {
 Prepare-ManualAction; $script:routine.Clear()
 if(New-StickyNote ''){Begin-State 'wave'; [void](Show-Remark 'notedone' -Force)}else{Show-Status 'That''s 20 notes already. Clear a few first.'}
}
# Notes from last time.
if(-not $script:SelfTestMode -and -not $script:FriendMode){
 try {
  if(Test-Path -LiteralPath $script:notesPath){
   $decodedNotes=Get-Content -LiteralPath $script:notesPath -Raw -Encoding UTF8 | ConvertFrom-Json
   $script:pendingNotes=@(foreach($n in $decodedNotes){$n})
  }
 } catch { $script:notesError=$_.Exception.Message }
}
# Shown once Capri herself is on screen (windows can't be shown before the main one exists).
# A note saved on a monitor that is no longer there comes back onto the screen.
function Show-SavedNotes {
 $left=[Windows.SystemParameters]::VirtualScreenLeft; $top=[Windows.SystemParameters]::VirtualScreenTop
 $right=$left+[Windows.SystemParameters]::VirtualScreenWidth-210; $bottom=$top+[Windows.SystemParameters]::VirtualScreenHeight-80
 foreach($n in @($script:pendingNotes)){
  if($null -eq $n){continue}
  $x=[Math]::Max($left,[Math]::Min($right,[double]$n.x)); $y=[Math]::Max($top,[Math]::Min($bottom,[double]$n.y))
  [void](New-StickyNote ([string]$n.text) $x $y -Restore)
 }
 $script:pendingNotes=@()
}
$script:pendingNotes=@($script:pendingNotes | Where-Object {$_})
$w.Add_ContentRendered({if(-not $script:SelfTestMode -and -not $script:FriendMode){try{Show-SavedNotes}catch{}}})
