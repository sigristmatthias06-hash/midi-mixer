using NAudio.Midi;

namespace MidiMixer;

sealed class PadSettings
{
    public string Output { get; set; } = "";
    public int[] Colors { get; set; } = new int[64];
    public int[] Buttons { get; set; } = new int[16];
    public int Brightness { get; set; } = 6;
}

sealed class PadForm : Form
{
    // Akai Communications Protocol v1.0, palette indices 0–127.
    static readonly string[] Palette = ("000000 1E1E1E 7F7F7F FFFFFF FF4C4C FF0000 590000 190000 FFBD6C FF5400 591D00 271B00 FFFF4C FFFF00 595900 191900 " +
        "88FF4C 54FF00 1D5900 142B00 4CFF4C 00FF00 005900 001900 4CFF5E 00FF19 00590D 001902 4CFF88 00FF55 00591D 001F12 " +
        "4CFFB7 00FF99 005935 001912 4CC3FF 00A9FF 004152 001019 4C88FF 0055FF 001D59 000819 4C4CFF 0000FF 000059 000019 " +
        "874CFF 5400FF 190064 0F0030 FF4CFF FF00FF 590059 190019 FF4C87 FF0054 59001D 220013 FF1500 993500 795100 436400 " +
        "033900 005735 00547F 0000FF 00454F 2500CC 7F7F7F 202020 FF0000 BDFF2D AFED06 64FF09 108B00 00FF87 00A9FF 002AFF " +
        "3F00FF 7A00FF B21A7D 402100 FF4A00 88E106 72FF15 00FF00 3BFF26 59FF71 38FFCC 5B8AFF 3151C6 877FE9 D31DFF FF005D " +
        "FF7F00 B9B000 90FF00 835D07 392b00 144C10 0D5038 15152A 16205A 693C1C A8000A DE513D D86A1C FFE126 9EE12F 67B50F " +
        "1E1E30 DCFF6B 80FFBD 9A99FF 8E66FF 404040 757575 E0FFFF A00000 350000 1AD000 074200 B9B000 3F3100 B35F00 4B1502").Split(' ');
    readonly PadSettings settings;
    readonly Action save;
    readonly ComboBox outputs = new() { Width = 245, DropDownStyle = ComboBoxStyle.DropDownList };
    readonly Label status = new() { Dock = DockStyle.Fill, AutoEllipsis = true };
    readonly Label brushLabel = new() { AutoSize = true, Padding = new Padding(5) };
    readonly Button[] pads = new Button[64];
    readonly Button[] sideButtons = new Button[16];
    readonly Button[] swatches = new Button[128];
    readonly LedOutput ledOutput;
    int brush = 5;
    int selected;

    public PadForm(PadSettings settings, Action save, LedOutput ledOutput)
    {
        this.settings = settings; this.save = save; this.ledOutput = ledOutput;
        if (settings.Colors == null || settings.Colors.Length != 64) settings.Colors = new int[64];
        if (settings.Buttons == null || settings.Buttons.Length != 16) settings.Buttons = new int[16];
        for (int i = 0; i < 64; i++) settings.Colors[i] = Math.Clamp(settings.Colors[i], 0, 127);
        for (int i = 0; i < 16; i++) settings.Buttons[i] = Math.Clamp(settings.Buttons[i], 0, 2);
        settings.Brightness = Math.Clamp(settings.Brightness, 0, 6);
        Text = "APC mini mk2 · Button-Farben";
        Font = new Font("Segoe UI", 10);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(1040, 780); MinimumSize = new Size(1000, 760);
        BackColor = Color.FromArgb(19, 23, 32); ForeColor = Color.WhiteSmoke;
        StartPosition = FormStartPosition.CenterParent;
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16), RowCount = 6, ColumnCount = 1 };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        foreach (var h in new[] { 45, 45, 240 }) root.RowStyles.Add(new RowStyle(SizeType.Absolute, h));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        Controls.Add(root);
        var connection = new FlowLayoutPanel { Dock = DockStyle.Fill };
        connection.Controls.Add(new Label { Text = "MIDI-Ausgang", AutoSize = true, Padding = new Padding(0, 6, 4, 0) });
        connection.Controls.Add(outputs);
        connection.Controls.Add(ActionButton("Geräte suchen", Scan));
        connection.Controls.Add(ActionButton("Verbinden && senden", Connect));
        connection.Controls.Add(ActionButton("Trennen", () => { Disconnect(); status.Text = "Getrennt · Änderungen werden nur gespeichert."; }));
        root.Controls.Add(connection, 0, 0);
        var options = new FlowLayoutPanel { Dock = DockStyle.Fill };
        options.Controls.Add(new Label { Text = "Helligkeit", AutoSize = true, Padding = new Padding(0, 6, 4, 0) });
        var brightness = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 90 };
        brightness.Items.AddRange(new object[] { "10 %", "25 %", "50 %", "65 %", "75 %", "90 %", "100 %" });
        brightness.SelectedIndex = settings.Brightness;
        brightness.SelectedIndexChanged += (_, _) => { settings.Brightness = brightness.SelectedIndex; Commit(Enumerable.Range(0, 64)); };
        options.Controls.Add(brightness);
        options.Controls.Add(brushLabel);
        options.Controls.Add(new Label { Text = "1. Farbe wählen   2. Pads anklicken", AutoSize = true, Padding = new Padding(12, 6, 0, 0) });
        root.Controls.Add(options, 0, 1);
        var palette = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 16, RowCount = 8 };
        for (int i = 0; i < 16; i++) palette.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 6.25f));
        for (int i = 0; i < 8; i++) palette.RowStyles.Add(new RowStyle(SizeType.Percent, 12.5f));
        for (int i = 0; i < 128; i++)
        {
            int color = i;
            var b = ColorButton(i); b.Text = i == 0 ? "Aus" : i.ToString(); b.Margin = new Padding(2);
            b.Font = new Font("Segoe UI", 8);
            b.Click += (_, _) => { brush = color; UpdateBrush(); };
            swatches[i] = b; palette.Controls.Add(b, i % 16, i / 16);
        }
        root.Controls.Add(palette, 0, 2);
        var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 9, RowCount = 9, Padding = new Padding(0, 8, 0, 0) };
        for (int i = 0; i < 9; i++) { grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / 9)); grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100f / 9)); }
        for (int row = 0; row < 8; row++)
            for (int col = 0; col < 8; col++)
            {
                int note = NoteAt(row, col);
                var b = ColorButton(settings.Colors[note]);
                b.Text = $"{row + 1} · {col + 1}";
                b.Click += (_, _) => { if (note == 63) { status.Text = "Pad oben rechts: Stream-Stumm. Grün = bereit, Rot = stumm; blinkend = wird angewendet."; return; } selected = note; settings.Colors[note] = brush; Commit(new[] { note }); };
                pads[note] = b; grid.Controls.Add(b, col, row);
            }
        for (int i = 0; i < 16; i++)
        {
            int index = i;
            var b = ColorButton(0); sideButtons[i] = b;
            b.Click += (_, _) => { status.Text = "Track-LEDs zeigen Stumm; die grüne Szenen-LED zeigt die aktive Mixer-Seite."; };
            grid.Controls.Add(b, i < 8 ? i : 8, i < 8 ? 8 : i - 8);
        }
        root.Controls.Add(grid, 0, 3);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill };
        actions.Controls.Add(ActionButton("Reihe färben", () => PaintPads(Enumerable.Range(selected / 8 * 8, 8))));
        actions.Controls.Add(ActionButton("Spalte färben", () => PaintPads(Enumerable.Range(0, 8).Select(r => r * 8 + selected % 8))));
        actions.Controls.Add(ActionButton("Alle Pads färben", () => PaintPads(Enumerable.Range(0, 64))));
        actions.Controls.Add(ActionButton("Alle Pads aus", () => { Array.Clear(settings.Colors); Commit(Enumerable.Range(0, 64)); }));
        actions.Controls.Add(ActionButton("Erneut senden", SendAll));
        root.Controls.Add(actions, 0, 4);
        root.Controls.Add(status, 0, 5);
        UpdateBrush(); RefreshPads(); Scan();
        // The mixer owns the output; closing this window keeps mute feedback active.
    }
    static Button ActionButton(string text, Action action)
    {
        var b = new Button { Text = text, AutoSize = true, Height = 32, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(40, 49, 66), ForeColor = Color.WhiteSmoke };
        b.Click += (_, _) => action(); return b;
    }
    static Color PaletteColor(int index) => ColorTranslator.FromHtml("#" + Palette[index]);
    static Button ColorButton(int index)
    {
        var b = new Button { Dock = DockStyle.Fill, FlatStyle = FlatStyle.Flat, Margin = new Padding(3), AutoSize = false, UseVisualStyleBackColor = false };
        SetColor(b, index); return b;
    }
    static void SetColor(Button b, int index)
    {
        b.BackColor = PaletteColor(index);
        var c = b.BackColor;
        b.ForeColor = (c.R * .299 + c.G * .587 + c.B * .114) > 145 ? Color.Black : Color.White;
    }
    static int NoteAt(int row, int col) => (7 - row) * 8 + col;
    static int ButtonNote(int index) => index < 8 ? 100 + index : 112 + index - 8;
    static int Message(int note, int value, int brightness) =>
        (note < 64 ? 0x90 + brightness : 0x90) | (note << 8) | (value << 16);
    void UpdateBrush()
    {
        brushLabel.Text = brush == 0 ? "Farbe: Aus" : $"Farbe: {brush}";
        for (int i = 0; i < swatches.Length; i++)
        { swatches[i].FlatAppearance.BorderSize = i == brush ? 3 : 1; swatches[i].FlatAppearance.BorderColor = i == brush ? Color.Cyan : Color.DimGray; }
    }
    internal void RefreshPads()
    {
        for (int i = 0; i < 64; i++)
        { SetColor(pads[i], settings.Colors[i]); pads[i].FlatAppearance.BorderSize = i == selected ? 3 : 1; pads[i].FlatAppearance.BorderColor = i == selected ? Color.Cyan : Color.DimGray; }
        pads[63].Text = "Stream Mute"; SetColor(pads[63], !ledOutput.StreamConnected ? 0 : ledOutput.StreamMuted ? 5 : 21);
        for (int i = 0; i < 16; i++)
        {
            int state = i < 8 ? (ledOutput.IsMuted(i) ? 1 : 0) : (ledOutput.ActivePage == i - 8 ? 1 : 0);
            SetColor(sideButtons[i], state == 0 ? 0 : i < 8 ? 5 : 21);
            sideButtons[i].Text = $"{(i < 8 ? "Track" : "Szene")} {i % 8 + 1}: {new[] { "Aus", "An", "Blinkt" }[state]}";
        }
    }
    void PaintPads(IEnumerable<int> notes)
    {
        var array = notes.ToArray(); foreach (int note in array) settings.Colors[note] = brush; Commit(array);
    }
    void Commit(IEnumerable<int> notes)
    {
        save(); RefreshPads();
        if (!ledOutput.Connected) { status.Text = "Gespeichert · Zum Übertragen MIDI-Ausgang verbinden. Track: nur Rot, Szene: nur Grün."; return; }
        Send(notes);
    }
    void Send(IEnumerable<int> notes)
    {
        if (!ledOutput.Connected) { status.Text = "Bitte zuerst den MIDI-Ausgang verbinden."; return; }
        try
        {
            foreach (var note in notes)
            {
                int value = note < 64 ? settings.Colors[note] : settings.Buttons[note < 108 ? note - 100 : note - 112 + 8];
                ledOutput.Send(Message(note, value, settings.Brightness));
            }
            status.Text = "Farben gesendet · Track: Rot = stumm. Szene: Grün = aktive Seite. Pad oben rechts = Stream-Stumm.";
        }
        catch (Exception ex) { Disconnect(); status.Text = "Senden fehlgeschlagen: " + ex.Message; }
    }
    void SendAll() => Send(Enumerable.Range(0, 64).Concat(Enumerable.Range(0, 16).Select(ButtonNote)));
    void Scan()
    {
        try
        {
            outputs.Items.Clear();
            for (int i = 0; i < MidiOut.NumberOfDevices; i++) outputs.Items.Add(MidiOut.DeviceInfo(i).ProductName);
            var preferred = outputs.Items.Cast<string>().FirstOrDefault(n => n == settings.Output)
                ?? outputs.Items.Cast<string>().FirstOrDefault(n => n.Equals("APC mini mk2", StringComparison.OrdinalIgnoreCase));
            if (preferred != null) outputs.SelectedItem = preferred;
            status.Text = "Ausgang „APC mini mk2“ wählen · Farben wählen und Pads anklicken · Track: Rot, Szene: Grün.";
        }
        catch (Exception ex) { status.Text = "MIDI-Ausgänge nicht verfügbar: " + ex.Message; }
    }
    void Connect()
    {
        Disconnect();
        if (outputs.SelectedIndex < 0) { status.Text = "Bitte einen MIDI-Ausgang auswählen."; return; }
        try { ledOutput.Connect(outputs.SelectedIndex); settings.Output = outputs.Text; save(); SendAll(); }
        catch (Exception ex) { Disconnect(); status.Text = "Ausgang konnte nicht geöffnet werden: " + ex.Message; }
    }
    void Disconnect() => ledOutput.Disconnect();
    internal static void VerifyProtocol()
    {
        if (Palette.Length != 128 || NoteAt(0, 0) != 56 || NoteAt(7, 0) != 0 || NoteAt(0, 7) != 63
            || Message(0, 5, 6) != 0x050096 || Message(100, 2, 6) != 0x026490
            || ButtonNote(15) != 119 || Message(63, 0, 0) != 0x003F90)
            throw new InvalidOperationException("APC LED protocol test failed");
        var restored = System.Text.Json.JsonSerializer.Deserialize<Settings>("{\"MidiDevice\":\"APC mini mk2\"}")!;
        if (restored.Pads.Colors.Length != 64) throw new InvalidOperationException("Settings migration failed");
    }
}
