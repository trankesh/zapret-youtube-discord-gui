param([string]$CacheDirectory = 'build\upstream-verification', [string]$OutputDirectory = 'dist')
$ErrorActionPreference='Stop'
[Net.ServicePointManager]::SecurityProtocol=[Net.SecurityProtocolType]::Tls12
$root=$PSScriptRoot
$cache=Join-Path $root $CacheDirectory
$dist=Join-Path $root $OutputDirectory
$version=[IO.File]::ReadAllText((Join-Path $root 'VERSION')).Trim()
if ($version -notmatch '^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$') { throw 'Invalid VERSION' }
$null=New-Item -ItemType Directory -Path $cache,$dist -Force
$archives=@(
    @{Name='WinDivert-v2.2.2-source.tar.gz';Url='https://api.github.com/repos/basil00/WinDivert/tarball/v2.2.2';Hash='b26a67dd9f58a899db1aa5b47a467c0efb98cf0d6cf357ec46c1198c260ccd7f'},
    @{Name='cygwin-3.4.10-1-src.tar.xz';Url='https://ftp.cvut.cz/mirrors/cygwin.com/x86_64/release/cygwin/cygwin-3.4.10-1-src.tar.xz';Hash='ac70af0d4e644732f74946f55f7bafe010bab9b9da39a9327c3f0367f1fa43a2'}
)
$files=@()
foreach ($archive in $archives) {
    $file=Join-Path $cache $archive.Name
    if (!(Test-Path -LiteralPath $file) -or (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant() -ne $archive.Hash) {
        $partial=$file+'.download'
        Invoke-WebRequest -UseBasicParsing -Uri $archive.Url -OutFile $partial -Headers @{'User-Agent'='ZapretDesktop-release'} -TimeoutSec 180
        if ((Get-FileHash -LiteralPath $partial -Algorithm SHA256).Hash.ToLowerInvariant() -ne $archive.Hash) { throw ('Source archive checksum mismatch: '+$archive.Name) }
        Move-Item -LiteralPath $partial -Destination $file -Force
    }
    $files+=$file
}
$files+=Join-Path $root 'licenses\SOURCES.md'
$files+=Get-ChildItem -LiteralPath (Join-Path $root 'licenses') -File -Filter '*.txt' | ForEach-Object FullName
$path=Join-Path $dist ('Zapret-Desktop-'+$version+'-third-party-sources.zip')
Compress-Archive -LiteralPath $files -DestinationPath $path -Force
Write-Output ('Verified corresponding sources: '+$path)
