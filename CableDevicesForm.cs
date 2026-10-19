using System.Diagnostics;
using System.Text;
using NAudio.CoreAudioApi;

namespace MidiMixer;

// Device availability is deliberately separate from driver installation: an installed
// driver can still have disabled, disconnected or renamed endpoints.
sealed class CableDevicesForm : Form
{
    readonly TextBox report = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, Dock = DockStyle.Fill, Font = new Font("Consolas", 10) };
    public CableDevicesForm()
    {
        Text = "Virtuelle Kabel · Geräteprüfung";
        ClientSize = new Size(920, 590); MinimumSize = new Size(700, 450);
        AutoScaleMode = AutoScaleMode.Dpi; StartPosition = FormStartPosition.CenterParent;
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), RowCount = 3, ColumnCount = 1 };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 65));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 45));
        root.Controls.Add(new Label { Dock = DockStyle.Fill, Text = "Windows-Geräte für vier Programme und einen Stream-Ausgang prüfen.\nDie App erzeugt keine Treibergeräte. VB-CABLE sowie A+B und C+D werden separat beim Hersteller bezogen.\nNach einer Installation gegebenenfalls Windows neu starten und anschließend aktualisieren." }, 0, 0);
        root.Controls.Add(report, 0, 1);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill };
        void Add(string title, Action action)
        {
            var button = new Button { Text = title, AutoSize = true, Height = 32 };
            button.Click += (_, _) => { try { action(); } catch (Exception ex) { MessageBox.Show(this, ex.Message, Text); } };
            actions.Controls.Add(button);
        }
        Add("Aktualisieren", RefreshDevices);
        Add("VB-Audio-Webseite", () => Process.Start(new ProcessStartInfo("https://vb-audio.com/Cable/") { UseShellExecute = true }));
        Add("Windows-Sound", () => Process.Start(new ProcessStartInfo("ms-settings:sound") { UseShellExecute = true }));
        Add("Schließen", Close);
        root.Controls.Add(actions, 0, 2); Controls.Add(root); RefreshDevices();
        Shown += (_, _) => report.Select(0, 0);
    }
    void RefreshDevices()
    {
        try { report.Text = BuildReport(); }
        catch (Exception ex) { report.Text = "Geräteprüfung fehlgeschlagen: " + ex.Message; }
    }
    internal static string BuildReport()
    {
        using var enumerator = new MMDeviceEnumerator();
        var devices = new List<(string Name, DataFlow Flow, DeviceState State)>();
        foreach (var d in enumerator.EnumerateAudioEndPoints(DataFlow.All, DeviceState.All))
            using (d) devices.Add((d.FriendlyName, d.DataFlow, d.State));
        var text = new StringBuilder("KABELPAARE (Standardnamen des Herstellers)\r\n\r\n");
        foreach (var (cable, purpose) in new[] { ("CABLE", "Stream → OBS"), ("CABLE-A", "Musik"), ("CABLE-B", "Spiel"), ("CABLE-C", "Discord"), ("CABLE-D", "Sonstiges") })
        {
            text.AppendLine($"{cable} · {purpose}");
            foreach (var flow in new[] { DataFlow.Render, DataFlow.Capture })
            {
                string name = cable + (flow == DataFlow.Render ? " Input" : " Output");
                var matches = devices.Where(d => d.Flow == flow && (d.Name.Equals(name, StringComparison.OrdinalIgnoreCase) || d.Name.StartsWith(name + " (", StringComparison.OrdinalIgnoreCase))).ToArray();
                text.AppendLine($"  {name,-16} {(matches.Length == 0 ? "Nicht erkannt" : string.Join("; ", matches.Select(d => StateText(d.State))))}");
            }
            text.AppendLine();
        }
        var jbl = devices.Where(d => d.Flow == DataFlow.Render && d.Name.Contains("JBL", StringComparison.OrdinalIgnoreCase)).ToArray();
        text.AppendLine("JBL-KOPFHÖRER");
        text.AppendLine(jbl.Length == 0 ? "  Nicht erkannt" : string.Join("\r\n", jbl.Select(d => $"  {d.Name}: {StateText(d.State)}")));
        text.AppendLine("\r\nSTANDARD-AUSGÄNGE");
        foreach (var role in new[] { Role.Console, Role.Multimedia, Role.Communications })
        {
            try { using var d = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, role); text.AppendLine($"  {role}: {d.FriendlyName}"); }
            catch { text.AppendLine($"  {role}: Nicht verfügbar"); }
        }
        text.AppendLine("\r\nALLE WINDOWS-AUDIOGERÄTE (auch deaktivierte/getrennte)");
        foreach (var d in devices.OrderBy(d => d.Flow).ThenBy(d => d.Name))
            text.AppendLine($"  {(d.Flow == DataFlow.Render ? "Ausgabe" : "Aufnahme")}: {d.Name} [{StateText(d.State)}]");
        text.AppendLine("\r\nNicht erkannt bedeutet nicht zwingend: Treiber fehlt. Geräte können umbenannt sein.\r\nAktiv bedeutet: Windows stellt das Gerät bereit; der Audiofluss wurde nicht getestet.\r\nOBS benötigt CABLE Output als Audioeingabeaufnahme.\r\nDiese Prüfung ändert keine Standardgeräte, Zuordnungen oder Lautstärken.");
        return text.ToString();
    }
    static string StateText(DeviceState state) => state switch
    {
        DeviceState.Active => "Aktiv", DeviceState.Disabled => "Deaktiviert",
        DeviceState.Unplugged => "Getrennt", DeviceState.NotPresent => "Nicht angeschlossen",
        _ => state.ToString()
    };
}
