using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

public sealed class HMCLPatchInfo
{
    public string State;
    public string Version;
    public string TargetClass;
    public int CallCount;
    public bool Signed;
    public string Message;
}

public sealed class HMCLPatchResult
{
    public string Action;
    public string State;
    public string Input;
    public string Output;
    public string Backup;
    public string BeforeHash;
    public string AfterHash;
    public string Message;
}

public static class HMCLPatchCore
{
    private const string PrimaryTargetEntry = "org/jackhuang/hmcl/ui/download/VersionsPage.class";
    private const string TargetClassName = "org/jackhuang/hmcl/ui/download/VersionsPage";
    private static readonly string[] TargetEntries =
    {
        "org/jackhuang/hmcl/ui/download/VersionsPage.class",
        "org/jackhuang/hmcl/ui/download/VersionsPage$VersionsPageSkin.class",
        "org/jackhuang/hmcl/ui/instances/DownloadListPage$ModDownloadListPageSkin.class"
    };
    private const string RegionClassName = "org/jackhuang/hmcl/util/RegionUtils";
    private const string RegionMethodName = "isIsChinaMainlandNetwork";
    private const string RegionMethodDescriptor = "()Z";
    private const string MarkerEntry = "META-INF/HMCL-PATCH-OPT.txt";
    private const string EngineVersion = "1.0";

    public static HMCLPatchInfo Inspect(string path)
    {
        var info = new HMCLPatchInfo();
        info.TargetClass = PrimaryTargetEntry;
        try
        {
            Payload payload = ReadPayload(path);
            using (var jarStream = new MemoryStream(payload.Jar))
            using (var archive = new ZipArchive(jarStream, ZipArchiveMode.Read))
            {
                info.Signed = HasSignature(archive);
                info.Version = ReadVersion(archive);
                bool foundTarget = false;
                int totalCalls = 0;
                string matchedEntry = null;
                foreach (string entryName in TargetEntries)
                {
                    var target = archive.GetEntry(entryName);
                    if (target == null) continue;
                    foundTarget = true;
                    byte[] classBytes = ReadEntry(target);
                    int calls = new JavaClass(classBytes).TargetCallCount;
                    if (calls > 0 && matchedEntry == null) matchedEntry = entryName;
                    totalCalls += calls;
                }
                if (matchedEntry != null) info.TargetClass = matchedEntry;
                info.CallCount = totalCalls;
                if (!foundTarget)
                {
                    info.State = "unsupported";
                    info.Message = "JAR 中找不到 HMCL 下载页类";
                    return info;
                }

                bool marker = archive.GetEntry(MarkerEntry) != null;
                if (marker)
                {
                    if (info.CallCount > 0)
                    {
                        info.State = "unsupported";
                        info.Message = "发现补丁标记，但区域判断调用仍然存在";
                    }
                    else
                    {
                        info.State = "patched";
                        info.Message = "已由 HMCL-Patch-Opt 修补";
                    }
                    return info;
                }

                if (info.CallCount > 0)
                {
                    info.State = "patchable";
                    info.Message = "发现 " + info.CallCount + " 处区域判断调用";
                }
                else if (archive.GetEntry("org/jackhuang/hmcl/util/RegionUtils.class") == null)
                {
                    info.State = "no_gate";
                    info.Message = "没有发现区域判断调用；该版本可能无需处理";
                }
                else
                {
                    info.State = "unsupported";
                    info.Message = "发现 RegionUtils，但没有找到 VersionsPage 的目标调用；结构可能已变化";
                }
            }
        }
        catch (Exception ex)
        {
            info.State = "unsupported";
            info.Message = ex.Message;
        }
        return info;
    }

    public static HMCLPatchResult Apply(string input, string backup, bool sideBySide, string output)
    {
        var info = Inspect(input);
        if (info.State == "no_gate")
        {
            return new HMCLPatchResult
            {
                Action = "apply",
                State = info.State,
                Input = input,
                BeforeHash = HashFile(input),
                Message = "没有发现区域限制，无需打补丁"
            };
        }
        if (info.State == "patched")
            throw new InvalidOperationException("目标已经是修补版。");
        if (info.State != "patchable")
            throw new InvalidOperationException("目标不可补丁：" + info.Message);
        if (info.Signed)
            throw new InvalidOperationException("目标 JAR 带签名，拒绝修改。");

        string inputFull = Path.GetFullPath(input);
        string outputFull;
        if (sideBySide)
        {
            if (string.IsNullOrWhiteSpace(output))
            {
                string dir = Path.GetDirectoryName(inputFull);
                string name = Path.GetFileNameWithoutExtension(inputFull);
                string ext = Path.GetExtension(inputFull);
                if (string.IsNullOrWhiteSpace(ext)) ext = ".jar";
                outputFull = Path.Combine(dir, name + ".patched" + ext);
            }
            else
            {
                outputFull = Path.GetFullPath(output);
            }
            if (string.Equals(inputFull, outputFull, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("输出文件不能与输入相同。");
            if (File.Exists(outputFull))
                throw new InvalidOperationException("输出文件已存在：" + outputFull);
        }
        else
        {
            outputFull = inputFull;
            backup = string.IsNullOrWhiteSpace(backup) ? GetBackupPath(inputFull) : Path.GetFullPath(backup);
        }

        bool reuseBackup = false;
        if (!sideBySide && File.Exists(backup))
        {
            if (string.Equals(HashFile(backup), HashFile(inputFull), StringComparison.OrdinalIgnoreCase))
                reuseBackup = true;
            else
                throw new InvalidOperationException("备份文件已存在且内容不同：" + backup);
        }

        Payload payload = ReadPayload(inputFull);
        string workDir = Path.GetDirectoryName(inputFull);
        string temp = Path.Combine(workDir, "." + Path.GetFileName(inputFull) + "." + Guid.NewGuid().ToString("N") + ".tmp");
        string staged = sideBySide ? outputFull + "." + Guid.NewGuid().ToString("N") + ".tmp" : temp;
        try
        {
            byte[] patchedJar = PatchJarBytes(payload.Jar);
            byte[] outputBytes = new byte[payload.Header.Length + patchedJar.Length];
            Buffer.BlockCopy(payload.Header, 0, outputBytes, 0, payload.Header.Length);
            Buffer.BlockCopy(patchedJar, 0, outputBytes, payload.Header.Length, patchedJar.Length);
            File.WriteAllBytes(staged, outputBytes);
            var check = Inspect(staged);
            if (check.State != "patched" || check.CallCount != 0)
                throw new InvalidOperationException("修补后校验失败：" + check.Message);

            string before = HashFile(inputFull);
            string after = HashFile(staged);
            if (!sideBySide)
            {
                if (reuseBackup)
                {
                    File.Replace(staged, inputFull, backup, true);
                }
                else
                {
                    File.Move(inputFull, backup);
                    try
                    {
                        File.Move(staged, inputFull);
                    }
                    catch
                    {
                        if (File.Exists(backup) && !File.Exists(inputFull))
                            File.Move(backup, inputFull);
                        throw;
                    }
                }
                temp = null;
                staged = null;
            }
            else
            {
                File.Move(staged, outputFull);
                staged = null;
            }

            return new HMCLPatchResult
            {
                Action = "apply",
                State = "patched",
                Input = inputFull,
                Output = sideBySide ? outputFull : inputFull,
                Backup = sideBySide ? null : backup,
                BeforeHash = before,
                AfterHash = after,
                Message = sideBySide ? "已生成并排修补文件" : (reuseBackup ? "已复用备份并原地修补" : "已备份并原地修补")
            };
        }
        finally
        {
            TryDelete(staged);
            TryDelete(temp);
        }
    }

    public static HMCLPatchResult Restore(string input, string backup)
    {
        string inputFull = Path.GetFullPath(input);
        if (string.IsNullOrWhiteSpace(backup))
        {
            string dir = Path.GetDirectoryName(inputFull);
            string leaf = Path.GetFileName(inputFull);
            var candidates = new List<string>();
            string defaultBackup = inputFull + ".bak";
            if (File.Exists(defaultBackup)) candidates.Add(defaultBackup);
            candidates.AddRange(Directory.GetFiles(dir, leaf + ".*.bak"));
            if (candidates.Count == 0)
                throw new InvalidOperationException("找不到备份文件。");
            backup = candidates.OrderByDescending(File.GetLastWriteTimeUtc).First();
        }
        if (string.IsNullOrWhiteSpace(backup) || !File.Exists(backup))
            throw new InvalidOperationException("找不到备份文件。");
        backup = Path.GetFullPath(backup);
        if (string.Equals(inputFull, backup, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("备份路径不能与目标相同。");

        var backupInfo = Inspect(backup);
        if (backupInfo.State == "unsupported")
            throw new InvalidOperationException("备份不是可识别的 HMCL JAR：" + backupInfo.Message);
        if (backupInfo.State == "patched")
            throw new InvalidOperationException("备份本身是修补版，拒绝恢复。");

        string temp = Path.Combine(Path.GetDirectoryName(inputFull), "." + Path.GetFileName(inputFull) + "." + Guid.NewGuid().ToString("N") + ".restore.tmp");
        string beforeHash = HashFile(inputFull);
        try
        {
            File.Copy(backup, temp, true);
            var check = Inspect(temp);
            if (check.State == "unsupported")
                throw new InvalidOperationException("备份校验失败：" + check.Message);
            File.Copy(temp, inputFull, true);
            return new HMCLPatchResult
            {
                Action = "restore",
                State = check.State,
                Input = inputFull,
                Backup = backup,
                BeforeHash = beforeHash,
                AfterHash = HashFile(inputFull),
                Message = "已从备份恢复"
            };
        }
        finally
        {
            TryDelete(temp);
        }
    }

    private static byte[] PatchJarBytes(byte[] jarBytes)
    {
        using (var stream = new MemoryStream())
        {
            stream.Write(jarBytes, 0, jarBytes.Length);
            stream.Position = 0;
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Update, true))
            {
                if (HasSignature(archive))
                    throw new InvalidOperationException("目标 JAR 带签名，拒绝修改。");
                int patchedEntries = 0;
                foreach (string entryName in TargetEntries)
                {
                    var entry = archive.GetEntry(entryName);
                    if (entry == null) continue;
                    byte[] original = ReadEntry(entry);
                    JavaClass javaClass = new JavaClass(original);
                    if (javaClass.TargetCallCount == 0) continue;
                    byte[] patched = JavaClass.Patch(original);
                    string name = entry.FullName;
                    DateTimeOffset time = entry.LastWriteTime;
                    entry.Delete();
                    var replacement = archive.CreateEntry(name, CompressionLevel.Optimal);
                    replacement.LastWriteTime = time;
                    using (var output = replacement.Open())
                        output.Write(patched, 0, patched.Length);
                    patchedEntries++;
                }
                if (patchedEntries == 0)
                    throw new InvalidOperationException("目标类中找不到区域判断调用。");

                var oldMarker = archive.GetEntry(MarkerEntry);
                if (oldMarker != null) oldMarker.Delete();
                var marker = archive.CreateEntry(MarkerEntry, CompressionLevel.Optimal);
                byte[] markerBytes = Encoding.UTF8.GetBytes("HMCL-Patch-Opt " + EngineVersion + "\r\n");
                using (var output = marker.Open())
                    output.Write(markerBytes, 0, markerBytes.Length);
            }
            return stream.ToArray();
        }
    }

    private sealed class Payload
    {
        public byte[] Header;
        public byte[] Jar;
    }

    private static Payload ReadPayload(string path)
    {
        byte[] all = File.ReadAllBytes(path);
        byte[] jar;
        if (TryReadJar(all, 0, out jar))
            return new Payload { Header = new byte[0], Jar = jar };

        int limit = Math.Min(all.Length - 4, 4 * 1024 * 1024);
        for (int i = 1; i <= limit; i++)
        {
            if (all[i] == 0x50 && all[i + 1] == 0x4B && all[i + 2] == 0x03 && all[i + 3] == 0x04 &&
                TryReadJar(all, i, out jar))
            {
                byte[] header = new byte[i];
                Buffer.BlockCopy(all, 0, header, 0, i);
                return new Payload { Header = header, Jar = jar };
            }
        }
        throw new InvalidDataException("无法识别 HMCL JAR、EXE 或 SH 结构。");
    }

    private static bool TryReadJar(byte[] all, int start, out byte[] jar)
    {
        jar = null;
        if (start < 0 || start + 4 > all.Length ||
            all[start] != 0x50 || all[start + 1] != 0x4B ||
            all[start + 2] != 0x03 || all[start + 3] != 0x04)
            return false;
        int length = all.Length - start;
        byte[] candidate = new byte[length];
        Buffer.BlockCopy(all, start, candidate, 0, length);
        try
        {
            using (var stream = new MemoryStream(candidate))
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Read))
            {
                bool hasTarget = false;
                foreach (string entryName in TargetEntries)
                {
                    if (archive.GetEntry(entryName) != null)
                    {
                        hasTarget = true;
                        break;
                    }
                }
                if (!hasTarget) return false;
            }
            jar = candidate;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool HasSignature(ZipArchive archive)
    {
        foreach (var entry in archive.Entries)
        {
            string name = entry.FullName.ToUpperInvariant();
            if (name.StartsWith("META-INF/") &&
                (name.EndsWith(".SF") || name.EndsWith(".RSA") || name.EndsWith(".DSA") || name.EndsWith(".EC")))
                return true;
        }
        return false;
    }

    private static string ReadVersion(ZipArchive archive)
    {
        var manifest = archive.GetEntry("META-INF/MANIFEST.MF");
        if (manifest == null) return "";
        string text;
        using (var reader = new StreamReader(manifest.Open(), Encoding.UTF8, true))
            text = reader.ReadToEnd();
        foreach (string line in text.Replace("\r\n", "\n").Split('\n'))
        {
            if (line.StartsWith("Implementation-Version:", StringComparison.OrdinalIgnoreCase))
                return line.Substring("Implementation-Version:".Length).Trim();
        }
        return "";
    }

    private static byte[] ReadEntry(ZipArchiveEntry entry)
    {
        using (var stream = entry.Open())
        using (var memory = new MemoryStream())
        {
            stream.CopyTo(memory);
            return memory.ToArray();
        }
    }

    private static string GetBackupPath(string input)
    {
        string path = input + ".bak";
        if (!File.Exists(path)) return path;
        string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        path = input + "." + stamp + ".bak";
        int suffix = 1;
        while (File.Exists(path))
        {
            path = input + "." + stamp + "-" + suffix + ".bak";
            suffix++;
        }
        return path;
    }

    public static string HashFile(string path)
    {
        using (var sha = SHA256.Create())
        using (var stream = File.OpenRead(path))
            return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
    }

    private static void TryDelete(string path)
    {
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
        {
            try { File.Delete(path); }
            catch { }
        }
    }
}

internal sealed class JavaClass
{
    private const string RegionClassName = "org/jackhuang/hmcl/util/RegionUtils";
    private const string RegionMethodName = "isIsChinaMainlandNetwork";
    private const string RegionMethodDescriptor = "()Z";

    private readonly byte[] data;
    private readonly int[] tag;
    private readonly string[] utf8;
    private readonly int[] first;
    private readonly int[] second;
    private readonly List<int> targetOffsets = new List<int>();

    public int TargetCallCount { get { return targetOffsets.Count; } }

    public JavaClass(byte[] bytes)
    {
        data = (byte[])bytes.Clone();
        int p = 0;
        Expect(bytes, ref p, 0xCA, 0xFE, 0xBA, 0xBE);
        ReadU2(bytes, ref p);
        ReadU2(bytes, ref p);
        int count = ReadU2(bytes, ref p);
        tag = new int[count];
        utf8 = new string[count];
        first = new int[count];
        second = new int[count];
        for (int i = 1; i < count; i++)
        {
            int t = ReadU1(bytes, ref p);
            tag[i] = t;
            switch (t)
            {
                case 1:
                    int len = ReadU2(bytes, ref p);
                    utf8[i] = Encoding.UTF8.GetString(bytes, p, len);
                    p += len;
                    break;
                case 7:
                case 8:
                case 16:
                case 19:
                case 20:
                    first[i] = ReadU2(bytes, ref p);
                    break;
                case 9:
                case 10:
                case 11:
                case 12:
                case 17:
                case 18:
                    first[i] = ReadU2(bytes, ref p);
                    second[i] = ReadU2(bytes, ref p);
                    break;
                case 3:
                case 4:
                    p += 4;
                    break;
                case 5:
                case 6:
                    p += 8;
                    i++;
                    break;
                case 15:
                    p += 3;
                    break;
                default:
                    throw new InvalidDataException("Unsupported constant pool tag " + t);
            }
        }

        ReadU2(bytes, ref p);
        ReadU2(bytes, ref p);
        ReadU2(bytes, ref p);
        int interfaces = ReadU2(bytes, ref p);
        p += interfaces * 2;
        SkipMembers(bytes, ref p);
        int methods = ReadU2(bytes, ref p);
        for (int i = 0; i < methods; i++)
        {
            ReadU2(bytes, ref p);
            ReadU2(bytes, ref p);
            ReadU2(bytes, ref p);
            int attributes = ReadU2(bytes, ref p);
            for (int j = 0; j < attributes; j++)
            {
                int nameIndex = ReadU2(bytes, ref p);
                long length = ReadU4(bytes, ref p);
                int start = p;
                if (Utf8(nameIndex) == "Code")
                {
                    ReadU2(bytes, ref p);
                    ReadU2(bytes, ref p);
                    long codeLength = ReadU4(bytes, ref p);
                    int codeStart = p;
                    ScanCode(bytes, codeStart, (int)codeLength);
                }
                p = start + checked((int)length);
            }
        }
    }

    public static byte[] Patch(byte[] original)
    {
        JavaClass javaClass = new JavaClass(original);
        if (javaClass.targetOffsets.Count == 0)
            throw new InvalidOperationException("目标方法中找不到区域判断调用。");
        byte[] patched = javaClass.data;
        foreach (int offset in javaClass.targetOffsets)
        {
            if (offset + 2 >= patched.Length || patched[offset] != 0xB8)
                throw new InvalidDataException("调用点结构异常。");
            patched[offset] = 0x03;     // iconst_0
            patched[offset + 1] = 0x00; // nop
            patched[offset + 2] = 0x00; // nop
        }
        return patched;
    }

    private void ScanCode(byte[] code, int start, int length)
    {
        int p = 0;
        while (p < length)
        {
            int op = code[start + p];
            if (op == 0xB8 && p + 2 < length)
            {
                int methodRef = (code[start + p + 1] << 8) | code[start + p + 2];
                if (IsTarget(methodRef))
                    targetOffsets.Add(start + p);
            }
            int step = InstructionLength(code, start, p, length);
            if (step <= 0 || p + step > length)
                throw new InvalidDataException("Invalid bytecode instruction length.");
            p += step;
        }
    }

    private bool IsTarget(int index)
    {
        if (index <= 0 || index >= tag.Length) return false;
        if (tag[index] != 10 && tag[index] != 11) return false;
        int classIndex = first[index];
        int nameTypeIndex = second[index];
        if (classIndex <= 0 || nameTypeIndex <= 0) return false;
        if (tag[classIndex] != 7 || tag[nameTypeIndex] != 12) return false;
        string className = Utf8(first[classIndex]);
        string methodName = Utf8(first[nameTypeIndex]);
        string descriptor = Utf8(second[nameTypeIndex]);
        return className == RegionClassName
            && methodName == RegionMethodName
            && descriptor == RegionMethodDescriptor;
    }

    private string Utf8(int index)
    {
        return index > 0 && index < utf8.Length && utf8[index] != null ? utf8[index] : "";
    }

    private static int InstructionLength(byte[] code, int start, int offset, int length)
    {
        int op = code[start + offset];
        switch (op)
        {
            case 0x10: return 2;
            case 0x11: return 3;
            case 0x12: return 2;
            case 0x13:
            case 0x14: return 3;
            case 0x15:
            case 0x16:
            case 0x17:
            case 0x18:
            case 0x19: return 2;
            case 0x36:
            case 0x37:
            case 0x38:
            case 0x39:
            case 0x3A: return 2;
            case 0x84: return 3;
            case 0x99:
            case 0x9A:
            case 0x9B:
            case 0x9C:
            case 0x9D:
            case 0x9E:
            case 0x9F:
            case 0xA0:
            case 0xA1:
            case 0xA2:
            case 0xA3:
            case 0xA4:
            case 0xA5:
            case 0xA6:
            case 0xA7:
            case 0xA8: return 3;
            case 0xA9: return 2;
            case 0xAA:
                {
                    int pad = (4 - ((offset + 1) & 3)) & 3;
                    int p = offset + 1 + pad;
                    int low = ReadS4(code, start, p + 4, length);
                    int high = ReadS4(code, start, p + 8, length);
                    long n = (long)high - low + 1;
                    if (n < 0 || n > 65536) throw new InvalidDataException("Invalid tableswitch");
                    return 1 + pad + 12 + (int)n * 4;
                }
            case 0xAB:
                {
                    int pad = (4 - ((offset + 1) & 3)) & 3;
                    int p = offset + 1 + pad;
                    int pairs = ReadS4(code, start, p + 4, length);
                    if (pairs < 0 || pairs > 65536) throw new InvalidDataException("Invalid lookupswitch");
                    return 1 + pad + 8 + pairs * 8;
                }
            case 0xB2:
            case 0xB3:
            case 0xB4:
            case 0xB5:
            case 0xB6:
            case 0xB7:
            case 0xB8: return 3;
            case 0xB9:
            case 0xBA: return 5;
            case 0xBB: return 3;
            case 0xBC: return 2;
            case 0xBD: return 3;
            case 0xC0:
            case 0xC1: return 3;
            case 0xC4:
                return offset + 1 < length && code[start + offset + 1] == 0x84 ? 6 : 4;
            case 0xC5: return 4;
            case 0xC6:
            case 0xC7: return 3;
            case 0xC8:
            case 0xC9: return 5;
            default: return 1;
        }
    }

    private static void SkipMembers(byte[] bytes, ref int p)
    {
        int count = ReadU2(bytes, ref p);
        for (int i = 0; i < count; i++)
        {
            p += 6;
            int attributes = ReadU2(bytes, ref p);
            for (int j = 0; j < attributes; j++)
            {
                ReadU2(bytes, ref p);
                long length = ReadU4(bytes, ref p);
                p += checked((int)length);
            }
        }
    }

    private static void Expect(byte[] bytes, ref int p, params byte[] expected)
    {
        for (int i = 0; i < expected.Length; i++)
            if (ReadU1(bytes, ref p) != expected[i])
                throw new InvalidDataException("Not a Java class file.");
    }

    private static int ReadU1(byte[] bytes, ref int p)
    {
        if (p < 0 || p >= bytes.Length) throw new EndOfStreamException();
        return bytes[p++];
    }

    private static int ReadU2(byte[] bytes, ref int p)
    {
        if (p < 0 || p + 2 > bytes.Length) throw new EndOfStreamException();
        int value = (bytes[p] << 8) | bytes[p + 1];
        p += 2;
        return value;
    }

    private static long ReadU4(byte[] bytes, ref int p)
    {
        if (p < 0 || p + 4 > bytes.Length) throw new EndOfStreamException();
        long value = ((long)bytes[p] << 24) | ((long)bytes[p + 1] << 16) | ((long)bytes[p + 2] << 8) | bytes[p + 3];
        p += 4;
        return value;
    }

    private static int ReadS4(byte[] bytes, int start, int offset, int codeLength)
    {
        if (offset < 0 || offset + 4 > codeLength || start + offset + 4 > bytes.Length)
            throw new EndOfStreamException();
        return (bytes[start + offset] << 24) | (bytes[start + offset + 1] << 16) | (bytes[start + offset + 2] << 8) | bytes[start + offset + 3];
    }
}
