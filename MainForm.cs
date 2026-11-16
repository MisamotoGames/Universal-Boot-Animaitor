using System.Drawing.Imaging;
using System.Text.Json;

namespace BasStudio;

public sealed class MainForm : Form
{
    readonly List<string> files = [];
    readonly PictureBox preview = new() { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.FromArgb(12, 15, 22) };
    readonly Label status = new() { AutoSize = true, ForeColor = Color.Gainsboro, Padding = new Padding(8) };
    readonly NumericUpDown fps = new() { Minimum = 1, Maximum = 60, Value = 40, Width = 90 };
    readonly NumericUpDown quality = new() { Minimum = 50, Maximum = 95, Value = 85, Width = 75 };
    readonly ComboBox outputSize = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 190 };
    readonly System.Windows.Forms.Timer timer = new();
    readonly TrackBar scrub = new() { Minimum = 0, Maximum = 0, TickStyle = TickStyle.None, Dock = DockStyle.Fill };
    int frame;
    string? sourceFolder;

    public MainForm()
    {
        Text = "Universal BAS"; MinimumSize = new Size(850, 580); Size = new Size(1100, 720);
        BackColor = Color.FromArgb(25, 29, 38); ForeColor = Color.White; Font = new Font("Segoe UI", 10);
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(16) };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 90)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 110));
        var header = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = true, AutoScroll = true };
        var title = new Label { Text = "BAS · BOOT ANIMATION SYSTEM", AutoSize = true, Font = new Font("Consolas", 16, FontStyle.Bold), ForeColor = Color.FromArgb(220, 220, 220), Padding = new Padding(0, 7, 25, 0) };
        outputSize.Items.AddRange(["1280 × 720 · faster", "1920 × 1080", "Original frame size"]);
        outputSize.SelectedIndex = 0;
        header.Controls.Add(title); header.Controls.Add(Button("Open frames…", OpenFrames)); header.Controls.Add(Button("Open GIF…", OpenGif)); header.Controls.Add(Button("Open MP4…", OpenVideo)); header.Controls.Add(Button("Create FAT32 boot volume…", CreateVolume)); header.Controls.Add(Button("Export for UEFI…", Export)); header.Controls.Add(Button("Boot setup guide", ShowBootGuide));
        header.Controls.Add(new Label { Text = "Export size", AutoSize = true, Padding = new Padding(8, 9, 0, 0) }); header.Controls.Add(outputSize);
        root.Controls.Add(header, 0, 0); root.Controls.Add(preview, 0, 1);
        var bottom = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        bottom.RowStyles.Add(new RowStyle(SizeType.Percent, 60)); bottom.RowStyles.Add(new RowStyle(SizeType.Percent, 40));
        var controls = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Padding = new Padding(0, 6, 0, 0) };
        var play = Button("▶ / ❚❚", (_, _) => { timer.Enabled = !timer.Enabled; });
        controls.Controls.Add(play); controls.Controls.Add(new Label { Text = "FPS", AutoSize = true, Padding = new Padding(12, 8, 4, 0) }); controls.Controls.Add(fps);
        controls.Controls.Add(new Label { Text = "  Frame", AutoSize = true, Padding = new Padding(10, 8, 4, 0) }); controls.Controls.Add(scrub); controls.Controls.Add(status);
        controls.Controls.Add(new Label { Text = "JPEG quality", AutoSize = true, Padding = new Padding(10, 8, 4, 0) }); controls.Controls.Add(quality);
        controls.Controls.Add(Button("Move ←", (_, _) => MoveFrame(-1)));
        controls.Controls.Add(Button("Move →", (_, _) => MoveFrame(1)));
        controls.Controls.Add(Button("Remove frame", (_, _) => RemoveFrame()));
        bottom.Controls.Add(controls, 0, 0); bottom.Controls.Add(new Label { Text = "UEFI mode: compact JPEG frame package · GOP display · chainload Windows Boot Manager", ForeColor = Color.Silver, AutoSize = true, Padding = new Padding(2, 4, 0, 0) }, 0, 1);
        root.Controls.Add(bottom, 0, 2); Controls.Add(root);
        timer.Tick += (_, _) => ShowFrame((frame + 1) % Math.Max(files.Count, 1)); fps.ValueChanged += (_, _) => SetInterval(); SetInterval();
        scrub.Scroll += (_, _) => ShowFrame(scrub.Value);
        outputSize.SelectedIndexChanged += (_, _) => { if (files.Count > 0) ShowFrame(frame); };
    }

    Button Button(string text, EventHandler action) { var b = new Button { Text = text, AutoSize = true, Height = 36, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(46, 56, 72), ForeColor = Color.White, Margin = new Padding(5) }; b.Click += action; return b; }
    void CreateVolume(object? s, EventArgs e)
    {
        if (files.Count == 0) { MessageBox.Show(this, "Open an animation before sizing its boot volume.", "No animation loaded"); return; }
        int requiredGiB;
        try { requiredGiB = RequiredPartitionGiB(); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Cannot estimate animation size", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
        if (requiredGiB > 29) { MessageBox.Show(this, "This animation needs more than the 29 GiB FAT32 volume limit. Reduce the frame dimensions or duration before creating its boot volume.", "Animation too large"); return; }
        string script = Path.Combine(AppContext.BaseDirectory, "Create-BAS-Volume.ps1");
        if (!File.Exists(script)) script = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Create-BAS-Volume.ps1");
        script = Path.GetFullPath(script);
        if (!File.Exists(script)) { MessageBox.Show(this, "The volume helper is missing. Reinstall Universal BAS from its complete folder.", "Helper missing"); return; }
        var answer = MessageBox.Show(this,
            "This helper creates a new FAT32 partition only in already-unallocated space on a disk you select. It will not shrink or format existing partitions. The admin console will show the selected disk and require you to type a confirmation phrase.\n\nConnect the intended animation drive first. Continue?",
            "Create dedicated animation volume", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
        if (answer != DialogResult.Yes) return;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoExit -ExecutionPolicy Bypass -File \"{script}\" -RequiredGiB {requiredGiB}",
                UseShellExecute = true,
                Verb = "runas"
            });
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223) { }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Could not start volume setup"); }
    }
    void ShowBootGuide(object? s, EventArgs e)
    {
        MessageBox.Show(this,
            "Universal BAS installs its EFI loader as a separate firmware boot option and never replaces another boot manager. On Dell systems: restart, press F2, open Boot Configuration > Add Boot Option, name it Universal BAS, and select \\EFI\\BAS\\BASX64.EFI on the EFI System Partition. Save without changing Intel Unlock Manager's position. Use F12 to select BAS for a manual test. The firmware UI varies by model.\n\nThe animation data lives at BAS\\animation.bas on a FAT32 volume visible to UEFI. Secure Boot must be off unless you sign the loader with a key trusted by firmware.",
            "Boot setup guide", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }
    int RequiredPartitionGiB()
    {
        long bytes = AnimationPackage.EstimateBytes(files, (long)quality.Value, SelectedOutputSize()) + 64L * 1024 * 1024;
        return Math.Max(1, (int)Math.Ceiling(bytes / (double)(1024L * 1024 * 1024)));
    }
    void SetInterval() => timer.Interval = Math.Max(1, (1000 + (int)fps.Value - 1) / (int)fps.Value);
    void OpenFrames(object? s, EventArgs e)
    {
        using var d = new FolderBrowserDialog { Description = "Choose a folder with sequential image frames" };
        if (d.ShowDialog() != DialogResult.OK) return;
        sourceFolder = d.SelectedPath;
        files.Clear(); files.AddRange(Directory.GetFiles(sourceFolder).Where(IsImage).OrderBy(p => Path.GetFileNameWithoutExtension(p), new NaturalComparer()));
        LoadFrames();
        string metadataPath = Path.Combine(sourceFolder, "animation.json");
        if (files.Count > 0 && File.Exists(metadataPath))
        {
            try
            {
                using var metadata = JsonDocument.Parse(File.ReadAllText(metadataPath));
                var root = metadata.RootElement;
                if (root.TryGetProperty("fps", out var fpsValue) && fpsValue.TryGetInt32(out int sourceFps) && sourceFps is >= 1 and <= 60)
                {
                    if (root.TryGetProperty("frameCount", out var countValue) && countValue.TryGetInt32(out int sourceCount) && sourceCount != files.Count)
                        status.Text = $"{files.Count} frames · metadata says {sourceCount} · source FPS not applied";
                    else
                    {
                        fps.Value = sourceFps;
                        status.Text = $"{files.Count} frames · source FPS {sourceFps} loaded (editable)";
                    }
                }
            }
            catch (JsonException) { status.Text = $"{files.Count} frames · animation.json could not be read; using current FPS"; }
        }
    }
    void OpenGif(object? s, EventArgs e)
    {
        using var d = new OpenFileDialog { Filter = "Animated GIF|*.gif" }; if (d.ShowDialog() != DialogResult.OK) return;
        sourceFolder = null; files.Clear();
        using var gif = Image.FromFile(d.FileName); var dim = new FrameDimension(gif.FrameDimensionsList[0]);
        int count = gif.GetFrameCount(dim);
        for (int i = 0; i < count; i++) { gif.SelectActiveFrame(dim, i); string temp = Path.Combine(Path.GetTempPath(), $"bas-preview-{Guid.NewGuid():N}.png"); using var bmp = new Bitmap(gif); bmp.Save(temp, ImageFormat.Png); files.Add(temp); }
        LoadFrames();
    }
    async void OpenVideo(object? s, EventArgs e)
    {
        using var d = new OpenFileDialog { Filter = "Video files|*.mp4;*.mov;*.m4v;*.wmv;*.avi;*.mkv|All files|*.*" };
        if (d.ShowDialog() != DialogResult.OK) return;
        timer.Enabled = false;
        UseWaitCursor = true;
        status.Text = "Decoding video to firmware preview frames…";
        try
        {
            int importFps = (int)fps.Value;
            var progress = new Progress<int>(n => status.Text = $"Decoding video… {n} frames");
            var result = await Task.Run(() => MediaFoundationVideo.Decode(d.FileName, importFps, progress));
            sourceFolder = result.Folder;
            files.Clear(); files.AddRange(Directory.GetFiles(result.Folder, "*.jpg").OrderBy(x => x, StringComparer.Ordinal));
            LoadFrames();
            status.Text = $"{result.Frames} frames · {result.Width}×{result.Height} · imported at {importFps} FPS";
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Video import failed", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        finally { UseWaitCursor = false; }
    }
    static bool IsImage(string p) => new[] { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff" }.Contains(Path.GetExtension(p), StringComparer.OrdinalIgnoreCase);
    sealed class NaturalComparer : IComparer<string>
    {
        public int Compare(string? a, string? b)
        {
            a ??= ""; b ??= ""; int i = 0, j = 0;
            while (i < a.Length && j < b.Length)
            {
                if (char.IsDigit(a[i]) && char.IsDigit(b[j]))
                {
                    int x = i, y = j; while (x < a.Length && char.IsDigit(a[x])) x++; while (y < b.Length && char.IsDigit(b[y])) y++;
                    string nx = a[i..x].TrimStart('0'), ny = b[j..y].TrimStart('0');
                    int cmp = nx.Length.CompareTo(ny.Length); if (cmp == 0) cmp = string.CompareOrdinal(nx, ny); if (cmp != 0) return cmp;
                    i = x; j = y; continue;
                }
                int c = char.ToLowerInvariant(a[i]).CompareTo(char.ToLowerInvariant(b[j])); if (c != 0) return c; i++; j++;
            }
            return (a.Length - i).CompareTo(b.Length - j);
        }
    }
    void LoadFrames() { if (files.Count == 0) { status.Text = "No supported images found."; return; } scrub.Maximum = files.Count - 1; ShowFrame(0); timer.Enabled = true; }
    void MoveFrame(int delta)
    {
        int target = frame + delta;
        if (target < 0 || target >= files.Count) return;
        (files[frame], files[target]) = (files[target], files[frame]);
        ShowFrame(target);
    }
    void RemoveFrame()
    {
        if (files.Count <= 1) { status.Text = "Keep at least one frame in the timeline."; return; }
        timer.Enabled = false;
        files.RemoveAt(frame);
        scrub.Maximum = files.Count - 1;
        ShowFrame(Math.Min(frame, files.Count - 1));
    }
    void ShowFrame(int n)
    {
        if (files.Count == 0) return; frame = n; scrub.Value = Math.Clamp(n, scrub.Minimum, scrub.Maximum);
        var old = preview.Image;
        using var src = Image.FromFile(files[frame]);
        var canvasSize = SelectedOutputSize() ?? new Size(src.Width, src.Height);
        using var canvas = new Bitmap(canvasSize.Width, canvasSize.Height);
        using (var graphics = Graphics.FromImage(canvas))
        {
            graphics.Clear(Color.Black);
            graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
            float scale = Math.Min(canvas.Width / (float)src.Width, canvas.Height / (float)src.Height);
            int width = Math.Max(1, (int)Math.Round(src.Width * scale));
            int height = Math.Max(1, (int)Math.Round(src.Height * scale));
            graphics.DrawImage(src, new Rectangle((canvas.Width - width) / 2, (canvas.Height - height) / 2, width, height));
        }
        preview.Image = new Bitmap(canvas); old?.Dispose(); status.Text = $"{frame + 1} / {files.Count}";
    }
    void Export(object? s, EventArgs e)
    {
        if (files.Count == 0) { MessageBox.Show(this, "Open a frame folder or animated GIF first."); return; }
        if (files.Count > 600) { MessageBox.Show(this, $"The UEFI player supports at most 600 frames; this timeline has {files.Count}. Remove frames or lower the imported video FPS.", "Timeline too long", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
        using var d = new FolderBrowserDialog { Description = "Choose the root of the dedicated FAT32 animation volume" };
        if (d.ShowDialog() != DialogResult.OK) return;
        string dest = Path.Combine(d.SelectedPath, "BAS");
        string package = Path.Combine(dest, "animation.bas");
        try
        {
            var drive = new DriveInfo(Path.GetPathRoot(d.SelectedPath)!);
            if (!drive.IsReady || !string.Equals(drive.DriveFormat, "FAT32", StringComparison.OrdinalIgnoreCase))
                throw new IOException("UEFI animation storage must be on a FAT32 volume. Choose your BAS animation volume.");
            long required = AnimationPackage.EstimateBytes(files, (long)quality.Value, SelectedOutputSize());
            long existingPackage = File.Exists(package) ? new FileInfo(package).Length : 0;
            long requiredFreeSpace = checked(required + existingPackage + 1024L * 1024);
            if (drive.AvailableFreeSpace < requiredFreeSpace)
                throw new IOException($"Not enough free space. Exporting needs about {(requiredFreeSpace + 1024 * 1024 - 1) / 1024 / 1024} MiB free for the new package and current package. Use Create FAT32 boot volume or choose a roomier FAT32 volume.");
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Cannot export to this volume", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
        Directory.CreateDirectory(dest);
        try
        {
            long length = AnimationPackage.Write(files, package, (int)fps.Value, (long)quality.Value, SelectedOutputSize());
            MessageBox.Show(this, $"Exported {files.Count} JPEG frames ({length / 1024 / 1024} MiB) at {fps.Value} FPS to:\n{package}", "Export complete");
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Export failed", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    Size? SelectedOutputSize() => outputSize.SelectedIndex switch
    {
        0 => new Size(1280, 720),
        1 => new Size(1920, 1080),
        _ => null
    };
}
