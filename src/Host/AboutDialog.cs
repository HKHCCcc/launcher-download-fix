using System;
using System.Drawing;
using System.Windows.Forms;

namespace PclPatchHost
{
    internal sealed class AboutDialog : ThemedForm
    {
        private Image avatarImage;

        public AboutDialog()
        {
            Text = "ABOUT // LDF.EXE";
            ClientSize = new Size(420, 220);
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            MaximizeBox = false;
            MinimizeBox = false;
            Icon = Theme.LoadAppIcon();

            var bar = new RetroPanel();
            bar.Location = new Point(4, 4);
            bar.Size = new Size(412, 26);
            bar.FillColor = Theme.Navy;
            Controls.Add(bar);

            avatarImage = Theme.LoadAvatar();
            var icon = new PictureBox();
            icon.Location = new Point(5, 3);
            icon.Size = new Size(18, 18);
            icon.SizeMode = PictureBoxSizeMode.Zoom;
            icon.BackColor = Theme.Navy;
            icon.Image = avatarImage;
            bar.Controls.Add(icon);

            var title = new Label();
            title.Text = "ABOUT // LAUNCHER DOWNLOAD FIX";
            title.Font = Theme.Mono(8.3f, FontStyle.Bold);
            title.ForeColor = Color.White;
            title.BackColor = Theme.Navy;
            title.Location = new Point(30, 4);
            title.Size = new Size(330, 18);
            bar.Controls.Add(title);

            var closeTop = new RetroButton();
            closeTop.Text = "X";
            closeTop.Location = new Point(382, 3);
            closeTop.Size = new Size(26, 19);
            closeTop.Font = Theme.Mono(9f, FontStyle.Bold);
            closeTop.Click += delegate { Close(); };
            bar.Controls.Add(closeTop);

            var screen = new RetroPanel();
            screen.Location = new Point(10, 38);
            screen.Size = new Size(400, 132);
            screen.FillColor = Theme.Screen;
            screen.Sunken = true;
            Controls.Add(screen);

            AddScreenLabel(screen, "LAUNCHER DOWNLOAD FIX", Theme.Mono(12f, FontStyle.Bold),
                Theme.Green, 14, 10, 370, 22);
            AddScreenLabel(screen, "PCL2 / PCL2 CE / HMCL", Theme.Mono(8f),
                Theme.GreenDim, 14, 33, 370, 17);
            AddScreenLabel(screen, "VERSION 2.0", Theme.Mono(8f, FontStyle.Bold),
                Theme.Amber, 14, 56, 370, 17);
            AddScreenLabel(screen, "LOCAL PATCH UTILITY\r\nNO WARRANTY / USE A COPY",
                Theme.Mono(8f), Theme.LogText, 14, 82, 370, 36);

            var note = new Label();
            note.Text = "非官方工具，仅用于个人研究和兼容性维护。";
            note.Font = Theme.Ui(7.8f);
            note.ForeColor = Theme.Muted;
            note.BackColor = Theme.Face;
            note.Location = new Point(12, 180);
            note.Size = new Size(396, 22);
            Controls.Add(note);

            WindowChrome.AttachDrag(this, bar);
            WindowChrome.AttachDrag(this, icon);
            WindowChrome.AttachDrag(this, title);
        }

        private static void AddScreenLabel(Control parent, string text, Font font, Color color,
            int x, int y, int width, int height)
        {
            var label = new Label();
            label.Text = text;
            label.Font = font;
            label.ForeColor = color;
            label.BackColor = Theme.Screen;
            label.Location = new Point(x, y);
            label.Size = new Size(width, height);
            label.UseMnemonic = false;
            parent.Controls.Add(label);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && avatarImage != null)
            {
                avatarImage.Dispose();
                avatarImage = null;
            }
            base.Dispose(disposing);
        }
    }
}
