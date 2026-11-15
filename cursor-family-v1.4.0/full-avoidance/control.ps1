[CmdletBinding()]
param([ValidateSet('Enable','Select','Stop','Status')][string]$Action='Status',[string]$Character='astra')
$ErrorActionPreference='Stop'
$target=Join-Path $env:LOCALAPPDATA 'AICursorFamilyFullAvoidance\v1'
$runtime=Join-Path $target 'runtime'
$exe=Join-Path $runtime 'AICursorFamilyFullAvoidance.exe'
$stateFile=Join-Path $target 'installation-state.json'
$roles=@('Arrow','Help','AppStarting','Wait','Crosshair','IBeam','NWPen','No','SizeNS','SizeWE','SizeNWSE','SizeNESW','SizeAll','UpArrow','Hand','Pin','Person')
$ids=@(32512,32651,32650,32514,32515,32513,32631,32648,32645,32644,32642,32643,32646,32516,32649,32671,32672)
$runName='AICursorFamilyFullAvoidance'
$utf8=[Text.UTF8Encoding]::new($false)
$engineExecutable=Join-Path $PSHOME $(if($PSVersionTable.PSEdition-eq'Core'){'pwsh.exe'}else{'powershell.exe'})
$priorStartupOwned=$false;$priorProfile=$null;$priorRunning=$false;$rollbackSelectionRestored=$false;
$phase='initialize';$operationId=[Guid]::NewGuid().ToString('N');$rollbackStages=@();$owns=$false;$changed=$false;$previous=$null;$selectionWritten=$false;$attemptScheme=$null;$expectedNativeSize=0;$previousNative=$null;$overwriteSources=@()
# [代码块] prototype.control.json | 范围：第 17-17 行（本次PowerShell AST核验）。
# [功能] 新状态先写同目录临时文件再原子替换；恢复基线不能留下半份JSON。
function Json($path,$value){$temp=$path+'.tmp';[IO.File]::WriteAllText($temp,($value|ConvertTo-Json -Depth 18),$utf8);if(Test-Path -LiteralPath $path){[IO.File]::Replace($temp,$path,[NullString]::Value)}else{[IO.File]::Move($temp,$path)}}
# [代码块] prototype.control.snapshot | 范围：第 20-23 行（本次PowerShell AST核验）。
# [修改] plan v2：保存CursorBaseSize的存在性、RegistryValueKind和值，连同原17路径。
function Snapshot {
 $key=[Microsoft.Win32.Registry]::CurrentUser.OpenSubKey('Control Panel\Cursors')
 try{return @(foreach($name in @('')+$roles+@('Scheme Source','CursorBaseSize')){$exists=$name-in$key.GetValueNames();[pscustomobject]@{name=$name;exists=$exists;value=if($exists){$key.GetValue($name,$null,[Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)}else{$null};kind=if($exists){$key.GetValueKind($name).ToString()}else{$null}}})}finally{$key.Dispose()}
}
# [代码块] prototype.control.baseline-paths | 范围：第 27-37 行（本次PowerShell AST核验）。
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
# [代码块] prototype.control.restore | 范围：第 42-48 行（本次PowerShell AST核验）。
# [连接] Stop/失败回退 → 原偏好与原方案路径 → SPI刷新；不合成猜测的旧基线。
# [代码块] full.control.validate-baseline | 范围：第 42-48 行（本次PowerShell AST核验）。
# [功能/连接] Restore/迁移 → 恢复字段完整性；写系统字段前拒绝重复、未知类型、缺失角色。
function Validate-RestoreRows($rows){
 foreach($name in @('')+$roles+@('Scheme Source','CursorBaseSize')){
  $matches=@($rows|Where-Object{$_.name-eq$name})
  if($matches.Count-ne1){throw ('恢复字段缺失或重复：'+$name)}
  if($matches[0].exists-and$matches[0].kind-notin@('String','ExpandString','DWord')){throw '恢复记录的注册表类型无效。'}
 }
}
function Restore($rows,$native){
 Validate-RestoreRows $rows
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
# [代码块] prototype.control.native-snapshot | 范围：第 67-73 行（本次PowerShell AST核验）。
# [连接] Enable/Select改变前采集；原ANI路径和实际unaware几何一起交给Restore。
function Snapshot-Native($rows){
 return @(for($i=0;$i-lt17;$i++){
  $g=[CursorTrialControl]::Geometry($ids[$i]);if($g[0]-ne$g[1]){throw '当前共享光标不是方形，无法无损记录当前恢复合同。'}
  $value=($rows|Where-Object{$_.name-eq$roles[$i]}).value
  [pscustomobject]@{role=$roles[$i];file=[Environment]::ExpandEnvironmentVariables([string]$value);size=$g[0];hotX=$g[2];hotY=$g[3]}
 })
}
# [代码块] prototype.control.matches-snapshot | 范围：第 76-82 行（本次PowerShell AST核验）。
# [功能] 原生恢复逐项提交前验证原方案字段仍属于本次恢复，不抢回外部新选择。
function Matches-Snapshot($rows){
 $key=[Microsoft.Win32.Registry]::CurrentUser.OpenSubKey('Control Panel\Cursors')
 try{foreach($row in $rows){
  $exists=$row.name-in$key.GetValueNames();if([bool]$row.exists-ne$exists){return $false}
  if($exists-and($key.GetValue([string]$row.name,$null,[Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)-ne$row.value-or$key.GetValueKind([string]$row.name).ToString()-ne$row.kind)){return $false}
 };return $true}finally{$key.Dispose()}
}
# [代码块] full.control.stage | 范围：第 86-90 行（本次PowerShell AST核验）。
# [功能/连接] 安装阶段与失败回退 → 追加诊断；诊断失败不得遮蔽最早异常。
# [修改] v1.4.0 保留阶段、PID、会话、原始异常、Windows错误码与操作身份。
function Record-Stage([string]$stage,[string]$outcome,$detail){
 $script:phase=$stage
 $record=@{utc=[DateTime]::UtcNow.ToString('o');operation=$operationId;action=$Action;character=$Character;phase=$stage;outcome=$outcome;pid=$PID;session=[Diagnostics.Process]::GetCurrentProcess().SessionId;detail=$detail}
 try{[void][IO.Directory]::CreateDirectory($target);[IO.File]::AppendAllText((Join-Path $target 'operations.jsonl'),(($record|ConvertTo-Json -Compress -Depth 8)+[Environment]::NewLine),$utf8)}catch{Write-Warning ('诊断日志写入失败：'+$_.Exception.Message)}
}
# [代码块] full.control.processes | 范围：第 93-96 行（本次PowerShell AST核验）。
# [功能/连接] Stop/Enable/Status → 精确路径与当前登录会话过滤；名称仅作查询缩小范围。
function Owned-Processes([string]$path){
 $name=[IO.Path]::GetFileName($path);$sessionId=[Diagnostics.Process]::GetCurrentProcess().SessionId
 return @(Get-CimInstance Win32_Process -Filter ("Name='"+$name.Replace("'","''")+"'")|Where-Object{$_.ExecutablePath-eq$path-and$_.SessionId-eq$sessionId}|Select-Object ProcessId,ExecutablePath,CreationDate,CommandLine,SessionId)
}
function Processes {return @(Owned-Processes $exe)}
# [代码块] full.control.preflight-integrity | 范围：第 101-108 行（本次PowerShell AST核验）。
# [功能/连接] Enable/Select/Stop/迁移 → 同会话精确路径和Token级别核对 → 才允许Run/方案/实例变更。
# [修改] 无法读取身份或目标级别更高时保留原状态，不提权、不修改UIPI规则。
function Assert-ControlAccess([string]$path){
 $name=[IO.Path]::GetFileName($path);$sessionId=[Diagnostics.Process]::GetCurrentProcess().SessionId
 $items=@(Get-CimInstance Win32_Process -Filter ("Name='"+$name.Replace("'","''")+"'")|Where-Object{$_.SessionId-eq$sessionId})
 foreach($item in $items){
  try{$actual=[CursorTrialControl]::ProcessPath([int]$item.ProcessId);if(-not[String]::Equals($actual,$path,[StringComparison]::OrdinalIgnoreCase)){continue};$current=[CursorTrialControl]::ProcessIntegrity($PID);$other=[CursorTrialControl]::ProcessIntegrity([int]$item.ProcessId)}catch{throw '无法核对同会话人物实例的控制权限，已保留原方案与登录启动；请用启动旧实例时的相同权限关闭它，再普通双击启用。'}
  if($other-gt$current){throw '已有管理员权限人物实例，本次普通权限无法可靠控制；已保留原方案与登录启动，请用相同管理员权限关闭旧实例后，再普通双击启用。'}
 }
}

# [代码块] full.control.stop-owned | 范围：第 113-131 行（本次PowerShell AST核验）。
# [功能/连接] Stop-Engine/迁移 → 已知会话停止事件 → 等待原PID退出；绝不启动EXE用于停止。
# [修改] v1.4.0 无活实例直接结束；事件缺失/停止超时明确报错，不按名称强杀。
function Stop-OwnedEngine([string]$path,[string]$eventPrefix,[string]$startupName){
 Assert-ControlAccess $path
 try {
  $owned=@(Owned-Processes $path)
  if($owned.Count-eq0){return}
  if($owned.Count-ne1){throw '同一路径存在多个归属实例，保留现场。'}
  $id=[int]$owned[0].ProcessId;$process=[Diagnostics.Process]::GetProcessById($id)
  try {
   if($process.MainModule.FileName-ne$path-or$process.SessionId-ne[Diagnostics.Process]::GetCurrentProcess().SessionId){throw '待停实例身份发生变化。'}
   $eventName='Local\'+$eventPrefix+'-'+[Security.Principal.WindowsIdentity]::GetCurrent().User.Value+'-'+$process.SessionId+'-stop'
   try{$signal=[Threading.EventWaitHandle]::OpenExisting($eventName)}catch [Threading.WaitHandleCannotBeOpenedException]{throw '已确认的进程尚无停止事件，保留现场；可稍后重试。'}
   try{[void]$signal.Set()}finally{$signal.Dispose()}
   if(-not$process.WaitForExit(10000)){throw '已确认实例未在10秒内退出。'}
  }finally{$process.Dispose()}
 } finally {
  $key=[Microsoft.Win32.Registry]::CurrentUser.OpenSubKey('Software\Microsoft\Windows\CurrentVersion\Run',$true)
  if($key){try{if([string]$key.GetValue($startupName)-eq('"'+$path+'"')){$key.DeleteValue($startupName,$false)}}finally{$key.Dispose()}}
 }
}
function Stop-Engine {Stop-OwnedEngine $exe 'AICursorFamilyFullAvoidance' $runName}
# [代码块] full.control.capture-intent | 范围：第 136-141 行（本次PowerShell AST核验）。
# [功能/连接] Enable/Select首个停止前 → 保存精确旧profile、已核实Run和同会话运行意图。
# [修改] v1.4.0 失败切换不再无声丢掉旧版自动启动。
function Capture-PreviousIntent($catalog){
 $script:priorProfile=Selected-Character $catalog
 $script:priorRunning=[bool]($priorProfile-and(Live))
 $key=[Microsoft.Win32.Registry]::CurrentUser.OpenSubKey('Software\Microsoft\Windows\CurrentVersion\Run')
 try{$script:priorStartupOwned=[bool]($priorProfile-and$key-and([string]$key.GetValue($runName)-eq('"'+$exe+'"')))}finally{if($key){$key.Dispose()}}
}
# [代码块] full.control.restore-intent | 范围：第 145-159 行（本次PowerShell AST核验）。
# [功能/连接] 安全恢复旧系统方案后 → 恢复原Run → 尝试原人物；每阶段独立保留结果。
# [修改] v1.4.0 系统拦截再次启动时保留原登录意图，明确人物恢复失败，不冒充全面回退成功。
function Restore-PreviousIntent {
 if(-not$rollbackSelectionRestored-or-not$priorProfile-or-not(Matches-Snapshot $previous)){return}
 if($priorStartupOwned){
  try{$key=[Microsoft.Win32.Registry]::CurrentUser.CreateSubKey('Software\Microsoft\Windows\CurrentVersion\Run');try{$key.SetValue($runName,('"'+$exe+'"'),[Microsoft.Win32.RegistryValueKind]::String)}finally{$key.Dispose()};$script:rollbackStages+=@{phase='restore-startup-intent';outcome='success'}}catch{$script:rollbackStages+=@{phase='restore-startup-intent';outcome='failed';error=$_.Exception.ToString()}}
 }
 if($priorRunning){
  try{
   if(@(Processes).Count-eq0){Start-Process -FilePath $exe -WindowStyle Hidden}
   $until=[DateTime]::UtcNow.AddSeconds(12)
   do{$live=Live;if($live-and$live.active-and$live.character-eq$priorProfile){break};Start-Sleep -Milliseconds 100}while([DateTime]::UtcNow-lt$until)
   if(-not$live-or-not$live.active-or$live.character-ne$priorProfile){throw '原人物未恢复运行；原登录启动意图单独保留。'}
   $script:rollbackStages+=@{phase='restore-avatar';outcome='success';profile=$priorProfile;pid=$live.pid}
  }catch{$script:rollbackStages+=@{phase='restore-avatar';outcome='failed';error=$_.Exception.ToString()}}
 }
}
# [代码块] full.control.migrate | 范围：第 163-203 行（本次PowerShell AST核验）。
# [功能/连接] Enable 写新方案前 → 两种已知旧安装身份 → 安全停旧实例/恢复旧baseline → 新方案快照。
# [修改] v1.4.0 支持旧EXE被拒启但旧scheme已选中的遗留；从旧记录恢复，不再次执行旧EXE。
function Migrate-KnownFamily {
 $definitions=@(
  @{directory='AICursorFamilyAvoidance\r5';exe='AICursorFamilyR5.exe';prefix='AICursorFamilyR5';run='AICursorFamilyAvoidanceR5'},
  @{directory='AICursorFamilyNonAvoidance\original-r1';exe='AICursorFamilyOriginalR1.exe';prefix='AICursorFamilyOriginalR1';run='AICursorFamilyNonAvoidanceR1'}
 )
 foreach($definition in $definitions){
  $oldTarget=Join-Path $env:LOCALAPPDATA $definition.directory;$oldRuntime=Join-Path $oldTarget 'runtime';$oldExe=Join-Path $oldRuntime $definition.exe
  $catalogPath=Join-Path $oldRuntime 'characters.json';$recordPath=Join-Path $oldTarget 'installation-state.json'
  $selected=$false;$oldRows=@();$saved=$null
  if(Test-Path -LiteralPath $catalogPath){$oldItems=Get-Content -LiteralPath $catalogPath -Raw -Encoding UTF8|ConvertFrom-Json;$oldRows=@($oldItems)}
  if(Test-Path -LiteralPath $recordPath){$saved=Get-Content -LiteralPath $recordPath -Raw -Encoding UTF8|ConvertFrom-Json;if($saved.ownedProfiles){$oldRows+=@($saved.ownedProfiles)}}
  $key=[Microsoft.Win32.Registry]::CurrentUser.OpenSubKey('Control Panel\Cursors')
  try {
   $scheme=[string]$key.GetValue('');$matches=@($oldRows|Where-Object{$_.scheme-eq$scheme})
   foreach($row in $matches){
    $match=$true
    if($row.expectedNativeSize-and[int]$key.GetValue('CursorBaseSize',32)-ne[int]$row.expectedNativeSize){$match=$false}
    foreach($role in $roles){if([Environment]::ExpandEnvironmentVariables([string]$key.GetValue($role,''))-ne(Join-Path $oldRuntime ('assets\'+$row.id+'\'+$role+'\symbol.ani'))){$match=$false;break}}
    if($match){$selected=$true;break}
   }
   # A partial/foreign selection referring to old resources must not be captured as a new baseline.
   $referencesOld=@($roles|Where-Object{([Environment]::ExpandEnvironmentVariables([string]$key.GetValue($_,''))).StartsWith($oldRuntime+'\',[StringComparison]::OrdinalIgnoreCase)}).Count
   if($referencesOld-gt0-and-not$selected){throw '旧方案字段/尺寸不一致，保留现场，无法安全迁移。'}
  }finally{$key.Dispose()}
  if($selected){
   if(-not$saved-or-not$saved.previous){throw '旧版已选中但恢复记录缺失，保留现场。'}
   if([int]$saved.schemaVersion-ge2-and-not$saved.previousNative){throw '旧动态尺寸恢复记录不完整，保留现场。'}
   $restoreRows=@($saved.previous)
   if(@($restoreRows|Where-Object{$_.name-eq'CursorBaseSize'}).Count-eq0){
    if([int]$saved.schemaVersion-ge2-or$row.expectedNativeSize){throw '旧动态尺寸缺少原始尺寸基线，保留现场。'}
    $restoreRows+=@(Snapshot|Where-Object{$_.name-eq'CursorBaseSize'})
   }
   Validate-RestoreRows $restoreRows;Validate-NativeBaselinePaths $restoreRows $saved.previousNative
   $migrationSnapshot=Snapshot
   Stop-OwnedEngine $oldExe $definition.prefix $definition.run
   if(-not(Matches-Snapshot $migrationSnapshot)){throw '迁移期间外部方案改变，已保留外部选择。'}
   Restore $restoreRows $saved.previousNative
   Record-Stage 'migration' 'restored' @{source=$oldTarget;schema=$saved.schemaVersion;nativeEvidence=if($saved.previousNative){'recorded'}else{'legacy-unobserved'}}
  }else{Stop-OwnedEngine $oldExe $definition.prefix $definition.run}
 }
}
function Live {
 try{$s=Get-Content -LiteralPath ($runtime+'\status.json') -Raw -Encoding UTF8|ConvertFrom-Json;$p=[Diagnostics.Process]::GetProcessById([int]$s.pid);try{if($p.HasExited-or$p.MainModule.FileName-ne$exe-or$p.StartTime.ToUniversalTime().Ticks-ne[long]$s.startedUtcTicks){return $null};return $s}finally{$p.Dispose()}}catch{return $null}
}
# [代码块] prototype.control.selected | 范围：第 209-219 行（本次PowerShell AST核验）。
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
# [代码块] full.control.read-catalog | 范围：第 223-226 行（本次PowerShell AST核验）。
# [功能/连接] Windows PowerShell5.1/Core解析JSON数组 → 明确逐profile输出，防止value/Count数组包装写进安装catalog。
# [修改] 先赋值再枚举，不对ConvertFrom-Json直接套数组子表达式；结构异常在修改前拒绝。
function Read-Catalog([string]$path){
 $items=Get-Content -LiteralPath $path -Raw -Encoding UTF8|ConvertFrom-Json
 foreach($item in $items){if(-not$item.id-or-not$item.scheme-or$item.expectedNativeSize-notin@(32,64,96,128,256)){throw '角色目录结构无效，保留当前安装。'};$item}
}
function Ownership-Catalog($current){
 $rows=@($current)
 if(Test-Path -LiteralPath ($runtime+'\characters.json')){$rows+=@(Read-Catalog ($runtime+'\characters.json'))}
 if(Test-Path -LiteralPath $stateFile){$saved=Get-Content -LiteralPath $stateFile -Raw -Encoding UTF8|ConvertFrom-Json;if($saved.ownedProfiles){$rows+=@($saved.ownedProfiles)}}
 $map=@{};foreach($row in $rows){if(-not$row.id-or-not$row.scheme-or$row.expectedNativeSize-notin@(32,64,96,128,256)){throw '已登记角色目录结构无效，保留当前安装。'};$map[[string]$row.scheme]=$row};return @($map.Values)
}
# [代码块] prototype.control.baseline | 范围：第 236-253 行（本次PowerShell AST核验）。
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
# [代码块] prototype.control.rollback-ownership | 范围：第 256-264 行（本次PowerShell AST核验）。
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
# [代码块] prototype.control.preserve-overwritten-native | 范围：第 268-284 行（本次PowerShell AST核验）。
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
# [代码块] prototype.control.restore-overwritten-native | 范围：第 287-293 行（本次PowerShell AST核验）。
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
# [代码块] prototype.control.operation | 范围：第 303-407 行（本次PowerShell AST核验）。
# [连接] 用户入口 → 安装互斥 → 原baseline/ANI保护 → 写profile偏好与方案 → reloadToken → 全17校验；Stop和异常走Restore。
# [修改] plan v2：系统逻辑大小随profile，保留v131跨仓所有权与旧状态迁移；不重放旧指针安装。
try{
 Add-Type -Path (Join-Path $PSScriptRoot 'ControlNative.cs')
 $mutex=[Threading.Mutex]::new($false,('Local\AICursorFamilyInstallerFullAvoidance-'+[Security.Principal.WindowsIdentity]::GetCurrent().User.Value));try{$owns=$mutex.WaitOne(0)}catch [Threading.AbandonedMutexException]{$owns=$true};if(-not$owns){throw '另一个本版操作正在执行。'}
 if($Action-in@('Enable','Select')){
  $characters=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'runtime\characters.json') -Raw -Encoding UTF8|ConvertFrom-Json
  $ownershipCharacters=@(Ownership-Catalog $characters)
  $chosen=@($characters|Where-Object{$_.id-eq$Character});if($chosen.Count-ne1){throw '角色编号无效。'}
  $expectedNativeSize=[int]$chosen[0].expectedNativeSize
  if($expectedNativeSize-notin@(32,64,96,128,256)){throw '完整原生符号尺寸无效。'}
  $attemptScheme=[string]$chosen[0].scheme
  # [代码块] full.control.input-preflight | 范围：第 316-326 行（本次PowerShell AST核验）。
  # [功能/连接] Enable完整输入校验 → 已知旧版迁移 → 本版安装；损坏包不停止旧实例、不删旧Run或改旧方案。
  # [修改] v1.4.0：将原安装分支的hash校验提前，后续复用$hashes，不重复扫描整包。
  if($Action-eq'Enable'){
   Record-Stage 'validate-payload' 'begin' $null
   $hashes=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'payload-hashes.json') -Raw -Encoding UTF8|ConvertFrom-Json
   foreach($f in $hashes.PSObject.Properties){if((Get-FileHash -LiteralPath (Join-Path $PSScriptRoot $f.Name) -Algorithm SHA256).Hash-ne$f.Value){throw ('包校验失败：'+$f.Name)}}
   Record-Stage 'validate-payload' 'success' $null
   Record-Stage 'control-access' 'begin' $null
   Assert-ControlAccess $exe
   Assert-ControlAccess (Join-Path $env:LOCALAPPDATA 'AICursorFamilyAvoidance\r5\runtime\AICursorFamilyR5.exe')
   Assert-ControlAccess (Join-Path $env:LOCALAPPDATA 'AICursorFamilyNonAvoidance\original-r1\runtime\AICursorFamilyOriginalR1.exe')
   Record-Stage 'migration' 'begin' $null;Migrate-KnownFamily
  }
  # Numbered entries must reuse the running family process too. A current,
  # verified installation can select without stopping or copying the runtime.
  if($Action-eq'Enable'-and@(Processes).Count-le1-and(Test-Path -LiteralPath ($target+'\payload-hashes.json'))){
   $sourceManifest=Join-Path $PSScriptRoot 'payload-hashes.json'
   if((Get-FileHash -LiteralPath $sourceManifest -Algorithm SHA256).Hash-eq(Get-FileHash -LiteralPath ($target+'\payload-hashes.json') -Algorithm SHA256).Hash){
    $known=Get-Content -LiteralPath $sourceManifest -Raw -Encoding UTF8|ConvertFrom-Json;$intact=(Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash-eq$known.'runtime/AICursorFamilyFullAvoidance.exe'
    foreach($f in $known.PSObject.Properties){if($f.Name.StartsWith(('runtime/assets/'+$Character+'/'),[StringComparison]::Ordinal)-or$f.Name.StartsWith(('runtime/assets/characters/'+$chosen[0].character+'/'+$chosen[0].size+'/'),[StringComparison]::Ordinal)){if(-not(Test-Path -LiteralPath (Join-Path $target $f.Name))-or(Get-FileHash -LiteralPath (Join-Path $target $f.Name) -Algorithm SHA256).Hash-ne$f.Value){$intact=$false;break}}}
    if($intact-and(-not(Selected-Character $ownershipCharacters)-or(Test-Path -LiteralPath $stateFile))){$Action='Select'}
   }
  }
  Record-Stage 'control-access' 'begin' $null;Assert-ControlAccess $exe
  Capture-PreviousIntent $ownershipCharacters
  if($Action-eq'Enable'){
   $alreadySelected=Selected-Character $ownershipCharacters
   if($alreadySelected-and-not(Test-Path -LiteralPath $stateFile)){throw '本版恢复记录缺失，保留运行现场。'}
   $previous=Snapshot;$previousNative=Snapshot-Native $previous;$old=Selected-Character $ownershipCharacters;$changed=$true
   Stop-Engine
   [void][IO.Directory]::CreateDirectory($target)
   Save-RestoreBaseline $ownershipCharacters $previous $previousNative
   $overwriteSources=@(Preserve-OverwrittenNative $hashes $previousNative)
   foreach($f in $hashes.PSObject.Properties){$source=Join-Path $PSScriptRoot $f.Name;$destination=Join-Path $target $f.Name;if($f.Name-eq'runtime/avoidance-settings.json'-and(Test-Path -LiteralPath $destination)){continue};if([IO.Path]::GetFullPath($source)-eq[IO.Path]::GetFullPath($destination)){continue};[void][IO.Directory]::CreateDirectory((Split-Path -Parent $destination));if((Test-Path -LiteralPath $destination)-and(Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash-eq$f.Value){continue};Copy-Item -LiteralPath $source -Destination $destination -Force;if((Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash-ne$f.Value){throw ('复制校验失败：'+$f.Name)}}
   # [修改] v1.4.0：保留已安装系列的profile目录；新包同id优先，避免回退旧人物时catalog缺失。
   $catalogMap=@{};foreach($profile in @($ownershipCharacters)+@($characters)){$catalogMap[[string]$profile.id]=$profile};Json (Join-Path $runtime 'characters.json') @($catalogMap.Values)
   if([IO.Path]::GetFullPath($PSScriptRoot)-ne[IO.Path]::GetFullPath($target)){Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'payload-hashes.json') -Destination (Join-Path $target 'payload-hashes.json') -Force -ErrorAction Stop}
   $key=[Microsoft.Win32.Registry]::CurrentUser.CreateSubKey('Control Panel\Cursors\Schemes');try{foreach($c in $characters){$paths=@($roles|ForEach-Object{Join-Path $runtime ('assets\'+$c.id+'\'+$_+'\symbol.ani')});$key.SetValue($c.scheme,($paths-join','),[Microsoft.Win32.RegistryValueKind]::String)}}finally{$key.Dispose()}
  }else{
   if(-not(Test-Path -LiteralPath $exe)){throw '请先运行角色入口安装本版。'}
   if((Selected-Character $ownershipCharacters)-and-not(Test-Path -LiteralPath $stateFile)){throw '本版恢复记录缺失，保留现场。'}
   $previous=Snapshot;$previousNative=Snapshot-Native $previous
   Save-RestoreBaseline $ownershipCharacters $previous $previousNative
  }
  Record-Stage 'write-selection' 'begin' @{scheme=$attemptScheme;size=$expectedNativeSize}
  $changed=$true;$selectionWritten=$true;$key=[Microsoft.Win32.Registry]::CurrentUser.CreateSubKey('Control Panel\Cursors');try{$key.SetValue('CursorBaseSize',$expectedNativeSize,[Microsoft.Win32.RegistryValueKind]::DWord);foreach($role in $roles){$key.SetValue($role,(Join-Path $runtime ('assets\'+$Character+'\'+$role+'\symbol.ani')),[Microsoft.Win32.RegistryValueKind]::String)};$key.SetValue('',$chosen[0].scheme,[Microsoft.Win32.RegistryValueKind]::String);$key.SetValue('Scheme Source',1,[Microsoft.Win32.RegistryValueKind]::DWord)}finally{$key.Dispose()}
  if(-not[CursorTrialControl]::SystemParametersInfo(0x57,0,[IntPtr]::Zero,0)){throw 'Windows刷新功能光标失败。'}
  $reloadToken=0;$beforeReload=Live
  if($beforeReload-and$beforeReload.active){$reloadToken=([Guid]::NewGuid().GetHashCode()-band[int]::MaxValue);if($reloadToken-eq0){$reloadToken=1};if(-not[CursorTrialControl]::RequestNativeReload([long]$beforeReload.controlHwnd,[int]$beforeReload.pid,$reloadToken)){throw '本版重载通知未送达。'}}
  if(@(Processes).Count-eq0){Record-Stage 'start-engine' 'begin' @{path=$exe};Start-Process -FilePath $exe -WindowStyle Hidden;Record-Stage 'start-engine' 'launched' $null}
  Record-Stage 'verify-engine' 'begin' $null
  $until=[DateTime]::UtcNow.AddSeconds(12)
  do{$s=Live;if($s-and$s.active-and$s.character-eq$Character-and$s.avatarHwnd-ne0-and($reloadToken-eq0-or$s.nativeReloadToken-eq$reloadToken)){break};Start-Sleep -Milliseconds 100}while([DateTime]::UtcNow-lt$until)
  if(-not$s-or-not$s.active-or$s.character-ne$Character-or@(Processes).Count-ne1-or($reloadToken-ne0-and$s.nativeReloadToken-ne$reloadToken)){throw '人物运行程序未正常启用，请查看runtime内error.txt/fatal.txt。'}
  for($i=0;$i-lt17;$i++){$d=[CursorTrialControl]::Dimensions($ids[$i]);if($d[0]-ne$expectedNativeSize-or$d[1]-ne$expectedNativeSize){throw ('共享光标尺寸异常：'+$roles[$i])}}
  $key=[Microsoft.Win32.Registry]::CurrentUser.CreateSubKey('Software\Microsoft\Windows\CurrentVersion\Run');try{$key.SetValue($runName,('"'+$exe+'"'),[Microsoft.Win32.RegistryValueKind]::String)}finally{$key.Dispose()}
  Record-Stage 'verify-enabled' 'success' $null
  $result=Status;Json (Join-Path $target 'last-operation.json') $result
  Write-Host ('已启用 '+$chosen[0].display+'：保留完整符号，系统指针基准随本档设为'+$expectedNativeSize+'；停止后恢复原偏好。人物停留自动淡出，移动恢复；识别到的提示区域透明避让。') -ForegroundColor Green
 }elseif($Action-eq'Stop'){
  Record-Stage 'control-access' 'begin' $null;Assert-ControlAccess $exe
  $characters=if(Test-Path -LiteralPath ($runtime+'\characters.json')){Get-Content -LiteralPath ($runtime+'\characters.json') -Raw -Encoding UTF8|ConvertFrom-Json}else{@()}
  $characters=@(Ownership-Catalog $characters)
  $selected=Selected-Character $characters;$stopFailure=$null
  try{Stop-Engine;Record-Stage 'stop-engine' 'success' $null}catch{$stopFailure=$_;Record-Stage 'stop-engine' 'failed' $_.Exception.ToString()}
  # [修改] v1.4.0：停止失败也独立尝试已核实的方案恢复；外部方案改变时不写回。
  if($selected-and(Selected-Character $characters)-eq$selected){if(-not(Test-Path -LiteralPath $stateFile)){throw '程序停止阶段已结束，但恢复记录缺失。'};$savedRestore=Get-Content -LiteralPath $stateFile -Raw -Encoding UTF8|ConvertFrom-Json;Restore $savedRestore.previous $savedRestore.previousNative;Record-Stage 'restore' 'success' $null}
  if($stopFailure){throw $stopFailure}
  $result=Status;if($result.processCount-ne0-or$result.loginStartup){throw '后台清理尚未完成。'}
  if(Test-Path -LiteralPath $target){Json (Join-Path $target 'last-operation.json') $result}
  Write-Host '已关闭，本版后台进程0，登录启动已取消。' -ForegroundColor Green
 }else{$result=Status;$result|ConvertTo-Json -Depth 12}
 exit 0
}catch{
 $failure=$_;$failedPhase=$phase
 # [代码块] full.control.rollback | 范围：第 392-400 行（本次PowerShell AST核验）。
 # [功能/连接] 安装/选择异常 → 分阶段停止、原ANI恢复、方案恢复；最后保留最早故障。
 # [修改] v1.4.0：停止异常不再跳过恢复。每阶段重核所有权，第三方新方案不会被覆盖。
 if($changed-and$previous){
  try{Stop-Engine;$rollbackStages+=@{phase='stop';outcome='success'}}catch{$rollbackStages+=@{phase='stop';outcome='failed';error=$_.Exception.ToString()}}
  $sourcesReady=$true
  try{Restore-OverwrittenNative $overwriteSources;$rollbackStages+=@{phase='restore-sources';outcome='success'}}catch{$sourcesReady=$false;$rollbackStages+=@{phase='restore-sources';outcome='failed';error=$_.Exception.ToString()}}
  try{
   $rollback=(May-RollbackAttempt)-or(-not$selectionWritten-and(Matches-Snapshot $previous))
   if($rollback-and$sourcesReady){Restore $previous $previousNative;$rollbackSelectionRestored=$true;$rollbackStages+=@{phase='restore-selection';outcome='success'}}else{$rollbackStages+=@{phase='restore-selection';outcome='skipped';reason=if(-not$sourcesReady){'original ANI source unavailable'}else{'selection no longer owned'}}}
  }catch{$rollbackStages+=@{phase='restore-selection';outcome='failed';error=$_.Exception.ToString()}}
 }
 Restore-PreviousIntent
 Record-Stage $failedPhase 'failed' @{error=$failure.Exception.ToString();position=$failure.InvocationInfo.PositionMessage;rollback=$rollbackStages}
 Write-Host ('操作失败（'+$failedPhase+'）：'+$failure.Exception.Message) -ForegroundColor Red
 foreach($stage in $rollbackStages){Write-Host ('回退 '+$stage.phase+'：'+$stage.outcome);if($stage.error){Write-Host $stage.error}}
 if(Test-Path -LiteralPath $target){try{Json (Join-Path $target 'last-failure.json') @{utc=[DateTime]::UtcNow.ToString('o');operation=$operationId;phase=$failedPhase;error=$failure.Exception.ToString();position=$failure.InvocationInfo.PositionMessage;rollback=$rollbackStages}}catch{Write-Warning $_.Exception.Message}}
 exit 1
}finally{if($owns){$mutex.ReleaseMutex()};if($mutex){$mutex.Dispose()}}
