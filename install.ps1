[CmdletBinding()]
param(
 [ValidateSet('all','deepseek')][string]$Character='all',
 [ValidateSet(32,48,64,128,256)][int]$Size=64,
 [switch]$Apply,[switch]$Restore,[switch]$VerifyOnly,[switch]$RegisterOnly
)
$ErrorActionPreference='Stop'
$packageRoot=$PSScriptRoot
$manifest=Get-Content -LiteralPath (Join-Path $packageRoot 'source\cursor_manifest.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$roles=@('Arrow','Help','AppStarting','Wait','Crosshair','IBeam','NWPen','No','SizeNS','SizeWE','SizeNWSE','SizeNESW','SizeAll','UpArrow','Hand','Pin','Person')
$characters=@($manifest | Select-Object -ExpandProperty character -Unique)
foreach($entry in $manifest){
 $file=Join-Path $packageRoot $entry.file
 if(-not(Test-Path -LiteralPath $file -PathType Leaf)){throw "Missing cursor: $file"}
}
if(([int][bool]$Restore+[int][bool]$VerifyOnly+[int][bool]$RegisterOnly) -gt 1 -or ($Apply -and ($Restore -or $VerifyOnly -or $RegisterOnly))){throw 'Choose installation, RegisterOnly, Restore, or VerifyOnly separately.'}
if($Character -ne 'all' -and $Character -notin $characters){throw "This package does not contain $Character."}
if($VerifyOnly){Write-Output "Verified $($manifest.Count) cursor files. No installation changes.";return}
# The ordinary right-click / no-argument entry point installs and applies 64px.
# RegisterOnly is the explicit opt-in for installing schemes without changing cursors.
if(-not $Restore -and -not $RegisterOnly){
 $Apply=$true
 if($Character -eq 'all'){
  $Character=$characters[0]
  $selectionKey=[Microsoft.Win32.Registry]::CurrentUser.OpenSubKey('Control Panel\Cursors')
  if($selectionKey){
   try{
    $previousScheme=[string]$selectionKey.GetValue('')
    foreach($candidate in $characters){
     if($previousScheme -match ('^'+[regex]::Escape($candidate)+' Mint (32|48|64|128|256)px$')){$Character=$candidate;break}
    }
   }finally{$selectionKey.Close()}
  }
 }
}
$destination=Join-Path $env:LOCALAPPDATA 'DeepSeekMintCursors\1.0.0'
$cursorKey='HKCU:\Control Panel\Cursors'
if(-not ('DeepSeekCursorNative' -as [type])){
 Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class DeepSeekCursorNative {
 [StructLayout(LayoutKind.Sequential)] private struct IconInfo {
  public int isIcon; public uint x,y; public IntPtr mask,color;
 }
 [StructLayout(LayoutKind.Sequential)] private struct BitmapInfo {
  public int type,width,height,widthBytes; public ushort planes,bits; public IntPtr pixels;
 }
 [DllImport("user32.dll", SetLastError=true)]
 public static extern bool SystemParametersInfo(uint action,uint parameter,IntPtr value,uint flags);
 [DllImport("user32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
 private static extern IntPtr LoadImageW(IntPtr module,string path,uint type,int width,int height,uint flags);
 [DllImport("user32.dll", SetLastError=true)]
 private static extern bool SetSystemCursor(IntPtr cursor,uint id);
 [DllImport("user32.dll")]
 private static extern bool DestroyCursor(IntPtr cursor);
 [DllImport("user32.dll", EntryPoint="LoadCursorW", SetLastError=true)]
 private static extern IntPtr CurrentSystemCursor(IntPtr module,IntPtr id);
 [DllImport("user32.dll", SetLastError=true)]
 private static extern bool GetIconInfo(IntPtr cursor,out IconInfo info);
 [DllImport("gdi32.dll", EntryPoint="GetObjectW")]
 private static extern int GetBitmapInfo(IntPtr bitmap,int bytes,out BitmapInfo info);
 [DllImport("gdi32.dll")]
 private static extern bool DeleteObject(IntPtr bitmap);
 public static bool HasExactSize(uint id,int size) {
  IntPtr cursor=CurrentSystemCursor(IntPtr.Zero,new IntPtr((long)id));IconInfo icon;
  if(cursor==IntPtr.Zero || !GetIconInfo(cursor,out icon)) return false;
  try {
   BitmapInfo bitmap;
   if(GetBitmapInfo(icon.color!=IntPtr.Zero?icon.color:icon.mask,Marshal.SizeOf(typeof(BitmapInfo)),out bitmap)==0) return false;
   int height=icon.color!=IntPtr.Zero?bitmap.height:bitmap.height/2;
   return bitmap.width==size && height==size;
  } finally {
   if(icon.mask!=IntPtr.Zero) DeleteObject(icon.mask);
   if(icon.color!=IntPtr.Zero) DeleteObject(icon.color);
  }
 }
 public static bool ApplyExactSize(string path,uint id,int size) {
  IntPtr cursor=LoadImageW(IntPtr.Zero,path,2,size,size,16);
  if(cursor==IntPtr.Zero) return false;
  // SetSystemCursor consumes a nonshared handle on success.
  if(SetSystemCursor(cursor,id)) return true;
  DestroyCursor(cursor);return false;
 }
}
'@
}
function Restore-MintSavedValues($values){
 $reg=[Microsoft.Win32.Registry]::CurrentUser.OpenSubKey('Control Panel\Cursors',$true)
 try{
  foreach($entry in $values){
   if($entry.exists){$kind=[System.Enum]::Parse([Microsoft.Win32.RegistryValueKind],$entry.kind);$reg.SetValue($entry.name,$entry.value,$kind)}
   else{$reg.DeleteValue($entry.name,$false)}
  }
 }finally{$reg.Close()}
}
if($Restore){
 $backups=Get-ChildItem -LiteralPath (Join-Path $destination 'backup') -Filter '*.json' | Sort-Object Name -Descending
 if(-not $backups){throw 'No saved cursor settings found.'}
 $saved=Get-Content -LiteralPath $backups[0].FullName -Raw -Encoding UTF8 | ConvertFrom-Json
 & (Join-Path $packageRoot 'keeper.ps1') -Action Stop
 Restore-MintSavedValues $saved
 if(-not[DeepSeekCursorNative]::SystemParametersInfo(0x57,0,[IntPtr]::Zero,0)){throw 'Windows cursor refresh failed.'}
 Write-Output "Restored settings saved in $($backups[0].Name)";return
}
[void](New-Item -ItemType Directory -Path $destination -Force)
foreach($entry in $manifest){
 $target=Join-Path $destination $entry.file
 [void](New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force)
 Copy-Item -LiteralPath (Join-Path $packageRoot $entry.file) -Destination $target -Force
 if((Get-FileHash -LiteralPath $target).Hash -ne (Get-FileHash -LiteralPath (Join-Path $packageRoot $entry.file)).Hash){throw "Copy mismatch: $target"}
}
$schemeKey=Join-Path $cursorKey 'Schemes'
if(-not(Test-Path -LiteralPath $schemeKey)){[void](New-Item -Path $schemeKey)}
foreach($c in $characters){foreach($pixels in @(32,48,64,128,256)){
 $entries=@($manifest | Where-Object { $_.character -eq $c -and $_.size -eq $pixels })
 if($entries.Count -ne 17){throw "Incomplete scheme: $c $pixels"}
 $paths=foreach($role in $roles){$e=$entries | Where-Object role -eq $role;Join-Path $destination $e.file}
 $display='DeepSeek'
 [void](New-ItemProperty -Path $schemeKey -Name "$display Mint $($pixels)px" -Value ($paths -join ',') -PropertyType String -Force)
}}
if($Apply){
 try{
 $reg=[Microsoft.Win32.Registry]::CurrentUser.OpenSubKey('Control Panel\Cursors',$true)
 try{
  $saved=foreach($name in @('')+$roles+@('Scheme Source','CursorBaseSize')){
   $exists=$name -in $reg.GetValueNames()
   [pscustomobject]@{name=$name;exists=$exists;value=if($exists){$reg.GetValue($name,$null,[Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)}else{$null};kind=if($exists){$reg.GetValueKind($name).ToString()}else{$null}}
  }
  $backupDir=Join-Path $destination 'backup';[void](New-Item -ItemType Directory -Path $backupDir -Force)
  $backup=Join-Path $backupDir ((Get-Date -Format 'yyyyMMdd-HHmmss-ffff')+'.json')
  [IO.File]::WriteAllText($backup,($saved | ConvertTo-Json -Depth 5),[Text.UTF8Encoding]::new($false))
  foreach($role in $roles){
   $e=$manifest | Where-Object { $_.character -eq $Character -and $_.size -eq $Size -and $_.role -eq $role }
   $reg.SetValue($role,(Join-Path $destination $e.file),[Microsoft.Win32.RegistryValueKind]::String)
  }
  $display='DeepSeek'
  $reg.SetValue('',"$display Mint $($Size)px",[Microsoft.Win32.RegistryValueKind]::String)
  $reg.SetValue('Scheme Source',1,[Microsoft.Win32.RegistryValueKind]::DWord)
  $reg.SetValue('CursorBaseSize',$Size,[Microsoft.Win32.RegistryValueKind]::DWord)
 }finally{$reg.Close()}
 if(-not[DeepSeekCursorNative]::SystemParametersInfo(0x57,0,[IntPtr]::Zero,0)){throw "Windows refresh failed. Backup: $backup"}
 $systemIds=@{Arrow=32512;Help=32651;AppStarting=32650;Wait=32514;Crosshair=32515;IBeam=32513;NWPen=32631;No=32648;SizeNS=32645;SizeWE=32644;SizeNWSE=32642;SizeNESW=32643;SizeAll=32646;UpArrow=32516;Hand=32649;Pin=32671;Person=32672}
 foreach($role in $roles){
  $entry=$manifest | Where-Object { $_.character -eq $Character -and $_.size -eq $Size -and $_.role -eq $role }
  if(-not[DeepSeekCursorNative]::ApplyExactSize((Join-Path $destination $entry.file),$systemIds[$role],$Size)){
   throw "Could not apply exact cursor size for $role. Backup: $backup"
  }
  if(-not[DeepSeekCursorNative]::HasExactSize($systemIds[$role],$Size)){
   Write-Warning "Cursor bitmap size could not confirm $Size px for $role. Installation kept; actual display size depends on Windows. Backup: $backup"
  }
 }
 }catch{
  $applyError=$_
  if($saved){
   try{
    Restore-MintSavedValues $saved
    if(-not[DeepSeekCursorNative]::SystemParametersInfo(0x57,0,[IntPtr]::Zero,0)){throw 'Rollback cursor refresh failed.'}
   }catch{throw "Apply failed: $($applyError.Exception.Message) Rollback failed: $($_.Exception.Message) Use -Restore. Backup: $backup"}
  }
  throw "Apply failed; previous cursor settings and size restored. $($applyError.Exception.Message)"
 }
 Write-Output "Applied $Character $Size px. All 17 cursor replacements succeeded; requested pixel size is not a display-size guarantee. Previous settings: $backup"
}else{Write-Output 'Registered cursor schemes only (-RegisterOnly). Run install.ps1 without arguments to apply the default 64px size.'}
