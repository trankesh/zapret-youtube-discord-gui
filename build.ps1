param([string]$OutputDirectory = 'build')
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$version = [IO.File]::ReadAllText((Join-Path $root 'VERSION')).Trim()
if ($version -notmatch '^(\d+)\.(\d+)\.(\d+)(-[0-9A-Za-z.-]+)?$') { throw 'VERSION must contain a semantic version' }
$numericVersion = $Matches[1] + '.' + $Matches[2] + '.' + $Matches[3] + '.0'
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$out = Join-Path $root $OutputDirectory
New-Item -ItemType Directory -Path $out -Force | Out-Null
$versionSource = Join-Path $out 'Version.g.cs'
[IO.File]::WriteAllText($versionSource, ('using System.Reflection; [assembly: AssemblyVersion("' + $numericVersion + '")] [assembly: AssemblyFileVersion("' + $numericVersion + '")] [assembly: AssemblyInformationalVersion("' + $version + '")]'))
$manifest = Join-Path $out 'Zapret.manifest'
[IO.File]::WriteAllText($manifest, ([IO.File]::ReadAllText((Join-Path $root 'app.manifest')).Replace('version="0.1.0.0"', ('version="' + $numericVersion + '"'))))
& (Join-Path $root 'build-icon.ps1')
$refs = @('System.dll', 'System.Core.dll', 'Microsoft.CSharp.dll', 'System.Xml.dll', 'System.Xaml.dll', 'System.Drawing.dll', 'System.IO.Compression.dll', 'System.IO.Compression.FileSystem.dll', 'System.Net.Http.dll', 'System.ServiceProcess.dll', 'System.Web.Extensions.dll', 'System.Windows.Forms.dll') | ForEach-Object { '/reference:' + (Join-Path $framework $_) }
$refs += @('PresentationCore.dll', 'PresentationFramework.dll', 'WindowsBase.dll') | ForEach-Object { '/reference:' + (Join-Path $framework ('WPF\' + $_)) }
$sources = @(Get-ChildItem -LiteralPath (Join-Path $root 'src') -Filter '*.cs' | ForEach-Object FullName) + @($versionSource)
$fontResource = Join-Path $out 'Zapret.g.resources'
$licenseResources = @('LICENSE','THIRD-PARTY-NOTICES.md') + @(Get-ChildItem -LiteralPath (Join-Path $root 'licenses') -File | ForEach-Object { 'licenses/' + $_.Name })
$licenseResources = @($licenseResources | ForEach-Object { '/resource:' + (Join-Path $root $_) + ',Legal.' + [IO.Path]::GetFileName($_) })
$nativeDnsResource='/resource:'+(Join-Path $root 'src\InterfaceDns.cs')+',InterfaceDns.cs'
$writer = [System.Resources.ResourceWriter]::new($fontResource)
try {
    foreach ($font in Get-ChildItem -LiteralPath (Join-Path $root 'assets\fonts') -Filter '*.ttf') {
        $writer.AddResource(('fonts/' + $font.Name.ToLowerInvariant()), [IO.File]::OpenRead($font.FullName), $true)
    }
    $writer.AddResource('icons/app.ico', [IO.File]::OpenRead((Join-Path $root 'assets\app.ico')), $true)
    $writer.Generate()
} finally { $writer.Dispose() }
& (Join-Path $framework 'csc.exe') /nologo /target:winexe /platform:x64 /optimize+ /utf8output ('/out:' + (Join-Path $out 'Zapret.exe')) ('/win32manifest:' + $manifest) ('/win32icon:' + (Join-Path $root 'assets\app.ico')) ('/resource:' + (Join-Path $root 'src\Shell.xaml') + ',Shell.xaml') ('/resource:' + (Join-Path $root 'Zapret.exe.config') + ',Default.config') ('/resource:' + (Join-Path $root 'src\DnsControl.ps1') + ',DnsControl.ps1') ('/resource:' + $fontResource + ',Zapret.g.resources') ('/resource:' + (Join-Path $root 'assets\fonts\OFL.txt') + ',Fonts.OFL.txt') ('/resource:' + (Join-Path $root 'assets\bundle\Flowseal-1.10.4.zip') + ',BundledZapret.zip') ('/resource:' + (Join-Path $root 'assets\bundle\LICENSE-Flowseal.txt') + ',Flowseal.LICENSE.txt') $nativeDnsResource @licenseResources @refs @sources
if ($LASTEXITCODE -ne 0) { throw 'Compilation failed' }
Copy-Item -LiteralPath (Join-Path $root 'Zapret.exe.config') -Destination $out -Force
Copy-Item -LiteralPath (Join-Path $root 'assets\fonts\OFL.txt') -Destination $out -Force
Copy-Item -LiteralPath (Join-Path $root 'assets\bundle\LICENSE-Flowseal.txt') -Destination $out -Force
Copy-Item -LiteralPath (Join-Path $root 'assets\bundle\NOTICE.md') -Destination (Join-Path $out 'UPSTREAM-NOTICE.md') -Force
foreach ($document in @('README.md','HOSTS-GUIDE.md','LICENSE','THIRD-PARTY-NOTICES.md','CHANGELOG.md','RELEASE.md','INSTALL.md')) { Copy-Item -LiteralPath (Join-Path $root $document) -Destination $out -Force }
Copy-Item -LiteralPath (Join-Path $root 'licenses') -Destination $out -Recurse -Force
Copy-Item -LiteralPath (Join-Path $root 'docs') -Destination $out -Recurse -Force
foreach ($relative in @('assets/app.png','assets/fonts/OFL.txt','assets/bundle/NOTICE.md','assets/bundle/LICENSE-Flowseal.txt')) {
    $destination=Join-Path $out $relative
    $null=New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force
    Copy-Item -LiteralPath (Join-Path $root $relative) -Destination $destination -Force
}
Write-Output ('Built: ' + (Join-Path $out 'Zapret.exe'))
