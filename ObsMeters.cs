using System.Collections.Concurrent;
using System.Text.Json;

namespace MidiMixer;

sealed class ObsMeters : IDisposable
{
    readonly CancellationTokenSource stop = new();
    readonly ConcurrentDictionary<string, (float Value, long Time)> values = new();
    ObsClient? client;
    public bool Alive { get; private set; }
    public float Peak(string key) => values.TryGetValue(key, out var value) && Environment.TickCount64 - value.Time < 1000 ? value.Value : 0;
    public async Task Start(int port, string password)
    {
        client = new ObsClient(); await client.Connect(port, password, 1 << 16);
        Alive = true;
        _ = Task.Run(async () =>
        {
            try
            {
                while (!stop.IsCancellationRequested)
                {
                    var message = await client.NextEvent(stop.Token);
                    if (message.GetProperty("op").GetInt32() != 5) continue;
                    var d = message.GetProperty("d");
                    if (d.GetProperty("eventType").GetString() != "InputVolumeMeters") continue;
                    foreach (var item in d.GetProperty("eventData").GetProperty("inputs").EnumerateArray())
                    {
                        string name = item.GetProperty("inputName").GetString()!;
                        if (!name.StartsWith(StreamMixer.SourcePrefix, StringComparison.Ordinal)) continue;
                        float peak = 0;
                        foreach (var channel in item.GetProperty("inputLevelsMul").EnumerateArray())
                            if (channel.GetArrayLength() > 1) peak = Math.Max(peak, channel[1].GetSingle());
                        values[StreamMixer.KeyPrefix + name] = (float.IsFinite(peak) ? Math.Clamp(peak, 0, 1) : 0, Environment.TickCount64);
                    }
                }
            }
            catch (Exception) { /* Main connection retries; stale meter values expire after one second. */ }
            finally { Alive = false; }
        });
    }
    public void Dispose() { stop.Cancel(); client?.Dispose(); values.Clear(); }
}
