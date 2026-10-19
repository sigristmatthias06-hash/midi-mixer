using System.Text.Json;

namespace MidiMixer;

static class ObsIntegrationTest
{
    internal static async Task Run()
    {
        var reportPath = Path.Combine(AppContext.BaseDirectory, "obs-test.txt");
        try
        {
            using var config = JsonDocument.Parse(File.ReadAllText(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "obs-studio", "plugin_config", "obs-websocket", "config.json")));
            var c = config.RootElement;
            using var obs = new ObsClient();
            await obs.Connect(c.GetProperty("server_port").GetInt32(), c.GetProperty("server_password").GetString()!);
            var version = await obs.Request("GetVersion");
            var scenes = await obs.Request("GetSceneList");
            var current = scenes.GetProperty("currentProgramSceneName").GetString();
            string name = "MIDI-Mixer-Test-" + Guid.NewGuid().ToString("N");
            bool createdScene = false, createdInput = false;
            try
            {
                await obs.Request("CreateScene", new { sceneName = name }); createdScene = true;
                var created = await obs.Request("CreateInput", new { sceneName = name, inputName = name + "-Audio", inputKind = "wasapi_process_output_capture", inputSettings = new { window = "Test:Test:midimixer-nonexistent.exe", priority = 2 }, sceneItemEnabled = true }); createdInput = true;
                await obs.Request("SetInputMute", new { inputName = name + "-Audio", inputMuted = true });
                if (!(await obs.Request("GetInputMute", new { inputName = name + "-Audio" })).GetProperty("inputMuted").GetBoolean()) throw new Exception("OBS mute check failed");
                await obs.Request("SetInputMute", new { inputName = name + "-Audio", inputMuted = false });
                if ((await obs.Request("GetInputMute", new { inputName = name + "-Audio" })).GetProperty("inputMuted").GetBoolean()) throw new Exception("OBS unmute check failed");
                await obs.Request("SetInputAudioMonitorType", new { inputName = name + "-Audio", monitorType = "OBS_MONITORING_TYPE_NONE" });
                using (var mixer = new StreamMixer(name + "-Audio"))
                {
                    await mixer.PollForTest();
                    string key = StreamMixer.KeyPrefix + name + "-Audio";
                    if (!mixer.Levels.ContainsKey(key)) throw new Exception("Stream mixer discovery failed");
                    mixer.SetVolume(key, .25f); await mixer.PollForTest();
                    if (Math.Abs(mixer.Levels[key].Volume - .25f) > .001f) throw new Exception("Separate OBS fader failed");
                    mixer.TogglePanic(); await mixer.PollForTest();
                    if (!mixer.PanicApplied) throw new Exception("Stream panic failed");
                    mixer.TogglePanic(); await mixer.PollForTest();
                    if (mixer.Levels[key].Mute) throw new Exception("Stream mute restore failed");
                    mixer.SetMute(key, true); await mixer.PollForTest();
                    mixer.TogglePanic(); await mixer.PollForTest(); mixer.TogglePanic(); await mixer.PollForTest();
                    if (!mixer.Levels[key].Mute) throw new Exception("Preexisting mute was not preserved");
                }
                await obs.Request("SetInputSettings", new { inputName = name + "-Audio", inputSettings = new { priority = 2 }, overlay = true });
                await obs.Request("SetSceneItemEnabled", new { sceneName = name, sceneItemId = created.GetProperty("sceneItemId").GetInt32(), sceneItemEnabled = true });
                if ((await obs.Request("GetSceneList")).GetProperty("currentProgramSceneName").GetString() != current) throw new Exception("Active OBS scene changed");
            }
            finally
            {
                try { if (createdInput) await obs.Request("RemoveInput", new { inputName = name + "-Audio" }); }
                finally { if (createdScene) await obs.Request("RemoveScene", new { sceneName = name }); }
            }
            File.WriteAllText(reportPath, $"PASS: OBS {version.GetProperty("obsVersion").GetString()}; authenticated connection, temporary app capture, separate stream volume, mute/unmute, panic and original mute restoration, meter subscription, monitoring, scene item, settings, cleanup. Active scene unchanged. No stream or recording started.");
        }
        catch (Exception ex) { File.WriteAllText(reportPath, "FAIL: " + ex.Message); Environment.ExitCode = 1; }
    }
}
