param()
$ErrorActionPreference='Stop'
$compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$argsList=@('/nologo','/codepage:65001','/optimize+','/target:winexe','/platform:anycpu','/main:Program','/reference:System.Drawing.dll','/reference:System.Windows.Forms.dll','/reference:System.Web.Extensions.dll',('/out:'+(Join-Path $PSScriptRoot 'runtime\AICursorFamilyR5.exe')))
$argsList+=@(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'source') -Filter '*.cs'|ForEach-Object{$_.FullName})
& $compiler $argsList
if($LASTEXITCODE-ne0){throw 'Runtime compilation failed'}
$old=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'payload-hashes.json') -Raw -Encoding UTF8|ConvertFrom-Json
$map=[ordered]@{}
foreach($f in $old.PSObject.Properties){$map[$f.Name]=(Get-FileHash -LiteralPath (Join-Path $PSScriptRoot $f.Name) -Algorithm SHA256).Hash.ToLowerInvariant()}
[IO.File]::WriteAllText((Join-Path $PSScriptRoot 'payload-hashes.json'),($map|ConvertTo-Json -Depth 4),[Text.UTF8Encoding]::new($false))
Write-Host 'Compilation finished; package hashes updated. No cursor was installed or enabled.'
