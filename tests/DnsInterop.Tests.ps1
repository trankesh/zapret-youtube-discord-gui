# Real PowerShell/JavaScriptSerializer/native marshaling boundary. No network writes.
param([Parameter(Mandatory=$true)][string]$ExePath)
$ErrorActionPreference='Stop'
$assembly=[Reflection.Assembly]::LoadFrom([IO.Path]::GetFullPath($ExePath))
$reader=[IO.StreamReader]::new($assembly.GetManifestResourceStream('DnsControl.ps1'))
try { $source=$reader.ReadToEnd() } finally { $reader.Dispose() }
$start=$source.IndexOf('function New-InterfaceDoh'); $end=$source.IndexOf('function Test-InterfaceDoh')
. ([scriptblock]::Create($source.Substring($start,$end-$start)))
$native=$assembly.GetType('ZapretDesktop.InterfaceDns')
function Validate($state,$v6=$false) {
    $json=$state | ConvertTo-Json -Depth 8 -Compress
    $null=$native.GetMethod('Validate').Invoke($null,[object[]]@([bool]$v6,[string]$json))
    return $json
}
foreach($servers in @(@('8.8.8.8'),@('8.8.8.8','8.8.4.4'))) {
    $state=New-InterfaceDoh $servers ([pscustomobject]@{ProfileNameServer=$null})
    $json=Validate $state
    if($json -notmatch '"ProfileServerProperties":\[\]') { throw 'Empty profile properties must be a JSON array.' }
    $state=New-InterfaceDoh $servers ([pscustomobject]@{ProfileNameServer='192.0.2.1'})
    $null=Validate $state
}
$state=New-InterfaceDoh @('2001:4860:4860::8888','2001:4860:4860::8844') ([pscustomobject]@{ProfileNameServer=$null})
$null=Validate $state $true
$json='{"NameServer":"8.8.8.8,8.8.4.4","ProfileNameServer":null,"ServerProperties":[{"Index":0,"Flags":17,"Template":"https://dns.google/dns-query"},{"Index":1,"Flags":17,"Template":"https://dns.google/dns-query"}],"ProfileServerProperties":[]}'
$serializer=New-Object Web.Script.Serialization.JavaScriptSerializer
$snapshot=$serializer.Deserialize($json,$assembly.GetType('ZapretDesktop.InterfaceDnsState'))
$null=Validate $snapshot
$dir=Join-Path (Split-Path $PSScriptRoot) ('build\dns-interop-'+[guid]::NewGuid().ToString('N'))
$null=New-Item -ItemType Directory -Path $dir
$file=Join-Path $dir 'snapshot.xml'; $snapshot | Export-Clixml -LiteralPath $file -Depth 8
$null=Validate (Import-Clixml -LiteralPath $file)
Write-Output 'PASS real DNS JSON arrays (empty/single/multiple), IPv6, expanded automatic/DDR templates and CLIXML rollback snapshot; no DNS writes.'
