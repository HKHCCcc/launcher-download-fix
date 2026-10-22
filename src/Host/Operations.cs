using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace PclPatchHost
{
    public sealed class PatchOptions
    {
        public string Input;
        public string Output;
        public string LogPath;
        public string BackupPath;
        public bool Verify;
        public bool SideBySide;
        public bool Restore;
        public bool Prune;
        public bool Quiet;
        public bool Yes;
        public bool Force;
        public bool NoColor;
        public bool NoBackup;
        public bool Gui;
        public bool Help;
        public int Keep = 1;
    }

    public sealed class HostLog
    {
        public string Tool = "LauncherDownloadFix";
        public string Time = DateTime.UtcNow.ToString("o");
        public string Profile = "";
        public string TargetProfile = "";
        public string Mode = "";
        public string Input = "";
        public string Output = "";
        public string Backup = "";
        public bool ReusedBackup;
        public bool NoBackup;
        public string PclVersion = "";
        public string PclState = "";
        public bool PclPatched;
        public bool PclSupported;
        public string PclAssembly = "";
        public int PclMarkerCount;
        public string[] PclMarkers = new string[0];
        public string TargetBeforeHash = "";
        public string TargetAfterHash = "";
        public string BackupHash = "";
        public string[] Methods = new string[0];
        public string[] Warnings = new string[0];
        public string Result = "running";
        public string Error = "";
        public long ElapsedMs;

        public string ToJson()
        {
            var sb = new StringBuilder();
            sb.Append("{");
            AppendString(sb, "tool", Tool); sb.Append(",");
            AppendString(sb, "time", Time); sb.Append(",");
            AppendString(sb, "profile", Profile); sb.Append(",");
            AppendString(sb, "targetProfile", TargetProfile); sb.Append(",");
            AppendString(sb, "mode", Mode); sb.Append(",");
            AppendString(sb, "input", Input); sb.Append(",");
            AppendString(sb, "output", Output); sb.Append(",");
            AppendString(sb, "backup", Backup); sb.Append(",");
            AppendBool(sb, "reusedBackup", ReusedBackup); sb.Append(",");
            AppendBool(sb, "noBackup", NoBackup); sb.Append(",");
            AppendString(sb, "pclVersion", PclVersion); sb.Append(",");
            AppendString(sb, "pclState", PclState); sb.Append(",");
            AppendBool(sb, "pclPatched", PclPatched); sb.Append(",");
            AppendBool(sb, "pclSupported", PclSupported); sb.Append(",");
            AppendString(sb, "pclAssemblyVersion", PclAssembly); sb.Append(",");
            sb.Append("\"pclMarkerCount\":").Append(PclMarkerCount).Append(",");
            AppendArray(sb, "pclMarkers", PclMarkers); sb.Append(",");
            AppendString(sb, "targetBeforeHash", TargetBeforeHash); sb.Append(",");
            AppendString(sb, "targetAfterHash", TargetAfterHash); sb.Append(",");
            AppendString(sb, "backupHash", BackupHash); sb.Append(",");
            AppendArray(sb, "methods", Methods); sb.Append(",");
            AppendArray(sb, "warnings", Warnings); sb.Append(",");
            AppendString(sb, "result", Result); sb.Append(",");
            AppendString(sb, "error", Error); sb.Append(",");
            sb.Append("\"elapsedMs\":").Append(ElapsedMs);
            sb.Append("}");
            return sb.ToString();
        }

        private static void AppendString(StringBuilder sb, string name, string value)
        {
            sb.Append("\"").Append(Escape(name)).Append("\":\"").Append(Escape(value ?? "")).Append("\"");
        }

        private static void AppendBool(StringBuilder sb, string name, bool value)
        {
            sb.Append("\"").Append(Escape(name)).Append("\":").Append(value ? "true" : "false");
        }

        private static void AppendArray(StringBuilder sb, string name, string[] values)
        {
            sb.Append("\"").Append(Escape(name)).Append("\":[");
            if (values != null)
            {
                for (int i = 0; i < values.Length; i++)
                {
                    if (i != 0) sb.Append(",");
                    sb.Append("\"").Append(Escape(values[i] ?? "")).Append("\"");
                }
            }
            sb.Append("]");
        }

        private static string Escape(string value)
        {
            if (value == null) return "";
            var sb = new StringBuilder(value.Length + 8);
            foreach (char c in value)
            {
                switch (c)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '"': sb.Append("\\\""); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 32) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }
    }

    public static class Operations
    {
        public static int Execute(PatchOptions options)
        {
            if (options.Prune) return Prune(options);
            if (options.Restore) return Restore(options);
            if (options.Verify) return Verify(options);
            return Apply(options);
        }

        public static int Verify(PatchOptions options)
        {
            var stopwatch = Stopwatch.StartNew();
            var log = NewLog("verify");
            try
            {
                var info = TargetProbe.Probe(options.Input);
                log.Input = options.Input;
                FillLog(log, info);
                bool success = info.IsKnown && (info.Supported || info.NoGate);
                log.Result = success ? "verified" : "unsupported";
                log.ElapsedMs = stopwatch.ElapsedMilliseconds;
                WriteLog(options, log);
                if (options.Quiet) return success ? 0 : 1;

                Print("============================================");
                Print(" " + info.ProfileLabel + " / " + info.StateLabel);
                Print("============================================");
                Print("目标：" + options.Input);
                Print("版本：" + info.Version);
                Print("状态：" + info.StateLabel + (info.MarkerCount > 0 ? "（" + info.MarkerCount + " 个标记）" : ""));
                if (!String.IsNullOrWhiteSpace(info.AssemblyName)) Print("程序集：" + info.AssemblyName);
                if (info.Methods.Length > 0) Print("入口：" + String.Join(", ", info.Methods));
                if (!String.IsNullOrWhiteSpace(info.Error)) Print("说明：" + info.Error);
                Print("耗时：" + stopwatch.Elapsed.TotalSeconds.ToString("N1") + "s");
                log.Result = info.IsKnown && (info.Supported || info.NoGate) ? "verified" : "unsupported";
                return info.IsKnown && (info.Supported || info.NoGate) ? 0 : 1;
            }
            catch (Exception ex)
            {
                log.Result = "failed";
                log.Error = ex.Message;
                WriteLog(options, log);
                throw;
            }
            finally
            {
                log.ElapsedMs = stopwatch.ElapsedMilliseconds;
            }
        }

        public static int Apply(PatchOptions options)
        {
            var stopwatch = Stopwatch.StartNew();
            var log = NewLog(options.SideBySide ? "side-by-side" : "in-place");
            try
            {
                var info = TargetProbe.Probe(options.Input);
                log.Input = options.Input;
                FillLog(log, info);
                if (!info.IsKnown) throw new InvalidOperationException(String.IsNullOrWhiteSpace(info.Error) ? "无法识别目标。" : info.Error);
                if (!info.Supported) throw new InvalidOperationException("目标结构不支持：" + info.Error);
                if (info.Patched) throw new InvalidOperationException("目标已经修补，请先还原。");
                if (info.NoGate)
                {
                    log.Result = "no_gate";
                    log.PclState = "no_gate";
                    WriteLog(options, log);
                    if (!options.Quiet) Print("当前版本没有下载限制，无需处理。");
                    return 0;
                }

                if (!options.Yes)
                {
                    if (options.Quiet) throw new InvalidOperationException("原地替换需要 --yes。");
                    string confirmText = "确认应用补丁？\r\n\r\n" + options.Input;
                    if (options.NoBackup && !options.SideBySide)
                        confirmText += "\r\n\r\n注意：未开启自动备份，修补后无法使用还原。";
                    if (!Confirm(confirmText, "应用")) return Cancel(log, options);
                }

                string target = Path.GetFullPath(options.Input);
                log.Input = target;
                log.Output = options.SideBySide ? "" : target;
                log.NoBackup = options.NoBackup && !options.SideBySide;
                log.TargetBeforeHash = BackupManager.Sha256(target);
                if (!BackupManager.IsWritable(target)) throw new IOException("目标文件被占用或不可写：" + target);

                if (info.Profile == TargetProfile.Hmcl)
                {
                    string backup = null;
                    bool temporaryBackup = options.NoBackup && !options.SideBySide;
                    if (!options.SideBySide)
                    {
                        backup = temporaryBackup
                            ? BackupManager.GetTemporaryPath(target)
                            : (String.IsNullOrWhiteSpace(options.BackupPath)
                                ? (BackupManager.FindMatching(target, log.TargetBeforeHash) ?? BackupManager.GetNewPath(target))
                                : Path.GetFullPath(options.BackupPath));
                    }

                    HMCLPatchResult result;
                    try
                    {
                        result = HMCLPatchCore.Apply(target, backup, options.SideBySide, options.Output);
                    }
                    catch
                    {
                        if (temporaryBackup && File.Exists(backup))
                        {
                            try { File.Copy(backup, target, true); } catch { }
                        }
                        throw;
                    }

                    log.Output = result.Output ?? target;
                    log.TargetAfterHash = result.AfterHash ?? "";
                    log.PclState = result.State == "patched" ? "patched" : result.State;
                    log.PclPatched = result.State == "patched";
                    log.PclMarkerCount = log.PclPatched ? 1 : 0;
                    log.PclMarkers = log.PclPatched ? new string[] { "HMCL-PATCH-OPT" } : new string[0];
                    log.Result = result.State == "no_gate" ? "no_gate" : "patched";

                    if (temporaryBackup)
                    {
                        if (result.State == "patched")
                        {
                            var finalInfo = TargetProbe.Probe(target);
                            if (!finalInfo.IsKnown || !finalInfo.Patched)
                            {
                                if (File.Exists(backup))
                                {
                                    try { File.Copy(backup, target, true); } catch { }
                                }
                                throw new IOException("修补后校验失败。");
                            }
                            BackupManager.TryDelete(backup);
                        }
                        log.Backup = "";
                        log.BackupHash = "";
                        log.ReusedBackup = false;
                        log.Warnings = AddWarning(log.Warnings, "未创建备份，修补后无法还原。");
                    }
                    else
                    {
                        log.Backup = result.Backup ?? "";
                        if (!String.IsNullOrWhiteSpace(log.Backup) && File.Exists(log.Backup))
                            log.BackupHash = BackupManager.Sha256(log.Backup);
                    }

                    WriteLog(options, log);
                    if (!options.Quiet) Print(result.Message);
                    return 0;
                }

                string directory = Path.GetDirectoryName(target);
                string stage = Path.Combine(directory, "." + Path.GetFileName(target) + "." + Guid.NewGuid().ToString("N") + ".tmp");
                try
                {
                    if (info.Profile == TargetProfile.PclCe) PclCePatchBuilder.Build(target, stage);
                    else PclDynamicPatchBuilder.Build(target, stage);

                    var check = TargetProbe.Probe(stage);
                    if (!check.IsKnown || !check.Patched || !check.Supported)
                        throw new InvalidOperationException("修补后校验失败。");

                    string patchedHash = BackupManager.Sha256(stage);
                    if (options.SideBySide)
                    {
                        string output = String.IsNullOrWhiteSpace(options.Output)
                            ? Path.Combine(directory, Path.GetFileNameWithoutExtension(target) + ".unrestricted" + Path.GetExtension(target))
                            : Path.GetFullPath(options.Output);
                        if (String.Equals(output, target, StringComparison.OrdinalIgnoreCase))
                            throw new InvalidOperationException("输出文件不能与输入相同。");
                        if (File.Exists(output)) throw new InvalidOperationException("输出文件已存在：" + output);
                        File.Copy(stage, output, false);
                        if (!String.Equals(BackupManager.Sha256(output), patchedHash, StringComparison.OrdinalIgnoreCase))
                            throw new IOException("输出文件校验失败。");
                        log.Output = output;
                        log.TargetAfterHash = patchedHash;
                    }
                    else
                    {
                        bool temporaryBackup = options.NoBackup;
                        string backup = temporaryBackup
                            ? BackupManager.GetTemporaryPath(target)
                            : (String.IsNullOrWhiteSpace(options.BackupPath)
                                ? (BackupManager.FindMatching(target, log.TargetBeforeHash) ?? BackupManager.GetNewPath(target))
                                : Path.GetFullPath(options.BackupPath));
                        bool reuse = !temporaryBackup && File.Exists(backup) &&
                            String.Equals(BackupManager.Sha256(backup), log.TargetBeforeHash, StringComparison.OrdinalIgnoreCase);
                        if (File.Exists(backup) && !reuse) throw new IOException("备份文件已存在且内容不同：" + backup);

                        try
                        {
                            BackupManager.ReplaceWithBackup(target, stage, backup, reuse);
                        }
                        catch
                        {
                            if (temporaryBackup && File.Exists(backup))
                            {
                                try { File.Copy(backup, target, true); } catch { }
                            }
                            throw;
                        }

                        log.TargetAfterHash = BackupManager.Sha256(target);
                        string backupHash = BackupManager.Sha256(backup);
                        if (!String.Equals(log.TargetAfterHash, patchedHash, StringComparison.OrdinalIgnoreCase) ||
                            !String.Equals(backupHash, log.TargetBeforeHash, StringComparison.OrdinalIgnoreCase))
                        {
                            try { File.Copy(backup, target, true); } catch { }
                            if (temporaryBackup) BackupManager.TryDelete(backup);
                            throw new IOException("替换后校验失败。");
                        }

                        if (temporaryBackup)
                        {
                            BackupManager.TryDelete(backup);
                            log.Backup = "";
                            log.BackupHash = "";
                            log.Warnings = AddWarning(log.Warnings, "未创建备份，修补后无法还原。");
                        }
                        else
                        {
                            log.Backup = backup;
                            log.ReusedBackup = reuse;
                            log.BackupHash = backupHash;
                        }
                    }

                    log.PclState = "patched";
                    log.PclPatched = true;
                    log.PclMarkerCount = info.Profile == TargetProfile.Pcl2 ? 5 : check.MarkerCount;
                    log.PclMarkers = check.Markers;
                    log.Result = options.SideBySide ? "patched" : "replaced";
                    WriteLog(options, log);
                    if (!options.Quiet) Print("修补完成：" + (options.SideBySide ? log.Output : target));
                    return 0;
                }
                finally
                {
                    BackupManager.TryDelete(stage);
                }
            }
            catch (Exception ex)
            {
                log.Result = "failed";
                log.Error = ex.Message;
                WriteLog(options, log);
                throw;
            }
            finally
            {
                log.ElapsedMs = stopwatch.ElapsedMilliseconds;
            }
        }

        public static int Restore(PatchOptions options)
        {
            var stopwatch = Stopwatch.StartNew();
            var log = NewLog("restore");
            try
            {
                var targetInfo = TargetProbe.Probe(options.Input);
                log.Input = options.Input;
                FillLog(log, targetInfo);
                if (!targetInfo.IsKnown) throw new InvalidOperationException("无法识别目标。");
                if (targetInfo.State == "original" && !targetInfo.Patched) return 0;
                string target = Path.GetFullPath(options.Input);
                if (!BackupManager.IsWritable(target)) throw new IOException("目标文件被占用或不可写：" + target);
                if (!options.Force && targetInfo.State != "patched" && targetInfo.State != "original")
                    throw new InvalidOperationException("目标已被修改或结构不支持；如确认覆盖请使用 --force。");

                string backup = !String.IsNullOrWhiteSpace(options.BackupPath)
                    ? Path.GetFullPath(options.BackupPath)
                    : BackupManager.Candidates(target).FirstOrDefault();
                if (String.IsNullOrWhiteSpace(backup) || !File.Exists(backup))
                    throw new FileNotFoundException("找不到备份。");
                var backupInfo = TargetProbe.Probe(backup);
                if (!backupInfo.IsKnown || backupInfo.Patched || backupInfo.State == "unsupported")
                    throw new InvalidOperationException("备份不是可识别的原版：" + backup);

                if (!options.Yes)
                {
                    if (options.Quiet) throw new InvalidOperationException("恢复需要 --yes。");
                    if (!Confirm("确认恢复原版？\r\n\r\n" + target + "\r\n\r\n备份：" + backup, "恢复")) return Cancel(log, options);
                }

                log.Input = target;
                log.Output = target;
                log.Backup = backup;
                log.TargetBeforeHash = BackupManager.Sha256(target);
                log.BackupHash = BackupManager.Sha256(backup);

                if (targetInfo.Profile == TargetProfile.Hmcl)
                {
                    var result = HMCLPatchCore.Restore(target, backup);
                    log.TargetAfterHash = result.AfterHash ?? BackupManager.Sha256(target);
                }
                else
                {
                    BackupManager.Restore(target, backup, log.BackupHash);
                    log.TargetAfterHash = BackupManager.Sha256(target);
                }

                log.PclState = "original";
                log.PclPatched = false;
                log.PclMarkerCount = 0;
                log.PclMarkers = new string[0];
                log.Result = "restored";
                WriteLog(options, log);
                if (!options.Quiet) Print("已恢复原版：" + target);
                return 0;
            }
            catch (Exception ex)
            {
                log.Result = "failed";
                log.Error = ex.Message;
                WriteLog(options, log);
                throw;
            }
            finally
            {
                log.ElapsedMs = stopwatch.ElapsedMilliseconds;
            }
        }

        public static int Prune(PatchOptions options)
        {
            var log = NewLog("prune");
            try
            {
                string target = Path.GetFullPath(options.Input);
                if (!File.Exists(target)) throw new FileNotFoundException("目标不存在。", target);
                var warnings = new List<string>();
                int removed = BackupManager.Prune(target, options.Keep, warnings);
                log.Input = target;
                log.Result = "pruned";
                log.Warnings = warnings.ToArray();
                WriteLog(options, log);
                if (!options.Quiet) Print("已清理时间戳备份：" + removed + " 个，保留最近 " + Math.Max(0, options.Keep) + " 个。");
                return 0;
            }
            catch (Exception ex)
            {
                log.Result = "failed";
                log.Error = ex.Message;
                WriteLog(options, log);
                throw;
            }
        }

        private static string[] AddWarning(string[] existing, string warning)
        {
            var values = new List<string>();
            if (existing != null) values.AddRange(existing);
            if (!values.Contains(warning)) values.Add(warning);
            return values.ToArray();
        }

        private static int Cancel(HostLog log, PatchOptions options)
        {
            log.Result = "cancelled";
            WriteLog(options, log);
            if (!options.Quiet) Print("已取消，未修改任何文件。");
            return 0;
        }

        private static bool Confirm(string text, string title)
        {
            return MessageBox.Show(text, "Launcher Download Fix - " + title, MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) == DialogResult.Yes;
        }

        private static HostLog NewLog(string mode)
        {
            var log = new HostLog();
            log.Mode = mode;
            log.Time = DateTime.UtcNow.ToString("o");
            return log;
        }

        private static void FillLog(HostLog log, TargetInfo info)
        {
            if (info == null) return;
            log.Profile = info.ProfileLabel;
            log.TargetProfile = info.Profile == TargetProfile.Pcl2 ? "pcl2" : info.Profile == TargetProfile.PclCe ? "pclce" : info.Profile == TargetProfile.Hmcl ? "hmcl" : "unknown";
            log.PclVersion = info.Version;
            log.PclState = info.State;
            log.PclPatched = info.Patched;
            log.PclSupported = info.Supported;
            log.PclAssembly = info.AssemblyName;
            log.PclMarkerCount = info.MarkerCount;
            log.PclMarkers = info.Markers ?? new string[0];
            log.Methods = info.Methods ?? new string[0];
            log.Error = (info.Supported || info.NoGate) ? "" : (info.Error ?? "");
        }

        private static void WriteLog(PatchOptions options, HostLog log)
        {
            if (String.IsNullOrWhiteSpace(options.LogPath)) return;
            try
            {
                if (log.ElapsedMs == 0)
                {
                    try
                    {
                        DateTime started = DateTime.Parse(log.Time, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
                        log.ElapsedMs = Math.Max(0, (long)(DateTime.UtcNow - started).TotalMilliseconds);
                    }
                    catch { }
                }
                string full = Path.GetFullPath(options.LogPath);
                string dir = Path.GetDirectoryName(full);
                if (!String.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(full, log.ToJson(), new UTF8Encoding(false));
            }
            catch { }
        }

        private static void Print(string text)
        {
            try { Console.WriteLine(text); } catch { }
        }
    }
}
