using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Forms;

namespace GreenChrome.UI
{
    /// <summary>
    /// 系统原生的「选择文件夹」对话框。
    ///
    /// .NET Framework 4.6 之后的 FolderBrowserDialog 底层已改用 Vista 的 IFileDialog，
    /// 所以外观是现代的 Windows 文件夹选择器，并且自带「新建文件夹」按钮。
    /// 相比自己画的列表，好处是：
    ///   · 有「新建文件夹」功能
    ///   · 支持地址栏、搜索、桌面/此电脑/网络位置、前进后退
    ///   · 右键菜单、缩略图、导航历史等都是系统原生的
    /// </summary>
    public static class FolderPicker
    {
        /// <summary>
        /// 让 WinForms 对话框以 WPF 窗口为 owner（这样它会跟着主窗口居中、模态）。
        /// </summary>
        private sealed class WpfWindowOwner : IWin32Window
        {
            private readonly IntPtr _handle;
            public WpfWindowOwner(IntPtr handle) { _handle = handle; }
            public IntPtr Handle { get { return _handle; } }
        }

        /// <summary>
        /// 弹出原生文件夹选择框。
        /// </summary>
        /// <param name="owner">父窗口（用来居中并保持模态）。</param>
        /// <param name="title">对话框标题。</param>
        /// <param name="initialPath">初始定位的目录，可为空。</param>
        /// <param name="selectedPath">用户最终选中的目录；取消时返回空字符串。</param>
        /// <returns>用户点了确定返回 true。</returns>
        public static bool Show(Window owner, string title, string initialPath, out string selectedPath)
        {
            selectedPath = "";

            using (var dlg = new FolderBrowserDialog())
            {
                dlg.Description = title ?? "选择文件夹";
                dlg.ShowNewFolderButton = true;      // 「新建文件夹」按钮
                dlg.SelectedPath = SafeExisting(initialPath);

                var native = new WpfWindowOwner(
                    owner == null ? new IntPtr(0) : new System.Windows.Interop.WindowInteropHelper(owner).Handle);

                var result = owner == null
                    ? dlg.ShowDialog()
                    : dlg.ShowDialog(native);

                if (result == DialogResult.OK)
                {
                    // 原生对话框返回的路径常带末尾反斜杠（如 D:\新建文件夹\），去掉更干净
                    selectedPath = Normalize(dlg.SelectedPath);
                    return true;
                }
                return false;
            }
        }

        /// <summary>去掉路径末尾的分隔符；空路径返回空串。</summary>
        private static string Normalize(string path)
        {
            if (string.IsNullOrEmpty(path)) return "";
            path = path.Trim();
            if (path.Length > 3)   // 保留 "D:\" 这种根路径
                path = path.TrimEnd('\\', '/');
            return path;
        }

        /// <summary>
        /// 只做「导航定位」，不改变用户选择：
        /// 用于把初始目录指向一个存在的位置。
        /// </summary>
        private static string SafeExisting(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return "";
            try
            {
                if (System.IO.Directory.Exists(path)) return path;
                var parent = System.IO.Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(parent) && System.IO.Directory.Exists(parent))
                    return parent;
            }
            catch { }
            return "";
        }
    }
}
