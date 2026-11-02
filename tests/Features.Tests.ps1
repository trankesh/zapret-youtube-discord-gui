param([string]$ExePath)
$ErrorActionPreference = 'Stop'
$root=Split-Path $PSScriptRoot
$path=Join-Path $root ('build\feature-tests-' + [Guid]::NewGuid().ToString('N'))
$null=New-Item -ItemType Directory -Path $path
if (!$ExePath) { $ExePath=Join-Path $root 'build\staging\Zapret.exe' }
$assembly=[Reflection.Assembly]::LoadFrom([IO.Path]::GetFullPath($ExePath))
try { $assembly.GetType('ZapretDesktop.FeatureTests').GetMethod('Run',[Reflection.BindingFlags]'Public,Static').Invoke($null,[object[]]@([string]$path)); Write-Output 'PASS DNS hosts merge, opt-out, catalog, script quoting and command size; installed profile import and task isolation.' }
catch { Write-Error $_.Exception.ToString(); exit 1 }
