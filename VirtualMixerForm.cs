using System.Diagnostics;
using System.Text.Json;

namespace MidiMixer;

sealed class VirtualMixerForm : Form
{
    readonly string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MidiMixer", "virtual-mixer.json");
    readonly VirtualMixerSettings settings;
    public VirtualMixerEngine Engine { get; }
    readonly ComboBox[] sources = new ComboBox[4];
    readonly ComboBox localOutput = new() { Width = 355, DropDownWidth = 650, DropDownStyle = ComboBoxStyle.DropDownList };
    readonly ComboBox streamOutput = new() { Width = 355, DropDownWidth = 650, DropDownStyle = ComboBoxStyle.DropDownList };
    readonly TrackBar[] sliders = new TrackBar[10];
    readonly CheckBox[] mutes = new CheckBox[10];
    readonly ProgressBar[] meters = new ProgressBar[10];
    readonly Label[] values = new Label[10];
    readonly Label status = new() { Dock = DockStyle.Fill };
    readonly System.Windows.Forms.Timer timer = new() { Interval = 100 };
    bool updating, closing;
    public VirtualMixerForm()
    {
        settings = new();
        try { if (!Program.TestMode && File.Exists(path)) settings = JsonSerializer.Deserialize<VirtualMixerSettings>(File.ReadAllText(path)) ?? new(); } catch { }
        if (settings.Channels == null || settings.Channels.Length != 4 || settings.Channels.Any(c => c == null)) settings = new();
        Engine = new(settings);
        Text = "Virtueller Mixer · Audio-Prototyp"; ClientSize = new Size(1150, 790); MinimumSize = new Size(1060, 760);
        AutoScaleMode = AutoScaleMode.Dpi; Font = new Font("Segoe UI", 10); BackColor = Color.FromArgb(19, 23, 32); ForeColor = Color.WhiteSmoke;
        StartPosition = FormStartPosition.CenterParent;
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18), ColumnCount = 1, RowCount = 6 };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 70)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 74));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 72)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 45)); Controls.Add(root);
        root.Controls.Add(new Label { Dock = DockStyle.Fill, Text = "VIER EINGÄNGE · ZWEI UNABHÄNGIGE MISCHUNGEN\nProgramme in Windows auf getrennte virtuelle Geräte legen. Hier deren Aufnahme-Ende wählen\noder einen vorhandenen virtuellen Wiedergabeausgang abgreifen. Noch keine Treiberinstallation / Kabel-Erstellung." }, 0, 0);
        var outputs = new FlowLayoutPanel { Dock = DockStyle.Fill };
        var left = new FlowLayoutPanel { Width = 480, Height = 70, FlowDirection = FlowDirection.TopDown };
        left.Controls.Add(new Label { Text = "Kopfhörer-Ausgang (z. B. JBL)", AutoSize = true }); left.Controls.Add(localOutput);
        var right = new FlowLayoutPanel { Width = 480, Height = 70, FlowDirection = FlowDirection.TopDown };
        right.Controls.Add(new Label { Text = "Stream-Ausgang (separates virtuelles Kabel → OBS)", AutoSize = true }); right.Controls.Add(streamOutput);
        outputs.Controls.Add(left); outputs.Controls.Add(right); root.Controls.Add(outputs, 0, 1);
        var table = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 6 };
        foreach (var percent in new[] { 11, 35, 27, 27 }) table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, percent));
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 26)); for (int i = 0; i < 5; i++) table.RowStyles.Add(new RowStyle(SizeType.Percent, 20));
        foreach (var (text, column) in new[] { ("Kanal", 0), ("Eingangsgerät", 1), ("Kopfhörer", 2), ("Stream", 3) }) table.Controls.Add(new Label { Text = text, AutoSize = true, ForeColor = Color.Turquoise }, column, 0);
        for (int i = 0; i < 5; i++)
        {
            int channel = i;
            table.Controls.Add(new Label { Text = i == 4 ? "MASTER" : VirtualMixerEngine.Names[i], AutoSize = true, Padding = new Padding(0, 15, 0, 0) }, 0, i + 1);
            if (i < 4)
            {
                sources[i] = new ComboBox { Dock = DockStyle.Top, DropDownWidth = 700, DropDownStyle = ComboBoxStyle.DropDownList, Margin = new Padding(3, 15, 8, 3) };
                sources[i].SelectedIndexChanged += (_, _) => { if (updating || Engine.Running) return; if (sources[channel].SelectedItem is MixerEndpoint ep) { settings.Channels[channel].DeviceId = ep.Id; settings.Channels[channel].Loopback = ep.Loopback; Save(); } };
                table.Controls.Add(sources[i], 1, i + 1);
            }
            else table.Controls.Add(new Label { Text = "Startpegel 50 %\nStream-Kanäle zunächst stumm", AutoSize = true }, 1, i + 1);
            for (int bus = 0; bus < 2; bus++)
            {
                int index = i * 2 + bus; bool stream = bus == 1;
                var cell = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 3 };
                cell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 65)); cell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35));
                cell.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); cell.RowStyles.Add(new RowStyle(SizeType.Absolute, 25)); cell.RowStyles.Add(new RowStyle(SizeType.Absolute, 7));
                sliders[index] = new TrackBar { Minimum = 0, Maximum = 100, TickStyle = TickStyle.None, Dock = DockStyle.Fill, AutoSize = false };
                sliders[index].Scroll += (_, _) => { Engine.Set(VirtualMixerEngine.Key(channel, stream), sliders[index].Value / 100f); Save(); };
                cell.Controls.Add(sliders[index], 0, 0); cell.SetColumnSpan(sliders[index], 2);
                mutes[index] = new CheckBox { Text = "Stumm", AutoSize = true };
                mutes[index].CheckedChanged += (_, _) => { if (!updating) { Engine.Set(VirtualMixerEngine.Key(channel, stream), mute: mutes[index].Checked); Save(); } };
                values[index] = new Label { AutoSize = true };
                cell.Controls.Add(mutes[index], 0, 1); cell.Controls.Add(values[index], 1, 1);
                meters[index] = new ProgressBar { Dock = DockStyle.Fill, Margin = Padding.Empty, Maximum = 100 };
                cell.Controls.Add(meters[index], 0, 2); cell.SetColumnSpan(meters[index], 2);
                table.Controls.Add(cell, bus + 2, i + 1);
            }
        }
        root.Controls.Add(table, 0, 2);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill };
        actions.Controls.Add(Button("Geräte suchen", Scan));
        actions.Controls.Add(Button("Audio starten", Start));
        actions.Controls.Add(Button("Audio stoppen", () => { Engine.Stop(); EnableDevices(); status.Text = "Audio gestoppt."; }));
        actions.Controls.Add(Button("Windows-Mixer", () => Process.Start(new ProcessStartInfo("ms-settings:apps-volume") { UseShellExecute = true })));
        actions.Controls.Add(Button("Kabel & Treiber", () => { using var dialog = new CableDevicesForm(); dialog.ShowDialog(this); Scan(); }));
        root.Controls.Add(actions, 0, 3);
        root.Controls.Add(new Label { Dock = DockStyle.Fill, Text = "OBS: Aufnahme-Ende des Stream-Kabels als Audioeingabeaufnahme verwenden; Desktop-/Programm-Audio nicht zusätzlich erfassen.\nKein Eingang darf auf seinen eigenen Kabel-Ausgang zurückgeführt werden. Elgato/andere Mixer dürfen keine zusätzliche Rückführung erzeugen.\nDer Prototyp arbeitet mit 48 kHz Stereo. Bei Übersteuerung Pegel senken. Beim Schließen dieses Fensters stoppt die Audio-Engine." }, 0, 4);
        root.Controls.Add(status, 0, 5);
        localOutput.SelectedIndexChanged += (_, _) => { if (!updating && localOutput.SelectedItem is OutputDevice d) { settings.LocalDevice = d.Id; Save(); } };
        streamOutput.SelectedIndexChanged += (_, _) => { if (!updating && streamOutput.SelectedItem is OutputDevice d) { settings.StreamDevice = d.Id; Save(); } };
        Scan(); RefreshLevels(); timer.Tick += (_, _) => RefreshLevels(); timer.Start();
        FormClosed += (_, _) => { closing = true; timer.Stop(); timer.Dispose(); Engine.Dispose(); };
    }
    static Button Button(string text, Action action)
    {
        var b = new Button { Text = text, AutoSize = true, Height = 34, FlatStyle = FlatStyle.Flat, ForeColor = Color.WhiteSmoke, BackColor = Color.FromArgb(40, 49, 66) };
        b.Click += (_, _) => { try { action(); } catch (Exception ex) { MessageBox.Show(b.FindForm(), ex.Message, "Virtueller Mixer"); } }; return b;
    }
    public void Save()
    {
        if (Program.TestMode) return;
        try { Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true })); File.Move(path + ".tmp", path, true); }
        catch (Exception ex) { status.Text = "Speichern fehlgeschlagen: " + ex.Message; }
    }
    void Scan()
    {
        if (Engine.Running) { status.Text = "Vor der Geräteänderung Audio stoppen."; return; }
        updating = true;
        try
        {
            var inputs = VirtualMixerEngine.Inputs(); var outputs = Headphones.List();
            for (int i = 0; i < 4; i++)
            {
                var c = settings.Channels[i]; sources[i].Items.Clear(); sources[i].Items.Add(new MixerEndpoint("", "Nicht belegt", false)); sources[i].Items.AddRange(inputs);
                var match = inputs.FirstOrDefault(d => d.Id == c.DeviceId && d.Loopback == c.Loopback);
                if (match != null) sources[i].SelectedItem = match;
                else if (string.IsNullOrEmpty(c.DeviceId)) sources[i].SelectedIndex = 0;
                else { var missing = new MixerEndpoint(c.DeviceId, "Gespeichertes Gerät nicht verfügbar", c.Loopback); sources[i].Items.Add(missing); sources[i].SelectedItem = missing; }
            }
            localOutput.Items.Clear(); streamOutput.Items.Clear(); localOutput.Items.AddRange(outputs); streamOutput.Items.AddRange(outputs);
            localOutput.SelectedItem = outputs.FirstOrDefault(d => d.Id == settings.LocalDevice) ?? outputs.FirstOrDefault(d => d.Name.Contains("JBL", StringComparison.OrdinalIgnoreCase));
            streamOutput.SelectedItem = outputs.FirstOrDefault(d => d.Id == settings.StreamDevice);
            status.Text = $"{inputs.Length} Eingänge/Loopbacks, {outputs.Length} Ausgänge gefunden. Geräte auswählen und Audio ausdrücklich starten.";
        }
        finally { updating = false; }
    }
    void Start()
    {
        if (localOutput.SelectedItem is not OutputDevice local || streamOutput.SelectedItem is not OutputDevice stream) throw new IOException("Kopfhörer- und Stream-Ausgang auswählen. Fehlende virtuelle Geräte müssen zuerst installiert werden.");
        settings.LocalDevice = local.Id; settings.StreamDevice = stream.Id;
        Engine.Start(); Save(); EnableDevices();
    }
    void EnableDevices() { localOutput.Enabled = streamOutput.Enabled = !Engine.Running; foreach (var c in sources) c.Enabled = !Engine.Running; }
    void RefreshLevels()
    {
        if (closing) return;
        if (Engine.Failure is { } error) { Engine.Stop(); EnableDevices(); status.Text = error + " · Audio gestoppt; Geräte prüfen und erneut starten."; Engine.Failure = null; }
        updating = true;
        for (int i = 0; i < 5; i++) for (int bus = 0; bus < 2; bus++)
        {
            int index = i * 2 + bus; bool stream = bus == 1;
            float volume = i == 4 ? (stream ? settings.StreamMaster : settings.LocalMaster) : (stream ? settings.Channels[i].Stream : settings.Channels[i].Local);
            bool mute = i == 4 ? (stream ? settings.StreamMute : settings.LocalMute) : (stream ? settings.Channels[i].StreamMute : settings.Channels[i].LocalMute);
            if (!sliders[index].Capture) sliders[index].Value = Math.Clamp((int)(volume * 100), 0, 100);
            mutes[index].Checked = mute; values[index].Text = $"{volume * 100:0} %";
            meters[index].Value = Math.Clamp((int)(Engine.Peak(VirtualMixerEngine.Key(i, stream)) * 100), 0, 100);
        }
        updating = false;
        if (Engine.Running) status.Text = $"Audio läuft · Pufferkorrekturen: {Engine.Overflows} · übersteuerte Samples: {Engine.Clipping}. Bei steigenden Werten Routing/Pegel prüfen.";
    }
}
