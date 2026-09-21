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
$assetFiles = @(
    'app.ico',
    'approved/animation-parts.png', 'approved/butterfly-open.png', 'approved/classic-open.png', 'approved/paws-mouse.png',
    'butterfly-mascot.png', 'classic-mascot.png',
    'huang/classic-closed.png', 'huang/classic-limbs.png', 'huang/classic-master.png', 'huang/classic-ornaments.png', 'huang/classic-plate.png',
    'huang/stage-closed.png', 'huang/stage-limbs.png', 'huang/stage-master.png', 'huang/stage-ornaments.png', 'huang/stage-plate.png',
    'layered/butterfly-brow-base.png', 'layered/butterfly-brows.png', 'layered/butterfly-closed-clean.png', 'layered/butterfly-ornaments.png', 'layered/butterfly-plate.png', 'layered/butterfly-sleeveless.png', 'layered/butterfly-sleep.png', 'layered/butterfly-sleeves.png',
    'layered/classic-bound-arms.png', 'layered/classic-brow-base.png', 'layered/classic-brows.png', 'layered/classic-closed-clean.png', 'layered/classic-ornaments.png', 'layered/classic-plate.png', 'layered/classic-sleeveless.png', 'layered/classic-sleep.png',
    'zhu/casual-closed.png', 'zhu/casual-master.png', 'zhu/casual-parts.png', 'zhu/casual-plate.png', 'zhu/stage-closed.png', 'zhu/stage-limbs.png', 'zhu/stage-master.png', 'zhu/stage-parts.png', 'zhu/stage-plate.png', 'zhu/star-eyes-small.png'
)
foreach($relative in $assetFiles)
{
    $source = Join-Path $PSScriptRoot ('assets\' + $relative.Replace('/','\'))
    $target = Join-Path $payload ('assets\' + $relative.Replace('/','\'))
    New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
    Copy-Item -LiteralPath $source -Destination $target
}
[IO.File]::WriteAllText((Join-Path $payload 'version.txt'), $Version, [Text.UTF8Encoding]::new($false))
$archive = Join-Path $releaseRoot 'DieShuDesktopPet-Windows.zip'
Compress-Archive -Path (Join-Path $payload '*') -DestinationPath $archive
$hash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText((Join-Path $releaseRoot 'SHA256SUMS.txt'), ($hash + '  DieShuDesktopPet-Windows.zip'), [Text.UTF8Encoding]::new($false))
Write-Output $releaseRoot
