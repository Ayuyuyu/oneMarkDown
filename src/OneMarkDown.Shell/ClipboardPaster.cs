using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using OneMarkDown.Diagnostics;

namespace OneMarkDown.Shell
{
    /// <summary>
    /// 往当前前台窗口发送 Ctrl+V。
    ///
    /// 关键点：热键触发的瞬间，用户手指还按着 Win / Alt，此时合成出来的按键在系统看来是
    /// Win+Alt+Ctrl+V，目标程序不会当成粘贴。所以必须先等物理修饰键松开再发。
    /// 另外这里不再限定目标必须是 OneNote，任何接受文本输入的窗口都能粘。
    /// </summary>
    internal static class ClipboardPaster
    {
        private const byte VkControl = 0x11;
        private const byte VkV = 0x56;
        private const uint KeyEventKeyUp = 0x0002;

        private const int VkShift = 0x10;
        private const int VkMenu = 0x12;
        private const int VkLWin = 0x5B;
        private const int VkRWin = 0x5C;

        private const int ModifierTimeoutMilliseconds = 1200;
        private const int KeyGapMilliseconds = 25;
        private const int FocusSettleMilliseconds = 160;

        private static readonly string[] ShellWindowClasses =
        {
            "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd"
        };

        internal static IntPtr CaptureForeground()
        {
            return GetForegroundWindow();
        }

        internal static bool TryPaste(IntPtr preferredTarget)
        {
            try
            {
                // 1. 等修饰键松开，否则发出去的是 Win+Alt+Ctrl+V。
                if (!WaitForModifiersReleased())
                {
                    // 用户一直按着（或者系统没上报松开），主动补 keyup 再发，
                    // 否则目标程序收到的是 Win+Alt+Ctrl+V，不会当成粘贴。
                    Log.Warn("修饰键在超时内没有释放，主动补发 keyup");
                    ForceReleaseModifiers();
                }

                // 2. 需要时把目标窗口切到前台（例如从托盘菜单触发）。
                if (preferredTarget != IntPtr.Zero && GetForegroundWindow() != preferredTarget)
                {
                    SetForegroundWindow(preferredTarget);
                    Thread.Sleep(FocusSettleMilliseconds);
                }

                // 3. 前台不能是我们自己的窗口（提示条 / 托盘菜单），也不能是桌面或任务栏。
                var foreground = GetForegroundWindow();
                if (foreground == IntPtr.Zero)
                {
                    Log.Warn("没有前台窗口，跳过自动粘贴");
                    return false;
                }

                if (IsOwnWindow(foreground))
                {
                    Log.Warn("前台是本程序窗口，跳过自动粘贴");
                    return false;
                }

                if (IsShellWindow(foreground))
                {
                    Log.Warn("前台是桌面或任务栏，跳过自动粘贴");
                    return false;
                }

                SendCtrlV();
                Log.Info("已发送 Ctrl+V 到 " + DescribeWindow(foreground));
                return true;
            }
            catch (Exception exception)
            {
                Log.Error("发送 Ctrl+V 失败", exception);
                return false;
            }
        }

        /// <summary>
        /// 轮询物理按键状态，直到 Win / Alt / Ctrl / Shift 全部松开。
        /// </summary>
        private static bool WaitForModifiersReleased()
        {
            var watch = Stopwatch.StartNew();

            while (watch.ElapsedMilliseconds < ModifierTimeoutMilliseconds)
            {
                if (!IsKeyDown(VkLWin) && !IsKeyDown(VkRWin) && !IsKeyDown(VkMenu) &&
                    !IsKeyDown(VkControl) && !IsKeyDown(VkShift))
                {
                    // 再留一点时间给"最后一个 keyup"被消息队列消化。
                    Thread.Sleep(40);
                    return true;
                }

                Thread.Sleep(15);
            }

            return false;
        }

        private static bool IsKeyDown(int virtualKey)
        {
            return (GetAsyncKeyState(virtualKey) & 0x8000) != 0;
        }

        /// <summary>
        /// 给仍然处于按下状态的修饰键补发 keyup，把按键状态清干净。
        /// 只对确实按下的键发，避免影响用户正常的输入。
        /// </summary>
        private static void ForceReleaseModifiers()
        {
            foreach (var key in new[] { VkLWin, VkRWin, VkMenu, VkControl, VkShift })
            {
                if (IsKeyDown(key))
                {
                    keybd_event((byte)key, 0, KeyEventKeyUp, UIntPtr.Zero);
                }
            }

            Thread.Sleep(80);
        }

        private static void SendCtrlV()
        {
            keybd_event(VkControl, 0, 0, UIntPtr.Zero);
            Thread.Sleep(KeyGapMilliseconds);

            keybd_event(VkV, 0, 0, UIntPtr.Zero);
            Thread.Sleep(KeyGapMilliseconds);

            keybd_event(VkV, 0, KeyEventKeyUp, UIntPtr.Zero);
            Thread.Sleep(KeyGapMilliseconds);

            keybd_event(VkControl, 0, KeyEventKeyUp, UIntPtr.Zero);
            Thread.Sleep(KeyGapMilliseconds);
        }

        private static bool IsOwnWindow(IntPtr window)
        {
            var processId = GetProcessId(window);
            return processId != 0 && processId == Process.GetCurrentProcess().Id;
        }

        private static bool IsShellWindow(IntPtr window)
        {
            try
            {
                var className = GetClassName(window);
                foreach (var shellClass in ShellWindowClasses)
                {
                    if (string.Equals(className, shellClass, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }
            catch
            {
            }

            return false;
        }

        private static uint GetProcessId(IntPtr window)
        {
            uint processId;
            GetWindowThreadProcessId(window, out processId);
            return processId;
        }

        private static string GetClassName(IntPtr window)
        {
            var buffer = new StringBuilder(256);
            GetClassNameW(window, buffer, buffer.Capacity);
            return buffer.ToString();
        }

        private static string DescribeWindow(IntPtr window)
        {
            try
            {
                var processId = GetProcessId(window);
                if (processId == 0)
                {
                    return "(未知进程)";
                }

                using (var process = Process.GetProcessById((int)processId))
                {
                    return process.ProcessName + " / " + GetClassName(window);
                }
            }
            catch
            {
                return "(未知进程)";
            }
        }

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassNameW(IntPtr hWnd, StringBuilder className, int maxCount);

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int virtualKey);

        [DllImport("user32.dll")]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);
    }
}
