using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;

namespace GreenChrome.UI
{
    /// <summary>目录列表中的一项。</summary>
    public class DirEntry
    {
        public string FullPath { get; set; }
        public string Display { get; set; }
    }

    /// <summary>
    /// 自绘目录选择器：盘符 → 子目录逐层进入，选中的目录作为结果返回。
    /// 不用系统 FolderBrowserDialog（样式陈旧）。
    /// </summary>
    public partial class DirectoryPicker : Window
    {
        /// <summary>用户选中的目录；取消时为空字符串。</summary>
        public string SelectedPath { get; private set; }

        /// <summary>初始目录；为空时从盘符列表开始。</summary>
        public string InitialPath { get; set; }

        private string _currentDir = "";

        public DirectoryPicker()
        {
            InitializeComponent();
            SelectedPath = "";
            Loaded += (s, e) =>
            {
                // 遮罩覆盖父窗口
                if (Owner != null)
                {
                    Left = Owner.Left;
                    Top = Owner.Top;
                    Width = Owner.ActualWidth;
                    Height = Owner.ActualHeight;
                }
                _currentDir = InitialPath ?? "";
                Refresh();
            };
        }

        // ---------- 目录枚举 ----------
        private void Refresh()
        {
            PathText.Text = string.IsNullOrEmpty(_currentDir) ? "此电脑" : _currentDir;
            BtnUp.IsEnabled = !string.IsNullOrEmpty(_currentDir);

            var items = new List<DirEntry>();

            if (string.IsNullOrEmpty(_currentDir))
            {
                // 盘符列表
                foreach (var d in Drives())
                {
                    items.Add(new DirEntry { FullPath = d, Display = d });
                }
            }
            else
            {
                foreach (var sub in Subdirs(_currentDir))
                {
                    items.Add(new DirEntry
                    {
                        FullPath = sub,
                        Display = Path.GetFileName(sub.TrimEnd('\\', '/'))
                    });
                }
            }

            EntryList.ItemsSource = items;
        }

        private static IEnumerable<string> Drives()
        {
            var list = new List<string>();
            foreach (var d in new[] { "C", "D", "E", "F", "G", "H", "A", "B" })
            {
                var root = d + @":\";
                try
                {
                    if (Directory.Exists(root))
                        list.Add(root);
                }
                catch { }
            }
            return list;
        }

        private static IEnumerable<string> Subdirs(string dir)
        {
            var list = new List<string>();
            try
            {
                foreach (var sub in Directory.GetDirectories(dir))
                    list.Add(sub);
            }
            catch { }
            list.Sort(StringComparer.OrdinalIgnoreCase);
            return list;
        }

        private static string ParentOf(string dir)
        {
            if (string.IsNullOrEmpty(dir)) return "";
            try
            {
                var p = Path.GetDirectoryName(dir.TrimEnd('\\', '/'));
                return p ?? "";
            }
            catch
            {
                return "";
            }
        }

        // ---------- 交互 ----------
        /// <summary>
        /// 单击即进入该目录（像资源管理器那样逐层点进去）。
        /// Refresh() 会重置 ItemsSource，选中项随之清空，不会重复触发。
        /// </summary>
        private void EntryList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            var entry = EntryList.SelectedItem as DirEntry;
            if (entry == null) return;
            Enter(entry);
        }

        private void Enter(DirEntry entry)
        {
            if (entry == null) return;
            _currentDir = entry.FullPath;
            Refresh();
        }

        private void BtnUp_Click(object sender, RoutedEventArgs e)
        {
            _currentDir = ParentOf(_currentDir);
            Refresh();
        }

        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_currentDir))
            {
                MessageDialog.Show(this, DialogKind.Warn, "请选择目录",
                    "请先进入一个目录，再点「选择此目录」。");
                return;
            }
            SelectedPath = _currentDir;
            DialogResult = true;
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            SelectedPath = "";
            DialogResult = false;
            Close();
        }
    }
}
