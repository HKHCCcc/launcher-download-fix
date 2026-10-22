# Launcher Download Fix

> **DeepSeek 100%**  
> 本项目的自有源代码、文档、原创 UI 和 8-bit 音乐 100% 由 DeepSeek 生成；第三方依赖 `Mono.Cecil` 除外。详见 [ATTRIBUTION.md](ATTRIBUTION.md)。

一个面向本地启动器副本的下载限制修复工具，支持：

- PCL2 / PCL2 CE 的单文件 EXE
- HMCL 的 JAR、EXE 和内嵌 JAR 结构

工具会识别目标结构，支持校验、应用、还原和备份管理。处理过程在本地完成。

> 这是非官方工具。修改后的启动器和本工具都没有签名，可能触发 SmartScreen 或杀毒软件提示。请只对自己的副本使用，并确认你所在地区和使用场景的合规性。

## 使用范围与责任

本项目用于学习、研究和本地兼容性测试。请仅处理你拥有或已获得明确授权的启动器副本，并遵守所在地法律、软件许可及相关平台的服务条款。

本项目不鼓励或支持绕过付费内容、未经授权的访问、传播修改后的启动器或其他违法、违约用途。修改后的启动器是否可用取决于目标版本和运行环境，项目不提供任何担保。

本项目不是 PCL2、PCL2 CE、HMCL 或 DeepSeek 的官方项目，也不代表其官方立场。

## 功能

- 按程序集、classfile 和补丁标记识别目标类型
- 默认在应用前创建 `.bak` 备份；也可以显式关闭备份
- 重复应用复用已有备份，不会无限产生备份文件
- 关闭备份时不生成持久 `.bak`，修补后会明确显示“无备份”并禁用还原
- 支持还原、并排输出和备份清理
- 拒绝签名 JAR、未知结构和外部修改过的目标
- Win98/注册机风格界面、原创 CRT 图标和默认开启的原创 8-bit 音乐
- HMCL 的 `no_gate` 版本会识别为“无需处理”，不会修改文件

## 环境要求

- Windows
- .NET Framework 4.8
- Windows PowerShell 5.1 或 PowerShell 7

## 构建

### 单文件 EXE

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build-single.ps1
```

输出：

```text
build\LauncherDownloadFix.exe
```

单文件 EXE 内嵌 `Mono.Cecil.dll`，运行时不需要外部 `lib`、`src` 或 `.ps1` 文件。

### 外置目录版本

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```

输出 `build\LauncherDownloadFix.cmd`、`.ps1` 和负载文件。外置版本用于排障和回退。

## 使用

GUI 直接双击 EXE，或：

```powershell
.\LauncherDownloadFix.exe --gui
```

命令行示例：

```powershell
.\LauncherDownloadFix.exe "D:\PCL\Plain Craft Launcher 2.exe" --verify
.\LauncherDownloadFix.exe "D:\PCL\Plain Craft Launcher 2.exe" --yes
.\LauncherDownloadFix.exe "D:\PCL\Plain Craft Launcher 2.exe" --restore
.\LauncherDownloadFix.exe "D:\PCL\Plain Craft Launcher 2.exe" --no-backup --yes
.\LauncherDownloadFix.exe "D:\HMCL\HMCL.jar" --side-by-side --output "D:\HMCL\HMCL-fixed.jar"
```

`--no-backup` 不创建持久 `.bak`，修补后无法使用还原；默认仍然创建备份。

完整参数：

```text
--verify  --side-by-side  --restore  --prune-backups  --keep  --quiet
--yes  --force  --no-color  --no-backup  --output  --log  --backup  --gui  --help
```

原地替换需要 `--yes`；`--force` 会跳过部分安全检查，只应在明确知道目标状态时使用。

## 测试

基础构建检查：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\Test-Build.ps1
```

篡改校验：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\Test-Tamper.ps1 -PclPath "D:\path\to\Plain Craft Launcher 2.exe"
```

单文件流程：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\Test-SingleExe.ps1 `
  -PclPath "D:\path\to\Plain Craft Launcher 2.exe" `
  -HmclPath "D:\path\to\HMCL.jar"
```

关闭备份流程：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\Test-NoBackup.ps1 `
  -PclPath "D:\path\to\Plain Craft Launcher 2.exe"
```

HMCL：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\Test-Hmcl.ps1 `
  -HmclPath "D:\path\to\HMCL.jar"
```

PCL2 CE：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\Test-PclCe.ps1 `
  -Path ".\build\LauncherDownloadFix.cmd" `
  -PclCePath "D:\path\to\PCL-CE.exe"
```

仓库不包含 PCL2、PCL2 CE 或 HMCL 的二进制样本。涉及真实启动器的测试需要自行准备文件，并优先使用副本。

## 目录结构

```text
.
├─ lib/Mono.Cecil.dll
├─ src/
│  ├─ patcher.cs
│  ├─ HMCLPatch.cs
│  ├─ patch.ps1
│  ├─ CECIL-LICENSE.txt
│  └─ Host/
├─ tests/
│  ├─ Test-Build.ps1
│  ├─ Test-Tamper.ps1
│  ├─ Test-SingleExe.ps1
│  ├─ Test-NoBackup.ps1
│  ├─ Test-Hmcl.ps1
│  └─ Test-PclCe.ps1
├─ build.ps1
├─ build-single.ps1
├─ LICENSE
├─ THIRD-PARTY-NOTICES.md
├─ ATTRIBUTION.md
├─ CHANGELOG.md
└─ README.md
```

## 已知限制

- HMCL `.deb` 尚未覆盖
- PCL2 CE ARM64 没有自动化测试环境
- 特殊二次打包、加密或内嵌 JAR 位置过深的 HMCL EXE 可能无法识别
- 真实 main/nightly gate 构建和完整在线下载流程不一定可复现
- 修补后的启动器是否可用取决于目标版本和运行环境

## 许可证

本项目代码使用 MIT License，详见 [LICENSE](LICENSE)。

项目包含或依赖的第三方组件和许可信息见 [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)。PCL2、PCL2 CE、HMCL 是其各自权利人的项目，本仓库不包含它们的源代码，也不代表它们官方。

## 免责声明

本项目按“现状”提供，不提供任何明示或暗示担保。作者不对因使用本工具导致的文件损坏、启动失败、数据丢失或其他后果承担责任。请在副本和隔离环境中测试，并保留原始备份。
