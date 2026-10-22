param(
    [string]$Path = (Join-Path $PSScriptRoot '..\build\LauncherDownloadFix.cmd'),
    [string]$PclPath
)

$ErrorActionPreference = 'Stop'
$Path = [IO.Path]::GetFullPath($Path)
if ([string]::IsNullOrWhiteSpace($PclPath)) {
    $parent = Split-Path -Parent $PSScriptRoot
    $candidates = @(Get-ChildItem -LiteralPath $parent -Directory -Filter 'PCL*' -ErrorAction SilentlyContinue | ForEach-Object {
        Get-ChildItem -LiteralPath $_.FullName -Filter 'Plain Craft Launcher 2.exe' -File -Recurse -ErrorAction SilentlyContinue
    })
    if ($candidates.Count -eq 0) { throw 'Missing PCL sample; pass -PclPath explicitly.' }
    $PclPath = $candidates[0].FullName
}
$PclPath = [IO.Path]::GetFullPath($PclPath)
if (-not (Test-Path -LiteralPath $PclPath -PathType Leaf)) { throw "Missing PCL sample: $PclPath" }

$root = Split-Path -Parent $Path
$tempRoot = Join-Path (Split-Path -Parent $PSScriptRoot) ('work\Test-Tamper-' + [Guid]::NewGuid().ToString('N'))
$tempBuild = Join-Path $tempRoot 'build'
$tempTarget = Join-Path $tempRoot 'target.exe'
$tempLog = Join-Path $tempRoot 'result.json'
[void][IO.Directory]::CreateDirectory($tempRoot)
try {
    Copy-Item -LiteralPath $root -Destination $tempBuild -Recurse -Force
    Copy-Item -LiteralPath $PclPath -Destination $tempTarget -Force
    $beforeHash = (Get-FileHash -LiteralPath $tempTarget -Algorithm SHA256).Hash
    $patchers = Join-Path $tempBuild 'src\patcher.cs'
    [IO.File]::AppendAllText($patchers, "`r`n// tamper test`r`n", (New-Object Text.UTF8Encoding($false)))

    $launcher = Join-Path $tempBuild 'LauncherDownloadFix.cmd'
    $previousErrorAction = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $output = & $launcher $tempTarget '--verify' '--log' $tempLog 2>&1 | Out-String
        $exitCode = $LASTEXITCODE
    } finally {
        $ErrorActionPreference = $previousErrorAction
    }
    if ($exitCode -eq 0) { throw "Tampered payload was accepted. Output: $output" }
    $afterHash = (Get-FileHash -LiteralPath $tempTarget -Algorithm SHA256).Hash
    if ($beforeHash -ne $afterHash) { throw 'Tampered verification modified the target.' }
    if (Test-Path -LiteralPath $tempLog) {
        $log = Get-Content -LiteralPath $tempLog -Raw -Encoding UTF8 | ConvertFrom-Json
        if ([string]::IsNullOrWhiteSpace([string]$log.error)) { throw 'Tamper test did not record an error.' }
    }

    [pscustomobject]@{
        Result   = 'PASS'
        ExitCode = $exitCode
        Error    = if (Test-Path -LiteralPath $tempLog) { (Get-Content -LiteralPath $tempLog -Raw -Encoding UTF8 | ConvertFrom-Json).error } else { 'no log' }
        Target   = $tempTarget
    } | Format-List
} finally {
    if (Test-Path -LiteralPath $tempRoot) { Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue }
}
