using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using GreenChrome.Core;

namespace GreenChrome.UI
{
    /// <summary>
    /// 通道选择对话框：列出 STABLE / BETA / DEV / CANARY 四个通道及其线上版本，
    /// 供「从零制作一套」时挑选要制作的通道。
    /// </summary>
    public partial class ChannelPicker : Window
    {
        /// <summary>用户选中的通道（小写：stable/beta/dev/canary）；取消时为 null。</summary>
        public string SelectedChannel { get; private set; }

        private class Row
        {
            public string Key;
            public Border Border;
        }

        private readonly List<Row> _rows = new List<Row>();

        private ChannelPicker()
        {
            InitializeComponent();
        }

        /// <summary>
        /// 弹出通道选择框。online 为各通道的线上版本（可为空），current 为当前配置里的通道。
        /// 返回选中的通道（小写），取消返回 null。
        /// </summary>
        public static string Pick(Window owner, Dictionary<string, ReleaseInfo> online, string current)
        {
            var dlg = new ChannelPicker { Owner = owner };

            if (owner != null)
            {
                dlg.Left = owner.Left;
                dlg.Top = owner.Top;
                dlg.Width = owner.ActualWidth;
                dlg.Height = owner.ActualHeight;
            }
            else
            {
                // 无父窗口时按内容自适应高度
                dlg.Width = 440;
                dlg.SizeToContent = SizeToContent.Height;
            }

            var names = new[]
            {
                new[] { "stable", "STABLE", "稳定版" },
                new[] { "beta",    "BETA",    "测试版" },
                new[] { "dev",     "DEV",     "开发版" },
                new[] { "canary",  "CANARY",  "每夜构建" }
            };

            foreach (var n in names)
            {
                ReleaseInfo rel = null;
                if (online != null) rel = CoiClient.Find(online, n[0], "x64");
                var usable = rel != null && rel.IsUsable;

                dlg.RowHost.Children.Add(dlg.BuildRow(
                    n[0], n[1],
                    usable ? rel.version : "—",
                    usable
                        ? n[2] + " · 下载约 " + (rel.size / 1048576.0).ToString("F0") + " MB"
                        : n[2] + " · 暂无可用版本"));
            }

            dlg.Select((current ?? "stable").ToLowerInvariant());
            dlg.ShowDialog();
            return dlg.SelectedChannel;
        }

        /// <summary>一行通道：左边名称+说明，右边版本号。整行可点。</summary>
        private Border BuildRow(string key, string name, string version, string note)
        {
            var row = new Row { Key = key };

            var border = new Border
            {
                Background = (Brush)FindResource("ColAccentBg"),
                BorderBrush = (Brush)FindResource("ColBorder"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(9),
                Padding = new Thickness(14, 11, 14, 11),
                Margin = new Thickness(0, 0, 0, 8),
                Cursor = Cursors.Hand
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var nameText = new TextBlock
            {
                Text = name,
                FontSize = 13.5,
                FontWeight = FontWeights.Medium,
                Foreground = (Brush)FindResource("ColText")
            };
            Grid.SetRow(nameText, 0);
            Grid.SetColumn(nameText, 0);
            grid.Children.Add(nameText);

            var verText = new TextBlock
            {
                Text = version,
                FontSize = 13,
                FontFamily = new FontFamily("Consolas"),
                Foreground = (Brush)FindResource("ColAccent"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(12, 0, 0, 0)
            };
            Grid.SetRow(verText, 0);
            Grid.SetColumn(verText, 1);
            grid.Children.Add(verText);

            var noteText = new TextBlock
            {
                Text = note,
                FontSize = 11.5,
                Foreground = (Brush)FindResource("ColTextMuted"),
                Margin = new Thickness(0, 4, 0, 0)
            };
            Grid.SetRow(noteText, 1);
            Grid.SetColumn(noteText, 0);
            Grid.SetColumnSpan(noteText, 2);
            grid.Children.Add(noteText);

            border.Child = grid;

            border.MouseLeftButtonUp += (s, e) => Select(key);
            border.MouseEnter += (s, e) =>
            {
                if (key != SelectedChannel)
                    border.Background = (Brush)FindResource("ColBorder");
            };
            border.MouseLeave += (s, e) =>
            {
                if (key != SelectedChannel)
                    border.Background = (Brush)FindResource("ColAccentBg");
            };

            row.Border = border;
            _rows.Add(row);
            return border;
        }

        private void Select(string key)
        {
            SelectedChannel = key;
            foreach (var r in _rows)
            {
                var sel = r.Key == key;
                r.Border.BorderBrush = sel
                    ? (Brush)FindResource("ColAccent")
                    : (Brush)FindResource("ColBorder");
                r.Border.BorderThickness = new Thickness(sel ? 1.5 : 1);
            }
        }

        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(SelectedChannel))
            {
                MessageDialog.Show(this, DialogKind.Warn, "请选择通道", "请先选择要制作的通道。");
                return;
            }
            DialogResult = true;
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            SelectedChannel = null;
            DialogResult = false;
            Close();
        }
    }
}
