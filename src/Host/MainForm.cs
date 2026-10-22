using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace PclPatchHost
{
    internal sealed class MainForm : ThemedForm
    {
        private readonly string initialTarget;
        private readonly ChiptunePlayer music;
        private string target;
        private TargetInfo current;
        private Image avatarImage;
        private ToolTip toolTip;
        private Label pathValue;
        private Label typeValue;
        private Label versionValue;
        private RetroStatus status;
        private LogView logView;
        private RetroButton primary;
        private RetroButton browse;
        private RetroButton about;
        private RetroButton closeButton;
        private RetroButton soundButton;
        private RetroCheckBox backup;

        public MainForm(string initialTarget)
        {
            this.initialTarget = initialTarget;
            music = new ChiptunePlayer();
            Text = "LDF.EXE // LAUNCHER DOWNLOAD FIX";
            ClientSize = new Size(480, 340);
            StartPosition = FormStartPosition.CenterScreen;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = true;
            Icon = Theme.LoadAppIcon();
            toolTip = new ToolTip();
            toolTip.AutoPopDelay = 6000;
            toolTip.InitialDelay = 400;
            toolTip.ReshowDelay = 100;

            BuildTitleBar();
            BuildTargetPanel();
            BuildLogPanel();
            BuildFooter();

            WindowChrome.AttachDrag(this, this);
            FormClosing += delegate { music.Stop(); };
            Shown += delegate
            {
                if (!String.IsNullOrWhiteSpace(this.initialTarget) && File.Exists(this.initialTarget))
                {
                    target = this.initialTarget;
                    ProbeTarget();
                    if (primary.Enabled) primary.Focus();
                    else browse.Focus();
                }
                else
                {
                    UpdateInfo();
                    AddLog("AWAITING TARGET...", Theme.GreenDim);
                    browse.Focus();
                }
                BeginInvoke((Action)StartMusic);
            };
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                music.Dispose();
                if (avatarImage != null)
                {
                    avatarImage.Dispose();
                    avatarImage = null;
                }
                if (toolTip != null)
                {
                    toolTip.Dispose();
                    toolTip = null;
                }
            }
            base.Dispose(disposing);
        }

        private void BuildTitleBar()
        {
            var bar = new RetroPanel();
            bar.Location = new Point(4, 4);
            bar.Size = new Size(472, 28);
            bar.FillColor = Theme.Navy;
            bar.Sunken = false;
            Controls.Add(bar);

            avatarImage = Theme.LoadAvatar();
            var icon = new PictureBox();
            icon.Location = new Point(5, 4);
            icon.Size = new Size(20, 20);
            icon.SizeMode = PictureBoxSizeMode.Zoom;
            icon.BackColor = Theme.Navy;
            icon.Image = avatarImage;
            bar.Controls.Add(icon);

            var title = MakeLabel("LDF.EXE  //  DOWNLOAD FIX", Theme.Mono(9f, FontStyle.Bold),
                Color.White, 32, 5, 300, 18, Theme.Navy);
            bar.Controls.Add(title);

            var version = MakeLabel("v2.0.0", Theme.Mono(8f, FontStyle.Bold), Theme.Green,
                386, 6, 48, 16, Theme.Navy);
            version.TextAlign = ContentAlignment.MiddleRight;
            bar.Controls.Add(version);

            closeButton = new RetroButton();
            closeButton.Text = "X";
            closeButton.Location = new Point(442, 5);
            closeButton.Size = new Size(28, 20);
            closeButton.Font = Theme.Mono(9f, FontStyle.Bold);
            closeButton.TabIndex = 5;
            closeButton.Click += delegate { Close(); };
            bar.Controls.Add(closeButton);

            WindowChrome.AttachDrag(this, bar);
            WindowChrome.AttachDrag(this, icon);
            WindowChrome.AttachDrag(this, title);
            WindowChrome.AttachDrag(this, version);
        }

        private void BuildTargetPanel()
        {
            var panel = new RetroPanel();
            panel.Location = new Point(10, 38);
            panel.Size = new Size(460, 100);
            panel.FillColor = Theme.Face;
            Controls.Add(panel);

            panel.Controls.Add(MakeLabel("TARGET", Theme.Mono(8.2f, FontStyle.Bold), Theme.Text,
                12, 5, 100, 16, Theme.Face));

            var pathPanel = new RetroPanel();
            pathPanel.Location = new Point(12, 23);
            pathPanel.Size = new Size(344, 24);
            pathPanel.FillColor = Theme.Screen;
            pathPanel.Sunken = true;
            panel.Controls.Add(pathPanel);

            pathValue = MakeLabel("NO TARGET SELECTED", Theme.Mono(8.4f), Theme.GreenDim,
                5, 2, 334, 18, Theme.Screen);
            pathValue.AutoEllipsis = true;
            pathValue.Cursor = Cursors.Hand;
            pathValue.DoubleClick += delegate { SelectTarget(); };
            pathPanel.Controls.Add(pathValue);

            browse = new RetroButton();
            browse.Text = "BROWSE";
            browse.Location = new Point(362, 23);
            browse.Size = new Size(88, 24);
            browse.Font = Theme.Mono(8f, FontStyle.Bold);
            browse.TabIndex = 0;
            browse.Click += delegate { SelectTarget(); };
            panel.Controls.Add(browse);

            panel.Controls.Add(MakeLabel("TYPE", Theme.Mono(7.8f, FontStyle.Bold), Theme.Muted, 12, 58, 34, 15, Theme.Face));
            typeValue = MakeLabel("--", Theme.Mono(8.5f, FontStyle.Bold), Theme.Text, 48, 56, 68, 18, Theme.Face);
            panel.Controls.Add(typeValue);

            panel.Controls.Add(MakeLabel("VERSION", Theme.Mono(7.8f, FontStyle.Bold), Theme.Muted, 124, 58, 48, 15, Theme.Face));
            versionValue = MakeLabel("--", Theme.Mono(8.5f), Theme.Text, 174, 56, 120, 18, Theme.Face);
            versionValue.AutoEllipsis = true;
            panel.Controls.Add(versionValue);

            panel.Controls.Add(MakeLabel("STATE", Theme.Mono(7.8f, FontStyle.Bold), Theme.Muted, 300, 58, 40, 15, Theme.Face));
            status = new RetroStatus();
            status.Location = new Point(344, 54);
            status.Size = new Size(106, 24);
            panel.Controls.Add(status);
        }

        private void BuildLogPanel()
        {
            var panel = new RetroPanel();
            panel.Location = new Point(10, 146);
            panel.Size = new Size(460, 120);
            panel.FillColor = Theme.Screen;
            panel.Sunken = true;
            Controls.Add(panel);

            panel.Controls.Add(MakeLabel("RUN LOG", Theme.Mono(8.8f, FontStyle.Bold), Theme.Green,
                8, 6, 120, 17, Theme.Screen));

            logView = new LogView();
            logView.Location = new Point(8, 25);
            logView.Size = new Size(444, 84);
            logView.BackColor = Theme.LogBackground;
            panel.Controls.Add(logView);
        }

        private void BuildFooter()
        {
            backup = new RetroCheckBox();
            backup.Text = "AUTO BACKUP";
            backup.Checked = true;
            backup.Location = new Point(12, 278);
            backup.Size = new Size(130, 22);
            backup.TabIndex = 1;
            backup.CheckedChanged += delegate
            {
                AddLog(backup.Checked
                    ? "AUTO BACKUP ENABLED."
                    : "AUTO BACKUP DISABLED. NO RESTORE POINT.",
                    backup.Checked ? Theme.GreenDim : Theme.Red);
            };
            Controls.Add(backup);

            soundButton = new RetroButton();
            soundButton.Text = "SOUND ON";
            soundButton.Location = new Point(148, 274);
            soundButton.Size = new Size(96, 30);
            soundButton.Font = Theme.Mono(8f, FontStyle.Bold);
            soundButton.TabIndex = 2;
            soundButton.Click += delegate { ToggleSound(); };
            Controls.Add(soundButton);

            primary = new RetroButton();
            primary.Text = "APPLY";
            primary.Location = new Point(250, 274);
            primary.Size = new Size(112, 30);
            primary.Font = Theme.Mono(8.5f, FontStyle.Bold);
            primary.Accent = true;
            primary.TabIndex = 3;
            primary.Enabled = false;
            primary.Click += delegate { RunPrimary(); };
            Controls.Add(primary);

            about = new RetroButton();
            about.Text = "ABOUT";
            about.Location = new Point(380, 274);
            about.Size = new Size(86, 30);
            about.Font = Theme.Mono(8f, FontStyle.Bold);
            about.TabIndex = 4;
            about.Click += delegate { ShowAbout(); };
            Controls.Add(about);
        }

        private static Label MakeLabel(string text, Font font, Color color, int x, int y,
            int width, int height, Color background)
        {
            var label = new Label();
            label.Text = text;
            label.Font = font;
            label.ForeColor = color;
            label.BackColor = background;
            label.Location = new Point(x, y);
            label.Size = new Size(width, height);
            label.UseMnemonic = false;
            label.TabStop = false;
            return label;
        }

        private void SelectTarget()
        {
            var dialog = new OpenFileDialog();
            dialog.Title = "SELECT PCL2 / PCL2 CE / HMCL";
            dialog.Filter = "Launchers (*.exe;*.jar;*.sh)|*.exe;*.jar;*.sh|All files (*.*)|*.*";
            dialog.CheckFileExists = true;
            if (!String.IsNullOrWhiteSpace(target)) dialog.InitialDirectory = Path.GetDirectoryName(target);
            try
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                target = dialog.FileName;
                AddLog("TARGET: " + target, Theme.Cyan);
                ProbeTarget();
            }
            finally
            {
                dialog.Dispose();
            }
        }

        private void ProbeTarget()
        {
            if (String.IsNullOrWhiteSpace(target)) return;
            Cursor = Cursors.WaitCursor;
            try
            {
                current = TargetProbe.Probe(target);
                AddLog("SCAN: " + current.ProfileLabel.ToUpperInvariant(), Theme.Cyan);
                AddLog("STATE: " + current.StateLabel.ToUpperInvariant() +
                    (String.IsNullOrWhiteSpace(current.Error) ? "" : " / " + current.Error),
                    current.Supported || current.NoGate ? Theme.Green : Theme.Red);
                if (current.Patched && !HasRestorableBackup(target))
                    AddLog("NO RESTORE POINT.", Theme.Red);
                UpdateInfo();
            }
            catch (Exception ex)
            {
                current = null;
                AddLog("SCAN FAILED: " + ex.Message, Theme.Red);
                UpdateInfo();
            }
            finally
            {
                Cursor = Cursors.Default;
            }
        }

        private void RunPrimary()
        {
            if (String.IsNullOrWhiteSpace(target) || current == null || !File.Exists(target))
            {
                SelectTarget();
                return;
            }
            if (current.NoGate)
            {
                AddLog("NO GATE DETECTED.", Theme.Amber);
                return;
            }
            if (!current.Supported)
            {
                AddLog("UNSUPPORTED STRUCTURE.", Theme.Red);
                return;
            }

            bool restoring = current.Patched;
            if (!restoring && !backup.Checked)
            {
                var confirm = MessageBox.Show(this,
                    "AUTO BACKUP IS DISABLED.\r\n\r\nNo restore point will be created. Continue?",
                    "NO BACKUP",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning,
                    MessageBoxDefaultButton.Button2);
                if (confirm != DialogResult.Yes)
                {
                    AddLog("CANCELLED.", Theme.Amber);
                    return;
                }
            }

            var options = new PatchOptions();
            options.Input = target;
            options.Yes = true;
            options.Quiet = true;
            options.NoBackup = !backup.Checked;
            Cursor = Cursors.WaitCursor;
            primary.Enabled = false;
            try
            {
                int code;
                if (restoring)
                {
                    AddLog("RESTORING ORIGINAL...", Theme.Cyan);
                    options.Restore = true;
                    code = Operations.Restore(options);
                }
                else
                {
                    AddLog("APPLYING PATCH...", Theme.Cyan);
                    code = Operations.Apply(options);
                }
                if (code == 0)
                {
                    AddLog(restoring ? "RESTORE COMPLETE." : "PATCH COMPLETE.",
                        Theme.Green);
                    if (!restoring && !backup.Checked)
                        AddLog("NO BACKUP CREATED. RESTORE UNAVAILABLE.", Theme.Red);
                }
                ProbeTarget();
            }
            catch (Exception ex)
            {
                AddLog("FAILED: " + ex.Message, Theme.Red);
                MessageBox.Show(this, ex.Message, "FAILED", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                Cursor = Cursors.Default;
                UpdateInfo();
            }
        }

        private void UpdateInfo()
        {
            if (String.IsNullOrWhiteSpace(target))
            {
                pathValue.Text = "NO TARGET SELECTED";
                pathValue.ForeColor = Theme.GreenDim;
                typeValue.Text = "--";
                versionValue.Text = "--";
                browse.Text = "BROWSE";
                SetStatus("NO FILE", Theme.Disabled, Theme.Disabled);
                primary.Text = "APPLY";
                primary.Enabled = false;
                toolTip.SetToolTip(pathValue, "");
                toolTip.SetToolTip(primary, "");
                return;
            }

            pathValue.Text = target;
            pathValue.ForeColor = Theme.Green;
            toolTip.SetToolTip(pathValue, target);
            browse.Text = "RE-BROWSE";
            typeValue.Text = current == null ? "UNKNOWN" : current.ProfileLabel.ToUpperInvariant();
            versionValue.Text = current == null || String.IsNullOrWhiteSpace(current.Version)
                ? "--" : FormatVersion(current.Version);
            toolTip.SetToolTip(versionValue, current == null || String.IsNullOrWhiteSpace(current.Version)
                ? "" : current.Version);

            bool hasBackup = current != null && current.Patched && HasRestorableBackup(target);

            if (current == null)
            {
                SetStatus("UNKNOWN", Theme.Red, Theme.Red);
            }
            else if (current.NoGate)
            {
                SetStatus("NO GATE", Theme.Amber, Theme.Amber);
            }
            else if (current.Patched)
            {
                if (hasBackup) SetStatus("PATCHED", Theme.Green, Theme.Green);
                else SetStatus("NO BACKUP", Theme.Red, Theme.Red);
            }
            else if (!current.Supported)
            {
                SetStatus("UNSUPPORTED", Theme.Red, Theme.Red);
            }
            else
            {
                SetStatus("ORIGINAL", Theme.Green, Theme.Green);
            }

            bool canAct = current != null && current.Supported && (current.Patched || !current.NoGate);
            if (current != null && current.Patched && !hasBackup)
            {
                primary.Enabled = false;
                primary.Text = "NO BACKUP";
                toolTip.SetToolTip(primary, "No usable backup; restore is unavailable.");
            }
            else
            {
                primary.Enabled = canAct;
                primary.Text = current != null && current.Patched ? "RESTORE" : "APPLY";
                toolTip.SetToolTip(primary, "");
            }
        }

        private static bool HasRestorableBackup(string path)
        {
            foreach (string candidate in BackupManager.Candidates(path))
            {
                try
                {
                    var info = TargetProbe.Probe(candidate);
                    if (info.IsKnown && !info.Patched && info.State != "unsupported")
                        return true;
                }
                catch { }
            }
            return false;
        }

        private static string FormatVersion(string version)
        {
            if (String.IsNullOrWhiteSpace(version) || version.IndexOf(" / ", StringComparison.Ordinal) < 0)
                return version;
            string[] parts = version.Split(new[] { " / " }, StringSplitOptions.None);
            for (int i = 0; i < parts.Length; i++)
            {
                while (parts[i].Length > 3 && parts[i].EndsWith(".0", StringComparison.Ordinal))
                    parts[i] = parts[i].Substring(0, parts[i].Length - 2);
            }
            return String.Join(" / ", parts);
        }

        private void SetStatus(string text, Color led, Color color)
        {
            status.Text = text;
            status.LedColor = led;
            status.TextColor = color;
        }

        private void StartMusic()
        {
            if (IsDisposed || Disposing || music.IsPlaying) return;
            bool playing = music.Play();
            soundButton.Text = playing ? "SOUND ON" : "SOUND OFF";
            AddLog(playing ? "CHIPTUNE: ON." : "CHIPTUNE: UNAVAILABLE.",
                playing ? Theme.Amber : Theme.Red);
        }

        private void ToggleSound()
        {
            bool playing = music.Toggle();
            soundButton.Text = playing ? "SOUND ON" : "SOUND OFF";
            if (playing) AddLog("CHIPTUNE: ON.", Theme.Amber);
            else AddLog("CHIPTUNE: OFF.", Theme.GreenDim);
        }

        private void AddLog(string text, Color color)
        {
            if (logView != null) logView.Add(text, color);
        }

        private void ShowAbout()
        {
            using (var dialog = new AboutDialog())
                dialog.ShowDialog(this);
        }
    }
}
