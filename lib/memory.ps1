# What Capri remembers between sessions (memory.json): when you met, days together, and running scores.
$script:memoryPath=Join-Path $script:AppRoot 'memory.json'
$script:memory=[ordered]@{
 firstMet=''; lastSeen=''; lastDay=''; days=0; sessions=0
 pets=0; throws=0; shakes=0; swats=0; returns=0; scans=0
 todayDate=''; todayPets=0; todayThrows=0; todaySwats=0
}
$script:memoryDirty=$false; $script:nextMemorySave=0.0; $script:greeting=$null
try {
 if(Test-Path -LiteralPath $script:memoryPath){
  $saved=Get-Content -LiteralPath $script:memoryPath -Raw -Encoding UTF8 | ConvertFrom-Json
  foreach($key in @($script:memory.Keys)){
   $value=$saved.$key; if($null -eq $value){continue}
   if($script:memory[$key] -is [string]){$script:memory[$key]=[string]$value}else{$script:memory[$key]=[Math]::Max(0,[int]$value)}
  }
 }
} catch { } # A damaged memory file starts fresh.

function Save-Memory {
 if($script:SelfTestMode -or -not $script:memoryDirty){return}
 try {
  $script:memory.lastSeen=(Get-Date).ToString('o')
  $script:memory | ConvertTo-Json | Set-Content -LiteralPath $script:memoryPath -Encoding UTF8
  $script:memoryDirty=$false
 } catch { }
}
function Update-MemoryDay {
 $today=(Get-Date).ToString('yyyy-MM-dd')
 if($script:memory.todayDate -ne $today){
  $script:memory.todayDate=$today; $script:memory.todayPets=0; $script:memory.todayThrows=0; $script:memory.todaySwats=0
  $script:memoryDirty=$true
 }
 if($script:memory.lastDay -ne $today){
  $script:memory.lastDay=$today; $script:memory.days++; $script:memoryDirty=$true
  return $true
 }
 return $false
}
# Counts an event and returns today's count for it (or the all-time count when there is no daily one).
function Add-Memory([string]$what){
 [void](Update-MemoryDay)
 $script:memory[$what]++
 $script:memoryDirty=$true
 $daily='today'+$what.Substring(0,1).ToUpper()+$what.Substring(1)
 if($script:memory.Contains($daily)){$script:memory[$daily]++; return [int]$script:memory[$daily]}
 return [int]$script:memory[$what]
}
# Picks the greeting for this session; it is shown a moment after she arrives.
function Start-MemorySession {
 $now=Get-Date
 $gapDays=0
 if($script:memory.lastSeen){try{$gapDays=[int][Math]::Floor(($now-[DateTime]::Parse($script:memory.lastSeen)).TotalDays)}catch{}}
 $newDay=Update-MemoryDay
 $script:memory.sessions++; $script:memoryDirty=$true
 if(-not $script:memory.firstMet){
  $script:memory.firstMet=$now.ToString('yyyy-MM-dd'); $script:greeting=@('firstmeet',$null)
 }elseif($newDay -and $script:memory.days -in @(7,30,50,100,200,365,500,1000)){
  $script:greeting=@('anniversary',$script:memory.days)
 }elseif($gapDays -ge 2){
  $script:greeting=@('longtime',$gapDays)
 }
 Save-Memory
}
function Get-MemorySummary {
 $m=$script:memory
 return "First met: $($m.firstMet)`nDays together: $($m.days)`nSessions: $($m.sessions)`n`nHead pats: $($m.pets) ($($m.todayPets) today)`nThrows: $($m.throws) ($($m.todayThrows) today)`nShakes: $($m.shakes)`nSwats: $($m.swats)`nCursor catches: $($script:rivalWins) / escapes: $($script:rivalEscapes)`nWelcome-backs: $($m.returns)`nFiles scanned: $($m.scans)"
}
# A milestone remark replaces the usual one when a daily count hits one of these.
function Test-Milestone([int]$count,[int[]]$marks){return $marks -contains $count}
