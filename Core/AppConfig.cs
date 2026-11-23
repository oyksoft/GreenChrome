using System;
using System.IO;
using System.Reflection;
using System.Text;
using Newtonsoft.Json;

namespace GreenChrome.Core
{
    /// <summary>
    /// 持久化配置。文件为 exe 同目录下的 greenchrome.conf（JSON）。
    /// 字段与 Go 版完全一致，两个版本可以共用同一份配置文件。
    /// </summary>
    public class AppConfig
    {
        public const string DefaultApi = "https://coi.oyk.pub/api";
        public const string DefaultChannel = "stable";
        public const string FileName = "greenchrome.conf";

        /// <summary>绑定的绿色版 Chrome 目录；空字符串 = 未绑定。</summary>
        [JsonProperty("chromeDir")]
        public string ChromeDir { get; set; }

        /// <summary>默认通道：stable / beta / dev / canary。</summary>
        [JsonProperty("channel")]
        public string Channel { get; set; }

        /// <summary>版本来源。</summary>
        [JsonProperty("api")]
        public string Api { get; set; }

        /// <summary>代理，空字符串 = 不用代理。</summary>
        [JsonProperty("proxy")]
        public string Proxy { get; set; }

        /// <summary>启动时是否自动检查更新。</summary>
        [JsonProperty("autoCheck")]
        public bool AutoCheck { get; set; }

        public AppConfig()
        {
            ChromeDir = "";
            Channel = DefaultChannel;
            Api = DefaultApi;
            Proxy = "";
            AutoCheck = false;
        }

        /// <summary>配置文件绝对路径：exe 同目录（不写注册表、不进 AppData）。</summary>
        public static string FilePath
        {
            get
            {
                var exe = Assembly.GetExecutingAssembly().Location;
                var dir = Path.GetDirectoryName(exe);
                return Path.Combine(string.IsNullOrEmpty(dir) ? "." : dir, FileName);
            }
        }

        /// <summary>
        /// 读取配置。文件不存在时返回默认值（首次使用，不报错）。
        /// 文件损坏时抛异常，由调用方提示并降级为默认值。
        /// </summary>
        public static AppConfig Load()
        {
            var path = FilePath;
            if (!File.Exists(path))
                return new AppConfig();

            var json = File.ReadAllText(path, Encoding.UTF8);
            var cfg = JsonConvert.DeserializeObject<AppConfig>(json) ?? new AppConfig();

            // 用默认值补全缺失字段（与 Go 版一致）
            if (string.IsNullOrEmpty(cfg.Channel)) cfg.Channel = DefaultChannel;
            if (string.IsNullOrEmpty(cfg.Api)) cfg.Api = DefaultApi;
            if (cfg.ChromeDir == null) cfg.ChromeDir = "";
            if (cfg.Proxy == null) cfg.Proxy = "";
            return cfg;
        }

        /// <summary>写入配置（缩进格式，便于用户查看与手改）。</summary>
        public void Save()
        {
            var json = JsonConvert.SerializeObject(this, Formatting.Indented);
            File.WriteAllText(FilePath, json + "\n", new UTF8Encoding(false));
        }
    }
}
