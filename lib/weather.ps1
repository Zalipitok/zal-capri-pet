# Weather (off until you turn it on and give a city): once an hour she checks the forecast at
# open-meteo.com (free, no account) and reacts: a morning report, grumbling when rain starts,
# wilting in the heat. Only the city name and its coordinates are sent.
try{Add-Type -AssemblyName System.Net.Http}catch{}
$script:deg=[string][char]0xB0
$script:weather=@{lat=$null; lon=$null; place=''; forCity=''; code=-1; temp=$null; high=$null; low=$null; rain=$null; fetched=$null; kind=''; reportDay=''; pendingReport=$false; pendingChange=$false}
$script:weatherTask=$null; $script:weatherTaskKind=''; $script:nextWeather=25.0; $script:weatherError=''; $script:weatherHttp=$null
function Get-WeatherHttp {
 if($null -eq $script:weatherHttp){
  Add-Type -AssemblyName System.Net.Http
  [Net.ServicePointManager]::SecurityProtocol=[Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
  $script:weatherHttp=[System.Net.Http.HttpClient]::new(); $script:weatherHttp.Timeout=[TimeSpan]::FromSeconds(20)
  $script:weatherHttp.DefaultRequestHeaders.UserAgent.ParseAdd('CapriDesktopCompanion/2.0')
 }
 return $script:weatherHttp
}
function Get-WeatherDescription([int]$code){
 switch($code){
  0 {'clear skies'} 1 {'mostly clear'} 2 {'partly cloudy'} 3 {'overcast'}
  {$_ -in 45,48} {'fog'} {$_ -in 51,53,55} {'drizzle'} {$_ -in 56,57} {'freezing drizzle'}
  61 {'light rain'} 63 {'rain'} 65 {'heavy rain'} {$_ -in 66,67} {'freezing rain'}
  71 {'light snow'} 73 {'snow'} 75 {'heavy snow'} 77 {'snow grains'}
  {$_ -in 80,81} {'rain showers'} 82 {'violent rain showers'} {$_ -in 85,86} {'snow showers'}
  95 {'a thunderstorm'} {$_ -in 96,99} {'a thunderstorm with hail'}
  default {'weather'}
 }
}
# clear | cloudy | fog | rain | snow | storm, or hot / cold when the temperature says more.
function Get-WeatherKind([int]$code,[double]$temp){
 $celsius=if($script:weatherUnit -eq 'F'){($temp-32)*5/9}else{$temp}
 $kind=if($code -le 1){'clear'}elseif($code -le 3){'cloudy'}elseif($code -in 45,48){'fog'}elseif(($code -ge 51 -and $code -le 67) -or ($code -ge 80 -and $code -le 82)){'rain'}elseif(($code -ge 71 -and $code -le 77) -or $code -in 85,86){'snow'}elseif($code -ge 95){'storm'}else{'cloudy'}
 if($celsius -ge 30 -and $kind -in 'clear','cloudy'){return 'hot'}
 if($celsius -le 2 -and $kind -in 'clear','cloudy','fog'){return 'cold'}
 return $kind
}
$script:weatherReaction=@{clear='stretch'; cloudy='sideglance'; fog='scan'; rain='grumpy'; snow='celebrate'; storm='shiver'; hot='wilt'; cold='shiver'}
function Format-Number([double]$n){return ([Math]::Round($n)).ToString([Globalization.CultureInfo]::InvariantCulture)}
function Reset-Weather {
 $script:weather.lat=$null; $script:weather.lon=$null; $script:weather.forCity=''; $script:weather.code=-1; $script:weather.kind=''
 $script:weather.fetched=$null; $script:weatherError=''; $script:weatherTask=$null; $script:nextWeather=$clock.Elapsed.TotalSeconds+2
}
function Start-WeatherRequest([string]$url,[string]$kind){
 $script:weatherTask=(Get-WeatherHttp).GetStringAsync($url); $script:weatherTaskKind=$kind
}
function Update-Weather([double]$t){
 if($script:FriendMode -or -not $script:weatherEnabled -or -not $script:weatherCity.Trim()){return}
 # A report or change that waited for you to be around.
 if(($script:weather.pendingReport -or $script:weather.pendingChange) -and (Test-CanReact) -and $script:state -ne 'catchgame'){
  $sysIdle=0.0; try{$sysIdle=[CapriDesktop]::IdleSeconds()}catch{}
  if($sysIdle -lt 30 -and -not $script:voiceBusy){Show-WeatherReaction}
 }
 Update-WeatherFollowUp
 if($null -ne $script:weatherTask){
  if(-not $script:weatherTask.IsCompleted){return}
  $task=$script:weatherTask; $script:weatherTask=$null
  if($task.IsFaulted -or $task.IsCanceled){
   $script:weatherError=if($task.Exception){$task.Exception.GetBaseException().Message}else{'request cancelled'}
   $script:nextWeather=$t+900; return
  }
  try {
   $data=$task.Result | ConvertFrom-Json
   if($script:weatherTaskKind -eq 'geo'){
    $wanted=($script:weatherCity -split ',',2)
    $hint=if($wanted.Count -gt 1){$wanted[1].Trim()}else{''}
    $results=@(foreach($r in @($data.results)){if($null -ne $r){$r}})
    if($hint){$match=@($results | Where-Object {$_.country -like ($hint+'*') -or $_.country_code -eq $hint -or $_.admin1 -like ($hint+'*')}); if($match.Count -gt 0){$results=$match}}
    if($results.Count -eq 0){
     $script:weatherError='no place called "'+$script:weatherCity+'" was found'
     Show-Problem ('Weather: I couldn''t find "'+$script:weatherCity+'". Try just the city name in Settings > Fun.')
     $script:nextWeather=[double]::PositiveInfinity; return
    }
    $place=$results[0]
    $script:weather.lat=[double]$place.latitude; $script:weather.lon=[double]$place.longitude
    $script:weather.place=[string]$place.name; $script:weather.forCity=$script:weatherCity
    $script:nextWeather=$t # get the forecast right away
    return
   }
   $current=$data.current; $daily=$data.daily
   $code=[int]$current.weather_code; $temp=[double]$current.temperature_2m
   $script:weather.code=$code; $script:weather.temp=$temp
   $script:weather.high=if($daily){[double]@($daily.temperature_2m_max)[0]}else{$temp}
   $script:weather.low=if($daily){[double]@($daily.temperature_2m_min)[0]}else{$temp}
   $script:weather.rain=if($daily -and $null -ne @($daily.precipitation_probability_max)[0]){[int]@($daily.precipitation_probability_max)[0]}else{$null}
   $script:weather.fetched=Get-Date; $script:weatherError=''
   $kind=Get-WeatherKind $code $temp
   $today=(Get-Date).ToString('yyyy-MM-dd')
   if($script:weather.reportDay -ne $today -and (Get-Date).Hour -ge 6){$script:weather.reportDay=$today; $script:weather.pendingReport=$true}
   elseif($script:weather.kind -and $kind -ne $script:weather.kind){$script:weather.pendingChange=$true}
   $script:weather.kind=$kind
  } catch { $script:weatherError=$_.Exception.Message }
  $script:nextWeather=$t+[Math]::Max(15.0,[double]$script:tuning.weatherMinutes)*60
  return
 }
 if($t -lt $script:nextWeather){return}
 $script:nextWeather=$t+900 # replaced once the request finishes
 try {
  if($null -eq $script:weather.lat -or $script:weather.forCity -ne $script:weatherCity){
   $name=($script:weatherCity -split ',',2)[0].Trim()
   Start-WeatherRequest ('https://geocoding-api.open-meteo.com/v1/search?count=10&language=en&format=json&name='+[Uri]::EscapeDataString($name)) 'geo'
  }else{
   $ci=[Globalization.CultureInfo]::InvariantCulture
   $url='https://api.open-meteo.com/v1/forecast?latitude='+$script:weather.lat.ToString($ci)+'&longitude='+$script:weather.lon.ToString($ci)+
    '&current=temperature_2m,weather_code&daily=temperature_2m_max,temperature_2m_min,precipitation_probability_max&timezone=auto&forecast_days=1'
   if($script:weatherUnit -eq 'F'){$url+='&temperature_unit=fahrenheit'}
   Start-WeatherRequest $url 'forecast'
  }
 } catch { $script:weatherError=$_.Exception.Message }
}
function Show-WeatherReaction {
 $w0=$script:weather; $kind=$w0.kind
 if(-not $kind){$w0.pendingReport=$false; $w0.pendingChange=$false; return}
 $unit=$script:deg+$script:weatherUnit
 $script:routine.Clear(); Begin-State 'weather'
 if($script:weatherReaction.ContainsKey($kind)){$script:routine.Enqueue($script:weatherReaction[$kind])}
 if($w0.pendingReport){
  [void](Show-Remark 'weatherreport' -Force -Value @($w0.place,(Get-WeatherDescription $w0.code),((Format-Number $w0.temp)+$unit),((Format-Number $w0.high)+$unit)))
  $script:messageUntil=$clock.Elapsed.TotalSeconds+7
  # The comment on the kind of day comes a little later, from the reaction state.
  $script:weatherFollowUp=$kind
 }else{
  [void](Show-Remark ('weather'+$kind) -Value ((Format-Number $w0.temp)+$unit))
 }
 $w0.pendingReport=$false; $w0.pendingChange=$false
}
function Get-WeatherSummary {
 $w0=$script:weather
 if(-not $script:weatherEnabled){return 'Weather is off (Settings > Fun).'}
 if($null -eq $w0.fetched){return 'No forecast yet'+$(if($script:weatherError){' ('+$script:weatherError+')'}else{'.'})}
 $unit=$script:deg+$script:weatherUnit
 $text=$w0.place+': '+(Get-WeatherDescription $w0.code)+', '+(Format-Number $w0.temp)+$unit+' (today '+(Format-Number $w0.low)+' to '+(Format-Number $w0.high)+$unit
 if($null -ne $w0.rain){$text+=', '+$w0.rain+'% chance of rain'}
 return $text+'). Checked at '+$w0.fetched.ToString('HH:mm')+'.'
}
$script:weatherFollowUp=''
$motions['weather']={$rotation.Angle=-$script:direction*4; $shift.Y=[Math]::Sin($phase*1.5)*2}
$motions['wilt']={
 $sag=[Math]::Min(1.0,$phase/2.0)
 $rotation.Angle=$script:direction*(4+3*$sag)+[Math]::Sin($phase*0.9)*2; $scale.ScaleY=1-0.05*$sag; $shift.Y=6*$sag
}
# After the morning report: a comment on the kind of day, once the report has been read.
function Update-WeatherFollowUp {
 if($script:weatherFollowUp -and $script:state -ne 'weather' -and ($clock.Elapsed.TotalSeconds-$script:stateStart) -gt 1.5 -and $clock.Elapsed.TotalSeconds -gt $script:messageUntil){
  $kind=$script:weatherFollowUp; $script:weatherFollowUp=''
  [void](Show-Remark ('weather'+$kind) -Force -Value ((Format-Number $script:weather.temp)+$script:deg+$script:weatherUnit))
 }
}
