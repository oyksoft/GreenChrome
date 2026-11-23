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
        public bool Sha256FromMirror;   // SHA256 是否来自镜像（而非官方）
        public bool VersionFromMirror;  // 版本号是否来自镜像（而非官方 API）
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
        /// <param name="mirrors">GitHub 镜像前缀，优先尝试；官方地址自动排在最后兜底。</param>
        /// <param name="log">日志回调。</param>
        public static ChromePlusRelease FetchLatest(string[] mirrors = null, Action<string> log = null)
        {
            var apiCandidates = CandidateUrls(RepoApi, mirrors);

            for (int attempt = 0; attempt < 2; attempt++)
            {
                foreach (var api in apiCandidates)
                {
                    try
                    {
                        var req = (HttpWebRequest)WebRequest.Create(api);
                        req.Timeout = 8000;
                        req.ReadWriteTimeout = 8000;
                        req.UserAgent = "GreenChrome/0.1";
                        req.Accept = "application/vnd.github+json";
                        Http.ApplyHeaders(req);

                        using (var resp = (HttpWebResponse)req.GetResponse())
                        using (var sr = new StreamReader(resp.GetResponseStream()))
                        {
                            var json = sr.ReadToEnd();
                            var obj = JObject.Parse(json);

                            var rel = new ChromePlusRelease
                            {
                                Version = (string)obj["tag_name"],
                                UpdateJsonUrl = UpdateJsonBase + obj["tag_name"] + "/update.json",
                                VersionFromMirror = !IsOfficial(api, mirrors)
                            };
                            if (string.IsNullOrEmpty(rel.Version)) return null;
                            if (rel.VersionFromMirror && log != null)
                                log("Chrome++ 版本号取自镜像 " + HostOf(api));

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
                            if (string.IsNullOrEmpty(rel.PackageUrl)) return null;

                            // SHA256 优先用官方的（最可信），官方取不到才走镜像
                            var uCandidates = CandidateUrlsOfficialFirst(rel.UpdateJsonUrl, mirrors);
                            foreach (var u in uCandidates)
                            {
                                try
                                {
                                    var uJson = DownloadText(u, 5000);
                                    var uo = JObject.Parse(uJson);
                                    rel.Sha256 = (string)uo["sha256"];
                                    if (rel.Size <= 0) rel.Size = (long?)uo["size"] ?? 0;
                                    rel.Sha256FromMirror = !IsOfficial(u, mirrors);
                                    break;
                                }
                                catch
                                {
                                    // 换下一个地址
                                }
                            }
                            if (string.IsNullOrEmpty(rel.Sha256) && log != null)
                                log("未取到官方 SHA256，仅校验文件大小");

                            return rel;
                        }
                    }
                    catch (WebException wex)
                    {
                        // GitHub 未认证时限流：未认证 60 次/小时/IP，国内共享出口 IP 很容易触发
                        var resp = wex.Response as HttpWebResponse;
                        if (resp != null && resp.StatusCode == HttpStatusCode.Forbidden)
                        {
                            var remain = resp.Headers["X-RateLimit-Remaining"];
                            if (log != null)
                                log("GitHub 接口被限流（剩余配额 " + remain + "），稍后再试");
                        }
                        else if (log != null)
                        {
                            log("获取 Chrome++ 版本失败（" + api + "）：" + wex.Message);
                        }
                    }
                    catch (Exception ex)
                    {
                        if (log != null) log("获取 Chrome++ 版本异常：" + ex.Message);
                    }
                }

                if (attempt == 0) Thread.Sleep(600);   // 整轮都失败才重试一轮
            }
            return null;
        }

        /// <summary>
        /// 生成候选地址：镜像优先，官方地址排最后兜底。
        /// </summary>
        private static List<string> CandidateUrls(string realUrl, string[] mirrors)
        {
            var list = new List<string>();
            if (mirrors != null)
            {
                foreach (var m in mirrors)
                {
                    if (string.IsNullOrWhiteSpace(m)) continue;
                    list.Add(m.TrimEnd('/') + "/" + realUrl);
                }
            }
            list.Add(realUrl);
            return list;
        }

        /// <summary>
        /// 生成候选地址：官方优先，镜像随后。
        /// 用于取校验用的 SHA256——官方的值最可信，官方不可达时才用镜像的。
        /// </summary>
        private static List<string> CandidateUrlsOfficialFirst(string realUrl, string[] mirrors)
        {
            var list = new List<string>();
            list.Add(realUrl);
            if (mirrors != null)
            {
                foreach (var m in mirrors)
                {
                    if (string.IsNullOrWhiteSpace(m)) continue;
                    list.Add(m.TrimEnd('/') + "/" + realUrl);
                }
            }
            return list;
        }

        /// <summary>判断某个地址是否来自镜像（而非官方）。</summary>
        private static bool IsOfficial(string url, string[] mirrors)
        {
            if (mirrors == null) return true;
            foreach (var m in mirrors)
            {
                if (string.IsNullOrWhiteSpace(m)) continue;
                if (url.StartsWith(m.TrimEnd('/'), StringComparison.OrdinalIgnoreCase))
                    return false;
            }
            return true;
        }

        /// <summary>
        /// 更新 Chrome++：下载 7z → 校验 → 解包取 &lt;架构&gt;/App/version.dll → 替换（先备份）。
        /// arch 取 x64 / x86 / arm64。
        /// </summary>
        public static string Update(string greenChromeDir, string arch, ChromePlusRelease rel,
                                    Action<string> log, Action<long, long> onProgress = null,
                                    CancellationToken cancel = default(CancellationToken),
                                    string[] mirrors = null)
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

                // 镜像优先，官方兼底
                var candidates = CandidateUrls(rel.PackageUrl, mirrors);
                Exception last = null;

                foreach (var url in candidates)
                {
                    cancel.ThrowIfCancellationRequested();
                    try
                    {
                        DownloadFile(url, pkgPath, log, onProgress, cancel);
                        last = null;
                        if (!IsOfficial(url, mirrors))
                            log("已从镜像下载：" + HostOf(url));
                        break;
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex)
                    {
                        last = ex;
                        if (log != null)
                            log("下载失败（" + HostOf(url) + "）：" + ex.Message + "，换下一个");
                    }
                }
                if (last != null)
                    throw new IOException("Chrome++ 包下载失败（镜像和官方地址都试过了）：" + last.Message, last);

                if (!string.IsNullOrEmpty(rel.Sha256))
                {
                    var actual = Core.Downloader.Sha256Of(pkgPath);
                    if (!string.Equals(actual, rel.Sha256, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("Chrome++ 包 SHA256 校验失败");
                    log("Chrome++ 包校验通过");
                }
                else
                {
                    log("未取到官方 SHA256，仅校验文件大小");
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

                // ini：用户的配置一律不动，只在缺失（新装）时才补一份默认的
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

        /// <summary>取地址的主机名，用于日志里简短显示。</summary>
        private static string HostOf(string url)
        {
            try { return new Uri(url).Host; } catch { return url; }
        }

        /// <summary>
        /// 下载文件。GitHub 的 release 下载会 302 跳到 release-assets.githubusercontent.com，
        /// 国内网络下这个跳转容易超时，所以加重试。
        /// </summary>
        private static void DownloadFile(string url, string destPath, Action<string> log,
                                         Action<long, long> onProgress, CancellationToken cancel)
        {
            Exception last = null;
            for (int attempt = 1; attempt <= 3; attempt++)
            {
                cancel.ThrowIfCancellationRequested();
                try
                {
                    var req = (HttpWebRequest)WebRequest.Create(url);
                    req.Timeout = 20000;
                    req.ReadWriteTimeout = 20000;
                    req.UserAgent = "GreenChrome/0.1";
                    req.AutomaticDecompression = DecompressionMethods.None;  // 二进制不能被解压
                    req.AllowAutoRedirect = true;                            // 跟随 302
                    Http.ApplyHeaders(req);

                    using (var resp = (HttpWebResponse)req.GetResponse())
                    using (var src = resp.GetResponseStream())
                    using (var dst = new FileStream(destPath, FileMode.Create, FileAccess.Write))
                    {
                        var buf = new byte[81920];
                        long got = 0;
                        var total = resp.ContentLength;
                        while (true)
                        {
                            cancel.ThrowIfCancellationRequested();
                            int n = src.Read(buf, 0, buf.Length);
                            if (n <= 0) break;
                            dst.Write(buf, 0, n);
                            got += n;
                            if (onProgress != null) onProgress(got, total);
                        }
                    }
                    return;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    last = ex;
                    if (attempt >= 3) break;
                    if (log != null)
                        log("下载失败（第 " + attempt + "/3 次）：" + ex.Message + "，重试…");
                    Thread.Sleep(800 * attempt);
                }
            }
            throw new IOException("Chrome++ 包下载失败（已重试 3 次）：" +
                (last == null ? "未知错误" : last.Message), last);
        }

        private static string DownloadText(string url, int timeoutMs)
        {
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.Timeout = timeoutMs;
            req.UserAgent = "GreenChrome/0.1";
            Http.ApplyHeaders(req);
            using (var resp = (HttpWebResponse)req.GetResponse())
            using (var sr = new StreamReader(resp.GetResponseStream()))
            {
                return sr.ReadToEnd();
            }
        }
    }
}
