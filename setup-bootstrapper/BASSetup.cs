using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

namespace BASSetup
{
    internal enum SetupPage { Splash, Welcome, Options, Installing, Finished, Error }

    internal sealed class DriveChoice
    {
        public string Letter;
        public string Label;

        public override string ToString() { return Label; }
    }

    internal sealed class SplashSurface : Panel
    {
        private double phase;

        public SplashSurface()
        {
            BackColor = Color.Black;
            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
        }

        public void Advance()
        {
            phase += 0.018;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.Clear(Color.Black);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            float scale = Math.Min(Width / 1280f, Height / 720f);
            float logoSize = Math.Max(50f, 82f * scale);
            using (Font logoFont = new Font("Segoe UI Semibold", logoSize, FontStyle.Regular, GraphicsUnit.Pixel))
            using (Font subtitleFont = new Font("Segoe UI", Math.Max(13f, 18f * scale), FontStyle.Regular, GraphicsUnit.Pixel))
            using (Brush white = new SolidBrush(Color.White))
            using (Brush muted = new SolidBrush(Color.FromArgb(205, 205, 205)))
            using (Pen outline = new Pen(Color.White, Math.Max(1f, scale)))
            {
                string logo = "BAS";
                SizeF logoBounds = g.MeasureString(logo, logoFont);
                float logoY = Height * 0.405f;
                g.DrawString(logo, logoFont, white, (Width - logoBounds.Width) / 2f, logoY);

                string subtitle = "BOOT ANIMATION SYSTEM";
                SizeF subtitleBounds = g.MeasureString(subtitle, subtitleFont);
                g.DrawString(subtitle, subtitleFont, muted, (Width - subtitleBounds.Width) / 2f, logoY + logoBounds.Height + 4f * scale);

                float barWidth = Width * 0.65f;
                float barHeight = Math.Max(12f, 15f * scale);
                float barX = (Width - barWidth) / 2f;
                float barY = Height * 0.82f;
                g.DrawRectangle(outline, barX, barY, barWidth, barHeight);

                float fill = 0.51f + 0.43f * (float)Math.Sin(phase);
                float innerWidth = Math.Max(0f, (barWidth - 4f) * fill);
                if (innerWidth > 1f)
                    g.FillRectangle(white, barX + 2f, barY + 2f, innerWidth, Math.Max(1f, barHeight - 4f));
            }
        }
    }

    internal sealed class SetupForm : Form
    {
        private readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
        private readonly SplashSurface splash = new SplashSurface();
        private readonly Panel prompt = new Panel();
        private readonly Label heading = new Label();
        private readonly Label description = new Label();
        private readonly CheckBox installSample = new CheckBox();
        private readonly ComboBox fat32Drives = new ComboBox();
        private readonly CheckBox desktopShortcut = new CheckBox();
        private readonly FlowLayoutPanel buttons = new FlowLayoutPanel();
        private readonly Button backButton = new Button();
        private readonly Button cancelButton = new Button();
        private readonly Button guideButton = new Button();
        private readonly Button primaryButton = new Button();
        private SetupPage page = SetupPage.Splash;
        private Action pendingPageChange;
        private Action afterSplash;
        private int fadeDirection;
        private int introTicks;
        private bool installing;
        private bool requestedSample;
        private bool requestedDesktopShortcut;
        private string requestedDriveLetter;
        private string logPath;
        private string tempDirectory;

        public SetupForm()
        {
            Text = "BAS Setup";
            BackColor = Color.Black;
            ForeColor = Color.White;
            Font = new Font("Segoe UI", 12f, FontStyle.Regular, GraphicsUnit.Point);
            FormBorderStyle = FormBorderStyle.None;
            WindowState = FormWindowState.Maximized;
            StartPosition = FormStartPosition.CenterScreen;
            KeyPreview = true;

            splash.Dock = DockStyle.Fill;
            Controls.Add(splash);

            prompt.Dock = DockStyle.Fill;
            prompt.BackColor = Color.Black;
            prompt.Visible = false;
            Controls.Add(prompt);

            heading.ForeColor = Color.White;
            heading.Font = new Font("Segoe UI Semibold", 32f, FontStyle.Regular, GraphicsUnit.Point);
            heading.TextAlign = ContentAlignment.MiddleCenter;
            heading.AutoEllipsis = true;
            prompt.Controls.Add(heading);

            description.ForeColor = Color.FromArgb(220, 220, 220);
            description.Font = new Font("Segoe UI", 17f, FontStyle.Regular, GraphicsUnit.Point);
            description.TextAlign = ContentAlignment.MiddleCenter;
            description.AutoEllipsis = true;
            prompt.Controls.Add(description);

            installSample.Text = "Install the included sample animation";
            installSample.ForeColor = Color.White;
            installSample.BackColor = Color.Black;
            installSample.Font = new Font("Segoe UI", 15f, FontStyle.Regular, GraphicsUnit.Point);
            installSample.AutoSize = true;
            installSample.CheckedChanged += delegate { fat32Drives.Enabled = installSample.Checked && fat32Drives.Items.Count > 0; };
            prompt.Controls.Add(installSample);

            fat32Drives.DropDownStyle = ComboBoxStyle.DropDownList;
            fat32Drives.Font = new Font("Segoe UI", 14f, FontStyle.Regular, GraphicsUnit.Point);
            fat32Drives.Width = 500;
            prompt.Controls.Add(fat32Drives);
            LoadFat32Drives();

            desktopShortcut.Text = "Create a desktop shortcut";
            desktopShortcut.ForeColor = Color.White;
            desktopShortcut.BackColor = Color.Black;
            desktopShortcut.Font = new Font("Segoe UI", 15f, FontStyle.Regular, GraphicsUnit.Point);
            desktopShortcut.AutoSize = true;
            prompt.Controls.Add(desktopShortcut);

            buttons.FlowDirection = FlowDirection.LeftToRight;
            buttons.WrapContents = false;
            buttons.AutoSize = true;
            buttons.BackColor = Color.Black;
            prompt.Controls.Add(buttons);

            ConfigureButton(backButton, "Back", delegate { ShowSplashThen(ShowWelcome); });
            ConfigureButton(cancelButton, "Cancel", delegate { Close(); });
            ConfigureButton(guideButton, "Boot setup guide", OpenGuide);
            ConfigureButton(primaryButton, "Continue", PrimaryAction);
            buttons.Controls.Add(backButton);
            buttons.Controls.Add(primaryButton);
            buttons.Controls.Add(cancelButton);
            buttons.Controls.Add(guideButton);

            timer.Interval = 16;
            timer.Tick += TimerTick;
            timer.Start();
            Resize += delegate { LayoutPrompt(); };
            KeyDown += HandleKeyDown;
            LayoutPrompt();
        }

        private void LoadFat32Drives()
        {
            try
            {
                foreach (DriveInfo drive in DriveInfo.GetDrives())
                {
                    try
                    {
                        if (!drive.IsReady || !String.Equals(drive.DriveFormat, "FAT32", StringComparison.OrdinalIgnoreCase))
                            continue;
                        string root = drive.RootDirectory.FullName;
                        string name = String.IsNullOrWhiteSpace(drive.VolumeLabel) ? "FAT32 volume" : drive.VolumeLabel;
                        fat32Drives.Items.Add(new DriveChoice
                        {
                            Letter = root.Substring(0, 1).ToUpperInvariant(),
                            Label = root + "  —  " + name
                        });
                    }
                    catch { }
                }
            }
            catch { }

            if (fat32Drives.Items.Count > 0)
                fat32Drives.SelectedIndex = 0;
            else
            {
                installSample.Enabled = false;
                installSample.Text = "No FAT32 volume detected; add an animation later in BAS";
            }
            fat32Drives.Enabled = false;
        }

        private static void ConfigureButton(Button button, string text, EventHandler action)
        {
            button.Text = text;
            button.Width = 190;
            button.Height = 54;
            button.Margin = new Padding(12, 0, 12, 0);
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderColor = Color.White;
            button.FlatAppearance.BorderSize = 1;
            button.BackColor = Color.Black;
            button.ForeColor = Color.White;
            button.Font = new Font("Segoe UI Semibold", 14f, FontStyle.Regular, GraphicsUnit.Point);
            button.Click += action;
        }

        private void LayoutPrompt()
        {
            if (prompt == null || heading == null || buttons == null)
                return;
            int width = Math.Min((int)(ClientSize.Width * 0.78), 1160);
            int left = (ClientSize.Width - width) / 2;
            heading.SetBounds(left, (int)(ClientSize.Height * 0.24), width, 76);
            description.SetBounds(left + 20, (int)(ClientSize.Height * 0.34), width - 40, 142);
            installSample.SetBounds(left + 28, (int)(ClientSize.Height * 0.53), width - 56, 40);
            fat32Drives.SetBounds(left + 56, (int)(ClientSize.Height * 0.60), Math.Min(width - 112, 700), 42);
            desktopShortcut.SetBounds(left + 28, (int)(ClientSize.Height * 0.68), width - 56, 40);
            buttons.Location = new Point((ClientSize.Width - buttons.Width) / 2, (int)(ClientSize.Height * 0.84));
        }

        private void TimerTick(object sender, EventArgs e)
        {
            splash.Advance();
            if (fadeDirection < 0)
            {
                Opacity = Math.Max(0.0, Opacity - 0.12);
                if (Opacity <= 0.001)
                {
                    if (pendingPageChange != null)
                        pendingPageChange();
                    pendingPageChange = null;
                    fadeDirection = 1;
                }
                return;
            }
            if (fadeDirection > 0)
            {
                Opacity = Math.Min(1.0, Opacity + 0.12);
                if (Opacity >= 0.999)
                {
                    Opacity = 1.0;
                    fadeDirection = 0;
                    if (page == SetupPage.Installing)
                        BeginInstall();
                }
                return;
            }

            if (page == SetupPage.Splash)
            {
                introTicks++;
                if (afterSplash != null && introTicks >= 50)
                {
                    Action next = afterSplash;
                    afterSplash = null;
                    TransitionTo(next);
                }
                else if (afterSplash == null && introTicks >= 95)
                    TransitionTo(ShowWelcome);
            }
        }

        private void TransitionTo(Action changePage)
        {
            if (fadeDirection != 0)
                return;
            pendingPageChange = changePage;
            fadeDirection = -1;
        }

        private void ShowSplashThen(Action nextPage)
        {
            afterSplash = nextPage;
            introTicks = 0;
            TransitionTo(delegate { SetPage(SetupPage.Splash); });
        }

        private void SetPage(SetupPage newPage)
        {
            page = newPage;
            bool showSplash = newPage == SetupPage.Splash || newPage == SetupPage.Installing;
            splash.Visible = showSplash;
            prompt.Visible = !showSplash;
            if (showSplash)
                return;

            backButton.Visible = newPage == SetupPage.Options;
            cancelButton.Visible = newPage == SetupPage.Welcome || newPage == SetupPage.Options;
            guideButton.Visible = newPage == SetupPage.Finished || newPage == SetupPage.Error;
            installSample.Visible = newPage == SetupPage.Options;
            fat32Drives.Visible = newPage == SetupPage.Options;
            desktopShortcut.Visible = newPage == SetupPage.Options;
            LayoutPrompt();
        }

        private void ShowWelcome()
        {
            SetPage(SetupPage.Welcome);
            heading.Text = "UNIVERSAL BAS SETUP";
            description.Text = "Install the BAS animation editor and its UEFI loader. Setup locates the active EFI System Partition and copies the loader there. Existing firmware boot order is left unchanged.";
            primaryButton.Text = "Continue";
            primaryButton.Visible = true;
            guideButton.Visible = false;
            backButton.Visible = false;
            cancelButton.Visible = true;
        }

        private void ShowOptions()
        {
            SetPage(SetupPage.Options);
            heading.Text = "SETUP OPTIONS";
            description.Text = "The included animation is optional. BAS will be installed under Program Files, and the EFI player will be copied to the system EFI partition.";
            primaryButton.Text = "Install";
            primaryButton.Visible = true;
            installSample.Checked = false;
            fat32Drives.Enabled = false;
            LayoutPrompt();
        }

        private void ShowFinished()
        {
            SetPage(SetupPage.Finished);
            heading.Text = "SETUP COMPLETE";
            description.Text = "BAS and its EFI loader are installed. Firmware boot order was not changed. Use your firmware's Add Boot Option screen to add \\EFI\\BAS\\BASX64.EFI as a separate entry, then select it manually for the first test.";
            primaryButton.Text = "Finish";
            primaryButton.Visible = true;
            guideButton.Text = "Boot setup guide";
            guideButton.Visible = true;
            cancelButton.Visible = false;
            backButton.Visible = false;
        }

        private void ShowError(string message)
        {
            SetPage(SetupPage.Error);
            heading.Text = "SETUP NEEDS ATTENTION";
            description.Text = message;
            primaryButton.Text = "Close";
            primaryButton.Visible = true;
            guideButton.Text = "Open setup log";
            guideButton.Visible = true;
            cancelButton.Visible = false;
            backButton.Visible = false;
        }

        private void PrimaryAction(object sender, EventArgs e)
        {
            if (page == SetupPage.Welcome)
                ShowSplashThen(ShowOptions);
            else if (page == SetupPage.Options)
            {
                if (installSample.Checked && !(fat32Drives.SelectedItem is DriveChoice))
                {
                    MessageBox.Show(this, "Choose a detected FAT32 volume or uncheck sample animation setup.", "Choose animation storage", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                requestedSample = installSample.Checked;
                requestedDesktopShortcut = desktopShortcut.Checked;
                requestedDriveLetter = requestedSample ? ((DriveChoice)fat32Drives.SelectedItem).Letter : null;
                TransitionTo(delegate { SetPage(SetupPage.Installing); });
            }
            else
                Close();
        }

        private void BeginInstall()
        {
            if (installing)
                return;
            installing = true;
            Thread worker = new Thread(InstallWorker);
            worker.IsBackground = true;
            worker.Start();
        }

        private void InstallWorker()
        {
            int exitCode = -1;
            string error = null;
            try
            {
                tempDirectory = Path.Combine(Path.GetTempPath(), "BASSetup-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(tempDirectory);
                string payload = Path.Combine(tempDirectory, "Universal-BAS-Setup-Payload.exe");
                using (Stream resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("BASSetup.Payload.exe"))
                {
                    if (resource == null)
                        throw new InvalidOperationException("The installation payload is missing from this setup file.");
                    using (FileStream output = new FileStream(payload, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                        resource.CopyTo(output);
                }

                string dataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Universal BAS");
                Directory.CreateDirectory(dataDirectory);
                logPath = Path.Combine(dataDirectory, "Setup.log");
                string arguments = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP- /LOG=" + QuoteArgument(logPath);
                if (requestedDesktopShortcut)
                    arguments += " /TASKS=desktopicon";
                if (requestedSample && !String.IsNullOrEmpty(requestedDriveLetter))
                    arguments += " /ANIMATIONDRIVE=" + requestedDriveLetter;

                ProcessStartInfo start = new ProcessStartInfo();
                start.FileName = payload;
                start.Arguments = arguments;
                start.UseShellExecute = false;
                start.CreateNoWindow = true;
                using (Process child = Process.Start(start))
                {
                    if (child == null)
                        throw new InvalidOperationException("Windows could not start the BAS installation payload.");
                    child.WaitForExit();
                    exitCode = child.ExitCode;
                }
            }
            catch (Exception ex)
            {
                error = ex.Message;
            }

            try { if (!String.IsNullOrEmpty(tempDirectory) && Directory.Exists(tempDirectory)) Directory.Delete(tempDirectory, true); }
            catch { }

            try
            {
                BeginInvoke((MethodInvoker)delegate
                {
                    installing = false;
                    if (error == null && exitCode == 0)
                        TransitionTo(ShowFinished);
                    else
                    {
                        string reason = error ?? ("The installer stopped with exit code " + exitCode + ".");
                        if (!String.IsNullOrEmpty(logPath))
                            reason += "\r\n\r\nSetup log: " + logPath;
                        TransitionTo(delegate { ShowError(reason); });
                    }
                });
            }
            catch (InvalidOperationException) { }
        }

        private static string QuoteArgument(string value)
        {
            return "\"" + value.Replace("\"", "\\\"") + "\"";
        }

        private void OpenGuide(object sender, EventArgs e)
        {
            if (page == SetupPage.Error)
            {
                if (!String.IsNullOrEmpty(logPath) && File.Exists(logPath))
                    Process.Start("notepad.exe", QuoteArgument(logPath));
                return;
            }
            string guide = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Universal BAS", "BOOT-SETUP.md");
            if (File.Exists(guide))
                Process.Start("notepad.exe", QuoteArgument(guide));
        }

        private void HandleKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape && !installing)
            {
                Close();
                e.Handled = true;
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (installing)
            {
                e.Cancel = true;
                return;
            }
            timer.Stop();
            base.OnFormClosing(e);
        }
    }

    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new SetupForm());
        }
    }
}
