using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace MidiMixer;

sealed class Route
{
    public string Program { get; set; } = "";
    public string Window { get; set; } = "";
    public bool Stream { get; set; }
}
sealed class RoutingSettings
{
    public string HeadphoneId { get; set; } = "";
    public List<Route> Routes { get; set; } = new();
}
sealed class RoutingForm : Form
{
    const string Prefix = "MIDI Mixer · ";
    readonly RoutingSettings settings;
    readonly Action save;
    readonly ComboBox devices = new() { Width = 380, DropDownStyle = ComboBoxStyle.DropDownList };
    readonly ComboBox scenes = new() { Width = 280, DropDownStyle = ComboBoxStyle.DropDownList };
    readonly DataGridView grid = new() { Dock = DockStyle.Fill, AllowUserToAddRows = false, AllowUserToDeleteRows = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, RowHeadersVisible = false, BackgroundColor = Color.FromArgb(29, 35, 47) };
    readonly Label status = new() { Dock = DockStyle.Fill, AutoEllipsis = true };
    readonly CheckBox isolate = new() { Text = "Andere OBS-Desktop-/Programm-Audioquellen stummschalten (Musik aus dem Stream ausschließen)", Checked = true, AutoSize = true };
    ObsClient? obs;
    bool busy;
    public RoutingForm(RoutingSettings settings, Action save)
    {
        this.settings = settings; this.save = save; settings.Routes ??= new();
        Text = "Audio-Routing · Kopfhörer & OBS"; ClientSize = new Size(1050, 730); MinimumSize = new Size(1000, 690);
        Font = new Font("Segoe UI", 10); BackColor = Color.FromArgb(19, 23, 32); ForeColor = Color.WhiteSmoke;
        AutoScaleMode = AutoScaleMode.Dpi; StartPosition = FormStartPosition.CenterParent;
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20), ColumnCount = 1, RowCount = 8 };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        foreach (var h in new[] { 65, 45, 70, 45 }) root.RowStyles.Add(new RowStyle(SizeType.Absolute, h));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        foreach (var h in new[] { 35, 45, 80 }) root.RowStyles.Add(new RowStyle(SizeType.Absolute, h));
        Controls.Add(root);
        root.Controls.Add(new Label { Dock = DockStyle.Fill, Text = "Kopfhörer: Windows spielt deine Programme lokal ab.\nStream: Nur markierte Programme werden als eigene Audioquellen in die gewählte OBS-Szene aufgenommen. Seitenwechsel ändern dieses Routing nicht." }, 0, 0);
        var local = new FlowLayoutPanel { Dock = DockStyle.Fill };
        local.Controls.Add(devices);
        local.Controls.Add(Button("Als Windows-Ausgabe", () => Run(() => { if (devices.SelectedItem is not OutputDevice d) throw new IOException("Kopfhörer auswählen."); Headphones.SetDefault(d.Id); settings.HeadphoneId = d.Id; save(); status.Text = "Windows-Standardausgabe gesetzt. Programme mit eigener Ausgabe im Windows-Mixer auf Standard/JBL stellen."; return Task.CompletedTask; })));
        local.Controls.Add(Button("Windows-Mixer", () => Process.Start(new ProcessStartInfo("ms-settings:apps-volume") { UseShellExecute = true })));
        root.Controls.Add(local, 0, 1);
        root.Controls.Add(new Label { Dock = DockStyle.Fill, Text = "OBS starten → Werkzeuge → WebSocket-Servereinstellungen → WebSocket-Server aktivieren.\nDie App liest Port und Passwort aus deiner lokalen OBS-Konfiguration; das Passwort wird nicht kopiert oder angezeigt.\nProgramme vor dem Suchen öffnen. Für Spiele im Vollbild ggf. kurz ins Fenstermodus wechseln." }, 0, 2);
        var obsBar = new FlowLayoutPanel { Dock = DockStyle.Fill };
        obsBar.Controls.Add(Button("OBS verbinden", () => Run(Connect)));
        obsBar.Controls.Add(scenes);
        obsBar.Controls.Add(Button("Programme / Geräte suchen", () => Run(() => { Scan(); return Task.CompletedTask; })));
        root.Controls.Add(obsBar, 0, 3);
        grid.DefaultCellStyle.ForeColor = Color.Black; grid.DefaultCellStyle.BackColor = Color.White;
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "program", HeaderText = "Programm", ReadOnly = true, FillWeight = 35 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "local", HeaderText = "Lokal", ReadOnly = true, FillWeight = 30 });
        grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "stream", HeaderText = "In OBS / Stream", FillWeight = 20 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "state", HeaderText = "Fenster", ReadOnly = true, FillWeight = 45 });
        grid.CurrentCellDirtyStateChanged += (_, _) => { if (grid.IsCurrentCellDirty) grid.CommitEdit(DataGridViewDataErrorContexts.Commit); };
        root.Controls.Add(grid, 0, 4); root.Controls.Add(isolate, 0, 5);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill };
        actions.Controls.Add(Button("Auswahl speichern", () => { Store(); status.Text = "Auswahl gespeichert; noch nicht in OBS angewendet."; }));
        actions.Controls.Add(Button("Routing in OBS anwenden", () => Run(Apply)));
        root.Controls.Add(actions, 0, 6); root.Controls.Add(status, 0, 7);
        Scan();
        FormClosing += (_, e) => { if (busy) { e.Cancel = true; return; } obs?.Dispose(); };
    }
    static Button Button(string text, Action click)
    {
        var b = new Button { Text = text, AutoSize = true, Height = 32, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(40, 49, 66), ForeColor = Color.WhiteSmoke };
        b.Click += (_, _) => click(); return b;
    }
    async void Run(Func<Task> action)
    {
        if (busy) return; busy = true; grid.Enabled = false;
        try { await action(); }
        catch (Exception ex) { status.Text = "Nicht abgeschlossen: " + ex.Message + " Bereits erfolgreiche Teilschritte können in OBS bestehen bleiben."; }
        finally { busy = false; if (!IsDisposed) grid.Enabled = true; }
    }
    void Store()
    {
        grid.EndEdit();
        foreach (DataGridViewRow row in grid.Rows) ((Route)row.Tag!).Stream = row.Cells["stream"].Value is true;
        save();
    }
    void Scan()
    {
        Store(); devices.Items.Clear(); devices.Items.AddRange(Headphones.List());
        devices.SelectedItem = devices.Items.Cast<OutputDevice>().FirstOrDefault(d => d.Id == settings.HeadphoneId)
            ?? devices.Items.Cast<OutputDevice>().FirstOrDefault(d => d.Name.Contains("JBL", StringComparison.OrdinalIgnoreCase));
        var open = new Dictionary<string, (string Window, string Title)>();
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            try
            {
                if (process.MainWindowHandle == IntPtr.Zero || string.IsNullOrWhiteSpace(process.MainWindowTitle) || process.ProcessName is "MidiMixer" or "obs64") continue;
                var cls = new StringBuilder(512); GetClassName(process.MainWindowHandle, cls, cls.Capacity);
                open.TryAdd(process.ProcessName, ($"{Encode(process.MainWindowTitle)}:{Encode(cls.ToString())}:{Encode(process.ProcessName + ".exe")}", process.MainWindowTitle));
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
        }
        foreach (var pair in open)
        {
            var route = settings.Routes.FirstOrDefault(r => r.Program == pair.Key);
            if (route == null) settings.Routes.Add(new Route { Program = pair.Key, Window = pair.Value.Window });
            else route.Window = pair.Value.Window;
        }
        grid.Rows.Clear();
        foreach (var route in settings.Routes.OrderBy(r => r.Program))
        {
            int index = grid.Rows.Add(route.Program, "Windows-Ausgabe", route.Stream, open.TryGetValue(route.Program, out var w) ? w.Title : "Nicht geöffnet");
            grid.Rows[index].Tag = route;
        }
        status.Text = devices.SelectedItem == null ? "Kein aktiver JBL-Ausgang gefunden. Kopfhörer verbinden und erneut suchen." : "JBL/Kopfhörer ausgewählt. Windows-Ausgabe setzen und OBS verbinden.";
    }
    internal static string Encode(string value) => value.Replace("#", "#22").Replace(":", "#3A");
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr handle, StringBuilder text, int length);
    async Task Connect()
    {
        obs?.Dispose(); obs = null; scenes.Items.Clear();
        string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "obs-studio", "plugin_config", "obs-websocket", "config.json");
        using var config = JsonDocument.Parse(File.ReadAllText(path)); var c = config.RootElement;
        if (!c.GetProperty("server_enabled").GetBoolean()) throw new IOException("In OBS zuerst den WebSocket-Server aktivieren (Werkzeuge → WebSocket-Servereinstellungen).");
        var client = new ObsClient();
        try
        {
            await client.Connect(c.GetProperty("server_port").GetInt32(), c.TryGetProperty("server_password", out var p) ? p.GetString() ?? "" : "");
            var list = await client.Request("GetSceneList");
            foreach (var scene in list.GetProperty("scenes").EnumerateArray()) scenes.Items.Add(scene.GetProperty("sceneName").GetString()!);
            scenes.SelectedItem = list.GetProperty("currentProgramSceneName").GetString();
            obs = client; status.Text = "OBS verbunden. Szene wählen, Programme markieren und Routing anwenden.";
        }
        catch { client.Dispose(); throw; }
    }
    async Task Apply()
    {
        Store();
        if (obs == null || scenes.SelectedItem is not string scene) throw new IOException("Zuerst OBS verbinden und Szene auswählen.");
        var inputs = (await obs.Request("GetInputList")).GetProperty("inputs").EnumerateArray().Select(e => (Name: e.GetProperty("inputName").GetString()!, Kind: e.GetProperty("inputKind").GetString()!)).ToList();
        var kinds = (await obs.Request("GetInputKindList", new { unversioned = true })).GetProperty("inputKinds").EnumerateArray().Select(e => e.GetString()).ToArray();
        if (!kinds.Contains("wasapi_process_output_capture")) throw new IOException("Diese OBS-/Windows-Version unterstützt keine Programm-Audioaufnahme.");
        // Reject collisions before changing any source.
        foreach (var route in settings.Routes)
            if (inputs.Any(i => i.Name == Prefix + route.Program && i.Kind != "wasapi_process_output_capture")) throw new IOException("Quellname bereits anderweitig belegt: " + Prefix + route.Program);
        if (isolate.Checked)
            foreach (var input in inputs.Where(i => i.Kind is "wasapi_output_capture" or "wasapi_process_output_capture" && !i.Name.StartsWith(Prefix, StringComparison.Ordinal)))
                await obs.Request("SetInputMute", new { inputName = input.Name, inputMuted = true });
        var sceneItems = (await obs.Request("GetSceneItemList", new { sceneName = scene })).GetProperty("sceneItems").EnumerateArray().Select(e => (Name: e.GetProperty("sourceName").GetString(), Id: e.GetProperty("sceneItemId").GetInt32())).ToList();
        foreach (var route in settings.Routes)
        {
            string name = Prefix + route.Program;
            bool exists = inputs.Any(i => i.Name == name);
            if (!exists && !route.Stream) continue;
            var capture = new { window = route.Window, priority = 2 };
            if (!exists)
                await obs.Request("CreateInput", new { sceneName = scene, inputName = name, inputKind = "wasapi_process_output_capture", inputSettings = capture, sceneItemEnabled = true });
            else if (route.Stream)
            {
                await obs.Request("SetInputSettings", new { inputName = name, inputSettings = capture, overlay = true });
                var items = sceneItems.Where(i => i.Name == name).ToArray();
                if (items.Length == 0) await obs.Request("CreateSceneItem", new { sceneName = scene, sourceName = name, sceneItemEnabled = true });
                else foreach (var item in items) await obs.Request("SetSceneItemEnabled", new { sceneName = scene, sceneItemId = item.Id, sceneItemEnabled = true });
            }
            await obs.Request("SetInputMute", new { inputName = name, inputMuted = !route.Stream });
            await obs.Request("SetInputAudioMonitorType", new { inputName = name, monitorType = "OBS_MONITORING_TYPE_NONE" });
        }
        status.Text = $"Routing in „{scene}“ angewendet. In OBS die Pegel prüfen. Mikrofone bleiben erhalten. Zusätzliche Browser-/Medien-/Spielaufnahmequellen können weiterhin eigenes Audio liefern.";
    }
}
