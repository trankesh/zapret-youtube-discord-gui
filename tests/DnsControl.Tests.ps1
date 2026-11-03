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
function Get-ZapretInterfaceDns {
    param($id,$af)
    if($global:NativeUnavailable) { throw 'Native DoH unavailable' }
    $state=$global:Native[$af]
    if($global:ExpandAutomatic) {
        $state=$state | ConvertTo-Json -Depth 8 -Compress | ConvertFrom-Json
        foreach($p in @($state.ServerProperties)+@($state.ProfileServerProperties)) {
            if($p -and ([uint64]$p.Flags -band 1)) { $p.Template=$provider.Template; $p.Flags=[uint64]$p.Flags -bor 16 }
        }
    }
    return $state
}
function Test-ZapretInterfaceDns { param($af,$state) if($global:InvalidNative) { throw 'Invalid native state' } }
function Set-ZapretInterfaceDns {
    param($id,$af,$state)
    if($global:IgnoreNativeOnce) { $global:IgnoreNativeOnce=$false; return }
    $global:Native[$af]=($state | ConvertTo-Json -Depth 8 -Compress | ConvertFrom-Json)
    foreach($p in @($global:Native[$af].ServerProperties)+@($global:Native[$af].ProfileServerProperties)) { if($p -and ([uint64]$p.Flags -band 1)) { $p.Template=$null } }
    if($global:IgnoreProfileOnce) { $global:IgnoreProfileOnce=$false; $global:Native[$af].ProfileServerProperties=@() }
}
function Reset-Fixture {
    $global:Servers=@{IPv4=@('192.168.1.2');IPv6=@('fe80::1')}
    $global:Entries=@{'1.1.1.1'=[pscustomobject]@{ServerAddress='1.1.1.1';DohTemplate='https://old.example/query';AutoUpgrade=$false;AllowFallbackToUdp=$true}}
    $global:Writes=0; $global:Reset=0; $global:FailV6=$false; $global:DisableV6=$false; $global:Policy=0; $global:Unavailable=$false
    $global:Native=@{
        IPv4=[pscustomobject]@{NameServer='192.168.1.2';ProfileNameServer=$null;ServerProperties=@([pscustomobject]@{Index=0;Flags=[uint64]6;Template='https://previous.example/query'});ProfileServerProperties=@()}
        IPv6=[pscustomobject]@{NameServer=$null;ProfileNameServer=$null;ServerProperties=@();ProfileServerProperties=@()}
    }
    $global:IgnoreNativeOnce=$false; $global:IgnoreProfileOnce=$false; $global:NativeUnavailable=$false; $global:InvalidNative=$false; $global:ExpandAutomatic=$false
}
Reset-Fixture
$action='Read'; $read=(& $code | ConvertFrom-Json)
Assert ($read.Doh -and $read.Adapters.Count -eq 1 -and $read.Adapters[0].Guid -eq $adapterId -and $global:Writes -eq 0) 'read-only discovery and JSON arrays'
$backup=Join-Path $testPath 'success.xml'; $action='Apply'; $null=& $code
Assert (Test-Path $backup) 'backup before changes'
Assert (($global:Servers.IPv4 -join ',') -eq '1.1.1.1,1.0.0.1') 'IPv4 applied'
Assert (($global:Servers.IPv6 -join ',') -eq ($provider.V6 -join ',')) 'IPv6 applied'
Assert ($global:Entries.Count -eq 4 -and $global:Entries['1.1.1.1'].AutoUpgrade -and !$global:Entries['1.1.1.1'].AllowFallbackToUdp) 'encrypted only'
Assert ($global:Native.IPv4.ServerProperties.Count -eq 2 -and $global:Native.IPv4.ServerProperties[0].Flags -eq 1 -and $null -eq $global:Native.IPv4.ServerProperties[0].Template -and $global:Native.IPv6.ServerProperties[1].Flags -eq 1 -and !$global:Native.IPv4.ProfileNameServer) 'automatic DoH for both families without creating an unused profile override'
$action='Read'; $read=(& $code | ConvertFrom-Json)
Assert ($read.Adapters[0].DohEnabled -and $read.Adapters[0].DohSupported) 'read verifies interface encryption instead of global catalog alone'
$action='Restore'; $null=& $code
Assert (($global:Servers.IPv4 -join ',') -eq '192.168.1.2' -and $global:Reset -eq 1) 'restore static IPv4 and automatic IPv6'
Assert ($global:Entries.Count -eq 1 -and $global:Entries['1.1.1.1'].DohTemplate -eq 'https://old.example/query' -and $global:Entries['1.1.1.1'].AllowFallbackToUdp) 'restore DoH entries exactly'
Assert ($global:Native.IPv4.ServerProperties[0].Flags -eq 6 -and $global:Native.IPv4.ServerProperties[0].Template -eq 'https://previous.example/query' -and $global:Native.IPv6.ServerProperties.Count -eq 0) 'restore per-interface modes, template, and original profile'
Reset-Fixture; $global:IgnoreNativeOnce=$true; $backup=Join-Path $testPath 'no-interface-doh.xml'; $action='Apply'; $failed=$false
try { $null=& $code } catch { $failed=$true }
Assert ($failed -and ($global:Servers.IPv4 -join ',') -eq '192.168.1.2' -and $global:Entries.Count -eq 1 -and $global:Native.IPv4.ServerProperties[0].Flags -eq 6) 'IP-only success is rejected and rolled back'
Reset-Fixture; $global:Native.IPv4.ProfileNameServer='192.168.1.2'; $global:Native.IPv4.ProfileServerProperties=$global:Native.IPv4.ServerProperties; $global:IgnoreProfileOnce=$true; $backup=Join-Path $testPath 'no-profile-doh.xml'; $action='Apply'; $failed=$false
try { $null=& $code } catch { $failed=$true }
Assert ($failed -and ($global:Servers.IPv4 -join ',') -eq '192.168.1.2' -and $global:Native.IPv4.ServerProperties[0].Flags -eq 6) 'unencrypted profile override cannot pass verification'
Reset-Fixture; $global:Native.IPv4.ProfileNameServer='192.168.1.2'; $global:Native.IPv4.ProfileServerProperties=$global:Native.IPv4.ServerProperties; $backup=Join-Path $testPath 'profile-doh.xml'; $action='Apply'; $null=& $code
Assert ($global:Native.IPv4.ProfileServerProperties.Count -eq 2 -and $global:Native.IPv4.ProfileServerProperties[1].Flags -eq 1) 'existing profile override receives automatic encryption'
$action='Restore'; $null=& $code
Assert ($global:Native.IPv4.ProfileNameServer -eq '192.168.1.2' -and $global:Native.IPv4.ProfileServerProperties[0].Flags -eq 6) 'existing profile override restored exactly'
Reset-Fixture; $global:Native.IPv4.ServerProperties[0].Flags=[uint64]17; $backup=Join-Path $testPath 'expanded-auto.xml'; $action='Apply'; $null=& $code; $action='Restore'; $null=& $code
Assert ($global:Native.IPv4.ServerProperties[0].Flags -eq 17) 'expanded automatic template does not prevent rollback verification'
Reset-Fixture; $global:ExpandAutomatic=$true; $backup=Join-Path $testPath 'expanded-apply.xml'; $action='Apply'; $null=& $code
Assert (($global:Servers.IPv4 -join ',') -eq ($provider.V4 -join ',') -and $global:Native.IPv4.ServerProperties[0].Flags -eq 1) 'expanded automatic/DDR readback is accepted after apply instead of false rollback'
$action='Read'; $read=(& $code | ConvertFrom-Json)
Assert ($read.Adapters[0].DohEnabled) 'expanded automatic mode is shown as enabled'
$action='Restore'; $null=& $code
Reset-Fixture; $global:NativeUnavailable=$true; $backup=Join-Path $testPath 'no-native.xml'; $action='Apply'; $failed=$false
try { $null=& $code } catch { $failed=$true }
Assert ($failed -and $global:Writes -eq 0 -and !(Test-Path $backup)) 'no mutation if adapter DoH cannot be read'
Reset-Fixture; $global:InvalidNative=$true; $backup=Join-Path $testPath 'invalid-native.xml'; $action='Apply'; $failed=$false
try { $null=& $code } catch { $failed=$true }
Assert ($failed -and $global:Writes -eq 0 -and !(Test-Path $backup) -and $global:Entries.Count -eq 1) 'apply and rollback data preflight before any DNS write'
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
Write-Output 'PASS DNS IPv4/IPv6, automatic interface/profile DoH, IP-only rejection, encrypted-profile verification, native rollback, DHCP restore and policy/adapter guards (mocked networking only).'
