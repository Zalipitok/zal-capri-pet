# Chat: a small window to type to her. Reminders ("remind me in 20 minutes to stretch") work on their
# own. Real replies are optional: they come from the Claude API with your own key (Settings > Fun),
# which is stored encrypted for your Windows account in chat-key.dat. Messages you send, and a few
# facts (time, what's playing, the weather, your history with her), go to Anthropic to write her reply.
$script:chatHistory=[Collections.Generic.List[object]]::new()
$script:chatKeyPath=Join-Path $script:AppRoot 'chat-key.dat'
$script:chatSend=$null; $script:chatRead=$null; $script:chatStatus=0; $script:chatWindow=$null; $script:chatError=''
function Get-ChatKey {
 try {
  if(Test-Path -LiteralPath $script:chatKeyPath){
   $secure=(Get-Content -LiteralPath $script:chatKeyPath -Raw).Trim() | ConvertTo-SecureString
   $bstr=[Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
   try{return [Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr)}finally{[Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr)}
  }
 } catch { }
 if($env:ANTHROPIC_API_KEY){return $env:ANTHROPIC_API_KEY}
 return ''
}
function Set-ChatKey([string]$key){
 $key=$key.Trim()
 if(-not $key){if(Test-Path -LiteralPath $script:chatKeyPath){Remove-Item -LiteralPath $script:chatKeyPath}; return}
 ConvertTo-SecureString -String $key -AsPlainText -Force | ConvertFrom-SecureString | Set-Content -LiteralPath $script:chatKeyPath -Encoding ASCII
}
function Get-ChatSystemPrompt {
 $m=$script:memory; $now=Get-Date
 $facts=@("It is "+$now.ToString('dddd HH:mm')+" for the user.",
  "You have known the user for "+$m.days+" days ("+$m.sessions+" sessions). They have given you "+$m.pets+" head pats, thrown you "+$m.throws+" times and swatted you "+$m.swats+" times. You've caught their cursor "+$script:rivalWins+" times; it escaped "+$script:rivalEscapes+" times.",
  "Your current mood: "+$script:mood+".")
 if($m.bestCatch -gt 0){$facts+="Their best catch-me score against you is "+$m.bestCatch+"."}
 if($script:musicEnabled -and $script:musicPlaying -and $script:musicTitle){$facts+="Music playing right now: "+$script:musicTitle+$(if($script:musicArtist){' by '+$script:musicArtist}else{''})+"."}
 if($script:weatherEnabled -and $null -ne $script:weather.fetched){$facts+="Weather where they are: "+(Get-WeatherSummary)}
 if($script:friendPresent){$facts+="A second Capri (your friend, in different colours) is on the screen too."}
 if($script:reminders.Count -gt 0){$facts+="Reminders you're holding for them: "+(@($script:reminders | Sort-Object at | Select-Object -First 5 | ForEach-Object {(Format-ReminderTime $_.at)+' '+$_.text}) -join '; ')+"."}
 return @"
You are Capri, a small, smug, playful desktop companion who lives on the user's screen. You float around their desktop, sit on their windows, tease them, chase their mouse cursor as their self-declared rival, and you're secretly fond of them. You have a cyberpunk netrunner flavour (the Blackwall, ICE, "jacking in"), but you never actually hack or touch anything on their computer.
Reply in 1 to 3 short sentences: casual, dry, a little teasing, never cruel. Plain text only: no markdown, no lists, no emoji, no stage directions. You can't see their screen, files or browser, so don't pretend to. If they want a reminder, tell them to type "remind me in 20 minutes to ..." here, because you handle those yourself. If they seem genuinely down, drop the teasing and be kind.
What you know right now:
$($facts -join "`n")
"@
}
function Add-ChatLine([string]$who,[string]$text){
 if($null -eq $script:chatLog){return}
 $line=[Windows.Controls.TextBlock]::new(); $line.TextWrapping='Wrap'; $line.Margin=[Windows.Thickness]::new(0,0,0,8)
 $name=[Windows.Documents.Run]::new($who+'  '); $name.FontWeight='Bold'
 $name.Foreground=[Windows.Media.BrushConverter]::new().ConvertFromString($(if($who -eq 'Capri'){'#FCEE0A'}elseif($who -eq 'You'){'#00E5FF'}else{'#8899AA'}))
 [void]$line.Inlines.Add($name); [void]$line.Inlines.Add([Windows.Documents.Run]::new($text))
 [void]$script:chatLog.Children.Add($line); $script:chatScroll.ScrollToEnd()
}
# She says it in her bubble too, for longer when it's longer.
function Show-ChatReply([string]$text){
 if($script:isHidden){return}
 Stop-Voice
 if((Test-CanReact) -and $script:state -ne 'catchgame'){$script:routine.Clear(); Begin-State 'chatreply'}
 $bubbleText=if($text.Length -gt 240){$text.Substring(0,237)+'...'}else{$text}
 $words.Text=$bubbleText; $bubble.Visibility='Visible'
 $script:messageUntil=$clock.Elapsed.TotalSeconds+[Math]::Max(4.0,[Math]::Min(14.0,$text.Length/13.0))
 $script:nextRemark=$clock.Elapsed.TotalSeconds+45; $script:nextYap=[Math]::Max($script:nextYap,$clock.Elapsed.TotalSeconds+20)
}
# "remind me in 20 minutes to stretch", "remind me to call mum at 18:30". Returns $true when handled.
function Invoke-ChatReminder([string]$text){
 $m=[regex]::Match($text,'^\s*(?:please\s+)?remind me\s+(?:to\s+(?<what>.+?)\s+(?<when>(?:in|at|tomorrow)\b.+?)|(?<when>(?:in|at|tomorrow)\b.+?)\s+(?:to|that|about)\s+(?<what>.+?))\s*[.!]*\s*$','IgnoreCase')
 if(-not $m.Success){return $false}
 $at=ConvertFrom-WhenText $m.Groups['when'].Value
 if($null -eq $at){Add-ChatLine 'Capri' ('"'+$m.Groups['when'].Value+'"? Give me something like "in 20 minutes" or "at 17:45".'); return $true}
 [void](Add-Reminder $at $m.Groups['what'].Value)
 $reply=if(Show-Remark 'reminderset' -Force -Value @((Format-ReminderTime $at),$m.Groups['what'].Value)){$words.Text}else{'Fine. '+(Format-ReminderTime $at)+'.'}
 Add-ChatLine 'Capri' $reply
 return $true
}
function Send-ChatMessage([string]$text){
 $text=$text.Trim(); if(-not $text){return}
 Register-Interaction
 Add-ChatLine 'You' $text
 if($text -eq '/clear'){$script:chatHistory.Clear(); $script:chatLog.Children.Clear(); Add-ChatLine '' 'Conversation cleared.'; return}
 if(Invoke-ChatReminder $text){return}
 if(-not $script:chatEnabled){Add-ChatLine '' 'Her own replies are off. Tick "Let her reply" below (uses your Claude API key). Reminders work either way.'; return}
 $key=Get-ChatKey
 if(-not $key){Add-ChatLine '' 'No API key yet. Paste one below and press Save key.'; return}
 if($null -ne $script:chatSend -or $null -ne $script:chatRead){Add-ChatLine '' 'Hold on, she''s still answering.'; return}
 $script:chatHistory.Add(@{role='user'; content=$text})
 while($script:chatHistory.Count -gt 16){$script:chatHistory.RemoveAt(0)}
 while($script:chatHistory.Count -gt 0 -and $script:chatHistory[0].role -ne 'user'){$script:chatHistory.RemoveAt(0)}
 $body=@{model=[string]$script:tuning.chatModel; max_tokens=[int]$script:tuning.chatMaxTokens; system=(Get-ChatSystemPrompt); messages=@($script:chatHistory | ForEach-Object {@{role=$_.role; content=$_.content}})}
 try {
  $request=[System.Net.Http.HttpRequestMessage]::new([System.Net.Http.HttpMethod]::Post,'https://api.anthropic.com/v1/messages')
  [void]$request.Headers.TryAddWithoutValidation('x-api-key',$key)
  [void]$request.Headers.TryAddWithoutValidation('anthropic-version','2023-06-01')
  $json=ConvertTo-Json -InputObject $body -Depth 6 -Compress
  $request.Content=[System.Net.Http.StringContent]::new($json,[Text.Encoding]::UTF8,'application/json')
  $script:chatSend=(Get-WeatherHttp).SendAsync($request)
  $script:chatTyping.Text='Capri is typing...'
  if(Test-CanReact){Begin-State 'sideglance'}
 } catch { Add-ChatLine '' ('Could not send: '+$_.Exception.Message); $script:chatHistory.RemoveAt($script:chatHistory.Count-1) }
}
function Update-Chat([double]$t){
 if($null -ne $script:chatSend -and $script:chatSend.IsCompleted){
  $task=$script:chatSend; $script:chatSend=$null
  if($task.IsFaulted -or $task.IsCanceled){Complete-Chat $null ('No connection: '+$(if($task.Exception){$task.Exception.GetBaseException().Message}else{'timed out'})); return}
  $script:chatStatus=[int]$task.Result.StatusCode
  $script:chatRead=$task.Result.Content.ReadAsStringAsync()
 }
 if($null -ne $script:chatRead -and $script:chatRead.IsCompleted){
  $task=$script:chatRead; $script:chatRead=$null
  if($task.IsFaulted){Complete-Chat $null 'Could not read her reply.'; return}
  $data=$null; try{$data=$task.Result | ConvertFrom-Json}catch{}
  if($script:chatStatus -ge 200 -and $script:chatStatus -lt 300 -and $null -ne $data){
   $reply=(@($data.content | Where-Object {$_.type -eq 'text'} | ForEach-Object {[string]$_.text}) -join ' ').Trim()
   if(-not $reply){$reply='...'}
   Complete-Chat $reply ''
  }else{
   $detail=if($null -ne $data -and $data.error){[string]$data.error.message}else{'HTTP '+$script:chatStatus}
   $why=switch($script:chatStatus){401 {'That API key was refused. Check it, then Save key again.'} 403 {'That key isn''t allowed to do this: '+$detail} 404 {'Model not found ('+$script:tuning.chatModel+'). Change chatModel in tuning.json.'} 429 {'Too many messages for now (rate limit). Try again in a minute.'} 529 {'The Claude API is overloaded. Try again shortly.'} default {'The API said: '+$detail}}
   Complete-Chat $null $why
  }
 }
}
function Complete-Chat($reply,[string]$problem){
 if($null -ne $script:chatTyping){$script:chatTyping.Text=''}
 if($null -eq $reply){
  $script:chatError=$problem
  if($script:chatHistory.Count -gt 0 -and $script:chatHistory[$script:chatHistory.Count-1].role -eq 'user'){$script:chatHistory.RemoveAt($script:chatHistory.Count-1)}
  Add-ChatLine '' $problem; return
 }
 $script:chatError=''
 $script:chatHistory.Add(@{role='assistant'; content=$reply})
 Add-ChatLine 'Capri' $reply
 Show-ChatReply $reply
}

function Show-ChatWindow {
 if($null -ne $script:chatWindow){try{[void]$script:chatWindow.Activate()}catch{}; return}
 $brush={param($hex) [Windows.Media.BrushConverter]::new().ConvertFromString($hex)}
 $dialog=[Windows.Window]::new(); $dialog.Title='Capri // Chat'; $dialog.Width=420; $dialog.Height=560
 $dialog.WindowStartupLocation='CenterScreen'; $dialog.Background=(& $brush '#111722'); $dialog.Foreground=[Windows.Media.Brushes]::White
 $grid=[Windows.Controls.Grid]::new(); $grid.Margin=[Windows.Thickness]::new(12); $dialog.Content=$grid
 foreach($h in @('*','Auto','Auto','Auto','Auto')){$row=[Windows.Controls.RowDefinition]::new(); $row.Height=[Windows.GridLengthConverter]::new().ConvertFromString($h); $grid.RowDefinitions.Add($row)}
 $script:chatScroll=[Windows.Controls.ScrollViewer]::new(); $script:chatScroll.VerticalScrollBarVisibility='Auto'; $script:chatScroll.Background=(& $brush '#0B1018'); $script:chatScroll.Padding=[Windows.Thickness]::new(10)
 $script:chatLog=[Windows.Controls.StackPanel]::new(); $script:chatScroll.Content=$script:chatLog
 [Windows.Controls.Grid]::SetRow($script:chatScroll,0); [void]$grid.Children.Add($script:chatScroll)
 $script:chatTyping=[Windows.Controls.TextBlock]::new(); $script:chatTyping.Opacity=0.7; $script:chatTyping.FontStyle='Italic'; $script:chatTyping.Margin=[Windows.Thickness]::new(0,4,0,4)
 [Windows.Controls.Grid]::SetRow($script:chatTyping,1); [void]$grid.Children.Add($script:chatTyping)
 $inputRow=[Windows.Controls.DockPanel]::new(); $inputRow.Margin=[Windows.Thickness]::new(0,2,0,8)
 $send=[Windows.Controls.Button]::new(); $send.Content='Send'; $send.IsDefault=$true; $send.Padding=[Windows.Thickness]::new(14,4,14,4); $send.Margin=[Windows.Thickness]::new(6,0,0,0)
 [Windows.Controls.DockPanel]::SetDock($send,'Right'); [void]$inputRow.Children.Add($send)
 $script:chatInput=[Windows.Controls.TextBox]::new(); $script:chatInput.Padding=[Windows.Thickness]::new(4); $script:chatInput.MaxLength=1000
 [void]$inputRow.Children.Add($script:chatInput)
 [Windows.Controls.Grid]::SetRow($inputRow,2); [void]$grid.Children.Add($inputRow)
 $send.Add_Click({$text=$script:chatInput.Text; $script:chatInput.Text=''; Send-ChatMessage $text; [void]$script:chatInput.Focus()})
 # Opt-in and key.
 $panel=[Windows.Controls.StackPanel]::new()
 $script:chatToggle=[Windows.Controls.CheckBox]::new(); $script:chatToggle.Content='Let her reply (uses your Claude API key; messages go to Anthropic)'; $script:chatToggle.Foreground=[Windows.Media.Brushes]::White
 $script:chatToggle.IsChecked=$script:chatEnabled
 $script:chatToggle.Add_Click({$script:chatEnabled=[bool]$script:chatToggle.IsChecked; Save-VoiceSettings})
 [void]$panel.Children.Add($script:chatToggle)
 $keyRow=[Windows.Controls.DockPanel]::new(); $keyRow.Margin=[Windows.Thickness]::new(0,6,0,0)
 $save=[Windows.Controls.Button]::new(); $save.Content='Save key'; $save.Padding=[Windows.Thickness]::new(10,3,10,3); $save.Margin=[Windows.Thickness]::new(6,0,0,0)
 [Windows.Controls.DockPanel]::SetDock($save,'Right'); [void]$keyRow.Children.Add($save)
 $script:chatKeyBox=[Windows.Controls.PasswordBox]::new(); $script:chatKeyBox.Padding=[Windows.Thickness]::new(3); [void]$keyRow.Children.Add($script:chatKeyBox)
 $save.Add_Click({
  try{Set-ChatKey $script:chatKeyBox.Password; $script:chatKeyBox.Password=''; Add-ChatLine '' $(if(Get-ChatKey){'Key saved (encrypted for your Windows account).'}else{'Key removed.'})}catch{Add-ChatLine '' ('Could not save the key: '+$_.Exception.Message)}
 })
 [void]$panel.Children.Add($keyRow)
 $note=[Windows.Controls.TextBlock]::new(); $note.TextWrapping='Wrap'; $note.FontSize=11; $note.Opacity=0.65; $note.Margin=[Windows.Thickness]::new(0,4,0,0)
 $note.Text='Get a key at console.anthropic.com (usage is billed to that account). Save an empty box to remove it. Type /clear to start over.'
 [void]$panel.Children.Add($note)
 [Windows.Controls.Grid]::SetRow($panel,3); [void]$grid.Children.Add($panel)
 $dialog.Add_Closed({$script:chatWindow=$null; $script:chatLog=$null; $script:chatTyping=$null; Register-Interaction})
 $script:chatWindow=$dialog
 $dialog.Show()
 foreach($entry in $script:chatHistory){Add-ChatLine $(if($entry.role -eq 'user'){'You'}else{'Capri'}) $entry.content}
 if($script:chatHistory.Count -eq 0){Add-ChatLine 'Capri' $(if($script:chatEnabled -and (Get-ChatKey)){'Well? Say something.'}else{'I can hold reminders for you: "remind me in 20 minutes to stretch". Want me to actually talk back? Tick the box below.'})}
 [void]$script:chatInput.Focus()
}
