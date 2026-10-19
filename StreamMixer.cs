using System.Text.Json;

namespace MidiMixer;

// A single serial request pump. Fader changes replace older pending values.
sealed class StreamMixer : IDisposable
{
    public const string KeyPrefix = "OBS: ";
    public const string SourcePrefix = "MIDI Mixer · ";
    public Dictionary<string, Level> Levels { get; } = new();
    public string Status { get; private set; } = "OBS nicht verbunden";
    public bool Connected => client != null;
    public bool Panic { get; private set; }
    public bool PanicApplied => Panic && Connected && Levels.Count > 0 && Levels.Values.All(v => v.Mute) && mutes.Count == 0;
    public event Action? Changed;
    readonly Dictionary<string, float> volumes = new();
    readonly Dictionary<string, bool> mutes = new();
    readonly Dictionary<string, bool> beforePanic = new();
    readonly System.Windows.Forms.Timer timer = new() { Interval = 50 };
    readonly string sourcePrefix;
    ObsClient? client;
    ObsMeters? meters;
    public float Peak(string key) => meters?.Peak(key) ?? 0;
    bool busy, disposed;
    DateTime nextConnect, nextRead;
    public StreamMixer(string sourcePrefix = SourcePrefix) { this.sourcePrefix = sourcePrefix; timer.Tick += async (_, _) => await Pump(); }
    internal Task PollForTest() { nextRead = DateTime.MinValue; return Pump(); }
    public void Start() => timer.Start();
    public void SetVolume(string key, float value) { if (Connected && Levels.ContainsKey(key)) volumes[key] = Math.Clamp(value, 0, 1); }
    public void SetMute(string key, bool value) { if (Connected && Levels.ContainsKey(key)) mutes[key] = Panic ? true : value; }
    public void TogglePanic()
    {
        if (!Connected || Levels.Count == 0 && !Panic) return;
        Panic = !Panic;
        if (Panic)
        {
            beforePanic.Clear();
            foreach (var pair in Levels) { beforePanic[pair.Key] = pair.Value.Mute; mutes[pair.Key] = true; }
        }
        else
        {
            foreach (var pair in beforePanic) if (Levels.ContainsKey(pair.Key)) mutes[pair.Key] = pair.Value;
            beforePanic.Clear();
        }
        Changed?.Invoke();
    }
    async Task Pump()
    {
        if (busy || disposed) return;
        busy = true;
        try
        {
            if (client == null)
            {
                if (DateTime.UtcNow < nextConnect) return;
                nextConnect = DateTime.UtcNow.AddSeconds(5);
                string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "obs-studio", "plugin_config", "obs-websocket", "config.json");
                using var config = JsonDocument.Parse(File.ReadAllText(path)); var c = config.RootElement;
                if (!c.GetProperty("server_enabled").GetBoolean()) { Status = "OBS-WebSocket deaktiviert"; return; }
                var candidate = new ObsClient();
                try { await candidate.Connect(c.GetProperty("server_port").GetInt32(), c.GetProperty("server_password").GetString() ?? ""); }
                catch { candidate.Dispose(); throw; }
                if (disposed) { candidate.Dispose(); return; }
                client = candidate; nextRead = DateTime.MinValue;
                meters?.Dispose(); meters = new ObsMeters();
                try { await meters.Start(c.GetProperty("server_port").GetInt32(), c.GetProperty("server_password").GetString() ?? ""); }
                catch { meters.Dispose(); meters = null; }
            }
            foreach (var key in volumes.Keys.ToArray())
            {
                float value = volumes[key]; volumes.Remove(key);
                await client.Request("SetInputVolume", new { inputName = key[KeyPrefix.Length..], inputVolumeMul = value });
                if (Levels.TryGetValue(key, out var level)) Levels[key] = level with { Volume = value };
            }
            foreach (var key in mutes.Keys.ToArray())
            {
                bool value = mutes[key]; mutes.Remove(key);
                await client.Request("SetInputMute", new { inputName = key[KeyPrefix.Length..], inputMuted = value });
                if (Levels.TryGetValue(key, out var level)) Levels[key] = level with { Mute = value };
            }
            if (DateTime.UtcNow >= nextRead)
            {
                nextRead = DateTime.UtcNow.AddSeconds(1);
                var inputs = (await client.Request("GetInputList")).GetProperty("inputs").EnumerateArray()
                    .Where(e => e.GetProperty("inputKind").GetString() == "wasapi_process_output_capture")
                    .Select(e => e.GetProperty("inputName").GetString()!).Where(n => n.StartsWith(sourcePrefix, StringComparison.Ordinal)).ToArray();
                var fresh = new Dictionary<string, Level>();
                foreach (var name in inputs)
                {
                    var volume = await client.Request("GetInputVolume", new { inputName = name });
                    var mute = await client.Request("GetInputMute", new { inputName = name });
                    string key = KeyPrefix + name;
                    bool isMuted = mute.GetProperty("inputMuted").GetBoolean();
                    if (Panic && !isMuted) { beforePanic.TryAdd(key, isMuted); await client.Request("SetInputMute", new { inputName = name, inputMuted = true }); isMuted = true; }
                    fresh[key] = new(Math.Clamp(volume.GetProperty("inputVolumeMul").GetSingle(), 0, 1), isMuted);
                }
                Levels.Clear(); foreach (var p in fresh) Levels[p.Key] = p.Value;
                Status = "OBS verbunden";
                Changed?.Invoke();
            }
        }
        catch (Exception ex)
        {
            client?.Dispose(); client = null; meters?.Dispose(); meters = null; Levels.Clear(); volumes.Clear(); mutes.Clear();
            Status = "OBS getrennt – neuer Versuch in 5 s: " + ex.Message;
            nextConnect = DateTime.UtcNow.AddSeconds(5);
            if (!disposed) Changed?.Invoke();
        }
        finally { busy = false; }
    }
    public void Dispose() { disposed = true; timer.Stop(); timer.Dispose(); meters?.Dispose(); client?.Dispose(); client = null; }
}
