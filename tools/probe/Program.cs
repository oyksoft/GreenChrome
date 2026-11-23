using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace GreenChrome.Tools
{
    /// <summary>
    /// 诊断工具：列出正在运行的、属于指定绿色版目录的 Chrome 进程。
    /// 用来排查「更新时提示 Chrome 正在运行」的问题。
    ///
    /// 用法：probe.exe &lt;绿色版目录&gt;
    /// </summary>
    class Program
    {
        static int Main(string[] args)
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;

            var dir = args.Length > 0 ? args[0] : @"D:\GreenSoft\MyChrome";
            var groups = RunningChromeIn(dir);

            Console.WriteLine("目录: " + dir);
            if (groups.Count == 0)
            {
                Console.WriteLine("没有正在运行的 Chrome 进程属于该目录。");
                return 0;
            }

            foreach (var g in groups)
                Console.WriteLine(string.Format("  {0}   （{1} 个进程）", g.Path, g.Count));

            Console.WriteLine("合计 " + groups.Sum(g => g.Count) + " 个进程。");
            return 0;
        }

        private class RunningProc
        {
            public string Path;
            public List<int> Pids = new List<int>();
            public int Count { get { return Pids.Count; } }
        }

        private static List<RunningProc> RunningChromeIn(string greenDir)
        {
            var found = new List<RunningProc>();
            if (string.IsNullOrWhiteSpace(greenDir)) return found;

            string root;
            try { root = Path.GetFullPath(greenDir).TrimEnd('\\', '/'); }
            catch { return found; }

            foreach (var name in new[] { "chrome", "chrome_proxy" })
            {
                Process[] procs;
                try { procs = Process.GetProcessesByName(name); } catch { continue; }

                foreach (var p in procs)
                {
                    using (p)
                    {
                        try
                        {
                            var file = p.MainModule.FileName;
                            if (string.IsNullOrEmpty(file)) continue;
                            var d = Path.GetDirectoryName(file);
                            if (string.IsNullOrEmpty(d)) continue;
                            d = Path.GetFullPath(d).TrimEnd('\\', '/');
                            if (!d.StartsWith(root, StringComparison.OrdinalIgnoreCase)) continue;
                            if (d.Length > root.Length && root.Length > 0 &&
                                d[root.Length] != '\\' && d[root.Length] != '/') continue;

                            var g = found.Find(x => string.Equals(x.Path, file, StringComparison.OrdinalIgnoreCase));
                            if (g == null) { g = new RunningProc { Path = file }; found.Add(g); }
                            g.Pids.Add(p.Id);
                        }
                        catch { /* 无权读取路径的进程跳过 */ }
                    }
                }
            }
            return found;
        }
    }
}
