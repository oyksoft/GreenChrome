using System.Windows;
using System.Windows.Media;

namespace GreenChrome.UI
{
    public enum DialogKind { Info, Warn, Error }

    /// <summary>
    /// 自绘模态消息弹窗（与 Go 版样式一致：半透明遮罩 + 圆角卡片 + 彩色圆点）。
    /// 单按钮（确定）或双按钮（取消 + 危险操作）。
    /// </summary>
    public partial class MessageDialog : Window
    {
        public bool Confirmed { get; private set; }

        private MessageDialog()
        {
            InitializeComponent();
        }

        /// <summary>弹出消息框（单按钮「确定」）。</summary>
        public static void Show(Window owner, DialogKind kind, string title, string message)
        {
            var dlg = Create(owner, kind, title, message, false, "确定", null);
            dlg.ShowDialog();
        }

        /// <summary>弹出确认框（双按钮）。返回 true 表示用户点了确认。</summary>
        public static bool Confirm(Window owner, DialogKind kind, string title, string message,
                                   string okText, string cancelText)
        {
            var dlg = Create(owner, kind, title, message, true, okText ?? "确定", cancelText ?? "取消");
            dlg.ShowDialog();
            return dlg.Confirmed;
        }

        private static MessageDialog Create(Window owner, DialogKind kind, string title, string message,
                                            bool twoButtons, string okText, string cancelText)
        {
            var dlg = new MessageDialog
            {
                Owner = owner
            };

            // 让遮罩覆盖整个父窗口
            if (owner != null)
            {
                dlg.Left = owner.Left;
                dlg.Top = owner.Top;
                dlg.Width = owner.ActualWidth;
                dlg.Height = owner.ActualHeight;
            }

            dlg.TitleText.Text = title ?? "";
            dlg.MessageText.Text = message ?? "";

            // 圆点颜色按类型
            Color accent;
            switch (kind)
            {
                case DialogKind.Warn: accent = Color.FromRgb(0xD9, 0x77, 0x06); break;
                case DialogKind.Error: accent = Color.FromRgb(0xC0, 0x39, 0x2B); break;
                default: accent = Color.FromRgb(0x16, 0xA3, 0x4A); break;
            }
            dlg.TitleDot.Fill = new SolidColorBrush(accent);

            dlg.BtnOk.Content = okText;
            if (twoButtons)
            {
                dlg.BtnCancel.Content = cancelText;
                dlg.BtnCancel.Visibility = Visibility.Visible;
                // 危险操作用红色按钮
                if (kind == DialogKind.Warn || kind == DialogKind.Error)
                    dlg.BtnOk.Style = (Style)dlg.FindResource("DangerButton");
            }
            else
            {
                dlg.BtnCancel.Visibility = Visibility.Collapsed;
            }

            dlg.SizeToContent = SizeToContent.Manual;
            dlg.Width = owner != null ? owner.ActualWidth : 440;
            dlg.Height = owner != null ? owner.ActualHeight : 300;

            return dlg;
        }

        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            Confirmed = true;
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            Confirmed = false;
            Close();
        }
    }
}
