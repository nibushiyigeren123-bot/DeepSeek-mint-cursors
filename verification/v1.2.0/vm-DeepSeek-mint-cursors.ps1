$ErrorActionPreference='Stop'
$root='C:\Lab\Projects\ScopedMatrix'
$payload=$root+'\payload'
Add-Type -AssemblyName System.Drawing,System.Windows.Forms
Add-Type -AssemblyName UIAutomationClient,UIAutomationTypes
Add-Type -Path @(($payload+'\cursor-regression-probe.cs'),($payload+'\ScopedAudit.cs')) -ReferencedAssemblies System.Drawing
[LabCursorProbe]::SetProcessDpiAwarenessContext([IntPtr](-4))|Out-Null
Add-Type -AssemblyName System.IO.Compression.FileSystem
if(-not(Test-Path -LiteralPath ($root+'\candidate\series-ready.flag'))){
 $null=New-Item -ItemType Directory -Path ($root+'\candidate') -Force
 [IO.Compression.ZipFile]::ExtractToDirectory(($payload+'\series-candidates.zip'),($root+'\candidate'))
 [IO.File]::WriteAllText(($root+'\candidate\series-ready.flag'),'ready')
}

$Repo='DeepSeek-mint-cursors'
$roles=@('Arrow','Help','AppStarting','Wait','Crosshair','IBeam','NWPen','No','SizeNS','SizeWE','SizeNWSE','SizeNESW','SizeAll','UpArrow','Hand','Pin','Person')
$ids=@(32512,32651,32650,32514,32515,32513,32631,32648,32645,32644,32642,32643,32646,32516,32649,32671,32672)
$script:observations=@()
$form=[Windows.Forms.Form]::new();$form.Text='AI series size and palette acceptance';$form.ClientSize=[Drawing.Size]::new(720,430);$form.StartPosition='CenterScreen';$form.BackColor=[Drawing.Color]::FromArgb(216,226,219);$form.Cursor=[Windows.Forms.Cursors]::Arrow
$label=[Windows.Forms.Label]::new();$label.Text='Only the currently selected AI cursor family may be adjusted';$label.AutoSize=$true;$label.Left=20;$label.Top=20;$form.Controls.Add($label);$form.Show();[Windows.Forms.Application]::DoEvents()
function Pump([int]$milliseconds){$until=[DateTime]::UtcNow.AddMilliseconds($milliseconds);do{[Windows.Forms.Application]::DoEvents();Start-Sleep -Milliseconds 50}while([DateTime]::UtcNow-lt$until)}
function Read-Base {(Get-ItemProperty -LiteralPath 'HKCU:\Control Panel\Cursors').CursorBaseSize}
function Set-UserSize([int]$value){
 Start-Process 'ms-settings:easeofaccess-mousepointer'
 $until=[DateTime]::UtcNow.AddSeconds(30);$window=$null
 do{
  $all=[Windows.Automation.AutomationElement]::RootElement.FindAll([Windows.Automation.TreeScope]::Children,[Windows.Automation.Condition]::TrueCondition)
  $items=@($all|Where-Object{$_.Current.Name-eq'Settings'-or$_.Current.Name-eq(([char]0x8bbe).ToString()+[char]0x7f6e)})
  if($items.Count-eq1){$window=$items[0];break};Pump 150
 }while([DateTime]::UtcNow-lt$until)
 if(-not$window){throw 'Windows Settings missing for user-size fixture'}
 $until=[DateTime]::UtcNow.AddSeconds(20);$found=@()
 do{
  $sliders=$window.FindAll([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ControlTypeProperty,[Windows.Automation.ControlType]::Slider))
  $found=@();foreach($s in $sliders){$p=$null;if($s.TryGetCurrentPattern([Windows.Automation.RangeValuePattern]::Pattern,[ref]$p)-and$p.Current.Minimum-eq1-and$p.Current.Maximum-eq15){$found+=,@{slider=$s;pattern=$p}}}
  if($found.Count-eq1){break};Pump 200
 }while([DateTime]::UtcNow-lt$until)
 if($found.Count-ne1){throw 'Unique Windows pointer size slider not found'}
 $found[0].pattern.SetValue($value);Pump 1400
 $record=@{stage='Series';requestedSlider=$value;sliderValue=$found[0].pattern.Current.Value;base=(Read-Base);id=$found[0].slider.Current.AutomationId}
 [IO.File]::WriteAllText(($env:LAB_OUTPUT+'\user-slider-'+$value+'.json'),(ConvertTo-Json -InputObject $record),[Text.UTF8Encoding]::new($false))
 try{$window.GetCurrentPattern([Windows.Automation.WindowPattern]::Pattern).Close()}catch{}
 Pump 400
}
function Select-Scheme([string]$name){
 $existing=[ScopedAudit]::MouseDialog();if($existing-ne[IntPtr]::Zero){[ScopedAudit]::Close($existing);Pump 400}
 Start-Process control.exe -ArgumentList 'main.cpl,,1'
 $until=[DateTime]::UtcNow.AddSeconds(20);$dialog=[IntPtr]::Zero
 do {$dialog=[ScopedAudit]::MouseDialog();if($dialog-ne[IntPtr]::Zero){break};Pump 100}while([DateTime]::UtcNow-lt$until)
 if($dialog-eq[IntPtr]::Zero){throw 'Mouse Properties missing'}
 Pump 600
 try{$wanted=[ScopedAudit]::Select($dialog,$name)}catch{[ScopedAudit]::Close($dialog);throw}
 $expectedName=if($name-eq'WINDOWS_DEFAULT'){$wanted -replace ' \([^)]*\)$',''}else{$wanted}
 $until=[DateTime]::UtcNow.AddSeconds(15)
 do {Pump 100;[ScopedAudit]::ConfirmTestSchemeReplace();$scheme=(Get-Item -LiteralPath 'HKCU:\Control Panel\Cursors').GetValue('');$done=$scheme-eq$expectedName -and[ScopedAudit]::ApplyDone($dialog);if($done){break}}while([DateTime]::UtcNow-lt$until)
 [ScopedAudit]::Close($dialog);Pump 200
 if($scheme-ne$expectedName){throw ('Mouse Properties did not apply '+$wanted+'; active='+$scheme)}
}
function Wait-Current([int]$size){
 $until=[DateTime]::UtcNow.AddSeconds(12)
 do {
  $form.Cursor=[Windows.Forms.Cursor]::new([LabCursorProbe]::LoadCursor([IntPtr]::Zero,[IntPtr]32512))
  $point=$form.PointToScreen([Drawing.Point]::new(360,200));[LabCursorProbe]::SetCursorPos($point.X,$point.Y)|Out-Null;[ScopedAudit]::Wake();Pump 150
  $ci=[LabCursorProbe+CI]::new();$ci.size=[Runtime.InteropServices.Marshal]::SizeOf($ci);[LabCursorProbe]::GetCursorInfo([ref]$ci)|Out-Null
  if($ci.cursor-ne[IntPtr]::Zero){$d=[LabCursorProbe]::Dimensions($ci.cursor);if($d[0]-eq$size-and$d[1]-eq$size-and$ci.flags-eq1){return}}
 }while([DateTime]::UtcNow-lt$until)
 throw ('Visible current cursor did not reach '+$size+'px')
}
function Observe([string]$name,[int]$expected,[bool]$allRoles=$false){
 Wait-Current $expected
 if($allRoles){
  $until=[DateTime]::UtcNow.AddSeconds(12)
  do {$pendingRoles=@();for($j=0;$j-lt$ids.Count;$j++){$d=[LabCursorProbe]::Size($ids[$j]);if($d[0]-ne$expected-or$d[1]-ne$expected){$pendingRoles+=($roles[$j]+':'+$d[0])}};if($pendingRoles.Count-eq0){break};Pump 100}while([DateTime]::UtcNow-lt$until)
  if($pendingRoles.Count){throw ('Role set did not settle: '+($pendingRoles -join ', '))}
 }
 if((Read-Base)-ne$script:base){throw ('Global size changed at '+$name)}
 $values=@();for($i=0;$i-lt$ids.Count;$i++){$d=[LabCursorProbe]::Size($ids[$i]);$values+=,[ordered]@{role=$roles[$i];bitmap=$d};if($allRoles-and($d[0]-ne$expected-or$d[1]-ne$expected)){throw ('Role size mismatch '+$roles[$i]+' at '+$name)}}
 $current=[ScopedAudit]::Current(($env:LAB_OUTPUT+'\'+$name+'.png'),0)
 $state=Join-Path $env:LOCALAPPDATA ($profile.namespace+'\'+$profile.installation_slot_version+'\keeper\status.txt')
 $status=$null
 if(Test-Path -LiteralPath $state){for($retry=0;$retry-lt10;$retry++){try{$status=[IO.File]::ReadAllText($state,[Text.Encoding]::UTF8);break}catch{Pump 50}}}
 $run=Get-ItemProperty -LiteralPath 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -ErrorAction SilentlyContinue
 $script:observations+=,[ordered]@{label=$name;globalBase=(Read-Base);expected=$expected;current=$current;roles=$values;scheme=(Get-Item -LiteralPath 'HKCU:\Control Panel\Cursors').GetValue('');keeperStatus=$status;keeperStartup=$run.($profile.keeper_run_name);dpi=[LabCursorProbe]::GetDpiForSystem()}
 [IO.File]::WriteAllText(($env:LAB_OUTPUT+'\observations.json'),(ConvertTo-Json -InputObject $script:observations -Depth 10),[Text.UTF8Encoding]::new($false))
}
function Keeper([string]$action){& ($package+'\keeper.ps1') -Action $action;Pump 600}
function Apply([int]$size){& ($package+'\install.ps1') -Character gemini -Size $size -Apply;Pump 250}
function File-Same([string]$a,[string]$b){if((Get-FileHash -LiteralPath $a).Hash-ne(Get-FileHash -LiteralPath $b).Hash){throw ('Cursor pixels changed unexpectedly: '+$a+' '+$b)}}

function Load-Profile { $script:profile=Get-Content -LiteralPath ($package+'\source\package.json') -Raw -Encoding UTF8|ConvertFrom-Json }
function Run-Cmd([string]$cid,[int]$size,[bool]$plus){
 $name='应用_'+$cid+'_'+$size+'px'+$(if($plus){'_Plus'}else{''})+'.cmd'
 & $env:ComSpec /d /c ('call "'+$package+'\'+$name+'" <NUL')
 if($LASTEXITCODE-ne0){throw ('CMD failed '+$name+' exit='+$LASTEXITCODE)}
 Pump 200
}
try {
 $script:base=Read-Base;if($base-ne32){throw 'Expected clean global size32'}
 $repo=$root+'\candidate\'+$Repo
 $package=$repo;Load-Profile
 Select-Scheme 'WINDOWS_DEFAULT';Observe '00-default-before' $base
 foreach($plus in @($false,$true)){
  $package=if($plus){$repo+'\large-arrow'}else{$repo};Load-Profile
  $edition=if($plus){'plus'}else{'original'}
  foreach($character in $profile.characters){
   $cid=$character.id
   foreach($size in $profile.baseline_sizes){
    Run-Cmd $cid $size $plus
    $physical=[int]$profile.render_sizes.([string]$size)
    $label=$edition+'-'+$cid+'-'+$size
    Observe $label $physical $true
    if($size-eq96){[ScopedAudit]::Current(($env:LAB_OUTPUT+'\'+$label+'-phase2.png'),2)|Out-Null}
    Select-Scheme 'WINDOWS_DEFAULT';Pump 1100
    Observe ($label+'-default') $base
    File-Same ($env:LAB_OUTPUT+'\00-default-before.png') ($env:LAB_OUTPUT+'\'+$label+'-default.png')
   }
  }
 }
 # The same Control Panel entry must still select the right native size with both keepers installed.
 $package=$repo;Load-Profile;$cid=$profile.default_character
 Select-Scheme ($profile.display_names.$cid+$profile.scheme_suffix+'96px')
 Observe '90-original96-via-cpl-both-keepers' 96 $true
 $package=$repo+'\large-arrow';Load-Profile;$cid=$profile.default_character
 Select-Scheme ($profile.display_names.$cid+$profile.scheme_suffix+'96px')
 Observe '91-plus96-via-cpl-both-keepers' 128 $true
 if(-not[ScopedAudit]::SystemParametersInfoW(0x57,0,[IntPtr]::Zero,0)){throw 'Cursor reload failed'}
 Observe '92-plus96-recovered-after-reload' 128 $true
 $package=$repo;Load-Profile;Keeper 'Stop'
 $package=$repo+'\large-arrow';Load-Profile;Observe '93-plus96-other-keeper-stopped' 128 $true
 Select-Scheme 'WINDOWS_DEFAULT';Pump 1400;Keeper 'Uninstall'
 $package=$repo;Load-Profile;Keeper 'Uninstall';Observe '94-default-after-both-uninstalled' $base
 File-Same ($env:LAB_OUTPUT+'\00-default-before.png') ($env:LAB_OUTPUT+'\94-default-after-both-uninstalled.png')
 if($Repo-eq'gemini-cursors'){
  $package=$repo+'\large-arrow';Load-Profile;Run-Cmd 'gemini' 256 $true
  Observe '95-largest-plus-before-boot' 352 $true
 }
 [IO.File]::WriteAllText(($env:LAB_OUTPUT+'\acceptance.json'),(ConvertTo-Json -InputObject @{success=$true;repository=$Repo;observations=$script:observations.Count;userBase=$base;actualCmdEntryForEverySize=$true;hostSettingsChanged=$false} -Depth 4),[Text.UTF8Encoding]::new($false))
}finally{$form.Close();$form.Dispose()}
