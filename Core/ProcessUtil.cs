using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace GreenChrome.Core
{
    /// <summary>
    /// 查找「某个绿色版目录下的 Chrome 进程」。
    ///
    /// 判定必须严格，只认这套绿色版自己的 chrome.exe / chrome_proxy.exe：
    ///   · 所在目录在该绿色版目录之下（且是真子目录，前缀相同的兄弟目录不算）
    ///   · 文件名是 chrome.exe 或 chrome_proxy.exe
    ///   · 永远不把调用方自己算进去
    /// </summary>
    public static class ProcessUtil
    {
        /// <summary>一组属于同一可执行文件的进程。</summary>
        public class RunningGroup
        {
            public string Path;                   // 可执行文件完整路径
            public List<int> Pids = new List<int>();
            public int Count { get { return Pids.Count; } }
        }

        /// <summary>
        /// 判断某个可执行文件是否属于指定绿色版目录下的 Chrome。
        /// </summary>
        public static bool IsChromeOf(string file, string root)
        {
            if (string.IsNullOrEmpty(file)) return false;
            if (string.IsNullOrEmpty(root)) return false;

            string dir;
            try
            {
                dir = Path.GetFullPath(Path.GetDirectoryName(file)).TrimEnd('\\', '/');
            }
            catch { return false; }
            if (string.IsNullOrEmpty(dir)) return false;

            if (!dir.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return false;
            // 必须是真子目录：D:\Foo\Bar 不应被 D:\Foo\Ba 误判
            if (dir.Length > root.Length && root.Length > 0 &&
                dir[root.Length] != '\\' && dir[root.Length] != '/') return false;

            var name = Path.GetFileName(file);
            return name.Equals("chrome.exe", StringComparison.OrdinalIgnoreCase)
                || name.Equals("chrome_proxy.exe", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>规范化绿色版根目录（去尾分隔符、绝对化）。失败返回空串。</summary>
        public static string NormalizeRoot(string greenDir)
        {
            if (string.IsNullOrWhiteSpace(greenDir)) return "";
            try { return Path.GetFullPath(greenDir).TrimEnd('\\', '/'); }
            catch { return ""; }
        }

        /// <summary>
        /// 找出正在运行的、属于该绿色版目录的 Chrome 进程，按可执行文件归组。
        /// </summary>
        public static List<RunningGroup> RunningChromeIn(string greenDir)
        {
            var found = new List<RunningGroup>();
            var root = NormalizeRoot(greenDir);
            if (root.Length == 0) return found;

            var myPid = SafeCurrentPid();

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
                            // 万一：绝不把自身算进去
                            if (p.Id == myPid) continue;

                            var file = p.MainModule.FileName;
                            if (!IsChromeOf(file, root)) continue;

                            var group = found.Find(g =>
                                string.Equals(g.Path, file, StringComparison.OrdinalIgnoreCase));
                            if (group == null)
                            {
                                group = new RunningGroup { Path = file };
                                found.Add(group);
                            }
                            group.Pids.Add(p.Id);
                        }
                        catch
                        {
                            // 拿不到别的用户的进程路径就跳过
                        }
                    }
                }
            }
            return found;
        }

        /// <summary>
        /// 确认这个进程此刻确实还是「该绿色版目录下的 Chrome」。
        /// 用来防止 pid 被系统回收复用后误杀无关进程。
        /// </summary>
        public static bool IsStillThisChrome(Process p, string root)
        {
            if (p == null) return false;
            try
            {
                if (p.Id == SafeCurrentPid()) return false;
                var file = p.MainModule.FileName;
                return IsChromeOf(file, root);
            }
            catch
            {
                // 读不到路径（进程已退出或权限不足）→ 不动它
                return false;
            }
        }

        private static int SafeCurrentPid()
        {
            try { return Process.GetCurrentProcess().Id; }
            catch { return -1; }
        }
    }
}
