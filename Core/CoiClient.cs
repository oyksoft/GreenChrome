using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using Newtonsoft.Json;

namespace GreenChrome.Core
{
    /// <summary>单个版本通道的信息（与 Go 版 release 结构一致）。</summary>
    public class ReleaseInfo
    {
        public string error { get; set; }
        public string version { get; set; }
        public long size { get; set; }
        public string sha256 { get; set; }
        public List<string> urls { get; set; }
        public bool is_offline { get; set; }

        public bool IsUsable
        {
            get { return !string.IsNullOrEmpty(version) && string.IsNullOrEmpty(error); }
        }
    }

    /// <summary>
    /// 线上版本 API 客户端。唯一版本源：https://coi.oyk.pub/api
    ///
    /// 解析用系统自带的 JavaScriptSerializer（System.Web.Extensions），
    /// 不引入任何第三方 JSON 库，也不增加 exe 体积。
    /// </summary>
    public static class CoiClient
    {
        /// <summary>
        /// 拉取全部通道信息。超时 8 秒。返回 key → ReleaseInfo（如 win_dev_x64）。
        /// </summary>
        public static Dictionary<string, ReleaseInfo> Fetch(string apiUrl)
        {
            if (string.IsNullOrWhiteSpace(apiUrl))
                apiUrl = AppConfig.DefaultApi;

            var req = (HttpWebRequest)WebRequest.Create(apiUrl);
            req.Timeout = 8000;
            req.ReadWriteTimeout = 8000;
            req.UserAgent = "GreenChrome/0.1";
            req.Method = "GET";
            req.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;

            using (var resp = (HttpWebResponse)req.GetResponse())
            using (var reader = new StreamReader(resp.GetResponseStream(), System.Text.Encoding.UTF8))
            {
                var json = reader.ReadToEnd();
                var dict = JsonConvert.DeserializeObject<Dictionary<string, ReleaseInfo>>(json);
                return dict ?? new Dictionary<string, ReleaseInfo>();
            }
        }

        /// <summary>从 API 返回里按通道+架构取条目，如 win_dev_x64。</summary>
        public static ReleaseInfo Find(Dictionary<string, ReleaseInfo> all, string channel, string arch)
        {
            if (all == null || string.IsNullOrEmpty(channel))
                return null;
            if (string.IsNullOrEmpty(arch)) arch = "x64";
            var key = "win_" + channel + "_" + arch;
            ReleaseInfo rel;
            return all.TryGetValue(key, out rel) ? rel : null;
        }
    }
}
