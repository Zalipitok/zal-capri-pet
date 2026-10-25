# Updates: once a day she asks GitHub whether a newer release of Capri is out (tuning.json updateRepo)
# and tells you. Debug and help > Install update downloads it, keeps your settings, memory, notes,
# reminders, voices and tuning, backs up every file it replaces, and restarts her.
$script:appVersion=[version]'0.0.0'
try { $versionFile=Join-Path $script:AppRoot 'VERSION'; if(Test-Path -LiteralPath $versionFile){$script:appVersion=[version]((Get-Content -LiteralPath $versionFile -Raw).Trim())} } catch { }
$script:update=$null; $script:updateTask=$null; $script:updateKind=''; $script:updateError=''; $script:nextUpdateCheck=90.0; $script:updateManual=$false
# Personal files an update never replaces.
$script:keepOnUpdate=@('voice-settings.json','memory.json','reminders.json','notes.json','chat-key.dat','tuning.json','capri.ico')
function Start-UpdateCheck([switch]$Manual){
 if($script:FriendMode -or $script:SelfTestMode -or $null -ne $script:updateTask){return}
 $script:updateManual=[bool]$Manual
 try {
  $request=[System.Net.Http.HttpRequestMessage]::new([System.Net.Http.HttpMethod]::Get,'https://api.github.com/repos/'+[string]$script:tuning.updateRepo+'/releases/latest')
  [void]$request.Headers.TryAddWithoutValidation('Accept','application/vnd.github+json')
  $script:updateTask=(Get-WeatherHttp).SendAsync($request); $script:updateKind='check'
  $script:memory.lastUpdateCheck=(Get-Date).ToString('yyyy-MM-dd'); $script:memoryDirty=$true
 } catch { $script:updateError=$_.Exception.Message }
}
function Update-Updates([double]$t){
 if($script:FriendMode -or $script:SelfTestMode){return}
 if($null -eq $script:updateTask){
  if($script:autoUpdate -and $t -ge $script:nextUpdateCheck){
   $script:nextUpdateCheck=$t+6*3600
   if($script:memory.lastUpdateCheck -ne (Get-Date).ToString('yyyy-MM-dd')){Start-UpdateCheck}
  }
  return
 }
 if(-not $script:updateTask.IsCompleted){return}
 $task=$script:updateTask; $script:updateTask=$null
 if($task.IsFaulted -or $task.IsCanceled){$script:updateError=if($task.Exception){$task.Exception.GetBaseException().Message}else{'timed out'}; if($script:updateManual){Show-Problem ('Update check failed: '+$script:updateError)}; return}
 if($script:updateKind -eq 'check'){
  $response=$task.Result; $status=[int]$response.StatusCode
  if($status -ne 200){
   $script:updateError=if($status -eq 404){'no public release found for '+$script:tuning.updateRepo}else{'GitHub answered '+$status}
   if($script:updateManual){Show-Problem ('Update check: '+$script:updateError)}; return
  }
  $script:updateTask=$response.Content.ReadAsStringAsync(); $script:updateKind='release'; return
 }
 if($script:updateKind -eq 'release'){
  try {
   $release=$task.Result | ConvertFrom-Json
   $tag=([string]$release.tag_name).TrimStart('v','V')
   $asset=@($release.assets | Where-Object {$_.name -like '*.zip'})[0]
   if($null -eq $asset){$script:updateError='the release has no zip'; return}
   $latest=[version]$tag
   $script:updateError=''
   if($latest -gt $script:appVersion){
    $script:update=@{version=$latest; url=[string]$asset.browser_download_url; notes=[string]$release.body}
    if($null -ne $script:tray){try{$script:tray.ShowBalloonTip(8000,'Capri '+$latest+' is out','Right-click her > Debug and help > Install update.',[Windows.Forms.ToolTipIcon]::Info)}catch{}}
    [void](Show-Remark 'updateavailable' -Force -Value ([string]$latest))
   }elseif($script:updateManual){Show-Status ('Up to date (version '+$script:appVersion+').') 4}
  } catch { $script:updateError=$_.Exception.Message; if($script:updateManual){Show-Problem ('Update check: '+$script:updateError)} }
  return
 }
 if($script:updateKind -eq 'download'){
  try { Install-UpdateFiles $task.Result } catch { Show-Problem ('Update failed: '+$_.Exception.Message+' Nothing was changed.') }
 }
}
function Start-UpdateInstall {
 if($null -eq $script:update){Start-UpdateCheck -Manual; Show-Status 'Checking for updates...'; return}
 $answer=[Windows.MessageBox]::Show("Install Capri $($script:update.version)? (You have $($script:appVersion).)`n`nYour settings, memory, notes, reminders, voices and tuning.json are kept. Replaced files are backed up to backup-before-$($script:update.version). She restarts when it's done.",'Capri update','YesNo','Question')
 if($answer -ne 'Yes'){return}
 Show-Status 'Downloading the update...' 10
 try{$script:updateTask=(Get-WeatherHttp).GetByteArrayAsync($script:update.url); $script:updateKind='download'}catch{Show-Problem ('Download failed: '+$_.Exception.Message)}
}
function Install-UpdateFiles([byte[]]$zip){
 Add-Type -AssemblyName System.IO.Compression.FileSystem
 $work=Join-Path ([IO.Path]::GetTempPath()) ('capri-update-'+[guid]::NewGuid().ToString('N'))
 [void](New-Item -ItemType Directory -Path $work)
 try {
  $zipPath=Join-Path $work 'update.zip'; [IO.File]::WriteAllBytes($zipPath,$zip)
  $unpacked=Join-Path $work 'files'; [IO.Compression.ZipFile]::ExtractToDirectory($zipPath,$unpacked)
  $marker=@(Get-ChildItem -LiteralPath $unpacked -Recurse -Filter 'Capri.ps1' | Sort-Object {$_.FullName.Length})[0]
  if($null -eq $marker){throw 'the download does not look like Capri.'}
  $source=$marker.Directory.FullName
  $backup=Join-Path $script:AppRoot ('backup-before-'+$script:update.version)
  $files=@(Get-ChildItem -LiteralPath $source -Recurse -File)
  foreach($file in $files){
   $relative=$file.FullName.Substring($source.Length).TrimStart('\','/')
   if($script:keepOnUpdate -contains $relative -or $relative -like 'voices*'){continue}
   $target=Join-Path $script:AppRoot $relative
   [void](New-Item -ItemType Directory -Force -Path (Split-Path -Parent $target))
   if(Test-Path -LiteralPath $target){
    $saved=Join-Path $backup $relative; [void](New-Item -ItemType Directory -Force -Path (Split-Path -Parent $saved))
    Copy-Item -LiteralPath $target -Destination $saved -Force
    # The running exe can't be overwritten, but it can be moved out of the way.
    if($script:IsExe -and $target -eq $script:LaunchPath){$old=$target+'.old'; if(Test-Path -LiteralPath $old){Remove-Item -LiteralPath $old -Force}; Rename-Item -LiteralPath $target -NewName ([IO.Path]::GetFileName($old))}
   }
   Copy-Item -LiteralPath $file.FullName -Destination $target -Force
  }
  # New tuning keys get their defaults from the code, so your tuning.json stays as it is.
 } finally { try{Remove-Item -LiteralPath $work -Recurse -Force}catch{} }
 # Start again once this Capri has gone.
 $shell=Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
 $quote={param($s) "'"+($s -replace "'","''")+"'"}
 $launch=if($script:IsExe){'Start-Process -FilePath '+(& $quote $script:LaunchPath)}else{'Start-Process -FilePath '+(& $quote $shell)+" -ArgumentList '-NoProfile','-STA','-ExecutionPolicy','Bypass','-WindowStyle','Hidden','-File',"+(& $quote ('"'+$script:LaunchPath+'"'))}
 $restart='Wait-Process -Id '+$PID+' -Timeout 60 -ErrorAction SilentlyContinue; '+$launch
 $encoded=[Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($restart))
 Start-Process -FilePath $shell -WindowStyle Hidden -ArgumentList @('-NoProfile','-EncodedCommand',$encoded)
 [void](Show-Remark 'updating' -Force)
 $script:updateRestarting=$true
 $w.Close()
}
