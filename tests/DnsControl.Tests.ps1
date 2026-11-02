# Integration tests with mocked Windows networking commands. No system changes.
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$testPath = Join-Path $root ('build\dns-tests-' + [Guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path $testPath
$source = [IO.File]::ReadAllText((Join-Path $root 'src\DnsControl.ps1'))
$parseErrors=$null; $tokens=$null
$null=[Management.Automation.Language.Parser]::ParseInput($source,[ref]$tokens,[ref]$parseErrors)
if ($parseErrors.Count) { throw ($parseErrors | Out-String) }
$code = [scriptblock]::Create($source.Replace('[Console]::Error.WriteLine($_.Exception.Message); exit 1','throw'))
$adapterId='11111111-1111-1111-1111-111111111111'
$adapter=[pscustomobject]@{InterfaceGuid=[guid]$adapterId;Name="Wi-Fi user's";ifIndex=8;Status='Up'}
$provider=[pscustomobject]@{Id='test';Template='https://dns.example/dns-query';V4=@('1.1.1.1','1.0.0.1');V6=@('2606:4700:4700::1111','2606:4700:4700::1001')}
function Assert($value,$name) { if (!$value) { throw ('FAIL: ' + $name) } }
function Get-NetAdapter { param([switch]$IncludeHidden) $adapter }
function Get-NetAdapterBinding { param($Name,$ComponentID) [pscustomobject]@{Enabled=($ComponentID -eq 'ms_tcpip' -or !$global:DisableV6)} }
function Get-DnsClientServerAddress {
    param($InterfaceIndex,$AddressFamily)
    foreach ($family in @('IPv4','IPv6')) { if (!$AddressFamily -or $AddressFamily -eq $family) { [pscustomobject]@{AF=$family;ServerAddresses=$global:Servers[$family]} } }
}
function Set-DnsClientServerAddress {
    param($InputObject,[string[]]$ServerAddresses,[switch]$ResetServerAddresses)
    $global:Writes++
    if ($global:FailV6 -and $InputObject.AF -eq 'IPv6' -and !$ResetServerAddresses) { throw 'Simulated IPv6 write failure' }
    if ($ResetServerAddresses) { $global:Servers[$InputObject.AF]=@('fe80::1'); $global:Reset++ }
    else { $global:Servers[$InputObject.AF]=$ServerAddresses }
}
function Get-ItemProperty {
    param($LiteralPath,$Name,$ErrorAction)
    if ($Name -eq 'DoHPolicy') { [pscustomobject]@{DoHPolicy=$global:Policy}; return }
    [pscustomobject]@{NameServer=$(if ($LiteralPath -like '*Tcpip6*') { '' } else { '192.168.1.2' })}
}
function Get-DnsClientDohServerAddress { if ($global:Unavailable) { throw 'DoH unavailable' }; $global:Entries.Values }
function Set-DnsClientDohServerAddress { param($ServerAddress,$DohTemplate,$AutoUpgrade,$AllowFallbackToUdp) $global:Entries[$ServerAddress]=[pscustomobject]@{ServerAddress=$ServerAddress;DohTemplate=$DohTemplate;AutoUpgrade=$AutoUpgrade;AllowFallbackToUdp=$AllowFallbackToUdp} }
function Add-DnsClientDohServerAddress { param($ServerAddress,$DohTemplate,$AutoUpgrade,$AllowFallbackToUdp) Set-DnsClientDohServerAddress @PSBoundParameters }
function Remove-DnsClientDohServerAddress { param($ServerAddress,$ErrorAction) $global:Entries.Remove($ServerAddress) }
function Clear-DnsClientCache { }
function Reset-Fixture {
    $global:Servers=@{IPv4=@('192.168.1.2');IPv6=@('fe80::1')}
    $global:Entries=@{'1.1.1.1'=[pscustomobject]@{ServerAddress='1.1.1.1';DohTemplate='https://old.example/query';AutoUpgrade=$false;AllowFallbackToUdp=$true}}
    $global:Writes=0; $global:Reset=0; $global:FailV6=$false; $global:DisableV6=$false; $global:Policy=0; $global:Unavailable=$false
}
Reset-Fixture
$action='Read'; $read=(& $code | ConvertFrom-Json)
Assert ($read.Doh -and $read.Adapters.Count -eq 1 -and $read.Adapters[0].Guid -eq $adapterId -and $global:Writes -eq 0) 'read-only discovery and JSON arrays'
$backup=Join-Path $testPath 'success.xml'; $action='Apply'; $null=& $code
Assert (Test-Path $backup) 'backup before changes'
Assert (($global:Servers.IPv4 -join ',') -eq '1.1.1.1,1.0.0.1') 'IPv4 applied'
Assert (($global:Servers.IPv6 -join ',') -eq ($provider.V6 -join ',')) 'IPv6 applied'
Assert ($global:Entries.Count -eq 4 -and $global:Entries['1.1.1.1'].AutoUpgrade -and !$global:Entries['1.1.1.1'].AllowFallbackToUdp) 'encrypted only'
$action='Restore'; $null=& $code
Assert (($global:Servers.IPv4 -join ',') -eq '192.168.1.2' -and $global:Reset -eq 1) 'restore static IPv4 and automatic IPv6'
Assert ($global:Entries.Count -eq 1 -and $global:Entries['1.1.1.1'].DohTemplate -eq 'https://old.example/query' -and $global:Entries['1.1.1.1'].AllowFallbackToUdp) 'restore DoH entries exactly'
Reset-Fixture; $global:FailV6=$true; $backup=Join-Path $testPath 'failure.xml'; $action='Apply'
$failed=$false; try { $null=& $code } catch { $failed=$true }
Assert $failed 'write failure reported'
Assert (($global:Servers.IPv4 -join ',') -eq '192.168.1.2' -and $global:Entries.Count -eq 1) 'rollback after partial write'
Reset-Fixture; $global:DisableV6=$true; $backup=Join-Path $testPath 'v4-only.xml'; $action='Apply'; $null=& $code
Assert (($global:Servers.IPv6 -join ',') -eq 'fe80::1' -and $global:Entries.Count -eq 2) 'disabled IPv6 untouched'
Reset-Fixture; $global:Policy=1; $backup=Join-Path $testPath 'policy.xml'; $failed=$false
try { $null=& $code } catch { $failed=$true }
Assert ($failed -and $global:Writes -eq 0 -and !(Test-Path $backup)) 'policy refusal before mutation'
Reset-Fixture; $global:Unavailable=$true; $action='Read'; $read=(& $code | ConvertFrom-Json)
Assert (!$read.Doh -and $global:Writes -eq 0) 'unsupported system detected without mutation'
$action='Apply'; $failed=$false
try { $null=& $code } catch { $failed=$true }
Assert ($failed -and $global:Writes -eq 0) 'unsupported system never falls back to plaintext'
Reset-Fixture; $adapterId='22222222-2222-2222-2222-222222222222'; $backup=Join-Path $testPath 'wrong-adapter.xml'; $failed=$false
try { $null=& $code } catch { $failed=$true }
Assert ($failed -and $global:Writes -eq 0) 'adapter identity checked'
Write-Output 'PASS DNS script parsing, apply IPv4/IPv6, exact restore, DHCP restore, partial failure rollback, disabled protocol, policy and adapter guards (mocked networking only).'
