using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows.Forms;
using OneMarkDown.Clipboard;
using OneMarkDown.Diagnostics;
using OneMarkDown.Markdown;

namespace OneMarkDown.Shell
{
    /// <summary>
    /// 常驻应用：托盘图标 + 全局热键。
    /// 热键按候选列表依次尝试，被占用就自动换下一个，并把实际生效的组合告诉用户。
    /// </summary>
    internal sealed class ShellApplicationContext : ApplicationContext
    {
        private const int MaxInputCharacters = 2 * 1024 * 1024;

        private static readonly HotkeyCandidate[] PasteCandidates =
        {
            new HotkeyCandidate(HotkeyWindow.ModWin | HotkeyWindow.ModAlt, Keys.M, "Win+Alt+M"),
            new HotkeyCandidate(HotkeyWindow.ModWin | HotkeyWindow.ModAlt, Keys.V, "Win+Alt+V"),
            new HotkeyCandidate(HotkeyWindow.ModControl | HotkeyWindow.ModAlt, Keys.M, "Ctrl+Alt+M"),
            new HotkeyCandidate(HotkeyWindow.ModWin | HotkeyWindow.ModShift, Keys.M, "Win+Shift+M")
        };

        private static readonly HotkeyCandidate[] FileCandidates =
        {
            new HotkeyCandidate(HotkeyWindow.ModWin | HotkeyWindow.ModAlt, Keys.F, "Win+Alt+F"),
            new HotkeyCandidate(HotkeyWindow.ModWin | HotkeyWindow.ModAlt, Keys.O, "Win+Alt+O"),
            new HotkeyCandidate(HotkeyWindow.ModControl | HotkeyWindow.ModAlt, Keys.F, "Ctrl+Alt+F")
        };

        private static readonly HotkeyCandidate[] QuitCandidates =
        {
            new HotkeyCandidate(HotkeyWindow.ModWin | HotkeyWindow.ModAlt, Keys.Q, "Win+Alt+Q"),
            new HotkeyCandidate(HotkeyWindow.ModControl | HotkeyWindow.ModAlt, Keys.Q, "Ctrl+Alt+Q"),
            new HotkeyCandidate(HotkeyWindow.ModControl | HotkeyWindow.ModShift, Keys.Q, "Ctrl+Shift+Q")
        };

        private readonly HotkeyWindow _hotkeys;
        private readonly Hud _hud;
        private readonly TrayIcon _tray;
        private readonly string _pasteKeyText;
        private readonly string _fileKeyText;
        private readonly string _quitKeyText;

        public ShellApplicationContext()
        {
            _hud = new Hud();
            _hotkeys = new HotkeyWindow();
            _hotkeys.HotkeyPressed += OnHotkey;

            _pasteKeyText = Bind("粘贴 Markdown", HotkeyWindow.IdPaste, PasteCandidates);
            _fileKeyText = Bind("从 .md 文件插入", HotkeyWindow.IdFile, FileCandidates);
            _quitKeyText = Bind("退出", HotkeyWindow.IdQuit, QuitCandidates);

            _tray = new TrayIcon(
                _pasteKeyText ?? "未绑定",
                _fileKeyText ?? "未绑定",
                _quitKeyText ?? "未绑定",
                () => Defer(PasteFromTray),
                () => Defer(InsertFileFromTray),
                OpenLogFolder,
                ShowAbout,
                Shutdown);

            _tray.SetTooltip("OneMarkDown - 粘贴 Markdown (" + (_pasteKeyText ?? "未绑定") + ")");

            var builder = new StringBuilder();
            builder.AppendLine("OneMarkDown 已启动，托盘图标已就位");
            builder.AppendLine("粘贴 Markdown：" + (_pasteKeyText ?? "热键全部被占用"));
            builder.AppendLine("从 .md 文件插入：" + (_fileKeyText ?? "热键全部被占用"));

            var hasFailure = _pasteKeyText == null || _fileKeyText == null;
            _hud.ShowMessage(builder.ToString().TrimEnd(), hasFailure);

            Log.Info("启动完成: 托盘=已创建, 粘贴=" + (_pasteKeyText ?? "失败") +
                     ", 文件=" + (_fileKeyText ?? "失败") +
                     ", 退出=" + (_quitKeyText ?? "失败"));
        }

        private string Bind(string description, int id, HotkeyCandidate[] candidates)
        {
            foreach (var candidate in candidates)
            {
                if (_hotkeys.TryRegister(id, candidate.Modifiers, candidate.Key))
                {
                    return candidate.Display;
                }
            }

            Log.Error("热键注册失败: " + description, null);
            return null;
        }

        private void OnHotkey(int id)
        {
            // 先抓住当前前台窗口，等会儿如果弹过对话框，还要把焦点还回去。
            var target = ClipboardPaster.CaptureForeground();

            try
            {
                switch (id)
                {
                    case HotkeyWindow.IdPaste:
                        Paste(target);
                        break;

                    case HotkeyWindow.IdFile:
                        InsertFile(target);
                        break;

                    case HotkeyWindow.IdQuit:
                        Shutdown();
                        break;
                }
            }
            catch (Exception exception)
            {
                Log.Error("处理热键 " + id + " 失败", exception);
                _hud.ShowMessage("处理失败：" + exception.Message, true);
            }
        }

        // 菜单关闭后系统会把焦点还给之前的活动窗口，这里不强行指定目标，
        // 谁在前台就粘给谁。
        private void PasteFromTray()
        {
            Paste(IntPtr.Zero);
        }

        private void InsertFileFromTray()
        {
            InsertFile(IntPtr.Zero);
        }

        private void Paste(IntPtr target)
        {
            string text;
            if (!ClipboardWriter.TryGetText(out text))
            {
                _hud.ShowMessage("剪贴板里没有可用的文本。\r\n请先复制 Markdown 源文本。", true);
                return;
            }

            Convert(text, null, "剪贴板", target);
        }

        private void InsertFile(IntPtr target)
        {
            using (var dialog = new OpenFileDialog())
            {
                dialog.Title = "选择 Markdown 文件";
                dialog.Filter = "Markdown 文件 (*.md;*.markdown)|*.md;*.markdown|文本文件 (*.txt)|*.txt|所有文件 (*.*)|*.*";
                dialog.CheckFileExists = true;

                if (dialog.ShowDialog() != DialogResult.OK)
                {
                    return;
                }

                var content = File.ReadAllText(dialog.FileName);
                Convert(content, Path.GetDirectoryName(dialog.FileName), Path.GetFileName(dialog.FileName), target);
            }
        }

        private void Convert(string markdown, string baseDirectory, string sourceName, IntPtr target)
        {
            var outcome = PasteFlow.Run(markdown, baseDirectory, sourceName, target);
            _hud.ShowMessage(outcome.Message, outcome.IsWarning);
        }

        private void OpenLogFolder()
        {
            try
            {
                Process.Start("explorer.exe", Log.DirectoryPath);
            }
            catch (Exception exception)
            {
                Log.Error("打开日志目录失败", exception);
            }
        }

        private void ShowAbout()
        {
            try
            {
                var assembly = GetType().Assembly;
                var version = assembly.GetName().Version;

                var text = new StringBuilder();
                text.AppendLine("OneMarkDown " + version);
                text.AppendLine();
                text.AppendLine("粘贴 Markdown：" + (_pasteKeyText ?? "未绑定"));
                text.AppendLine("从 .md 文件插入：" + (_fileKeyText ?? "未绑定"));
                text.AppendLine("退出：" + (_quitKeyText ?? "未绑定"));
                text.AppendLine();
                text.AppendLine("用法：复制 Markdown → 切到 OneNote 点好光标 → 按粘贴热键。");
                text.AppendLine("从 .md 文件插入时，相对路径的图片会按 base64 内嵌。");
                text.AppendLine();
                text.AppendLine("日志目录：");
                text.AppendLine(Log.DirectoryPath);

                MessageBox.Show(
                    text.ToString(),
                    "OneMarkDown",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            catch (Exception exception)
            {
                Log.Error("显示关于信息失败", exception);
            }
        }

        /// <summary>
        /// 托盘菜单关闭后再执行，否则前台窗口还停在菜单上，Ctrl+V 会打空。
        /// </summary>
        private void Defer(Action action)
        {
            // 250ms 是实测下来菜单完全关闭、焦点已还给上一个窗口的时间。
            var timer = new System.Windows.Forms.Timer { Interval = 250 };
            timer.Tick += (sender, args) =>
            {
                timer.Stop();
                timer.Dispose();

                try
                {
                    action();
                }
                catch (Exception exception)
                {
                    Log.Error("托盘菜单动作失败", exception);
                    _hud.ShowMessage("处理失败：" + exception.Message, true);
                }
            };
            timer.Start();
        }

        private void Shutdown()
        {
            Log.Info("Shell 退出");
            _hotkeys.Unregister(HotkeyWindow.IdPaste);
            _hotkeys.Unregister(HotkeyWindow.IdFile);
            _hotkeys.Unregister(HotkeyWindow.IdQuit);
            _hotkeys.Dispose();
            _tray.Dispose();
            _hud.Dispose();
            ExitThread();
        }

        private sealed class HotkeyCandidate
        {
            public HotkeyCandidate(uint modifiers, Keys key, string display)
            {
                Modifiers = modifiers;
                Key = key;
                Display = display;
            }

            public uint Modifiers { get; private set; }

            public Keys Key { get; private set; }

            public string Display { get; private set; }
        }
    }
}
