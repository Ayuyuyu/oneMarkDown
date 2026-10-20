using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace OneMarkDown.Ui
{
    /// <summary>
    /// 对话框统一入口。所有弹窗都以 OneNote 主窗口为 owner。
    ///
    /// 为什么不能用自己的 MainWindowHandle：加载项跑在 dllhost.exe 代理进程里，
    /// 那个进程没有主窗口，MainWindowHandle 是 0 —— 于是 owner 变成 null，
    /// 对话框无主、被压到 OneNote 后面，而 OneNote 的 COM 回调是同步的，
    /// 它一直在等回调返回，用户看到的就是"OneNote 卡死"。
    /// </summary>
    public static class Dialogs
    {
        private const string Caption = "OneMarkDown";
        private const string HostProcessName = "ONENOTE";

        public static void Info(string message)
        {
            Show(message, MessageBoxIcon.Information);
        }

        public static void Warn(string message)
        {
            Show(message, MessageBoxIcon.Warning);
        }

        public static void Error(string message)
        {
            Show(message, MessageBoxIcon.Error);
        }

        public static IWin32Window GetOwnerWindow()
        {
            var handle = FindHostWindow();
            return handle == IntPtr.Zero ? null : new WindowHandle(handle);
        }

        /// <summary>
        /// 把 OneNote 主窗口拉回前台。从对话框返回、或注入按键之前调用。
        /// </summary>
        public static IntPtr BringHostToFront()
        {
            var handle = FindHostWindow();
            if (handle == IntPtr.Zero)
            {
                return IntPtr.Zero;
            }

            try
            {
                SetForegroundWindow(handle);
            }
            catch
            {
            }

            return handle;
        }

        private static IntPtr FindHostWindow()
        {
            try
            {
                // 1) 前台窗口就属于 OneNote —— 点完功能区按钮后的常态
                var foreground = GetForegroundWindow();
                if (foreground != IntPtr.Zero && IsHostProcess(foreground))
                {
                    return foreground;
                }

                // 2) 自己进程的主窗口（直接在 OneNote 进程内运行的情况）
                var own = Process.GetCurrentProcess().MainWindowHandle;
                if (own != IntPtr.Zero)
                {
                    return own;
                }

                // 3) 按进程名找 OneNote 主窗口
                foreach (var process in Process.GetProcessesByName(HostProcessName))
                {
                    if (process.MainWindowHandle != IntPtr.Zero)
                    {
                        return process.MainWindowHandle;
                    }
                }
            }
            catch
            {
            }

            return IntPtr.Zero;
        }

        private static bool IsHostProcess(IntPtr handle)
        {
            uint processId;
            GetWindowThreadProcessId(handle, out processId);
            if (processId == 0)
            {
                return false;
            }

            try
            {
                using (var process = Process.GetProcessById((int)processId))
                {
                    return string.Equals(process.ProcessName, HostProcessName, StringComparison.OrdinalIgnoreCase);
                }
            }
            catch
            {
                return false;
            }
        }

        private static void Show(string message, MessageBoxIcon icon)
        {
            try
            {
                MessageBox.Show(GetOwnerWindow(), message, Caption, MessageBoxButtons.OK, icon);
            }
            catch
            {
                // 弹窗失败不能影响宿主。
            }
        }

        private sealed class WindowHandle : IWin32Window
        {
            public WindowHandle(IntPtr handle)
            {
                Handle = handle;
            }

            public IntPtr Handle { get; private set; }
        }

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
    }
}
