[CmdletBinding()]
param(
    [string]$OutputPath
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $root 'build\LauncherDownloadFix.exe'
}
$OutputPath = [IO.Path]::GetFullPath($OutputPath)
$outDir = Split-Path -Parent $OutputPath
if (-not (Test-Path -LiteralPath $outDir)) { [void][IO.Directory]::CreateDirectory($outDir) }

$cscCandidates = @(
    (Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'),
    (Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe')
)
$csc = $cscCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (-not $csc) { throw 'csc.exe not found' }

$framework = Split-Path -Parent $csc
$cecil = Join-Path $root 'lib\Mono.Cecil.dll'
$license = Join-Path $root 'src\CECIL-LICENSE.txt'
$icon = Join-Path $root 'src\Host\Assets\app.ico'
$avatar = Join-Path $root 'src\Host\Assets\avatar.png'
$audio = Join-Path $root 'src\Host\Assets\chiptune.wav'
$sources = @(
    (Join-Path $root 'src\patcher.cs'),
    (Join-Path $root 'src\HMCLPatch.cs')
) + @(Get-ChildItem (Join-Path $root 'src\Host') -Filter '*.cs' | Select-Object -ExpandProperty FullName)
foreach ($source in $sources) {
    if (-not (Test-Path -LiteralPath $source)) { throw ("Missing source: " + $source) }
}
if (-not (Test-Path -LiteralPath $cecil)) { throw "Missing Mono.Cecil" }
if (-not (Test-Path -LiteralPath $icon)) { throw "Missing app icon" }
if (-not (Test-Path -LiteralPath $avatar)) { throw "Missing avatar image" }
if (-not (Test-Path -LiteralPath $audio)) { throw "Missing chiptune audio" }

$references = @(
    (Join-Path $framework 'System.dll'),
    (Join-Path $framework 'System.Core.dll'),
    (Join-Path $framework 'System.Xml.dll'),
    (Join-Path $framework 'System.Windows.Forms.dll'),
    (Join-Path $framework 'System.Drawing.dll'),
    (Join-Path $framework 'System.IO.Compression.dll'),
    (Join-Path $framework 'System.IO.Compression.FileSystem.dll'),
    $cecil
)
foreach ($reference in $references) {
    if (-not (Test-Path -LiteralPath $reference)) { throw ("Missing reference: " + $reference) }
}

$tempResources = Join-Path ([IO.Path]::GetTempPath()) ('ldf-resources-' + [Guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($tempResources)
$cecilGz = Join-Path $tempResources 'Mono.Cecil.dll.gz'
$audioGz = Join-Path $tempResources 'chiptune.wav.gz'
try {
    function Compress-GzipResource {
        param([string]$Source, [string]$Destination)
        $sourceStream = [IO.File]::OpenRead($Source)
        $destinationStream = [IO.File]::Create($Destination)
        $gzip = New-Object IO.Compression.GZipStream($destinationStream, [IO.Compression.CompressionLevel]::Optimal, $false)
        try { $sourceStream.CopyTo($gzip) }
        finally {
            $gzip.Dispose()
            $destinationStream.Dispose()
            $sourceStream.Dispose()
        }
    }
    Compress-GzipResource -Source $cecil -Destination $cecilGz
    Compress-GzipResource -Source $audio -Destination $audioGz

    $arguments = @(
        '/nologo',
        '/target:exe',
        '/platform:anycpu',
        '/optimize+',
        '/debug-',
        ('/out:"' + $OutputPath + '"'),
        ('/win32icon:"' + $icon + '"'),
        ('/resource:"' + $cecilGz + '",Mono.Cecil.dll.gz'),
        ('/resource:"' + $license + '",CECIL-LICENSE.txt'),
        ('/resource:"' + $avatar + '",PclPatchHost.avatar.png'),
        ('/resource:"' + $audioGz + '",PclPatchHost.chiptune.wav.gz')
    )
    foreach ($reference in $references) { $arguments += ('/reference:"' + $reference + '"') }
    $arguments += $sources

    & $csc @arguments
    if ($LASTEXITCODE -ne 0) { throw ("csc failed: " + $LASTEXITCODE) }
}
finally {
    if (Test-Path -LiteralPath $tempResources) {
        Remove-Item -LiteralPath $tempResources -Recurse -Force
    }
}

$file = Get-Item -LiteralPath $OutputPath
$sha = (Get-FileHash -LiteralPath $OutputPath -Algorithm SHA256).Hash.ToLowerInvariant()
[pscustomobject]@{
    Output = $file.FullName
    Size   = $file.Length
    SHA256 = $sha
    Compiler = $csc
} | Format-List
