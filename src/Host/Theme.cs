using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace PclPatchHost
{
    internal static class Theme
    {
        public static readonly Color Desktop = Color.FromArgb(192, 192, 192);
        public static readonly Color Face = Color.FromArgb(192, 192, 192);
        public static readonly Color Light = Color.FromArgb(255, 255, 255);
        public static readonly Color Shadow = Color.FromArgb(128, 128, 128);
        public static readonly Color Dark = Color.FromArgb(64, 64, 64);
        public static readonly Color Navy = Color.FromArgb(0, 0, 128);
        public static readonly Color NavyLight = Color.FromArgb(16, 132, 208);
        public static readonly Color Black = Color.FromArgb(0, 0, 0);
        public static readonly Color Screen = Color.FromArgb(3, 9, 5);
        public static readonly Color Green = Color.FromArgb(90, 255, 124);
        public static readonly Color GreenDim = Color.FromArgb(30, 150, 74);
        public static readonly Color Amber = Color.FromArgb(255, 184, 48);
        public static readonly Color Cyan = Color.FromArgb(105, 226, 255);
        public static readonly Color Red = Color.FromArgb(255, 90, 90);
        public static readonly Color Text = Color.FromArgb(16, 16, 16);
        public static readonly Color Muted = Color.FromArgb(72, 72, 72);
        public static readonly Color Disabled = Color.FromArgb(160, 160, 160);
        public static readonly Color LogBackground = Color.FromArgb(3, 9, 5);
        public static readonly Color LogTime = Color.FromArgb(32, 125, 65);
        public static readonly Color LogText = Color.FromArgb(90, 255, 124);
        public static readonly Color LogTrack = Color.FromArgb(18, 42, 27);
        public static readonly Color LogThumb = Color.FromArgb(38, 130, 71);

        public static Font Ui(float size)
        {
            return Ui(size, FontStyle.Regular);
        }

        public static Font Ui(float size, FontStyle style)
        {
            try { return new Font("Microsoft YaHei UI", size, style, GraphicsUnit.Point); }
            catch
            {
                try { return new Font("Microsoft YaHei", size, style, GraphicsUnit.Point); }
                catch { return new Font(FontFamily.GenericSansSerif, size, style, GraphicsUnit.Point); }
            }
        }

        public static Font Mono(float size)
        {
            return Mono(size, FontStyle.Regular);
        }

        public static Font Mono(float size, FontStyle style)
        {
            try { return new Font("Consolas", size, style, GraphicsUnit.Point); }
            catch
            {
                try { return new Font("Lucida Console", size, style, GraphicsUnit.Point); }
                catch { return new Font(FontFamily.GenericMonospace, size, style, GraphicsUnit.Point); }
            }
        }

        public static Image LoadAvatar()
        {
            try
            {
                using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("PclPatchHost.avatar.png"))
                {
                    if (stream == null) return null;
                    using (var image = Image.FromStream(stream))
                        return new Bitmap(image);
                }
            }
            catch { return null; }
        }

        public static Icon LoadAppIcon()
        {
            try
            {
                Icon icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                if (icon != null) return icon;
            }
            catch { }
            return SystemIcons.Application;
        }
    }

    internal static class WindowChrome
    {
        private const int WM_NCLBUTTONDOWN = 0xA1;
        private const int HTCAPTION = 2;

        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        public static void AttachDrag(Form form, Control control)
        {
            control.MouseDown += delegate(object sender, MouseEventArgs e)
            {
                if (e.Button != MouseButtons.Left) return;
                ReleaseCapture();
                SendMessage(form.Handle, WM_NCLBUTTONDOWN, (IntPtr)HTCAPTION, IntPtr.Zero);
            };
        }
    }

    internal class ThemedForm : Form
    {
        public ThemedForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            BackColor = Theme.Desktop;
            Font = Theme.Ui(9f);
            DoubleBuffered = true;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            KeyPreview = true;
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ClassStyle |= 0x00020000;
                return cp;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.Clear(Theme.Desktop);
            DrawBevel(e.Graphics, new Rectangle(0, 0, ClientSize.Width, ClientSize.Height), false);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape)
            {
                Close();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        internal static void DrawBevel(Graphics graphics, Rectangle rect, bool sunken)
        {
            if (rect.Width < 2 || rect.Height < 2) return;
            Color topLeft = sunken ? Theme.Shadow : Theme.Light;
            Color bottomRight = sunken ? Theme.Light : Theme.Shadow;
            using (var light = new Pen(topLeft))
            using (var dark = new Pen(bottomRight))
            {
                graphics.DrawLine(light, rect.Left, rect.Top, rect.Right - 1, rect.Top);
                graphics.DrawLine(light, rect.Left, rect.Top, rect.Left, rect.Bottom - 1);
                graphics.DrawLine(dark, rect.Left, rect.Bottom - 1, rect.Right - 1, rect.Bottom - 1);
                graphics.DrawLine(dark, rect.Right - 1, rect.Top, rect.Right - 1, rect.Bottom - 1);
            }
        }
    }

    internal class RetroPanel : Panel
    {
        private Color fillColor = Theme.Face;
        private bool sunken;

        public RetroPanel()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = fillColor;
        }

        public Color FillColor
        {
            get { return fillColor; }
            set { fillColor = value; BackColor = value; Invalidate(); }
        }

        public bool Sunken
        {
            get { return sunken; }
            set { sunken = value; Invalidate(); }
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.Clear(fillColor);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(fillColor);
            ThemedForm.DrawBevel(e.Graphics, new Rectangle(0, 0, Width, Height), sunken);
            if (sunken)
                ThemedForm.DrawBevel(e.Graphics, new Rectangle(2, 2, Math.Max(0, Width - 4), Math.Max(0, Height - 4)), true);
            base.OnPaint(e);
        }
    }

    internal class RetroButton : Button
    {
        private bool hover;
        private bool pressed;
        private bool accent;

        public RetroButton()
        {
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            UseVisualStyleBackColor = false;
            BackColor = Theme.Face;
            Cursor = Cursors.Hand;
            Font = Theme.Ui(8.5f, FontStyle.Bold);
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        public bool Accent
        {
            get { return accent; }
            set { accent = value; Invalidate(); }
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            hover = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            hover = false;
            pressed = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            pressed = true;
            Invalidate();
            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            pressed = false;
            Invalidate();
            base.OnMouseUp(e);
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            Invalidate();
            base.OnEnabledChanged(e);
        }

        protected override void OnGotFocus(EventArgs e)
        {
            Invalidate();
            base.OnGotFocus(e);
        }

        protected override void OnLostFocus(EventArgs e)
        {
            Invalidate();
            base.OnLostFocus(e);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.Clear(BackColor);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(BackColor);
            Color face = accent ? Theme.Black : Theme.Face;
            if (Enabled && hover) face = accent ? Color.FromArgb(12, 20, 14) : Color.FromArgb(210, 210, 210);
            if (!Enabled) face = Theme.Face;
            using (var brush = new SolidBrush(face))
                e.Graphics.FillRectangle(brush, ClientRectangle);

            var rect = new Rectangle(0, 0, Width, Height);
            if (pressed)
            {
                ThemedForm.DrawBevel(e.Graphics, rect, true);
                ThemedForm.DrawBevel(e.Graphics, new Rectangle(2, 2, Math.Max(0, Width - 4), Math.Max(0, Height - 4)), true);
            }
            else
            {
                ThemedForm.DrawBevel(e.Graphics, rect, false);
                ThemedForm.DrawBevel(e.Graphics, new Rectangle(2, 2, Math.Max(0, Width - 4), Math.Max(0, Height - 4)), false);
            }

            Color text = accent ? Theme.Green : Theme.Text;
            if (!Enabled) text = Theme.Disabled;
            TextRenderer.DrawText(e.Graphics, Text, Font, new Rectangle(4, 1, Width - 8, Height - 3), text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
            if (Focused && ShowFocusCues)
                ControlPaint.DrawFocusRectangle(e.Graphics, new Rectangle(3, 3, Math.Max(0, Width - 7), Math.Max(0, Height - 7)));
        }
    }

    internal class RetroCheckBox : Control
    {
        private bool isChecked;
        private bool hover;

        public event EventHandler CheckedChanged;

        public RetroCheckBox()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.Selectable, true);
            BackColor = Theme.Face;
            Font = Theme.Mono(8f, FontStyle.Bold);
            Size = new Size(150, 22);
            TabStop = true;
            Cursor = Cursors.Hand;
            AccessibleRole = AccessibleRole.CheckButton;
        }

        public bool Checked
        {
            get { return isChecked; }
            set
            {
                if (isChecked == value) return;
                isChecked = value;
                Invalidate();
                if (CheckedChanged != null) CheckedChanged(this, EventArgs.Empty);
            }
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            hover = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            hover = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            Focus();
            base.OnMouseDown(e);
        }

        protected override void OnClick(EventArgs e)
        {
            Checked = !Checked;
            base.OnClick(e);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space)
            {
                Checked = !Checked;
                e.Handled = true;
                return;
            }
            base.OnKeyDown(e);
        }

        protected override void OnGotFocus(EventArgs e)
        {
            Invalidate();
            base.OnGotFocus(e);
        }

        protected override void OnLostFocus(EventArgs e)
        {
            Invalidate();
            base.OnLostFocus(e);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            if (Parent != null) e.Graphics.Clear(Parent.BackColor);
            else e.Graphics.Clear(Theme.Desktop);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Parent != null ? Parent.BackColor : Theme.Desktop);
            var box = new Rectangle(0, 3, 14, 14);
            using (var brush = new SolidBrush(hover ? Color.FromArgb(232, 255, 240) : Color.White))
                e.Graphics.FillRectangle(brush, box);
            ThemedForm.DrawBevel(e.Graphics, box, true);
            if (isChecked)
            {
                using (var pen = new Pen(Theme.GreenDim, 2f))
                {
                    e.Graphics.DrawLines(pen, new[]
                    {
                        new Point(3, 10),
                        new Point(6, 13),
                        new Point(12, 6)
                    });
                }
            }
            TextRenderer.DrawText(e.Graphics, Text, Font, new Rectangle(21, 0, Math.Max(0, Width - 21), Height),
                Enabled ? Theme.Text : Theme.Disabled,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPrefix);
            if (Focused)
                ControlPaint.DrawFocusRectangle(e.Graphics, new Rectangle(0, 0, Width - 1, Height - 1));
        }
    }

    internal class RetroStatus : Control
    {
        private Color ledColor = Theme.GreenDim;
        private Color textColor = Theme.Green;
        private bool fillScreen = true;

        public RetroStatus()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Screen;
            Font = Theme.Mono(8f, FontStyle.Bold);
            Size = new Size(150, 23);
            TabStop = false;
        }

        public Color LedColor
        {
            get { return ledColor; }
            set { ledColor = value; Invalidate(); }
        }

        public Color TextColor
        {
            get { return textColor; }
            set { textColor = value; Invalidate(); }
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.Clear(fillScreen ? Theme.Screen : Theme.Face);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(fillScreen ? Theme.Screen : Theme.Face);
            ThemedForm.DrawBevel(e.Graphics, new Rectangle(0, 0, Width, Height), true);
            using (var dot = new SolidBrush(ledColor))
                e.Graphics.FillEllipse(dot, 7, Height / 2 - 3, 6, 6);
            TextRenderer.DrawText(e.Graphics, Text, Font, new Rectangle(19, 1, Math.Max(0, Width - 22), Height - 2),
                textColor, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPrefix |
                TextFormatFlags.EndEllipsis);
        }
    }
}
