param([switch]$RunNetworkTests)
$ErrorActionPreference = 'Stop'
$root=$PSScriptRoot
$version=[IO.File]::ReadAllText((Join-Path $root 'VERSION')).Trim()
if ($version -notmatch '^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$') { throw 'Invalid VERSION' }
$buildRelative='build\release\' + $version + '-' + [Guid]::NewGuid().ToString('N').Substring(0,8)
$stage=Join-Path $root $buildRelative
& (Join-Path $root 'build.ps1') -OutputDirectory $buildRelative
$exe=Join-Path $stage 'Zapret.exe'
$powershell=Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'
& $powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'tests\Features.Tests.ps1') -ExePath $exe
if ($LASTEXITCODE -ne 0) { throw 'Feature tests failed' }
& $powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'tests\DnsControl.Tests.ps1')
if ($LASTEXITCODE -ne 0) { throw 'DNS tests failed' }
& $powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'tests\StartupSchema.Tests.ps1') -ExePath $exe
if ($LASTEXITCODE -ne 0) { throw 'Task Scheduler schema validation failed' }
& $powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'tests\StartupRead.Tests.ps1') -ExePath $exe
if ($LASTEXITCODE -ne 0) { throw 'Native startup state read failed' }
if ($RunNetworkTests) {
    $process=Start-Process -FilePath $exe -ArgumentList '--self-test' -WindowStyle Hidden -PassThru -Wait
    if ($process.ExitCode -ne 0) { Get-Content (Join-Path $stage 'self-test.txt'); throw 'Network self-tests failed' }
    Write-Output 'PASS full self-test including loopback HTTP/curl'
}
$process=Start-Process -FilePath $exe -ArgumentList '--preview' -WindowStyle Hidden -PassThru -Wait
if ($process.ExitCode -ne 0) { throw 'UI render preview failed' }
$names=@('Zapret.exe','Zapret.exe.config','README.md','HOSTS-GUIDE.md','LICENSE','THIRD-PARTY-NOTICES.md','CHANGELOG.md','RELEASE.md','INSTALL.md','OFL.txt','LICENSE-Flowseal.txt','UPSTREAM-NOTICE.md')
foreach ($name in $names) { if (!(Test-Path -LiteralPath (Join-Path $stage $name) -PathType Leaf)) { throw ('Missing release file: ' + $name) } }
foreach ($name in @('LGPL-3.0.txt','GPL-3.0.txt','WinDivert-LICENSE.txt','Cygwin-linking-exception.txt')) {
    if (!(Test-Path -LiteralPath (Join-Path $stage ('licenses\' + $name)) -PathType Leaf)) { throw ('Missing dependency license: ' + $name) }
}
$dist=Join-Path $root 'dist'; $null=New-Item -ItemType Directory -Path $dist -Force
$zipName='Zapret-Desktop-' + $version + '-win-x64.zip'
$zipPath=Join-Path $dist $zipName
$directories=@('licenses','docs','assets')
$paths=@($names | ForEach-Object { Join-Path $stage $_ }) + @($directories | ForEach-Object { Join-Path $stage $_ })
$package=Join-Path $stage 'package'
$null=New-Item -ItemType Directory -Path $package
foreach ($path in $paths) { Copy-Item -LiteralPath $path -Destination $package -Recurse -Force }
& $powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'tests\ReleasePrivacy.Tests.ps1') -Directory $package
if ($LASTEXITCODE -ne 0) { throw 'Release privacy scan failed' }
$packagePaths=@($names | ForEach-Object { Join-Path $package $_ }) + @($directories | ForEach-Object { Join-Path $package $_ })
Compress-Archive -LiteralPath $packagePaths -DestinationPath $zipPath -Force
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive=[IO.Compression.ZipFile]::OpenRead($zipPath)
try {
    $allowed=@($names) + @($directories | ForEach-Object { Get-ChildItem -LiteralPath (Join-Path $stage $_) -Recurse -File | ForEach-Object { $_.FullName.Substring($stage.Length+1).Replace('\','/') } })
    foreach ($entry in $archive.Entries) { if ($allowed -notcontains $entry.FullName.Replace('\','/')) { throw ('Unexpected file in release ZIP: ' + $entry.FullName) } }
    foreach ($name in $allowed) { if (!($archive.Entries | Where-Object { $_.FullName.Replace('\','/') -eq $name })) { throw ('File missing from release ZIP: ' + $name) } }
} finally { $archive.Dispose() }
$artifacts=@($zipPath)
$standalone=Join-Path $dist ('Zapret-Desktop-' + $version + '-win-x64.exe'); Copy-Item -LiteralPath $exe -Destination $standalone -Force; $artifacts+=$standalone
& (Join-Path $root 'prepare-third-party-sources.ps1')
$artifacts+=Join-Path $dist ('Zapret-Desktop-' + $version + '-third-party-sources.zip')
$sums=@($artifacts | ForEach-Object { (Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + [IO.Path]::GetFileName($_) })
[IO.File]::WriteAllText((Join-Path $dist 'SHA256SUMS.txt'),(($sums -join "`n") + "`n"),[Text.UTF8Encoding]::new($false))
Write-Output ('Release package: ' + $zipPath)
Write-Output ('Build directory: ' + $stage)
Write-Output ($sums -join "`n")
