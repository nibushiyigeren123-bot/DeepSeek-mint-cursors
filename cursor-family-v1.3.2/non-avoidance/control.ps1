[CmdletBinding()]
param([ValidateSet('Enable','Select','Stop','Status')][string]$Action='Status',[string]$Character='astra')
$ErrorActionPreference='Stop'
$target=Join-Path $env:LOCALAPPDATA 'AICursorFamilyNonAvoidance\original-r1'
$runtime=Join-Path $target 'runtime'
$exe=Join-Path $runtime 'AICursorFamilyOriginalR1.exe'
$stateFile=Join-Path $target 'installation-state.json'
$roles=@('Arrow','Help','AppStarting','Wait','Crosshair','IBeam','NWPen','No','SizeNS','SizeWE','SizeNWSE','SizeNESW','SizeAll','UpArrow','Hand','Pin','Person')
$ids=@(32512,32651,32650,32514,32515,32513,32631,32648,32645,32644,32642,32643,32646,32516,32649,32671,32672)
$runName='AICursorFamilyNonAvoidanceR1'
$utf8=[Text.UTF8Encoding]::new($false)
$engineExecutable=Join-Path $PSHOME $(if($PSVersionTable.PSEdition-eq'Core'){'pwsh.exe'}else{'powershell.exe'})
$owns=$false;$changed=$false;$previous=$null;$selectionWritten=$false;$attemptScheme=$null;$expectedNativeSize=0;$previousNative=$null;$overwriteSources=@()
# [代码块] prototype.control.json | 范围：第 16-16 行（PowerShell AST核验）。
# [功能] 新状态先写同目录临时文件再原子替换；恢复基线不能留下半份JSON。
function Json($path,$value){$temp=$path+'.tmp';[IO.File]::WriteAllText($temp,($value|ConvertTo-Json -Depth 18),$utf8);if(Test-Path -LiteralPath $path){[IO.File]::Replace($temp,$path,[NullString]::Value)}else{[IO.File]::Move($temp,$path)}}
# [代码块] prototype.control.snapshot | 范围：第 19-22 行（PowerShell AST核验）。
# [修改] plan v2：保存CursorBaseSize的存在性、RegistryValueKind和值，连同原17路径。
function Snapshot {
 $key=[Microsoft.Win32.Registry]::CurrentUser.OpenSubKey('Control Panel\Cursors')
 try{return @(foreach($name in @('')+$roles+@('Scheme Source','CursorBaseSize')){$exists=$name-in$key.GetValueNames();[pscustomobject]@{name=$name;exists=$exists;value=if($exists){$key.GetValue($name,$null,[Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)}else{$null};kind=if($exists){$key.GetValueKind($name).ToString()}else{$null}}})}finally{$key.Dispose()}
}
# [代码块] prototype.control.baseline-paths | 范围：第 26-36 行（PowerShell AST核验）。
# [连接] Restore写系统字段之前核对保存的17个原路径；空/缺省值映射到Windows内置默认角色。
# [修改] v3：SPI优先分支只能使用与原登记方案对应的native基线，不接受来源错配。
function Validate-NativeBaselinePaths($rows,$native){
 if(-not$native){return}
 if(@($native).Count-ne17){throw '已观测的共享光标基线不完整。'}
 for($i=0;$i-lt17;$i++){
  if($native[$i].role-ne$roles[$i]){throw '共享光标基线顺序异常。'}
  $registered=@($rows|Where-Object{$_.name-eq$roles[$i]})
  if($registered.Count-ne1){throw '原方案角色路径记录不完整或重复。'}
  $path=if($registered[0].exists){[Environment]::ExpandEnvironmentVariables([string]$registered[0].value)}else{''}
  if(-not[String]::Equals([string]$native[$i].file,$path,[StringComparison]::OrdinalIgnoreCase)){throw '原共享光标源与登记方案路径不一致。'}
 }
}
# [代码块] prototype.control.restore | 范围：第 39-53 行（PowerShell AST核验）。
# [连接] Stop/失败回退 → 原偏好与原方案路径 → SPI刷新；不合成猜测的旧基线。
function Restore($rows,$native){
 Validate-NativeBaselinePaths $rows $native
 $key=[Microsoft.Win32.Registry]::CurrentUser.CreateSubKey('Control Panel\Cursors')
 try{foreach($row in $rows){
  if($row.name-notin(@('')+$roles+@('Scheme Source','CursorBaseSize'))){continue}
  if($row.exists){$key.SetValue($row.name,$row.value,[Enum]::Parse([Microsoft.Win32.RegistryValueKind],$row.kind))}else{$key.DeleteValue($row.name,$false)}
 }}finally{$key.Dispose()}
 if(-not(Matches-Snapshot $rows)){throw '恢复期间外部方案变化，已停止刷新。'}
 if(-not[CursorTrialControl]::SystemParametersInfo(0x57,0,[IntPtr]::Zero,0)){throw 'Windows刷新失败。'}
 if($native){
  $stillOwned=[Func[bool]]{return (Matches-Snapshot $rows)}
  $ok=[CursorTrialControl]::RestoreNative([string[]]@($native|ForEach-Object{$_.file}),[int[]]@($native|ForEach-Object{$_.size}),[int[]]@($native|ForEach-Object{$_.hotX}),[int[]]@($native|ForEach-Object{$_.hotY}),$stillOwned)
  if(-not$ok){throw '恢复期间外部方案变化，已停止写入并刷新当前方案。'}
 }else{Write-Host '旧安装记录未观测原共享位图；已恢复已保存字段与系统方案，无法证明旧共享像素逐项一致。'}
}
# [代码块] prototype.control.native-snapshot | 范围：第 56-62 行（PowerShell AST核验）。
# [连接] Enable/Select改变前采集；原ANI路径和实际unaware几何一起交给Restore。
function Snapshot-Native($rows){
 return @(for($i=0;$i-lt17;$i++){
  $g=[CursorTrialControl]::Geometry($ids[$i]);if($g[0]-ne$g[1]){throw '当前共享光标不是方形，无法无损记录当前恢复合同。'}
  $value=($rows|Where-Object{$_.name-eq$roles[$i]}).value
  [pscustomobject]@{role=$roles[$i];file=[Environment]::ExpandEnvironmentVariables([string]$value);size=$g[0];hotX=$g[2];hotY=$g[3]}
 })
}
# [代码块] prototype.control.matches-snapshot | 范围：第 65-71 行（PowerShell AST核验）。
# [功能] 原生恢复逐项提交前验证原方案字段仍属于本次恢复，不抢回外部新选择。
function Matches-Snapshot($rows){
 $key=[Microsoft.Win32.Registry]::CurrentUser.OpenSubKey('Control Panel\Cursors')
 try{foreach($row in $rows){
  $exists=$row.name-in$key.GetValueNames();if([bool]$row.exists-ne$exists){return $false}
  if($exists-and($key.GetValue([string]$row.name,$null,[Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)-ne$row.value-or$key.GetValueKind([string]$row.name).ToString()-ne$row.kind)){return $false}
 };return $true}finally{$key.Dispose()}
}
function Stop-Engine {
 if(Test-Path -LiteralPath $exe){$p=Start-Process -FilePath $exe -ArgumentList '--stop' -WindowStyle Hidden -PassThru;try{if(-not$p.WaitForExit(10000)-or$p.ExitCode-ne0){throw '本版停止未完成。'}}finally{$p.Dispose()}}
 $key=[Microsoft.Win32.Registry]::CurrentUser.OpenSubKey('Software\Microsoft\Windows\CurrentVersion\Run',$true);if($key){try{if([string]$key.GetValue($runName)-eq('"'+$exe+'"')){$key.DeleteValue($runName,$false)}}finally{$key.Dispose()}}
}
function Processes {return @(Get-CimInstance Win32_Process -Filter "Name='AICursorFamilyOriginalR1.exe'"|Where-Object{$_.ExecutablePath-eq$exe}|Select-Object ProcessId,ExecutablePath,CreationDate,CommandLine)}
function Live {
 try{$s=Get-Content -LiteralPath ($runtime+'\status.json') -Raw -Encoding UTF8|ConvertFrom-Json;$p=[Diagnostics.Process]::GetProcessById([int]$s.pid);try{if($p.HasExited-or$p.MainModule.FileName-ne$exe-or$p.StartTime.ToUniversalTime().Ticks-ne[long]$s.startedUtcTicks){return $null};return $s}finally{$p.Dispose()}}catch{return $null}
}
# [代码块] prototype.control.selected | 范围：第 82-92 行（PowerShell AST核验）。
# [功能] 方案、17路径、profile逻辑尺寸三者一致才持有；旧发行无尺寸字段时仍是32。
function Selected-Character($characters){
 $key=[Microsoft.Win32.Registry]::CurrentUser.OpenSubKey('Control Panel\Cursors')
 try{foreach($c in $characters){
  if($key.GetValue('')-ne$c.scheme){continue}
  $expected=if($c.expectedNativeSize){[int]$c.expectedNativeSize}else{32}
  if([int]$key.GetValue('CursorBaseSize',32)-ne$expected){continue}
  $match=$true
  foreach($role in $roles){if([Environment]::ExpandEnvironmentVariables([string]$key.GetValue($role,''))-ne(Join-Path $runtime ('assets\'+$c.id+'\'+$role+'\symbol.ani'))){$match=$false;break}}
  if($match){return $c.id}
 };return $null}finally{$key.Dispose()}
}
function Ownership-Catalog($current){
 $rows=@($current)
 if(Test-Path -LiteralPath ($runtime+'\characters.json')){$rows+=@(Get-Content -LiteralPath ($runtime+'\characters.json') -Raw -Encoding UTF8|ConvertFrom-Json)}
 if(Test-Path -LiteralPath $stateFile){$saved=Get-Content -LiteralPath $stateFile -Raw -Encoding UTF8|ConvertFrom-Json;if($saved.ownedProfiles){$rows+=@($saved.ownedProfiles)}}
 $map=@{};foreach($row in $rows){$map[[string]$row.scheme]=$row};return @($map.Values)
}
# [代码块] prototype.control.baseline | 范围：第 102-119 行（PowerShell AST核验）。
# [连接] Enable/Select在改变偏好之前调用；保留跨仓ownedProfiles和原baseline。
# [修改] 旧v131不写CursorBaseSize，因此旧记录缺字段时从尚未改动的现值迁移；动态旧记录缺失则拒绝猜测。
function Save-RestoreBaseline($catalog,$snapshot,$nativeSnapshot){
 $owned=Selected-Character $catalog
 if($owned){
  if(-not(Test-Path -LiteralPath $stateFile)){throw '本版恢复记录缺失，保留现场。'}
  $record=Get-Content -LiteralPath $stateFile -Raw -Encoding UTF8|ConvertFrom-Json
  if(@($record.previous|Where-Object{$_.name-eq'CursorBaseSize'}).Count-eq0){
   $scheme=($snapshot|Where-Object{$_.name-eq''}).value
   $oldProfile=@($catalog|Where-Object{$_.scheme-eq$scheme})
   if($oldProfile.Count-ne1-or$oldProfile[0].expectedNativeSize){throw '动态尺寸恢复基线缺失，不能用当前尺寸冒充原始偏好。'}
   $record.previous=@($record.previous)+@($snapshot|Where-Object{$_.name-eq'CursorBaseSize'})
   $record|Add-Member -NotePropertyName sizeBaselineMigration -NotePropertyValue 'legacy-v131-did-not-change-size; captured-before-first-dynamic-write' -Force
  }
 }else{$record=[pscustomobject]@{utc=[DateTime]::UtcNow.ToString('o');previous=$snapshot;previousNative=$nativeSnapshot;nativeBaselineEvidence='observed-before-first-dynamic-write'}}
 if(-not$record.PSObject.Properties['previousNative']){if([int]$record.schemaVersion-ge2){throw '动态安装记录缺少原共享位图基线，保留现场。'};$record|Add-Member -NotePropertyName previousNative -NotePropertyValue $null;$record|Add-Member -NotePropertyName nativeBaselineEvidence -NotePropertyValue 'legacy-unobserved' -Force}
 $record|Add-Member -NotePropertyName schemaVersion -NotePropertyValue 2 -Force
 $record|Add-Member -NotePropertyName ownedProfiles -NotePropertyValue $catalog -Force
 Json $stateFile $record
}
# [代码块] prototype.control.rollback-ownership | 范围：第 122-130 行（PowerShell AST核验）。
# [功能] 部分写入失败只允许原值/本次目标值的组合；发现第三方值后不覆盖外部选择。
function May-RollbackAttempt {
 if(-not$selectionWritten){return $false}
 $key=[Microsoft.Win32.Registry]::CurrentUser.OpenSubKey('Control Panel\Cursors')
 try{foreach($row in $previous){
  $now=$key.GetValue([string]$row.name,$null,[Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)
  $desired=if($row.name-eq''){$attemptScheme}elseif($row.name-eq'CursorBaseSize'){$expectedNativeSize}elseif($row.name-eq'Scheme Source'){1}else{Join-Path $runtime ('assets\'+$Character+'\'+$row.name+'\symbol.ani')}
  if($now-ne$row.value-and$now-ne$desired){return $false}
 };return $true}finally{$key.Dispose()}
}
# [代码块] prototype.control.preserve-overwritten-native | 范围：第 134-150 行（PowerShell AST核验）。
# [连接] Enable复制新payload前 → 当前17共享源与本次确会覆盖的目的路径交集。
# [功能] 只保存会被本次升级覆盖的原ANI文件；不展开通用文件事务或复制整套运行时。
function Preserve-OverwrittenNative($hashes,$native){
 $destinations=@{};foreach($item in $hashes.PSObject.Properties){$destinations[[IO.Path]::GetFullPath((Join-Path $target $item.Name))]=$item.Value}
 $saved=@();$folder=$null
 foreach($row in $native){
  if([string]::IsNullOrWhiteSpace($row.file)){continue}
  $path=[IO.Path]::GetFullPath([string]$row.file)
  if(-not$destinations.ContainsKey($path)-or-not(Test-Path -LiteralPath $path)){continue}
  $hash=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
  if($hash-eq$destinations[$path]){continue}
  if(-not$folder){$folder=Join-Path $target ('operation-rollback\'+[Guid]::NewGuid().ToString('N'));[void][IO.Directory]::CreateDirectory($folder)}
  $copy=Join-Path $folder ($row.role+'.ani');Copy-Item -LiteralPath $path -Destination $copy
  if((Get-FileHash -LiteralPath $copy -Algorithm SHA256).Hash-ne$hash){throw '升级前原ANI保存校验失败。'}
  $saved+=[pscustomobject]@{original=$path;saved=$copy;sha256=$hash}
 }
 if($folder){Json (Join-Path $folder 'receipt.json') @{purpose='current shared ANI source protection for this Enable payload overwrite';files=$saved}}
 return $saved
}
# [代码块] prototype.control.restore-overwritten-native | 范围：第 153-159 行（PowerShell AST核验）。
# [功能] Enable失败先恢复本次覆盖的原ANI字节，再从原路径按原实际尺寸恢复共享句柄。
function Restore-OverwrittenNative($saved){
 foreach($item in $saved){
  if((Get-FileHash -LiteralPath $item.saved -Algorithm SHA256).Hash-ne$item.sha256){throw '升级回退的原ANI副本校验失败。'}
  Copy-Item -LiteralPath $item.saved -Destination $item.original -Force
  if((Get-FileHash -LiteralPath $item.original -Algorithm SHA256).Hash-ne$item.sha256){throw '升级回退原ANI写回校验失败。'}
 }
}
function Status {
 $s=Live;$processes=@(Processes);$shared=@{};for($i=0;$i-lt17;$i++){$shared[$roles[$i]]=[CursorTrialControl]::Dimensions($ids[$i])}
 $key=[Microsoft.Win32.Registry]::CurrentUser.OpenSubKey('Control Panel\Cursors');try{$selection=$key.GetValue('');$size=$key.GetValue('CursorBaseSize',32)}finally{$key.Dispose()}
 $run=[Microsoft.Win32.Registry]::CurrentUser.OpenSubKey('Software\Microsoft\Windows\CurrentVersion\Run');try{$startup=$run-and([string]$run.GetValue($runName)-eq('"'+$exe+'"'))}finally{if($run){$run.Dispose()}}
 return [pscustomobject]@{utc=[DateTime]::UtcNow.ToString('o');active=[bool]($s-and$s.active);processCount=$processes.Count;processes=$processes;loginStartup=[bool]$startup;currentScheme=$selection;preference=$size;runtime=$s;sharedRoles=$shared;sharedDpiContext='unaware-logical';installDirectory=$target}
}
# [代码块] prototype.control.operation | 范围：第 169-248 行（主操作try/finally核验）。
# [连接] 用户入口 → 安装互斥 → 原baseline/ANI保护 → 写profile偏好与方案 → reloadToken → 全17校验；Stop和异常走Restore。
# [修改] plan v2：系统逻辑大小随profile，保留v131跨仓所有权与旧状态迁移；不重放旧指针安装。
try{
 Add-Type -Path (Join-Path $PSScriptRoot 'ControlNative.cs')
 $mutex=[Threading.Mutex]::new($false,('Local\AICursorFamilyInstallerOriginalR1-'+[Security.Principal.WindowsIdentity]::GetCurrent().User.Value));try{$owns=$mutex.WaitOne(0)}catch [Threading.AbandonedMutexException]{$owns=$true};if(-not$owns){throw '另一个本版操作正在执行。'}
 if($Action-in@('Enable','Select')){
  $characters=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'runtime\characters.json') -Raw -Encoding UTF8|ConvertFrom-Json
  $ownershipCharacters=@(Ownership-Catalog $characters)
  $chosen=@($characters|Where-Object{$_.id-eq$Character});if($chosen.Count-ne1){throw '角色编号无效。'}
  $expectedNativeSize=[int]$chosen[0].expectedNativeSize
  if($expectedNativeSize-notin@(32,64,96,128,256)){throw '完整原生符号尺寸无效。'}
  $attemptScheme=[string]$chosen[0].scheme
  $legacyStopped=$false
  if($Action-eq'Enable'-and-not(Selected-Character $ownershipCharacters)){
   $r3=Join-Path $env:LOCALAPPDATA 'AICursorAvoidanceTrial\1.0.1\control.ps1'
   if(Test-Path -LiteralPath $r3){& $engineExecutable -NoLogo -NoProfile -ExecutionPolicy Bypass -File $r3 -Action Stop;if($LASTEXITCODE-ne0){throw '旧r3未正常关闭。'}}
   & (Join-Path $PSScriptRoot 'legacy-keeper-control.ps1') -Action Stop
   $legacyStopped=$true
  }
  # Numbered entries must reuse the running family process too. A current,
  # verified installation can select without stopping or copying the runtime.
  if($Action-eq'Enable'-and@(Processes).Count-le1-and(Test-Path -LiteralPath ($target+'\payload-hashes.json'))){
   $sourceManifest=Join-Path $PSScriptRoot 'payload-hashes.json'
   if((Get-FileHash -LiteralPath $sourceManifest -Algorithm SHA256).Hash-eq(Get-FileHash -LiteralPath ($target+'\payload-hashes.json') -Algorithm SHA256).Hash){
    $known=Get-Content -LiteralPath $sourceManifest -Raw -Encoding UTF8|ConvertFrom-Json;$intact=(Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash-eq$known.'runtime/AICursorFamilyOriginalR1.exe'
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
   if(Test-Path -LiteralPath $r3){& $engineExecutable -NoLogo -NoProfile -ExecutionPolicy Bypass -File $r3 -Action Stop;if($LASTEXITCODE-ne0){throw '旧r3未正常关闭。'}}
   & (Join-Path $PSScriptRoot 'legacy-keeper-control.ps1') -Action Stop
   }
   Stop-Engine
   $previous=Snapshot;$previousNative=Snapshot-Native $previous;$old=Selected-Character $ownershipCharacters;$changed=$true
   [void][IO.Directory]::CreateDirectory($target)
   Save-RestoreBaseline $ownershipCharacters $previous $previousNative
   $overwriteSources=@(Preserve-OverwrittenNative $hashes $previousNative)
   foreach($f in $hashes.PSObject.Properties){$source=Join-Path $PSScriptRoot $f.Name;$destination=Join-Path $target $f.Name;if([IO.Path]::GetFullPath($source)-eq[IO.Path]::GetFullPath($destination)){continue};[void][IO.Directory]::CreateDirectory((Split-Path -Parent $destination));if((Test-Path -LiteralPath $destination)-and(Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash-eq$f.Value){continue};Copy-Item -LiteralPath $source -Destination $destination -Force;if((Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash-ne$f.Value){throw ('复制校验失败：'+$f.Name)}}
   if([IO.Path]::GetFullPath($PSScriptRoot)-ne[IO.Path]::GetFullPath($target)){Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'payload-hashes.json') -Destination (Join-Path $target 'payload-hashes.json') -Force -ErrorAction Stop}
   $key=[Microsoft.Win32.Registry]::CurrentUser.CreateSubKey('Control Panel\Cursors\Schemes');try{foreach($c in $characters){$paths=@($roles|ForEach-Object{Join-Path $runtime ('assets\'+$c.id+'\'+$_+'\symbol.ani')});$key.SetValue($c.scheme,($paths-join','),[Microsoft.Win32.RegistryValueKind]::String)}}finally{$key.Dispose()}
  }else{
   if(-not(Test-Path -LiteralPath $exe)){throw '请先运行角色入口安装本版。'}
   if((Selected-Character $ownershipCharacters)-and-not(Test-Path -LiteralPath $stateFile)){throw '本版恢复记录缺失，保留现场。'}
   $previous=Snapshot;$previousNative=Snapshot-Native $previous
   Save-RestoreBaseline $ownershipCharacters $previous $previousNative
  }
  $changed=$true;$selectionWritten=$true;$key=[Microsoft.Win32.Registry]::CurrentUser.CreateSubKey('Control Panel\Cursors');try{$key.SetValue('CursorBaseSize',$expectedNativeSize,[Microsoft.Win32.RegistryValueKind]::DWord);foreach($role in $roles){$key.SetValue($role,(Join-Path $runtime ('assets\'+$Character+'\'+$role+'\symbol.ani')),[Microsoft.Win32.RegistryValueKind]::String)};$key.SetValue('',$chosen[0].scheme,[Microsoft.Win32.RegistryValueKind]::String);$key.SetValue('Scheme Source',1,[Microsoft.Win32.RegistryValueKind]::DWord)}finally{$key.Dispose()}
  if(-not[CursorTrialControl]::SystemParametersInfo(0x57,0,[IntPtr]::Zero,0)){throw 'Windows刷新功能光标失败。'}
  $reloadToken=0;$beforeReload=Live
  if($beforeReload-and$beforeReload.active){$reloadToken=([Guid]::NewGuid().GetHashCode()-band[int]::MaxValue);if($reloadToken-eq0){$reloadToken=1};if(-not[CursorTrialControl]::RequestNativeReload([long]$beforeReload.controlHwnd,[int]$beforeReload.pid,$reloadToken)){throw '本版重载通知未送达。'}}
  if(@(Processes).Count-eq0){Start-Process -FilePath $exe -WindowStyle Hidden}
  $until=[DateTime]::UtcNow.AddSeconds(12)
  do{$s=Live;if($s-and$s.active-and$s.character-eq$Character-and$s.avatarHwnd-ne0-and($reloadToken-eq0-or$s.nativeReloadToken-eq$reloadToken)){break};Start-Sleep -Milliseconds 100}while([DateTime]::UtcNow-lt$until)
  if(-not$s-or-not$s.active-or$s.character-ne$Character-or@(Processes).Count-ne1-or($reloadToken-ne0-and$s.nativeReloadToken-ne$reloadToken)){throw '人物运行程序未正常启用，请查看runtime内error.txt/fatal.txt。'}
  for($i=0;$i-lt17;$i++){$d=[CursorTrialControl]::Dimensions($ids[$i]);if($d[0]-ne$expectedNativeSize-or$d[1]-ne$expectedNativeSize){throw ('共享光标尺寸异常：'+$roles[$i])}}
  $key=[Microsoft.Win32.Registry]::CurrentUser.CreateSubKey('Software\Microsoft\Windows\CurrentVersion\Run');try{$key.SetValue($runName,('"'+$exe+'"'),[Microsoft.Win32.RegistryValueKind]::String)}finally{$key.Dispose()}
  $result=Status;Json (Join-Path $target 'last-operation.json') $result
  Write-Host ('已启用 '+$chosen[0].display+'：保留完整符号，系统指针基准随本档设为'+$expectedNativeSize+'；停止后恢复原偏好。人物原布局、不避让提示框。') -ForegroundColor Green
 }elseif($Action-eq'Stop'){
  $characters=if(Test-Path -LiteralPath ($runtime+'\characters.json')){Get-Content -LiteralPath ($runtime+'\characters.json') -Raw -Encoding UTF8|ConvertFrom-Json}else{@()}
  $characters=@(Ownership-Catalog $characters)
  $selected=Selected-Character $characters;Stop-Engine
  if($selected-and(Selected-Character $characters)-eq$selected){if(-not(Test-Path -LiteralPath $stateFile)){throw '程序已停止，但恢复记录缺失。'};$savedRestore=Get-Content -LiteralPath $stateFile -Raw -Encoding UTF8|ConvertFrom-Json;Restore $savedRestore.previous $savedRestore.previousNative}
  $result=Status;if($result.processCount-ne0-or$result.loginStartup){throw '后台清理尚未完成。'}
  if(Test-Path -LiteralPath $target){Json (Join-Path $target 'last-operation.json') $result}
  Write-Host '已关闭，本版后台进程0，登录启动已取消。' -ForegroundColor Green
 }else{$result=Status;$result|ConvertTo-Json -Depth 12}
 exit 0
}catch{
 $failure=$_
 if($changed-and$previous){try{$rollback=(May-RollbackAttempt)-or(-not$selectionWritten-and(Matches-Snapshot $previous));Stop-Engine;Restore-OverwrittenNative $overwriteSources;if($rollback){Restore $previous $previousNative}}catch{Write-Host ('回退错误：'+$_.Exception.Message)}}
 Write-Host ('操作失败：'+$failure.Exception.Message) -ForegroundColor Red
 if(Test-Path -LiteralPath $target){Json (Join-Path $target 'last-failure.json') @{error=$failure.Exception.ToString();position=$failure.InvocationInfo.PositionMessage}}
 exit 1
}finally{if($owns){$mutex.ReleaseMutex()};if($mutex){$mutex.Dispose()}}
