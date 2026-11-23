using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
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
        private bool _uiReady = false;     // Loaded 之后才响应导航切换
        private int _currentPage = 0;      // 0 = 仪表盘，1 = 关于

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            LoadConfig();
            SyncChannelChips();
            UpdateView();
            UpdateUpgradeButton();   // 未检查前禁用
            _initializing = false;
            _uiReady = true;
        }

        /// <summary>把配置里的通道同步到分段控件与版本号高亮。</summary>
        private void UpdateChannelSelection()
        {
            var ch = (_cfg.Channel ?? "stable").ToLowerInvariant();
            switch (ch)
            {
                case "beta":   ChipBeta.IsChecked = true;   break;
                case "dev":    ChipDev.IsChecked = true;    break;
                case "canary": ChipCanary.IsChecked = true; break;
                default:       ChipStable.IsChecked = true; break;
            }
            HighlightSelectedChannel();
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

            // 同步界面：对比行、右侧高亮、按钮可用性与文案
            CompareChannel.Text = ch.ToUpperInvariant();
            if (_lastData != null)
            {
                var picked = CoiClient.Find(_lastData, ch, "x64");
                CompareOnline.Text = (picked != null && picked.IsUsable) ? picked.version : "无数据";
            }
            HighlightSelectedChannel();
            UpdateUpgradeButton();
        }

        // ================= 启动 =================
        private void LoadConfig()
        {
            string cfgWarning;
            _cfg = AppConfig.Load(out cfgWarning);
            if (!string.IsNullOrEmpty(cfgWarning))
                AddLog(cfgWarning);

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
            bool showAbout = _currentPage == 1;

            PageOnboarding.Visibility = (!bound && !showAbout) ? Visibility.Visible : Visibility.Collapsed;
            PageDashboard.Visibility = (bound && !showAbout) ? Visibility.Visible : Visibility.Collapsed;
            PageAbout.Visibility = showAbout ? Visibility.Visible : Visibility.Collapsed;

            UpdateApiLink();

            if (bound)
                RefreshLocalInfo();
        }

        /// <summary>
        /// 把超链接指向配置里实际的版本来源（不写死域名）。
        /// </summary>
        private void UpdateApiLink()
        {
            var api = string.IsNullOrWhiteSpace(_cfg.Api) ? AppConfig.DefaultApi : _cfg.Api;
            Uri site;
            try
            {
                // 从 https://coi.oyk.pub/api 取到 https://coi.oyk.pub
                site = new Uri(new Uri(api).GetLeftPart(UriPartial.Authority));
            }
            catch
            {
                site = new Uri("https://coi.oyk.pub");
            }

            if (ApiLink != null)
            {
                ApiLink.NavigateUri = site;
                ApiLink.ToolTip = "打开 " + site + "\\n版本来源：" + api;
            }
            if (ApiLinkAbout != null)
            {
                Uri full;
                try { full = new Uri(api); } catch { full = site; }
                ApiLinkAbout.NavigateUri = full;
                ApiLinkAbout.ToolTip = "打开 " + full;
            }
        }

        /// <summary>用系统默认浏览器打开超链接。</summary>
        private void Hyperlink_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
                e.Handled = true;
            }
            catch (Exception ex)
            {
                AddLog("打开链接失败：" + ex.Message);
            }
        }

        private void RefreshLocalInfo()
        {
            Ui(RefreshLocalInfoCore);
        }

        private void RefreshLocalInfoCore()
        {
            var ver = Detector.ChromeVersion(_cfg.ChromeDir);
            var arch = Detector.Arch(_cfg.ChromeDir);
            var verText = string.IsNullOrEmpty(ver) ? "未检测" : ver;
            LocalVersion.Text = verText + " · " + arch;

            var cpVer = Detector.ChromePlusVersion(_cfg.ChromeDir);
            var cpName = Detector.ChromePlusName(_cfg.ChromeDir);
            if (string.IsNullOrEmpty(cpVer))
                LocalChromePlus.Text = "未检测";
            else if (string.IsNullOrEmpty(cpName))
                LocalChromePlus.Text = cpVer;
            else
                LocalChromePlus.Text = cpName + " " + cpVer;

            LocalDir.Text = _cfg.ChromeDir;
            CompareLocal.Text = verText;
            CompareChannel.Text = (_cfg.Channel ?? "stable").ToUpperInvariant();
        }

        // ================= 导航 =================
        private void NavDashboard_Checked(object sender, RoutedEventArgs e)
        {
            if (!_uiReady) return;   // 初始化期间的事件忽略
            _currentPage = 0;
            UpdateView();
        }

        private void NavAbout_Checked(object sender, RoutedEventArgs e)
        {
            if (!_uiReady) return;
            _currentPage = 1;
            UpdateView();
        }

        // ================= 首次绑定 =================
        private void BtnPickExisting_Click(object sender, RoutedEventArgs e)
        {
            var path = PickExistingGreenDir();
            if (string.IsNullOrEmpty(path))
                return;

            BindDirectory(path);
        }

        private async void BtnMakeFresh_Click(object sender, RoutedEventArgs e)
        {
            // ---------- 第一步：选通道 ----------
            AddLog("正在获取各通道的线上版本…");
            Dictionary<string, ReleaseInfo> online = null;
            try
            {
                online = await Task.Run(() => CoiClient.Fetch(_cfg.Api));
            }
            catch (Exception ex)
            {
                AddLog("获取线上版本失败：" + ex.Message);
                MessageDialog.Show(this, DialogKind.Error, "无法获取版本信息",
                    "获取线上版本失败，制作需要先知道要下载哪个版本。\n\n" + ex.Message);
                return;
            }

            var channel = UI.ChannelPicker.Pick(this, online, _cfg.Channel);
            if (string.IsNullOrEmpty(channel))
            {
                AddLog("已取消制作");
                return;
            }
            AddLog("选择了通道 " + channel.ToUpperInvariant());

            // ---------- 第二步：选目录（选错了可以一直重选） ----------
            var path = PickFreshTargetDir();
            if (string.IsNullOrEmpty(path))
            {
                AddLog("已取消制作");
                return;
            }

            // ---------- 第三步：确认 ----------
            var relPreview = CoiClient.Find(online, channel, "x64");
            var sizeText = (relPreview != null && relPreview.IsUsable)
                ? (relPreview.size / 1048576.0).ToString("F0") + " MB"
                : "未知";
            var verText = (relPreview != null && relPreview.IsUsable) ? relPreview.version : "未知";

            // 目标目录里若已有 Chrome 在跑，先处理掉
            if (!await EnsureChromeClosedAsync(path))
            {
                AddLog("目标目录里有 Chrome 正在运行，已取消制作");
                return;
            }

            if (!MessageDialog.Confirm(this, DialogKind.Info, "从零制作一套",
                "将在下面这个目录里制作一套全新的绿色版 Chrome：\n\n" + path +
                "\n\n· 通道：" + channel.ToUpperInvariant() +
                "\n· 版本：" + verText +
                "\n· 需要下载约 " + sizeText + " 官方离线包" +
                "\n· 会写入 App\\<版本>\\、App\\chrome.exe、Chrome++" +
                "\n· 现有其它目录不会被改动" +
                "\n\n过程中请勿关闭窗口。现在开始吗？",
                "开始制作", "取消"))
                return;

            // 记住这次选的通道
            if (!string.Equals(_cfg.Channel, channel, StringComparison.OrdinalIgnoreCase))
            {
                _cfg.Channel = channel;
                try { _cfg.Save(); } catch { }
                UpdateChannelSelection();
            }

            _cancel = new CancellationTokenSource();
            BtnPickExisting.IsEnabled = false;
            BtnMakeFresh.IsEnabled = false;
            FreshProgress.Visibility = Visibility.Visible;
            ColFreshDone.Width = new GridLength(0);
            ColFreshRest.Width = new GridLength(1, GridUnitType.Star);
            FreshStatus.Text = "正在获取线上版本…";
            AddLog("开始从零制作：" + path + "（通道 " + channel + "）");

            try
            {
                var rel = CoiClient.Find(online, channel, "x64");
                if (rel == null || !rel.IsUsable)
                {
                    MessageDialog.Show(this, DialogKind.Warn, "无可用版本",
                        channel.ToUpperInvariant() + " 通道当前没有可用的 x64 版本信息，请稍后再试。");
                    return;
                }

                FreshStatus.Text = "准备下载 " + rel.version;
                AddLog("目标版本 " + rel.version + "（" + (rel.size / 1048576.0).ToString("F0") + " MB）");

                var progress = new FreshProgressReporter(this);
                var version = await Task.Run(() =>
                    ChromeUpdater.CreateFresh(path, rel, "x64", progress, _cancel.Token, _cfg.MirrorList));

                AddLog("制作完成，版本 " + version);
                FreshStatus.Text = "制作完成：" + version;
                BindDirectory(path, silent: true);
                MessageDialog.Show(this, DialogKind.Info, "制作完成",
                    "已在这个目录制作好一套绿色版 Chrome：\n\n" + path +
                    "\n\n版本：" + version + "\nChrome++ 已一并安装，用户配置会存放在 App\\chrome++.ini。\n\n可以直接运行 App\\chrome.exe。");
            }
            catch (OperationCanceledException)
            {
                AddLog("制作已取消");
                FreshStatus.Text = "已取消";
            }
            catch (Exception ex)
            {
                AddLog("制作失败：" + ex.Message);
                FreshStatus.Text = "制作失败：" + ex.Message;
                MessageDialog.Show(this, DialogKind.Error, "制作失败", ex.Message);
            }
            finally
            {
                BtnPickExisting.IsEnabled = true;
                BtnMakeFresh.IsEnabled = true;
                FreshProgress.Visibility = Visibility.Collapsed;
                _cancel = null;
            }
        }

        /// <summary>把制作进度转到引导页。</summary>
        private class FreshProgressReporter : ChromeUpdater.IProgress
        {
            private readonly MainWindow _w;
            public FreshProgressReporter(MainWindow w) { _w = w; }

            public void Log(string message) { _w.AddLog(message); }

            public void Status(string message)
            {
                _w.Ui(() => _w.FreshStatus.Text = message);
            }

            public void Stage(string stage)
            {
                _w.Ui(() => _w.FreshStatus.Text = "—— " + stage + " ——");
                _w.AddLog("—— " + stage + " ——");
            }

            public void DownloadProgress(long received, long total)
            {
                _w.Ui(() =>
                {
                    double pct = total > 0 ? (double)received / total : 0;
                    if (pct < 0) pct = 0;
                    if (pct > 1) pct = 1;

                    // 用列宽比例控制进度，不能靠 Border.Width（那样 ActualWidth 永远是 0）
                    _w.ColFreshDone.Width = new GridLength(pct, GridUnitType.Star);
                    _w.ColFreshRest.Width = new GridLength(1 - pct, GridUnitType.Star);

                    _w.FreshStatus.Text = total > 0
                        ? string.Format("正在下载  {0:F0}%   {1:F0} / {2:F0} MB", pct * 100,
                            received / 1048576.0, total / 1048576.0)
                        : string.Format("正在下载  {0:F0} MB", received / 1048576.0);
                });
            }
        }

        // ================= 仪表盘操作 =================
        private void BtnChangeDir_Click(object sender, RoutedEventArgs e)
        {
            var path = PickExistingGreenDir();
            if (string.IsNullOrEmpty(path))
                return;

            BindDirectory(path);
        }

        /// <summary>在资源管理器里打开绑定的绿色版目录。</summary>
        private void BtnOpenDir_Click(object sender, RoutedEventArgs e)
        {
            OpenFolder(_cfg.ChromeDir, "绿色版目录");
        }

        /// <summary>打开 Chrome 程序所在的 App 目录。</summary>
        private void BtnOpenAppDir_Click(object sender, RoutedEventArgs e)
        {
            OpenFolder(Path.Combine(_cfg.ChromeDir ?? "", "App"), "Chrome 程序目录");
        }

        /// <summary>打开用户数据目录 Data。</summary>
        private void BtnOpenDataDir_Click(object sender, RoutedEventArgs e)
        {
            OpenFolder(Path.Combine(_cfg.ChromeDir ?? "", "Data"), "用户数据目录");
        }

        /// <summary>运行这套绿色版 Chrome。</summary>
        private void BtnRunChrome_Click(object sender, RoutedEventArgs e)
        {
            var exe = Path.Combine(_cfg.ChromeDir ?? "", "App", "chrome.exe");
            if (!File.Exists(exe))
            {
                MessageDialog.Show(this, DialogKind.Warn, "找不到 Chrome",
                    "这个目录里没有 App\\chrome.exe。\n\n" + exe);
                return;
            }
            try
            {
                Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(exe) });
                AddLog("已启动 Chrome：" + exe);
            }
            catch (Exception ex)
            {
                AddLog("启动 Chrome 失败：" + ex.Message);
                MessageDialog.Show(this, DialogKind.Error, "启动失败", ex.Message);
            }
        }

        /// <summary>在资源管理器里打开一个目录。</summary>
        private void OpenFolder(string dir, string what)
        {
            if (string.IsNullOrEmpty(_cfg.ChromeDir))
            {
                MessageDialog.Show(this, DialogKind.Warn, "尚未绑定目录",
                    "还没有绑定绿色版 Chrome 目录。\n\n请先选择已有目录，或从零制作一套。");
                return;
            }
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
            {
                MessageDialog.Show(this, DialogKind.Warn, "目录不存在",
                    what + "不存在：\n\n" + dir +
                    "\n\n如果还没创建过，可以从零制作一套，或手动建好后再试。");
                return;
            }

            try
            {
                // UseShellExecute + 目录本身 = 打开资源管理器并定位到该目录
                Process.Start(new ProcessStartInfo(dir) { UseShellExecute = true });
                AddLog("已打开" + what + " " + dir);
            }
            catch (Exception ex)
            {
                AddLog("打开目录失败：" + ex.Message);
                MessageDialog.Show(this, DialogKind.Error, "打开失败", ex.Message);
            }
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

        private void BindDirectory(string path, bool silent = false)
        {
            _cfg.ChromeDir = path;
            try
            {
                _cfg.Save();
                AddLog("已绑定绿色版目录 " + path);
                if (!silent)
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
            ShowIndeterminateProgress();
            SetStatus("状态：正在检查更新…", "ColAccent");
            AddLog("正在读取线上版本信息…");

            try
            {
                // Chrome 与 Chrome++ 并行检查；Chrome++ 失败不影响主流程
                var cppTask = Task.Run(() => ChromePlusClient.FetchLatest(_cfg.MirrorList, m => AddLog(m)));
                var data = await Task.Run(() => CoiClient.Fetch(_cfg.Api));
                _cppRelease = await cppTask;         // 失败时为 null，静默降级
                if (_cppRelease != null)
                {
                    _cppLatestVersion = _cppRelease.Version ?? "";
                    AddLog("Chrome++ 最新版 " + _cppLatestVersion);
                }
                else
                {
                    AddLog("Chrome++ 版本检查失败（不影响 Chrome 更新）");
                }
                UpdateChromePlusLabel();

                VerStable.Text = DisplayOf(data, "stable");
                VerBeta.Text = DisplayOf(data, "beta");
                VerDev.Text = DisplayOf(data, "dev");
                VerCanary.Text = DisplayOf(data, "canary");
                _lastData = data;
                CompareChannel.Text = (_cfg.Channel ?? "stable").ToUpperInvariant();
                var picked = CoiClient.Find(data, _cfg.Channel, "x64");
                CompareOnline.Text = (picked != null && picked.IsUsable) ? picked.version : "无数据";
                HighlightSelectedChannel();
                SetStatus("状态：已获取线上版本", "ColAccent");
                // 放在 SetStatus 之后：内部会按版本对比结果覆盖状态提示
                UpdateUpgradeButton();
                AddLog("已获取线上版本信息");
            }
            catch (Exception ex)
            {
                VerStable.Text = VerBeta.Text = VerDev.Text = VerCanary.Text = "获取失败";
                CompareOnline.Text = "获取失败";
                SetStatus("状态：检查失败", "ColDanger");
                AddLog("获取失败：" + ex.Message);
                MessageDialog.Show(this, DialogKind.Error, "获取失败", ex.Message);
            }
            finally
            {
                _isChecking = false;
                BtnCheck.IsEnabled = true;
                HideProgress();
            }
        }

        private Dictionary<string, ReleaseInfo> _lastData;
        private ChromePlusRelease _cppRelease;      // Chrome++ 最新版（null = 未获取到）
        private string _cppLatestVersion = "";     // Chrome++ 线上最新版本号

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

        /// <summary>刷新 Chrome++ 行的显示：本地版本 + 是否有新版。</summary>
        private void UpdateChromePlusLabel()
        {
            if (LocalChromePlus == null) return;

            var local = Detector.ChromePlusVersion(_cfg.ChromeDir);
            if (string.IsNullOrEmpty(local))
            {
                LocalChromePlus.Text = "未检测";
                ChromePlusHint.Visibility = Visibility.Collapsed;
                BtnCppUpdate.Visibility = Visibility.Collapsed;
                return;
            }

            var name = Detector.ChromePlusName(_cfg.ChromeDir);
            var localText = string.IsNullOrEmpty(name) ? local : name + " " + local;
            LocalChromePlus.Text = localText;

            // 线上有新版才显示更新按钮；有新版的目标版本单独放一行，不挤在同一行
            if (!string.IsNullOrEmpty(_cppLatestVersion) && !SameVersion(local, _cppLatestVersion))
            {
                ChromePlusHint.Text = "可更新到 " + _cppLatestVersion;
                ChromePlusHint.Visibility = Visibility.Visible;
                BtnCppUpdate.Visibility = Visibility.Visible;
                BtnCppUpdate.Content = "更新";
                BtnCppUpdate.IsEnabled = true;
            }
            else
            {
                ChromePlusHint.Visibility = Visibility.Collapsed;
                BtnCppUpdate.Visibility = Visibility.Collapsed;
            }
        }

        /// <summary>更新 Chrome++（只替换 version.dll，不动 chrome++.ini）。</summary>
        private async void BtnCppUpdate_Click(object sender, RoutedEventArgs e)
        {
            if (_cppRelease == null)
            {
                MessageDialog.Show(this, DialogKind.Warn, "无法更新", "没有获取到 Chrome++ 的版本信息，请稍后再试。");
                return;
            }
            if (string.IsNullOrEmpty(_cfg.ChromeDir))
            {
                MessageDialog.Show(this, DialogKind.Warn, "尚未绑定目录", "请先绑定绿色版 Chrome 目录。");
                return;
            }

            // 替换 version.dll 时 Chrome 也在占用它
            if (!await EnsureChromeClosedAsync(_cfg.ChromeDir))
            {
                AddLog("Chrome 正在运行，已取消 Chrome++ 更新");
                return;
            }

            SetBusy(true);
            try
            {
                var arch = Detector.Arch(_cfg.ChromeDir);
                if (arch == "未知") arch = "x64";

                var log = new List<string>();
                var bak = await Task.Run(() => ChromePlusClient.Update(_cfg.ChromeDir, arch, _cppRelease, m =>
                {
                    log.Add(m);
                    AddLog(m);
                },
                (got, total) => SetProgress(got, total),
                _cancel != null ? _cancel.Token : CancellationToken.None,
                _cfg.MirrorList));

                AddLog("Chrome++ 已更新到 " + _cppRelease.Version);
                RefreshLocalInfo();
                UpdateChromePlusLabel();
                MessageDialog.Show(this, DialogKind.Info, "Chrome++ 已更新",
                    "Chrome++ 已更新到 " + _cppRelease.Version + "。\n\n你的 chrome++.ini 配置保持不变。");
            }
            catch (Exception ex)
            {
                AddLog("Chrome++ 更新失败：" + ex.Message);
                MessageDialog.Show(this, DialogKind.Error, "更新失败", ex.Message);
            }
            finally
            {
                HideProgress();
                SetBusy(false);
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

        private CancellationTokenSource _cancel;

        /// <summary>一组正在运行的、属于某套绿色版的进程（按可执行文件路径归组）。</summary>
        private class RunningProc
        {
            public string Path;                 // 可执行文件路径
            public List<int> Pids = new List<int>();

            public int Count { get { return Pids.Count; } }
        }

        /// <summary>
        /// 找出正在运行的、属于该绿色版目录的 Chrome 进程。
        /// 绿色版的 chrome.exe / chrome_proxy.exe 可能位于 App\ 或 App\&lt;版本&gt;\ 下，
        /// 只要 exe 所在目录在这套绿色版之内就算。
        /// </summary>
        private static List<RunningProc> RunningChromeIn(string greenDir)
        {
            var found = new List<RunningProc>();
            if (string.IsNullOrWhiteSpace(greenDir)) return found;

            string root;
            try
            {
                root = Path.GetFullPath(greenDir).TrimEnd('\\', '/');
            }
            catch
            {
                return found;
            }

            foreach (var name in new[] { "chrome", "chrome_proxy" })
            {
                Process[] procs;
                try { procs = Process.GetProcessesByName(name); }
                catch { continue; }

                foreach (var p in procs)
                {
                    using (p)
                    {
                        try
                        {
                            var file = p.MainModule.FileName;
                            if (string.IsNullOrEmpty(file)) continue;
                            var dir = Path.GetDirectoryName(file);
                            if (string.IsNullOrEmpty(dir)) continue;
                            dir = Path.GetFullPath(dir).TrimEnd('\\', '/');
                            if (!dir.StartsWith(root, StringComparison.OrdinalIgnoreCase)) continue;
                            if (dir.Length > root.Length && root.Length > 0 &&
                                dir[root.Length] != '\\' && dir[root.Length] != '/') continue;

                            // 同一个 exe 会有很多子进程，按路径归组
                            var group = found.Find(g => string.Equals(g.Path, file, StringComparison.OrdinalIgnoreCase));
                            if (group == null)
                            {
                                group = new RunningProc { Path = file };
                                found.Add(group);
                            }
                            group.Pids.Add(p.Id);
                        }
                        catch
                        {
                            // 拿不到别的用户的进程路径就跳过
                        }
                    }
                }
            }
            return found;
        }

        /// <summary>
        /// 更新前确认这套绿色版的 Chrome 没有在运行。
        /// 返回 true 表示可以继续；返回 false 表示用户取消，流程应终止。
        /// </summary>
        /// <summary>
        /// 更新前确认这套绿色版的 Chrome 没有在运行。
        /// 返回 true 表示可以继续；返回 false 表示用户取消或关不掉，流程应终止。
        ///
        /// 结束进程后不会立即返回：Chrome 的子进程被杀后主进程可能还在，
        /// 甚至会崩溃重启，所以要反复检测 + 重试，直到干净为止。
        /// </summary>
        private async Task<bool> EnsureChromeClosedAsync(string greenDir)
        {
            var groups = RunningChromeIn(greenDir);
            if (groups.Count == 0)
                return true;

            var total = groups.Sum(g => g.Count);
            var shown = groups.Select(g => "  · " + g.Path +
                (g.Count > 1 ? "   （" + g.Count + " 个进程）" : ""));
            var NL = "\n";

            var message =
                "检测到 " + total + " 个属于这套绿色版的 Chrome 进程正在运行：" + NL + NL +
                string.Join(NL, shown) + NL + NL +
                "更新需要替换 Chrome 程序文件，运行中会被占用导致失败。" + NL +
                "· 点「结束并继续」强制结束这些进程（未保存的内容会丢失）" + NL +
                "· 点「取消」请自己关闭 Chrome 后再回来更新";

            var ok = MessageDialog.Confirm(this, DialogKind.Warn, "Chrome 正在运行", message,
                "结束并继续", "取消");

            if (!ok)
                return false;

            // 最多试 5 轮：杀进程 → 轮询等待 → 还在就再杀
            for (int round = 1; round <= 5; round++)
            {
                var alive = RunningChromeIn(greenDir);
                if (alive.Count == 0)
                {
                    AddLog("Chrome 已全部关闭");
                    return true;
                }

                SetStatus("状态：正在关闭 Chrome（第 " + round + "/5 轮）…", "ColAccent");

                // 先给一次温和的机会：发关闭消息，让 Chrome 自己退
                foreach (var g in alive)
                {
                    foreach (var pid in g.Pids)
                    {
                        try
                        {
                            var p = Process.GetProcessById(pid);
                            if (!p.CloseMainWindow())
                                p.Kill();
                        }
                        catch { }
                    }
                }

                // 温和关闭给 3 秒
                for (int i = 0; i < 6 && RunningChromeIn(greenDir).Count > 0; i++)
                    await Task.Delay(500);

                // 还活着就强杀
                var still = RunningChromeIn(greenDir);
                if (still.Count > 0)
                {
                    foreach (var g in still)
                    {
                        foreach (var pid in g.Pids)
                        {
                            try
                            {
                                var p = Process.GetProcessById(pid);
                                p.Kill();
                                p.WaitForExit(3000);
                            }
                            catch { }
                        }
                    }
                    AddLog("已强制结束 " + still.Sum(g => g.Count) + " 个 Chrome 进程");
                }

                // 强杀后轮询等待，最多 10 秒，确认真的退干净了
                for (int i = 0; i < 20; i++)
                {
                    await Task.Delay(500);
                    if (RunningChromeIn(greenDir).Count == 0)
                    {
                        AddLog("Chrome 已全部关闭");
                        SetStatus("状态：Chrome 已关闭", "ColAccent");
                        return true;
                    }
                }
            }

            var left = RunningChromeIn(greenDir);
            var leftCount = left.Sum(g => g.Count);
            AddLog("Chrome 未能完全关闭，剩余 " + leftCount + " 个进程");
            SetStatus("状态：Chrome 未能关闭", "ColDanger");
            MessageDialog.Show(this, DialogKind.Error, "Chrome 没能关闭",
                "结束进程后等了 30 多秒，仍然检测到 Chrome 在运行：" + NL + NL +
                string.Join(NL, left.Select(g => "  · " + g.Path)) + NL + NL +
                "可能是浏览器崩溃后自动重启了。" + NL +
                "请手动完全退出 Chrome（必要时在任务管理器里结束）后再重试。");
            return false;
        }

        private async void BtnUpgrade_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_cfg.ChromeDir))
            {
                MessageDialog.Show(this, DialogKind.Warn, "尚未绑定目录", "请先绑定一个绿色版 Chrome 目录。");
                return;
            }
            if (_lastData == null)
            {
                MessageDialog.Show(this, DialogKind.Warn, "尚未检查更新", "请先点「检查更新」。");
                return;
            }

            var rel = CoiClient.Find(_lastData, _cfg.Channel, "x64");
            if (rel == null || !rel.IsUsable)
            {
                MessageDialog.Show(this, DialogKind.Warn, "无可用版本", "所选通道没有可用的版本信息。");
                return;
            }

            // Chrome 在运行会锁住程序文件，必须先关掉
            if (!await EnsureChromeClosedAsync(_cfg.ChromeDir))
            {
                AddLog("Chrome 正在运行，已取消更新");
                SetStatus("状态：Chrome 正在运行", "ColDanger");
                return;
            }

            // 确认：可能比本机旧（跨通道回退）
            var localVer = Detector.ChromeVersion(_cfg.ChromeDir);
            if (!string.IsNullOrEmpty(localVer) && !SameVersion(localVer, rel.version))
            {
                bool newer = IsNewer(rel.version, localVer);
                string msg = newer
                    ? "将把本机 Chrome 从 " + localVer + " 更新到 " + rel.version + "。\n\n过程中请勿运行 Chrome。"
                    : "线上版本 " + rel.version + " 比本机 " + localVer + " 更旧。\n\n应用后本机 Chrome 会回退到旧版本。\n\n确定继续吗？";
                if (!MessageDialog.Confirm(this, newer ? DialogKind.Info : DialogKind.Warn,
                    newer ? "确认更新" : "确认回退", msg, "确定", "取消"))
                    return;
            }

            _cancel = new CancellationTokenSource();
            SetBusy(true);
            try
            {
                var progress = new UpdaterProgress(this);
                var version = await Task.Run(() =>
                    ChromeUpdater.Apply(_cfg.ChromeDir, rel, progress, _cancel.Token));

                AddLog("已应用版本 " + version);
                SetStatus("状态：已更新到 " + version, "ColAccent");
                RefreshLocalInfo();
                MessageDialog.Show(this, DialogKind.Info, "更新完成",
                    "Chrome 已更新到 " + version + "。\n\n重新启动 Chrome 即可生效。");
            }
            catch (OperationCanceledException)
            {
                AddLog("操作已取消");
                SetStatus("状态：已取消", "ColTextMuted");
            }
            catch (Exception ex)
            {
                AddLog("更新失败：" + ex.Message);
                SetStatus("状态：更新失败", "ColDanger");
                MessageDialog.Show(this, DialogKind.Error, "更新失败", ex.Message);
            }
            finally
            {
                HideProgress();
                SetBusy(false);
                _cancel = null;
            }
        }

        /// <summary>把下载/解包进度转到界面。</summary>
        private class UpdaterProgress : ChromeUpdater.IProgress
        {
            private readonly MainWindow _w;
            public UpdaterProgress(MainWindow w) { _w = w; }

            public void Log(string message) { _w.AddLog(message); }
            public void Status(string message) { _w.SetStatus("状态：" + message, "ColAccent"); }
            public void Stage(string stage) { _w.AddLog("—— " + stage + " ——"); }
            public void DownloadProgress(long received, long total)
            {
                _w.SetProgress(received, total);
            }
        }

        private void SetBusy(bool busy)
        {
            BtnCheck.IsEnabled = !busy;
            BtnUpgrade.IsEnabled = !busy;
            if (!busy) UpdateUpgradeButton();
        }

        private void SetProgress(long received, long total)
        {
            Ui(() =>
            {
                ProgressRow.Visibility = Visibility.Visible;
                double pct = total > 0 ? (double)received / total : 0;
                if (pct < 0) pct = 0;
                if (pct > 1) pct = 1;

                // 用列宽比例控制进度，不能靠 Border.Width（那样 ActualWidth 永远是 0）
                ColProgressDone.Width = new GridLength(pct, GridUnitType.Star);
                ColProgressRest.Width = new GridLength(1 - pct, GridUnitType.Star);

                ProgressText.Text = total > 0
                    ? string.Format("{0:F0}%   {1:F0} / {2:F0} MB",
                        pct * 100, received / 1048576.0, total / 1048576.0)
                    : string.Format("{0:F0} MB", received / 1048576.0);
            });
        }

        /// <summary>
        /// 显示进度行但不知道进度（检查更新时用）：进度条走满格 + 文字说明。
        /// </summary>
        private void ShowIndeterminateProgress()
        {
            Ui(() =>
            {
                ProgressRow.Visibility = Visibility.Visible;
                ColProgressDone.Width = new GridLength(1, GridUnitType.Star);
                ColProgressRest.Width = new GridLength(0);
                ProgressText.Text = "正在获取线上版本信息…";
            });
        }

        /// <summary>隐藏下载进度行（下载结束后调用）。</summary>
        private void HideProgress()
        {
            Ui(() =>
            {
                ProgressRow.Visibility = Visibility.Collapsed;
                ProgressText.Text = "";
                ColProgressDone.Width = new GridLength(0);
                ColProgressRest.Width = new GridLength(1, GridUnitType.Star);
            });
        }

        private static bool IsNewer(string a, string b)
        {
            try { return new Version(a) > new Version(b); }
            catch { return false; }
        }

        // ================= 日志 =================
        private void BtnClearLog_Click(object sender, RoutedEventArgs e)
        {
            LogList.Items.Clear();
            UpdateLogCount();
        }

        /// <summary>
        /// 把所有 UI 操作切回 UI 线程。
        /// 下载/解包在后台线程跑，任何回调都不能直接碰控件。
        /// </summary>
        private void Ui(Action action)
        {
            if (action == null) return;
            var disp = Dispatcher;
            if (disp == null) return;
            try
            {
                if (disp.CheckAccess()) action();
                else disp.Invoke(action);
            }
            catch (Exception)
            {
                // 窗口已关闭 / 正在退出时忽略，避免后台任务把进程炸掉
            }
        }

        public void AddLog(string text)
        {
            Ui(() =>
            {
                if (LogList == null) return;
                LogList.Items.Add("[" + DateTime.Now.ToString("HH:mm:ss") + "] " + text);
                if (LogList.Items.Count > 0)
                    LogList.ScrollIntoView(LogList.Items[LogList.Items.Count - 1]);
                UpdateLogCount();
            });
        }

        private void UpdateLogCount()
        {
            LogCount.Text = "共 " + LogList.Items.Count + " 条";
        }

        // ================= 状态行 =================
        private void SetStatus(string text, string colorKey)
        {
            Ui(() =>
            {
                StatusText.Text = text;
                var brush = (SolidColorBrush)FindResource(colorKey);
                StatusText.Foreground = brush;
                StatusDot.Fill = brush;
            });
        }

        // ================= 目录选择 =================
        /// <summary>
        /// 弹出系统原生的「选择文件夹」对话框（自带新建文件夹、地址栏、搜索）。
        /// </summary>
        private string PickDirectory(string title = "选择绿色版 Chrome 所在目录", string initialPath = "")
        {
            string picked;
            if (!UI.FolderPicker.Show(this, title, initialPath, out picked))
                return "";
            return picked ?? "";
        }

        /// <summary>
        /// 选一个「绿色版 Chrome 已存在」的目录。
        /// 选错了可以接着重选，直到选对或主动取消。
        /// </summary>
        private string PickExistingGreenDir()
        {
            while (true)
            {
                var path = PickDirectory();
                if (string.IsNullOrEmpty(path))
                    return "";   // 用户取消

                if (Detector.IsGreenChromeDir(path))
                    return path;

                AddLog("目录 " + path + " 里没找到 App\\chrome.exe，不是绿色版 Chrome");
                var again = MessageDialog.Confirm(this, DialogKind.Warn, "不是绿色版 Chrome",
                    "目录：" + path + "\n\n里面没找到 App\\chrome.exe，\n这不是一套绿色版 Chrome。\n\n是否重新选择一个目录？",
                    "重新选择", "取消");
                if (!again)
                    return "";
            }
        }

        /// <summary>
        /// 选一个「可以放新绿色版」的目录（必须空或不存在）。
        /// 选错了可以接着重选，直到选对或主动取消。
        /// </summary>
        private string PickFreshTargetDir()
        {
            var hint = "";
            while (true)
            {
                var path = PickDirectory("选择新绿色版 Chrome 要放置的目录（可点「新建文件夹」创建一个空目录）", hint);
                if (string.IsNullOrEmpty(path))
                    return "";   // 用户取消

                var reason = ChromeUpdater.ValidateFreshTarget(path, _cfg.ChromeDir);
                if (reason == null)
                    return path;

                AddLog("不能在这个目录制作：" + path);
                var again = MessageDialog.Confirm(this, DialogKind.Warn, "这个目录不行",
                    reason + "\n\n选中的目录：\n" + path +
                    "\n\n是否重新选择一个目录？（在对话框里可以点「新建文件夹」建一个空目录）",
                    "重新选择", "取消");
                if (!again)
                    return "";
                hint = path;
            }
        }
    }
}
