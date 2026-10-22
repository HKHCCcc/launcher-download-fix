[CmdletBinding()]
param(
    [Parameter(Position = 0)][string]$InputFile,
    [Parameter(Position = 1)][string]$OutputFile,
    [switch]$Quiet,
    [switch]$VerifyOnly,
    [switch]$SideBySide,
    [switch]$RestoreOnly,
    [switch]$Yes,
    [switch]$NoColor,
    [switch]$Gui,
    [switch]$PruneBackups,
    [int]$Keep = 1,
    [string]$LogPath,
    [string]$BackupPath
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$script:ScriptRoot = if (-not [string]::IsNullOrWhiteSpace($PSScriptRoot)) {
    [IO.Path]::GetFullPath($PSScriptRoot)
} elseif (-not [string]::IsNullOrWhiteSpace($env:PCL_PATCH_HOME)) {
    [IO.Path]::GetFullPath($env:PCL_PATCH_HOME)
} elseif (-not [string]::IsNullOrWhiteSpace($env:PCL_PATCH_SELF)) {
    Split-Path -Parent ([IO.Path]::GetFullPath($env:PCL_PATCH_SELF))
} else {
    (Get-Location).Path
}
$script:ExternalLayout = Test-Path -LiteralPath (Join-Path $script:ScriptRoot 'payload.json') -PathType Leaf
if ([Console]::IsOutputRedirected) {
    try { [Console]::OutputEncoding = [Text.Encoding]::UTF8 } catch { }
}
if (-not [string]::IsNullOrWhiteSpace($env:NO_COLOR)) { $NoColor = $true }

function Write-Ui {
    param([string]$Text = '', [string]$Color = 'Gray')
    if ($NoColor -or [Console]::IsOutputRedirected) { Write-Output $Text }
    else { Write-Host $Text -ForegroundColor $Color }
}

function Write-UiError {
    param([string]$Text)
    if ($NoColor -or [Console]::IsOutputRedirected) { [Console]::Error.WriteLine($Text) }
    else { Write-Host $Text -ForegroundColor Red }
}

function Show-Usage {
    Write-Ui 'Launcher Download Fix 1.4'
    Write-Ui ''
    Write-Ui '用法：'
    Write-Ui '  LauncherDownloadFix.cmd "PCL.exe"'
    Write-Ui '  LauncherDownloadFix.cmd "PCL.exe" --verify'
    Write-Ui '  LauncherDownloadFix.cmd "HMCL.jar" --verify'
    Write-Ui '  LauncherDownloadFix.cmd "PCL.exe" --side-by-side [--output "out.exe"]'
    Write-Ui '  LauncherDownloadFix.cmd "HMCL.jar" --side-by-side --output "HMCL-unlocked.jar"'
    Write-Ui '  LauncherDownloadFix.cmd "PCL.exe" --restore [--backup "PCL.exe.bak"]'
    Write-Ui '  LauncherDownloadFix.cmd "PCL.exe" --prune-backups [--keep 1] --yes'
    Write-Ui '  LauncherDownloadFix.cmd "PCL.exe" --quiet --yes'
    Write-Ui ''
    Write-Ui '支持 PCL2 / PCL2 CE / HMCL；目标类型按内容自动识别。'
    Write-Ui '选项：--verify --side-by-side --restore --prune-backups --keep --quiet --yes --no-color --output --log --backup'
}

function Split-CommandLine {
    param([string]$Text)
    $result = New-Object System.Collections.Generic.List[string]
    $current = New-Object System.Text.StringBuilder
    $inQuotes = $false
    foreach ($ch in $Text.ToCharArray()) {
        if ($ch -eq '"') { $inQuotes = -not $inQuotes; continue }
        if (-not $inQuotes -and [char]::IsWhiteSpace($ch)) {
            if ($current.Length -gt 0) { $result.Add($current.ToString()); [void]$current.Clear() }
        } else {
            [void]$current.Append($ch)
        }
    }
    if ($current.Length -gt 0) { $result.Add($current.ToString()) }
    return $result.ToArray()
}

if (-not [string]::IsNullOrWhiteSpace($env:PCL_PATCH_ARGS)) {
    $tokens = @(Split-CommandLine $env:PCL_PATCH_ARGS)
    $positionals = @()
    for ($i = 0; $i -lt $tokens.Count; $i++) {
        $t = $tokens[$i]
        if ($t -eq '--verify') { $VerifyOnly = $true }
        elseif ($t -eq '--side-by-side') { $SideBySide = $true }
        elseif ($t -eq '--restore') { $RestoreOnly = $true }
        elseif ($t -eq '--prune-backups') { $PruneBackups = $true }
        elseif ($t -eq '--quiet') { $Quiet = $true }
        elseif ($t -eq '--yes') { $Yes = $true }
        elseif ($t -eq '--gui') { $Gui = $true }
        elseif ($t -eq '--no-color') { $NoColor = $true }
        elseif ($t -eq '--help' -or $t -eq '-h' -or $t -eq '/?') { Show-Usage; return }
        elseif ($t -eq '--output' -or $t -eq '-o') {
            $i++
            if ($i -ge $tokens.Count) { throw '--output 缺少路径。' }
            $OutputFile = $tokens[$i]
        }
        elseif ($t -eq '--log') {
            $i++
            if ($i -ge $tokens.Count) { throw '--log 缺少路径。' }
            $LogPath = $tokens[$i]
        }
        elseif ($t -eq '--backup') {
            $i++
            if ($i -ge $tokens.Count) { throw '--backup 缺少路径。' }
            $BackupPath = $tokens[$i]
        }
        elseif ($t -eq '--keep') {
            $i++
            if ($i -ge $tokens.Count) { throw '--keep 缺少数量。' }
            $Keep = [int]$tokens[$i]
        }
        elseif ($t.StartsWith('-')) { throw "未知参数：$t" }
        else { $positionals += $t }
    }
    if ($positionals.Count -ge 1) { $InputFile = $positionals[0] }
    if ($positionals.Count -ge 2) {
        if ([string]::IsNullOrWhiteSpace($OutputFile)) { $OutputFile = $positionals[1] }
        else { throw '输出路径重复。' }
    }
    if ($positionals.Count -gt 2) { throw '参数过多。' }
}

if (-not $Quiet -and -not [Console]::IsOutputRedirected) {
    try { $Host.UI.RawUI.WindowTitle = 'Launcher Download Fix' } catch { }
}

if ([string]::IsNullOrWhiteSpace($InputFile) -and -not [string]::IsNullOrWhiteSpace($env:PCL_PATCH_INPUT)) { $InputFile = $env:PCL_PATCH_INPUT }
if ([string]::IsNullOrWhiteSpace($OutputFile) -and -not [string]::IsNullOrWhiteSpace($env:PCL_PATCH_OUTPUT)) { $OutputFile = $env:PCL_PATCH_OUTPUT }
if ([string]::IsNullOrWhiteSpace($LogPath) -and -not [string]::IsNullOrWhiteSpace($env:PCL_PATCH_LOG)) { $LogPath = $env:PCL_PATCH_LOG }

if ($RestoreOnly -and ($VerifyOnly -or $SideBySide -or $PruneBackups)) { throw '--restore 不能和 --verify / --side-by-side / --prune-backups 同时使用。' }
if ($PruneBackups -and ($VerifyOnly -or $SideBySide)) { throw '--prune-backups 不能和 --verify / --side-by-side 同时使用。' }
if ($Gui -and ($VerifyOnly -or $SideBySide -or $RestoreOnly -or $PruneBackups)) { throw '--gui 不能和 --verify / --side-by-side / --restore / --prune-backups 同时使用。' }
if ($VerifyOnly -and $SideBySide) { throw '不能同时使用 --verify 和 --side-by-side。' }
if ($RestoreOnly -and -not [string]::IsNullOrWhiteSpace($OutputFile)) { throw '--restore 不接受 --output。' }
if ($Keep -lt 0) { throw '--keep 不能小于 0。' }
if (-not $Gui -and [string]::IsNullOrWhiteSpace($env:PCL_PATCH_ARGS) -and [string]::IsNullOrWhiteSpace($InputFile) -and -not $VerifyOnly -and -not $SideBySide -and -not $RestoreOnly -and -not $PruneBackups) {
    $Gui = $true
}

$inPlace = -not $VerifyOnly -and -not $SideBySide -and -not $RestoreOnly -and -not $PruneBackups
$script:TaskFailed = $false
$script:TaskCancelled = $false
$script:TaskWorkspace = $null
$script:TaskTempOut = $null
$script:TaskTempTarget = $null
$script:TaskBackup = $null
$script:TaskError = $null
$script:StepIndex = 0
$script:StepTotal = if ($PruneBackups) { 1 } elseif ($VerifyOnly) { 2 } elseif ($RestoreOnly) { 3 } elseif ($SideBySide) { 3 } else { 4 }
$script:StepWatch = [Diagnostics.Stopwatch]::StartNew()
$script:Stopwatch = [Diagnostics.Stopwatch]::StartNew()
$script:TaskLog = [ordered]@{
    tool             = 'LauncherDownloadFix'
    time             = (Get-Date).ToUniversalTime().ToString('o')
    profile          = 'PCL structural (2.99+)'
    targetProfile    = 'unknown'
    mode             = $null
    input            = $null
    output           = $null
    backup           = $null
    reusedBackup     = $false
    prunedBackups    = 0
    keptBackups      = 0
    verifyOnly       = [bool]$VerifyOnly
    restoreOnly      = [bool]$RestoreOnly
    payloadSha256    = $null
    cecilSha256      = $null
    pclVersion       = $null
    pclVersionCode   = $null
    pclState         = $null
    pclPatched       = $false
    pclSupported     = $false
    pclAssemblyVersion = $null
    pclMarkerCount   = 0
    pclMarkers       = @()
    targetBeforeHash = $null
    targetAfterHash  = $null
    backupHash       = $null
    methods          = @()
    warnings         = @()
    elapsedMs        = $null
    result           = 'running'
    error            = $null
}

function Format-Size {
    param([long]$Bytes)
    if ($Bytes -le 0) { return '0 B' }
    if ($Bytes -ge 1MB) { return ('{0:N2} MB' -f ($Bytes / 1MB)) }
    return ('{0:N0} KB' -f ($Bytes / 1KB))
}

function Format-Hash {
    param([string]$Hash)
    if ([string]::IsNullOrWhiteSpace($Hash)) { return '' }
    if ($Hash.Length -gt 16) { return $Hash.Substring(0, 16) + '...' }
    return $Hash
}

function Write-Summary {
    param([string]$Title, [string[]]$Lines, [string]$Color = 'Green')
    if ($Quiet) { return }
    Write-Ui ''
    Write-Ui '============================================' DarkCyan
    Write-Ui (" {0}" -f $Title) $Color
    foreach ($line in $Lines) {
        if ($null -ne $line) { Write-Ui (" {0}" -f $line) }
    }
    Write-Ui '============================================' DarkCyan
}

function Write-Banner {
    param([string]$Target, [string]$Mode)
    if ($Quiet) { return }
    $size = if (Test-Path -LiteralPath $Target) { (Get-Item -LiteralPath $Target).Length } else { 0 }
    Write-Ui '============================================' DarkCyan
    $profileText = if ($script:TaskLog.targetProfile -eq 'pclce') { 'PCL2 CE bundle' } elseif ($script:TaskLog.targetProfile -eq 'pcl2') { 'PCL structural (2.99+)' } elseif ($script:TaskLog.targetProfile -eq 'hmcl') { 'HMCL classfile' } else { 'auto (PCL2 / PCL2 CE / HMCL)' }
    Write-Ui ' Launcher Download Fix 1.4' Cyan
    Write-Ui (" profile: {0}" -f $profileText) DarkCyan
    Write-Ui '============================================' DarkCyan
    Write-Ui ("目标：{0}" -f $Target)
    Write-Ui ("大小：{0}" -f (Format-Size $size))
    Write-Ui ("模式：{0}" -f $Mode)
    if ($script:TaskBackup) { Write-Ui ("备份：{0}" -f $script:TaskBackup) }
    Write-Ui ''
}

function Write-Step {
    param([string]$Label, [string]$Status = 'OK', [string]$Color = 'Green')
    if ($Quiet) { return }
    $script:StepIndex++
    $elapsed = $script:StepWatch.Elapsed.TotalSeconds
    $script:StepWatch.Restart()
    Write-Ui ("[{0}/{1}] {2} ... {3}  ({4:N1}s)" -f $script:StepIndex, $script:StepTotal, $Label, $Status, $elapsed) $Color
}

function Get-Sha256Hex {
    param([byte[]]$Bytes)
    $sha = [Security.Cryptography.SHA256]::Create()
    try { return ([BitConverter]::ToString($sha.ComputeHash($Bytes)) -replace '-', '').ToLowerInvariant() } finally { $sha.Dispose() }
}

function Get-FileSha256Hex {
    param([string]$Path)
    $sha = [Security.Cryptography.SHA256]::Create()
    $fs = $null
    try {
        $fs = [IO.File]::Open($Path, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
        return ([BitConverter]::ToString($sha.ComputeHash($fs)) -replace '-', '').ToLowerInvariant()
    } finally {
        if ($fs) { $fs.Dispose() }
        $sha.Dispose()
    }
}

function Get-PclCeInfo {
    param([string]$Path)
    if ([string]::IsNullOrWhiteSpace($Path) -or -not (Test-Path -LiteralPath $Path -PathType Leaf)) { return $null }
    try {
        $info = [PclCePatchBuilder]::Verify($Path)
        if ($info -and $info.IsPclCe) { return $info }
    } catch { }
    return $null
}

function Get-Pcl2Info {
    param([string]$Path)
    if ([string]::IsNullOrWhiteSpace($Path) -or -not (Test-Path -LiteralPath $Path -PathType Leaf)) { return $null }
    try {
        $info = [PclDynamicPatchBuilder]::Verify($Path)
        if ($info -and ($info.Supported -or $info.AssemblyName -eq 'Plain Craft Launcher 2')) { return $info }
    } catch { }
    return $null
}

function Get-HmclInfo {
    param([string]$Path, [switch]$AllowAnyExtension)
    if ([string]::IsNullOrWhiteSpace($Path) -or -not (Test-Path -LiteralPath $Path -PathType Leaf)) { return $null }
    if (-not $AllowAnyExtension) {
        $ext = [IO.Path]::GetExtension($Path)
        if ($ext -notin @('.jar', '.exe', '.sh')) { return $null }
    }
    try {
        $info = [HMCLPatchCore]::Inspect($Path)
        if ($info -and ($info.State -in @('patchable', 'patched', 'no_gate') -or -not [string]::IsNullOrWhiteSpace([string]$info.Version) -or $info.Signed)) {
            return $info
        }
    } catch { }
    return $null
}

function Test-OriginalPcl {
    param([string]$Path)
    $asm = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($Path)
    try {
        if ($asm.Name.Name -ne 'Plain Craft Launcher 2') { return $false }
        $type = $asm.MainModule.Types | Where-Object { $_.FullName -eq 'PCL.ModSecret' } | Select-Object -First 1
        if (-not $type) { return $false }
        $flag = $type.Fields | Where-Object { $_.Name -eq 'RestrictMcDownload' } | Select-Object -First 1
        return ($null -ne $flag -and $flag.HasConstant -and [bool]$flag.Constant)
    } finally {
        $asm.Dispose()
    }
}

function Get-BackupCandidates {
    param([string]$Target)
    if ([string]::IsNullOrWhiteSpace($Target)) { return @() }
    $default = $Target + '.bak'
    $leaf = [IO.Path]::GetFileName($Target)
    $dir = [IO.Path]::GetDirectoryName($Target)
    $files = @()
    if (Test-Path -LiteralPath $default) { $files += Get-Item -LiteralPath $default }
    $files += @(Get-ChildItem -LiteralPath $dir -Filter ($leaf + '.*.bak') -File -ErrorAction SilentlyContinue)
    return @($files | Sort-Object LastWriteTimeUtc -Descending)
}

function Find-MatchingBackup {
    param([string]$Target, [string]$TargetHash)
    if ([string]::IsNullOrWhiteSpace($TargetHash)) { return $null }
    foreach ($file in @(Get-BackupCandidates -Target $Target)) {
        try {
            if ($file.Length -ne (Get-Item -LiteralPath $Target).Length) { continue }
            if ((Get-FileSha256Hex $file.FullName) -eq $TargetHash) {
                return $file.FullName
            }
        } catch { }
    }
    return $null
}

function Get-BackupPath {
    param([string]$Target)
    $base = $Target + '.bak'
    if (-not (Test-Path -LiteralPath $base)) { return $base }
    $stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
    $candidate = $Target + '.' + $stamp + '.bak'
    $i = 1
    while (Test-Path -LiteralPath $candidate) {
        $candidate = $Target + '.' + $stamp + '-' + $i + '.bak'
        $i++
    }
    return $candidate
}

function Test-TargetWritable {
    param([string]$Path)
    try {
        $fs = [IO.File]::Open($Path, [IO.FileMode]::Open, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
        $fs.Dispose()
        return $true
    } catch {
        return $false
    }
}

function Invoke-ReplaceWithBackup {
    param([string]$Target, [string]$Patched, [string]$Backup, [switch]$ReuseBackup)
    if ($ReuseBackup) {
        if (-not (Test-Path -LiteralPath $Backup)) { throw "已有备份不存在：$Backup" }
    } elseif (Test-Path -LiteralPath $Backup) {
        throw "备份文件已存在：$Backup"
    }
    try {
        if ($ReuseBackup) {
            [IO.File]::Replace($Patched, $Target, $Backup, $true)
            return 'replace-reuse'
        }
        [IO.File]::Replace($Patched, $Target, $Backup, $true)
        return 'replace'
    } catch {
        $replaceError = $_.Exception.Message
        try {
            if ($ReuseBackup) {
                $swap = Join-Path ([IO.Path]::GetDirectoryName($Target)) ('.' + [IO.Path]::GetFileName($Target) + '.' + [Guid]::NewGuid().ToString('N') + '.swap.tmp')
                [IO.File]::Move($Target, $swap)
                try {
                    [IO.File]::Move($Patched, $Target)
                } catch {
                    [IO.File]::Move($swap, $Target)
                    throw
                }
                Remove-Item -LiteralPath $swap -Force -ErrorAction SilentlyContinue
                return 'move-reuse'
            }
            [IO.File]::Move($Target, $Backup)
            try {
                [IO.File]::Move($Patched, $Target)
                return 'move'
            } catch {
                [IO.File]::Move($Backup, $Target)
                throw
            }
        } catch {
            throw "替换失败：$replaceError / $($_.Exception.Message)"
        }
    }
}

function Show-ReplaceConfirm {
    param([string]$Target, [string]$Backup, [string]$Action = '替换')
    $form = New-Object Windows.Forms.Form
    $form.Text = 'Launcher Download Fix'
    $form.ClientSize = New-Object System.Drawing.Size(620, 375)
    $form.StartPosition = 'CenterScreen'
    $form.FormBorderStyle = 'FixedDialog'
    $form.MaximizeBox = $false
    $form.MinimizeBox = $false
    $form.TopMost = $true
    $form.KeyPreview = $true
    $form.Add_KeyDown({
        param($sender, $e)
        if ($e.KeyCode -eq [Windows.Forms.Keys]::Escape) {
            $sender.DialogResult = [Windows.Forms.DialogResult]::No
            $sender.Close()
        }
    })

    $title = New-Object Windows.Forms.Label
    $title.Text = "确认$Action启动器"
    $title.Font = New-Object System.Drawing.Font('Segoe UI', 14, [System.Drawing.FontStyle]::Bold)
    $title.AutoSize = $true
    $title.Location = New-Object System.Drawing.Point(20, 18)
    $form.Controls.Add($title)

    $paths = New-Object Windows.Forms.TextBox
    $paths.Multiline = $true
    $paths.ReadOnly = $true
    $paths.ScrollBars = 'Vertical'
    $paths.Text = "目标：`r`n$Target`r`n`r`n备份：`r`n$Backup"
    $paths.Location = New-Object System.Drawing.Point(22, 55)
    $paths.Size = New-Object System.Drawing.Size(576, 140)
    $form.Controls.Add($paths)

    $detail = New-Object Windows.Forms.Label
    if ($Action -eq '恢复') {
        $detail.Text = '将用备份恢复原版 PCL，当前修补版会被覆盖，备份会保留。'
    } else {
        $detail.Text = '将恢复：游戏本体下载/安装、整合包导入/安装、版本修改页 MC 卡片、资源详情页整合包安装。'
    }
    $detail.AutoSize = $false
    $detail.Size = New-Object System.Drawing.Size(576, 36)
    $detail.Location = New-Object System.Drawing.Point(22, 203)
    $form.Controls.Add($detail)

    $warn = New-Object Windows.Forms.Label
    $warn.Text = '请确认 PCL 已经完全退出。'
    $warn.ForeColor = [System.Drawing.Color]::FromArgb(180, 80, 0)
    $warn.AutoSize = $true
    $warn.Location = New-Object System.Drawing.Point(22, 248)
    $form.Controls.Add($warn)

    $btnReplace = New-Object Windows.Forms.Button
    $btnReplace.Text = $Action
    $btnReplace.Size = New-Object System.Drawing.Size(100, 32)
    $btnReplace.Location = New-Object System.Drawing.Point(390, 305)
    $btnReplace.DialogResult = [Windows.Forms.DialogResult]::Yes
    $form.Controls.Add($btnReplace)

    $btnCancel = New-Object Windows.Forms.Button
    $btnCancel.Text = '取消'
    $btnCancel.Size = New-Object System.Drawing.Size(100, 32)
    $btnCancel.Location = New-Object System.Drawing.Point(500, 305)
    $btnCancel.DialogResult = [Windows.Forms.DialogResult]::No
    $form.Controls.Add($btnCancel)

    $form.AcceptButton = $btnCancel
    $form.CancelButton = $btnCancel
    try {
        $result = $form.ShowDialog()
        return ($result -eq [Windows.Forms.DialogResult]::Yes)
    } finally {
        $form.Dispose()
    }
}

function Write-TaskLog {
    if ([string]::IsNullOrWhiteSpace($LogPath)) { return }
    try {
        $full = [IO.Path]::GetFullPath($LogPath)
        $dir = [IO.Path]::GetDirectoryName($full)
        if ($dir -and -not [IO.Directory]::Exists($dir)) { [void][IO.Directory]::CreateDirectory($dir) }
        $json = $script:TaskLog | ConvertTo-Json -Depth 5
        [IO.File]::WriteAllText($full, $json, (New-Object Text.UTF8Encoding($false)))
    } catch {
        Write-Warning "写入日志失败：$($_.Exception.Message)"
    }
}

function Ensure-GuiTarget {
    param([switch]$AutoApply)
    if (-not [string]::IsNullOrWhiteSpace($script:GuiTarget) -and (Test-Path -LiteralPath $script:GuiTarget)) {
        $script:GuiPendingAction = if ($AutoApply) { 'apply' } else { $null }
        Start-GuiAction 'verify'
        return
    }
    $script:GuiTarget = $null
    $script:GuiStatus = '未知'
    $script:GuiBackup = $null
    $script:GuiRestoreBackup = $null
    Update-GuiInfo
    Add-GuiLog '请选择 PCL2、PCL2 CE 或 HMCL 文件。' 'DarkGoldenrod'
    Select-GuiTarget
}

function Add-GuiLog {
    param([string]$Text, [string]$Color = 'Black')
    if (-not $script:GuiLogBox) { return }
    try {
        $script:GuiLogBox.SelectionStart = $script:GuiLogBox.TextLength
        $script:GuiLogBox.SelectionColor = [System.Drawing.Color]::FromName($Color)
        $script:GuiLogBox.AppendText(("[{0}] {1}`r`n" -f (Get-Date -Format 'HH:mm:ss'), $Text))
        $script:GuiLogBox.SelectionColor = [System.Drawing.Color]::Black
        $script:GuiLogBox.ScrollToCaret()
    } catch { }
}

function Set-GuiStatus {
    param([string]$Text, [string]$Color = 'Black')
    if ($script:GuiStatusLabel) {
        $script:GuiStatusLabel.Text = "状态：$Text"
        $script:GuiStatusLabel.ForeColor = [System.Drawing.Color]::FromName($Color)
    }
}

function Get-GuiBackup {
    param([string]$Target)
    if ([string]::IsNullOrWhiteSpace($Target)) { return $null }
    $candidates = @(Get-BackupCandidates -Target $Target)
    if ($candidates.Count -gt 0) { return $candidates[0].FullName }
    return $null
}

function Update-GuiInfo {
    if (-not $script:GuiForm) { return }
    if ([string]::IsNullOrWhiteSpace($script:GuiTarget)) {
        $script:GuiTargetLabel.Text = '未选择'
        $script:GuiFileValue.Text = ''
        $script:GuiPrimary.Text = '应用'
        $script:GuiPrimary.Enabled = (-not $script:GuiBusy)
        return
    }
    $file = [IO.Path]::GetFileName($script:GuiTarget)
    $ver = ''
    if (-not [string]::IsNullOrWhiteSpace($script:GuiVersion)) {
        $ver = ' (' + $script:GuiVersion + ')'
    } else {
        try { $ver = ' (PCL ' + ([Reflection.AssemblyName]::GetAssemblyName($script:GuiTarget)).Version.ToString() + ')' } catch { }
    }
    $script:GuiTargetLabel.Text = "$($script:GuiTarget)$ver"
    $script:GuiFileValue.Text = $file
    $script:GuiAuthorValue.Text = 'DeepSeek'
    $script:GuiDateValue.Text = (Get-Date -Format 'yyyy.MM.dd')
    $script:GuiPrimary.Text = if ($script:GuiStatus -eq '已修补') { '还原' } elseif ($script:GuiStatus -eq '无需处理') { '无需处理' } else { '应用' }
    $canAct = if ($script:GuiStatus -eq '原版' -or $script:GuiStatus -eq '未知') { $true } elseif ($script:GuiStatus -eq '已修补') { [bool]$script:GuiBackup } else { $false }
    $script:GuiPrimary.Enabled = ((-not $script:GuiBusy) -and $canAct)
}

function Set-GuiBusy {
    param([bool]$Busy, [string]$Text)
    $script:GuiBusy = $Busy
    if (-not $script:GuiForm) { return }
    $script:GuiForm.UseWaitCursor = $Busy
    $script:GuiAbout.Enabled = -not $Busy
    $script:GuiExit.Enabled = -not $Busy
    $canAct = if ($script:GuiStatus -eq '原版' -or $script:GuiStatus -eq '未知') { $true } elseif ($script:GuiStatus -eq '已修补') { [bool]$script:GuiBackup } else { $false }
    $script:GuiPrimary.Enabled = ((-not $Busy) -and $canAct)
    if (-not [string]::IsNullOrWhiteSpace($Text)) {
        $color = if ($Busy) { 'DarkGoldenrod' } else { 'Black' }
        Set-GuiStatus -Text $Text -Color $color
    }
}

function Start-GuiAction {
    param([string]$Action)
    if ($script:GuiBusy -or [string]::IsNullOrWhiteSpace($script:GuiTarget)) { return }
    if ([string]::IsNullOrWhiteSpace($env:PCL_PATCH_SELF)) {
        Add-GuiLog '找不到补丁主程序。' 'Red'
        return
    }
    $log = Join-Path ([IO.Path]::GetTempPath()) ('PclPatchGui-' + [Guid]::NewGuid().ToString('N') + '.json')
    $argList = @("`"$($script:GuiTarget)`"")
    switch ($Action) {
        'verify' { $argList += @('--quiet', '--verify', '--log', "`"$log`""); $busyText = '正在校验...' }
        'apply'  { $argList += @('--quiet', '--yes', '--log', "`"$log`""); $busyText = '正在应用补丁...' }
        'restore' {
            $argList += @('--restore', '--quiet', '--yes', '--log', "`"$log`"")
            if ($script:GuiRestoreBackup) { $argList += @('--backup', "`"$($script:GuiRestoreBackup)`"") }
            $busyText = '正在恢复原版...'
        }
        default { return }
    }
    $script:GuiAction = $Action
    $script:GuiLogPath = $log
    Add-GuiLog $busyText 'Blue'
    Set-GuiBusy -Busy $true -Text $busyText
    try {
        $script:GuiProcess = Start-Process -FilePath $env:PCL_PATCH_SELF -ArgumentList $argList -WindowStyle Hidden -PassThru
        $script:GuiTimer.Start()
    } catch {
        Add-GuiLog ("启动失败：" + $_.Exception.Message) 'Red'
        Set-GuiBusy -Busy $false -Text '就绪'
    }
}

function Complete-GuiAction {
    if (-not $script:GuiProcess) {
        if ($script:GuiTimer) { $script:GuiTimer.Stop() }
        return
    }
    if (-not $script:GuiProcess.HasExited) { return }
    $script:GuiTimer.Stop()
    $exitCode = $script:GuiProcess.ExitCode
    try { $script:GuiProcess.Dispose() } catch { }
    $script:GuiProcess = $null
    $action = $script:GuiAction
    $logPath = $script:GuiLogPath
    $script:GuiAction = $null
    $script:GuiLogPath = $null
    $data = $null
    if ($logPath -and (Test-Path -LiteralPath $logPath)) {
        try { $data = Get-Content -LiteralPath $logPath -Raw -Encoding UTF8 | ConvertFrom-Json } catch { }
        Remove-Item -LiteralPath $logPath -Force -ErrorAction SilentlyContinue
    }
    $statusText = '就绪'
    $statusColor = 'Black'
    $autoApply = $false
    $isCe = ($data -and $data.targetProfile -eq 'pclce')
    $markerTotal = if ($isCe) { 0 } else { 5 }
    if ($action -ne 'verify') { $script:GuiPendingAction = $null }
    if ($exitCode -eq 0) {
        switch ($action) {
            'verify' {
                $script:GuiProfile = if ($isCe) { 'pclce' } else { 'pcl2' }
                $script:GuiVersion = if ($data) { [string]$data.pclVersion } else { '' }
                $ver = if ($data -and $data.pclVersion) { [string]$data.pclVersion } else { if ($isCe) { 'PCL2 CE' } else { 'PCL' } }
                $typeLabel = if ($data -and $data.targetProfile -eq 'pclce') { 'PCL2 CE' } elseif ($data -and $data.targetProfile -eq 'hmcl') { 'HMCL' } elseif ($data -and $data.targetProfile -eq 'pcl2') { 'PCL2' } else { '未知' }
                Add-GuiLog ("自动识别：{0}" -f $typeLabel) 'Gray'
                $script:GuiBackup = Get-GuiBackup $script:GuiTarget
                if ($data -and $data.pclPatched) {
                    $script:GuiStatus = '已修补'
                    Add-GuiLog ("检测：$ver，已修补（{0} 个标记）" -f $data.pclMarkerCount) 'DarkOrange'
                    if ($data.pclState -eq 'modified' -or $data.pclState -eq 'partial') { Add-GuiLog '检测到部分修补标记，建议还原后再重试。' 'DarkOrange' }
                    if ($script:GuiBackup) { Add-GuiLog ("原版备份：" + $script:GuiBackup) 'Gray' } else { Add-GuiLog '未找到原版备份，无法自动还原。' 'DarkOrange' }
                    $statusText = '已修补（可还原）'
                    $statusColor = 'DarkOrange'
                } elseif ($data -and $data.pclSupported -eq $false) {
                    $script:GuiStatus = '不支持'
                    Add-GuiLog ("检测：$ver，结构不支持") 'Red'
                    if ($data.error) { Add-GuiLog ("原因：" + $data.error) 'Red' }
                    $statusText = '结构不支持'
                    $statusColor = 'Red'
                } elseif ($data -and $data.pclState -eq 'no_gate') {
                    $script:GuiStatus = '无需处理'
                    Add-GuiLog ("检测：$ver，没有下载限制，无需处理") 'Green'
                    $statusText = '无需处理'
                    $statusColor = 'Green'
                } else {
                    $script:GuiStatus = '原版'
                    Add-GuiLog ("检测：$ver，原版（0 个修补标记）") 'Green'
                    if ($script:GuiBackup) { Add-GuiLog ("原版备份：" + $script:GuiBackup) 'Gray' }
                    $autoApply = ($script:GuiPendingAction -eq 'apply')
                    $statusText = '原版（可应用补丁）'
                    $statusColor = 'Green'
                }
                $script:GuiPendingAction = $null
            }
            'apply' {
                if ($data -and $data.pclState -eq 'no_gate') {
                    $script:GuiStatus = '无需处理'
                    Add-GuiLog '当前版本没有下载限制，无需处理。' 'DarkGoldenrod'
                    $statusText = '无需处理'
                    $statusColor = 'Green'
                } else {
                    $script:GuiStatus = '已修补'
                    $applied = if ($data -and $data.pclMarkerCount) { [int]$data.pclMarkerCount } else { 5 }
                    Add-GuiLog ("补丁已应用（{0} 个修补标记）。" -f $applied) 'Green'
                    if ($data -and $data.backup) { $script:GuiBackup = $data.backup; Add-GuiLog ("原版备份：" + $data.backup) 'Gray' }
                    $statusText = '已修补（可还原）'
                    $statusColor = 'Green'
                }
            }
            'restore' {
                $script:GuiStatus = '原版'
                Add-GuiLog '已恢复原版。' 'Green'
                $statusText = '原版'
                $statusColor = 'Green'
            }
        }
    } else {
        $err = if ($data -and $data.error) { $data.error } else { "子进程退出码 $exitCode" }
        Add-GuiLog ("失败：" + $err) 'Red'
        if ($action -eq 'verify') {
            $script:GuiStatus = '未知'
            $script:GuiPendingAction = $null
            $statusText = '校验失败'
            $statusColor = 'Red'
        } else {
            $statusText = '操作失败'
            $statusColor = 'Red'
        }
    }
    Set-GuiBusy -Busy $false -Text $statusText
    Update-GuiInfo
    if ($autoApply) { Start-GuiAction 'apply' }
}

function Select-GuiTarget {
    if ($script:GuiBusy) { return }
    $script:GuiPendingAction = $null
    $dlg = New-Object Windows.Forms.OpenFileDialog
    $dlg.Title = '选择 PCL2 / PCL2 CE / HMCL'
    $dlg.Filter = '启动器 (*.exe;*.jar;*.sh)|*.exe;*.jar;*.sh|所有文件 (*.*)|*.*'
    $dlg.CheckFileExists = $true
    if ($script:GuiTarget) {
        $dlg.InitialDirectory = [IO.Path]::GetDirectoryName($script:GuiTarget)
    } else {
        $dlg.InitialDirectory = if ($env:PCL_PATCH_SELF) { Split-Path -Parent $env:PCL_PATCH_SELF } else { (Get-Location).Path }
    }
    try {
        if ($dlg.ShowDialog() -ne [Windows.Forms.DialogResult]::OK) {
            $script:GuiPendingAction = $null
            Add-GuiLog '已取消选择。' 'DarkGoldenrod'
            return
        }
        $script:GuiTarget = $dlg.FileName
        $script:GuiStatus = '未知'
        $script:GuiBackup = Get-GuiBackup $script:GuiTarget
        $script:GuiRestoreBackup = $null
        Update-GuiInfo
        Add-GuiLog ("目标：" + $script:GuiTarget) 'Black'
        Add-GuiLog '已手动选择目标；校验完成后请确认状态再继续。' 'DarkGoldenrod'
        Start-GuiAction 'verify'
    } finally {
        $dlg.Dispose()
    }
}

function Show-GuiAbout {
    $about = New-Object Windows.Forms.Form
    $about.Text = '关于'
    $about.ClientSize = New-Object System.Drawing.Size(300, 180)
    $about.FormBorderStyle = 'FixedDialog'
    $about.MaximizeBox = $false
    $about.MinimizeBox = $false
    $about.StartPosition = 'CenterParent'
    $about.Font = New-Object System.Drawing.Font('宋体', 9)
    $about.TopMost = $true
    $label = New-Object Windows.Forms.Label
    $label.Text = "启动器解锁 1.4`r`n作者：DeepSeek`r`nprofile: PCL2 / PCL2 CE / HMCL`r`n构建: $($script:GuiBuildHash)`r`n`r`nPCL2 使用 5 处 IL 改写；PCL2 CE 使用区域策略补丁；HMCL 使用 classfile 补丁。非官方工具。`r`n仅用于个人研究，使用前请确认合规性。"
    $label.Location = New-Object System.Drawing.Point(14, 12)
    $label.Size = New-Object System.Drawing.Size(272, 120)
    $about.Controls.Add($label)
    $ok = New-Object Windows.Forms.Button
    $ok.Text = '确定'
    $ok.Location = New-Object System.Drawing.Point(110, 140)
    $ok.Size = New-Object System.Drawing.Size(80, 28)
    $ok.DialogResult = [Windows.Forms.DialogResult]::OK
    $about.Controls.Add($ok)
    $about.AcceptButton = $ok
    $about.CancelButton = $ok
    try { [void]$about.ShowDialog() } finally { $about.Dispose() }
}

function Show-Gui {
    Add-Type -AssemblyName System.Windows.Forms
    Add-Type -AssemblyName System.Drawing
    [Windows.Forms.Application]::EnableVisualStyles()
    [Windows.Forms.Application]::SetCompatibleTextRenderingDefault($false)

    $script:GuiTarget = $null
    $script:GuiStatus = '未知'
    $script:GuiProfile = 'unknown'
    $script:GuiVersion = ''
    $script:GuiBusy = $false
    $script:GuiProcess = $null
    $script:GuiTimer = $null
    $script:GuiAction = $null
    $script:GuiLogPath = $null
    $script:GuiBackup = $null
    $script:GuiRestoreBackup = $null
    $script:GuiPendingAction = $null
    $script:GuiForm = $null
    $script:GuiBuildHash = if ($env:PCL_PATCH_SELF) { Get-FileSha256Hex $env:PCL_PATCH_SELF } else { '' }
    if (-not [string]::IsNullOrWhiteSpace($InputFile)) {
        try { $script:GuiTarget = (Resolve-Path -LiteralPath $InputFile).ProviderPath } catch { $script:GuiTarget = $InputFile }
    }

    $font = New-Object System.Drawing.Font('宋体', 9)
    $form = New-Object Windows.Forms.Form
    $form.Text = 'PCL2 / PCL2 CE / HMCL 下载限制解锁'
    $form.ClientSize = New-Object System.Drawing.Size(381, 255)
    $form.FormBorderStyle = 'FixedDialog'
    $form.MaximizeBox = $false
    $form.MinimizeBox = $false
    $form.StartPosition = 'CenterScreen'
    $form.Font = $font
    $form.AutoScaleMode = [Windows.Forms.AutoScaleMode]::Font
    try { $form.Icon = [System.Drawing.SystemIcons]::Application } catch { }
    $script:GuiForm = $form

    $targetLabel = New-Object Windows.Forms.Label
    $targetLabel.Location = New-Object System.Drawing.Point(3, 3)
    $targetLabel.Size = New-Object System.Drawing.Size(375, 16)
    $targetLabel.AutoEllipsis = $true
    $targetLabel.Text = '未选择'
    $targetLabel.Cursor = [Windows.Forms.Cursors]::Hand
    $script:GuiTargetLabel = $targetLabel
    $form.Controls.Add($targetLabel)

    $info = New-Object Windows.Forms.GroupBox
    $info.Location = New-Object System.Drawing.Point(3, 16)
    $info.Size = New-Object System.Drawing.Size(375, 90)
    $form.Controls.Add($info)

    $fileTitle = New-Object Windows.Forms.Label
    $fileTitle.Text = '文件名称'; $fileTitle.Location = New-Object System.Drawing.Point(5, 13); $fileTitle.Size = New-Object System.Drawing.Size(60, 16)
    $info.Controls.Add($fileTitle)
    $fileValue = New-Object Windows.Forms.Label
    $fileValue.Text = ''; $fileValue.Location = New-Object System.Drawing.Point(87, 13); $fileValue.Size = New-Object System.Drawing.Size(278, 16); $fileValue.AutoEllipsis = $true
    $fileValue.Cursor = [Windows.Forms.Cursors]::Hand
    $script:GuiFileValue = $fileValue; $info.Controls.Add($fileValue)
    $script:GuiToolTip = New-Object Windows.Forms.ToolTip
    $script:GuiToolTip.SetToolTip($targetLabel, '双击可重新选择 PCL2 / PCL2 CE / HMCL')
    $script:GuiToolTip.SetToolTip($fileValue, '双击可重新选择 PCL2 / PCL2 CE / HMCL')

    $authorTitle = New-Object Windows.Forms.Label
    $authorTitle.Text = '补丁作者'; $authorTitle.Location = New-Object System.Drawing.Point(5, 41); $authorTitle.Size = New-Object System.Drawing.Size(60, 16)
    $info.Controls.Add($authorTitle)
    $authorValue = New-Object Windows.Forms.Label
    $authorValue.Text = 'DeepSeek'; $authorValue.Location = New-Object System.Drawing.Point(87, 41); $authorValue.Size = New-Object System.Drawing.Size(278, 16); $authorValue.AutoEllipsis = $true
    $script:GuiAuthorValue = $authorValue; $info.Controls.Add($authorValue)

    $dateTitle = New-Object Windows.Forms.Label
    $dateTitle.Text = '发布日期'; $dateTitle.Location = New-Object System.Drawing.Point(5, 69); $dateTitle.Size = New-Object System.Drawing.Size(60, 16)
    $info.Controls.Add($dateTitle)
    $dateValue = New-Object Windows.Forms.Label
    $dateValue.Text = (Get-Date -Format 'yyyy.MM.dd'); $dateValue.Location = New-Object System.Drawing.Point(87, 69); $dateValue.Size = New-Object System.Drawing.Size(278, 16); $dateValue.AutoEllipsis = $true
    $script:GuiDateValue = $dateValue; $info.Controls.Add($dateValue)

    $logGroup = New-Object Windows.Forms.GroupBox
    $logGroup.Text = '发布信息'
    $logGroup.Location = New-Object System.Drawing.Point(3, 110)
    $logGroup.Size = New-Object System.Drawing.Size(375, 115)
    $form.Controls.Add($logGroup)

    $logBox = New-Object Windows.Forms.RichTextBox
    $logBox.Location = New-Object System.Drawing.Point(5, 15)
    $logBox.Size = New-Object System.Drawing.Size(365, 94)
    $logBox.ReadOnly = $true
    $logBox.BorderStyle = 'FixedSingle'
    $logBox.BackColor = [System.Drawing.Color]::White
    $logBox.Font = $font
    $logBox.WordWrap = $true
    $logBox.ScrollBars = 'Vertical'
    $logBox.HideSelection = $false
    $script:GuiLogBox = $logBox
    $logGroup.Controls.Add($logBox)

    $backup = New-Object Windows.Forms.CheckBox
    $backup.Text = '创建备份'
    $backup.Checked = $true
    $backup.Location = New-Object System.Drawing.Point(7, 231)
    $backup.Size = New-Object System.Drawing.Size(90, 20)
    $form.Controls.Add($backup)

    $primary = New-Object Windows.Forms.Button
    $primary.Text = '应用'
    $primary.Location = New-Object System.Drawing.Point(100, 229)
    $primary.Size = New-Object System.Drawing.Size(90, 23)
    $primary.Enabled = $false
    $script:GuiPrimary = $primary
    $form.Controls.Add($primary)

    $about = New-Object Windows.Forms.Button
    $about.Text = '关于'
    $about.Location = New-Object System.Drawing.Point(193, 229)
    $about.Size = New-Object System.Drawing.Size(90, 23)
    $script:GuiAbout = $about
    $form.Controls.Add($about)

    $exit = New-Object Windows.Forms.Button
    $exit.Text = '退出'
    $exit.Location = New-Object System.Drawing.Point(286, 229)
    $exit.Size = New-Object System.Drawing.Size(90, 23)
    $script:GuiExit = $exit
    $form.Controls.Add($exit)

    $script:GuiTimer = New-Object Windows.Forms.Timer
    $script:GuiTimer.Interval = 300
    $script:GuiTimer.Add_Tick({ Complete-GuiAction })

    $backup.Add_CheckedChanged({
        if (-not $script:GuiBusy -and -not $backup.Checked) {
            $backup.Checked = $true
            Add-GuiLog '本工具强制创建备份。' 'DarkGoldenrod'
        }
    })
    $targetLabel.Add_DoubleClick({ Select-GuiTarget })
    $fileValue.Add_DoubleClick({ Select-GuiTarget })
    $primary.Add_Click({
        if ($script:GuiBusy) { return }
        if ([string]::IsNullOrWhiteSpace($script:GuiTarget) -or $script:GuiStatus -eq '未知' -or -not (Test-Path -LiteralPath $script:GuiTarget)) {
            Ensure-GuiTarget -AutoApply
            return
        }
        if ($script:GuiStatus -eq '原版') {
            Start-GuiAction 'apply'
        } elseif ($script:GuiStatus -eq '已修补') {
            $backupPath = Get-GuiBackup $script:GuiTarget
            if (-not $backupPath) { Add-GuiLog '找不到原版备份，无法还原。' 'Red'; return }
            $script:GuiRestoreBackup = $backupPath
            Start-GuiAction 'restore'
        }
    })
    $about.Add_Click({ Show-GuiAbout })
    $exit.Add_Click({ $script:GuiForm.Close() })
    $form.Add_FormClosing({
        param($sender, $e)
        if ($script:GuiBusy) {
            $e.Cancel = $true
            Add-GuiLog '正在处理，请等待当前操作完成。' 'DarkOrange'
            return
        }
        if ($script:GuiTimer) { $script:GuiTimer.Stop(); $script:GuiTimer.Dispose(); $script:GuiTimer = $null }
    })
    $form.Add_Shown({
        try {
        if ($script:GuiTarget) {
            $script:GuiStatus = '未知'
            Update-GuiInfo
            Add-GuiLog ("目标：" + $script:GuiTarget) 'Black'
            Add-GuiLog '提示：双击顶部目标路径或文件名称可重新选择。' 'Gray'
            Start-GuiAction 'verify'
        } else {
            Update-GuiInfo
            Add-GuiLog '请选择 PCL2、PCL2 CE 或 HMCL 文件：双击顶部目标行或点击“应用”打开文件选择框。' 'DarkGoldenrod'
            Set-GuiStatus '未选择' 'Black'
        }
        } catch {
            try { [IO.File]::WriteAllText((Join-Path ([IO.Path]::GetTempPath()) 'PclPatchGui-error.txt'), $_.Exception.ToString()) } catch { }
            Add-GuiLog ("启动失败：" + $_.Exception.Message) 'Red'
        }
    })

    [void][Windows.Forms.Application]::Run($form)
    $form.Dispose()
}

if ($Gui) {
    try { Show-Gui }
    catch {
        try { [IO.File]::WriteAllText((Join-Path ([IO.Path]::GetTempPath()) 'PclPatchGui-error.txt'), $_.Exception.ToString()) } catch { }
        throw
    }
    return
}

try {
    if (-not $Quiet -and -not $VerifyOnly) { Add-Type -AssemblyName System.Windows.Forms }

    if ([string]::IsNullOrWhiteSpace($InputFile)) {
        if ($Quiet -or $VerifyOnly) { throw '未指定输入 EXE。' }
        $taskDialog = [Windows.Forms.OpenFileDialog]::new()
        $taskDialog.Title = '选择需要关闭 MC 下载限制的 PCL2 / PCL2 CE / HMCL'
        $taskDialog.Filter = '启动器 (*.exe;*.jar;*.sh)|*.exe;*.jar;*.sh|所有文件 (*.*)|*.*'
        $taskDialog.CheckFileExists = $true
        try {
            if ($taskDialog.ShowDialog() -ne [Windows.Forms.DialogResult]::OK) { exit 0 }
            $InputFile = $taskDialog.FileName
        } finally {
            $taskDialog.Dispose()
        }
    }

    $InputFile = (Resolve-Path -LiteralPath $InputFile).ProviderPath
    $script:TaskLog.input = $InputFile

    if ($PruneBackups) {
        if (-not $Yes) { throw '--prune-backups 需要 --yes 确认删除。' }
        if (-not (Test-Path -LiteralPath $InputFile -PathType Leaf)) { throw "目标不是文件：$InputFile" }
        $leaf = [IO.Path]::GetFileName($InputFile)
        $dir = [IO.Path]::GetDirectoryName($InputFile)
        $timestamped = @(Get-ChildItem -LiteralPath $dir -Filter ($leaf + '.*.bak') -File -ErrorAction SilentlyContinue | Sort-Object LastWriteTimeUtc -Descending)
        $keepCount = [Math]::Max(0, $Keep)
        $removeList = @($timestamped | Select-Object -Skip $keepCount)
        $removed = 0
        foreach ($file in $removeList) {
            try {
                Remove-Item -LiteralPath $file.FullName -Force -ErrorAction Stop
                $removed++
            } catch {
                $script:TaskLog.warnings += ("删除备份失败：{0}" -f $_.Exception.Message)
            }
        }
        $script:TaskLog.mode = 'prune'
        $script:TaskLog.output = $InputFile
        $script:TaskLog.prunedBackups = $removed
        $script:TaskLog.keptBackups = [Math]::Min($keepCount, $timestamped.Count)
        $script:TaskLog.result = 'pruned'
        Write-Step '清理旧备份' ("OK  (删除 {0} 个，保留 {1} 个时间戳备份)" -f $removed, $script:TaskLog.keptBackups)
        Write-Summary '备份清理完成' @(
            ("目标：{0}" -f $InputFile),
            ("删除：{0} 个时间戳备份" -f $removed),
            ("保留：.bak 和最近 {0} 个时间戳备份" -f $script:TaskLog.keptBackups)
        )
        return
    }

    if ($VerifyOnly) {
        if (-not [string]::IsNullOrWhiteSpace($OutputFile)) { throw '--verify 不接受输出路径。' }
        $modeLabel = '校验（只读）'
        $script:TaskLog.mode = 'verify'
    } elseif ($RestoreOnly) {
        if (-not (Test-Path -LiteralPath $InputFile -PathType Leaf)) { throw "目标不是文件：$InputFile" }
        if ((Get-Item -LiteralPath $InputFile).IsReadOnly) { throw "目标是只读文件，无法恢复：$InputFile" }
        if (-not (Test-TargetWritable $InputFile)) { throw "目标文件被占用或不可写，请先完全退出 PCL：$InputFile" }
        if ([string]::IsNullOrWhiteSpace($BackupPath)) {
            $candidates = @(Get-BackupCandidates -Target $InputFile)
            if ($candidates.Count -gt 0) { $BackupPath = $candidates[0].FullName }
        }
        if ([string]::IsNullOrWhiteSpace($BackupPath)) { throw "找不到备份文件：$($InputFile + '.bak')" }
        $BackupPath = (Resolve-Path -LiteralPath $BackupPath).ProviderPath
        if ([IO.Path]::GetFullPath($BackupPath) -eq $InputFile) { throw '备份路径不能与目标相同。' }
        $script:TaskBackup = $BackupPath
        $script:TaskLog.mode = 'restore'
        $script:TaskLog.output = $InputFile
        $script:TaskLog.backup = $BackupPath
        $modeLabel = '恢复原版（从备份）'
    } elseif ($SideBySide) {
        if ([string]::IsNullOrWhiteSpace($OutputFile)) {
            $OutputFile = Join-Path ([IO.Path]::GetDirectoryName($InputFile)) ([IO.Path]::GetFileNameWithoutExtension($InputFile) + '.unrestricted.exe')
        }
        $OutputFile = [IO.Path]::GetFullPath($OutputFile)
        if ($InputFile -eq $OutputFile) { throw '输出路径不能与原文件相同。' }
        if (Test-Path -LiteralPath $OutputFile) { throw "输出文件已存在，拒绝覆盖：$OutputFile" }
        $modeLabel = '并排输出'
        $script:TaskLog.mode = 'side-by-side'
        $script:TaskLog.output = $OutputFile
    } else {
        if (-not [string]::IsNullOrWhiteSpace($OutputFile)) { throw '原地替换模式不接受输出路径；请使用 --side-by-side。' }
        if (-not (Test-Path -LiteralPath $InputFile -PathType Leaf)) { throw "目标不是文件：$InputFile" }
        if ((Get-Item -LiteralPath $InputFile).IsReadOnly) { throw "目标是只读文件，无法替换：$InputFile" }
        if (-not (Test-TargetWritable $InputFile)) { throw "目标文件被占用或不可写，请先完全退出 PCL：$InputFile" }
        $script:TaskBackup = Get-BackupPath $InputFile
        $script:TaskLog.mode = 'in-place'
        $script:TaskLog.output = $InputFile
        $script:TaskLog.backup = $script:TaskBackup
        $modeLabel = '原地替换（自动备份）'
    }

    $script:TaskLog.targetBeforeHash = Get-FileSha256Hex $InputFile
    if ($inPlace) {
        $matchingBackup = Find-MatchingBackup -Target $InputFile -TargetHash $script:TaskLog.targetBeforeHash
        if ($matchingBackup) {
            $script:TaskBackup = $matchingBackup
            $script:TaskLog.backup = $matchingBackup
            $script:TaskLog.reusedBackup = $true
            Write-Ui ("复用已有备份：{0}" -f $matchingBackup) Gray
        }
    }
    Write-Banner $InputFile $modeLabel

    $taskTempBase = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
    $script:TaskWorkspace = Join-Path $taskTempBase ('PclAutoPatch-' + [Guid]::NewGuid().ToString('N'))
    [void][IO.Directory]::CreateDirectory($script:TaskWorkspace)

    $taskSelfPath = if (-not [string]::IsNullOrWhiteSpace($PSCommandPath)) { $PSCommandPath } else { $env:PCL_PATCH_SELF }
    if ([string]::IsNullOrWhiteSpace($taskSelfPath)) { throw '无法定位补丁文件。' }
    $script:TaskLog.payloadSha256 = Get-FileSha256Hex $taskSelfPath

    if ($script:ExternalLayout) {
        $taskPayloadManifestPath = Join-Path $script:ScriptRoot 'payload.json'
        $taskPayloadManifest = Get-Content -LiteralPath $taskPayloadManifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
        if ($taskPayloadManifest.format -ne 'external-v1') { throw '外部负载清单格式不受支持。' }
        $taskCecilPath = Join-Path $script:ScriptRoot 'lib\Mono.Cecil.dll'
        $taskPatcherPath = Join-Path $script:ScriptRoot 'src\patcher.cs'
        $taskHmclPath = Join-Path $script:ScriptRoot 'src\HMCLPatch.cs'
        $taskLicensePath = Join-Path $script:ScriptRoot 'CECIL-LICENSE.txt'
        $taskExpectedFiles = @(
            [pscustomobject]@{ name = 'lib/Mono.Cecil.dll'; path = $taskCecilPath },
            [pscustomobject]@{ name = 'src/patcher.cs'; path = $taskPatcherPath },
            [pscustomobject]@{ name = 'src/HMCLPatch.cs'; path = $taskHmclPath }
        )
        foreach ($expected in $taskExpectedFiles) {
            if (-not (Test-Path -LiteralPath $expected.path -PathType Leaf)) { throw "外部负载缺失：$($expected.name)" }
            $entry = @($taskPayloadManifest.files | Where-Object { $_.name -eq $expected.name } | Select-Object -First 1)
            if ($entry.Count -ne 1) { throw "外部负载清单缺少：$($expected.name)" }
            $file = Get-Item -LiteralPath $expected.path
            if ($file.Length -ne [long]$entry[0].length -or (Get-FileSha256Hex $expected.path) -ne [string]$entry[0].sha256) {
                throw "外部负载校验失败：$($expected.name)"
            }
        }
        [byte[]]$taskCecilBytes = [IO.File]::ReadAllBytes($taskCecilPath)
        [byte[]]$taskPatcherBytes = [IO.File]::ReadAllBytes($taskPatcherPath)
        [byte[]]$taskHmclBytes = [IO.File]::ReadAllBytes($taskHmclPath)
        [byte[]]$taskReferenceBytes = @()
        [byte[]]$taskLicenseBytes = if (Test-Path -LiteralPath $taskLicensePath) { [IO.File]::ReadAllBytes($taskLicensePath) } else { @() }
        [byte[]]$taskManifestBytes = @()
    } else {
        [byte[]]$taskSelfBytes = [IO.File]::ReadAllBytes($taskSelfPath)
        $taskCabMarker = (':' + 'PCLDATA') + "`r`n"
        $taskMarkerOffset = [Text.Encoding]::ASCII.GetString($taskSelfBytes).IndexOf($taskCabMarker, [StringComparison]::Ordinal)
        if ($taskMarkerOffset -lt 0) { throw '找不到内置补丁数据。' }
        $taskCabOffset = $taskMarkerOffset + [Text.Encoding]::ASCII.GetByteCount($taskCabMarker)
        [byte[]]$taskCabBytes = New-Object byte[] ($taskSelfBytes.Length - $taskCabOffset)
        [Buffer]::BlockCopy($taskSelfBytes, $taskCabOffset, $taskCabBytes, 0, $taskCabBytes.Length)
        $taskCabPath = Join-Path $script:TaskWorkspace 'payload.cab'
        [IO.File]::WriteAllBytes($taskCabPath, $taskCabBytes)

        $taskPayloadPath = Join-Path $script:TaskWorkspace 'payload.dat'
        & "$env:SystemRoot\System32\expand.exe" $taskCabPath $taskPayloadPath | Out-Null
        if ($LASTEXITCODE -ne 0) { throw '内置补丁数据解压失败。' }

        $taskReader = [IO.BinaryReader]::new([IO.File]::OpenRead($taskPayloadPath))
        try {
            $taskMagic = [Text.Encoding]::ASCII.GetString($taskReader.ReadBytes(8))
            if ($taskMagic -ne 'PCLSLIM3') { throw "内置补丁数据格式不支持：$taskMagic" }
            $taskPartCount = $taskReader.ReadUInt32()
            if ($taskPartCount -ne 5) { throw "内置补丁数据分段数异常：$taskPartCount" }

            $taskParts = @()
            for ($i = 0; $i -lt $taskPartCount; $i++) {
                $taskLength = $taskReader.ReadUInt32()
                if ($taskLength -gt 16777216) { throw '内置数据长度异常。' }
                [byte[]]$taskData = $taskReader.ReadBytes([int]$taskLength)
                if ($taskData.Length -ne $taskLength) { throw '内置数据不完整。' }
                $taskParts += ,$taskData
            }
            if ($taskReader.BaseStream.Position -ne $taskReader.BaseStream.Length) { throw '内置数据存在多余内容。' }
        } finally {
            $taskReader.Dispose()
        }

        [byte[]]$taskCecilBytes = $taskParts[0]
        [byte[]]$taskPatcherBytes = $taskParts[1]
        [byte[]]$taskHmclBytes = @()
        [byte[]]$taskReferenceBytes = $taskParts[2]
        [byte[]]$taskLicenseBytes = $taskParts[3]
        [byte[]]$taskManifestBytes = $taskParts[4]

        $taskManifest = ([Text.Encoding]::UTF8.GetString($taskManifestBytes) | ConvertFrom-Json)
        if ($taskManifest.format -ne 3) { throw '内置清单格式不受支持。' }
        $taskManifestParts = @($taskManifest.parts)
        if ($taskManifestParts.Count -ne 4) { throw '内置清单分段数异常。' }
        for ($i = 0; $i -lt 4; $i++) {
            $expected = $taskManifestParts[$i]
            $actualHash = Get-Sha256Hex $taskParts[$i]
            if ($taskParts[$i].Length -ne [int]$expected.length -or $actualHash -ne [string]$expected.sha256) {
                throw "内置负载校验失败：$($expected.name)"
            }
        }

        $taskCecilPath = Join-Path $script:TaskWorkspace 'Mono.Cecil.dll'
        [IO.File]::WriteAllBytes($taskCecilPath, $taskCecilBytes)
        [IO.File]::WriteAllBytes((Join-Path $script:TaskWorkspace 'Mono.Cecil.LICENSE.txt'), $taskLicenseBytes)
    }

    $script:TaskLog.cecilSha256 = Get-FileSha256Hex $taskCecilPath
    $expectedCecilSha256 = 'c41bdb9ffd3c5f6e17d2382c1012d73703e035e3f1100245fdd4e08c8dc6eb5b'
    if ($script:TaskLog.cecilSha256 -ne $expectedCecilSha256) {
        throw 'Mono.Cecil 哈希与已知官方 0.11.6 net40 版本不一致。'
    }

    $taskCecilAssembly = [Reflection.Assembly]::Load($taskCecilBytes)
    if ($taskCecilAssembly.GetName().Name -ne 'Mono.Cecil' -or $taskCecilAssembly.GetName().Version.ToString() -ne '0.11.6.0') {
        throw '内置 IL 编辑库无法识别。'
    }

    $taskPatcherSource = [Text.Encoding]::UTF8.GetString($taskPatcherBytes)
    Add-Type -TypeDefinition $taskPatcherSource -ReferencedAssemblies $taskCecilPath, 'System.dll', 'System.Core.dll', 'System.Xml.dll', 'System.IO.Compression.dll', 'System.IO.Compression.FileSystem.dll' -ErrorAction Stop
    if ($taskHmclBytes.Length -gt 0) {
        $taskHmclSource = [Text.Encoding]::UTF8.GetString($taskHmclBytes)
        Add-Type -TypeDefinition $taskHmclSource -ReferencedAssemblies 'System.dll', 'System.Core.dll', 'System.IO.Compression.dll', 'System.IO.Compression.FileSystem.dll' -ErrorAction Stop
    }

    $script:TaskLog.targetProfile = 'unknown'
    $taskCeInfo = Get-PclCeInfo $InputFile
    $taskHmclInfo = $null
    $taskPcl2Info = $null
    if ($taskCeInfo) {
        $script:TaskLog.targetProfile = 'pclce'
        $script:TaskLog.profile = 'PCL2 CE bundle'
        $script:TaskLog.pclVersion = 'PCL2 CE ' + [string]$taskCeInfo.Version
        $script:TaskLog.pclVersionCode = 0
        $script:TaskLog.pclState = [string]$taskCeInfo.State
        $script:TaskLog.pclPatched = [bool]($taskCeInfo.Supported -and $taskCeInfo.State -ne 'original')
        $script:TaskLog.pclSupported = [bool]$taskCeInfo.Supported
        $script:TaskLog.pclAssemblyVersion = [string]$taskCeInfo.CoreAssembly
        $script:TaskLog.pclMarkerCount = @($taskCeInfo.Markers).Count
        $script:TaskLog.pclMarkers = @($taskCeInfo.Markers)
        $script:TaskLog.methods = @($taskCeInfo.Targets)
    } else {
        $taskHmclInfo = Get-HmclInfo $InputFile
        if ($taskHmclInfo) {
            $script:TaskLog.targetProfile = 'hmcl'
            $script:TaskLog.profile = 'HMCL classfile'
            $script:TaskLog.pclVersion = 'HMCL ' + [string]$taskHmclInfo.Version
            $script:TaskLog.pclVersionCode = 0
            $script:TaskLog.pclState = switch ([string]$taskHmclInfo.State) {
                'patched' { 'patched' }
                'unsupported' { 'unsupported' }
                'no_gate' { 'no_gate' }
                default { 'original' }
            }
            $script:TaskLog.pclPatched = [bool]($taskHmclInfo.State -eq 'patched')
            $script:TaskLog.pclSupported = [bool]($taskHmclInfo.State -ne 'unsupported')
            $script:TaskLog.pclAssemblyVersion = [string]$taskHmclInfo.TargetClass
            $script:TaskLog.pclMarkerCount = if ($taskHmclInfo.State -eq 'patched') { 1 } else { 0 }
            $script:TaskLog.pclMarkers = if ($taskHmclInfo.State -eq 'patched') { @('HMCL-PATCH-OPT') } else { @() }
            $script:TaskLog.methods = @('VersionsPage -> RegionUtils.isIsChinaMainlandNetwork')
        } else {
            $taskPcl2Info = Get-Pcl2Info $InputFile
            if ($taskPcl2Info) {
                $script:TaskLog.targetProfile = 'pcl2'
                $script:TaskLog.profile = 'PCL structural (2.99+)'
                $script:TaskLog.pclVersion = $taskPcl2Info.VersionBaseName
                $script:TaskLog.pclVersionCode = $taskPcl2Info.VersionCode
                $script:TaskLog.pclState = [string]$taskPcl2Info.State
                $script:TaskLog.pclPatched = [bool]$taskPcl2Info.Patched
                $script:TaskLog.pclSupported = [bool]$taskPcl2Info.Supported
                $script:TaskLog.pclAssemblyVersion = [string]$taskPcl2Info.AssemblyVersion
                $script:TaskLog.pclMarkerCount = [int]$taskPcl2Info.MarkerCount
                $script:TaskLog.pclMarkers = @($taskPcl2Info.Markers)
                $script:TaskLog.methods = @($taskPcl2Info.Methods)
            } else {
                $script:TaskLog.profile = 'unknown'
                $script:TaskLog.pclState = 'unsupported'
                $script:TaskLog.pclSupported = $false
                $script:TaskLog.pclVersion = 'unknown'
            }
        }
    }

    $taskProfileLabel = switch ([string]$script:TaskLog.targetProfile) {
        'pcl2' { 'PCL2' }
        'pclce' { 'PCL2 CE' }
        'hmcl' { 'HMCL' }
        default { '未知' }
    }
    if (-not $Quiet) { Write-Ui ("识别类型：{0}" -f $taskProfileLabel) DarkCyan }

    if ($RestoreOnly) {
        Write-Step '校验内置负载' 'OK'
        if ($script:TaskLog.targetProfile -eq 'pclce') {
            $taskCeBackupInfo = Get-PclCeInfo $script:TaskBackup
            if (-not $taskCeBackupInfo -or -not $taskCeBackupInfo.IsPclCe -or -not $taskCeBackupInfo.Supported -or $taskCeBackupInfo.Patched) {
                throw "备份不是原始 PCL2 CE：$($script:TaskBackup)"
            }
            $script:TaskLog.pclVersion = 'PCL2 CE ' + [string]$taskCeBackupInfo.Version
            $script:TaskLog.pclVersionCode = 0
            $script:TaskLog.pclState = [string]$taskCeBackupInfo.State
            $script:TaskLog.pclSupported = [bool]$taskCeBackupInfo.Supported
            $script:TaskLog.pclMarkerCount = @($taskCeBackupInfo.Markers).Count
            $script:TaskLog.pclMarkers = @($taskCeBackupInfo.Markers)
            $script:TaskLog.methods = @($taskCeBackupInfo.Targets)
        } elseif ($script:TaskLog.targetProfile -eq 'hmcl') {
            $taskHmclBackupInfo = Get-HmclInfo -Path $script:TaskBackup -AllowAnyExtension
            if (-not $taskHmclBackupInfo -or $taskHmclBackupInfo.State -eq 'patched' -or $taskHmclBackupInfo.State -eq 'unsupported') {
                throw "备份不是原始 HMCL：$($script:TaskBackup)"
            }
            $script:TaskLog.pclVersion = 'HMCL ' + [string]$taskHmclBackupInfo.Version
            $script:TaskLog.pclVersionCode = 0
            $script:TaskLog.pclState = 'original'
            $script:TaskLog.pclSupported = $true
            $script:TaskLog.pclMarkerCount = 0
            $script:TaskLog.pclMarkers = @()
            $script:TaskLog.methods = @('VersionsPage -> RegionUtils.isIsChinaMainlandNetwork')
        } elseif (-not (Test-OriginalPcl $script:TaskBackup)) {
            throw "备份不是原始 PCL（下载限制开关未开启）：$($script:TaskBackup)"
        } else {
            $script:TaskLog.pclVersion = 'backup'
            $script:TaskLog.pclVersionCode = 0
        }
        $script:TaskLog.backupHash = Get-FileSha256Hex $script:TaskBackup
        Write-Step '校验备份' ("OK  ({0})" -f (Format-Hash $script:TaskLog.backupHash))

        if (-not $Yes) {
            if ($Quiet) { throw '--restore 静默执行需要 --yes。' }
            $confirmed = Show-ReplaceConfirm -Target $InputFile -Backup $script:TaskBackup -Action '恢复'
            if (-not $confirmed) {
                $script:TaskCancelled = $true
                $script:TaskLog.result = 'cancelled'
                Write-Ui '已取消，未修改任何文件。' Yellow
                return
            }
        }
        $script:StepWatch.Restart()

        $restoreTemp = Join-Path ([IO.Path]::GetDirectoryName($InputFile)) ('.' + [IO.Path]::GetFileName($InputFile) + '.' + [Guid]::NewGuid().ToString('N') + '.restore.tmp')
        $script:TaskTempTarget = $restoreTemp
        [IO.File]::WriteAllBytes($restoreTemp, [IO.File]::ReadAllBytes($script:TaskBackup))
        if ((Get-FileSha256Hex $restoreTemp) -ne $script:TaskLog.backupHash) { throw '恢复临时文件校验失败。' }

        try {
            [IO.File]::Replace($restoreTemp, $InputFile, $null, $true)
        } catch {
            try {
                [IO.File]::Copy($restoreTemp, $InputFile, $true)
                Remove-Item -LiteralPath $restoreTemp -Force -ErrorAction SilentlyContinue
            } catch {
                throw "恢复失败：$($_.Exception.Message)"
            }
        }
        $script:TaskTempTarget = $null
        $script:TaskLog.targetAfterHash = Get-FileSha256Hex $InputFile
        if ($script:TaskLog.targetAfterHash -ne $script:TaskLog.backupHash) {
            throw '恢复后校验失败：目标哈希与备份不一致。'
        }
        $script:TaskLog.pclState = 'original'
        $script:TaskLog.pclPatched = $false
        $script:TaskLog.pclMarkerCount = 0
        $script:TaskLog.pclMarkers = @()
        $script:TaskLog.result = 'restored'
        Write-Step '恢复原版' 'OK'
        Write-Summary '已恢复原版' @(
            ("当前启动器：{0}" -f $InputFile),
            ("备份：{0}" -f $script:TaskBackup),
            ("耗时：{0:N1}s" -f $script:Stopwatch.Elapsed.TotalSeconds)
        )
        if (-not $Quiet) {
            [void][Windows.Forms.MessageBox]::Show("已恢复原版。`r`n`r`n当前启动器：`r`n$InputFile`r`n`r`n备份：`r`n$($script:TaskBackup)", 'Launcher Download Fix', [Windows.Forms.MessageBoxButtons]::OK, [Windows.Forms.MessageBoxIcon]::Information)
        }
        return
    }

    Write-Step '校验内置负载' 'OK'

    if ($script:TaskLog.targetProfile -eq 'unknown') {
        throw '无法自动识别目标：不是受支持的 PCL2、PCL2 CE 或 HMCL 文件。'
    }

    if ($script:TaskLog.targetProfile -eq 'hmcl') {
        $taskInfo = $taskHmclInfo
        if (-not $taskInfo) { throw 'HMCL 结构识别失败。' }
        $taskStateText = switch ([string]$taskInfo.State) {
            'patchable' { '可补丁' }
            'patched' { '已修补' }
            'no_gate' { '无需处理' }
            default { '结构不支持' }
        }
        Write-Step '校验 HMCL 结构' ("OK  ({0}，{1}，调用 {2} 处)" -f $taskInfo.Version, $taskStateText, $taskInfo.CallCount)
        if ($VerifyOnly) {
            if (-not $Quiet) {
                Write-Summary '校验完成' @(
                    ("HMCL 版本：{0}" -f $taskInfo.Version),
                    ("目标类：{0}" -f $taskInfo.TargetClass),
                    ("状态：{0}（{1} 处区域判断调用）" -f $taskStateText, $taskInfo.CallCount),
                    ("签名：{0}" -f $(if ($taskInfo.Signed) { '是（拒绝修补）' } else { '否' })),
                    ("可打补丁：{0}" -f $(if ($taskInfo.State -eq 'patchable') { '是' } else { '否' })),
                    ("耗时：{0:N1}s" -f $script:Stopwatch.Elapsed.TotalSeconds)
                )
            }
            $script:TaskLog.result = 'verified'
            return
        }

        if (-not $Yes) {
            if ($Quiet) { throw 'HMCL 原地替换需要 --yes 才能静默执行。' }
            $taskDefaultBackup = Get-BackupPath $InputFile
            $confirmed = Show-ReplaceConfirm -Target $InputFile -Backup $taskDefaultBackup -Action '应用'
            if (-not $confirmed) {
                $script:TaskCancelled = $true
                $script:TaskLog.result = 'cancelled'
                Write-Ui '已取消，未修改任何文件。' Yellow
                return
            }
        }
        $script:TaskLog.mode = if ($SideBySide) { 'side-by-side' } else { 'in-place' }
        $script:TaskLog.output = if ($SideBySide) { $OutputFile } else { $InputFile }
        $script:TaskLog.targetBeforeHash = Get-FileSha256Hex $InputFile
        $taskHmclBackup = if ($SideBySide) { $null } else { $script:TaskBackup }
        $taskResult = [HMCLPatchCore]::Apply($InputFile, $taskHmclBackup, [bool]$SideBySide, $OutputFile)
        $script:TaskLog.targetAfterHash = [string]$taskResult.AfterHash
        $script:TaskLog.backup = [string]$taskResult.Backup
        $script:TaskLog.pclState = if ($taskResult.State -eq 'patched') { 'patched' } else { [string]$taskResult.State }
        $script:TaskLog.pclPatched = ($taskResult.State -eq 'patched')
        $script:TaskLog.pclMarkerCount = if ($taskResult.State -eq 'patched') { 1 } else { 0 }
        $script:TaskLog.result = 'patched'
        Write-Step '生成修补文件' 'OK'
        if (-not $Quiet) {
            Write-Summary 'HMCL 修补完成' @(
                ("状态：{0}" -f $taskResult.Message),
                ("输出：{0}" -f $(if ($SideBySide) { $taskResult.Output } else { $InputFile })),
                ("备份：{0}" -f $(if ($taskResult.Backup) { $taskResult.Backup } else { '无（并排输出）' })),
                ("SHA256：{0}" -f (Format-Hash $script:TaskLog.targetAfterHash)),
                ("耗时：{0:N1}s" -f $script:Stopwatch.Elapsed.TotalSeconds)
            )
        }
        return
    }

    if ($script:TaskLog.targetProfile -eq 'pclce') {
        $taskInfo = $taskCeInfo
        if (-not $taskInfo) { throw 'PCL2 CE 结构识别失败。' }
        $taskStateText = switch ([string]$taskInfo.State) {
            'original' { '原版' }
            'patched' { '已修补' }
            'partial' { '部分修改' }
            default { '结构不支持' }
        }
        Write-Step '校验 PCL2 CE 结构' ("OK  ({0}，{1}，标记 {2} 个)" -f $taskInfo.Version, $taskStateText, @($taskInfo.Markers).Count)
    } else {
        $taskInfo = $taskPcl2Info
        if (-not $taskInfo) { throw 'PCL2 结构识别失败。' }
        $script:TaskLog.pclVersion = $taskInfo.VersionBaseName
        $script:TaskLog.pclVersionCode = $taskInfo.VersionCode
        $script:TaskLog.pclState = [string]$taskInfo.State
        $script:TaskLog.pclPatched = [bool]$taskInfo.Patched
        $script:TaskLog.pclSupported = [bool]$taskInfo.Supported
        $script:TaskLog.pclAssemblyVersion = [string]$taskInfo.AssemblyVersion
        $script:TaskLog.pclMarkerCount = [int]$taskInfo.MarkerCount
        $script:TaskLog.pclMarkers = @($taskInfo.Markers)
        $script:TaskLog.methods = @($taskInfo.Methods)
        $taskStateText = switch ([string]$taskInfo.State) {
            'original' { '原版' }
            'patched' { '已修补' }
            'modified' { '部分修改' }
            default { '结构不支持' }
        }
        Write-Step '校验 PCL 结构' ("OK  ({0} / {1}，{2}，标记 {3}/5)" -f $taskInfo.VersionBaseName, $taskInfo.VersionCode, $taskStateText, $taskInfo.MarkerCount)
    }

    if ($VerifyOnly) {
        if (-not $Quiet) {
            $taskSupportText = if ($taskInfo.Supported) { '是' } elseif ($taskInfo.Error) { '否（' + $taskInfo.Error + '）' } else { '否' }
            if ($script:TaskLog.targetProfile -eq 'pclce') {
                Write-Summary '校验完成' @(
                    ("PCL2 CE 版本：{0}" -f $taskInfo.Version),
                    ("程序集：{0} / {1}" -f $taskInfo.CoreAssembly, $taskInfo.MainAssembly),
                    ("状态：{0}（{1} 个修补标记）" -f $taskStateText, @($taskInfo.Markers).Count),
                    ("可打补丁：{0}" -f $taskSupportText),
                    ("匹配入口：{0}" -f (@($taskInfo.Targets) -join ', ')),
                    ("耗时：{0:N1}s" -f $script:Stopwatch.Elapsed.TotalSeconds)
                )
            } else {
                Write-Summary '校验完成' @(
                    ("PCL 版本：{0} ({1})" -f $taskInfo.VersionBaseName, $taskInfo.VersionCode),
                    ("程序集：{0}" -f $taskInfo.AssemblyName),
                    ("状态：{0}（{1}/5 个修补标记）" -f $taskStateText, $taskInfo.MarkerCount),
                    ("可打补丁：{0}" -f $taskSupportText),
                    ("匹配入口：{0}" -f ($taskInfo.Methods -join ', ')),
                    ("耗时：{0:N1}s" -f $script:Stopwatch.Elapsed.TotalSeconds)
                )
            }
        }
        $script:TaskLog.result = 'verified'
        return
    }

    if ($script:TaskLog.targetProfile -eq 'pclce') {
        if ($taskInfo.State -ne 'original') { throw '目标已经修补或部分修改，请先还原。' }
        $taskStage = Join-Path $script:TaskWorkspace 'PCL2CE-patched.exe'
        [void][PclCePatchBuilder]::Build($InputFile, $taskStage)
        $taskCheck = [PclCePatchBuilder]::Verify($taskStage)
        if (-not $taskCheck.Patched -or -not $taskCheck.Supported) {
            throw 'PCL2 CE 修补后校验失败。'
        }
    } else {
        $taskStage = Join-Path $script:TaskWorkspace 'PCL-patched.exe'
        [void][PclDynamicPatchBuilder]::Build($InputFile, $taskStage)
    }
    [byte[]]$taskResult = [IO.File]::ReadAllBytes($taskStage)
    $patchedHash = Get-Sha256Hex $taskResult

    if ($SideBySide) {
        $taskOutDir = [IO.Path]::GetDirectoryName($OutputFile)
        if (-not [IO.Directory]::Exists($taskOutDir)) { throw "输出目录不存在：$taskOutDir" }
        $script:TaskTempOut = Join-Path $taskOutDir ('.' + [IO.Path]::GetFileName($OutputFile) + '.' + [Guid]::NewGuid().ToString('N') + '.tmp')
        try {
            [IO.File]::WriteAllBytes($script:TaskTempOut, $taskResult)
            if ((Get-FileSha256Hex $script:TaskTempOut) -ne $patchedHash) {
                throw '磁盘输出与通过检查的数据不一致。'
            }
            [IO.File]::Move($script:TaskTempOut, $OutputFile)
            $script:TaskTempOut = $null
        } catch {
            if ($script:TaskTempOut -and (Test-Path -LiteralPath $script:TaskTempOut)) {
                Remove-Item -LiteralPath $script:TaskTempOut -Force -ErrorAction SilentlyContinue
            }
            throw
        }
        $script:TaskLog.targetAfterHash = Get-FileSha256Hex $OutputFile
        $script:TaskLog.pclState = 'patched'
        $script:TaskLog.pclPatched = $true
        if ($script:TaskLog.targetProfile -eq 'pcl2') {
            $script:TaskLog.pclMarkerCount = 5
        } else {
            $script:TaskLog.pclMarkerCount = @($taskInfo.Markers).Count
        }
        $script:TaskLog.result = 'patched'
        Write-Step '生成输出文件' 'OK'
        if (-not $Quiet) {
            Write-Summary '修补完成，原文件未修改' @(
                ("输出文件：{0}" -f $OutputFile),
                ("大小：{0}" -f (Format-Size (Get-Item -LiteralPath $OutputFile).Length)),
                ("SHA256：{0}" -f (Format-Hash $script:TaskLog.targetAfterHash)),
                ("耗时：{0:N1}s" -f $script:Stopwatch.Elapsed.TotalSeconds)
            )
            [void][Windows.Forms.MessageBox]::Show("修补完成。`r`n`r`n输出文件：`r`n$OutputFile`r`n`r`n原文件未修改。", 'Launcher Download Fix', [Windows.Forms.MessageBoxButtons]::OK, [Windows.Forms.MessageBoxIcon]::Information)
        }
        return
    }

    Write-Step '生成修补文件' 'OK'

    if (-not $Yes) {
        if ($Quiet) { throw '原地替换需要 --yes 才能静默执行。' }
        $confirmed = Show-ReplaceConfirm -Target $InputFile -Backup $script:TaskBackup
        if (-not $confirmed) {
            $script:TaskCancelled = $true
            $script:TaskLog.result = 'cancelled'
            Write-Ui '已取消，未修改任何文件。' Yellow
            return
        }
    }

    $script:StepWatch.Restart()
    $taskTargetDir = [IO.Path]::GetDirectoryName($InputFile)
    $script:TaskTempTarget = Join-Path $taskTargetDir ('.' + [IO.Path]::GetFileName($InputFile) + '.' + [Guid]::NewGuid().ToString('N') + '.tmp')
    [IO.File]::WriteAllBytes($script:TaskTempTarget, $taskResult)
    if ((Get-FileSha256Hex $script:TaskTempTarget) -ne $patchedHash) { throw '临时修补文件校验失败。' }
    try {
        $taskAcl = Get-Acl -LiteralPath $InputFile
        Set-Acl -LiteralPath $script:TaskTempTarget -AclObject $taskAcl
    } catch {
        $script:TaskLog.warnings += ("复制文件权限失败：{0}" -f $_.Exception.Message)
    }

    [void](Invoke-ReplaceWithBackup -Target $InputFile -Patched $script:TaskTempTarget -Backup $script:TaskBackup -ReuseBackup:$([bool]$script:TaskLog.reusedBackup))
    $script:TaskTempTarget = $null
    $script:TaskLog.targetAfterHash = Get-FileSha256Hex $InputFile
    $script:TaskLog.backupHash = Get-FileSha256Hex $script:TaskBackup

    if ($script:TaskLog.targetAfterHash -ne $patchedHash -or $script:TaskLog.backupHash -ne $script:TaskLog.targetBeforeHash) {
        try {
            [IO.File]::Copy($script:TaskBackup, $InputFile, $true)
            Write-Ui '替换后校验失败，已用备份恢复原文件。' Yellow
        } catch {
            Write-Ui "替换后校验失败，且回滚失败！原版备份：$($script:TaskBackup)" Red
        }
        throw '替换后校验失败。'
    }

    $script:TaskLog.pclState = 'patched'
    $script:TaskLog.pclPatched = $true
    if ($script:TaskLog.targetProfile -eq 'pcl2') {
        $script:TaskLog.pclMarkerCount = 5
    } else {
        $script:TaskLog.pclMarkerCount = @($taskInfo.Markers).Count
    }
    $script:TaskLog.result = 'replaced'
    Write-Step '备份并替换' 'OK'
    if (-not $Quiet) {
        Write-Summary '修补完成' @(
            ("原版备份：{0}" -f $script:TaskBackup),
            ("备份大小：{0}" -f (Format-Size (Get-Item -LiteralPath $script:TaskBackup).Length)),
            ("当前启动器：{0}" -f $InputFile),
            ("SHA256：{0}" -f (Format-Hash $script:TaskLog.targetAfterHash)),
            ("耗时：{0:N1}s" -f $script:Stopwatch.Elapsed.TotalSeconds)
        )
        [void][Windows.Forms.MessageBox]::Show("修补完成。`r`n`r`n原版备份：`r`n$($script:TaskBackup)`r`n`r`n当前启动器：`r`n$InputFile", 'Launcher Download Fix', [Windows.Forms.MessageBoxButtons]::OK, [Windows.Forms.MessageBoxIcon]::Information)
    }
} catch {
    $taskMessage = $_.Exception.Message
    $taskError = $_.Exception
    while ($taskError.InnerException) {
        $taskError = $taskError.InnerException
        $taskMessage = $taskError.Message
    }
    $script:TaskError = $taskMessage
    $script:TaskLog.result = 'failed'
    $script:TaskLog.error = $taskMessage
    $script:TaskFailed = $true
    Write-UiError $taskMessage
    if (-not $Quiet -and -not $VerifyOnly) {
        [void][Windows.Forms.MessageBox]::Show($taskMessage, 'Launcher Download Fix', [Windows.Forms.MessageBoxButtons]::OK, [Windows.Forms.MessageBoxIcon]::Error)
    }
} finally {
    if ($script:TaskTempOut -and (Test-Path -LiteralPath $script:TaskTempOut)) {
        Remove-Item -LiteralPath $script:TaskTempOut -Force -ErrorAction SilentlyContinue
    }
    if ($script:TaskTempTarget -and (Test-Path -LiteralPath $script:TaskTempTarget)) {
        Remove-Item -LiteralPath $script:TaskTempTarget -Force -ErrorAction SilentlyContinue
    }
    $script:TaskLog.elapsedMs = [int]$script:Stopwatch.ElapsedMilliseconds
    Write-TaskLog
    if ($script:TaskWorkspace -and (Test-Path -LiteralPath $script:TaskWorkspace)) {
        $taskResolvedWorkspace = [IO.Path]::GetFullPath($script:TaskWorkspace)
        $taskExpectedPrefix = [IO.Path]::Combine([IO.Path]::GetFullPath([IO.Path]::GetTempPath()), 'PclAutoPatch-')
        if ($taskResolvedWorkspace.StartsWith($taskExpectedPrefix, [StringComparison]::OrdinalIgnoreCase) -and [IO.Path]::GetFileName($taskResolvedWorkspace) -match '^PclAutoPatch-[0-9a-f]{32}$') {
            Remove-Item -LiteralPath $taskResolvedWorkspace -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}

if ($script:TaskFailed) { exit 1 }
