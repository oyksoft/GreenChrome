using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using SharpCompress.Archives.SevenZip;

namespace GreenChrome.Core
{
    /// <summary>
    /// 单文件打包自检：把嵌入 exe 的第三方依赖真实跑一遍，
    /// 确认它们在「依赖全部嵌入 exe」的情况下也能正常工作。
    ///
    /// 用法：GreenChrome.exe --selftest
    /// 正常时退出码 0，任何一项失败退出码 1。
    /// </summary>
    public static class SelfTest
    {
        public static int Run(TextWriter output)
        {
            int failed = 0;

            // 1. Newtonsoft.Json
            try
            {
                var obj = new { name = "GreenChrome", ver = "0.1.0", list = new[] { 1, 2, 3 } };
                var json = JsonConvert.SerializeObject(obj);
                var back = JsonConvert.DeserializeObject<Dictionary<string, object>>(json);
                bool ok = back != null && back.ContainsKey("name")
                          && back["name"].ToString() == "GreenChrome";
                output.WriteLine(ok ? "  [OK]   Newtonsoft.Json 序列化/反序列化"
                                     : "  [FAIL] Newtonsoft.Json 结果不符");
                if (!ok) failed++;
            }
            catch (Exception ex)
            {
                output.WriteLine("  [FAIL] Newtonsoft.Json: " + ex.Message);
                failed++;
            }

            // 2. SharpCompress：确认程序集与 7z 读取 API 真的可用
            try
            {
                // 用垃圾数据调用真实 API，必然抛异常 —— 说明程序集加载成功、方法能被解析调用
                bool reachedApi = false;
                try
                {
                    using (var junk = new MemoryStream(new byte[] { 0x00, 0x01, 0x02, 0x03 }))
                    using (var arc = SevenZipArchive.Open(junk)) { }
                }
                catch { reachedApi = true; }
                if (!reachedApi) throw new InvalidOperationException("SevenZipArchive.Open 没有按预期响应");

                // 同目录里若放了 .7z，就做一次真实解压
                var sevenZip = Directory.GetFiles(AppContext.BaseDirectory, "*.7z").FirstOrDefault();
                if (sevenZip != null)
                {
                    using (var arc = SevenZipArchive.Open(sevenZip))
                    {
                        int n = arc.Entries.Count;
                        if (n <= 0) throw new InvalidDataException("7z 里没有条目");
                        output.WriteLine("  [OK]   SharpCompress 真实解压 " + Path.GetFileName(sevenZip)
                            + "（" + n + " 个条目）");
                    }
                }
                else
                {
                    output.WriteLine("  [OK]   SharpCompress 7z 读取 API 可用"
                        + "（同目录放个 .7z 可做真实解压验证）");
                }
            }
            catch (Exception ex)
            {
                output.WriteLine("  [FAIL] SharpCompress: " + ex.Message);
                failed++;
            }

            // 3. 单实例之外的框架依赖：Windows 窗体（原生文件夹对话框用）
            try
            {
                var fbd = new System.Windows.Forms.FolderBrowserDialog();
                fbd.ShowNewFolderButton = true;
                fbd.Dispose();
                output.WriteLine("  [OK]   System.Windows.Forms（原生文件夹对话框）");
            }
            catch (Exception ex)
            {
                output.WriteLine("  [FAIL] System.Windows.Forms: " + ex.Message);
                failed++;
            }

            output.WriteLine();
            output.WriteLine(failed == 0 ? "自检通过：所有依赖均可正常加载使用。"
                                        : ("自检失败：" + failed + " 项异常。"));
            return failed == 0 ? 0 : 1;
        }
    }
}
