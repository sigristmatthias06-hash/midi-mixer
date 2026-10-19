using System.Diagnostics;
using System.Text.Json;
using NAudio.CoreAudioApi;
using NAudio.Midi;

namespace MidiMixer;

static class Program
{
    public static bool TestMode { get; private set; }
    [STAThread]
    static void Main(string[] args)
    {
        TestMode = args.Any(a => a.EndsWith("-test", StringComparison.Ordinal));
        if (args.Contains("--core-test")) { Environment.ExitCode = CoreTests.Run(); return; }
        ApplicationConfiguration.Initialize();
        if (args.Contains("--cable-devices-test"))
        {
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "cable-devices.txt"), CableDevicesForm.BuildReport());
            using var form = new CableDevicesForm(); form.Show(); Application.DoEvents();
            using var image = new Bitmap(form.Width, form.Height);
            form.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size));
            image.Save(Path.Combine(AppContext.BaseDirectory, "cable-devices.png"));
            form.Close(); return;
        }
        if (args.Contains("--virtual-mixer-test")) { VirtualMixerTests.Run(); return; }
        if (args.Contains("--virtual-device-test")) { VirtualMixerTests.DeviceTest(); return; }
        if (args.Contains("--obs-test"))
        {
            Task.Run(ObsIntegrationTest.Run).GetAwaiter().GetResult();
            return;
        }
        if (args.Contains("--smoke-test"))
        {
            var oldSettings = new Settings(); oldSettings.Channels[0].Target = "Migration-Test";
            oldSettings.PreparePages();
            oldSettings.Pages[1].Channels[0].Target = "Andere Seite";
            var reloaded = JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(oldSettings))!;
            reloaded.PreparePages();
            if (reloaded.Pages.Count != 8 || reloaded.Channels[0].Target != "Migration-Test" || reloaded.Pages[1].Channels[0].Target != "Andere Seite") throw new Exception("Seitenmigration fehlgeschlagen");
            if (RoutingForm.Encode("a#:b") != "a#22#3Ab") throw new Exception("OBS-Fensterkodierung fehlgeschlagen");
            var result = Audio.Read();
            using var form = new MixerForm();
            form.Show();
            Application.DoEvents();
            form.VerifyLayoutAndMidi();
            form.Close();
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "smoke-test.json"),
                JsonSerializer.Serialize(new { result.Device, Programs = result.Values.Keys, Outputs = Headphones.List(), MidiInputs = Enumerable.Range(0, MidiIn.NumberOfDevices).Select(i => MidiIn.DeviceInfo(i).ProductName).ToArray() }));
            return;
        }
        var main = new MixerForm();
        if (args.Contains("--virtual-mixer")) main.Shown += (_, _) => main.OpenVirtualMixer();
        Application.Run(main);
    }
}

record Level(float Volume, bool Mute);
record Snapshot(string Device, Dictionary<string, Level> Values);

static class Audio
{
    static MMDeviceEnumerator? enumerator;
    static MMDevice? device;
    static readonly List<MMDevice> sessionDevices = new();
    static readonly Dictionary<string, List<AudioSessionControl>> cached = new();
    public static void Close()
    {
        foreach (var session in cached.Values.SelectMany(s => s)) session.Dispose();
        cached.Clear();
        foreach (var endpoint in sessionDevices) endpoint.Dispose();
        sessionDevices.Clear();
        device?.Dispose(); device = null;
        enumerator?.Dispose(); enumerator = null;
    }
    public const string Master = "Gesamtlautstärke";
    public const string Empty = "— Nicht zugewiesen —";
    static string Key(AudioSessionControl session)
    {
        if (session.IsSystemSoundsSession) return "Windows-Systemklänge";
        using var process = Process.GetProcessById((int)session.GetProcessID);
        return process.ProcessName;
    }
    public static Snapshot Read()
    {
        Close();
        enumerator = new MMDeviceEnumerator();
        device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        var values = new Dictionary<string, Level> { [Master] = new(device.AudioEndpointVolume.MasterVolumeLevelScalar, device.AudioEndpointVolume.Mute) };
        foreach (var endpoint in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
        {
        sessionDevices.Add(endpoint);
        var sessions = endpoint.AudioSessionManager.Sessions;
        for (int i = 0; i < sessions.Count; i++)
        {
            var session = sessions[i];
            bool retained = false;
            try
            {
                if (session.State == NAudio.CoreAudioApi.Interfaces.AudioSessionState.AudioSessionStateExpired) continue;
                var key = Key(session);
                var level = new Level(session.SimpleAudioVolume.Volume, session.SimpleAudioVolume.Mute);
                if (values.TryGetValue(key, out var previous)) level = new(Math.Max(previous.Volume, level.Volume), previous.Mute && level.Mute);
                values[key] = level;
                if (!cached.TryGetValue(key, out var group)) cached[key] = group = new();
                group.Add(session); retained = true;
            }
            catch (ArgumentException) { }
            catch (System.Runtime.InteropServices.COMException) { }
            finally { if (!retained) session.Dispose(); }
        }
        }
        return new(device.FriendlyName, values);
    }
    public static void Set(string key, float? volume = null, bool? mute = null)
    {
        if (key == Empty || device == null) return;
        if (key == Master)
        {
            if (volume.HasValue) device.AudioEndpointVolume.MasterVolumeLevelScalar = Math.Clamp(volume.Value, 0, 1);
            if (mute.HasValue) device.AudioEndpointVolume.Mute = mute.Value;
            return;
        }
        if (!cached.TryGetValue(key, out var sessions)) return;
        foreach (var session in sessions)
        {
            try
            {
                if (volume.HasValue) session.SimpleAudioVolume.Volume = Math.Clamp(volume.Value, 0, 1);
                if (mute.HasValue) session.SimpleAudioVolume.Mute = mute.Value;
            }
            catch (ArgumentException) { }
            catch (System.Runtime.InteropServices.COMException) { }
        }
    }
    public static float Peak(string key)
    {
        try
        {
            if (key == Master) return device?.AudioMeterInformation.MasterPeakValue ?? 0;
            if (!cached.TryGetValue(key, out var sessions)) return 0;
            float peak = 0;
            foreach (var session in sessions)
                try { peak = Math.Max(peak, session.AudioMeterInformation.MasterPeakValue); } catch (System.Runtime.InteropServices.COMException) { }
            return peak;
        }
        catch { return 0; }
    }
}

sealed class Mapping
{
    public string Label { get; set; } = "";
    public string Target { get; set; } = Audio.Empty;
    public int CC { get; set; }
    public int Channel { get; set; } = 1;
}
sealed class Settings
{
    public bool AutoReconnect { get; set; } = true;
    public bool CloseToTray { get; set; } = true;
    public List<MixerPage> Pages { get; set; } = new();
    public int ActivePage { get; set; }
    public RoutingSettings Routing { get; set; } = new();
    public PadSettings Pads { get; set; } = new();
    public string MidiDevice { get; set; } = "";
    public Mapping[] Channels { get; set; } = Enumerable.Range(0, 9).Select(i => new Mapping { CC = 48 + i, Target = i == 8 ? Audio.Master : Audio.Empty }).ToArray();
    public void PreparePages()
    {
        if (Pages == null || Pages.Count != 8 || Pages.Any(p => p == null || p.Channels == null || p.Channels.Length != 9 || p.Channels.Any(c => c == null || c.CC is < 0 or > 127 || c.Channel is < 1 or > 16 || string.IsNullOrWhiteSpace(c.Target))))
            Pages = Enumerable.Range(0, 8).Select(i => new MixerPage { Name = $"Seite {i + 1}", Channels = i == 0 ? Channels : new Settings().Channels }).ToList();
        ActivePage = Math.Clamp(ActivePage, 0, 7);
        Channels = Pages[ActivePage].Channels;
    }
}
sealed class MixerPage
{
    public string Name { get; set; } = "Seite";
    public Mapping[] Channels { get; set; } = new Settings().Channels;
}

sealed class MixerForm : Form
{
    readonly string settingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MidiMixer", "settings.json");
    Settings settings = new();
    readonly ComboBox midiDevices = new() { Width = 250, DropDownStyle = ComboBoxStyle.DropDownList };
    readonly Label status = new() { AutoSize = true, ForeColor = Color.LightSkyBlue };
    readonly Label output = new() { AutoSize = true };
    readonly ComboBox[] targets = new ComboBox[9];
    readonly TrackBar[] sliders = new TrackBar[9];
    readonly Label[] levels = new Label[9];
    readonly Button[] mutes = new Button[9];
    readonly Button[] learns = new Button[9];
    readonly System.Windows.Forms.Timer timer = new() { Interval = 1500 };
    readonly System.Windows.Forms.Timer midiTimer = new() { Interval = 16 };
    readonly System.Collections.Concurrent.ConcurrentDictionary<(int Channel, int CC), (object? Sender, MidiInMessageEventArgs Event)> pendingCC = new();
    MidiIn? midi;
    bool updating;
    int learning = -1;
    Snapshot? snapshot;
    PadForm? padForm;
    readonly LedOutput ledOutput = new();
    readonly ComboBox pagePicker = new() { Width = 180, DropDownStyle = ComboBoxStyle.DropDownList };
    RoutingForm? routingForm;
    VirtualMixerForm? virtualMixer;
    readonly Pickup[] pickup = Enumerable.Range(0, 9).Select(_ => new Pickup()).ToArray();
    readonly TextBox[] names = new TextBox[9];
    readonly ProgressBar[] meters = new ProgressBar[9];
    readonly StreamMixer streamMixer = new();
    readonly Button panicButton = MakeButton("Stream stumm");
    readonly Label streamStatus = new() { Width = 310, Height = 35, AutoEllipsis = true, Padding = new Padding(8, 5, 0, 0) };
    readonly System.Windows.Forms.Timer utilityTimer = new() { Interval = 75 };
    readonly NotifyIcon tray = new() { Icon = SystemIcons.Application, Text = "MIDI Mixer" };
    bool exiting, manuallyDisconnected;
    DateTime nextMidiRetry;

    public MixerForm()
    {
        Text = "MIDI Mixer · APC mini mk2";
        ClientSize = new Size(1230, 650);
        MinimumSize = new Size(1020, 650);
        BackColor = Color.FromArgb(19, 23, 32);
        ForeColor = Color.WhiteSmoke;
        Font = new Font("Segoe UI", 10);
        AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.CenterScreen;
        try
        {
            if (!Program.TestMode && File.Exists(settingsPath)) settings = JsonSerializer.Deserialize<Settings>(File.ReadAllText(settingsPath)) ?? new();
            if (settings.Channels == null || settings.Channels.Length != 9 || settings.Channels.Any(c => c == null || c.CC is < 0 or > 127 || c.Channel is < 1 or > 16 || string.IsNullOrWhiteSpace(c.Target))) settings = new();
        }
        catch (Exception ex) { status.Text = "Einstellungen konnten nicht geladen werden: " + ex.Message; }
        settings.PreparePages();
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20), RowCount = 5, ColumnCount = 1 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 55));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 80));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 65));
        Controls.Add(layout);
        var header = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        header.Controls.Add(new Label { Text = "MIDI MIXER", Font = new Font("Segoe UI", 23, FontStyle.Bold), AutoSize = true, Margin = new Padding(0, 0, 25, 0) });
        var options = MakeButton("Profile / Optionen"); options.Click += (_, _) => ShowOptions(); header.Controls.Add(options);
        panicButton.Click += (_, _) => { streamMixer.TogglePanic(); UpdatePanic(); };
        header.Controls.Add(panicButton); header.Controls.Add(streamStatus); layout.Controls.Add(header, 0, 0);
        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Fill };
        toolbar.Controls.Add(new Label { Text = "MIDI-Eingang", AutoSize = true, Padding = new Padding(0, 6, 8, 0) });
        toolbar.Controls.Add(midiDevices);
        var connect = MakeButton("Verbinden");
        connect.Click += (_, _) => Connect();
        toolbar.Controls.Add(connect);
        var scan = MakeButton("Geräte suchen");
        scan.Click += (_, _) => ScanMidi();
        toolbar.Controls.Add(scan);
        var disconnect = MakeButton("Trennen");
        disconnect.Click += (_, _) => { manuallyDisconnected = true; Disconnect(); status.Text = "MIDI getrennt"; };
        toolbar.Controls.Add(disconnect);
        var colors = MakeButton("APC-Farben");
        colors.Click += (_, _) =>
        {
            if (padForm == null || padForm.IsDisposed)
                padForm = new PadForm(settings.Pads ??= new(), Save, ledOutput);
            padForm.Show(this); padForm.BringToFront();
        };
        toolbar.Controls.Add(colors);
        layout.Controls.Add(toolbar, 0, 1);
        var pageArea = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown };
        pageArea.Controls.Add(output);
        var pageBar = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        pagePicker.Items.AddRange(settings.Pages.Select(p => p.Name).ToArray());
        pagePicker.SelectedIndex = settings.ActivePage;
        pagePicker.SelectedIndexChanged += PageChanged;
        pageBar.Controls.Add(pagePicker);
        var pageName = new TextBox { Width = 140, PlaceholderText = "Neuer Seitenname" };
        pageBar.Controls.Add(pageName);
        var rename = MakeButton("Umbenennen");
        rename.Click += (_, _) => { if (string.IsNullOrWhiteSpace(pageName.Text)) return; settings.Pages[settings.ActivePage].Name = pageName.Text.Trim(); pagePicker.Items[settings.ActivePage] = pageName.Text.Trim(); Save(); };
        pageBar.Controls.Add(rename);
        var routing = MakeButton("Audio-Routing / OBS");
        routing.Click += (_, _) => { if (routingForm == null || routingForm.IsDisposed) routingForm = new RoutingForm(settings.Routing ??= new(), Save); routingForm.Show(this); routingForm.BringToFront(); };
        pageBar.Controls.Add(routing);
        var virtualButton = MakeButton("Virtueller Mixer");
        virtualButton.Click += (_, _) => OpenVirtualMixer();
        pageBar.Controls.Add(virtualButton);
        pageArea.Controls.Add(pageBar);
        layout.Controls.Add(pageArea, 0, 2);
        var strips = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 9, RowCount = 1 };
        layout.Controls.Add(strips, 0, 3);
        for (int i = 0; i < 9; i++)
        {
            int index = i;
            strips.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / 9));
            var strip = new TableLayoutPanel { Dock = DockStyle.Fill, Margin = new Padding(4), Padding = new Padding(8), BackColor = Color.FromArgb(29, 35, 47), RowCount = 6, ColumnCount = 1 };
            strip.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            strip.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            strip.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            strip.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            strip.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            strip.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            strip.RowStyles.Add(new RowStyle(SizeType.Absolute, 45));
            names[i] = new TextBox { Dock = DockStyle.Fill, Text = settings.Channels[i].Label, PlaceholderText = i == 8 ? "MASTER" : $"KANAL {i + 1}", BorderStyle = BorderStyle.None, BackColor = strip.BackColor, ForeColor = Color.Turquoise };
            names[i].TextChanged += (_, _) => { if (!updating) { settings.Channels[index].Label = names[index].Text; Save(); } };
            strip.Controls.Add(names[i], 0, 0);
            targets[i] = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList, DropDownWidth = 260 };
            targets[i].Items.Add(settings.Channels[i].Target);
            targets[i].SelectedIndex = 0;
            targets[i].SelectedIndexChanged += (_, _) => { if (!updating) { pickup[index].Reset(); settings.Channels[index].Target = targets[index].Text; Save(); RefreshAudio(); } };
            strip.Controls.Add(targets[i], 0, 1);
            sliders[i] = new TrackBar { Orientation = Orientation.Vertical, Minimum = 0, Maximum = 100, TickFrequency = 10, Anchor = AnchorStyles.Top | AnchorStyles.Bottom, Width = 48 };
            sliders[i].Scroll += (_, _) => Apply(index, sliders[index].Value / 100f);
            strip.Controls.Add(sliders[i], 0, 2);
            levels[i] = new Label { AutoSize = true, Text = "—" };
            var readout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
            readout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            readout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); readout.RowStyles.Add(new RowStyle(SizeType.Absolute, 8));
            levels[i].Font = new Font("Segoe UI", 8);
            meters[i] = new ProgressBar { Dock = DockStyle.Fill, Maximum = 100, Style = ProgressBarStyle.Continuous, Margin = Padding.Empty };
            readout.Controls.Add(levels[i], 0, 0); readout.Controls.Add(meters[i], 0, 1);
            strip.Controls.Add(readout, 0, 3);
            mutes[i] = MakeButton("Stumm");
            mutes[i].Dock = DockStyle.Fill;
            mutes[i].AutoSize = false;
            mutes[i].Click += (_, _) => ToggleMute(index);
            strip.Controls.Add(mutes[i], 0, 4);
            learns[i] = MakeButton($"CC {settings.Channels[i].CC}");
            learns[i].Dock = DockStyle.Fill;
            learns[i].AutoSize = false;
            learns[i].Click += (_, _) => { learning = learning == index ? -1 : index; UpdateLearn(); status.Text = learning < 0 ? "MIDI Learn beendet" : "Jetzt den gewünschten Fader bewegen … (erneuter Klick bricht ab)"; };
            strip.Controls.Add(learns[i], 0, 5);
            strips.Controls.Add(strip, i, 0);
        }
        var footer = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown };
        footer.Controls.Add(new Label { Text = "Programm wählen · CC anklicken für MIDI Learn · Track-Tasten 1–8: Stumm · Änderungen werden automatisch gespeichert", AutoSize = true });
        footer.Controls.Add(status);
        layout.Controls.Add(footer, 0, 4);
        timer.Tick += (_, _) => RefreshAudio();
        midiTimer.Tick += (_, _) => DrainMidi();
        streamMixer.Changed += () => { if (!IsDisposed) { UpdatePanic(); } };
        utilityTimer.Tick += (_, _) => UtilityTick();
        var trayMenu = new ContextMenuStrip();
        trayMenu.Items.Add("Öffnen", null, (_, _) => { Show(); WindowState = FormWindowState.Normal; Activate(); });
        trayMenu.Items.Add("Beenden", null, (_, _) => { exiting = true; Close(); });
        tray.ContextMenuStrip = trayMenu; tray.DoubleClick += (_, _) => { Show(); WindowState = FormWindowState.Normal; Activate(); };
        Shown += (_, _) => { RefreshAudio(); ScanMidi(); timer.Start(); midiTimer.Start(); if (!Program.TestMode) { tray.Visible = true; utilityTimer.Start(); streamMixer.Start(); } };
        FormClosing += (_, e) =>
        {
            if (!Program.TestMode && !exiting && settings.CloseToTray && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); return; }
            virtualMixer?.Close(); routingForm?.Close(); padForm?.Close(); timer.Stop(); midiTimer.Stop(); utilityTimer.Stop(); streamMixer.Dispose(); tray.Dispose(); Disconnect(); ledOutput.Dispose(); Audio.Close();
        };
        FormClosed += (_, _) => { timer.Dispose(); midiTimer.Dispose(); utilityTimer.Dispose(); trayMenu.Dispose(); };
    }
    static Button MakeButton(string text) => new() { Text = text, AutoSize = true, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(40, 49, 66), ForeColor = Color.WhiteSmoke, Height = 32 };
    public void OpenVirtualMixer()
    {
        if (virtualMixer == null || virtualMixer.IsDisposed) virtualMixer = new VirtualMixerForm();
        virtualMixer.Show(this); virtualMixer.BringToFront();
    }
    void UpdatePanic()
    {
        streamStatus.Text = streamMixer.Connected ? $"OBS · {streamMixer.Levels.Count} Quellen" : "OBS getrennt · automatische Wiederverbindung";
        panicButton.Enabled = streamMixer.Connected && streamMixer.Levels.Count > 0;
        panicButton.Text = streamMixer.Panic ? (streamMixer.PanicApplied ? "STREAM STUMM · aufheben" : "Stream wird stumm …") : "Stream stumm";
        panicButton.BackColor = streamMixer.Panic ? (streamMixer.PanicApplied ? Color.Firebrick : Color.DarkOrange) : Color.FromArgb(40, 49, 66);
        try { ledOutput.SetStreamState(streamMixer.Connected && streamMixer.Levels.Count > 0, streamMixer.Panic, streamMixer.PanicApplied); } catch (Exception ex) { status.Text = "LED-Ausgang: " + ex.Message; }
    }
    void UtilityTick()
    {
        for (int i = 0; i < 9; i++)
        {
            string target = settings.Channels[i].Target;
            bool stream = target.StartsWith(StreamMixer.KeyPrefix, StringComparison.Ordinal);
            meters[i].Enabled = true;
            float peak = target.StartsWith("Mix: ", StringComparison.Ordinal) ? virtualMixer?.Engine.Peak(target) ?? 0 : stream ? streamMixer.Peak(target) : Audio.Peak(target);
            meters[i].Value = Math.Clamp((int)(peak * 100), 0, 100);
        }
        UpdatePanic();
        if (!settings.AutoReconnect || manuallyDisconnected || DateTime.UtcNow < nextMidiRetry) return;
        nextMidiRetry = DateTime.UtcNow.AddSeconds(5);
        try
        {
            string wanted = string.IsNullOrWhiteSpace(settings.MidiDevice) ? "APC mini mk2" : settings.MidiDevice;
            int index = Enumerable.Range(0, MidiIn.NumberOfDevices).FirstOrDefault(i => MidiIn.DeviceInfo(i).ProductName == wanted, -1);
            if (index < 0) { if (midi != null) { Disconnect(); ledOutput.Disconnect(); status.Text = "APC getrennt – warte auf Wiederverbindung"; } return; }
            if (midi == null) { ScanMidi(); midiDevices.SelectedIndex = index; Connect(); }
            else if (!ledOutput.Connected && wanted == "APC mini mk2")
            {
                int outputIndex = Enumerable.Range(0, MidiOut.NumberOfDevices).FirstOrDefault(i => MidiOut.DeviceInfo(i).ProductName == wanted, -1);
                if (outputIndex >= 0) { SyncLeds(); ledOutput.Connect(outputIndex); }
            }
        }
        catch (Exception ex) { status.Text = "Wiederverbinden: " + ex.Message; }
    }
    protected override void WndProc(ref Message m)
    {
        // Reopen WinMM handles after hotplug, even when the device returns with the same name.
        if (m.Msg == 0x0219 && !Program.TestMode && settings.AutoReconnect && !manuallyDisconnected)
            Post(() => { if (IsDisposed) return; Disconnect(); ledOutput.Disconnect(); nextMidiRetry = DateTime.MinValue; });
        base.WndProc(ref m);
    }
    void ShowOptions()
    {
        using var dialog = new Form { Text = "Profile & Optionen", ClientSize = new Size(590, 350), StartPosition = FormStartPosition.CenterParent, Font = Font, BackColor = BackColor, ForeColor = ForeColor };
        var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, Padding = new Padding(20), WrapContents = false };
        dialog.Controls.Add(panel);
        var reconnect = new CheckBox { Text = "APC automatisch wiederverbinden", Checked = settings.AutoReconnect, AutoSize = true };
        reconnect.CheckedChanged += (_, _) => { settings.AutoReconnect = reconnect.Checked; manuallyDisconnected = false; Save(); };
        panel.Controls.Add(reconnect);
        var background = new CheckBox { Text = "Beim Schließen im Infobereich weiterlaufen", Checked = settings.CloseToTray, AutoSize = true };
        background.CheckedChanged += (_, _) => { settings.CloseToTray = background.Checked; Save(); }; panel.Controls.Add(background);
        using var run = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        var autostart = new CheckBox { Text = "Mit Windows starten", Checked = run.GetValue("MidiMixer") != null, AutoSize = true };
        autostart.CheckedChanged += (_, _) =>
        {
            try { if (autostart.Checked) run.SetValue("MidiMixer", "\"" + Environment.ProcessPath + "\""); else run.DeleteValue("MidiMixer", false); }
            catch (Exception ex) { MessageBox.Show(dialog, ex.Message, "Autostart"); }
        };
        panel.Controls.Add(autostart);
        var export = MakeButton("Profil speichern …");
        export.Click += (_, _) =>
        {
            using var file = new SaveFileDialog { Filter = "MIDI-Mixer-Profil|*.json", FileName = "Streaming.json" };
            if (file.ShowDialog(dialog) != DialogResult.OK) return;
            try { File.WriteAllText(file.FileName, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true })); status.Text = "Profil gespeichert"; }
            catch (Exception ex) { MessageBox.Show(dialog, ex.Message, "Profil speichern"); }
        };
        panel.Controls.Add(export);
        var import = MakeButton("Profil laden …");
        import.Click += (_, _) =>
        {
            using var file = new OpenFileDialog { Filter = "MIDI-Mixer-Profil|*.json" };
            if (file.ShowDialog(dialog) != DialogResult.OK) return;
            try
            {
                var loaded = JsonSerializer.Deserialize<Settings>(File.ReadAllText(file.FileName)) ?? throw new IOException("Ungültiges Profil");
                if (loaded.Channels == null || loaded.Channels.Length != 9 || loaded.Channels.Any(c => c == null || c.Target == null)) throw new IOException("Ungültige Kanalbelegung");
                loaded.PreparePages();
                // Snapshot for recovery before replacing the current profile.
                File.WriteAllText(settingsPath + ".before-profile.json", JsonSerializer.Serialize(settings));
                routingForm?.Close(); padForm?.Close();
                if (routingForm is { IsDisposed: false }) throw new IOException("Bitte warten, bis die OBS-Aktion abgeschlossen ist.");
                Disconnect(); ledOutput.Disconnect(); settings = loaded;
                updating = true; pagePicker.Items.Clear(); pagePicker.Items.AddRange(settings.Pages.Select(p => p.Name).ToArray());
                pagePicker.SelectedIndex = settings.ActivePage; updating = false;
                SwitchPage(settings.ActivePage); manuallyDisconnected = false;
                status.Text = "Profil geladen. Routing-Auswahl übernommen; im Routing-Fenster ausdrücklich anwenden.";
                dialog.Close();
            }
            catch (Exception ex) { updating = false; MessageBox.Show(dialog, ex.Message, "Profil laden"); }
        };
        panel.Controls.Add(import);
        panel.Controls.Add(new Label { Text = "Profile enthalten Seiten, Namen, Farben und Routing-Auswahl.\nAudio-Routing erst im Routing-Fenster anwenden.\nBeenden: Rechtsklick auf das Symbol im Infobereich → Beenden.\nStream-Stumm betrifft nur die von MIDI Mixer verwalteten OBS-Quellen.", AutoSize = true });
        dialog.ShowDialog(this);
    }
    void PageChanged(object? sender, EventArgs e) { if (!updating) SwitchPage(pagePicker.SelectedIndex); }
    void SwitchPage(int index)
    {
        if (index < 0 || index > 7) return;
        pendingCC.Clear(); learning = -1;
        foreach (var p in pickup) p.Reset();
        settings.ActivePage = index; settings.Channels = settings.Pages[index].Channels;
        updating = true; for (int i = 0; i < 9; i++) names[i].Text = settings.Channels[i].Label; updating = false;
        UpdateLearn(); RefreshAudio(); Save();
        status.Text = $"{settings.Pages[index].Name} · Audio-Routing bleibt bestehen";
    }
    internal void VerifyLayoutAndMidi()
    {
        using (var routing = new RoutingForm(new RoutingSettings(), () => { }))
        {
            routing.Show(); Application.DoEvents();
            using var picture = new Bitmap(routing.Width, routing.Height);
            routing.DrawToBitmap(picture, new Rectangle(Point.Empty, picture.Size));
            picture.Save(Path.Combine(AppContext.BaseDirectory, "routing-test.png"));
            routing.Close();
        }
        PadForm.VerifyProtocol();
        Pickup.Verify();
        LedOutput.Verify();
        using (var pads = new PadForm(new PadSettings(), () => { }, ledOutput))
        {
            pads.Show(); Application.DoEvents();
            using var picture = new Bitmap(pads.Width, pads.Height);
            pads.DrawToBitmap(picture, new Rectangle(Point.Empty, picture.Size));
            picture.Save(Path.Combine(AppContext.BaseDirectory, "pads-test.png"));
            pads.Close();
        }
        foreach (var size in new[] { new Size(1020, 650), new Size(1230, 650), new Size(1900, 980) })
        {
            ClientSize = size;
            PerformLayout();
            Application.DoEvents();
            foreach (var control in targets.Cast<Control>().Concat(mutes).Concat(learns).Concat(sliders))
                if (control.Left < 0 || control.Right > control.Parent!.ClientSize.Width || control.Bottom > control.Parent.ClientSize.Height)
                    throw new InvalidOperationException($"Layout overflow: {control.GetType().Name} at {size}");
        }
        using (var bitmap = new Bitmap(ClientSize.Width, ClientSize.Height))
        {
            DrawToBitmap(bitmap, new Rectangle(Point.Empty, ClientSize));
            bitmap.Save(Path.Combine(AppContext.BaseDirectory, "layout-test.png"));
        }
        // A burst must occupy one slot per CC and retain the final value, without writing audio.
        var watch = Stopwatch.StartNew();
        for (int i = 0; i < 10000; i++)
            OnMidi(this, new MidiInMessageEventArgs(new ControlChangeEvent(0, 1, (MidiController)48, i % 128).GetAsShortMessage(), 0));
        if (pendingCC.Count != 1 || ((ControlChangeEvent)pendingCC[(1, 48)].Event.MidiEvent).ControllerValue != 9999 % 128)
            throw new InvalidOperationException("MIDI burst coalescing failed");
        DrainMidi(); // Sender is deliberately not the connected device: stale events must be ignored.
        if (!pendingCC.IsEmpty) throw new InvalidOperationException("MIDI drain failed");
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "performance-test.txt"), $"10000 MIDI events coalesced and drained in {watch.Elapsed.TotalMilliseconds:F2} ms. Layout bounds passed at 3 sizes. No volume writes.");
    }
    void Save()
    {
        if (Program.TestMode) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
            File.WriteAllText(settingsPath + ".tmp", JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(settingsPath + ".tmp", settingsPath, true);
        }
        catch (Exception ex) { status.Text = "Speichern fehlgeschlagen: " + ex.Message; }
    }
    void RefreshAudio()
    {
        try
        {
            var oldSnapshot = snapshot;
            snapshot = Audio.Read();
            foreach (var pair in streamMixer.Levels) snapshot.Values[pair.Key] = pair.Value;
            if (virtualMixer is { IsDisposed: false }) foreach (var pair in virtualMixer.Engine.Levels()) snapshot.Values[pair.Key] = pair.Value;
            output.Text = "Windows-Standardausgabe: " + snapshot.Device;
            updating = true;
            for (int i = 0; i < 9; i++)
            {
                var target = settings.Channels[i].Target;
                var choices = new[] { Audio.Empty, Audio.Master }.Concat(snapshot.Values.Keys.Where(k => k != Audio.Master).Order()).Append(target).Distinct().ToArray();
                if (!targets[i].Items.Cast<string>().SequenceEqual(choices))
                {
                    targets[i].Items.Clear(); targets[i].Items.AddRange(choices); targets[i].SelectedItem = target;
                }
                targets[i].SelectedItem = target;
                bool available = snapshot.Values.TryGetValue(target, out var value);
                if (!available || oldSnapshot == null || !oldSnapshot.Values.TryGetValue(target, out var oldValue) || Math.Abs(oldValue.Volume - value!.Volume) > .015f) pickup[i].Reset();
                sliders[i].Enabled = mutes[i].Enabled = available;
                levels[i].Text = available ? $"{Math.Round(value!.Volume * 100)} %{pickup[i].Hint(value.Volume)}" : target == Audio.Empty ? "Nicht belegt" : "Nicht aktiv";
                if (available && !sliders[i].Capture) sliders[i].Value = Math.Clamp((int)Math.Round(value!.Volume * 100), 0, 100);
                mutes[i].Text = available && value!.Mute ? "Stumm ✓" : "Stumm";
            }
        }
        catch (Exception ex) { output.Text = "Audio nicht verfügbar: " + ex.Message; snapshot = null; foreach (var control in sliders) control.Enabled = false; foreach (var control in mutes) control.Enabled = false; }
        finally { updating = false; SyncLeds(); }
    }
    void SyncLeds()
    {
        try { ledOutput.Sync(settings.Channels, snapshot, settings.ActivePage); }
        catch (Exception ex) { status.Text = "LED-Ausgang getrennt: " + ex.Message; }
        if (padForm is { IsDisposed: false }) padForm.RefreshPads();
    }
    void Apply(int index, float volume, bool fromMidi = false)
    {
        try
        {
            var key = settings.Channels[index].Target;
            if (snapshot == null || !snapshot.Values.TryGetValue(key, out var previous)) return;
            if (key.StartsWith("Mix: ", StringComparison.Ordinal) && virtualMixer is { IsDisposed: false }) { virtualMixer.Engine.Set(key, volume); virtualMixer.Save(); }
            else if (key.StartsWith(StreamMixer.KeyPrefix, StringComparison.Ordinal)) streamMixer.SetVolume(key, volume);
            else Audio.Set(key, volume);
            snapshot.Values[key] = previous with { Volume = volume };
            for (int i = 0; i < 9; i++)
                if (settings.Channels[i].Target == key)
                {
                    if (!fromMidi || i != index) pickup[i].Reset();
                    sliders[i].Value = Math.Clamp((int)Math.Round(volume * 100), 0, 100);
                    levels[i].Text = $"{Math.Round(volume * 100)} %{pickup[i].Hint(volume)}";
                }
        }
        catch (Exception ex) { status.Text = "Lautstärke konnte nicht gesetzt werden: " + ex.Message; }
    }
    void ToggleMute(int index)
    {
        try
        {
            var key = settings.Channels[index].Target;
            if (key.StartsWith("Mix: ", StringComparison.Ordinal) && virtualMixer is { IsDisposed: false })
            {
                if (virtualMixer.Engine.Levels().TryGetValue(key, out var mixLevel)) { virtualMixer.Engine.Set(key, mute: !mixLevel.Mute); virtualMixer.Save(); RefreshAudio(); }
                return;
            }
            if (key.StartsWith(StreamMixer.KeyPrefix, StringComparison.Ordinal))
            {
                if (streamMixer.Levels.TryGetValue(key, out var streamLevel)) streamMixer.SetMute(key, !streamLevel.Mute);
                return;
            }
            if (Audio.Read().Values.TryGetValue(key, out var level)) Audio.Set(key, mute: !level.Mute);
            RefreshAudio();
        }
        catch (Exception ex) { status.Text = "Stummschalten fehlgeschlagen: " + ex.Message; }
    }
    void ScanMidi()
    {
        try
        {
            midiDevices.Items.Clear();
            for (int i = 0; i < MidiIn.NumberOfDevices; i++) midiDevices.Items.Add(MidiIn.DeviceInfo(i).ProductName);
            var preferred = midiDevices.Items.Cast<string>().FirstOrDefault(n => n == settings.MidiDevice)
                ?? midiDevices.Items.Cast<string>().FirstOrDefault(n => n.Contains("APC", StringComparison.OrdinalIgnoreCase));
            if (preferred != null) midiDevices.SelectedItem = preferred;
            else if (midiDevices.Items.Count > 0) midiDevices.SelectedIndex = 0;
            status.Text = midiDevices.Items.Count == 0 ? "Kein MIDI-Eingang gefunden. Controller anschließen und Geräte suchen." : "MIDI-Eingang auswählen und verbinden.";
        }
        catch (Exception ex) { status.Text = "MIDI-Suche fehlgeschlagen: " + ex.Message; }
    }
    void Connect()
    {
        manuallyDisconnected = false;
        Disconnect();
        if (midiDevices.SelectedIndex < 0) return;
        try
        {
            midi = new MidiIn(midiDevices.SelectedIndex);
            midi.MessageReceived += OnMidi;
            midi.ErrorReceived += OnMidiError;
            midi.Start();
            settings.MidiDevice = midiDevices.Text; Save();
            status.Text = "Verbunden: " + settings.MidiDevice;
            if (!ledOutput.Connected && settings.MidiDevice.Equals("APC mini mk2", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    SyncLeds();
                    int device = Enumerable.Range(0, MidiOut.NumberOfDevices).FirstOrDefault(i => MidiOut.DeviceInfo(i).ProductName.Equals("APC mini mk2", StringComparison.OrdinalIgnoreCase), -1);
                    if (device >= 0) ledOutput.Connect(device);
                    else status.Text += " · Kein APC-LED-Ausgang gefunden";
                }
                catch (Exception ex) { status.Text = "MIDI-Eingang verbunden; LED-Ausgang fehlgeschlagen: " + ex.Message; }
            }
        }
        catch (Exception ex) { Disconnect(); status.Text = "MIDI-Verbindung fehlgeschlagen: " + ex.Message; }
    }
    void Disconnect()
    {
        var old = midi; midi = null;
        foreach (var p in pickup) p.Reset();
        pendingCC.Clear();
        if (old != null)
        {
            old.MessageReceived -= OnMidi; old.ErrorReceived -= OnMidiError;
            try { old.Stop(); } finally { old.Dispose(); }
        }
        learning = -1; UpdateLearn();
    }
    void Post(Action action)
    {
        if (IsDisposed || !IsHandleCreated) return;
        try { BeginInvoke(action); } catch (InvalidOperationException) { }
    }
    void OnMidiError(object? sender, MidiInMessageEventArgs e) => Post(() => status.Text = "Ungültige MIDI-Nachricht empfangen.");
    void OnMidi(object? sender, MidiInMessageEventArgs e)
    {
        if (e.MidiEvent is ControlChangeEvent cc)
            pendingCC[(cc.Channel, (int)cc.Controller)] = (sender, e);
        else Post(() => HandleMidi(sender, e));
    }
    void DrainMidi()
    {
        foreach (var key in pendingCC.Keys)
            if (pendingCC.TryRemove(key, out var message)) HandleMidi(message.Sender, message.Event);
    }
    void HandleMidi(object? sender, MidiInMessageEventArgs e)
    {
        if (sender != midi || IsDisposed) return;
        if (e.MidiEvent is ControlChangeEvent cc)
        {
            int number = (int)cc.Controller;
            status.Text = $"MIDI · Kanal {cc.Channel} · CC {number} · Wert {cc.ControllerValue}";
            if (learning >= 0)
            {
                var mapping = settings.Channels[learning]; mapping.CC = number; mapping.Channel = cc.Channel;
                pickup[learning].Reset();
                learning = -1; Save(); UpdateLearn();
                return;
            }
            for (int i = 0; i < 9; i++)
                if (settings.Channels[i].CC == number && settings.Channels[i].Channel == cc.Channel && snapshot != null && snapshot.Values.TryGetValue(settings.Channels[i].Target, out var value))
                {
                    if (pickup[i].Accept(cc.ControllerValue / 127f, value.Volume)) Apply(i, cc.ControllerValue / 127f, true);
                    else levels[i].Text = $"{Math.Round(value.Volume * 100)} %{pickup[i].Hint(value.Volume)}";
                }
        }
        else if (e.MidiEvent is NoteOnEvent note && note.Velocity > 0 && note.Channel == 1 && note.NoteNumber is >= 100 and <= 107 && learning < 0)
            ToggleMute(note.NoteNumber - 100);
        else if (e.MidiEvent is NoteOnEvent scene && scene.Velocity > 0 && scene.Channel == 1 && scene.NoteNumber is >= 112 and <= 119)
            pagePicker.SelectedIndex = scene.NoteNumber - 112;
        else if (e.MidiEvent is NoteOnEvent pad && pad.Velocity > 0 && pad.Channel == 1 && pad.NoteNumber == 63)
        { streamMixer.TogglePanic(); UpdatePanic(); }
    }
    void UpdateLearn()
    {
        for (int i = 0; i < 9; i++) if (learns[i] != null) learns[i].Text = learning == i ? "Bewegen …" : $"CC {settings.Channels[i].CC}";
    }
}
