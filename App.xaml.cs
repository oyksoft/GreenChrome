using System;
using System.Linq;
using System.Threading;
using System.Windows;

namespace GreenChrome
{
    public partial class App : Application
    {
        private Mutex _singleInstanceMutex;

        protected override void OnStartup(StartupEventArgs e)
        {
            // 单文件打包（Costura）：依赖 dll 嵌在 exe 里，
            // 这里先初始化加载器，保证后续用到依赖时能加载成功
            CosturaUtility.Initialize();

            // 自检模式：验证嵌入的依赖可用，不启动界面
            if (e.Args != null && e.Args.Length > 0 &&
                e.Args.Any(a => string.Equals(a, "--selftest", StringComparison.OrdinalIgnoreCase)))
            {
                var writer = new System.IO.StringWriter();
                int code = GreenChrome.Core.SelfTest.Run(writer);
                var text = writer.ToString();
                Console.Write(text);
                // WinExe 没有控制台，结果同时写到同目录的 selftest.log
                try
                {
                    System.IO.File.WriteAllText(
                        System.IO.Path.Combine(AppContext.BaseDirectory, "selftest.log"),
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "\r\n" + text);
                }
                catch { }
                Shutdown(code);
                return;
            }

            StartCore(e);
        }

        private void StartCore(StartupEventArgs e)
        {
            // 单实例：与 Go 版保持同样行为
            _singleInstanceMutex = new Mutex(true, @"Global\GreenChrome_SingleInstance", out bool createdNew);
            if (!createdNew)
            {
                Shutdown();
                return;
            }

            base.OnStartup(e);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _singleInstanceMutex?.Dispose();
            base.OnExit(e);
        }
    }
}
