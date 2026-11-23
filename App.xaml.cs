using System;
using System.Threading;
using System.Windows;

namespace GreenChrome
{
    public partial class App : Application
    {
        private Mutex _singleInstanceMutex;

        protected override void OnStartup(StartupEventArgs e)
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
