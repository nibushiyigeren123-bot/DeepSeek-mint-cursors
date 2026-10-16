[CmdletBinding()]
param([ValidateSet('all','deepseek')][string]$Character='deepseek',[ValidateSet(32,48,64,96,128,256)][int]$Size=64)
$ErrorActionPreference='Stop'
& (Join-Path $PSScriptRoot 'install.ps1') -Character $Character -Size $Size -Apply
& (Join-Path $PSScriptRoot 'keeper.ps1') -Action Install
