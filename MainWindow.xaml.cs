using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using GreenChrome.Core;
using GreenChrome.UI;

namespace GreenChrome
{
    public partial class MainWindow : Window
    {
        private AppConfig _cfg = new AppConfig();

        public MainWindow()
        {
            InitializeComponent();
            // 在 Loaded 里做初始化：此时 XAML 元素已全部就绪，
            // 构造函数阶段操作 ListBox 等控件可能不生效。
            Loaded += MainWindow_Loaded;
        }

        private bool _initializing = true;

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            LoadConfig();
            SyncChannelChips();
            UpdateView();
            UpdateUpgradeButton();   // 未检查前禁用
            _initializing = false;
        }

        /// <summary>按配置里的 channel 选中对应 chip（初始化阶段不落盘）。</summary>
        private void SyncChannelChips()
        {
            switch ((_cfg.Channel ?? "").ToLowerInvariant())
            {
                case "beta": ChipBeta.IsChecked = true; break;
                case "dev": ChipDev.IsChecked = true; break;
                case "canary": ChipCanary.IsChecked = true; break;
                default: ChipStable.IsChecked = true; break;
            }
        }

        /// <summary>用户切换通道：记入配置并保存。</summary>
        private void ChannelChip_Checked(object sender, RoutedEventArgs e)
        {
            if (_initializing || _cfg == null) return;
            var chip = sender as System.Windows.Controls.RadioButton;
            if (chip == null) return;
            var ch = chip.Tag as string;
            if (string.IsNullOrEmpty(ch) || ch == _cfg.Channel) return;

            _cfg.Channel = ch;
            try
            {
                _cfg.Save();
                AddLog("通道已切换为 " + ch);
            }
            catch (Exception ex)
            {
                AddLog("配置保存失败：" + ex.Message);
            }
        }

        // ================= 启动 =================
        private void LoadConfig()
        {
            try
            {
                _cfg = AppConfig.Load();
            }
            catch (Exception ex)
            {
                AddLog("读取配置失败（将使用默认值）：" + ex.Message);
                _cfg = new AppConfig();
            }

            // 绑定的目录若已失效（被删/挪走），回到引导页
            if (!string.IsNullOrEmpty(_cfg.ChromeDir) && !Detector.IsGreenChromeDir(_cfg.ChromeDir))
            {
                AddLog("绑定的目录 " + _cfg.ChromeDir + " 已找不到绿色版 Chrome，回到引导页");
                _cfg.ChromeDir = "";
            }

            if (!string.IsNullOrEmpty(_cfg.ChromeDir))
                AddLog("GreenChrome 已启动，绑定目录 " + _cfg.ChromeDir);
            else
                AddLog("GreenChrome 已启动，请先绑定绿色版 Chrome 目录");
        }

        private void UpdateView()
        {
            bool bound = !string.IsNullOrEmpty(_cfg.ChromeDir);
            bool showAbout = NavAbout != null && NavAbout.IsChecked == true;

            PageOnboarding.Visibility = (!bound && !showAbout) ? Visibility.Visible : Visibility.Collapsed;
            PageDashboard.Visibility = (bound && !showAbout) ? Visibility.Visible : Visibility.Collapsed;
            PageAbout.Visibility = showAbout ? Visibility.Visible : Visibility.Collapsed;

            if (bound)
                RefreshLocalInfo();
        }

        private void RefreshLocalInfo()
        {
            var ver = Detector.ChromeVersion(_cfg.ChromeDir);
            var arch = Detector.Arch(_cfg.ChromeDir);
            var verText = string.IsNullOrEmpty(ver) ? "未检测" : ver;
            LocalVersion.Text = verText + " · " + arch;
            LocalDir.Text = _cfg.ChromeDir;
        }

        // ================= 导航 =================
        private void NavDashboard_Checked(object sender, RoutedEventArgs e)
        {
            if (PageDashboard == null) return;
            UpdateView();
        }

        private void NavAbout_Checked(object sender, RoutedEventArgs e)
        {
            if (PageAbout == null) return;
            UpdateView();
        }

        // ================= 首次绑定 =================
        private void BtnPickExisting_Click(object sender, RoutedEventArgs e)
        {
            var path = PickDirectory();
            if (string.IsNullOrEmpty(path))
                return;

            if (!Detector.IsGreenChromeDir(path))
            {
                AddLog("目录 " + path + " 里没找到 App\\chrome.exe，不是绿色版 Chrome");
                MessageDialog.Show(this, DialogKind.Warn, "不是绿色版 Chrome",
                    "目录：" + path + "\n\n里面没找到 App\\chrome.exe，\n这不是一套绿色版 Chrome。\n\n请重新选择，或先用「从零制作一套」。");
                return;
            }

            BindDirectory(path);
        }

        private void BtnMakeFresh_Click(object sender, RoutedEventArgs e)
        {
            AddLog("从零制作流程尚未接入");
            MessageDialog.Show(this, DialogKind.Info, "尚未接入",
                "「从零制作一套」需要下载官方离线包并解包，\n这一步还在开发中。\n\n暂时请先用「选择已有目录」绑定一个现成的绿色版。");
        }

        // ================= 仪表盘操作 =================
        private void BtnChangeDir_Click(object sender, RoutedEventArgs e)
        {
            var path = PickDirectory();
            if (string.IsNullOrEmpty(path))
                return;

            if (!Detector.IsGreenChromeDir(path))
            {
                AddLog("目录 " + path + " 里没找到 App\\chrome.exe，不是绿色版 Chrome");
                MessageDialog.Show(this, DialogKind.Warn, "不是绿色版 Chrome",
                    "目录：" + path + "\n\n里面没找到 App\\chrome.exe，\n这不是一套绿色版 Chrome。");
                return;
            }

            BindDirectory(path);
        }

        private void BtnUnbind_Click(object sender, RoutedEventArgs e)
        {
            bool ok = MessageDialog.Confirm(this, DialogKind.Warn, "解除绑定",
                "确定要解除绑定吗？\n\n这不会删除你的绿色版文件，\n只是 GreenChrome 不再管理这个目录。",
                "解除绑定", "取消");
            if (!ok) return;

            _cfg.ChromeDir = "";
            try
            {
                _cfg.Save();
                AddLog("已解除绑定，回到引导页");
            }
            catch (Exception ex)
            {
                AddLog("配置保存失败：" + ex.Message);
                MessageDialog.Show(this, DialogKind.Error, "配置保存失败", ex.Message);
            }
            UpdateView();
        }

        private void BindDirectory(string path)
        {
            _cfg.ChromeDir = path;
            try
            {
                _cfg.Save();
                AddLog("已绑定绿色版目录 " + path);
                MessageDialog.Show(this, DialogKind.Info, "绑定成功", "已绑定绿色版目录：\n" + path);
            }
            catch (Exception ex)
            {
                AddLog("配置保存失败：" + ex.Message);
                MessageDialog.Show(this, DialogKind.Error, "配置保存失败",
                    "绑定成功，但配置保存失败：\n" + ex.Message);
            }
            UpdateView();
        }

        // ================= 检查更新 =================
        private async void BtnCheck_Click(object sender, RoutedEventArgs e)
        {
            if (_isChecking) return;
            _isChecking = true;

            BtnCheck.IsEnabled = false;
            ProgressWrap.Visibility = Visibility.Visible;
            SetStatus("状态：正在检查更新…", "ColAccent");
            AddLog("正在读取线上版本信息…");

            try
            {
                var data = await Task.Run(() => CoiClient.Fetch(_cfg.Api));

                VerStable.Text = DisplayOf(data, "stable");
                VerBeta.Text = DisplayOf(data, "beta");
                VerDev.Text = DisplayOf(data, "dev");
                VerCanary.Text = DisplayOf(data, "canary");
                _lastData = data;
                HighlightSelectedChannel();
                SetStatus("状态：已获取线上版本", "ColAccent");
                // 放在 SetStatus 之后：内部会按版本对比结果覆盖状态提示
                UpdateUpgradeButton();
                AddLog("已获取线上版本信息");
            }
            catch (Exception ex)
            {
                VerStable.Text = VerBeta.Text = VerDev.Text = VerCanary.Text = "获取失败";
                SetStatus("状态：检查失败", "ColDanger");
                AddLog("获取失败：" + ex.Message);
                MessageDialog.Show(this, DialogKind.Error, "获取失败", ex.Message);
            }
            finally
            {
                _isChecking = false;
                BtnCheck.IsEnabled = true;
                ProgressWrap.Visibility = Visibility.Collapsed;
            }
        }

        private Dictionary<string, ReleaseInfo> _lastData;

        /// <summary>
        /// 根据「本机版本 vs 所选通道的线上版本」决定「立即更新」按钮：
        /// 未检查 / 无法判定 → 禁用；版本相同 → 禁用并显示「已是最新」；
        /// 版本不同（含跨通道变旧）→ 启用并显示「应用到 x.x.x.x」。
        /// </summary>
        private void UpdateUpgradeButton()
        {
            BtnUpgrade.IsEnabled = false;
            BtnUpgrade.Content = "立即更新";

            if (_lastData == null) return;                     // 还没检查过
            if (string.IsNullOrEmpty(_cfg.ChromeDir)) return;  // 未绑定

            var localVer = Detector.ChromeVersion(_cfg.ChromeDir);
            if (string.IsNullOrEmpty(localVer))
            {
                BtnUpgrade.Content = "本机版本未知";
                return;
            }

            var rel = CoiClient.Find(_lastData, _cfg.Channel, "x64");
            if (rel == null || !rel.IsUsable)
            {
                BtnUpgrade.Content = "无可用版本";
                return;
            }

            var target = rel.version;
            if (SameVersion(localVer, target))
            {
                BtnUpgrade.Content = "已是最新";
                return;
            }

            BtnUpgrade.IsEnabled = true;
            BtnUpgrade.Content = "应用到 " + target;
            AddStatusHint(localVer, target);
        }

        /// <summary>状态行补充提示：线上比本机新 / 旧。</summary>
        private void AddStatusHint(string localVer, string target)
        {
            try
            {
                var cur = (System.Windows.Media.SolidColorBrush)FindResource("ColAccent");
                var local = new Version(localVer);
                var online = new Version(target);
                StatusText.Foreground = cur;
                StatusDot.Fill = cur;
                StatusText.Text = online > local
                    ? "状态：线上有新版本"
                    : "状态：线上版本较本机旧，应用将回退";
            }
            catch
            {
                // 版本号格式异常时忽略提示
            }
        }

        /// <summary>比较四段版本号是否完全相同。</summary>
        private static bool SameVersion(string a, string b)
        {
            if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase)) return true;
            try
            {
                return new Version(a) == new Version(b);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>把用户所选通道的版本号高亮（其余淡化）。</summary>
        private void HighlightSelectedChannel()
        {
            var normal = (System.Windows.Media.SolidColorBrush)FindResource("ColTextMuted");
            var accent = (System.Windows.Media.SolidColorBrush)FindResource("ColAccent");
            foreach (var pair in new[]
            {
                new { Ch = "stable", Tb = VerStable },
                new { Ch = "beta",   Tb = VerBeta },
                new { Ch = "dev",    Tb = VerDev },
                new { Ch = "canary", Tb = VerCanary }
            })
            {
                var match = string.Equals(pair.Ch, _cfg.Channel, StringComparison.OrdinalIgnoreCase);
                pair.Tb.Foreground = match ? accent : normal;
                pair.Tb.FontWeight = match ? FontWeights.Medium : FontWeights.Normal;
            }
        }

        private bool _isChecking;

        private static string DisplayOf(Dictionary<string, ReleaseInfo> data, string channel)
        {
            var rel = CoiClient.Find(data, channel, "x64");
            if (rel == null) return "无数据";
            if (!string.IsNullOrEmpty(rel.error)) return "错误";
            return string.IsNullOrEmpty(rel.version) ? "无数据" : rel.version;
        }

        private void BtnUpgrade_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_cfg.ChromeDir))
            {
                MessageDialog.Show(this, DialogKind.Warn, "尚未绑定目录", "请先绑定一个绿色版 Chrome 目录。");
                return;
            }
            AddLog("升级流程尚未接入");
            MessageDialog.Show(this, DialogKind.Info, "尚未接入",
                "升级需要下载官方离线包、解包并切换版本，\n这一步还在开发中。");
        }

        // ================= 日志 =================
        private void BtnClearLog_Click(object sender, RoutedEventArgs e)
        {
            LogList.Items.Clear();
            UpdateLogCount();
        }

        public void AddLog(string text)
        {
            if (LogList == null) return;
            LogList.Items.Add("[" + DateTime.Now.ToString("HH:mm:ss") + "] " + text);
            if (LogList.Items.Count > 0)
                LogList.ScrollIntoView(LogList.Items[LogList.Items.Count - 1]);
            UpdateLogCount();
        }

        private void UpdateLogCount()
        {
            LogCount.Text = "共 " + LogList.Items.Count + " 条";
        }

        // ================= 状态行 =================
        private void SetStatus(string text, string colorKey)
        {
            StatusText.Text = text;
            var brush = (SolidColorBrush)FindResource(colorKey);
            StatusText.Foreground = brush;
            StatusDot.Fill = brush;
        }

        // ================= 目录选择 =================
        private string PickDirectory()
        {
            var picker = new DirectoryPicker
            {
                Owner = this,
                InitialPath = ""
            };
            picker.ShowDialog();
            return picker.SelectedPath ?? "";
        }
    }
}
