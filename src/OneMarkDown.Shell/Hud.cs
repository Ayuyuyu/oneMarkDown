using System;
using System.Drawing;
using System.Windows.Forms;

namespace OneMarkDown.Shell
{
    /// <summary>
    /// 无边框、不抢焦点、自动消失的提示条。
    /// 没有托盘图标，所有反馈都靠它。
    /// </summary>
    internal sealed class Hud : Form
    {
        private const int WsExNoActivate = 0x08000000;
        private const int WsExToolWindow = 0x00000080;
        private const int MaxTextWidth = 520;

        private readonly Label _label;
        private readonly Timer _timer;

        public Hud()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = Color.FromArgb(28, 28, 30);
            Padding = new Padding(18, 12, 18, 12);

            _label = new Label
            {
                AutoSize = false,
                ForeColor = Color.White,
                BackColor = Color.Transparent,
                Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point)
            };
            Controls.Add(_label);

            _timer = new Timer();
            _timer.Tick += (sender, args) =>
            {
                _timer.Stop();
                Hide();
            };
        }

        protected override bool ShowWithoutActivation
        {
            get { return true; }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var parameters = base.CreateParams;
                parameters.ExStyle |= WsExNoActivate;
                parameters.ExStyle |= WsExToolWindow;
                return parameters;
            }
        }

        public void ShowMessage(string text, bool isWarning)
        {
            try
            {
                if (string.IsNullOrEmpty(text))
                {
                    return;
                }

                _label.ForeColor = isWarning ? Color.FromArgb(255, 209, 102) : Color.White;

                var measured = TextRenderer.MeasureText(
                    text,
                    _label.Font,
                    new Size(MaxTextWidth, int.MaxValue),
                    TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);

                _label.Bounds = new Rectangle(Padding.Left, Padding.Top, measured.Width, measured.Height);
                ClientSize = new Size(measured.Width + Padding.Horizontal, measured.Height + Padding.Vertical);
                _label.Text = text;

                var area = Screen.FromPoint(Cursor.Position).WorkingArea;
                Location = new Point(area.Right - Width - 24, area.Bottom - Height - 24);

                if (!Visible)
                {
                    Show();
                }
                else
                {
                    BringToFront();
                }

                _timer.Stop();
                _timer.Interval = isWarning ? 6000 : 1800;
                _timer.Start();
            }
            catch
            {
                // 提示条失败不能影响主流程。
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _timer.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
