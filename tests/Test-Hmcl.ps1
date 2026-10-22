param(
    [string]$ExePath,
    [Parameter(Mandatory = $true)][string]$HmclPath
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($ExePath)) {
    $ExePath = Join-Path $PSScriptRoot '..\build\LauncherDownloadFix.exe'
}
$ExePath = [IO.Path]::GetFullPath($ExePath)
$HmclPath = [IO.Path]::GetFullPath($HmclPath)
if (-not (Test-Path -LiteralPath $ExePath -PathType Leaf)) { throw "Missing EXE: $ExePath" }
if (-not (Test-Path -LiteralPath $HmclPath -PathType Leaf)) { throw "Missing HMCL: $HmclPath" }

$tempRoot = Join-Path (Split-Path -Parent $PSScriptRoot) ('work\Test-Hmcl-' + [Guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($tempRoot)
$extension = [IO.Path]::GetExtension($HmclPath)
if ([string]::IsNullOrWhiteSpace($extension)) { $extension = '.jar' }
$original = Join-Path $tempRoot ('hmcl-original' + $extension)
$sideBySide = Join-Path $tempRoot ('hmcl-patched' + $extension)
$inPlace = Join-Path $tempRoot ('hmcl-inplace' + $extension)
$verifyLog = Join-Path $tempRoot 'verify.json'
$sideLog = Join-Path $tempRoot 'side-by-side.json'
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

function Read-Json([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path)) { throw "Missing log: $Path" }
    return Get-Content -LiteralPath $Path -Raw -Encoding UTF8 | ConvertFrom-Json
}

try {
    Copy-Item -LiteralPath $HmclPath -Destination $original -Force
    $originalHash = (Get-FileHash -LiteralPath $original -Algorithm SHA256).Hash

    $verify = Invoke-Exe @($original, '--verify', '--quiet', '--log', $verifyLog)
    if ($verify.ExitCode -ne 0) { throw "Verify failed: $($verify.Output)" }
    $verifyData = Read-Json $verifyLog
    if ($verifyData.pclState -ne 'patchable') { throw "Expected patchable HMCL, got $($verifyData.pclState)." }

    $side = Invoke-Exe @($original, '--side-by-side', '--output', $sideBySide, '--yes', '--quiet', '--log', $sideLog)
    if ($side.ExitCode -ne 0) { throw "Side-by-side apply failed: $($side.Output)" }
    if (-not (Test-Path -LiteralPath $sideBySide)) { throw 'Side-by-side output missing.' }
    $sideData = Read-Json $sideLog
    if ($sideData.pclState -ne 'patched') { throw 'Side-by-side output did not report patched.' }

    $sideVerify = Invoke-Exe @($sideBySide, '--verify', '--quiet')
    if ($sideVerify.ExitCode -ne 0) { throw "Patched output verify failed: $($sideVerify.Output)" }

    Copy-Item -LiteralPath $HmclPath -Destination $inPlace -Force
    $apply = Invoke-Exe @($inPlace, '--yes', '--quiet', '--log', $applyLog)
    if ($apply.ExitCode -ne 0) { throw "In-place apply failed: $($apply.Output)" }
    $backup = $inPlace + '.bak'
    if (-not (Test-Path -LiteralPath $backup)) { throw 'In-place apply did not create a backup.' }
    $applyData = Read-Json $applyLog
    if ($applyData.pclState -ne 'patched') { throw 'In-place apply did not report patched.' }

    $restore = Invoke-Exe @($inPlace, '--restore', '--yes', '--quiet', '--log', $restoreLog)
    if ($restore.ExitCode -ne 0) { throw "Restore failed: $($restore.Output)" }
    $restoredHash = (Get-FileHash -LiteralPath $inPlace -Algorithm SHA256).Hash
    if ($restoredHash -ne $originalHash) { throw 'Restore did not reproduce the original HMCL hash.' }

    [pscustomobject]@{
        Result = 'PASS'
        Version = $verifyData.pclVersion
        Input = $HmclPath
        OriginalHash = $originalHash
        PatchedHash = $applyData.targetAfterHash
        Restored = ($restoredHash -eq $originalHash)
        SideBySide = (Test-Path -LiteralPath $sideBySide)
    } | Format-List
}
finally {
    if (Test-Path -LiteralPath $tempRoot) {
        Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}
