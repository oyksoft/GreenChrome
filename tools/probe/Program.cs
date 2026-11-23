using System;
using System.IO;
using System.Text;
using GreenChrome.Core;

class Program
{
    static void Main()
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        var pkg = @"D:\tmp_gc\cpp.7z";
        var outDir = @"D:\tmp_gc\cpp_x";
        if (Directory.Exists(outDir)) Directory.Delete(outDir, true);
        Directory.CreateDirectory(outDir);
        Unpacker.Extract(pkg, outDir, null, null);
        Console.WriteLine("解包完成");

        var pkgIni = Path.Combine(outDir, "x64", "App", "chrome++.ini");
        var userIni = @"D:\GreenSoft\MyChrome\App\chrome++.ini";

        foreach (var f in new[] { pkgIni, userIni })
        {
            Console.WriteLine(f + "  存在=" + File.Exists(f) +
                (File.Exists(f) ? "  大小=" + new FileInfo(f).Length : ""));
        }

        if (!File.Exists(pkgIni) || !File.Exists(userIni)) return;

        var a = File.ReadAllBytes(pkgIni);
        var b = File.ReadAllBytes(userIni);
        bool same = a.Length == b.Length;
        if (same)
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) { same = false; break; }

        Console.WriteLine();
        Console.WriteLine("包内 ini 与用户 ini " + (same ? "完全相同" : "不同"));
        if (same) return;

        // 逐行对比
        var la = File.ReadAllLines(pkgIni, Encoding.UTF8);
        var lb = File.ReadAllLines(userIni, Encoding.UTF8);
        Console.WriteLine("包内 " + la.Length + " 行, 用户 " + lb.Length + " 行");

        var setA = new System.Collections.Generic.HashSet<string>(la);
        var setB = new System.Collections.Generic.HashSet<string>(lb);

        Console.WriteLine();
        Console.WriteLine("--- 只有包内有的行（新版新增项）---");
        int n = 0;
        foreach (var line in la)
        {
            var t = line.Trim();
            if (t.Length == 0) continue;
            if (!setB.Contains(line))
            {
                if (n++ < 25) Console.WriteLine("  + " + t);
            }
        }
        Console.WriteLine("  共 " + n + " 行");

        Console.WriteLine();
        Console.WriteLine("--- 只有用户有的行（用户改过的）---");
        int m = 0;
        foreach (var line in lb)
        {
            var t = line.Trim();
            if (t.Length == 0) continue;
            if (!setA.Contains(line))
            {
                if (m++ < 25) Console.WriteLine("  - " + t);
            }
        }
        Console.WriteLine("  共 " + m + " 行");
    }
}
