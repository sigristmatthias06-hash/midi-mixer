using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MidiMixer;

sealed class ObsClient : IDisposable
{
    readonly ClientWebSocket socket = new();
    static string Hash(string value) => Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    public static string Authenticate(string password, string salt, string challenge) => Hash(Hash(password + salt) + challenge);
    public async Task Connect(int port, string password, int subscriptions = 0)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        await socket.ConnectAsync(new Uri($"ws://127.0.0.1:{port}"), timeout.Token);
        var hello = await Receive(timeout.Token);
        if (hello.GetProperty("op").GetInt32() != 0) throw new IOException("OBS hat keine gültige Begrüßung gesendet.");
        var identify = new Dictionary<string, object> { ["rpcVersion"] = 1, ["eventSubscriptions"] = subscriptions };
        if (hello.GetProperty("d").TryGetProperty("authentication", out var auth))
            identify["authentication"] = Authenticate(password, auth.GetProperty("salt").GetString()!, auth.GetProperty("challenge").GetString()!);
        await Send(new { op = 1, d = identify }, timeout.Token);
        if ((await Receive(timeout.Token)).GetProperty("op").GetInt32() != 2) throw new IOException("OBS-Anmeldung fehlgeschlagen.");
    }
    public Task<JsonElement> NextEvent(CancellationToken token) => Receive(token);
    public async Task<JsonElement> Request(string type, object? data = null)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        string id = Guid.NewGuid().ToString();
        await Send(new { op = 6, d = new { requestType = type, requestId = id, requestData = data ?? new { } } }, timeout.Token);
        while (true)
        {
            var response = await Receive(timeout.Token);
            if (response.GetProperty("op").GetInt32() != 7) continue;
            var d = response.GetProperty("d");
            if (d.GetProperty("requestId").GetString() != id) continue;
            var status = d.GetProperty("requestStatus");
            if (!status.GetProperty("result").GetBoolean())
                throw new IOException($"OBS {type}: {(status.TryGetProperty("comment", out var comment) ? comment.GetString() : status.GetProperty("code").ToString())}");
            return d.TryGetProperty("responseData", out var result) ? result.Clone() : JsonSerializer.SerializeToElement(new { });
        }
    }
    async Task Send(object value, CancellationToken token) => await socket.SendAsync(new ArraySegment<byte>(JsonSerializer.SerializeToUtf8Bytes(value)), WebSocketMessageType.Text, true, token);
    async Task<JsonElement> Receive(CancellationToken token)
    {
        using var buffer = new MemoryStream();
        var bytes = new byte[8192];
        WebSocketReceiveResult part;
        do
        {
            part = await socket.ReceiveAsync(new ArraySegment<byte>(bytes), token);
            if (part.MessageType == WebSocketMessageType.Close) throw new IOException("OBS hat die Verbindung geschlossen. Passwort und WebSocket-Einstellungen prüfen.");
            buffer.Write(bytes, 0, part.Count);
            if (buffer.Length > 8 * 1024 * 1024) throw new IOException("OBS-Antwort zu groß.");
        } while (!part.EndOfMessage);
        using var json = JsonDocument.Parse(buffer.ToArray()); return json.RootElement.Clone();
    }
    public void Dispose() => socket.Dispose();
}
