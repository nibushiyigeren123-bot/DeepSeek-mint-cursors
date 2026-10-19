[CmdletBinding()]
param([ValidateSet('Enable','Select','Stop','Status')][string]$Action='Status',[string]$Character='astra')
$ErrorActionPreference='Stop'
$target=Join-Path $env:LOCALAPPDATA 'AICursorFamilyAvoidance\r5'
$runtime=Join-Path $target 'runtime'
$exe=Join-Path $runtime 'AICursorFamilyR5.exe'
$stateFile=Join-Path $target 'installation-state.json'
$roles=@('Arrow','Help','AppStarting','Wait','Crosshair','IBeam','NWPen','No','SizeNS','SizeWE','SizeNWSE','SizeNESW','SizeAll','UpArrow','Hand','Pin','Person')
$ids=@(32512,32651,32650,32514,32515,32513,32631,32648,32645,32644,32642,32643,32646,32516,32649,32671,32672)
$runName='AICursorFamilyAvoidanceR5'
$utf8=[Text.UTF8Encoding]::new($false)
$owns=$false;$changed=$false;$previous=$null
function Json($path,$value){[IO.File]::WriteAllText($path,($value|ConvertTo-Json -Depth 14),$utf8)}
function Snapshot {
 $key=[Microsoft.Win32.Registry]::CurrentUser.OpenSubKey('Control Panel\Cursors')
 try{return @(foreach($name in @('')+$roles+@('Scheme Source')){$exists=$name-in$key.GetValueNames();[pscustomobject]@{name=$name;exists=$exists;value=if($exists){$key.GetValue($name,$null,[Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)}else{$null};kind=if($exists){$key.GetValueKind($name).ToString()}else{$null}}})}finally{$key.Dispose()}
}
function Restore($rows){$key=[Microsoft.Win32.Registry]::CurrentUser.CreateSubKey('Control Panel\Cursors');try{foreach($row in $rows){if($row.name-notin(@('')+$roles+@('Scheme Source'))){continue};if($row.exists){$key.SetValue($row.name,$row.value,[Enum]::Parse([Microsoft.Win32.RegistryValueKind],$row.kind))}else{$key.DeleteValue($row.name,$false)}}}finally{$key.Dispose()};if(-not[CursorTrialControl]::SystemParametersInfo(0x57,0,[IntPtr]::Zero,0)){throw 'Windows刷新失败。'}}
function Stop-Engine {
 if(Test-Path -LiteralPath $exe){$p=Start-Process -FilePath $exe -ArgumentList '--stop' -WindowStyle Hidden -PassThru;try{if(-not$p.WaitForExit(10000)-or$p.ExitCode-ne0){throw '本版停止未完成。'}}finally{$p.Dispose()}}
 $key=[Microsoft.Win32.Registry]::CurrentUser.OpenSubKey('Software\Microsoft\Windows\CurrentVersion\Run',$true);if($key){try{if([string]$key.GetValue($runName)-eq('"'+$exe+'"')){$key.DeleteValue($runName,$false)}}finally{$key.Dispose()}}
}
function Processes {return @(Get-CimInstance Win32_Process -Filter "Name='AICursorFamilyR5.exe'"|Where-Object{$_.ExecutablePath-eq$exe}|Select-Object ProcessId,ExecutablePath,CreationDate,CommandLine)}
function Live {
 try{$s=Get-Content -LiteralPath ($runtime+'\status.json') -Raw -Encoding UTF8|ConvertFrom-Json;$p=[Diagnostics.Process]::GetProcessById([int]$s.pid);try{if($p.HasExited-or$p.MainModule.FileName-ne$exe-or$p.StartTime.ToUniversalTime().Ticks-ne[long]$s.startedUtcTicks){return $null};return $s}finally{$p.Dispose()}}catch{return $null}
}
function Selected-Character($characters){$key=[Microsoft.Win32.Registry]::CurrentUser.OpenSubKey('Control Panel\Cursors');try{foreach($c in $characters){if($key.GetValue('')-ne$c.scheme){continue};$match=$true;foreach($role in $roles){if([Environment]::ExpandEnvironmentVariables([string]$key.GetValue($role,''))-ne(Join-Path $runtime ('assets\'+$c.id+'\'+$role+'\symbol.ani'))){$match=$false;break}};if($match){return $c.id}};return $null}finally{$key.Dispose()}}
function Ownership-Catalog($current){
 $rows=@($current)
 if(Test-Path -LiteralPath ($runtime+'\characters.json')){$rows+=@(Get-Content -LiteralPath ($runtime+'\characters.json') -Raw -Encoding UTF8|ConvertFrom-Json)}
 if(Test-Path -LiteralPath $stateFile){$saved=Get-Content -LiteralPath $stateFile -Raw -Encoding UTF8|ConvertFrom-Json;if($saved.ownedProfiles){$rows+=@($saved.ownedProfiles)}}
 $map=@{};foreach($row in $rows){$map[[string]$row.scheme]=$row};return @($map.Values)
}
function Status {
 $s=Live;$processes=@(Processes);$shared=@{};for($i=0;$i-lt17;$i++){$shared[$roles[$i]]=[CursorTrialControl]::Dimensions($ids[$i])}
 $key=[Microsoft.Win32.Registry]::CurrentUser.OpenSubKey('Control Panel\Cursors');try{$selection=$key.GetValue('');$size=$key.GetValue('CursorBaseSize',32)}finally{$key.Dispose()}
 $run=[Microsoft.Win32.Registry]::CurrentUser.OpenSubKey('Software\Microsoft\Windows\CurrentVersion\Run');try{$startup=$run-and([string]$run.GetValue($runName)-eq('"'+$exe+'"'))}finally{if($run){$run.Dispose()}}
 return [pscustomobject]@{utc=[DateTime]::UtcNow.ToString('o');active=[bool]($s-and$s.active);processCount=$processes.Count;processes=$processes;loginStartup=[bool]$startup;currentScheme=$selection;preference=$size;runtime=$s;sharedRoles=$shared;installDirectory=$target}
}
try{
 Add-Type -Path (Join-Path $PSScriptRoot 'ControlNative.cs')
 $mutex=[Threading.Mutex]::new($false,('Local\AICursorFamilyInstallerR5-'+[Security.Principal.WindowsIdentity]::GetCurrent().User.Value));try{$owns=$mutex.WaitOne(0)}catch [Threading.AbandonedMutexException]{$owns=$true};if(-not$owns){throw '另一个本版操作正在执行。'}
 if($Action-in@('Enable','Select')){
  $key=[Microsoft.Win32.Registry]::CurrentUser.OpenSubKey('Control Panel\Cursors');try{if([int]$key.GetValue('CursorBaseSize',32)-ne32){throw '本版要求Windows最小档32，不会自动改写全局指针偏好。'}}finally{$key.Dispose()}
  $characters=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'runtime\characters.json') -Raw -Encoding UTF8|ConvertFrom-Json
  $ownershipCharacters=@(Ownership-Catalog $characters)
  $chosen=@($characters|Where-Object{$_.id-eq$Character});if($chosen.Count-ne1){throw '角色编号无效。'}
  $legacyStopped=$false
  if($Action-eq'Enable'-and-not(Selected-Character $ownershipCharacters)){
   $r3=Join-Path $env:LOCALAPPDATA 'AICursorAvoidanceTrial\1.0.1\control.ps1'
   if(Test-Path -LiteralPath $r3){& powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File $r3 -Action Stop;if($LASTEXITCODE-ne0){throw '旧r3未正常关闭。'}}
   & (Join-Path $PSScriptRoot 'legacy-keeper-control.ps1') -Action Stop
   $legacyStopped=$true
  }
  # Numbered entries must reuse the running family process too. A current,
  # verified installation can select without stopping or copying the runtime.
  if($Action-eq'Enable'-and@(Processes).Count-le1-and(Test-Path -LiteralPath ($target+'\payload-hashes.json'))){
   $sourceManifest=Join-Path $PSScriptRoot 'payload-hashes.json'
   if((Get-FileHash -LiteralPath $sourceManifest -Algorithm SHA256).Hash-eq(Get-FileHash -LiteralPath ($target+'\payload-hashes.json') -Algorithm SHA256).Hash){
    $known=Get-Content -LiteralPath $sourceManifest -Raw -Encoding UTF8|ConvertFrom-Json;$intact=(Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash-eq$known.'runtime/AICursorFamilyR5.exe'
    foreach($f in $known.PSObject.Properties){if($f.Name.StartsWith(('runtime/assets/'+$Character+'/'),[StringComparison]::Ordinal)-or$f.Name.StartsWith(('runtime/assets/characters/'+$chosen[0].character+'/'+$chosen[0].size+'/'),[StringComparison]::Ordinal)){if(-not(Test-Path -LiteralPath (Join-Path $target $f.Name))-or(Get-FileHash -LiteralPath (Join-Path $target $f.Name) -Algorithm SHA256).Hash-ne$f.Value){$intact=$false;break}}}
    if($intact-and(-not(Selected-Character $ownershipCharacters)-or(Test-Path -LiteralPath $stateFile))){$Action='Select'}
   }
  }
  if($Action-eq'Enable'){
   $hashes=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'payload-hashes.json') -Raw -Encoding UTF8|ConvertFrom-Json
   foreach($f in $hashes.PSObject.Properties){if((Get-FileHash -LiteralPath (Join-Path $PSScriptRoot $f.Name) -Algorithm SHA256).Hash-ne$f.Value){throw ('包校验失败：'+$f.Name)}}
   $alreadySelected=Selected-Character $ownershipCharacters
   if($alreadySelected-and-not(Test-Path -LiteralPath $stateFile)){throw '本版恢复记录缺失，保留运行现场。'}
   if(-not$legacyStopped){
   $r3=Join-Path $env:LOCALAPPDATA 'AICursorAvoidanceTrial\1.0.1\control.ps1'
   if(Test-Path -LiteralPath $r3){& powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File $r3 -Action Stop;if($LASTEXITCODE-ne0){throw '旧r3未正常关闭。'}}
   & (Join-Path $PSScriptRoot 'legacy-keeper-control.ps1') -Action Stop
   }
   Stop-Engine
   $previous=Snapshot;$old=Selected-Character $ownershipCharacters;$changed=$true
   [void][IO.Directory]::CreateDirectory($target)
   if($old){if(-not(Test-Path -LiteralPath $stateFile)){throw '本版恢复记录缺失，保留现场。'}}else{Json $stateFile @{utc=[DateTime]::UtcNow.ToString('o');previous=$previous;ownedProfiles=$ownershipCharacters}}
   $restoreRecord=Get-Content -LiteralPath $stateFile -Raw -Encoding UTF8|ConvertFrom-Json;$restoreRecord|Add-Member -NotePropertyName ownedProfiles -NotePropertyValue $ownershipCharacters -Force;Json $stateFile $restoreRecord
   foreach($f in $hashes.PSObject.Properties){$source=Join-Path $PSScriptRoot $f.Name;$destination=Join-Path $target $f.Name;if([IO.Path]::GetFullPath($source)-eq[IO.Path]::GetFullPath($destination)){continue};[void][IO.Directory]::CreateDirectory((Split-Path -Parent $destination));if((Test-Path -LiteralPath $destination)-and(Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash-eq$f.Value){continue};Copy-Item -LiteralPath $source -Destination $destination -Force;if((Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash-ne$f.Value){throw ('复制校验失败：'+$f.Name)}}
   if([IO.Path]::GetFullPath($PSScriptRoot)-ne[IO.Path]::GetFullPath($target)){Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'payload-hashes.json') -Destination (Join-Path $target 'payload-hashes.json') -Force -ErrorAction Stop}
   $key=[Microsoft.Win32.Registry]::CurrentUser.CreateSubKey('Control Panel\Cursors\Schemes');try{foreach($c in $characters){$paths=@($roles|ForEach-Object{Join-Path $runtime ('assets\'+$c.id+'\'+$_+'\symbol.ani')});$key.SetValue($c.scheme,($paths-join','),[Microsoft.Win32.RegistryValueKind]::String)}}finally{$key.Dispose()}
  }else{
   if(-not(Test-Path -LiteralPath $exe)){throw '请先运行角色入口安装本版。'}
   if((Selected-Character $ownershipCharacters)-and-not(Test-Path -LiteralPath $stateFile)){throw '本版恢复记录缺失，保留现场。'}
   $previous=Snapshot
   if(-not(Selected-Character $ownershipCharacters)){Json $stateFile @{utc=[DateTime]::UtcNow.ToString('o');previous=$previous;ownedProfiles=$ownershipCharacters}}
  }
  $changed=$true;$key=[Microsoft.Win32.Registry]::CurrentUser.CreateSubKey('Control Panel\Cursors');try{foreach($role in $roles){$key.SetValue($role,(Join-Path $runtime ('assets\'+$Character+'\'+$role+'\symbol.ani')),[Microsoft.Win32.RegistryValueKind]::String)};$key.SetValue('',$chosen[0].scheme,[Microsoft.Win32.RegistryValueKind]::String);$key.SetValue('Scheme Source',1,[Microsoft.Win32.RegistryValueKind]::DWord)}finally{$key.Dispose()}
  if(-not[CursorTrialControl]::SystemParametersInfo(0x57,0,[IntPtr]::Zero,0)){throw 'Windows刷新功能光标失败。'}
  $reloadToken=0;$beforeReload=Live
  if($beforeReload-and$beforeReload.active){$reloadToken=([Guid]::NewGuid().GetHashCode()-band[int]::MaxValue);if($reloadToken-eq0){$reloadToken=1};if(-not[CursorTrialControl]::RequestNativeReload([long]$beforeReload.controlHwnd,[int]$beforeReload.pid,$reloadToken)){throw '本版重载通知未送达。'}}
  if(@(Processes).Count-eq0){Start-Process -FilePath $exe -WindowStyle Hidden}
  $until=[DateTime]::UtcNow.AddSeconds(12)
  do{$s=Live;if($s-and$s.active-and$s.character-eq$Character-and$s.avatarHwnd-ne0-and($reloadToken-eq0-or$s.nativeReloadToken-eq$reloadToken)){break};Start-Sleep -Milliseconds 100}while([DateTime]::UtcNow-lt$until)
  if(-not$s-or-not$s.active-or$s.character-ne$Character-or@(Processes).Count-ne1-or($reloadToken-ne0-and$s.nativeReloadToken-ne$reloadToken)){throw '人物运行程序未正常启用，请查看runtime内error.txt/fatal.txt。'}
  for($i=0;$i-lt17;$i++){$d=[CursorTrialControl]::Dimensions($ids[$i]);if($d[0]-ne32-or$d[1]-ne32){throw ('共享光标尺寸异常：'+$roles[$i])}}
  $key=[Microsoft.Win32.Registry]::CurrentUser.CreateSubKey('Software\Microsoft\Windows\CurrentVersion\Run');try{$key.SetValue($runName,('"'+$exe+'"'),[Microsoft.Win32.RegistryValueKind]::String)}finally{$key.Dispose()}
  $result=Status;Json (Join-Path $target 'last-operation.json') $result
  Write-Host ('已启用 '+$chosen[0].display+'：人物与功能符号按所选档位显示。后台仅1个程序，无托盘或任务栏图标。') -ForegroundColor Green
 }elseif($Action-eq'Stop'){
  $characters=if(Test-Path -LiteralPath ($runtime+'\characters.json')){Get-Content -LiteralPath ($runtime+'\characters.json') -Raw -Encoding UTF8|ConvertFrom-Json}else{@()}
  $characters=@(Ownership-Catalog $characters)
  $selected=Selected-Character $characters;Stop-Engine
  if($selected){if(-not(Test-Path -LiteralPath $stateFile)){throw '程序已停止，但恢复记录缺失。'};Restore (Get-Content -LiteralPath $stateFile -Raw -Encoding UTF8|ConvertFrom-Json).previous}
  $result=Status;if($result.processCount-ne0-or$result.loginStartup){throw '后台清理尚未完成。'}
  if(Test-Path -LiteralPath $target){Json (Join-Path $target 'last-operation.json') $result}
  Write-Host '已关闭，本版后台进程0，登录启动已取消。' -ForegroundColor Green
 }else{$result=Status;$result|ConvertTo-Json -Depth 12}
 exit 0
}catch{
 $failure=$_
 if($changed-and$previous){try{Stop-Engine;Restore $previous}catch{Write-Host ('回退错误：'+$_.Exception.Message)}}
 Write-Host ('操作失败：'+$failure.Exception.Message) -ForegroundColor Red
 if(Test-Path -LiteralPath $target){Json (Join-Path $target 'last-failure.json') @{error=$failure.Exception.ToString();position=$failure.InvocationInfo.PositionMessage}}
 exit 1
}finally{if($owns){$mutex.ReleaseMutex()};if($mutex){$mutex.Dispose()}}
