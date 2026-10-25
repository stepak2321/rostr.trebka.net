using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using RobloxLauncher.Core;
using RobloxLauncher.Settings;

namespace RobloxLauncher;

public partial class MainWindow : Window
{
    // ---------- Sessions (one per running client) ----------

    private void StartSession(Session s, DateTime launchedAt)
    {
        _sessions.Add(s);

        s.Watcher.ServerAddress += (relay, rcc) => Dispatcher.InvokeAsync(() => OnServerAddress(s, relay, rcc));
        s.Watcher.GameJoined += (job, place, ip) => Dispatcher.InvokeAsync(() => OnGameJoined(s, job, place, ip));
        s.Watcher.GameClosed += () => Dispatcher.InvokeAsync(() => OnGameClosed(s));
        s.Watcher.PidAdopted += pid => Dispatcher.InvokeAsync(() =>
        {
            s.Pid = pid;
            s.Overlay?.Attach(pid);
        });
        s.Watcher.Start(launchedAt, s.Pid);

        if (S.OverlayEnabled) AttachOverlay(s);
    }

    private void AttachOverlay(Session s)
    {
        var overlay = new OverlayWindow();
        overlay.SetCorner(S.OverlayCorner);
        overlay.SetAccount(AccountStore.All.Count > 1 ? s.AccountName : null);
        ApplyOverlayFields(overlay);
        overlay.SetServer(s.RelayIp, s.Region);
        if (s.Pid != 0) overlay.Attach(s.Pid);
        s.Overlay = overlay;
    }

    private void EndSession(Session s)
    {
        FlushPlaytime(s, true);
        s.Watcher.Dispose();
        if (s.Overlay != null)
        {
            s.Overlay.StopOverlay();
            s.Overlay.Close();
            s.Overlay = null;
        }
        _sessions.Remove(s);
        _ = RefreshDiscordAsync();
    }


    // ---------- Discord status ----------

    private void DiscordToggle_Click(object sender, RoutedEventArgs e)
    {
        S.DiscordEnabled = !S.DiscordEnabled;
        S.Save();
        UpdateSettingsUi();
        _ = RefreshDiscordAsync();
    }

    private void DiscordHideToggle_Click(object sender, RoutedEventArgs e)
    {
        S.DiscordHideGame = !S.DiscordHideGame;
        S.Save();
        UpdateSettingsUi();
        _ = RefreshDiscordAsync();
    }

    // Shows the game of the client you joined last. With no game running it shows an idle status; with the switch off it is cleared.
    private async Task RefreshDiscordAsync()
    {
        try
        {
            var s = S.DiscordEnabled
                ? _sessions.LastOrDefault(x => x.PresenceStartUtc != null && x.PresencePlace > 0)
                : null;
            if (!S.DiscordEnabled)
            {
                _discord.Clear();
                return;
            }
            if (s == null)
            {
                // Not in a game: show that the launcher is open, and which game page is being browsed.
                var browsing = GamePanel.Visibility == Visibility.Visible && _placeId != 0 && _gameName.Length > 0
                    ? _gameName : null;
                _discord.SetIdle(browsing, S.DiscordHideGame, _launcherStartUtc);
                return;
            }
            Log.Info($"Discord status: joined place {s.PresencePlace}, preparing it.");

            var place = s.PresencePlace;
            var start = s.PresenceStartUtc!.Value;

            if (!_discordGames.TryGetValue(place, out var game))
            {
                try
                {
                    game = await RobloxApi.GetGameAsync(place);
                    _discordGames[place] = game;
                }
                catch (Exception ex)
                {
                    Log.Warn("Discord status: could not load game info: " + ex.Message);
                }
            }

            // The player may have left while the game info was loading. Whoever changed things refreshes again.
            if (!S.DiscordEnabled || !_sessions.Contains(s) || s.PresencePlace != place) return;

            var known = (GameEntry?)S.Recent.FirstOrDefault(r => r.PlaceId == place)
                        ?? S.Favorites.FirstOrDefault(f => f.PlaceId == place);
            var name = game?.Name ?? known?.Name ?? "";
            var creator = game != null && game.Creator != "unknown" ? game.Creator : "";

            _discord.Set(new DiscordPresence.Info(name, creator, place, game?.IconUrl ?? known?.IconUrl,
                                                  start, S.DiscordHideGame));
        }
        catch (Exception ex)
        {
            Log.Warn("Discord status failed: " + ex.Message);
        }
    }


    // ---------- Playtime ----------

    private static string FormatPlaytime(long seconds)
    {
        if (seconds < 60) return Loc.T(seconds <= 0 ? "0 min" : "under 1 min");
        var minutes = seconds / 60;
        if (minutes < 60) return Loc.T($"{minutes} min");
        var h = minutes / 60;
        var m = minutes % 60;
        return h >= 100 || m == 0 ? Loc.T($"{h} h") : Loc.T($"{h} h") + " " + Loc.T($"{m} min");
    }

    private static string PlaytimeLine(long placeId)
    {
        if (!S.Playtime.TryGetValue(placeId, out var st) || st.Seconds <= 0 && st.Sessions == 0)
            return Loc.T("No playtime tracked yet");
        var sessions = Loc.T(st.Sessions == 1 ? "1 session" : $"{st.Sessions} sessions");
        return Loc.T($"Played {FormatPlaytime(st.Seconds)}") + "  |  " + sessions;
    }

    private void AddPlaytime(long place, long seconds, bool newSession)
    {
        if (place <= 0) return;
        if (!S.Playtime.TryGetValue(place, out var st))
            S.Playtime[place] = st = new PlayStat();

        st.Seconds += Math.Max(0, seconds);
        if (newSession) st.Sessions++;
        st.LastPlayed = DateTime.Now;

        var known = (GameEntry?)S.Recent.FirstOrDefault(r => r.PlaceId == place)
                    ?? S.Favorites.FirstOrDefault(f => f.PlaceId == place);
        if (!string.IsNullOrEmpty(known?.Name)) st.Name = known!.Name;
        else if (string.IsNullOrEmpty(st.Name)) st.Name = $"Game {place}";
    }

    // Moves the time since the last flush into the stats and starts counting again from now.
    private void FlushPlaytime(Session s, bool save)
    {
        if (s.JoinedUtc == null || s.PlaytimePlace <= 0) return;
        var now = DateTime.UtcNow;
        var seconds = (long)(now - s.JoinedUtc.Value).TotalSeconds;
        s.JoinedUtc = now;
        AddPlaytime(s.PlaytimePlace, seconds, false);
        if (save) S.Save();
    }

    private void TickPlaytime()
    {
        var active = _sessions.Where(x => x.JoinedUtc != null).ToList();
        if (active.Count == 0) return;

        foreach (var session in active) FlushPlaytime(session, false);
        S.Save();

        // Update the labels in place, so the Library does not jump back to the top while you scroll it.
        if (LibraryPage.Visibility != Visibility.Visible) return;
        foreach (var row in _libRows) row.Playtime = PlaytimeLine(row.PlaceId);
        UpdateLibraryStats();
    }

    private void UpdateLibraryStats()
    {
        var played = S.Playtime.Values.Where(x => x.Seconds > 0).ToList();
        StatTotal.Text = FormatPlaytime(played.Sum(x => x.Seconds));
        StatGames.Text = played.Count.ToString();

        var top = played.OrderByDescending(x => x.Seconds).FirstOrDefault();
        StatTop.Text = top == null ? "-" : $"{(string.IsNullOrEmpty(top.Name) ? Loc.T("Unknown") : top.Name)}  ({FormatPlaytime(top.Seconds)})";
    }


    // ---------- Log watcher events ----------

    private static bool IsPrivate(string ip)
    {
        if (!System.Net.IPAddress.TryParse(ip, out var a)) return true;
        var b = a.GetAddressBytes();
        if (b.Length != 4) return true;
        return b[0] == 10 || b[0] == 127 ||
               (b[0] == 192 && b[1] == 168) ||
               (b[0] == 172 && b[1] >= 16 && b[1] <= 31);
    }

    // relay = the address the client talks to (ping it); rcc = the real server, when its address is public.
    private void OnServerAddress(Session s, string relay, string? rcc)
    {
        s.RelayIp = relay;
        s.Overlay?.SetServer(relay, null);
        _ = ResolveRegionAsync(s, rcc ?? relay);
    }

    private void OnGameJoined(Session s, string job, long place, string ip)
    {
        s.Job = job;

        // Playtime starts when the player is really in a game. A teleport to another place closes the
        // old game's time and starts the new one.
        var playPlace = place > 0 ? place : s.PlaceId;
        if (s.JoinedUtc != null && s.PlaytimePlace != playPlace)
        {
            FlushPlaytime(s, false);
            s.JoinedUtc = null;
        }
        if (s.JoinedUtc == null && playPlace > 0)
        {
            s.JoinedUtc = DateTime.UtcNow;
            s.PlaytimePlace = playPlace;
            s.PresenceStartUtc = DateTime.UtcNow;
            s.PresencePlace = playPlace;
            AddPlaytime(playPlace, 0, true);
            S.Save();
            _ = RefreshDiscordAsync();
        }

        var entry = S.Recent.FirstOrDefault(r => r.PlaceId == place);
        if (entry != null)
        {
            entry.LastJobId = job;
            S.Save();
        }

        if (s.RelayIp == null && !IsPrivate(ip))
        {
            s.RelayIp = ip;
            s.Overlay?.SetServer(ip, null);
            _ = ResolveRegionAsync(s, ip);
        }

        CommitRegion(s);
        UpdatePlayButtons();
    }

    private async Task ResolveRegionAsync(Session s, string ip)
    {
        s.RegionIp = ip;

        string? region = null;
        for (int attempt = 0; attempt < 3 && region == null; attempt++)
        {
            region = await GeoLocator.LookupAsync(ip);
            if (region == null) await Task.Delay(3000);
            if (s.RegionIp != ip) return;
        }
        if (region == null) return;

        s.Region = region;
        s.Overlay?.SetRegion(region);
        SetStatus("Connected: " + region, Ok);
        CommitRegion(s);
    }

    private void StoreRegion(string job, string region)
    {
        S.Regions[job] = region;
        if (S.Regions.Count > 500)
            foreach (var k in S.Regions.Keys.Take(100).ToList())
                S.Regions.Remove(k);
    }

    private void CommitRegion(Session s)
    {
        if (s.Job == null || s.Region == null) return;

        StoreRegion(s.Job, s.Region);

        var entry = S.Recent.FirstOrDefault(r => r.LastJobId == s.Job);
        if (entry != null) entry.LastRegion = s.Region;
        S.Save();

        if (GamePanel.Visibility == Visibility.Visible && _servers.Count > 0) ApplyFilter();
        if (LibraryPage.Visibility == Visibility.Visible) RefreshLibrary();
    }

    private void OnGameClosed(Session s)
    {
        EndSession(s);
        UpdatePlayButtons();
        SetStatus(_sessions.Count > 0 ? $"A client closed. {_sessions.Count} still running." : "Roblox closed.", Busy);
    }
}
