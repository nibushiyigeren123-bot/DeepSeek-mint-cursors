[CmdletBinding()]
param([ValidateSet('Stop','Status')][string]$Action='Stop')
$ErrorActionPreference='Stop'
$records=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'legacy-keepers.json') -Raw -Encoding UTF8|ConvertFrom-Json
$known=@()
foreach($record in $records){
 $root=Join-Path $env:LOCALAPPDATA $record.directory
 $keeper=Join-Path $root 'keeper'
 $exe=Join-Path $keeper 'MintCursorKeeper.exe'
 $known+=$exe
 if($Action-ne'Stop'){continue}
 if(Test-Path -LiteralPath $keeper){[IO.File]::WriteAllText((Join-Path $keeper 'disabled.flag'),'disabled',[Text.UTF8Encoding]::new($false))}
 if(Test-Path -LiteralPath $exe){
  $p=Start-Process -FilePath $exe -ArgumentList '--stop' -WindowStyle Hidden -PassThru
  try{if(-not$p.WaitForExit(12000)-or$p.ExitCode-ne0){throw ('Legacy keeper did not stop: '+$record.repository+' '+$record.edition)}}finally{$p.Dispose()}
 }
 $key=[Microsoft.Win32.Registry]::CurrentUser.OpenSubKey('Software\Microsoft\Windows\CurrentVersion\Run',$true)
 if($key){try{$value=[string]$key.GetValue($record.runName);if($value-in@(('"'+$exe+'"'),('"'+(Join-Path $keeper 'MintCursorKeeper-v1.0.0.exe')+'"'))){$key.DeleteValue($record.runName,$false)}}finally{$key.Dispose()}}
}
$remaining=@(Get-CimInstance Win32_Process -Filter "Name='MintCursorKeeper.exe'"|Where-Object{$_.ExecutablePath-in$known}|Select-Object ProcessId,ExecutablePath,CreationDate,CommandLine)
if($Action-eq'Stop'-and$remaining.Count-ne0){throw 'An identified family legacy keeper is still running.'}
if($Action-eq'Status'){$remaining|ConvertTo-Json -Depth 4}else{Write-Output 'Identified family legacy keepers stopped; cursor files and schemes preserved.'}
