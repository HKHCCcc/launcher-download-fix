param(
    [string]$ExePath = (Join-Path $PSScriptRoot '..\build\LauncherDownloadFix.exe'),
    [string]$PclPath
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

$tempRoot = Join-Path (Split-Path -Parent $PSScriptRoot) ('work\Test-NoBackup-' + [Guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($tempRoot)
$target = Join-Path $tempRoot 'pcl2.exe'
$applyLog = Join-Path $tempRoot 'apply.json'
$restoreLog = Join-Path $tempRoot 'restore.json'

function Invoke-Exe([string[]]$Arguments) {
    $saved = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $output = & $ExePath @Arguments 2>&1 | Out-String
        return [pscustomobject]@{ ExitCode = $LASTEXITCODE; Output = $output }
    } finally {
        $ErrorActionPreference = $saved
    }
}

try {
    Copy-Item -LiteralPath $PclPath -Destination $target -Force
    $apply = Invoke-Exe @($target, '--no-backup', '--yes', '--quiet', '--log', $applyLog)
    if ($apply.ExitCode -ne 0) { throw "No-backup apply failed: $($apply.Output)" }
    if (Test-Path -LiteralPath ($target + '.bak')) { throw 'No-backup apply created a .bak file.' }
    if (@(Get-ChildItem -LiteralPath $tempRoot -File -Filter '*.rollback.tmp').Count -ne 0) {
        throw 'No-backup apply left a temporary rollback file.'
    }

    $data = Get-Content -LiteralPath $applyLog -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($data.noBackup -ne $true) { throw 'Apply log did not record noBackup.' }
    if ($data.pclState -ne 'patched') { throw 'No-backup apply did not report patched.' }

    $restore = Invoke-Exe @($target, '--restore', '--yes', '--quiet', '--log', $restoreLog)
    if ($restore.ExitCode -eq 0) { throw 'Restore unexpectedly succeeded without a backup.' }

    [pscustomobject]@{
        Result = 'PASS'
        NoBackupLog = $data.noBackup
        BackupCreated = $false
        RestoreWithoutBackup = 'rejected'
    } | Format-List
} finally {
    if (Test-Path -LiteralPath $tempRoot) {
        Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}
