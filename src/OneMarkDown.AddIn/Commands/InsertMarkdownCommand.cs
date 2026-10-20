using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;
using OneMarkDown.Clipboard;
using OneMarkDown.Diagnostics;
using OneMarkDown.Input;
using OneMarkDown.Markdown;
using OneMarkDown.Ui;

namespace OneMarkDown.Commands
{
    /// <summary>
    /// 编排一条完整的粘贴链路：取源文本 -> 渲染 -> 剪贴板 -> 注入 Ctrl+V。
    /// 任何一步失败都退化为"内容已在剪贴板，请手动 Ctrl+V"。
    /// </summary>
    public static class InsertMarkdownCommand
    {
        private const int MaxInputCharacters = 2 * 1024 * 1024;

        public static void PasteFromClipboard()
        {
            string text;
            if (!ClipboardWriter.TryGetText(out text))
            {
                Dialogs.Warn(
                    "剪贴板里没有可用的文本。\r\n\r\n" +
                    "请先复制 Markdown 源文本，再点这个按钮。");
                return;
            }

            Execute(text, null, "剪贴板");
        }

        public static void InsertFromFile()
        {
            try
            {
                using (var dialog = new OpenFileDialog())
                {
                    dialog.Title = "选择 Markdown 文件";
                    dialog.Filter = "Markdown 文件 (*.md;*.markdown)|*.md;*.markdown|文本文件 (*.txt)|*.txt|所有文件 (*.*)|*.*";
                    dialog.CheckFileExists = true;

                    // 代理进程里必须先把 OneNote 拉到前台并给它当 owner：
                    // 否则对话框落在 OneNote 后面，而宿主正同步等这个回调返回，
                    // 表现出来就是 OneNote 卡死。
                    Dialogs.BringHostToFront();

                    if (dialog.ShowDialog(Dialogs.GetOwnerWindow()) != DialogResult.OK)
                    {
                        return;
                    }

                    var content = File.ReadAllText(dialog.FileName);
                    Execute(content, Path.GetDirectoryName(dialog.FileName), Path.GetFileName(dialog.FileName));
                }
            }
            catch (Exception exception)
            {
                Log.Error("读取 Markdown 文件失败", exception);
                Dialogs.Error("读取文件失败：" + exception.Message);
            }
        }

        private static void Execute(string markdown, string baseDirectory, string sourceName)
        {
            var stopwatch = Stopwatch.StartNew();

            try
            {
                if (string.IsNullOrWhiteSpace(markdown))
                {
                    Dialogs.Warn("内容是空的，没有可插入的内容。");
                    return;
                }

                if (markdown.Length > MaxInputCharacters)
                {
                    Dialogs.Warn(
                        "内容太大（" + markdown.Length + " 字符），超过 " + MaxInputCharacters + " 字符的上限。\r\n" +
                        "请拆分后再插入。");
                    return;
                }

                Log.Info("开始转换: 来源=" + sourceName + ", 长度=" + markdown.Length);

                var result = MarkdownRenderer.Render(markdown, baseDirectory);

                if (string.IsNullOrWhiteSpace(result.Html))
                {
                    Dialogs.Warn("转换后没有任何可插入的内容。");
                    return;
                }

                var cfHtml = CfHtmlWriter.Build(result.Html);
                if (!ClipboardWriter.TrySetHtml(cfHtml, result.PlainText))
                {
                    Dialogs.Error("写入剪贴板失败，可能有其他程序正在占用剪贴板。请稍后重试。");
                    return;
                }

                var outcome = PasteInjector.TryPaste();

                stopwatch.Stop();
                Log.Info("完成: 元素 " + result.ElementCount + ", 总耗时 " + stopwatch.ElapsedMilliseconds +
                         "ms, 自动粘贴=" + (outcome == PasteOutcome.Pasted));

                if (outcome == PasteOutcome.ManualRequired)
                {
                    Dialogs.Warn(
                        "内容已经放进剪贴板，但没能自动按 Ctrl+V。\r\n\r\n" +
                        "请回到笔记页面，手动按一次 Ctrl+V。");
                }
                else if (result.Warnings.Count > 0)
                {
                    Dialogs.Warn(
                        "已插入，但有 " + result.Warnings.Count + " 个图片没能内嵌：\r\n\r\n" +
                        string.Join("\r\n", result.Warnings) + "\r\n\r\n" +
                        "相对路径图片建议改用「从 .md 文件插入」。");
                }
            }
            catch (Exception exception)
            {
                Log.Error("插入 Markdown 失败", exception);
                Dialogs.Error("处理失败：" + exception.Message + "\r\n\r\n详细信息已写入日志：\r\n" + Log.FilePath);
            }
        }
    }
}
