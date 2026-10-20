using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using OneMarkDown.Clipboard;
using OneMarkDown.Markdown;
using Xunit;

namespace OneMarkDown.Tests
{
    /// <summary>
    /// 真机剪贴板回归测试。
    /// 这三类 bug 单看 CfHtmlWriter 的输出是发现不了的，必须把数据真正写进剪贴板再读回原始字节：
    /// 1) DataObject 传 byte[] 会写成字面量 "System.Byte[]"
    /// 2) 传 string 会按系统 ANSI 编码写入，与 UTF-8 偏移量错位
    /// 3) GlobalAlloc 长度参数转换错误会分配出 0 字节块
    /// 注意：运行本测试会覆盖当前剪贴板内容。
    /// </summary>
    public class ClipboardRoundTripTests
    {
        private const string Markdown =
            "## 标题测试\n\n这是一段 **加粗** 文字，还有 `代码`。\n\n| 列A | 列B |\n| --- | --- |\n| 中文 | 2 |\n";

        [Fact]
        public void HtmlPayload_SurvivesClipboardRoundTrip()
        {
            RunOnSta(() =>
            {
                var rendered = MarkdownRenderer.Render(Markdown, null);
                var payload = CfHtmlWriter.Build(rendered.Html);

                Assert.True(ClipboardWriter.TrySetHtml(payload, rendered.PlainText), "写入剪贴板失败");

                var diagnostic = string.Empty;
                var raw = ReadHtmlFormat(out diagnostic);

                Assert.NotNull(raw);
                Assert.True(raw.Length > 0, diagnostic);

                var expected = Encoding.UTF8.GetByteCount(payload);
                Assert.Equal(expected, raw.Length);

                var startHtml = ReadOffset(payload, "StartHTML");
                var endHtml = ReadOffset(payload, "EndHTML");
                var startFragment = ReadOffset(payload, "StartFragment");
                var endFragment = ReadOffset(payload, "EndFragment");

                // EndHTML 必须等于实际写入的字节数，否则宿主会判定载荷非法。
                Assert.Equal(raw.Length, endHtml);
                Assert.True(endFragment > startFragment);

                var marker = Encoding.UTF8.GetString(raw, startFragment, Math.Min(32, endFragment - startFragment));
                Assert.StartsWith("<", marker);

                var htmlHead = Encoding.UTF8.GetString(raw, startHtml, 5);
                Assert.StartsWith("<html", htmlHead);

                // 中文必须原样保留（说明没有走 ANSI 编码）。
                var whole = Encoding.UTF8.GetString(raw, 0, raw.Length);
                Assert.Contains("标题测试", whole);
            });
        }

        [Fact]
        public void PlainTextFallback_IsAlsoWritten()
        {
            RunOnSta(() =>
            {
                var rendered = MarkdownRenderer.Render("# 纯文本兜底\n\n正文", null);
                Assert.True(ClipboardWriter.TrySetHtml(CfHtmlWriter.Build(rendered.Html), rendered.PlainText));

                string text;
                Assert.True(ClipboardWriter.TryGetText(out text));
                Assert.Contains("纯文本兜底", text);
            });
        }

        private static int ReadOffset(string payload, string name)
        {
            var match = Regex.Match(payload, name + @":(\d{10})");
            Assert.True(match.Success, "没有找到 " + name);
            return int.Parse(match.Groups[1].Value);
        }

        private static byte[] ReadHtmlFormat(out string diagnostic)
        {
            diagnostic = string.Empty;

            for (var attempt = 0; attempt < 10; attempt++)
            {
                if (!OpenClipboard(IntPtr.Zero))
                {
                    Thread.Sleep(60);
                    continue;
                }

                try
                {
                    var handle = GetClipboardData(RegisterClipboardFormat("HTML Format"));
                    if (handle == IntPtr.Zero)
                    {
                        diagnostic = "剪贴板里没有 HTML Format";
                        return null;
                    }

                    var size = (long)GlobalSize(handle);
                    diagnostic = "GlobalSize=" + size;

                    var pointer = GlobalLock(handle);
                    try
                    {
                        var buffer = new byte[size];
                        if (size > 0)
                        {
                            Marshal.Copy(pointer, buffer, 0, (int)size);
                        }

                        return buffer;
                    }
                    finally
                    {
                        GlobalUnlock(handle);
                    }
                }
                finally
                {
                    CloseClipboard();
                }
            }

            diagnostic = "10 次都没能打开剪贴板";
            return null;
        }

        private static void RunOnSta(Action action)
        {
            Exception error = null;

            var thread = new Thread(() =>
            {
                try
                {
                    action();
                }
                catch (Exception exception)
                {
                    error = exception;
                }
            });

            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();

            if (error != null)
            {
                throw new InvalidOperationException("STA 线程内断言失败: " + error.Message, error);
            }
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern uint RegisterClipboardFormat(string format);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool OpenClipboard(IntPtr owner);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool CloseClipboard();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr GetClipboardData(uint format);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GlobalLock(IntPtr handle);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GlobalUnlock(IntPtr handle);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern UIntPtr GlobalSize(IntPtr handle);
    }
}
