[CmdletBinding()]
param([ValidateSet('deepseek')][string]$Character='deepseek',[ValidateSet(32,48,64,96,128,256)][int]$Size=64)
$ErrorActionPreference='Stop'
& (Join-Path $PSScriptRoot 'keeper.ps1') -Action Stop
& (Join-Path $PSScriptRoot 'original\apply-updated.ps1') -Character $Character -Size $Size
