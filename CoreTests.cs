using System.Text.Json;

namespace MidiMixer;

// CI entry point: no windows, hardware enumeration, local profiles, OBS or MIDI I/O.
static class CoreTests
{
    public static int Run()
    {
        var tests = new (string Name, Action Run)[]
        {
            ("Fader pickup crossings, boundaries and reset", Pickup.Verify),
            ("Mute and page LED protocol with fake output", LedOutput.Verify),
            ("APC palette protocol", PadForm.VerifyProtocol),
            ("Independent audio buses, mute, clipping and validation", VirtualMixerTests.RunCore),
            ("Legacy settings migration and page persistence", SettingsRoundTrip),
            ("OBS window-name encoding", () => Require(RoutingForm.Encode("a#:b") == "a#22#3Ab", "Escaping"))
        };
        int failures = 0;
        foreach (var test in tests)
        {
            try { test.Run(); Console.WriteLine("PASS: " + test.Name); }
            catch (Exception ex) { failures++; Console.Error.WriteLine($"FAIL: {test.Name}: {ex}"); }
        }
        Console.WriteLine($"{tests.Length - failures}/{tests.Length} test groups passed.");
        return failures == 0 ? 0 : 1;
    }
    static void SettingsRoundTrip()
    {
        var original = new Settings(); original.Channels[0].Target = "Legacy application";
        original.PreparePages(); original.Pages[1].Channels[0].Target = "Second application";
        original.ActivePage = 1;
        var restored = JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(original))!;
        restored.PreparePages();
        Require(restored.Pages.Count == 8 && restored.Pages[0].Channels[0].Target == "Legacy application", "Legacy mapping lost");
        Require(restored.Channels[0].Target == "Second application", "Active page lost");
        restored.Channels[0].Target = "Changed";
        Require(restored.Pages[0].Channels[0].Target == "Legacy application", "Pages must be independent");
        restored.ActivePage = 100; restored.PreparePages();
        Require(restored.ActivePage == 7, "Invalid page must be clamped");
    }
    static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
}
