param(
    [string]$ExePath = (Join-Path $PSScriptRoot '..\build\LauncherDownloadFix.exe'),
    [string]$PclPath,
    [string]$HmclPath
)

$ErrorActionPreference = 'Stop'
$ExePath = [IO.Path]::GetFullPath($ExePath)
if (-not (Test-Path -LiteralPath $ExePath -PathType Leaf)) { throw "Missing EXE: $ExePath" }
if ([string]::IsNullOrWhiteSpace($PclPath)) {
    $parent = Split-Path -Parent $PSScriptRoot
    $candidates = @(Get-ChildItem -LiteralPath $parent -Directory -Filter 'PCL*' -ErrorAction SilentlyContinue | ForEach-Object {
        Get-ChildItem -LiteralPath $_.FullName -Filter 'Plain Craft Launcher 2.exe' -File -Recurse -ErrorAction SilentlyContinue
    })
    if ($candidates.Count -eq 0) { throw 'Missing PCL sample; pass -PclPath.' }
    $PclPath = $candidates[0].FullName
}
$PclPath = [IO.Path]::GetFullPath($PclPath)
$hasHmcl = -not [string]::IsNullOrWhiteSpace($HmclPath)
if ($hasHmcl) { $HmclPath = [IO.Path]::GetFullPath($HmclPath) }

$tempRoot = Join-Path (Split-Path -Parent $PSScriptRoot) ('work\Test-SingleExe-' + [Guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($tempRoot)
$copyExe = Join-Path $tempRoot 'LauncherDownloadFix.exe'
$pclTarget = Join-Path $tempRoot 'pcl2.exe'
$hmclLog = Join-Path $tempRoot 'hmcl.json'
$pclLog = Join-Path $tempRoot 'pcl2.json'
$pclApplyLog = Join-Path $tempRoot 'pcl2-apply.json'
$pclRestoreLog = Join-Path $tempRoot 'pcl2-restore.json'
Copy-Item -LiteralPath $ExePath -Destination $copyExe -Force
Copy-Item -LiteralPath $PclPath -Destination $pclTarget -Force
$originalHash = (Get-FileHash -LiteralPath $pclTarget -Algorithm SHA256).Hash

function Invoke-Exe([string[]]$Arguments) {
    $saved = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $output = & $copyExe @Arguments 2>&1 | Out-String
        return [pscustomobject]@{ ExitCode = $LASTEXITCODE; Output = $output }
    } finally {
        $ErrorActionPreference = $saved
    }
}

try {
    $help = Invoke-Exe @('--help')
    if ($help.ExitCode -ne 0) { throw 'Single EXE --help failed.' }
    $unexpected = @(Get-ChildItem -LiteralPath $tempRoot -File | Where-Object { $_.Name -notin @('LauncherDownloadFix.exe', 'pcl2.exe') })
    if ($unexpected.Count -ne 0) { throw ('Test workspace unexpectedly contains external runtime files: ' + ($unexpected.Name -join ', ')) }

    $pclVerify = Invoke-Exe @($pclTarget, '--verify', '--quiet', '--log', $pclLog)
    if ($pclVerify.ExitCode -ne 0) { throw "PCL2 verify failed: $($pclVerify.Output)" }
    $pclData = Get-Content -LiteralPath $pclLog -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($pclData.targetProfile -ne 'pcl2') { throw 'PCL2 auto-detection failed.' }

    $pclApply = Invoke-Exe @($pclTarget, '--quiet', '--yes', '--log', $pclApplyLog)
    if ($pclApply.ExitCode -ne 0) { throw "PCL2 apply failed: $($pclApply.Output)" }
    $pclPatched = Get-Content -LiteralPath $pclApplyLog -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($pclPatched.pclState -ne 'patched') { throw 'PCL2 apply did not report patched.' }

    $pclRestore = Invoke-Exe @($pclTarget, '--restore', '--quiet', '--yes', '--log', $pclRestoreLog)
    if ($pclRestore.ExitCode -ne 0) { throw "PCL2 restore failed: $($pclRestore.Output)" }
    $restoredHash = (Get-FileHash -LiteralPath $pclTarget -Algorithm SHA256).Hash
    if ($restoredHash -ne $originalHash) { throw 'PCL2 restore hash mismatch.' }

    if ($hasHmcl -and (Test-Path -LiteralPath $HmclPath -PathType Leaf)) {
        $hmclVerify = Invoke-Exe @($HmclPath, '--verify', '--quiet', '--log', $hmclLog)
        if ($hmclVerify.ExitCode -ne 0) { throw "HMCL verify failed: $($hmclVerify.Output)" }
        $hmclData = Get-Content -LiteralPath $hmclLog -Raw -Encoding UTF8 | ConvertFrom-Json
        if ($hmclData.targetProfile -ne 'hmcl') { throw 'HMCL auto-detection failed.' }
    }

    [pscustomobject]@{
        Result = 'PASS'
        Exe = $copyExe
        ExternalDependencies = 0
        PclProfile = $pclData.targetProfile
        PclRestored = ($restoredHash -eq $originalHash)
        HmclProfile = if (Test-Path -LiteralPath $hmclLog) { (Get-Content -LiteralPath $hmclLog -Raw -Encoding UTF8 | ConvertFrom-Json).targetProfile } else { 'not-run' }
    } | Format-List
} finally {
    if (Test-Path -LiteralPath $tempRoot) { Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue }
}
