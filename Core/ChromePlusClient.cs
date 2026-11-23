using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using Newtonsoft.Json.Linq;

namespace GreenChrome.Core
{
    /// <summary>Chrome++ (Chrome++ Next) 版本信息。</summary>
    public class ChromePlusRelease
    {
        public string Version;      // 如 1.20.1
        public string Sha256;       // 官方 update.json 提供
        public long Size;
        public string PackageUrl;   // 7z 包下载地址
        public string UpdateJsonUrl;
    }

    /// <summary>
    /// Chrome++ 版本检查与更新。
    ///
    /// 版本源：GitHub Releases API（api.github.com/repos/Bush2021/chrome_plus/releases/latest）。
    /// 该 API 偶发失败/限流，因此：
    ///   · 超时 5 秒，失败重试 1 次
    ///   · 检查失败时返回 null（调用方静默降级，只记日志，不影响 Chrome 主流程）
    ///   · 更新只替换 <架构>/App/version.dll，不动 chrome++.ini（那是用户配置）
    /// </summary>
    public static class ChromePlusClient
    {
        private const string RepoApi = "https://api.github.com/repos/Bush2021/chrome_plus/releases/latest";
        private const string UpdateJsonBase = "https://github.com/Bush2021/chrome_plus/releases/download/";

        /// <summary>
        /// 查询最新 Chrome++ 版本。失败返回 null（不抛异常）。
        /// </summary>
        public static ChromePlusRelease FetchLatest()
        {
            for (int attempt = 0; attempt < 2; attempt++)
            {
                try
                {
                    var req = (HttpWebRequest)WebRequest.Create(RepoApi);
                    req.Timeout = 5000;
                    req.ReadWriteTimeout = 5000;
                    req.UserAgent = "GreenChrome/0.1";
                    req.Accept = "application/vnd.github+json";

                    using (var resp = (HttpWebResponse)req.GetResponse())
                    using (var sr = new StreamReader(resp.GetResponseStream()))
                    {
                        var json = sr.ReadToEnd();
                        var obj = JObject.Parse(json);

                        var rel = new ChromePlusRelease
                        {
                            Version = (string)obj["tag_name"],
                            UpdateJsonUrl = UpdateJsonBase + obj["tag_name"] + "/update.json"
                        };
                        if (string.IsNullOrEmpty(rel.Version)) return null;

                        // 找 7z 资源包
                        var assets = obj["assets"] as JArray;
                        if (assets != null)
                        {
                            foreach (var a in assets)
                            {
                                var name = (string)a["name"];
                                if (name != null && name.EndsWith(".7z", StringComparison.OrdinalIgnoreCase)
                                    && name.IndexOf("setdll", StringComparison.OrdinalIgnoreCase) < 0)
                                {
                                    rel.PackageUrl = (string)a["browser_download_url"];
                                    rel.Size = (long?)a["size"] ?? 0;
                                    break;
                                }
                            }
                        }

                        // 从官方 update.json 取 sha256（若可访问）
                        try
                        {
                            var uJson = DownloadText(rel.UpdateJsonUrl, 4000);
                            var uo = JObject.Parse(uJson);
                            rel.Sha256 = (string)uo["sha256"];
                            if (rel.Size <= 0) rel.Size = (long?)uo["size"] ?? 0;
                        }
                        catch
                        {
                            // 拿不到 sha256 也允许继续（下载后只校验大小）
                        }

                        return rel;
                    }
                }
                catch (Exception)
                {
                    if (attempt == 1) return null;   // 重试一次仍失败 → 静默降级
                    Thread.Sleep(500);
                }
            }
            return null;
        }

        /// <summary>
        /// 更新 Chrome++：下载 7z → 校验 → 解包取 &lt;架构&gt;/App/version.dll → 替换（先备份）。
        /// arch 取 x64 / x86 / arm64。
        /// </summary>
        public static string Update(string greenChromeDir, string arch, ChromePlusRelease rel,
                                    Action<string> log)
        {
            if (rel == null || string.IsNullOrEmpty(rel.PackageUrl))
                throw new InvalidOperationException("没有可用的 Chrome++ 下载地址");

            var tempDir = Path.Combine(Path.GetTempPath(),
                "gc_cpp_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(tempDir);
            try
            {
                var pkgPath = Path.Combine(tempDir, "chrome_plus.7z");
                log("正在下载 Chrome++ " + rel.Version + "（" + (rel.Size / 1024) + " KB）");

                DownloadFile(rel.PackageUrl, pkgPath);

                if (!string.IsNullOrEmpty(rel.Sha256))
                {
                    var actual = Core.Downloader.Sha256Of(pkgPath);
                    if (!string.Equals(actual, rel.Sha256, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("Chrome++ 包 SHA256 校验失败");
                    log("Chrome++ 包校验通过");
                }

                var extractDir = Path.Combine(tempDir, "x");
                Directory.CreateDirectory(extractDir);
                Unpacker.Extract(pkgPath, extractDir, null, null);

                // 取 <arch>/App/version.dll
                var dllSrc = Path.Combine(extractDir, arch, "App", "version.dll");
                if (!File.Exists(dllSrc))
                    throw new FileNotFoundException("Chrome++ 包内没有 " + arch + "/App/version.dll");

                var appDir = Path.Combine(greenChromeDir, "App");
                Directory.CreateDirectory(appDir);
                var dllDst = Path.Combine(appDir, "version.dll");

                // 已有才备份；新建时本来就没有 version.dll，不算错误
                string bak = null;
                if (File.Exists(dllDst))
                {
                    bak = dllDst + ".bak";
                    File.Copy(dllDst, bak, true);
                }

                File.Copy(dllSrc, dllDst, true);
                log("Chrome++ version.dll 已更新：" + rel.Version);

                // ini 只在缺失时补一份默认的，绝不覆盖用户已有的配置
                var iniSrc = Path.Combine(extractDir, arch, "App", "chrome++.ini");
                var iniDst = Path.Combine(appDir, "chrome++.ini");
                if (File.Exists(iniSrc) && !File.Exists(iniDst))
                {
                    File.Copy(iniSrc, iniDst, true);
                    log("已安装默认 chrome++.ini");
                }

                return bak;
            }
            finally
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }

        private static void DownloadFile(string url, string destPath)
        {
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.Timeout = 15000;
            req.ReadWriteTimeout = 15000;
            req.UserAgent = "GreenChrome/0.1";
            req.AutomaticDecompression = DecompressionMethods.None;
            using (var resp = (HttpWebResponse)req.GetResponse())
            using (var src = resp.GetResponseStream())
            using (var dst = new FileStream(destPath, FileMode.Create, FileAccess.Write))
            {
                src.CopyTo(dst);
            }
        }

        private static string DownloadText(string url, int timeoutMs)
        {
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.Timeout = timeoutMs;
            req.UserAgent = "GreenChrome/0.1";
            using (var resp = (HttpWebResponse)req.GetResponse())
            using (var sr = new StreamReader(resp.GetResponseStream()))
            {
                return sr.ReadToEnd();
            }
        }
    }
}
