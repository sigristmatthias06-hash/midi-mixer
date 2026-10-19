using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System.Text.Json;

namespace MidiMixer;

static class VirtualMixerTests
{
    public static void DeviceTest()
    {
        var inputs = VirtualMixerEngine.Inputs(); var outputs = Headphones.List();
        var source = inputs.FirstOrDefault(d => d.Loopback && d.Name == "Music (Elgato Virtual Audio)");
        var local = outputs.FirstOrDefault(d => d.Name == "Lautsprecher (Realtek(R) Audio)");
        var stream = outputs.FirstOrDefault(d => d.Name == "System (Elgato Virtual Audio)");
        string report = Path.Combine(AppContext.BaseDirectory, "virtual-device-test.txt");
        if (source == null || local == null || stream == null) { File.WriteAllText(report, "SKIP: Known test endpoints not present."); return; }
        var settings = new VirtualMixerSettings { LocalDevice = local.Id, StreamDevice = stream.Id, LocalMute = true, StreamMute = true, LocalMaster = 0, StreamMaster = 0 };
        settings.Channels[0] = new VirtualChannel { DeviceId = source.Id, Loopback = true, LocalMute = true, StreamMute = true, Local = 0, Stream = 0 };
        using var engine = new VirtualMixerEngine(settings);
        try
        {
            engine.Start();
            var wait = System.Diagnostics.Stopwatch.StartNew();
            while (wait.ElapsedMilliseconds < 2000) { Application.DoEvents(); Thread.Sleep(10); }
            Require(engine.Running && engine.Failure == null, "Silent hardware pipeline");
            engine.Stop();
            Require(!engine.Running, "Hardware pipeline cleanup");
            File.WriteAllText(report, "PASS: Elgato Music loopback → independent Realtek and Elgato System outputs opened for 2 seconds and disposed. Both channel paths and both masters muted at zero throughout. No device defaults or external routing changed. JBL and actual audible routing not tested.");
        }
        catch (Exception ex) { File.WriteAllText(report, "FAIL: " + ex.Message); Environment.ExitCode = 1; }
    }
    public static void RunCore()
    {
        var format = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);
        byte[] signal = new byte[480 * 2 * sizeof(float)];
        var samples = Enumerable.Repeat(.8f, 480 * 2).ToArray(); Buffer.BlockCopy(samples, 0, signal, 0, signal.Length);
        var split = new CaptureSplit(format); split.Local.Gain = .5f; split.Stream.Gain = .25f;
        split.Push(signal, signal.Length);
        var local = new float[960]; var stream = new float[960];
        split.Local.Read(local, 0, local.Length); split.Stream.Read(stream, 0, stream.Length);
        Require(local.All(v => Math.Abs(v - .4f) < .0001f) && stream.All(v => Math.Abs(v - .2f) < .0001f), "Independent bus gains / fan-out");
        split.Local.Mute = true; split.Push(signal, signal.Length);
        split.Local.Read(local, 0, local.Length); split.Stream.Read(stream, 0, stream.Length);
        Require(local.All(v => v == 0) && stream.All(v => Math.Abs(v - .2f) < .0001f), "Local mute must not mute stream");
        split.Local.Mute = false; split.Stream.Mute = true; split.Push(signal, signal.Length);
        split.Local.Read(local, 0, local.Length); split.Stream.Read(stream, 0, stream.Length);
        Require(local.All(v => Math.Abs(v - .4f) < .0001f) && stream.All(v => v == 0), "Stream mute must not mute local");
        split.Stream.Read(stream, 0, stream.Length); Require(stream.All(v => v == 0), "Empty buffer produces silence");
        for (int i = 0; i < 100; i++) split.Push(signal, signal.Length);
        Require(split.Overflows > 0, "Bounded buffers recover from overflow");
        var a = new CaptureSplit(format); var b = new CaptureSplit(format); a.Push(signal, signal.Length); b.Push(signal, signal.Length);
        var mixed = new MixingSampleProvider(new[] { a.Local, b.Local }) { ReadFully = true };
        var final = new GainStage(mixed, true); final.Read(local, 0, local.Length);
        Require(local.All(v => v == 1) && final.ClippedSamples == 960, "Master clipping detection and clamp");
        var converted = new CaptureSplit(WaveFormat.CreateIeeeFloatWaveFormat(44100, 1));
        Require(converted.Local.WaveFormat.SampleRate == 48000 && converted.Local.WaveFormat.Channels == 2, "Mono/rate conversion");
        using var engine = new VirtualMixerEngine(new VirtualMixerSettings());
        engine.Set(VirtualMixerEngine.Key(0, true), .17f, false);
        Require(engine.Settings.Channels[0].Local == .5f && engine.Settings.Channels[0].Stream == .17f, "MIDI target isolation");
        Require(engine.Levels().Count == 0, "Stopped engine exposes no active targets");
        bool rejected = false; try { engine.Start(); } catch (IOException) { rejected = true; }
        Require(rejected && !engine.Running, "Missing routes rejected before starting audio");
    }
    public static void Run()
    {
        RunCore();
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "virtual-mixer-test.json"), JsonSerializer.Serialize(new { Result = "PASS", Checks = 10, Inputs = VirtualMixerEngine.Inputs(), Outputs = Headphones.List(), Note = "Synthetic audio only. No real audio routes changed." }, new JsonSerializerOptions { WriteIndented = true }));
        using var form = new VirtualMixerForm(); form.Show(); Application.DoEvents();
        using var image = new Bitmap(form.Width, form.Height); form.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size)); image.Save(Path.Combine(AppContext.BaseDirectory, "virtual-mixer-test.png")); form.Close();
    }
    static void Require(bool condition, string name) { if (!condition) throw new Exception("Virtual mixer test failed: " + name); }
}
