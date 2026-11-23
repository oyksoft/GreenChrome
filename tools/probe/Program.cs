using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using GreenChrome.Core;

class Program
{
    static void Main()
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        var fakeDir = @"D:\tmp_gc\fake";
        var realDir = @"D:\GreenSoft\MyChrome";

        Console.WriteLine("== 启动假 chrome 进程 ==");
        var proc = Process.Start(new ProcessStartInfo(Path.Combine(fakeDir, "App", "chrome.exe")) { UseShellExecute = true });
        System.Threading.Thread.Sleep(2500);

        foreach (var target in new[] { fakeDir, realDir, @"D:\tmp_gc" })
        {
            var found = Detect(target);
            Console.WriteLine(string.Format("  目录 {0,-32} 命中 {1} 个", target, found.Count));
            foreach (var f in found) Console.WriteLine("      " + f);
        }

        try { proc.Kill(); } catch { }
        Console.WriteLine("已结束假进程");
    }

    static List<string> Detect(string greenDir)
    {
        var found = new List<string>();
        string root = Path.GetFullPath(greenDir).TrimEnd('\\', '/');
        foreach (var name in new[] { "chrome", "chrome_proxy" })
        {
            foreach (var p in Process.GetProcessesByName(name))
            {
                using (p)
                {
                    try
                    {
                        var file = p.MainModule.FileName;
                        var dir = Path.GetDirectoryName(file);
                        if (string.IsNullOrEmpty(dir)) continue;
                        dir = Path.GetFullPath(dir).TrimEnd('\\', '/');
                        if (!dir.StartsWith(root, StringComparison.OrdinalIgnoreCase)) continue;
                        if (dir.Length > root.Length && root.Length > 0 &&
                            dir[root.Length] != '\\' && dir[root.Length] != '/') continue;
                        found.Add(file);
                    }
                    catch { }
                }
            }
        }
        return found;
    }
}
