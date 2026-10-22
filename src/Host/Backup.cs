using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace PclPatchHost
{
    public static class BackupManager
    {
        public static string Sha256(string path)
        {
            using (var sha = SHA256.Create())
            using (var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
            }
        }

        public static byte[] Sha256Bytes(byte[] bytes)
        {
            using (var sha = SHA256.Create())
                return sha.ComputeHash(bytes);
        }

        public static bool IsWritable(string path)
        {
            try
            {
                using (var stream = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static List<string> Candidates(string target)
        {
            var result = new List<string>();
            string full = Path.GetFullPath(target);
            string dir = Path.GetDirectoryName(full);
            string leaf = Path.GetFileName(full);
            string canonical = full + ".bak";
            if (File.Exists(canonical)) result.Add(canonical);
            if (!String.IsNullOrWhiteSpace(dir) && Directory.Exists(dir))
            {
                foreach (string file in Directory.GetFiles(dir, "*", SearchOption.TopDirectoryOnly))
                {
                    string name = Path.GetFileName(file);
                    if (String.Equals(name, leaf + ".bak", StringComparison.OrdinalIgnoreCase)) continue;
                    if (!name.StartsWith(leaf + ".", StringComparison.OrdinalIgnoreCase)) continue;
                    if (!name.EndsWith(".bak", StringComparison.OrdinalIgnoreCase)) continue;
                    result.Add(file);
                }
            }
            return result
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(x => File.GetLastWriteTimeUtc(x))
                .ToList();
        }

        public static string FindMatching(string target, string targetHash)
        {
            if (String.IsNullOrWhiteSpace(targetHash)) return null;
            long targetLength = new FileInfo(target).Length;
            foreach (string candidate in Candidates(target))
            {
                try
                {
                    if (new FileInfo(candidate).Length != targetLength) continue;
                    if (String.Equals(Sha256(candidate), targetHash, StringComparison.OrdinalIgnoreCase))
                        return candidate;
                }
                catch { }
            }
            return null;
        }

        public static string GetNewPath(string target)
        {
            string full = Path.GetFullPath(target);
            string canonical = full + ".bak";
            if (!File.Exists(canonical)) return canonical;
            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            string candidate = full + "." + stamp + ".bak";
            int suffix = 1;
            while (File.Exists(candidate))
                candidate = full + "." + stamp + "-" + suffix++ + ".bak";
            return candidate;
        }

        public static string GetTemporaryPath(string target)
        {
            string full = Path.GetFullPath(target);
            string dir = Path.GetDirectoryName(full);
            string leaf = Path.GetFileName(full);
            return Path.Combine(dir, "." + leaf + "." + Guid.NewGuid().ToString("N") + ".rollback.tmp");
        }

        public static void ReplaceWithBackup(string target, string patched, string backup, bool reuse)
        {
            if (reuse)
            {
                if (!File.Exists(backup)) throw new IOException("已有备份不存在：" + backup);
                try
                {
                    File.Replace(patched, target, backup, true);
                    return;
                }
                catch
                {
                    string swap = Path.Combine(Path.GetDirectoryName(target), "." + Path.GetFileName(target) + "." + Guid.NewGuid().ToString("N") + ".swap.tmp");
                    File.Move(target, swap);
                    try
                    {
                        File.Move(patched, target);
                    }
                    catch
                    {
                        File.Move(swap, target);
                        throw;
                    }
                    TryDelete(swap);
                    return;
                }
            }

            if (File.Exists(backup)) throw new IOException("备份文件已存在：" + backup);
            try
            {
                File.Replace(patched, target, backup, true);
                return;
            }
            catch
            {
                File.Move(target, backup);
                try
                {
                    File.Move(patched, target);
                }
                catch
                {
                    if (File.Exists(backup) && !File.Exists(target))
                        File.Move(backup, target);
                    throw;
                }
            }
        }

        public static void Restore(string target, string backup, string expectedHash)
        {
            if (!File.Exists(backup)) throw new IOException("备份不存在：" + backup);
            string temporary = Path.Combine(Path.GetDirectoryName(target), "." + Path.GetFileName(target) + "." + Guid.NewGuid().ToString("N") + ".restore.tmp");
            try
            {
                File.Copy(backup, temporary, true);
                string copyHash = Sha256(temporary);
                if (!String.IsNullOrWhiteSpace(expectedHash) && !String.Equals(copyHash, expectedHash, StringComparison.OrdinalIgnoreCase))
                    throw new IOException("恢复临时文件校验失败。");
                try
                {
                    File.Replace(temporary, target, null, true);
                }
                catch
                {
                    File.Copy(temporary, target, true);
                    TryDelete(temporary);
                }
                string actual = Sha256(target);
                if (!String.IsNullOrWhiteSpace(expectedHash) && !String.Equals(actual, expectedHash, StringComparison.OrdinalIgnoreCase))
                    throw new IOException("恢复后校验失败：目标哈希与备份不一致。");
            }
            finally
            {
                TryDelete(temporary);
            }
        }

        public static int Prune(string target, int keep, List<string> warnings)
        {
            int removed = 0;
            string canonical = Path.GetFullPath(target) + ".bak";
            var timestamped = Candidates(target)
                .Where(x => !String.Equals(Path.GetFullPath(x), canonical, StringComparison.OrdinalIgnoreCase))
                .ToList();
            int skip = Math.Max(0, keep);
            foreach (string file in timestamped.Skip(skip))
            {
                try
                {
                    File.Delete(file);
                    removed++;
                }
                catch (Exception ex)
                {
                    if (warnings != null) warnings.Add("删除备份失败：" + ex.Message);
                }
            }
            return removed;
        }

        public static void TryDelete(string path)
        {
            try
            {
                if (!String.IsNullOrWhiteSpace(path) && File.Exists(path)) File.Delete(path);
            }
            catch { }
        }
    }
}
