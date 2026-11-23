using System.Net;

namespace GreenChrome.Core
{
    /// <summary>
    /// 统一的 HTTP 请求头设置。
    /// 代理已不启用：版本接口和 Chrome 官方包都走直连，GitHub 走国内镜像。
    /// </summary>
    public static class Http
    {
        /// <summary>设置常见请求头（有的站点会拒绝没有 UA 的请求）。</summary>
        public static void ApplyHeaders(HttpWebRequest req, string userAgent = "GreenChrome/0.1")
        {
            if (req == null) return;
            try { req.UserAgent = userAgent; } catch { }
            try { req.Accept = "*/*"; } catch { }
        }
    }
}
