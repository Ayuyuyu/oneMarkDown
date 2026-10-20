using System;
using Microsoft.Office.Core;
using OneMarkDown.Commands;
using OneMarkDown.Clipboard;
using OneMarkDown.Diagnostics;
using OneMarkDown.Ui;

namespace OneMarkDown.Ribbon
{
    /// <summary>
    /// Ribbon 回调的实际处理器。所有入口都包了 try/catch：
    /// 回调抛异常会被 Office 的韧性机制记一次，累计多次会直接禁用加载项。
    ///
    /// 动作都在本进程内完成（加载项跑在 dllhost.exe 代理里，和 OneMore 一样）。
    /// 两个跨进程必须注意的点已经处理：
    ///   1) 对话框必须以 OneNote 主窗口为 owner 并前置（见 Dialogs）
    ///   2) 按键注入用 keybd_event 而不是 SendKeys（见 KeyInjector）
    /// </summary>
    public sealed class RibbonController
    {
        private static readonly RibbonController Shared = new RibbonController();

        private RibbonController()
        {
        }

        public static RibbonController Instance
        {
            get { return Shared; }
        }

        public IRibbonUI RibbonUi { get; private set; }

        public void OnRibbonLoad(IRibbonUI ribbonUi)
        {
            try
            {
                RibbonUi = ribbonUi;
                Log.Info("Ribbon onLoad 成功");
            }
            catch (Exception exception)
            {
                Log.Error("Ribbon onLoad 失败", exception);
            }
        }

        public void OnPasteMarkdown(IRibbonControl control)
        {
            Guard("粘贴 Markdown", InsertMarkdownCommand.PasteFromClipboard);
        }

        public void OnInsertFile(IRibbonControl control)
        {
            Guard("从 .md 文件插入", InsertMarkdownCommand.InsertFromFile);
        }

        public void OnHelp(IRibbonControl control)
        {
            Guard("诊断信息", () =>
            {
                var report = DiagnosticsReport.Build();

                if (ClipboardWriter.TrySetText(report))
                {
                    Log.Info("诊断信息已复制到剪贴板");
                }
                else
                {
                    Log.Warn("复制诊断信息失败");
                }

                Dialogs.Info(
                    "诊断信息已复制到剪贴板，可直接粘贴给别人。\r\n\r\n" +
                    "日志目录：\r\n" + Log.DirectoryPath + "\r\n\r\n" +
                    "----- 摘要 -----\r\n" +
                    FirstLines(report, 12));
            });
        }

        private static void Guard(string title, Action action)
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                Log.Error(title + " 未捕获异常", exception);
                Dialogs.Error(title + "时出错：" + exception.Message + "\r\n\r\n日志：\r\n" + Log.FilePath);
            }
        }

        private static string FirstLines(string text, int count)
        {
            var lines = (text ?? string.Empty).Replace("\r\n", "\n").Split('\n');
            var length = Math.Min(count, lines.Length);
            var result = new string[length];
            Array.Copy(lines, result, length);
            return string.Join(Environment.NewLine, result);
        }
    }
}
