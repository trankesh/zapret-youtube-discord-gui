# TASK_VALIDATE_ONLY (1) checks XML syntax without registering or running a task.
param([string]$ExePath)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot
if (!$ExePath) { $ExePath=Join-Path $root 'build\staging\Zapret.exe' }
$assembly=[Reflection.Assembly]::LoadFrom([IO.Path]::GetFullPath($ExePath))
$type=$assembly.GetType('ZapretDesktop.AutoStart')
$flags=[Reflection.BindingFlags]'NonPublic,Static'
$sid=[Security.Principal.WindowsIdentity]::GetCurrent().User.Value
$directory=[string]$type.GetProperty('ProtectedDirectory',$flags).GetValue($null,$null)
$xml=$type.GetMethod('TaskXml',$flags).Invoke($null,[object[]]@([string]$ExePath,[string](Join-Path $root 'build\schema-data'),[string](Join-Path $directory 'schema-validation.exe'),[string]$sid))
$service=New-Object -ComObject Schedule.Service
$service.Connect()
$folder=$service.GetFolder('\')
$null=$folder.RegisterTask(('ZapretDesktop-SchemaValidation-' + $sid),$xml,1,$sid,$null,3,$null)
Write-Output 'PASS native Task Scheduler XML validation (TASK_VALIDATE_ONLY; no task registered).'
