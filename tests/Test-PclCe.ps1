param(
    [string]$Path = (Join-Path $PSScriptRoot '..\build\LauncherDownloadFix.cmd'),
    [string]$PclCePath = (Join-Path $PSScriptRoot '..\work\pclce-test\PCL2_CE_Release_x64_2.15.1.exe')
)

$ErrorActionPreference = 'Stop'
$Path = [IO.Path]::GetFullPath($Path)
$PclCePath = [IO.Path]::GetFullPath($PclCePath)
if (-not (Test-Path -LiteralPath $PclCePath -PathType Leaf)) { throw "Missing PCL2 CE sample: $PclCePath" }

$tempRoot = Join-Path (Split-Path -Parent $PSScriptRoot) ('work\Test-PclCe-' + [Guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($tempRoot)
$target = Join-Path $tempRoot 'pclce.exe'
Copy-Item -LiteralPath $PclCePath -Destination $target -Force
$originalHash = (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash
$previousErrorAction = $ErrorActionPreference

function Invoke-Launcher([string[]]$Arguments) {
    $saved = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $output = & $Path @Arguments 2>&1 | Out-String
        return [pscustomobject]@{ ExitCode = $LASTEXITCODE; Output = $output }
    } finally {
        $ErrorActionPreference = $saved
    }
}

try {
    $verifyLog = Join-Path $tempRoot 'verify.json'
    $verify = Invoke-Launcher @($target, '--verify', '--quiet', '--log', $verifyLog)
    if ($verify.ExitCode -ne 0) { throw "PCL2 CE verify failed: $($verify.Output)" }
    $verifyData = Get-Content -LiteralPath $verifyLog -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($verifyData.targetProfile -ne 'pclce' -or $verifyData.pclSupported -ne $true) { throw 'PCL2 CE was not recognized as a supported target.' }

    $side = Join-Path $tempRoot 'pclce-unlocked.exe'
    $sideResult = Invoke-Launcher @($target, '--side-by-side', '--output', $side, '--quiet', '--yes')
    if ($sideResult.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $side)) { throw "PCL2 CE side-by-side failed: $($sideResult.Output)" }
    $sideLog = Join-Path $tempRoot 'side.json'
    $sideVerify = Invoke-Launcher @($side, '--verify', '--quiet', '--log', $sideLog)
    if ($sideVerify.ExitCode -ne 0) { throw "PCL2 CE patched verify failed: $($sideVerify.Output)" }
    $sideData = Get-Content -LiteralPath $sideLog -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($sideData.pclState -ne 'patched') { throw 'PCL2 CE patched output was not marked patched.' }

    for ($i = 1; $i -le 3; $i++) {
        $applyLog = Join-Path $tempRoot ("apply-$i.json")
        $apply = Invoke-Launcher @($target, '--quiet', '--yes', '--log', $applyLog)
        if ($apply.ExitCode -ne 0) { throw "PCL2 CE apply $i failed: $($apply.Output)" }
        $restoreLog = Join-Path $tempRoot ("restore-$i.json")
        $restore = Invoke-Launcher @($target, '--restore', '--quiet', '--yes', '--log', $restoreLog)
        if ($restore.ExitCode -ne 0) { throw "PCL2 CE restore $i failed: $($restore.Output)" }
    }

    $restoredHash = (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash
    if ($restoredHash -ne $originalHash) { throw 'PCL2 CE restore did not return the original file.' }
    $backups = @(Get-ChildItem -LiteralPath $tempRoot -Filter 'pclce.exe*.bak' -File)
    if ($backups.Count -gt 2) { throw "Unexpected backup count: $($backups.Count)" }

    [pscustomobject]@{
        Result       = 'PASS'
        Target       = $target
        OriginalHash = $originalHash
        SideOutput   = $side
        Backups      = $backups.Count
    } | Format-List
} finally {
    $ErrorActionPreference = $previousErrorAction
    if (Test-Path -LiteralPath $tempRoot) { Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue }
}
