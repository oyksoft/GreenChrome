using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using GreenChrome.Core;

namespace GreenChrome.Tools
{
    /// <summary>
    /// 诊断工具：验证「只认指定绿色版目录下的 Chrome」这条判定是否严格。
    /// 用法：probe.exe [绿色版目录]
    /// </summary>
    class Program
    {
        static int Main(string[] args)
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;

            var root = args.Length > 0 ? args[0] : @"D:\GreenSoft\MyChrome";
            var rootNorm = ProcessUtil.NormalizeRoot(root);
            Console.WriteLine("绿色版目录: " + root);
            Console.WriteLine("规范化后 : " + rootNorm);
            Console.WriteLine();

            var cases = new[]
            {
                new { File = rootNorm + @"\App\chrome.exe",                     Expect = true,  Why = "绿色版主程序" },
                new { File = rootNorm + @"\App\160.0.8171.0\chrome.exe",         Expect = true,  Why = "版本目录里的 exe" },
                new { File = rootNorm + @"\App\chrome_proxy.exe",               Expect = true,  Why = "代理程序" },
                new { File = rootNorm + @"\App\version.dll",                     Expect = false, Why = "不是 exe" },
                new { File = rootNorm + @"\App\chrome.7z",                      Expect = false, Why = "不是 chrome.exe" },
                new { File = rootNorm + @"Backup\App\chrome.exe",               Expect = false, Why = "同前缀兄弟目录" },
                new { File = rootNorm + @"2\App\chrome.exe",                    Expect = false, Why = "同前缀兄弟目录 2" },
                new { File = @"C:\Program Files\Google\Chrome\Application\chrome.exe", Expect = false, Why = "系统安装的 Chrome" },
                new { File = @"D:\GreenSoft\OtherChrome\App\chrome.exe",        Expect = false, Why = "别的绿色版" },
                new { File = Path.Combine(@"D:\cs_project\GreenChrome\bin\Release\net481", "GreenChrome.exe"), Expect = false, Why = "GreenChrome 自己" },
                new { File = rootNorm + @"\App\chrome.EXE",                     Expect = true,  Why = "大小写不敏感" },
            };

            int bad = 0;
            foreach (var c in cases)
            {
                bool got = ProcessUtil.IsChromeOf(c.File, rootNorm);
                bool ok = got == c.Expect;
                if (!ok) bad++;
                Console.WriteLine((ok ? "  OK   " : "  FAIL ") + (got ? "[命中]" : "[排除]")
                    + "  " + c.File + "   <- " + c.Why);
            }

            Console.WriteLine();
            Console.WriteLine(bad == 0 ? "全部符合预期" : (bad + " 项不符合预期"));

            // 真实进程核对
            Console.WriteLine();
            Console.WriteLine("=== 当前机器上的 chrome 进程 ===");
            foreach (var p in Process.GetProcessesByName("chrome"))
            {
                using (p)
                {
                    try
                    {
                        var f = p.MainModule.FileName;
                        Console.WriteLine("  " + (ProcessUtil.IsChromeOf(f, rootNorm) ? "[命中]" : "[排除]") + " " + f);
                    }
                    catch { }
                }
            }

            var groups = ProcessUtil.RunningChromeIn(root);
            Console.WriteLine();
            Console.WriteLine("=== RunningChromeIn 分组结果 ===");
            if (groups.Count == 0)
                Console.WriteLine("  （没有属于该目录的进程）");
            foreach (var g in groups)
                Console.WriteLine("  " + g.Path + "   " + g.Count + " 个进程");

            return bad == 0 ? 0 : 1;
        }
    }
}
