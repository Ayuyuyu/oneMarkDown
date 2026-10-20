using System;
using System.Collections.Generic;
using OneMarkDown.Clipboard;
using OneMarkDown.Diagnostics;
using OneMarkDown.Markdown;

namespace OneMarkDown.Shell
{
    /// <summary>
    /// 一次完整的「Markdown → 富文本 → 剪贴板 → Ctrl+V」流程。
    /// 热键、托盘菜单两条入口都走这里，保证行为一致。
    /// </summary>
    internal static class PasteFlow
    {
        private const int MaxInputCharacters = 2 * 1024 * 1024;

        internal sealed class Outcome
        {
            public Outcome(string message, bool isWarning)
            {
                Message = message;
                IsWarning = isWarning;
            }

            public string Message { get; private set; }

            public bool IsWarning { get; private set; }
        }

        internal static Outcome Run(string markdown, string baseDirectory, string sourceName, IntPtr target)
        {
            if (string.IsNullOrWhiteSpace(markdown))
            {
                return Warn("内容是空的，没有可插入的内容。");
            }

            if (markdown.Length > MaxInputCharacters)
            {
                return Warn("内容太大（" + markdown.Length + " 字符），请拆分后再插入。");
            }

            Log.Info("开始转换: 来源=" + sourceName + ", 长度=" + markdown.Length);

            var result = MarkdownRenderer.Render(markdown, baseDirectory);
            if (string.IsNullOrWhiteSpace(result.Html))
            {
                return Warn("转换后没有可插入的内容。");
            }

            if (!ClipboardWriter.TrySetHtml(CfHtmlWriter.Build(result.Html), result.PlainText))
            {
                return Warn("写入剪贴板失败，可能有程序正在占用剪贴板，请稍后重试。");
            }

            var pasted = ClipboardPaster.TryPaste(target);
            var warning = result.Warnings.Count > 0
                ? "\r\n（" + result.Warnings.Count + " 张图片未内嵌：" + FirstWarning(result.Warnings) + "）"
                : string.Empty;

            if (pasted)
            {
                return new Outcome(
                    "已发送 Ctrl+V（" + result.ElementCount + " 个元素，耗时 " + result.ElapsedMilliseconds + "ms）" + warning,
                    warning.Length > 0);
            }

            return Warn("内容已经放进剪贴板，但没能自动粘贴。\r\n请切到目标窗口按一次 Ctrl+V。" + warning);
        }

        private static Outcome Warn(string message)
        {
            return new Outcome(message, true);
        }

        private static string FirstWarning(IList<string> warnings)
        {
            var text = warnings[0] ?? string.Empty;
            return text.Length <= 60 ? text : text.Substring(0, 60) + "...";
        }
    }
}
