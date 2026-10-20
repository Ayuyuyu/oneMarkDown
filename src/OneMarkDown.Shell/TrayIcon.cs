using System;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using OneMarkDown.Diagnostics;

namespace OneMarkDown.Shell
{
    /// <summary>
    /// 托盘图标与右键菜单。图标从内嵌资源加载，多尺寸 ICO（DIB 帧）保证各 DPI 下都清晰。
    /// </summary>
    internal sealed class TrayIcon : IDisposable
    {
        private const string ResourceName = "OneMarkDown.app.ico";

        private readonly NotifyIcon _notifyIcon;

        public TrayIcon(string pasteKey, string fileKey, string quitKey,
            Action onPaste, Action onFile, Action onLog, Action onAbout, Action onQuit)
        {
            var menu = new ContextMenuStrip { ShowImageMargin = false };

            var paste = new ToolStripMenuItem("粘贴 Markdown") { ShortcutKeyDisplayString = pasteKey };
            paste.Click += (sender, args) => onPaste();
            menu.Items.Add(paste);

            var file = new ToolStripMenuItem("从 .md 文件插入") { ShortcutKeyDisplayString = fileKey };
            file.Click += (sender, args) => onFile();
            menu.Items.Add(file);

            menu.Items.Add(new ToolStripSeparator());

            var log = new ToolStripMenuItem("打开日志目录");
            log.Click += (sender, args) => onLog();
            menu.Items.Add(log);

            var about = new ToolStripMenuItem("关于 / 热键说明");
            about.Click += (sender, args) => onAbout();
            menu.Items.Add(about);

            menu.Items.Add(new ToolStripSeparator());

            var quit = new ToolStripMenuItem("退出") { ShortcutKeyDisplayString = quitKey };
            quit.Click += (sender, args) => onQuit();
            menu.Items.Add(quit);

            _notifyIcon = new NotifyIcon
            {
                Icon = LoadIcon(),
                Text = "OneMarkDown",
                Visible = true,
                ContextMenuStrip = menu
            };

            // 双击走"关于"，避免误触直接往笔记里粘东西。
            _notifyIcon.DoubleClick += (sender, args) => onAbout();
        }

        public void SetTooltip(string text)
        {
            try
            {
                if (string.IsNullOrEmpty(text))
                {
                    return;
                }

                // NotifyIcon.Text 上限 63 个字符，超了会抛异常。
                _notifyIcon.Text = text.Length <= 63 ? text : text.Substring(0, 60) + "...";
            }
            catch
            {
            }
        }

        public void Dispose()
        {
            try
            {
                _notifyIcon.Visible = false;
                _notifyIcon.Dispose();
            }
            catch
            {
            }
        }

        private static Icon LoadIcon()
        {
            try
            {
                var assembly = Assembly.GetExecutingAssembly();
                using (var stream = assembly.GetManifestResourceStream(ResourceName))
                {
                    if (stream != null)
                    {
                        return new Icon(stream, SystemInformation.SmallIconSize);
                    }
                }

                Log.Error("找不到内嵌图标资源 " + ResourceName, null);
            }
            catch (Exception exception)
            {
                Log.Error("加载托盘图标失败", exception);
            }

            return SystemIcons.Application;
        }
    }
}
