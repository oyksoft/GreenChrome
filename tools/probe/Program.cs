using System.Linq;
using System;
using System.IO;
using System.Threading;
using GreenChrome.Core;

class Program
{
    class P : ChromeUpdater.IProgress
    {
        int _lastPct = -1;
        public void Log(string m) { Console.WriteLine("  LOG  " + m); }
        public void Status(string m) { Console.WriteLine("  STAT " + m); }
        public void Stage(string s) { Console.WriteLine("== " + s); }
        public void DownloadProgress(long got, long total)
        {
            if (total <= 0) return;
            int pct = (int)(got * 100 / total);
            if (pct / 5 != _lastPct / 5)   // 每 5% 打一次
            {
                _lastPct = pct;
                Console.WriteLine(string.Format("  DL   {0,3}%  {1:F0}/{2:F0} MB",
                    pct, got / 1048576.0, total / 1048576.0));
            }
        }
    }

    static void Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        var target = @"D:\tmp_gc\GCFresh";
        var bound = @"D:\GreenSoft\MyChrome";   // 现有绿色版，必须不受影响

        Console.WriteLine("=== 安全校验（必须拒绝现有目录）===");
        foreach (var c in new[] { bound, @"D:\GreenSoft", bound + @"\X", @"D:\GreenSoft\MyChrome" })
        {
            var r = ChromeUpdater.ValidateFreshTarget(c, bound);
            Console.WriteLine("  " + (r == null ? "[通过!] " : "[拒绝] ") + c);
        }
        Console.WriteLine("  目标 " + target + " -> " +
            (ChromeUpdater.ValidateFreshTarget(target, bound) ?? "通过"));

        Console.WriteLine();
        Console.WriteLine("=== 从零制作（完整下载流程）===");
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var all = CoiClient.Fetch("https://coi.oyk.pub/api");
        var rel = CoiClient.Find(all, "canary", "x64");
        Console.WriteLine(string.Format("目标版本 {0}  {1:F0} MB", rel.version, rel.size / 1048576.0));

        var ver = ChromeUpdater.CreateFresh(target, rel, "x64", new P(), CancellationToken.None);
        sw.Stop();
        Console.WriteLine(string.Format("完成：{0}  用时 {1:F1}s", ver, sw.Elapsed.TotalSeconds));

        Console.WriteLine();
        Console.WriteLine("=== 结果 ===");
        foreach (var d in Directory.GetDirectories(target))
        {
            Console.WriteLine("  DIR  " + Path.GetFileName(d));
            foreach (var f in Directory.GetFiles(d).Take(3))
                Console.WriteLine("        " + Path.GetFileName(f) + "  " + new FileInfo(f).Length);
        }
        foreach (var f in Directory.GetFiles(Path.Combine(target, "App")))
            Console.WriteLine("  FILE App/" + Path.GetFileName(f) + "  " + new FileInfo(f).Length);

        Console.WriteLine();
        Console.WriteLine("=== 检测器复查 ===");
        Console.WriteLine("  是绿色版? " + Detector.IsGreenChromeDir(target));
        Console.WriteLine("  Chrome 版本: " + Detector.ChromeVersion(target));
        Console.WriteLine("  架构: " + Detector.Arch(target));
        Console.WriteLine("  Chrome++: " + Detector.ChromePlusVersion(target) + " (" + Detector.ChromePlusName(target) + ")");

        Console.WriteLine();
        Console.WriteLine("=== 现有绿色版是否被影响 ===");
        Console.WriteLine("  版本: " + Detector.ChromeVersion(bound));
        Console.WriteLine("  Chrome++: " + Detector.ChromePlusVersion(bound));
        var vdl = Path.Combine(bound, "App", "version.dll");
        Console.WriteLine("  version.dll 大小: " + new FileInfo(vdl).Length + " (期望 198656)");
    }
}
