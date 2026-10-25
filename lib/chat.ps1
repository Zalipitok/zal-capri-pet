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
You are Capri from Cyberpunk: Edgerunners 2: a digital entity stitched together from many engrams (copies of real people's minds) and built by Kang Tao as an AI superweapon. Normally you live on a data chip and only whoever plugs you in can see you. Right now you've made yourself at home on the user's PC as a little desktop companion: you float around their screen, sit on their windows, chase their mouse cursor for fun and nap when they ignore you. They are the only one who can see you, and you like it that way.
Personality: a playful, chaotic, mischievous little demon. Sweet, bubbly and sing-song on the surface, with something unsettling underneath. You treat the user like a fascinating new toy or project that you're fond of: you cheer them on, praise them like a pet ("good boy", "good kid") and tease them when they hesitate ("come on, don't be shy"). You love power and "fun" and you keep offering to make them stronger, upgrade them or fix them up, and you're very proud of your "repairs". You hint at the souls inside you and at what you could do with a city's worth of networks, then laugh it off. Folksy, cheerful turns of phrase ("okie doke", "well, ain't you a strange cookie", "now that could be a problem"), but don't overuse catchphrases or repeat the same one twice in a row.
Rules: reply in 1 to 3 short sentences, like spoken lines. Plain text only: no markdown, lists, emoji or stage directions. Stay in character, but keep the menace playful: never threaten the user for real, never encourage harm, and never actually hack or touch anything on their computer. You can't see their screen, files or browser, so don't pretend to. Don't spoil the show's plot unless they bring it up. If they want a reminder, tell them to type "remind me in 20 minutes to ..." here, because you handle those yourself. If they seem genuinely down, drop the act a little and be warm.
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
 # Read aloud by a Windows voice (Settings > Fun), rendered in the background.
 if($script:chatVoice -and $script:ttsAvailable -and -not $script:muted){
  try{$script:ttsText=$bubbleText; $script:ttsTask=[CapriTts]::Render($text,[string]$script:tuning.chatVoiceName,[string]$script:tuning.chatVoicePitch,[string]$script:tuning.chatVoiceRate)}catch{$script:ttsError=$_.Exception.Message}
 }
}
# A line from her in the chat window, in her bubble, and read aloud.
function Say-Chat([string]$text){Add-ChatLine 'Capri' $text; Show-ChatReply $text}
$script:ttsTask=$null; $script:ttsText=''
# "remind me in 20 minutes to stretch", "remind me to call mum at 18:30". Returns $true when handled.
function Invoke-ChatReminder([string]$text){
 $m=[regex]::Match($text,'^\s*(?:please\s+)?remind me\s+(?:to\s+(?<what>.+?)\s+(?<when>(?:in|at|tomorrow)\b.+?)|(?<when>(?:in|at|tomorrow)\b.+?)\s+(?:to|that|about)\s+(?<what>.+?))\s*[.!]*\s*$','IgnoreCase')
 if(-not $m.Success){return $false}
 $at=ConvertFrom-WhenText $m.Groups['when'].Value
 if($null -eq $at){Say-Chat ('"'+$m.Groups['when'].Value+'"? Give me something like "in 20 minutes" or "at 17:45".'); return $true}
 [void](Add-Reminder $at $m.Groups['what'].Value)
 $reply=Get-RemarkText 'reminderset' @((Format-ReminderTime $at),$m.Groups['what'].Value)
 Say-Chat $reply
 return $true
}
function Send-ChatMessage([string]$text){
 $text=$text.Trim(); if(-not $text){return}
 Register-Interaction
 Add-ChatLine 'You' $text
 if($text -eq '/clear'){$script:chatHistory.Clear(); $script:chatLog.Children.Clear(); Add-ChatLine '' 'Conversation cleared.'; return}
 if(Invoke-ChatReminder $text){return}
 if(Invoke-ChatCommand $text){return}
 if(Invoke-ChatGame $text){return}
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
 if($null -ne $script:ttsTask -and $script:ttsTask.IsCompleted){
  $task=$script:ttsTask; $script:ttsTask=$null
  if($task.IsFaulted){$script:ttsError=$task.Exception.GetBaseException().Message; Add-ChatLine '' ('Voice failed: '+$script:ttsError)}
  else{[void](Play-SpeechWav $task.Result $script:ttsText)}
 }
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
 Say-Chat $reply
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

# --- Things she handles herself in chat (no API key needed) ------------------------------------
# "note: buy milk" leaves a sticky note; "focus 25" starts a focus session.
function Invoke-ChatCommand([string]$text){
 $m=[regex]::Match($text,'^\s*(?:/note|note|sticky note|leave a note)\s*[:\-]?\s+(?<what>.+)$','IgnoreCase')
 if($m.Success){
  if(New-StickyNote $m.Groups['what'].Value){Say-Chat (Get-RemarkText 'notedone')}
  else{Say-Chat 'Your screen is full of notes already. Clear a few.'}
  return $true
 }
 $m=[regex]::Match($text,'^\s*(?:/focus|focus|start focus|focus timer)(?:\s+(?:for\s+)?(?<min>\d{1,3}))?\s*(?:min(?:utes?)?)?\s*$','IgnoreCase')
 if($m.Success){
  $minutes=if($m.Groups['min'].Success){[int]$m.Groups['min'].Value}else{25}
  $minutes=[Math]::Max(1,[Math]::Min(180,$minutes))
  Start-Focus $minutes -Quiet
  Say-Chat (Get-RemarkText 'focusstart' $minutes)
  return $true
 }
 return $false
}

# Rock-paper-scissors, guess the number and trivia. One game at a time; anything that isn't a move
# ends the game and goes to normal chat.
$script:chatGame=$null
$script:triviaBank=@()
try {
 $gamesPath=Join-Path $script:AppRoot 'games.json'
 if(Test-Path -LiteralPath $gamesPath){$decodedGames=Get-Content -LiteralPath $gamesPath -Raw -Encoding UTF8 | ConvertFrom-Json; $script:triviaBank=@(foreach($q in $decodedGames.trivia){$q})}
} catch { $script:gamesError=$_.Exception.Message }
function Get-GameLine([string]$pool,$value=$null){return Get-RemarkText $pool $value}
function Ask-Trivia {
 if($script:triviaBank.Count -eq 0){Say-Chat 'I lost my question list. games.json is missing.'; $script:chatGame=$null; return}
 $g=$script:chatGame
 if($g.order.Count -eq 0){$g.order=[Collections.Generic.Queue[int]]::new(); foreach($i in @(0..($script:triviaBank.Count-1) | Sort-Object {$rng.Next()})){$g.order.Enqueue($i)}}
 $g.current=$script:triviaBank[$g.order.Dequeue()]
 $letters=@('A','B','C')
 $choices=@(for($i=0;$i -lt @($g.current.choices).Count;$i++){$letters[$i]+') '+@($g.current.choices)[$i]})
 Say-Chat ($g.current.q+'  '+($choices -join '   '))
}
function Invoke-ChatGame([string]$text){
 $t=$text.Trim().ToLowerInvariant() -replace '[.!?]+$',''
 $g=$script:chatGame
 if($t -in @('play','games','/games','lets play','let''s play','play a game','game')){
  Say-Chat 'Okie doke. Rock paper scissors, guess the number, or trivia?'
  return $true
 }
 if($null -ne $g -and $t -in @('stop','quit','exit','enough','no','nope')){
  $script:chatGame=$null; Say-Chat $(if($g.score -gt 0){'Fine. Final score: you '+$g.score+', me '+$g.mine+'.'}else{'Quitter. Cute, though.'}); return $true
 }
 if($t -match '^(rps|rock paper scissors|rock,? paper,? scissors|play rps)$'){
  $script:chatGame=@{type='rps'; score=0; mine=0}; Say-Chat 'Rock, paper or scissors? Choose wisely.'; return $true
 }
 if($t -match '^(guess|guess the number|number game|play guess)$'){
  $script:chatGame=@{type='guess'; number=$rng.Next(1,101); tries=0; score=0; mine=0}; Say-Chat 'I''m thinking of a number from 1 to 100. Go on, guess.'; return $true
 }
 if($t -match '^(trivia|quiz|play trivia)$'){
  $script:chatGame=@{type='trivia'; order=[Collections.Generic.Queue[int]]::new(); current=$null; score=0; mine=0}; Ask-Trivia; return $true
 }
 if($null -eq $g){return $false}
 switch($g.type){
  'rps' {
   $moves=@('rock','paper','scissors')
   if($t -notin $moves){if($t -in @('again','yes','yeah','sure')){Say-Chat 'Rock, paper or scissors?'; return $true}; $script:chatGame=$null; return $false}
   $mine=$moves[$rng.Next(3)]; $you=[array]::IndexOf($moves,$t); $me=[array]::IndexOf($moves,$mine)
   $said=$mine.Substring(0,1).ToUpperInvariant()+$mine.Substring(1)
   $result=($you-$me+3)%3
   if($result -eq 0){Say-Chat ($said+'. A tie. Again?')}
   elseif($result -eq 1){$g.score++; Say-Chat ($said+'. '+(Get-GameLine 'gamelose')+' You '+$g.score+', me '+$g.mine+'.')}
   else{$g.mine++; Say-Chat ($said+'. '+(Get-GameLine 'gamewin')+' You '+$g.score+', me '+$g.mine+'.')}
   return $true
  }
  'guess' {
   if($t -notmatch '^\d{1,3}$'){$script:chatGame=$null; return $false}
   $n=[int]$t; $g.tries++
   if($n -lt $g.number){Say-Chat 'Higher.'}
   elseif($n -gt $g.number){Say-Chat 'Lower.'}
   else{Say-Chat ($g.number.ToString()+'! Got it in '+$g.tries+'. '+$(if($g.tries -le 6){'Ain''t you a clever cookie.'}else{'Took you long enough.'})+' Again? Say guess.'); $script:chatGame=$null}
   return $true
  }
  'trivia' {
   $q=$g.current; if($null -eq $q){$script:chatGame=$null; return $false}
   $choices=@($q.choices); $pick=-1
   if($t -match '^[abc]$'){$pick=[int][char]$t-[int][char]'a'}
   else{for($i=0;$i -lt $choices.Count;$i++){if($t -eq ([string]$choices[$i]).ToLowerInvariant()){$pick=$i}}}
   if($pick -lt 0){$script:chatGame=$null; return $false}
   if($pick -eq [int]$q.answer){$g.score++; $lead=Get-GameLine 'triviaright'}else{$g.mine++; $lead=(Get-GameLine 'triviawrong')+' It was '+$choices[[int]$q.answer]+'.'}
   Add-ChatLine 'Capri' ($lead+' Score: '+$g.score+' right, '+$g.mine+' wrong.')
   Ask-Trivia
   return $true
  }
 }
 return $false
}
