using System;
using System.Diagnostics;
using System.Threading;
using OneMarkDown.Diagnostics;
using OneMarkDown.Ui;

namespace OneMarkDown.Input
{
    public enum PasteOutcome
    {
        /// <summary>已成功注入 Ctrl+V。</summary>
        Pasted,

        /// <summary>无法确认焦点，或注入失败；剪贴板内容仍然可用，提示用户手动粘贴。</summary>
        ManualRequired
    }

    /// <summary>
    /// 加载项跑在 dllhost.exe 代理进程里（OneNote 这个版本不支持进程内托管加载项），
    /// 但按键是往系统输入流里塞的，仍然会落到 OneNote 的前台窗口上，
    /// 所以"写完剪贴板再合成 Ctrl+V"这条路跨进程照样成立。
    /// </summary>
    public static class PasteInjector
    {
        private const string HostProcessName = "ONENOTE";

        /// <summary>点完按钮后焦点从 Ribbon 回到编辑区需要一点时间。</summary>
        private const int FocusSettleMilliseconds = 240;

        private const int ForegroundRetryMilliseconds = 180;

        public static PasteOutcome TryPaste()
        {
            try
            {
                Thread.Sleep(FocusSettleMilliseconds);

                if (!IsHostForeground())
                {
                    // 从文件对话框返回时焦点可能还在我们身上，主动把 OneNote 拉回前台。
                    Log.Info("前台不是 OneNote，尝试把它切回前台");
                    Dialogs.BringHostToFront();
                    Thread.Sleep(ForegroundRetryMilliseconds);
                }

                if (!IsHostForeground())
                {
                    Log.Warn("前台窗口不是 OneNote，回退为手动粘贴");
                    return PasteOutcome.ManualRequired;
                }

                KeyInjector.SendCtrlV();
                Log.Info("已注入 Ctrl+V");
                return PasteOutcome.Pasted;
            }
            catch (Exception exception)
            {
                Log.Error("注入 Ctrl+V 失败", exception);
                return PasteOutcome.ManualRequired;
            }
        }

        private static bool IsHostForeground()
        {
            var handle = NativeMethods.GetForegroundWindow();
            if (handle == IntPtr.Zero)
            {
                return false;
            }

            uint processId;
            NativeMethods.GetWindowThreadProcessId(handle, out processId);
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
    }
}
