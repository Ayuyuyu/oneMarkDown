using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace OneMarkDown.Shell
{
    /// <summary>
    /// 只收 WM_HOTKEY 的消息窗口，不显示任何界面。
    /// </summary>
    internal sealed class HotkeyWindow : NativeWindow, IDisposable
    {
        private const int WmHotkey = 0x0312;
        private const int HwndMessage = -3;
        private const uint ModNoRepeat = 0x4000;

        internal const uint ModAlt = 0x0001;
        internal const uint ModControl = 0x0002;
        internal const uint ModShift = 0x0004;
        internal const uint ModWin = 0x0008;

        internal const int IdPaste = 1;
        internal const int IdFile = 2;
        internal const int IdQuit = 3;

        public event Action<int> HotkeyPressed;

        public HotkeyWindow()
        {
            CreateHandle(new CreateParams
            {
                Caption = "OneMarkDown.Hotkeys",
                Parent = new IntPtr(HwndMessage)
            });
        }

        public bool TryRegister(int id, uint modifiers, Keys key)
        {
            return RegisterHotKey(Handle, id, modifiers | ModNoRepeat, (uint)key);
        }

        public void Unregister(int id)
        {
            try
            {
                UnregisterHotKey(Handle, id);
            }
            catch
            {
            }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WmHotkey)
            {
                var handler = HotkeyPressed;
                if (handler != null)
                {
                    handler(m.WParam.ToInt32());
                }
            }

            base.WndProc(ref m);
        }

        public void Dispose()
        {
            try
            {
                DestroyHandle();
            }
            catch
            {
            }
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    }
}
