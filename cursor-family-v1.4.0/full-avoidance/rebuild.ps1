param()
$ErrorActionPreference='Stop'
$compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$argsList=@('/nologo','/codepage:65001','/optimize+','/target:winexe','/platform:anycpu','/main:Program','/reference:System.Drawing.dll','/reference:System.Windows.Forms.dll','/reference:System.Web.Extensions.dll',('/out:'+(Join-Path $PSScriptRoot 'runtime\AICursorFamilyFullAvoidance.exe')))
foreach($assembly in @('UIAutomationClient.dll','UIAutomationTypes.dll','WindowsBase.dll')){$argsList+=('/reference:'+(Join-Path $env:WINDIR ('Microsoft.NET/Framework64/v4.0.30319/WPF/'+$assembly)))}
$argsList+=@(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'source') -Filter '*.cs'|ForEach-Object{$_.FullName})
& $compiler $argsList
if($LASTEXITCODE-ne0){throw 'Runtime compilation failed'}
# [功能/连接] 编译产物 → 候选元数据；避免后续重建仍携带旧EXE摘要。
$metadataPath=Join-Path $PSScriptRoot 'candidate-consistency.json'
if(Test-Path -LiteralPath $metadataPath){$metadata=Get-Content -LiteralPath $metadataPath -Raw -Encoding UTF8|ConvertFrom-Json;$metadata|Add-Member -NotePropertyName completeVmAcceptance -NotePropertyValue $false -Force;$metadata|Add-Member -NotePropertyName technicalVmAcceptance -NotePropertyValue $false -Force;$metadata|Add-Member -NotePropertyName stage -NotePropertyValue 'local-rebuilt-unvalidated' -Force;$metadata|Add-Member -NotePropertyName coreExecutableSha256 -NotePropertyValue ((Get-FileHash -LiteralPath (Join-Path $PSScriptRoot 'runtime/AICursorFamilyFullAvoidance.exe')).Hash.ToLowerInvariant()) -Force;[IO.File]::WriteAllText($metadataPath,($metadata|ConvertTo-Json -Depth 12),[Text.UTF8Encoding]::new($false))}
# [功能/连接] 本地重建后的来源记录明确撤销原VM/资源校验，不把旧通过状态继承给新二进制。
$proofPath=Join-Path $PSScriptRoot 'v140-package-provenance.json'
if(Test-Path -LiteralPath $proofPath){
 $proof=Get-Content -LiteralPath $proofPath -Raw -Encoding UTF8|ConvertFrom-Json
 $proof|Add-Member -NotePropertyName runtimeSha256 -NotePropertyValue ((Get-FileHash -LiteralPath (Join-Path $PSScriptRoot 'runtime/AICursorFamilyFullAvoidance.exe')).Hash.ToLowerInvariant()) -Force
 $proof|Add-Member -NotePropertyName completeVmAcceptance -NotePropertyValue $false -Force;$proof|Add-Member -NotePropertyName technicalVmAcceptance -NotePropertyValue $false -Force
 $proof|Add-Member -NotePropertyName stage -NotePropertyValue 'local-rebuilt-unvalidated' -Force
 $proof|Add-Member -NotePropertyName mediaPreservation -NotePropertyValue 'not revalidated after local rebuild' -Force
 $proof|Add-Member -NotePropertyName originalCmdBytesPreserved -NotePropertyValue $null -Force
 [IO.File]::WriteAllText($proofPath,($proof|ConvertTo-Json -Depth 14),[Text.UTF8Encoding]::new($false))
}
$old=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'payload-hashes.json') -Raw -Encoding UTF8|ConvertFrom-Json
$map=[ordered]@{}
foreach($f in $old.PSObject.Properties){$map[$f.Name]=(Get-FileHash -LiteralPath (Join-Path $PSScriptRoot $f.Name) -Algorithm SHA256).Hash.ToLowerInvariant()}
[IO.File]::WriteAllText((Join-Path $PSScriptRoot 'payload-hashes.json'),($map|ConvertTo-Json -Depth 4),[Text.UTF8Encoding]::new($false))
Write-Host 'Compilation finished; package hashes updated. No cursor was installed or enabled.'
