param([Parameter(Mandatory=$true)][string]$Version)
$ErrorActionPreference = 'Stop'
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw 'Expected version such as 0.2.0.' }
$sourceVersion = [regex]::Match((Get-Content -LiteralPath (Join-Path $PSScriptRoot 'src\UpdateService.cs') -Raw), 'VersionText = "([0-9.]+)"').Groups[1].Value
if ($Version -ne $sourceVersion) { throw 'Release tag must match UpdateService.VersionText.' }
& (Join-Path $PSScriptRoot 'build.ps1')
$releaseRoot = Join-Path $PSScriptRoot ('dist\' + $Version + '-' + [guid]::NewGuid().ToString('N'))
$payload = Join-Path $releaseRoot 'package'
New-Item -ItemType Directory -Path $payload -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'DieYing.exe') -Destination $payload
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'assets') -Destination $payload -Recurse
[IO.File]::WriteAllText((Join-Path $payload 'version.txt'), $Version, [Text.UTF8Encoding]::new($false))
$archive = Join-Path $releaseRoot 'DieYing-Windows.zip'
Compress-Archive -Path (Join-Path $payload '*') -DestinationPath $archive
$hash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText((Join-Path $releaseRoot 'SHA256SUMS.txt'), ($hash + '  DieYing-Windows.zip'), [Text.UTF8Encoding]::new($false))
Write-Output $releaseRoot
