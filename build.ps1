$ErrorActionPreference = 'Stop'
$compilerPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compilerPath)) { $compilerPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
$sourceFiles = @(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'src') -Filter '*.cs' | ForEach-Object { $_.FullName })
& $compilerPath /nologo /target:winexe /platform:anycpu /optimize+ /utf8output "/out:$PSScriptRoot\DieYing.exe" "/win32manifest:$PSScriptRoot\src\app.manifest" "/win32icon:$PSScriptRoot\assets\app.ico" /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:System.Web.Extensions.dll /reference:System.IO.Compression.dll /reference:System.IO.Compression.FileSystem.dll $sourceFiles
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
Write-Output 'Built development application: DieYing.exe'
