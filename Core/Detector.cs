using System;
using System.IO;

namespace GreenChrome.Core
{
    /// <summary>
    /// 绿色版 Chrome 目录探测。判定依据与实际布局（App/chrome.exe、App/&lt;版本&gt;/chrome.dll）。
    /// 与 Go 版 detect 包行为一致。
    /// </summary>
    public static class Detector
    {
        /// <summary>
        /// 判断 dir 是不是一套有效的绿色版 Chrome 目录：
        /// 目录存在且 App\chrome.exe 存在。
        /// </summary>
        public static bool IsGreenChromeDir(string dir)
        {
            if (string.IsNullOrWhiteSpace(dir))
                return false;
            try
            {
                var mainExe = Path.Combine(dir, "App", "chrome.exe");
                return File.Exists(mainExe);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 从 App\&lt;版本&gt;\chrome.dll 的目录名推断版本号。
        /// 读不到返回空字符串。
        /// </summary>
        public static string ChromeVersion(string dir)
        {
            if (string.IsNullOrWhiteSpace(dir))
                return "";
            try
            {
                var appDir = Path.Combine(dir, "App");
                if (!Directory.Exists(appDir))
                    return "";

                foreach (var sub in Directory.GetDirectories(appDir))
                {
                    var name = Path.GetFileName(sub);
                    // 版本号形如 160.0.8171.0：至少两个点
                    int dots = 0;
                    foreach (char ch in name)
                        if (ch == '.') dots++;
                    if (dots < 2) continue;

                    if (File.Exists(Path.Combine(sub, "chrome.dll")))
                        return name;
                }
            }
            catch
            {
                // 权限不足等，按未检测处理
            }
            return "";
        }

        /// <summary>
        /// 探测 CPU 位数：读 chrome.dll 的 PE 头 Machine 字段。
        /// 返回 x64 / x86 / arm64，探测失败返回“未知”。
        /// </summary>
        public static string Arch(string dir)
        {
            var dll = FindChromeDll(dir);
            if (dll == null)
                return "未知";

            try
            {
                using (var fs = new FileStream(dll, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var br = new BinaryReader(fs))
                {
                    // DOS 头："MZ"
                    fs.Position = 0;
                    var mz = br.ReadBytes(2);
                    if (mz.Length < 2 || mz[0] != 'M' || mz[1] != 'Z')
                        return "未知";

                    // e_lfanew 在偏移 0x3C
                    fs.Position = 0x3C;
                    int peOffset = br.ReadInt32();

                    // NT 头：4 字节签名 + Machine(uint16)
                    fs.Position = peOffset + 4;
                    ushort machine = br.ReadUInt16();

                    switch (machine)
                    {
                        case 0x8664: return "x64";
                        case 0x014C: return "x86";
                        case 0xAA64: return "arm64";
                        default: return "未知";
                    }
                }
            }
            catch
            {
                return "未知";
            }
        }

        private static string FindChromeDll(string dir)
        {
            if (string.IsNullOrWhiteSpace(dir))
                return null;
            try
            {
                var appDir = Path.Combine(dir, "App");
                if (!Directory.Exists(appDir))
                    return null;

                foreach (var sub in Directory.GetDirectories(appDir))
                {
                    var dll = Path.Combine(sub, "chrome.dll");
                    if (File.Exists(dll))
                        return dll;
                }
            }
            catch { }
            return null;
        }
    }
}
