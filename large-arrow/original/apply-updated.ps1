[CmdletBinding()]
param([ValidateSet('all','deepseek')][string]$Character='deepseek',[int]$Size=64)
$ErrorActionPreference='Stop'
$profile=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'source\package.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$keeper=Join-Path $env:LOCALAPPDATA ($profile.namespace+'\1.0.0\keeper\MintCursorKeeper.exe')
$wasRunning=$false
if(Test-Path -LiteralPath $keeper){
 $info=[Diagnostics.ProcessStartInfo]::new();$info.FileName=$keeper;$info.Arguments='--status';$info.UseShellExecute=$false;$info.CreateNoWindow=$true;$info.WindowStyle=[Diagnostics.ProcessWindowStyle]::Hidden
 $process=[Diagnostics.Process]::Start($info)
 try{if(-not $process.WaitForExit(12000)){throw 'Size keeper status timed out.'};$wasRunning=$process.ExitCode -eq 0}finally{$process.Dispose()}
}
if($wasRunning){& (Join-Path $PSScriptRoot 'keeper.ps1') -Action Stop}
$applyError=$null;$keeperError=$null
try{& (Join-Path $PSScriptRoot 'install.ps1') -Character $Character -Size $Size -Apply}catch{$applyError=$_}
if($wasRunning){try{& (Join-Path $PSScriptRoot 'keeper.ps1') -Action Install}catch{$keeperError=$_}}
if($applyError -and $keeperError){throw ('Apply failed: '+$applyError.Exception.Message+'; previous keeper restart also failed: '+$keeperError.Exception.Message)}
if($applyError){throw $applyError}
if($keeperError){throw ('Cursor applied, but previous keeper could not be restarted: '+$keeperError.Exception.Message)}
