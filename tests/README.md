# Tests

The repository does not include PCL2, PCL2 CE, or HMCL binaries. Tests that
need a real launcher accept its path as a parameter and should be run against
a copy, never against an important working installation.

```powershell
# Build and payload checks (no launcher sample required)
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Test-Build.ps1

# Tamper check; requires a PCL2 sample
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Test-Tamper.ps1 `
  -PclPath "D:\samples\Plain Craft Launcher 2.exe"

# Single-file flow; HMCL is optional
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Test-SingleExe.ps1 `
  -PclPath "D:\samples\Plain Craft Launcher 2.exe" `
  -HmclPath "D:\samples\HMCL.jar"

# No-backup flow; requires a PCL2 sample
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Test-NoBackup.ps1 `
  -PclPath "D:\samples\Plain Craft Launcher 2.exe"

# HMCL flow; requires an HMCL JAR or EXE sample
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Test-Hmcl.ps1 `
  -HmclPath "D:\samples\HMCL.jar"

# PCL2 CE flow; requires a PCL2 CE sample
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Test-PclCe.ps1 `
  -Path ".\build\LauncherDownloadFix.cmd" `
  -PclCePath "D:\samples\PCL-CE.exe"
```

`Test-Build.ps1` expects the external launcher and payload to exist first:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```

Do not commit downloaded launchers, test copies, logs, or generated `.bak`
files. They are covered by the repository `.gitignore`.
