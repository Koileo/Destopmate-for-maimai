param(
    [string]$Version = '0.2.0'
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$dll = Join-Path $projectRoot 'bin\Release\DesktopMateOnMaimai.dll'
if (-not (Test-Path -LiteralPath $dll)) { throw 'Build Release before packaging.' }
$outputRoot = Join-Path $projectRoot 'dist'
New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null
$staging = Join-Path $outputRoot ('package-' + [guid]::NewGuid().ToString('N'))
$modRoot = Join-Path $staging 'Mods\DesktopMate'
$characterRoot = Join-Path $modRoot 'Characters\placeholder'
New-Item -ItemType Directory -Path $characterRoot -Force | Out-Null
Copy-Item -LiteralPath $dll -Destination (Join-Path $staging 'Mods\DesktopMateOnMaimai.dll')
Copy-Item -LiteralPath (Join-Path $projectRoot 'assets\DesktopMate.toml') -Destination $modRoot
Copy-Item -LiteralPath (Join-Path $projectRoot 'assets\placeholder\miku\sprites') -Destination $characterRoot -Recurse
Copy-Item -LiteralPath (Join-Path $projectRoot 'docs\04-安装与验证.md') -Destination (Join-Path $staging 'INSTALL.zh-CN.md')
$zip = Join-Path $outputRoot ('DesktopMateOnMaimai-v' + $Version + '.zip')
Compress-Archive -Path (Join-Path $staging '*') -DestinationPath $zip -Force
# Deliberately includes only our mod, config, test sprites and instructions.
# Game DLLs, local character exports and Desktop Mate packages stay local.
Get-Item -LiteralPath $zip | Select-Object FullName,Length
Get-FileHash -LiteralPath $zip -Algorithm SHA256
