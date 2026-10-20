using System;
using System.Threading;
using System.Windows.Forms;
using OneMarkDown.Diagnostics;

namespace OneMarkDown.Shell
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            bool isFirstInstance;
            using (var mutex = new Mutex(true, @"Local\OneMarkDown.Shell", out isFirstInstance))
            {
                if (!isFirstInstance)
                {
                    // 已经在跑了，静默退出。
                    return;
                }

                Log.EnsureInitialized();
                Log.Info("Shell 启动");

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                Application.ThreadException += (sender, args) =>
                    Log.Error("UI 线程未捕获异常", args.Exception);
                AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
                    Log.Error("后台线程未捕获异常", args.ExceptionObject as Exception);

                using (var context = new ShellApplicationContext())
                {
                    Application.Run(context);
                }

                GC.KeepAlive(mutex);
            }
        }
    }
}
