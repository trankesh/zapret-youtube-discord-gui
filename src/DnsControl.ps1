$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
[Console]::OutputEncoding = New-Object Text.UTF8Encoding($false)
function Find-Adapter($id) {
    $items = @(Get-NetAdapter -IncludeHidden | Where-Object { $_.InterfaceGuid.ToString().Trim('{}') -eq $id.Trim('{}') })
    if ($items.Count -ne 1) { throw 'Network adapter not found. Refresh the adapter list.' }
    return $items[0]
}
function New-InterfaceDoh($servers,$previous) {
    $props = @(for ($i=0; $i -lt $servers.Count; $i++) { [pscustomobject]@{Index=$i;Flags=[uint64]1;Template=$null} })
    $profile=![string]::IsNullOrWhiteSpace($previous.ProfileNameServer)
    $profileProps=@(); if($profile) { $profileProps=@($props) }
    [pscustomobject]@{NameServer=($servers -join ',');ProfileNameServer=$(if($profile){$servers -join ','}else{$null});ServerProperties=[object[]]$props;ProfileServerProperties=[object[]]$profileProps}
}
function Test-InterfaceDoh($state,$servers,$automatic=$false) {
    $name=$state.NameServer; $props=@($state.ServerProperties)
    if (![string]::IsNullOrWhiteSpace($state.ProfileNameServer)) { $name=$state.ProfileNameServer; $props=@($state.ProfileServerProperties) }
    $actual=@(($name -split '[,\s]+' | Where-Object { $_ }) | ForEach-Object { ([ipaddress]$_).ToString() })
    $expected=@($servers | ForEach-Object { ([ipaddress]$_).ToString() })
    if (!$expected.Count -or ($actual -join ',') -ne ($expected -join ',') -or $props.Count -ne $expected.Count) { return $false }
    for ($i=0; $i -lt $expected.Count; $i++) {
        $p=@($props | Where-Object Index -eq $i)
        if ($p.Count -ne 1 -or !([uint64]$p[0].Flags -band 3) -or ([uint64]$p[0].Flags -band 4)) { return $false }
        # Windows may expand the template when reading automatic mode. Only
        # writes require NULL; verify the mode bits here, not that read value.
        if ($automatic -and (([uint64]$p[0].Flags -band 3) -ne 1)) { return $false }
    }
    return $true
}
function Interface-Signature($state) {
    @($state.NameServer,$state.ProfileNameServer | ForEach-Object { (@($_ -split '[,\s]+' | Where-Object { $_ } | ForEach-Object { ([ipaddress]$_).ToString() }) -join ',') }) -join '|'
    foreach ($props in @(@{P=$state.ServerProperties},@{P=$state.ProfileServerProperties})) { @($props.P | Sort-Object Index | ForEach-Object { $template=if([uint64]$_.Flags -band 1){''}else{$_.Template}; '{0}:{1}:{2}' -f $_.Index,$_.Flags,$template }) -join ';' }
}
function Restore-State($state) {
    if ($state.Version -notin @(1,2)) { throw 'Unsupported DNS backup.' }
    $adapter = Find-Adapter $state.Guid
    if($state.Version -eq 2) { foreach($family in $state.Families) { Test-ZapretInterfaceDns $family.AF $family.Interface } }
    $errorsFound = @()
    foreach ($family in $state.Families) {
        try {
            $row = Get-DnsClientServerAddress -InterfaceIndex $adapter.ifIndex -AddressFamily $family.AF
            if ($family.Static) { Set-DnsClientServerAddress -InputObject $row -ServerAddresses ([string[]]$family.Servers) }
            else { Set-DnsClientServerAddress -InputObject $row -ResetServerAddresses }
        } catch { $errorsFound += $_.Exception.Message }
    }
    foreach ($entry in $state.Entries) {
        try {
            if ($entry.Existed) {
                $existing = @(Get-DnsClientDohServerAddress | Where-Object ServerAddress -eq $entry.IP)
                if ($existing.Count) { Set-DnsClientDohServerAddress -ServerAddress $entry.IP -DohTemplate $entry.Template -AutoUpgrade ([bool]$entry.Auto) -AllowFallbackToUdp ([bool]$entry.Fallback) }
                else { Add-DnsClientDohServerAddress -ServerAddress $entry.IP -DohTemplate $entry.Template -AutoUpgrade ([bool]$entry.Auto) -AllowFallbackToUdp ([bool]$entry.Fallback) }
            } else {
                if (@(Get-DnsClientDohServerAddress | Where-Object ServerAddress -eq $entry.IP).Count) { Remove-DnsClientDohServerAddress -ServerAddress $entry.IP -ErrorAction Stop }
            }
        } catch { $errorsFound += $_.Exception.Message }
    }
    if ($state.Version -eq 2) {
        foreach ($family in $state.Families) {
            try {
                Set-ZapretInterfaceDns $state.Guid $family.AF $family.Interface
                $actual=Get-ZapretInterfaceDns $state.Guid $family.AF
                if ((Interface-Signature $actual | Out-String) -ne (Interface-Signature $family.Interface | Out-String)) { throw 'Adapter DoH rollback verification failed.' }
            } catch { $errorsFound += $_.Exception.Message }
        }
    }
    if ($errorsFound.Count) { throw ($errorsFound -join '; ') }
    Clear-DnsClientCache
}
try {
    $supported = [bool](Get-Command Get-DnsClientDohServerAddress -ErrorAction SilentlyContinue)
    if ($supported) { try { $null = Get-DnsClientDohServerAddress } catch { $supported = $false } }
    if ($action -eq 'Read') {
        $items = @(foreach ($adapter in (Get-NetAdapter | Where-Object Status -eq 'Up')) {
            $secure=$true; $nativeSupported=$supported
            foreach ($af in @('IPv4','IPv6')) {
                $binding=if ($af -eq 'IPv4') { 'ms_tcpip' } else { 'ms_tcpip6' }
                if (!(Get-NetAdapterBinding -Name $adapter.Name -ComponentID $binding).Enabled) { continue }
                try { $native=Get-ZapretInterfaceDns $adapter.InterfaceGuid.ToString() $af; $servers=@((Get-DnsClientServerAddress -InterfaceIndex $adapter.ifIndex -AddressFamily $af).ServerAddresses); if ($servers.Count -and !(Test-InterfaceDoh $native $servers)) { $secure=$false } }
                catch { $nativeSupported=$false; $secure=$null }
            }
            [pscustomobject]@{ Guid=$adapter.InterfaceGuid.ToString(); Name=$adapter.Name; Servers=@(Get-DnsClientServerAddress -InterfaceIndex $adapter.ifIndex | ForEach-Object { $_.ServerAddresses });DohEnabled=$secure;DohSupported=$nativeSupported }
        })
        [pscustomobject]@{ Doh=$supported; Adapters=$items } | ConvertTo-Json -Depth 6 -Compress
    } elseif ($action -eq 'Restore') {
        Restore-State (Import-Clixml -LiteralPath $backup)
        Write-Output 'Previous DNS settings restored.'
    } elseif ($action -eq 'Apply') {
        if (!$supported) { throw 'System DNS over HTTPS is unavailable. Use Windows 11 or browser Secure DNS.' }
        $policy = Get-ItemProperty -LiteralPath 'HKLM:\SOFTWARE\Policies\Microsoft\Windows NT\DNSClient' -Name DoHPolicy -ErrorAction SilentlyContinue
        if ($null -ne $policy -and $policy.DoHPolicy -eq 1) { throw 'DNS over HTTPS is prohibited by Windows policy.' }
        $adapter = Find-Adapter $adapterId
        if ($adapter.Status -ne 'Up') { throw 'Selected network adapter is disconnected.' }
        $families = @(foreach ($af in @('IPv4','IPv6')) {
            $binding = if ($af -eq 'IPv4') { 'ms_tcpip' } else { 'ms_tcpip6' }
            if (!(Get-NetAdapterBinding -Name $adapter.Name -ComponentID $binding).Enabled) { continue }
            $protocol = if ($af -eq 'IPv4') { 'Tcpip' } else { 'Tcpip6' }
            $path = 'HKLM:\SYSTEM\CurrentControlSet\Services\' + $protocol + '\Parameters\Interfaces\{' + $adapter.InterfaceGuid.ToString().Trim('{}') + '}'
            $static = (Get-ItemProperty -LiteralPath $path -Name NameServer -ErrorAction SilentlyContinue).NameServer
            [pscustomobject]@{ AF=$af; Static=![string]::IsNullOrWhiteSpace(($static -join ' ')); Servers=@((Get-DnsClientServerAddress -InterfaceIndex $adapter.ifIndex -AddressFamily $af).ServerAddresses);Interface=(Get-ZapretInterfaceDns $adapter.InterfaceGuid.ToString() $af) }
        })
        if (!$families.Count) { throw 'No enabled IP protocol on the adapter.' }
        foreach ($family in $families) {
            $servers=if($family.AF -eq 'IPv4') { [string[]]$provider.V4 } else { [string[]]$provider.V6 }
            Test-ZapretInterfaceDns $family.AF (New-InterfaceDoh $servers $family.Interface)
            Test-ZapretInterfaceDns $family.AF $family.Interface
        }
        $addresses = @(foreach ($family in $families) { if ($family.AF -eq 'IPv4') { $provider.V4 } else { $provider.V6 } })
        $known = @(Get-DnsClientDohServerAddress)
        $entries = @(foreach ($ip in $addresses) {
            $old = @($known | Where-Object ServerAddress -eq $ip)
            [pscustomobject]@{ IP=$ip; Existed=($old.Count -gt 0); Template=($old | Select-Object -First 1 -ExpandProperty DohTemplate); Auto=($old | Select-Object -First 1 -ExpandProperty AutoUpgrade); Fallback=($old | Select-Object -First 1 -ExpandProperty AllowFallbackToUdp) }
        })
        $state = [pscustomobject]@{ Version=2; Guid=$adapter.InterfaceGuid.ToString(); Name=$adapter.Name; Provider=$provider.Id; Families=$families; Entries=$entries }
        $state | Export-Clixml -LiteralPath $backup -NoClobber -Depth 8
        try {
            foreach ($entry in $entries) {
                if ($entry.Existed) { Set-DnsClientDohServerAddress -ServerAddress $entry.IP -DohTemplate $provider.Template -AutoUpgrade $true -AllowFallbackToUdp $false }
                else { Add-DnsClientDohServerAddress -ServerAddress $entry.IP -DohTemplate $provider.Template -AutoUpgrade $true -AllowFallbackToUdp $false }
            }
            foreach ($family in $families) {
                $servers = if ($family.AF -eq 'IPv4') { [string[]]$provider.V4 } else { [string[]]$provider.V6 }
                Set-DnsClientServerAddress -InputObject (Get-DnsClientServerAddress -InterfaceIndex $adapter.ifIndex -AddressFamily $family.AF) -ServerAddresses $servers
                Set-ZapretInterfaceDns $adapter.InterfaceGuid.ToString() $family.AF (New-InterfaceDoh $servers $family.Interface)
                $actual = @((Get-DnsClientServerAddress -InterfaceIndex $adapter.ifIndex -AddressFamily $family.AF).ServerAddresses)
                if (($actual -join ',') -ne ($servers -join ',')) { throw 'Windows did not accept the requested DNS addresses.' }
                if (!(Test-InterfaceDoh (Get-ZapretInterfaceDns $adapter.InterfaceGuid.ToString() $family.AF) $servers $true)) { throw 'Windows did not enable automatic DoH on the selected adapter.' }
            }
            foreach ($ip in $addresses) {
                $actual = @(Get-DnsClientDohServerAddress | Where-Object ServerAddress -eq $ip)
                if ($actual.Count -ne 1 -or !$actual[0].AutoUpgrade -or $actual[0].AllowFallbackToUdp -or $actual[0].DohTemplate -ne $provider.Template) { throw 'Windows did not accept the requested DoH configuration.' }
            }
            Clear-DnsClientCache
            Write-Output 'DNS over HTTPS configured. Unencrypted fallback disabled.'
        } catch {
            $original = $_.Exception.Message
            try { Restore-State $state } catch { throw ($original + ' Rollback failed: ' + $_.Exception.Message + ' Backup: ' + $backup) }
            throw ($original + ' Previous settings restored.')
        }
    } else { throw 'Unknown DNS action.' }
} catch { [Console]::Error.WriteLine($_.Exception.Message); exit 1 }
