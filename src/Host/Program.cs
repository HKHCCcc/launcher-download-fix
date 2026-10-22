using System;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace PclPatchHost
{
    internal static class Program
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool FreeConsole();

        [STAThread]
        private static int Main(string[] args)
        {
            AppDomain.CurrentDomain.AssemblyResolve += ResolveAssembly;

            try
            {
                var options = Parse(args);
                if (options.Help)
                {
                    ShowHelp();
                    return 0;
                }

                if (options.Gui || args.Length == 0)
                {
                    try { FreeConsole(); } catch { }
                    Application.EnableVisualStyles();
                    Application.SetCompatibleTextRenderingDefault(false);
                    Application.Run(new MainForm(options.Input));
                    return 0;
                }

                return Operations.Execute(options);
            }
            catch (Exception ex)
            {
                try { Console.Error.WriteLine(ex.Message); } catch { }
                return 1;
            }
        }

        private static PatchOptions Parse(string[] args)
        {
            var options = new PatchOptions();
            var positionals = new System.Collections.Generic.List<string>();
            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                if (arg == "--verify") options.Verify = true;
                else if (arg == "--side-by-side") options.SideBySide = true;
                else if (arg == "--restore") options.Restore = true;
                else if (arg == "--prune-backups") options.Prune = true;
                else if (arg == "--apply") { }
                else if (arg == "--quiet") options.Quiet = true;
                else if (arg == "--yes") options.Yes = true;
                else if (arg == "--force") options.Force = true;
                else if (arg == "--no-color") options.NoColor = true;
                else if (arg == "--no-backup") options.NoBackup = true;
                else if (arg == "--gui") options.Gui = true;
                else if (arg == "--help" || arg == "-h" || arg == "/?") options.Help = true;
                else if (arg == "--output" || arg == "-o")
                {
                    i++;
                    if (i >= args.Length) throw new ArgumentException("--output 缺少路径。");
                    options.Output = args[i];
                }
                else if (arg == "--log")
                {
                    i++;
                    if (i >= args.Length) throw new ArgumentException("--log 缺少路径。");
                    options.LogPath = args[i];
                }
                else if (arg == "--backup")
                {
                    i++;
                    if (i >= args.Length) throw new ArgumentException("--backup 缺少路径。");
                    options.BackupPath = args[i];
                }
                else if (arg == "--keep")
                {
                    i++;
                    if (i >= args.Length) throw new ArgumentException("--keep 缺少数量。");
                    options.Keep = Int32.Parse(args[i]);
                }
                else if (arg.StartsWith("-"))
                    throw new ArgumentException("未知参数：" + arg);
                else
                    positionals.Add(arg);
            }

            if (positionals.Count > 0) options.Input = positionals[0];
            if (positionals.Count > 1)
            {
                if (String.IsNullOrWhiteSpace(options.Output)) options.Output = positionals[1];
                else throw new ArgumentException("输出路径重复。");
            }
            if (positionals.Count > 2) throw new ArgumentException("参数过多。");

            if (options.Restore && (options.Verify || options.SideBySide || options.Prune))
                throw new ArgumentException("--restore 不能和 --verify / --side-by-side / --prune-backups 同时使用。");
            if (options.Prune && options.Verify)
                throw new ArgumentException("--prune-backups 不能和 --verify 同时使用。");
            if (options.Prune && !options.Yes)
                throw new ArgumentException("--prune-backups 需要 --yes。");
            if (options.Keep < 0) throw new ArgumentException("--keep 不能小于 0。");
            if (String.IsNullOrWhiteSpace(options.Input) && !options.Help && !options.Gui && args.Length != 0)
                throw new ArgumentException("未指定输入文件。");
            return options;
        }

        private static void ShowHelp()
        {
            Console.WriteLine("Launcher Download Fix 2.0");
            Console.WriteLine();
            Console.WriteLine("用法：");
            Console.WriteLine("  LauncherDownloadFix.exe \"PCL.exe\" --verify");
            Console.WriteLine("  LauncherDownloadFix.exe \"PCL.exe\" --yes");
            Console.WriteLine("  LauncherDownloadFix.exe \"HMCL.jar\" --side-by-side --output \"HMCL-unlocked.jar\"");
            Console.WriteLine("  LauncherDownloadFix.exe \"PCL.exe\" --restore");
            Console.WriteLine("  LauncherDownloadFix.exe \"PCL.exe\" --prune-backups --keep 1 --yes");
            Console.WriteLine("  LauncherDownloadFix.exe");
            Console.WriteLine();
            Console.WriteLine("支持 PCL2 / PCL2 CE / HMCL；目标类型按内容自动识别。");
            Console.WriteLine("选项：--verify --side-by-side --restore --prune-backups --keep --quiet --yes --force --no-color --no-backup --output --log --backup --gui");
            Console.WriteLine("--no-backup 仅用于原地应用；不创建 .bak，修补后无法使用还原。");
        }

        private static Assembly ResolveAssembly(object sender, ResolveEventArgs args)
        {
            if (!String.Equals(new AssemblyName(args.Name).Name, "Mono.Cecil", StringComparison.OrdinalIgnoreCase))
                return null;
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Mono.Cecil.dll.gz"))
            {
                if (stream == null) return null;
                using (var gzip = new GZipStream(stream, CompressionMode.Decompress))
                using (var bytes = new MemoryStream())
                {
                    gzip.CopyTo(bytes);
                    return Assembly.Load(bytes.ToArray());
                }
            }
        }
    }
}
