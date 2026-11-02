param([Parameter(Mandatory=$true)][string]$Directory)
$ErrorActionPreference='Stop'
$directoryPath=(Resolve-Path -LiteralPath $Directory).Path
$files=Get-ChildItem -LiteralPath $directoryPath -Recurse -File
$secrets='ghp_[A-Za-z0-9]{30,}|github_pat_[A-Za-z0-9_]{60,}|AKIA[A-Z0-9]{16}|-----BEGIN (RSA |EC |OPENSSH )?PRIVATE KEY-----'
$forbidden=@('settings.json','zapret.log','last-results.json','last-results.txt','portable.mode','installed.mode','error.log')
$userPattern=if ($env:USERNAME.Length -ge 4) { '(?i)\b'+[regex]::Escape($env:USERNAME)+'\b' } else { '(?!)' }
foreach ($file in $files) {
    $relative=$file.FullName.Substring($directoryPath.Length).TrimStart('\','/')
    if ($file.Name -in $forbidden -or $relative -match '(^|[\\/])(data|backups|\.git)([\\/]|$)' -or $file.Extension -in @('.log','.pdb')) { throw ('Private/runtime file in release: '+$relative) }
    if ($file.Extension -in @('.png','.ico','.ttf','.zip','.xz','.gz')) { continue }
    $bytes=[IO.File]::ReadAllBytes($file.FullName)
    foreach ($text in @([Text.Encoding]::UTF8.GetString($bytes),[Text.Encoding]::Unicode.GetString($bytes))) {
        if ($text -match '(?i)[A-Z]:[\\/]Users[\\/][^\s\\/]+') { throw ('Absolute user profile path in release: '+$relative) }
        if ($text -match $userPattern) { throw ('Build user name in release: '+$relative) }
        if ($text -match $secrets) { throw ('Potential credential in release: '+$relative) }
    }
}
Write-Output ('PASS release privacy scan: '+$files.Count+' files; no profile/logs/backups, build user, absolute profile paths or credential patterns.')
