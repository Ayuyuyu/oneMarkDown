using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace OneMarkDown.Input
{
    /// <summary>
    /// 用 keybd_event 直接合成 Ctrl+V。
    ///
    /// 不用 SendKeys：它依赖调用线程有消息循环，而我们在 dllhost 代理进程里、
    /// 正处在宿主同步 COM 回调的调用栈上，SendKeys.SendWait 容易挂住或打空。
    /// keybd_event 只是往输入流里塞按键，目标永远是新前台窗口，更可控。
    /// </summary>
    internal static class KeyInjector
    {
        private const byte VkControl = 0x11;
        private const byte VkV = 0x56;
        private const uint KeyEventKeyUp = 0x0002;

        /// <summary>按下与抬起之间留一点间隔，太快的话部分程序收不到组合键。</summary>
        private const int GapMilliseconds = 25;

        internal static void SendCtrlV()
        {
            keybd_event(VkControl, 0, 0, UIntPtr.Zero);
            Thread.Sleep(GapMilliseconds);

            keybd_event(VkV, 0, 0, UIntPtr.Zero);
            Thread.Sleep(GapMilliseconds);

            keybd_event(VkV, 0, KeyEventKeyUp, UIntPtr.Zero);
            Thread.Sleep(GapMilliseconds);

            keybd_event(VkControl, 0, KeyEventKeyUp, UIntPtr.Zero);
            Thread.Sleep(GapMilliseconds);
        }

        [DllImport("user32.dll")]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);
    }
}
