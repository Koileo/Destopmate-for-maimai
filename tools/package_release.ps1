param(
    [string]$Version = '0.5.4',
    [string]$MikuSource = (Join-Path $PSScriptRoot '..\assets\local\miku-local')
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$dll = Join-Path $projectRoot 'bin\Release\DesktopMateOnMaimai.dll'
if (-not (Test-Path -LiteralPath $dll)) { throw 'Build Release before packaging.' }
# Every release must include the model selected by the default configuration.
# Fail before touching an existing ZIP if the local export is unavailable.
foreach ($required in @('character.dmc', 'motion.dmm')) {
    $resource = Join-Path $MikuSource $required
    if (-not (Test-Path -LiteralPath $resource -PathType Leaf) -or (Get-Item -LiteralPath $resource).Length -eq 0) {
        throw "Missing Miku release asset: $resource"
    }
}
$textures = @(Get-ChildItem -LiteralPath $MikuSource -Filter '*.png' -File)
if ($textures.Count -eq 0) { throw 'Miku release assets contain no textures.' }
$installDocs = @(Get-ChildItem -LiteralPath (Join-Path $projectRoot 'docs') -Filter '04-*.md' -File)
if ($installDocs.Count -ne 1) { throw 'Expected one installation guide in docs/04-*.md.' }

# Check every texture referenced by the portable model, including missing files
# that a broad *.png copy alone would silently overlook.
$reader = [IO.BinaryReader]::new([IO.File]::OpenRead((Join-Path $MikuSource 'character.dmc')))
try {
    if ([Text.Encoding]::ASCII.GetString($reader.ReadBytes(8)) -ne 'DMCHAR01') { throw 'Invalid Miku model header.' }
    $nodeCount = $reader.ReadInt32()
    if ($nodeCount -le 0 -or $nodeCount -gt 4096) { throw 'Invalid Miku node count.' }
    for ($i = 0; $i -lt $nodeCount; $i++) {
        $length = $reader.ReadInt32()
        if ($length -lt 0 -or $length -gt 4096) { throw 'Invalid Miku node name.' }
        [void]$reader.ReadBytes($length + 44)
    }
    $materialCount = $reader.ReadInt32()
    if ($materialCount -le 0 -or $materialCount -gt 256) { throw 'Invalid Miku material count.' }
    for ($i = 0; $i -lt $materialCount; $i++) {
        $length = $reader.ReadInt32()
        if ($length -lt 0 -or $length -gt 4096) { throw 'Invalid Miku material name.' }
        [void]$reader.ReadBytes($length)
        $length = $reader.ReadInt32()
        if ($length -lt 0 -or $length -gt 4096) { throw 'Invalid Miku texture name.' }
        $texture = [Text.Encoding]::UTF8.GetString($reader.ReadBytes($length))
        if ($texture -and ($texture -ne [IO.Path]::GetFileName($texture) -or
            [IO.Path]::GetExtension($texture) -ne '.png' -or
            -not (Test-Path -LiteralPath (Join-Path $MikuSource $texture) -PathType Leaf))) {
            throw "Missing or invalid Miku texture: $texture"
        }
        [void]$reader.ReadBytes(20)
    }
}
finally { $reader.Dispose() }
$outputRoot = Join-Path $projectRoot 'dist'
New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null
$staging = Join-Path $outputRoot ('package-' + [guid]::NewGuid().ToString('N'))
$zip = Join-Path $outputRoot ('DesktopMateOnMaimai-v' + $Version + '.zip')
try {
    $modRoot = Join-Path $staging 'Mods\DesktopMate'
    $characterRoot = Join-Path $modRoot 'Characters\miku-local'
    New-Item -ItemType Directory -Path $characterRoot -Force | Out-Null
    Copy-Item -LiteralPath $dll -Destination (Join-Path $staging 'Mods\DesktopMateOnMaimai.dll')
    Copy-Item -LiteralPath (Join-Path $projectRoot 'assets\DesktopMate.toml') -Destination $modRoot
    Copy-Item -LiteralPath (Join-Path $MikuSource 'character.dmc') -Destination $characterRoot
    Copy-Item -LiteralPath (Join-Path $MikuSource 'motion.dmm') -Destination $characterRoot
    $textures | Copy-Item -Destination $characterRoot
    # Discover the Unicode filename without a non-ASCII literal, so the script
    # works under Windows PowerShell 5.1's legacy script encoding as well.
    Copy-Item -LiteralPath $installDocs[0].FullName -Destination (Join-Path $staging 'INSTALL.zh-CN.md')
    Copy-Item -LiteralPath (Join-Path $projectRoot 'THIRD_PARTY.md') -Destination $staging
    # Only Miku runtime assets are bundled; source.json, other characters,
    # game DLLs, Desktop Mate packages and decryption data stay local.
    Compress-Archive -Path (Join-Path $staging '*') -DestinationPath $zip -Force
}
finally {
    $resolvedStaging = [IO.Path]::GetFullPath($staging)
    $resolvedOutput = [IO.Path]::GetFullPath($outputRoot).TrimEnd('\') + '\'
    if (-not $resolvedStaging.StartsWith($resolvedOutput, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Refusing to remove a staging directory outside dist.'
    }
    if (Test-Path -LiteralPath $resolvedStaging) { Remove-Item -LiteralPath $resolvedStaging -Recurse -Force }
}
Get-Item -LiteralPath $zip | Select-Object FullName,Length
Get-FileHash -LiteralPath $zip -Algorithm SHA256
