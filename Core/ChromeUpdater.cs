using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace GreenChrome.Core
{
    /// <summary>
    /// Chrome 版本应用流程：下载 → 校验 → 提取 CHROME.7Z → 解包 → 写入 App/&lt;版本&gt;/。
    ///
    /// 目录布局（与用户现有绿色版一致）：
    ///   &lt;green&gt;/App/&lt;版本&gt;/   ← 解包出的 Chrome 文件
    ///   &lt;green&gt;/App/version.dll   ← Chrome++ 代理（不在此流程内改动）
    ///   &lt;green&gt;/Data, Cache, Ext  ← 用户数据（不改动）
    /// </summary>
    public static class ChromeUpdater
    {
        /// <summary>各阶段回调，用于更新界面与日志。</summary>
        public interface IProgress
        {
            void Log(string message);
            void Status(string message);
            void DownloadProgress(long received, long total);
            void Stage(string stage);
        }

        /// <summary>
        /// 应用指定版本。返回解包出来的版本目录名。
        /// </summary>
        public static string Apply(string greenChromeDir, ReleaseInfo rel, IProgress p, CancellationToken cancel)
        {
            if (string.IsNullOrEmpty(greenChromeDir))
                throw new InvalidOperationException("尚未绑定绿色版目录");
            if (rel == null || !rel.IsUsable)
                throw new InvalidOperationException("没有可用的版本信息");
            if (rel.urls == null || rel.urls.Count == 0)
                throw new InvalidOperationException("版本信息里没有下载地址");

            // 只用 dl.google.com 的地址（未被墙且速度快）；没有则退回全部
            var urls = rel.urls.Where(u => u != null && u.IndexOf("dl.google.com", StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            if (urls.Count == 0)
            {
                p.Log("未找到 dl.google.com 地址，改用全部下载地址");
                urls = rel.urls.Where(u => !string.IsNullOrEmpty(u)).ToList();
            }
            p.Log("下载地址 " + urls.Count + " 个（dl.google.com）");

            var tempDir = Path.Combine(Path.GetTempPath(), "gc_up_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(tempDir);
            try
            {
                // ---------- 1. 下载 ----------
                p.Stage("下载");
                p.Status("正在下载 " + rel.version);
                var exePath = Path.Combine(tempDir, "chrome_installer.exe");
                var usedUrl = Downloader.Download(urls, exePath, rel.size, rel.sha256,
                    prog => p.DownloadProgress(prog.Received, prog.Total), cancel);
                p.Log("下载完成（" + usedUrl + "）");

                // ---------- 2. 校验 ----------
                p.Stage("校验");
                p.Status("正在校验 SHA256");
                if (!string.IsNullOrEmpty(rel.sha256))
                {
                    var actual = Downloader.Sha256Of(exePath);
                    if (!string.Equals(actual, rel.sha256, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("SHA256 校验失败，文件可能已损坏");
                    p.Log("SHA256 校验通过");
                }
                if (rel.size > 0 && new FileInfo(exePath).Length != rel.size)
                    throw new InvalidDataException("文件大小与预期不符");

                // ---------- 3. 提取内嵌 CHROME.7Z ----------
                p.Stage("解包");
                p.Status("正在提取内嵌资源");
                var res = PeResource.Find(exePath, "BN", "CHROME.7Z");
                if (res == null)
                    throw new InvalidDataException("离线包内没有找到 BN\\CHROME.7Z 资源");

                p.Log(string.Format("找到 CHROME.7Z：offset={0} size={1}", res.Offset, res.Size));

                // ---------- 4. 解压 ----------
                p.Status("正在解压 Chrome 文件");
                var extractDir = Path.Combine(tempDir, "chrome");
                Directory.CreateDirectory(extractDir);
                int files;
                using (var fs = new FileStream(exePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    files = Unpacker.ExtractFromStream(fs, res.Offset, res.Size, extractDir, null, null);
                }
                p.Log("解压完成，共 " + files + " 个文件");

                // ---------- 5. 校验解出的版本 ----------
                var layout = FindLayout(extractDir);
                if (layout == null)
                    throw new InvalidDataException("解压结果里找不到版本目录（含 chrome.dll）");

                var actualVersion = Path.GetFileName(layout.VersionDir);
                if (!string.IsNullOrEmpty(rel.version) &&
                    !string.Equals(actualVersion, rel.version, StringComparison.OrdinalIgnoreCase))
                {
                    p.Log("注意：解包版本 " + actualVersion + " 与 API 版本 " + rel.version + " 不一致");
                }

                // ---------- 6. 写入 App/<版本>/ 与 App/ 外壳 ----------
                p.Stage("应用");
                p.Status("正在写入 " + actualVersion);
                var appDir = Path.Combine(greenChromeDir, "App");
                var targetDir = Path.Combine(appDir, actualVersion);
                if (Directory.Exists(targetDir))
                {
                    var old = targetDir + ".old_" + DateTime.Now.ToString("HHmmss");
                    p.Log("目标版本目录已存在，先重命名备份：" + Path.GetFileName(old));
                    Directory.Move(targetDir, old);
                }
                Directory.CreateDirectory(appDir);
                CopyDirectory(layout.VersionDir, targetDir);
                p.Log("已写入 App\\" + actualVersion);

                // 外壳文件（chrome.exe / chrome_proxy.exe 等）放到 App/ 根
                foreach (var file in Directory.GetFiles(layout.ShellDir))
                {
                    var name = Path.GetFileName(file);
                    // version.dll / chrome++.ini 由用户自己的绿色版管理，不覆盖
                    if (string.Equals(name, "version.dll", StringComparison.OrdinalIgnoreCase)) continue;
                    if (string.Equals(name, "chrome++.ini", StringComparison.OrdinalIgnoreCase)) continue;
                    var dst = Path.Combine(appDir, name);
                    if (File.Exists(dst))
                    {
                        try { File.Delete(dst); } catch { }
                    }
                    File.Copy(file, dst, true);
                    p.Log("已写入 App\\" + name);
                }

                // ---------- 7. 清理其他版本目录 ----------
                foreach (var dir in Directory.GetDirectories(appDir))
                {
                    var name = Path.GetFileName(dir);
                    if (name == actualVersion) continue;
                    // 只清理形如 160.0.8171.0 的版本目录（保留 .old_ 备份不动）
                    if (name.Count(c => c == '.') < 2) continue;
                    try
                    {
                        p.Log("删除旧版本目录 " + name);
                        Directory.Delete(dir, true);
                    }
                    catch (Exception ex)
                    {
                        p.Log("删除旧版本失败：" + ex.Message);
                    }
                }

                p.Status("已完成 " + actualVersion);
                return actualVersion;
            }
            finally
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }

        /// <summary>
        /// 校验目录是否适合「从零制作一套」。
        /// 返回 null 表示可以；返回字符串表示不可以的原因。
        /// 宁可拒绝，也不要在用户已有的目录里动手。
        /// </summary>
        public static string ValidateFreshTarget(string dir, string currentBoundDir)
        {
            if (string.IsNullOrWhiteSpace(dir))
                return "请选择一个目录";

            var full = Path.GetFullPath(dir).TrimEnd('\\', '/');

            // 已有绿色版就直接拒绝：那是「应用到」的活，不该重建
            if (Detector.IsGreenChromeDir(full))
                return "这个目录已经是一套绿色版 Chrome 了。\n请改用「选择已有目录」绑定它，或换一个空目录。";

            // 和当前已绑定的目录重合（本身 / 父目录 / 子目录）都要拒绝
            if (!string.IsNullOrEmpty(currentBoundDir))
            {
                var bound = Path.GetFullPath(currentBoundDir).TrimEnd('\\', '/');
                if (string.Equals(full, bound, StringComparison.OrdinalIgnoreCase))
                    return "这就是当前已绑定的绿色版目录，不能在这里重建。";
                if (bound.StartsWith(full + "\\", StringComparison.OrdinalIgnoreCase))
                    return "这个目录包含了当前已绑定的绿色版（" + bound + "），\n在这里制作会动到现有文件，请换一个空目录。";
                if (full.StartsWith(bound + "\\", StringComparison.OrdinalIgnoreCase))
                    return "这个目录在当前绿色版目录里面，请换一个独立的空目录。";
            }

            if (!Directory.Exists(full))
                return null;   // 不存在：创建全新的，安全

            // 必须为空目录
            string[] entries;
            try
            {
                entries = Directory.GetFileSystemEntries(full);
            }
            catch (Exception ex)
            {
                return "无法读取这个目录：" + ex.Message;
            }
            if (entries.Length > 0)
                return "这个目录不是空的（有 " + entries.Length + " 项）。\n为了不破坏已有文件，请选择一个空目录或新建目录。";

            return null;
        }

        /// <summary>
        /// 从零制作一套绿色版：下载解包写入 App/&lt;版本&gt;、装上 Chrome++、建好 Data。
        /// 返回实际版本号。调用前应先过 ValidateFreshTarget。
        /// </summary>
        public static string CreateFresh(string targetDir, ReleaseInfo rel, string arch,
            IProgress p, CancellationToken cancel)
        {
            var version = Apply(targetDir, rel, p, cancel);

            // 补标准目录，让 chrome.exe 一启动就有地方放数据
            foreach (var sub in new[] { "Data", "Cache" })
            {
                var d = Path.Combine(targetDir, sub);
                if (!Directory.Exists(d)) Directory.CreateDirectory(d);
            }

            // 装 Chrome++（绿色版要它做便携化）；失败只记日志，不推翻已完成的 Chrome
            p.Stage("Chrome++");
            p.Status("正在安装 Chrome++");
            try
            {
                var cpp = ChromePlusClient.FetchLatest();
                if (cpp == null)
                {
                    p.Log("Chrome++ 获取失败，先跳过（Chrome 已可用，稍后可单独更新）");
                }
                else
                {
                    ChromePlusClient.Update(targetDir, arch ?? "x64", cpp, p.Log);
                    p.Log("已安装 Chrome++ " + cpp.Version);
                }
            }
            catch (Exception ex)
            {
                p.Log("Chrome++ 安装失败：" + ex.Message + "（不影响 Chrome 使用）");
            }

            p.Status("制作完成 " + version);
            return version;
        }

        /// <summary>
        /// 在解压结果里定位版本目录与外壳文件。
        /// 离线包解压结构：Chrome-bin/&lt;版本&gt;/... 加 Chrome-bin/chrome.exe 等外壳文件。
        /// </summary>
        private class Layout
        {
            public string VersionDir;      // 含 chrome.dll 的版本目录
            public string ShellDir;        // 外壳文件所在目录（chrome.exe / chrome_proxy.exe）
        }

        private static Layout FindLayout(string root)
        {
            // 先找一层（Chrome-bin/&lt;版本&gt;）
            foreach (var dir in Directory.GetDirectories(root))
            {
                if (File.Exists(Path.Combine(dir, "chrome.dll")))
                    return new Layout { VersionDir = dir, ShellDir = dir };
            }
            // 再找两层
            foreach (var dir in Directory.GetDirectories(root))
            {
                foreach (var sub in Directory.GetDirectories(dir))
                {
                    if (File.Exists(Path.Combine(sub, "chrome.dll")))
                        return new Layout { VersionDir = sub, ShellDir = dir };
                }
            }
            return null;
        }

        private static void CopyDirectory(string src, string dst)
        {
            Directory.CreateDirectory(dst);
            foreach (var file in Directory.GetFiles(src))
                File.Copy(file, Path.Combine(dst, Path.GetFileName(file)), true);
            foreach (var dir in Directory.GetDirectories(src))
                CopyDirectory(dir, Path.Combine(dst, Path.GetFileName(dir)));
        }
    }
}
