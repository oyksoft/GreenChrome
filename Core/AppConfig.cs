using System;
using System.Collections.Generic;
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

        /// <summary>
        /// 版本来源。固定使用 <see cref="DefaultApi"/>，不需要用户配置；
        /// 这个属性只为与 Go 版共用同一份配置文件而保留，不写入文件。
        /// </summary>
        [JsonProperty("api", NullValueHandling = NullValueHandling.Ignore)]
        public string Api { get; set; }

        /// <summary>
        /// 代理预留字段（形如 http://127.0.0.1:7890），当前版本不使用。
        /// 版本接口（coi.oyk.pub）和 Chrome 官方包（dl.google.com）都走直连，
        /// GitHub（Chrome++）走国内镜像，镜像失败再回退官方地址。
        /// 保留是为了与 Go 版共用同一份配置文件。
        /// </summary>
        [JsonProperty("proxy")]
        public string Proxy { get; set; }

        /// <summary>启动时是否自动检查更新。</summary>
        [JsonProperty("autoCheck")]
        public bool AutoCheck { get; set; }

        /// <summary>
        /// GitHub 国内镜像前缀，按顺序优先尝试，最后回退官方地址。
        /// 留空 / 缺省则用 <see cref="DefaultGhMirrors"/>。
        /// </summary>
        [JsonProperty("ghMirrors", NullValueHandling = NullValueHandling.Ignore)]
        public string[] GhMirrors { get; set; }

        /// <summary>内置的 GitHub 镜像（实测可用）。</summary>
        public static readonly string[] DefaultGhMirrors =
        {
            "https://gh-proxy.com",
            "https://ghproxy.net"
        };

        /// <summary>实际使用的镜像列表。</summary>
        [JsonIgnore]
        public string[] MirrorList
        {
            get
            {
                if (GhMirrors == null || GhMirrors.Length == 0) return DefaultGhMirrors;
                var list = new List<string>();
                foreach (var m in GhMirrors)
                    if (!string.IsNullOrWhiteSpace(m)) list.Add(m.Trim());
                return list.Count > 0 ? list.ToArray() : DefaultGhMirrors;
            }
        }

        public AppConfig()
        {
            ChromeDir = "";
            Channel = DefaultChannel;
            Api = null;          // 固定用 DefaultApi，不写入配置文件
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
        /// 文件损坏时直接用默认配置启动（不保留坏文件），
        /// 警告文案通过 warning 返回，由界面写日志。
        /// </summary>
        public static AppConfig Load()
        {
            string ignored;
            return Load(out ignored);
        }

        public static AppConfig Load(out string warning)
        {
            warning = null;
            var path = FilePath;
            if (!File.Exists(path))
                return new AppConfig();

            AppConfig cfg;
            try
            {
                var json = File.ReadAllText(path, Encoding.UTF8);
                cfg = JsonConvert.DeserializeObject<AppConfig>(json) ?? new AppConfig();
            }
            catch (Exception ex)
            {
                warning = "配置文件格式有误，本次按默认配置启动（" + ex.Message + "）";
                return new AppConfig();
            }

            // 用默认值补全缺失字段（与 Go 版一致）
            if (string.IsNullOrEmpty(cfg.Channel)) cfg.Channel = DefaultChannel;
            cfg.Api = null;   // 固定用 DefaultApi，不从文件里读
            if (cfg.ChromeDir == null) cfg.ChromeDir = "";
            if (cfg.Proxy == null) cfg.Proxy = "";
            return cfg;
        }

        /// <summary>写入配置（缩进格式，便于用户查看与手改）。</summary>
        public void Save()
        {
            // NullValueHandling / DefaultValueHandling 都忽略：
            // 未使用的字段（api/proxy/autoCheck）与空值不会写进配置文件
            var json = JsonConvert.SerializeObject(this, Formatting.Indented,
                new JsonSerializerSettings
                {
                    NullValueHandling = NullValueHandling.Ignore,
                    DefaultValueHandling = DefaultValueHandling.Ignore
                });
            File.WriteAllText(FilePath, json + "\n", new UTF8Encoding(false));
        }
    }
}
