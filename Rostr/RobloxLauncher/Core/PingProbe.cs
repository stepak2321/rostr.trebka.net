using System.Net;
using System.Net.NetworkInformation;

namespace RobloxLauncher.Core;

public record PingTarget(IPAddress Address, bool Estimated);

/// <summary>
/// Roblox game servers sit behind a relay that usually ignores ICMP ping, so a plain ping
/// shows "no reply". This first tries the server directly. If that fails it walks the route
/// (like tracert) and uses the last router that answers, which gives an estimate that is
/// close to, but a little under, the real latency. Estimates are marked with "~".
/// </summary>
public static class PingProbe
{
    public static async Task<PingTarget?> FindTargetAsync(string ip, CancellationToken ct)
    {
        if (!IPAddress.TryParse(ip, out var dest)) return null;

        using var ping = new Ping();

        for (int i = 0; i < 2; i++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var r = await ping.SendPingAsync(dest, 1500);
                if (r.Status == IPStatus.Success) return new PingTarget(dest, false);
            }
            catch { }
        }

        IPAddress? lastHop = null;
        var payload = new byte[32];
        for (int ttl = 1; ttl <= 24; ttl++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var r = await ping.SendPingAsync(dest, 900, payload, new PingOptions(ttl, true));
                if (r.Status == IPStatus.Success) break;
                if (r.Status == IPStatus.TtlExpired && r.Address != null && IsPublic(r.Address))
                    lastHop = r.Address;
            }
            catch { }
        }

        return lastHop == null ? null : new PingTarget(lastHop, true);
    }

    public static async Task<long?> MeasureAsync(PingTarget target)
    {
        try
        {
            using var ping = new Ping();
            var r = await ping.SendPingAsync(target.Address, 1500);
            return r.Status == IPStatus.Success ? r.RoundtripTime : null;
        }
        catch
        {
            return null;
        }
    }

    private static bool IsPublic(IPAddress a)
    {
        var b = a.GetAddressBytes();
        if (b.Length != 4) return false;
        if (b[0] == 0 || b[0] == 10 || b[0] == 127) return false;
        if (b[0] == 192 && b[1] == 168) return false;
        if (b[0] == 172 && b[1] >= 16 && b[1] <= 31) return false;
        if (b[0] == 169 && b[1] == 254) return false;
        if (b[0] == 100 && b[1] >= 64 && b[1] <= 127) return false; // carrier-grade NAT
        return true;
    }
}
