# Read-only integration check. Never registers or updates startup.
param([string]$ExePath)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot
if (!$ExePath) { $ExePath=Join-Path $root 'build\staging\Zapret.exe' }
$assembly=[Reflection.Assembly]::LoadFrom([IO.Path]::GetFullPath($ExePath))
$type=$assembly.GetType('ZapretDesktop.AutoStart')
$enabled=$type.GetMethod('Enabled').Invoke($null,[object[]]@([string]$ExePath))
$errorText=$type.GetField('ReadError').GetValue($null)
if ($null -ne $errorText) { throw ('Startup read failed: ' + $errorText) }
Write-Output ('PASS native startup state read: Enabled=' + $enabled + '; ReadError=null; no startup changes.')
