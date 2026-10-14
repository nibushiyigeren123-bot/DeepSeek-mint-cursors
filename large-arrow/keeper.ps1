[CmdletBinding()]
param([ValidateSet('Install','Stop','Uninstall','Status')][string]$Action='Install')
$ErrorActionPreference='Stop'
$destination=Join-Path $env:LOCALAPPDATA 'DeepSeekMintCursorsLargeArrow\1.0.0'
$keeperDir=Join-Path $destination 'keeper'
$exeName='MintCursorKeeper.exe'
$installedExe=Join-Path $keeperDir $exeName
$sourceCode=Join-Path $PSScriptRoot 'source\MintCursorKeeper.cs'
$buildFile=Join-Path $keeperDir 'build.json'
$disabled=Join-Path $keeperDir 'disabled.flag'
$runPath='Software\Microsoft\Windows\CurrentVersion\Run'
$runName='DeepSeekMintCursorsLargeArrowKeeper'
function Invoke-KeeperControl([string]$file,[string]$argument){
 $start=[Diagnostics.ProcessStartInfo]::new()
 $start.FileName=$file;$start.Arguments=$argument;$start.UseShellExecute=$false;$start.CreateNoWindow=$true;$start.WindowStyle=[Diagnostics.ProcessWindowStyle]::Hidden
 $start.RedirectStandardInput=$true;$start.RedirectStandardOutput=$true;$start.RedirectStandardError=$true
 $process=[Diagnostics.Process]::Start($start)
 try{if(-not $process.WaitForExit(12000)){throw 'Cursor size keeper did not answer within 12 seconds.'};return $process.ExitCode}finally{$process.Dispose()}
}
function Stop-MintKeeper {
 if(Test-Path -LiteralPath $keeperDir){[IO.File]::WriteAllText($disabled,'disabled',[Text.UTF8Encoding]::new($false))}
 $controlExe=if(Test-Path -LiteralPath $installedExe){$installedExe}else{$null}
 if($controlExe){if((Invoke-KeeperControl $controlExe '--stop') -ne 0){throw 'Could not stop the registered cursor size keeper.'}}
 $reg=[Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($runPath,$true)
 if($reg){try{$value=[string]$reg.GetValue($runName);if($value -in @(('"'+$installedExe+'"'),('"'+(Join-Path $keeperDir 'MintCursorKeeper-v1.0.0.exe')+'"'))){$reg.DeleteValue($runName,$false)}}finally{$reg.Close()}}
}
if($Action -eq 'Status'){
 if(-not(Test-Path -LiteralPath $installedExe)){Write-Output 'Cursor size keeper is not installed.';return}
 $state=Invoke-KeeperControl $installedExe '--status'
 Write-Output $(if($state -eq 0){'Cursor size keeper is running.'}else{'Cursor size keeper is stopped.'})
 $status=Join-Path $keeperDir 'status.txt';if(Test-Path -LiteralPath $status){Get-Content -LiteralPath $status -Encoding UTF8};return
}
if($Action -in @('Stop','Uninstall')){
 Stop-MintKeeper
 if($Action -eq 'Uninstall'){
  foreach($name in @($exeName,'MintCursorKeeper-v1.0.0.exe','MintCursorKeeper.cs','build.json','status.txt','keeper.log','keeper.log.previous','disabled.flag')){
   $file=Join-Path $keeperDir $name
   if(Test-Path -LiteralPath $file){Remove-Item -LiteralPath $file -Force}
  }
 }
 Write-Output 'Cursor size keeper stopped; login startup removed. Cursor files and schemes kept.';return
}
if(-not(Test-Path -LiteralPath $sourceCode -PathType Leaf)){throw 'Cursor size keeper source missing from this package.'}
$infCursors=Join-Path $env:WINDIR 'Cursors\DeepSeekMintCursorsLargeArrow\cursors'
if(-not(Test-Path -LiteralPath (Join-Path $destination 'cursors')) -and -not(Test-Path -LiteralPath $infCursors)){throw 'Install cursor files first with an INF or install.ps1.'}
[void](New-Item -ItemType Directory -Path $keeperDir -Force)
$oldRun=[Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($runPath)
$oldValue=if($oldRun){try{[string]$oldRun.GetValue($runName)}finally{$oldRun.Close()}}else{''}
if($oldValue -and $oldValue -ne ('"'+$installedExe+'"')){Stop-MintKeeper}

$sourceHash=(Get-FileHash -LiteralPath $sourceCode -Algorithm SHA256).Hash
$prior=if(Test-Path -LiteralPath $buildFile){Get-Content -LiteralPath $buildFile -Raw -Encoding UTF8 | ConvertFrom-Json}else{$null}
$reuse=(Test-Path -LiteralPath $installedExe) -and $prior -and $prior.source_sha256 -eq $sourceHash -and $prior.binary_sha256 -eq (Get-FileHash -LiteralPath $installedExe -Algorithm SHA256).Hash
if(-not $reuse){
 $compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
 if(-not(Test-Path -LiteralPath $compiler)){$compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'}
 if(-not(Test-Path -LiteralPath $compiler)){throw 'Windows .NET Framework C# compiler unavailable. Optional keeper cannot be built.'}
 $staged=Join-Path $keeperDir ('build-'+[Guid]::NewGuid().ToString('N')+'.exe')
 try{
  & $compiler /nologo /target:winexe /platform:anycpu /reference:System.Windows.Forms.dll ('/out:'+$staged) $sourceCode
  if($LASTEXITCODE -ne 0 -or -not(Test-Path -LiteralPath $staged)){throw 'Cursor size keeper compilation failed.'}
  Stop-MintKeeper
  Move-Item -LiteralPath $staged -Destination $installedExe -Force
  Copy-Item -LiteralPath $sourceCode -Destination (Join-Path $keeperDir 'MintCursorKeeper.cs') -Force
  $build=[ordered]@{version='1.1.0';source_sha256=$sourceHash;binary_sha256=(Get-FileHash -LiteralPath $installedExe -Algorithm SHA256).Hash;compiler=$compiler;generated_locally=$true}
  [IO.File]::WriteAllText($buildFile,($build | ConvertTo-Json),[Text.UTF8Encoding]::new($false))
 }finally{if(Test-Path -LiteralPath $staged){Remove-Item -LiteralPath $staged -Force}}
}
if(Test-Path -LiteralPath $disabled){Remove-Item -LiteralPath $disabled -Force}
$reg=[Microsoft.Win32.Registry]::CurrentUser.CreateSubKey($runPath)
try{$reg.SetValue($runName,('"'+$installedExe+'"'),[Microsoft.Win32.RegistryValueKind]::String)}finally{$reg.Close()}
if((Invoke-KeeperControl $installedExe '--status') -ne 0){
 # Shell launch keeps the resident process independent of installer capture pipes.
 # Only the locally compiled file in this user's installation directory is launched.
 Start-Process -FilePath $installedExe -WindowStyle Hidden
}
$running=$false
for($attempt=0;$attempt -lt 20;$attempt++){
 if((Invoke-KeeperControl $installedExe '--status') -eq 0){$running=$true;break}
 Start-Sleep -Milliseconds 100
}
if(-not $running){throw 'Cursor size keeper failed to start; inspect keeper.log.'}
Write-Output 'Optional cursor size keeper built locally and enabled at login. Default INF installation does not enable it.'
