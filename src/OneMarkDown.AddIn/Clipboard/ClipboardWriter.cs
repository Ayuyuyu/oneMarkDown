using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using OneMarkDown.Diagnostics;

namespace OneMarkDown.Clipboard
{
    /// <summary>
    /// 直接通过 Win32 写剪贴板，不用 System.Windows.Forms.DataObject。
    ///
    /// 为什么必须这样：
    /// 1) DataObject.SetData(DataFormats.Html, byte[]) 会把字节数组 ToString()，
    ///    剪贴板里最终是字面量 "System.Byte[]"，宿主只能回退纯文本。
    /// 2) 传 string 时 .NET 会按系统 ANSI 代码页（中文机器是 GBK）编码写入，
    ///    而 CF_HTML 的偏移量是按 UTF-8 字节算的，两者一错位，宿主直接判定载荷非法。
    /// 所以这里自己分配 HGLOBAL、自己写 UTF-8 字节，保证偏移量和实际字节严格一致。
    /// </summary>
    public static class ClipboardWriter
    {
        private const uint CfText = 1;
        private const uint CfUnicodeText = 13;
        private const uint GmemMoveable = 0x0042;

        private const int RetryTimes = 5;
        private const int RetryDelayMilliseconds = 50;

        private static readonly uint CfHtml = RegisterClipboardFormat("HTML Format");

        public static bool TrySetHtml(string cfHtml, string plainText)
        {
            var htmlBytes = Encoding.UTF8.GetBytes(cfHtml ?? string.Empty);
            var textBytes = Encoding.Unicode.GetBytes((plainText ?? string.Empty) + "\0");

            for (var attempt = 1; attempt <= RetryTimes; attempt++)
            {
                if (!OpenClipboard(IntPtr.Zero))
                {
                    Log.Warn("打开剪贴板失败（第 " + attempt + " 次），错误码 " + Marshal.GetLastWin32Error());
                    Thread.Sleep(RetryDelayMilliseconds);
                    continue;
                }

                try
                {
                    if (!EmptyClipboard())
                    {
                        Log.Warn("清空剪贴板失败，错误码 " + Marshal.GetLastWin32Error());
                        Thread.Sleep(RetryDelayMilliseconds);
                        continue;
                    }

                    if (!WriteBlock(CfHtml, htmlBytes))
                    {
                        return false;
                    }

                    // 纯文本是兜底：宿主不认 HTML 时至少还能粘出可读内容。
                    if (!WriteBlock(CfUnicodeText, textBytes))
                    {
                        Log.Warn("写入纯文本兜底失败，但 HTML 已写入");
                    }

                    return true;
                }
                finally
                {
                    CloseClipboard();
                }
            }

            Log.Error("写入剪贴板重试全部失败", null);
            return false;
        }

        /// <summary>
        /// 只写纯文本（诊断报告之类的场景）。
        /// </summary>
        public static bool TrySetText(string text)
        {
            var payload = Encoding.Unicode.GetBytes((text ?? string.Empty) + "\0");

            for (var attempt = 1; attempt <= RetryTimes; attempt++)
            {
                if (!OpenClipboard(IntPtr.Zero))
                {
                    Thread.Sleep(RetryDelayMilliseconds);
                    continue;
                }

                try
                {
                    if (!EmptyClipboard())
                    {
                        Thread.Sleep(RetryDelayMilliseconds);
                        continue;
                    }

                    return WriteBlock(CfUnicodeText, payload);
                }
                finally
                {
                    CloseClipboard();
                }
            }

            return false;
        }

        /// <summary>
        /// 读纯文本。刻意不走 System.Windows.Forms.Clipboard：
        /// 那个实现依赖 OLE 数据对象与消息循环，在 dllhost 代理进程里，
        /// 正处在宿主同步 COM 回调的调用栈上时，ContainsText 会拿不到内容
        /// （实测功能区按钮报"剪贴板里没有内容"）。直接用 Win32 读最稳。
        /// </summary>
        public static bool TryGetText(out string text)
        {
            text = null;

            for (var attempt = 1; attempt <= RetryTimes; attempt++)
            {
                try
                {
                    if (!OpenClipboard(IntPtr.Zero))
                    {
                        Thread.Sleep(RetryDelayMilliseconds);
                        continue;
                    }

                    try
                    {
                        // Unicode 优先，退而求其次读 ANSI
                        var value = ReadBlock(CfUnicodeText, true);
                        if (string.IsNullOrEmpty(value))
                        {
                            value = ReadBlock(CfText, false);
                        }

                        if (string.IsNullOrEmpty(value))
                        {
                            Log.Warn("剪贴板里没有文本格式的数据");
                            return false;
                        }

                        text = value;
                        return true;
                    }
                    finally
                    {
                        CloseClipboard();
                    }
                }
                catch (Exception exception)
                {
                    Log.Warn("读取剪贴板失败（第 " + attempt + " 次）: " + exception.Message);
                }

                Thread.Sleep(RetryDelayMilliseconds);
            }

            return false;
        }

        private static string ReadBlock(uint format, bool unicode)
        {
            var handle = GetClipboardData(format);
            if (handle == IntPtr.Zero)
            {
                return null;
            }

            var pointer = GlobalLock(handle);
            if (pointer == IntPtr.Zero)
            {
                return null;
            }

            try
            {
                return unicode
                    ? Marshal.PtrToStringUni(pointer)
                    : Marshal.PtrToStringAnsi(pointer);
            }
            finally
            {
                GlobalUnlock(handle);
            }
        }

        private static bool WriteBlock(uint format, byte[] payload)
        {
            if (payload.Length == 0)
            {
                Log.Error("拒绝写入空数据块，format=" + format, null);
                return false;
            }

            // 长度必须显式转成 uint 再交给 UIntPtr：
            // 直接 (UIntPtr)intValue 会走到有歧义的转换路径，实测会分配出 0 字节块。
            var size = new UIntPtr(unchecked((uint)payload.Length));
            var handle = GlobalAlloc(GmemMoveable, size);
            if (handle == IntPtr.Zero)
            {
                Log.Error("GlobalAlloc 失败，长度 " + payload.Length + ", 错误码 " + Marshal.GetLastWin32Error(), null);
                return false;
            }

            var pointer = GlobalLock(handle);
            if (pointer == IntPtr.Zero)
            {
                GlobalFree(handle);
                Log.Error("GlobalLock 失败", null);
                return false;
            }

            try
            {
                Marshal.Copy(payload, 0, pointer, payload.Length);
            }
            finally
            {
                GlobalUnlock(handle);
            }

            // SetClipboardData 成功后内存所有权归系统，不能再 GlobalFree。
            if (SetClipboardData(format, handle) == IntPtr.Zero)
            {
                GlobalFree(handle);
                Log.Error("SetClipboardData 失败，format=" + format + ", 错误码 " + Marshal.GetLastWin32Error(), null);
                return false;
            }

            return true;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool OpenClipboard(IntPtr hWndNewOwner);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool CloseClipboard();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool EmptyClipboard();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr GetClipboardData(uint uFormat);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern uint RegisterClipboardFormat(string lpszFormat);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GlobalAlloc(uint uFlags, UIntPtr dwBytes);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GlobalFree(IntPtr hMem);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GlobalLock(IntPtr hMem);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GlobalUnlock(IntPtr hMem);
    }
}
