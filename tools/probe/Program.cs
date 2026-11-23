using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using GreenChrome.Core;

class Program
{
    static void Main()
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        Console.WriteLine("=== 1. 版本接口：直连 coi.oyk.pub ===");
        var sw = Stopwatch.StartNew();
        var data = CoiClient.Fetch("https://coi.oyk.pub/api");
        sw.Stop();
        Console.WriteLine("  OK  " + data.Count + " 条，用时 " + sw.Elapsed.TotalSeconds.ToString("F1") + "s");

        Console.WriteLine();
        Console.WriteLine("=== 2. Chrome++ 版本查询：镜像优先 ===");
        sw.Restart();
        var cpp = ChromePlusClient.FetchLatest(AppConfig.DefaultGhMirrors, m => Console.WriteLine("    LOG " + m));
        sw.Stop();
        if (cpp == null) { Console.WriteLine("  FAIL 返回 null"); return; }
        Console.WriteLine("  OK  版本 " + cpp.Version + "  用时 " + sw.Elapsed.TotalSeconds.ToString("F1") + "s");
        Console.WriteLine("  SHA256 = " + (cpp.Sha256 ?? "(无)"));
        Console.WriteLine("  版本号来自镜像? " + cpp.VersionFromMirror);
        Console.WriteLine("  SHA256 来自镜像? " + cpp.Sha256FromMirror);

        Console.WriteLine();
        Console.WriteLine("=== 3. 镜像全部失效时，应回退官方 ===");
        var deadMirrors = new[] { "https://not-exist-mirror-xxx.invalid" };
        sw.Restart();
        var cpp2 = ChromePlusClient.FetchLatest(deadMirrors, m => Console.WriteLine("    LOG " + m));
        sw.Stop();
        Console.WriteLine("  " + (cpp2 != null ? "OK  回退成功，版本 " + cpp2.Version : "FAIL 返回 null")
            + "，用时 " + sw.Elapsed.TotalSeconds.ToString("F1") + "s");

        Console.WriteLine();
        Console.WriteLine("=== 4. 下载 + 解包 + 装 version.dll（走镜像）===");
        sw.Restart();
        var dir = Path.Combine(Path.GetTempPath(), "gc_cpp_t" + DateTime.Now.ToString("HHmmss"));
        Directory.CreateDirectory(dir);
        try
        {
            ChromePlusClient.Update(dir, "x64", cpp, m => Console.WriteLine("    LOG " + m),
                (g, t) => { }, CancellationToken.None, AppConfig.DefaultGhMirrors);
            sw.Stop();
            var dll = Path.Combine(dir, "App", "version.dll");
            var ini = Path.Combine(dir, "App", "chrome++.ini");
            Console.WriteLine("  OK  version.dll " + new FileInfo(dll).Length + " 字节"
                + "  chrome++.ini " + (File.Exists(ini) ? new FileInfo(ini).Length + " 字节" : "无")
                + "  用时 " + sw.Elapsed.TotalSeconds.ToString("F1") + "s");
        }
        catch (Exception ex)
        {
            Console.WriteLine("  FAIL " + ex.Message);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }
}
