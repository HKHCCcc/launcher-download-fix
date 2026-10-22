[CmdletBinding()]
param(
    [string]$OutputPath
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $root 'build\LauncherDownloadFix.cmd'
}
$OutputPath = [IO.Path]::GetFullPath($OutputPath)
$buildDir = Split-Path -Parent $OutputPath
$libDir = Join-Path $buildDir 'lib'
$srcDir = Join-Path $buildDir 'src'
$work = Join-Path $root 'work'
foreach ($dir in @($buildDir, $libDir, $srcDir, $work)) {
    if (-not (Test-Path -LiteralPath $dir)) { [void][IO.Directory]::CreateDirectory($dir) }
}

function Get-Sha256Hex {
    param([byte[]]$Bytes)
    $sha = [Security.Cryptography.SHA256]::Create()
    try { return ([BitConverter]::ToString($sha.ComputeHash($Bytes)) -replace '-', '').ToLowerInvariant() } finally { $sha.Dispose() }
}

$scriptSource = [IO.File]::ReadAllText((Join-Path $root 'src\patch.ps1'), [Text.Encoding]::UTF8)
$cecilSource = Join-Path $root 'lib\Mono.Cecil.dll'
$patcherSource = Join-Path $root 'src\patcher.cs'
$hmclSource = Join-Path $root 'src\HMCLPatch.cs'
$licenseSource = Join-Path $root 'src\CECIL-LICENSE.txt'
$scriptPath = Join-Path $buildDir 'LauncherDownloadFix.ps1'
$cecilPath = Join-Path $libDir 'Mono.Cecil.dll'
$patcherPath = Join-Path $srcDir 'patcher.cs'
$hmclPath = Join-Path $srcDir 'HMCLPatch.cs'
$licensePath = Join-Path $buildDir 'CECIL-LICENSE.txt'

# Keep the PowerShell entry point visible and UTF-8 with BOM.  A visible script
# avoids the encoded-command bootstrap that is commonly used by malware.
[IO.File]::WriteAllText($scriptPath, $scriptSource, (New-Object Text.UTF8Encoding($true)))
[IO.File]::Copy($cecilSource, $cecilPath, $true)
[IO.File]::Copy($patcherSource, $patcherPath, $true)
[IO.File]::Copy($hmclSource, $hmclPath, $true)
[IO.File]::Copy($licenseSource, $licensePath, $true)

$files = @(
    [pscustomobject]@{
        name   = 'lib/Mono.Cecil.dll'
        length = (Get-Item -LiteralPath $cecilPath).Length
        sha256 = (Get-FileHash -LiteralPath $cecilPath -Algorithm SHA256).Hash.ToLowerInvariant()
    },
    [pscustomobject]@{
        name   = 'src/patcher.cs'
        length = (Get-Item -LiteralPath $patcherPath).Length
        sha256 = (Get-FileHash -LiteralPath $patcherPath -Algorithm SHA256).Hash.ToLowerInvariant()
    },
    [pscustomobject]@{
        name   = 'src/HMCLPatch.cs'
        length = (Get-Item -LiteralPath $hmclPath).Length
        sha256 = (Get-FileHash -LiteralPath $hmclPath -Algorithm SHA256).Hash.ToLowerInvariant()
    }
)
$manifest = [ordered]@{
    format = 'external-v1'
    files  = @($files)
} | ConvertTo-Json -Depth 5
[IO.File]::WriteAllText((Join-Path $buildDir 'payload.json'), $manifest, (New-Object Text.UTF8Encoding($false)))

$header = @"
@echo off
setlocal EnableExtensions DisableDelayedExpansion
set "PCL_PATCH_HOME=%~dp0"
set "PCL_PATCH_ARGS=%*"
if "%~1"=="" goto :guimode
if /i "%~1"=="--gui" goto :guimode
powershell.exe -NoProfile -STA -ExecutionPolicy Bypass -File "%~dp0LauncherDownloadFix.ps1"
exit /b %errorlevel%
:guimode
start "" powershell.exe -NoProfile -STA -WindowStyle Hidden -ExecutionPolicy Bypass -File "%~dp0LauncherDownloadFix.ps1"
exit /b 0
"@
$header = ($header -replace "`r?`n", "`r`n").TrimEnd([char[]]"`r`n") + "`r`n"
[IO.File]::WriteAllText($OutputPath, $header, (New-Object Text.ASCIIEncoding))

$legacyCab = Join-Path $work 'payload.cab'
$legacyDat = Join-Path $work 'payload.dat'
foreach ($legacy in @($legacyCab, $legacyDat)) {
    if (Test-Path -LiteralPath $legacy) { Remove-Item -LiteralPath $legacy -Force -ErrorAction SilentlyContinue }
}

$hash = (Get-FileHash -LiteralPath $OutputPath -Algorithm SHA256).Hash.ToLowerInvariant()
$file = Get-Item -LiteralPath $OutputPath
[pscustomobject]@{
    Output   = $file.FullName
    Size     = $file.Length
    SHA256   = $hash
    Script   = $scriptPath
    Payload  = (Join-Path $buildDir 'payload.json')
    Manifest = $manifest
} | Format-List
