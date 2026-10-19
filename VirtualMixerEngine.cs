using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace MidiMixer;

sealed class VirtualChannel
{
    public string DeviceId { get; set; } = "";
    public bool Loopback { get; set; }
    public float Local { get; set; } = .5f;
    public float Stream { get; set; } = .5f;
    public bool LocalMute { get; set; }
    public bool StreamMute { get; set; } = true;
}
sealed class VirtualMixerSettings
{
    public VirtualChannel[] Channels { get; set; } = Enumerable.Range(0, 4).Select(_ => new VirtualChannel()).ToArray();
    public string LocalDevice { get; set; } = "";
    public string StreamDevice { get; set; } = "";
    public float LocalMaster { get; set; } = .5f;
    public float StreamMaster { get; set; } = .5f;
    public bool LocalMute { get; set; }
    public bool StreamMute { get; set; }
}
record MixerEndpoint(string Id, string Name, bool Loopback)
{
    public override string ToString() => (Loopback ? "Wiedergabe abgreifen: " : "Aufnahme: ") + Name;
}
sealed class GainStage(ISampleProvider source, bool clip = false) : ISampleProvider
{
    public volatile float Gain = 1;
    public volatile bool Mute;
    public volatile float Peak;
    public long ClippedSamples;
    public WaveFormat WaveFormat => source.WaveFormat;
    public int Read(float[] buffer, int offset, int count)
    {
        int read = source.Read(buffer, offset, count); float gain = Mute ? 0 : Gain, peak = 0;
        for (int i = offset; i < offset + read; i++)
        {
            float value = buffer[i] * gain;
            if (!float.IsFinite(value)) value = 0;
            peak = Math.Max(peak, Math.Abs(value));
            if (clip && Math.Abs(value) > 1) { Interlocked.Increment(ref ClippedSamples); value = Math.Clamp(value, -1, 1); }
            buffer[i] = value;
        }
        Peak = peak; return read;
    }
}
sealed class CaptureSplit
{
    readonly BufferedWaveProvider[] buffers;
    public GainStage Local { get; }
    public GainStage Stream { get; }
    long overflows;
    public long Overflows => Interlocked.Read(ref overflows);
    public CaptureSplit(WaveFormat format)
    {
        if (format.Channels is not (1 or 2)) throw new NotSupportedException("Der Prototyp unterstützt Mono- und Stereo-Eingänge.");
        buffers = Enumerable.Range(0, 2).Select(_ => new BufferedWaveProvider(format) { BufferDuration = TimeSpan.FromMilliseconds(300), ReadFully = true, DiscardOnBufferOverflow = true }).ToArray();
        Local = new GainStage(Convert(buffers[0])); Stream = new GainStage(Convert(buffers[1]));
    }
    static ISampleProvider Convert(BufferedWaveProvider buffer)
    {
        ISampleProvider samples = buffer.ToSampleProvider();
        if (samples.WaveFormat.Channels == 1) samples = new MonoToStereoSampleProvider(samples);
        if (samples.WaveFormat.SampleRate != 48000) samples = new WdlResamplingSampleProvider(samples, 48000);
        return samples;
    }
    public void Push(byte[] bytes, int count)
    {
        foreach (var buffer in buffers)
        {
            if (buffer.BufferedBytes + count > buffer.BufferLength)
            { buffer.ClearBuffer(); Interlocked.Increment(ref overflows); }
            buffer.AddSamples(bytes, 0, count);
        }
    }
}

sealed class VirtualMixerEngine : IDisposable
{
    public static readonly string[] Names = { "Musik", "Spiel", "Discord", "Sonstiges" };
    public static string Key(int channel, bool stream) => $"Mix: {(stream ? "Stream" : "Kopfhörer")} · {(channel == 4 ? "Master" : Names[channel])}";
    readonly List<MMDevice> devices = new();
    readonly List<WasapiCapture> captures = new();
    readonly List<WasapiOut> players = new();
    readonly CaptureSplit?[] splits = new CaptureSplit?[4];
    GainStage? localMaster, streamMaster;
    public bool Running { get; private set; }
    public string? Failure;
    public VirtualMixerSettings Settings { get; }
    public VirtualMixerEngine(VirtualMixerSettings settings) => Settings = settings;
    public static MixerEndpoint[] Inputs()
    {
        using var e = new MMDeviceEnumerator(); var result = new List<MixerEndpoint>();
        foreach (var flow in new[] { DataFlow.Capture, DataFlow.Render })
            foreach (var device in e.EnumerateAudioEndPoints(flow, DeviceState.Active))
                using (device) result.Add(new(device.ID, device.FriendlyName, flow == DataFlow.Render));
        return result.ToArray();
    }
    public void Start()
    {
        Stop(); Failure = null;
        if (string.IsNullOrEmpty(Settings.LocalDevice) || string.IsNullOrEmpty(Settings.StreamDevice)) throw new IOException("Zwei Ausgänge wählen: Kopfhörer und einen getrennten Stream-Ausgang.");
        if (Settings.LocalDevice == Settings.StreamDevice) throw new IOException("Kopfhörer und Stream benötigen unterschiedliche Ausgänge.");
        var assigned = Settings.Channels.Where(c => !string.IsNullOrEmpty(c.DeviceId)).ToArray();
        if (assigned.Length == 0) throw new IOException("Mindestens einen Eingang wählen.");
        if (assigned.Select(c => c.DeviceId).Distinct().Count() != assigned.Length) throw new IOException("Ein Gerät darf nur einem Eingang zugeordnet sein.");
        if (assigned.Any(c => c.Loopback && (c.DeviceId == Settings.LocalDevice || c.DeviceId == Settings.StreamDevice))) throw new IOException("Ein Ausgang darf nicht gleichzeitig als Loopback-Eingang dienen (Rückkopplung).");
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            var localMix = new MixingSampleProvider(WaveFormat.CreateIeeeFloatWaveFormat(48000, 2)) { ReadFully = true };
            var streamMix = new MixingSampleProvider(WaveFormat.CreateIeeeFloatWaveFormat(48000, 2)) { ReadFully = true };
            for (int i = 0; i < 4; i++)
            {
                var config = Settings.Channels[i]; if (string.IsNullOrEmpty(config.DeviceId)) continue;
                var device = enumerator.GetDevice(config.DeviceId); devices.Add(device);
                if (device.State != DeviceState.Active || device.DataFlow != (config.Loopback ? DataFlow.Render : DataFlow.Capture)) throw new IOException("Eingang nicht mehr verfügbar: " + Names[i]);
                WasapiCapture capture = config.Loopback ? new WasapiLoopbackCapture(device) : new WasapiCapture(device);
                captures.Add(capture);
                var split = new CaptureSplit(capture.WaveFormat); splits[i] = split;
                localMix.AddMixerInput(split.Local); streamMix.AddMixerInput(split.Stream);
                capture.DataAvailable += (_, e) => split.Push(e.Buffer, e.BytesRecorded);
                capture.RecordingStopped += (_, e) => { if (e.Exception != null) Failure = "Eingang getrennt: " + e.Exception.Message; };
            }
            localMaster = new GainStage(localMix, true); streamMaster = new GainStage(streamMix, true); UpdateGains();
            foreach (var (id, source) in new[] { (Settings.LocalDevice, localMaster), (Settings.StreamDevice, streamMaster) })
            {
                var device = enumerator.GetDevice(id); devices.Add(device);
                if (device.State != DeviceState.Active || device.DataFlow != DataFlow.Render) throw new IOException("Ausgang ist nicht verfügbar.");
                // VB-CABLE input/output names differ only by their direction; they form one cable.
                static string CableName(string name) => name.Replace("Input", "", StringComparison.OrdinalIgnoreCase).Replace("Output", "", StringComparison.OrdinalIgnoreCase).Trim();
                if (device.FriendlyName.Contains("CABLE", StringComparison.OrdinalIgnoreCase) && devices.Any(d => d.DataFlow == DataFlow.Capture && CableName(d.FriendlyName).Equals(CableName(device.FriendlyName), StringComparison.OrdinalIgnoreCase)))
                    throw new IOException("Ein VB-CABLE darf nicht zugleich Eingang und Ausgang der Engine sein.");
                var player = new WasapiOut(device, AudioClientShareMode.Shared, true, 60); players.Add(player);
                player.Init(source.ToWaveProvider());
                player.PlaybackStopped += (_, e) => { if (e.Exception != null) Failure = "Ausgang getrennt: " + e.Exception.Message; };
            }
            Running = true;
            foreach (var player in players) player.Play();
            foreach (var capture in captures) capture.StartRecording();
        }
        catch { Stop(); throw; }
    }
    public void UpdateGains()
    {
        for (int i = 0; i < 4; i++) if (splits[i] is { } split)
        {
            var c = Settings.Channels[i]; split.Local.Gain = Math.Clamp(c.Local, 0, 1); split.Local.Mute = c.LocalMute;
            split.Stream.Gain = Math.Clamp(c.Stream, 0, 1); split.Stream.Mute = c.StreamMute;
        }
        if (localMaster != null) { localMaster.Gain = Math.Clamp(Settings.LocalMaster, 0, 1); localMaster.Mute = Settings.LocalMute; }
        if (streamMaster != null) { streamMaster.Gain = Math.Clamp(Settings.StreamMaster, 0, 1); streamMaster.Mute = Settings.StreamMute; }
    }
    public Dictionary<string, Level> Levels()
    {
        var result = new Dictionary<string, Level>(); if (!Running) return result;
        for (int i = 0; i < 4; i++) if (splits[i] != null)
        {
            var c = Settings.Channels[i]; result[Key(i, false)] = new(c.Local, c.LocalMute); result[Key(i, true)] = new(c.Stream, c.StreamMute);
        }
        result[Key(4, false)] = new(Settings.LocalMaster, Settings.LocalMute); result[Key(4, true)] = new(Settings.StreamMaster, Settings.StreamMute);
        return result;
    }
    public bool Set(string key, float? volume = null, bool? mute = null)
    {
        for (int i = 0; i < 5; i++) foreach (bool stream in new[] { false, true }) if (Key(i, stream) == key)
        {
            if (i == 4)
            {
                if (volume.HasValue) { if (stream) Settings.StreamMaster = volume.Value; else Settings.LocalMaster = volume.Value; }
                if (mute.HasValue) { if (stream) Settings.StreamMute = mute.Value; else Settings.LocalMute = mute.Value; }
            }
            else
            {
                var c = Settings.Channels[i];
                if (volume.HasValue) { if (stream) c.Stream = volume.Value; else c.Local = volume.Value; }
                if (mute.HasValue) { if (stream) c.StreamMute = mute.Value; else c.LocalMute = mute.Value; }
            }
            UpdateGains(); return true;
        }
        return false;
    }
    public float Peak(string key)
    {
        if (!Running) return 0;
        for (int i = 0; i < 4; i++) { if (key == Key(i, false)) return splits[i]?.Local.Peak ?? 0; if (key == Key(i, true)) return splits[i]?.Stream.Peak ?? 0; }
        return key == Key(4, false) ? localMaster?.Peak ?? 0 : key == Key(4, true) ? streamMaster?.Peak ?? 0 : 0;
    }
    public long Overflows => splits.Where(s => s != null).Sum(s => s!.Overflows);
    public long Clipping => (localMaster?.ClippedSamples ?? 0) + (streamMaster?.ClippedSamples ?? 0);
    public void Stop()
    {
        Running = false;
        foreach (var c in captures) { try { c.StopRecording(); } catch { } c.Dispose(); } captures.Clear();
        foreach (var p in players) { try { p.Stop(); } catch { } p.Dispose(); } players.Clear();
        foreach (var d in devices) d.Dispose(); devices.Clear();
        Array.Clear(splits); localMaster = streamMaster = null;
    }
    public void Dispose() => Stop();
}
