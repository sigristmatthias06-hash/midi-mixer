using NAudio.Midi;

namespace MidiMixer;

// One shared output for pad colors and live mute feedback, owned by the mixer.
sealed class LedOutput : IDisposable
{
    MidiOut? output;
    readonly Action<int>? testSend;
    readonly bool[] muted = new bool[8];
    readonly int[] sent = Enumerable.Repeat(-1, 8).ToArray();
    readonly int[] sceneSent = Enumerable.Repeat(-1, 8).ToArray();
    public int ActivePage { get; private set; }
    public bool StreamConnected { get; private set; }
    public bool StreamMuted { get; private set; }
    bool streamApplied;
    int panicSent = -1;
    int PanicMessage => (StreamMuted && !streamApplied ? 0x9C : 0x96) | (63 << 8) | ((StreamConnected ? (StreamMuted ? 5 : 21) : 0) << 16);
    public void SetStreamState(bool connected, bool muted, bool applied)
    {
        StreamConnected = connected; StreamMuted = muted; streamApplied = applied;
        if (Connected && panicSent != PanicMessage) Send(PanicMessage);
    }
    public bool Connected => output != null || testSend != null;
    public bool IsMuted(int track) => muted[track];
    public LedOutput(Action<int>? testSend = null) => this.testSend = testSend;

    public void Connect(int device)
    {
        Disconnect();
        output = new MidiOut(device);
        Flush();
    }
    public void Sync(Mapping[] mappings, Snapshot? snapshot, int page = 0)
    {
        ActivePage = Math.Clamp(page, 0, 7);
        for (int i = 0; i < 8; i++)
            muted[i] = snapshot != null && snapshot.Values.TryGetValue(mappings[i].Target, out var level) && level.Mute;
        Flush();
    }
    void Flush()
    {
        if (!Connected) return;
        for (int i = 0; i < 8; i++)
        {
            int value = muted[i] ? 1 : 0;
            if (sent[i] != value) Send(0x90 | ((100 + i) << 8) | (value << 16));
            int sceneValue = i == ActivePage ? 1 : 0;
            if (sceneSent[i] != sceneValue) Send(0x90 | ((112 + i) << 8) | (sceneValue << 16));
        }
        if (panicSent != PanicMessage) Send(PanicMessage);
    }
    public void Send(int message)
    {
        int note = (message >> 8) & 127;
        if (note == 63) message = PanicMessage;
        // Color resends must never overwrite the current mute state.
        if (note is >= 100 and <= 107)
            message = 0x90 | (note << 8) | ((muted[note - 100] ? 1 : 0) << 16);
        if (note is >= 112 and <= 119)
            message = 0x90 | (note << 8) | ((note - 112 == ActivePage ? 1 : 0) << 16);
        if (!Connected) return;
        try
        {
            if (testSend != null) testSend(message); else output!.Send(message);
            if (note is >= 100 and <= 107) sent[note - 100] = (message >> 16) & 127;
            if (note is >= 112 and <= 119) sceneSent[note - 112] = (message >> 16) & 127;
            if (note == 63) panicSent = message;
        }
        catch { Disconnect(); throw; }
    }
    public void Disconnect()
    {
        var old = output; output = null;
        Array.Fill(sent, -1);
        Array.Fill(sceneSent, -1);
        panicSent = -1;
        old?.Dispose();
    }
    public void Dispose() => Disconnect();

    internal static void Verify()
    {
        var messages = new List<int>();
        using var leds = new LedOutput(messages.Add);
        var mappings = new Settings().Channels;
        mappings[0].Target = "test";
        var snapshot = new Snapshot("test", new() { ["test"] = new(.5f, true) });
        leds.Sync(mappings, snapshot);
        if (messages.Count != 17 || messages[0] != 0x016490 || messages[1] != 0x017090) throw new Exception("Initial mute/page LEDs failed");
        messages.Clear(); leds.Sync(mappings, snapshot);
        if (messages.Count != 0) throw new Exception("Unchanged LEDs resent");
        leds.Send(0x026490);
        if (messages.Single() != 0x016490) throw new Exception("Manual color overrode mute");
        messages.Clear(); snapshot.Values["test"] = new(.5f, false); leds.Sync(mappings, snapshot);
        if (messages.Single() != 0x006490) throw new Exception("Unmute LED failed");
        snapshot.Values["test"] = new(.5f, true); leds.Sync(mappings, snapshot);
        messages.Clear(); leds.Sync(mappings, null);
        if (messages.Single() != 0x006490) throw new Exception("Unavailable audio LED failed");
        messages.Clear(); leds.Disconnect(); leds.Sync(mappings, snapshot);
        if (messages.Count != 17 || messages[0] != 0x016490) throw new Exception("Reconnect sync failed");
        messages.Clear(); leds.Sync(mappings, snapshot, 3);
        if (!messages.SequenceEqual(new[] { 0x007090, 0x017390 })) throw new Exception("Page LED switch failed");
    }
}
