param(
    [string]$Path = (Join-Path $PSScriptRoot '..\build\LauncherDownloadFix.cmd')
)

$ErrorActionPreference = 'Stop'
$Path = [IO.Path]::GetFullPath($Path)
$root = Split-Path -Parent $Path
$scriptPath = Join-Path $root 'LauncherDownloadFix.ps1'
$payloadPath = Join-Path $root 'payload.json'
$cecilPath = Join-Path $root 'lib\Mono.Cecil.dll'
$patcherPath = Join-Path $root 'src\patcher.cs'
$hmclPath = Join-Path $root 'src\HMCLPatch.cs'

foreach ($file in @($Path, $scriptPath, $payloadPath, $cecilPath, $patcherPath, $hmclPath)) {
    if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { throw "Missing: $file" }
}

$cmdText = [IO.File]::ReadAllText($Path, [Text.Encoding]::Default)
if ($cmdText -match '-EncodedCommand') { throw 'Launcher still uses -EncodedCommand.' }
if ($cmdText -notmatch '-File') { throw 'Launcher does not invoke the visible PowerShell script.' }
if ($cmdText -notmatch 'PCL_PATCH_ARGS') { throw 'Launcher does not pass arguments through PCL_PATCH_ARGS.' }

$scriptBytes = [IO.File]::ReadAllBytes($scriptPath)
if ($scriptBytes.Length -lt 3 -or $scriptBytes[0] -ne 0xEF -or $scriptBytes[1] -ne 0xBB -or $scriptBytes[2] -ne 0xBF) {
    throw 'LauncherDownloadFix.ps1 is missing the UTF-8 BOM required by Windows PowerShell.'
}
$scriptText = [IO.File]::ReadAllText($scriptPath, [Text.Encoding]::UTF8)
[void][ScriptBlock]::Create($scriptText)

$manifest = [IO.File]::ReadAllText($payloadPath, [Text.Encoding]::UTF8) | ConvertFrom-Json
if ($manifest.format -ne 'external-v1') { throw "Unexpected payload format: $($manifest.format)" }
$files = @($manifest.files)
if ($files.Count -ne 3) { throw "Unexpected payload file count: $($files.Count)" }

foreach ($entry in @(
    [pscustomobject]@{ name = 'lib/Mono.Cecil.dll'; path = $cecilPath },
    [pscustomobject]@{ name = 'src/patcher.cs'; path = $patcherPath },
    [pscustomobject]@{ name = 'src/HMCLPatch.cs'; path = $hmclPath }
)) {
    $matched = @($files | Where-Object { $_.name -eq $entry.name })
    if ($matched.Count -ne 1) { throw "Manifest entry missing: $($entry.name)" }
    $file = Get-Item -LiteralPath $entry.path
    if ($file.Length -ne [long]$matched[0].length) { throw "Size mismatch: $($entry.name)" }
    $hash = (Get-FileHash -LiteralPath $entry.path -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($hash -ne [string]$matched[0].sha256) { throw "Hash mismatch: $($entry.name)" }
}

$cecilHash = (Get-FileHash -LiteralPath $cecilPath -Algorithm SHA256).Hash.ToLowerInvariant()
if ($cecilHash -ne 'c41bdb9ffd3c5f6e17d2382c1012d73703e035e3f1100245fdd4e08c8dc6eb5b') {
    throw "Unexpected Mono.Cecil hash: $cecilHash"
}

[pscustomobject]@{
    Result   = 'PASS'
    Launcher = $Path
    Script   = $scriptPath
    Payload  = $payloadPath
    Cecil    = $cecilHash
    Patcher  = (Get-FileHash -LiteralPath $patcherPath -Algorithm SHA256).Hash.ToLowerInvariant()
} | Format-List
