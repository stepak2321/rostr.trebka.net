using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace RobloxLauncher.Core;

/// <summary>
/// Discord Rich Presence over Discord's local pipe (no extra package needed). Shows the Roblox game you are in.
///
/// Setup, once: go to https://discord.com/developers/applications, create an application named "Rostr",
/// copy its Application ID into ClientId below, and under Rich Presence > Art Assets upload the Rostr logo
/// with the key "rostr".
///
/// Everything here is best effort: no Discord, no problem. It never throws and never blocks the UI.
/// </summary>
public sealed class DiscordPresence : IDisposable
{
    public const string ClientId = "1563661058575700138";
    public const string SiteUrl = "https://rostr.trebka.net";

    public sealed record Info(string GameName, string Creator, long PlaceId, string? IconUrl,
                              DateTime StartUtc, bool HideGame, bool Idle = false);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Timer _retry;
    private NamedPipeClientStream? _pipe;
    private volatile Info? _desired;
    private volatile bool _disposed;
    private bool _loggedNotConfigured;
    private bool _loggedNoDiscord;
    private DateTime _lastPushUtc = DateTime.MinValue;

    public DiscordPresence()
    {
        // If Discord was not running (or was restarted), try again every 15 seconds. While connected, the
        // same status is sent again once a minute, so a broken pipe is noticed and fixed by itself.
        _retry = new Timer(_ =>
        {
            if (_desired == null) return;
            if (_pipe == null || DateTime.UtcNow - _lastPushUtc > TimeSpan.FromSeconds(60)) _ = PushAsync();
        }, null, 15000, 15000);
    }

    private static bool Configured => !ClientId.StartsWith("PUT_", StringComparison.Ordinal);

    public void Set(Info info)
    {
        _desired = info;
        _ = Task.Run(PushAsync);
    }

    /// <summary>Status for when no game is running: the launcher is open and you are browsing.</summary>
    public void SetIdle(string? browsingGame, bool hideGame, DateTime launcherStartUtc)
    {
        Set(new Info(browsingGame ?? "", "", 0, null, launcherStartUtc, hideGame, Idle: true));
    }

    public void Clear()
    {
        _desired = null;
        _ = Task.Run(PushAsync);
    }

    private async Task PushAsync()
    {
        if (_disposed) return;
        if (!Configured)
        {
            if (!_loggedNotConfigured)
            {
                _loggedNotConfigured = true;
                Log.Warn("Discord status is off: ClientId in Core/DiscordPresence.cs is not set yet.");
            }
            return;
        }
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var info = _desired;
            if (info == null && _pipe == null) return;           // nothing to show, nothing to clear
            if (_pipe == null && !await ConnectAsync().ConfigureAwait(false))   // Discord is not running
            {
                if (!_loggedNoDiscord)
                {
                    _loggedNoDiscord = true;
                    Log.Warn("Discord status: could not find the Discord desktop app. Will keep trying.");
                }
                return;
            }
            await SendAsync(BuildActivityCommand(info)).ConfigureAwait(false);
            _lastPushUtc = DateTime.UtcNow;
            Log.Info(info == null ? "Discord status cleared."
                : info.Idle ? "Discord status set: idle in the launcher."
                : $"Discord status set: {info.GameName} (place {info.PlaceId}).");
        }
        catch (Exception ex)
        {
            Log.Warn("Discord presence: " + ex.Message);
            Drop();
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<bool> ConnectAsync()
    {
        for (int i = 0; i < 10; i++)
        {
            var pipe = new NamedPipeClientStream(".", $"discord-ipc-{i}", PipeDirection.InOut, PipeOptions.Asynchronous);
            try
            {
                await pipe.ConnectAsync(500).ConfigureAwait(false);
                _pipe = pipe;
                await WriteFrameAsync(0, JsonSerializer.Serialize(new { v = 1, client_id = ClientId })).ConfigureAwait(false);
                var (op, json) = await ReadFrameAsync().ConfigureAwait(false);
                if (op == 2) throw new IOException("Discord refused the handshake: " + json);
                _loggedNoDiscord = false;
                Log.Info($"Discord status: connected to discord-ipc-{i}.");
                return true;
            }
            catch (Exception ex)
            {
                if (_pipe == pipe) Log.Warn($"Discord status: discord-ipc-{i} connected but failed: {ex.Message}");
                _pipe = null;
                pipe.Dispose();
            }
        }
        return false;
    }

    private static string Clip(string s) => s.Length <= 128 ? s : s[..125] + "...";

    private static string BuildActivityCommand(Info? info)
    {
        Dictionary<string, object?>? activity = null;

        if (info != null && info.Idle)
        {
            var browsing = !info.HideGame && !string.IsNullOrWhiteSpace(info.GameName);
            activity = new Dictionary<string, object?>
            {
                ["type"] = 0,
                ["details"] = browsing ? "Browsing servers" : "In the launcher",
                ["state"] = browsing ? Clip(info.GameName) : "Not in a game",
                ["timestamps"] = new Dictionary<string, object?>
                {
                    ["start"] = new DateTimeOffset(DateTime.SpecifyKind(info.StartUtc, DateTimeKind.Utc)).ToUnixTimeSeconds()
                },
                ["assets"] = new Dictionary<string, object?> { ["large_image"] = "rostr", ["large_text"] = "Rostr" },
                ["buttons"] = new List<object> { new { label = "Get Rostr", url = SiteUrl } }
            };
        }
        else if (info != null)
        {
            var hide = info.HideGame;
            var details = hide || string.IsNullOrWhiteSpace(info.GameName) ? "Playing a Roblox game" : Clip(info.GameName);
            var state = hide || string.IsNullOrWhiteSpace(info.Creator) ? "Roblox" : Clip("Roblox · by " + info.Creator);

            var icon = !hide && info.IconUrl != null && info.IconUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                ? info.IconUrl : null;

            var assets = new Dictionary<string, object?>
            {
                ["large_image"] = icon ?? "rostr",
                ["large_text"] = icon != null ? "Roblox" : "Rostr"
            };
            if (icon != null)
            {
                assets["small_image"] = "rostr";
                assets["small_text"] = "Rostr";
            }

            var buttons = new List<object> { new { label = "Get Rostr", url = SiteUrl } };
            if (!hide && info.PlaceId > 0)
                buttons.Add(new { label = "See game page", url = $"https://www.roblox.com/games/{info.PlaceId}" });

            activity = new Dictionary<string, object?>
            {
                ["type"] = 0,
                ["details"] = details,
                ["state"] = state,
                ["timestamps"] = new Dictionary<string, object?>
                {
                    ["start"] = new DateTimeOffset(DateTime.SpecifyKind(info.StartUtc, DateTimeKind.Utc)).ToUnixTimeSeconds()
                },
                ["assets"] = assets,
                ["buttons"] = buttons
            };
        }

        return JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["cmd"] = "SET_ACTIVITY",
            ["nonce"] = Guid.NewGuid().ToString(),
            ["args"] = new Dictionary<string, object?>
            {
                ["pid"] = Environment.ProcessId,
                ["activity"] = activity          // null clears the presence
            }
        });
    }

    private async Task SendAsync(string json)
    {
        await WriteFrameAsync(1, json).ConfigureAwait(false);
        var (_, reply) = await ReadFrameAsync().ConfigureAwait(false);
        if (reply.Contains("\"evt\":\"ERROR\"", StringComparison.Ordinal))
            Log.Warn("Discord rejected the presence: " + reply);
    }

    // A Discord pipe frame is: 4 bytes opcode, 4 bytes length (both little endian), then the JSON text.
    private async Task WriteFrameAsync(int op, string json)
    {
        var body = Encoding.UTF8.GetBytes(json);
        var frame = new byte[8 + body.Length];
        BitConverter.GetBytes(op).CopyTo(frame, 0);
        BitConverter.GetBytes(body.Length).CopyTo(frame, 4);
        body.CopyTo(frame, 8);

        using var cts = new CancellationTokenSource(3000);
        await _pipe!.WriteAsync(frame, cts.Token).ConfigureAwait(false);
        await _pipe.FlushAsync(cts.Token).ConfigureAwait(false);
    }

    private async Task<(int Op, string Json)> ReadFrameAsync()
    {
        using var cts = new CancellationTokenSource(3000);
        var head = new byte[8];
        await ReadExactAsync(head, cts.Token).ConfigureAwait(false);
        var op = BitConverter.ToInt32(head, 0);
        var len = BitConverter.ToInt32(head, 4);
        if (len < 0 || len > 1_000_000) throw new IOException("Unexpected reply from Discord");
        var body = new byte[len];
        await ReadExactAsync(body, cts.Token).ConfigureAwait(false);
        return (op, Encoding.UTF8.GetString(body));
    }

    private async Task ReadExactAsync(byte[] buffer, CancellationToken ct)
    {
        var got = 0;
        while (got < buffer.Length)
        {
            var n = await _pipe!.ReadAsync(buffer.AsMemory(got), ct).ConfigureAwait(false);
            if (n == 0) throw new IOException("Discord closed the connection");
            got += n;
        }
    }

    private void Drop()
    {
        try { _pipe?.Dispose(); } catch { }
        _pipe = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _desired = null;
        _retry.Dispose();

        // Best effort: take the status down before the launcher closes, but never hang the shutdown.
        try
        {
            Task.Run(async () =>
            {
                await _gate.WaitAsync().ConfigureAwait(false);
                try
                {
                    if (_pipe != null) await SendAsync(BuildActivityCommand(null)).ConfigureAwait(false);
                }
                catch { }
                finally
                {
                    Drop();
                    _gate.Release();
                }
            }).Wait(1500);
        }
        catch { }
    }
}
