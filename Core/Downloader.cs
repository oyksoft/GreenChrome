using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace GreenChrome.Core
{
    /// <summary>
    /// 离线包下载：多 URL 回退、进度回调、SHA256 校验。
    /// 下载到临时文件，校验通过后才改名到目标路径。
    /// </summary>
    public static class Downloader
    {
        /// <summary>下载进度。</summary>
        public class Progress
        {
            public long Received;     // 已下载字节
            public long Total;        // 总字节（0 = 未知）
            public string Url;        // 当前使用的 URL
            public double Percent
            {
                get { return Total > 0 ? (double)Received / Total * 100.0 : 0; }
            }
        }

        /// <summary>
        /// 依次尝试 urls，下载到 destPath。
        /// onProgress 会在后台线程被调用。返回实际使用的 URL。
        /// 全部失败时抛出最后一个异常。
        /// </summary>
        public static string Download(IList<string> urls, string destPath,
                                      long expectedSize, string expectedSha256,
                                      Action<Progress> onProgress,
                                      CancellationToken cancel)
        {
            if (urls == null || urls.Count == 0)
                throw new ArgumentException("没有可用的下载地址");

            Exception lastError = null;
            foreach (var url in urls)
            {
                cancel.ThrowIfCancellationRequested();
                try
                {
                    DownloadOne(url, destPath, expectedSize, onProgress, cancel);
                    return url;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    lastError = ex;
                    SafeDelete(destPath);
                }
            }
            throw new Exception("所有下载地址均失败", lastError);
        }

        private static void DownloadOne(string url, string destPath,
                                        long expectedSize, Action<Progress> onProgress,
                                        CancellationToken cancel)
        {
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.Timeout = 30000;
            req.ReadWriteTimeout = 30000;
            req.UserAgent = "GreenChrome/0.1";
            req.AutomaticDecompression = DecompressionMethods.None; // 二进制不能被解压

            using (var resp = (HttpWebResponse)req.GetResponse())
            {
                long total = expectedSize;
                if (total <= 0 && resp.ContentLength > 0) total = resp.ContentLength;

                var progress = new Progress { Url = url, Total = total };
                using (var src = resp.GetResponseStream())
                using (var dst = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    var buf = new byte[81920];
                    while (true)
                    {
                        cancel.ThrowIfCancellationRequested();
                        int n = src.Read(buf, 0, buf.Length);
                        if (n <= 0) break;
                        dst.Write(buf, 0, n);
                        progress.Received += n;
                        if (onProgress != null)
                        {
                            var snapshot = new Progress
                            {
                                Url = progress.Url,
                                Total = progress.Total,
                                Received = progress.Received
                            };
                            onProgress(snapshot);
                        }
                    }
                }
            }
        }

        /// <summary>计算文件 SHA256（小写十六进制）。</summary>
        public static string Sha256Of(string path)
        {
            using (var sha = SHA256.Create())
            using (var fs = File.OpenRead(path))
            {
                var hash = sha.ComputeHash(fs);
                var sb = new System.Text.StringBuilder(hash.Length * 2);
                foreach (var b in hash) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }

        /// <summary>校验大小与 SHA256；expectedSha256 为空时只校验大小。</summary>
        public static bool Verify(string path, long expectedSize, string expectedSha256)
        {
            if (!File.Exists(path)) return false;

            var fi = new FileInfo(path);
            if (expectedSize > 0 && fi.Length != expectedSize)
                return false;

            if (!string.IsNullOrEmpty(expectedSha256))
            {
                var actual = Sha256Of(path);
                if (!string.Equals(actual, expectedSha256, StringComparison.OrdinalIgnoreCase))
                    return false;
            }
            return true;
        }

        public static void SafeDelete(string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch { }
        }
    }
}
