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
    // ---------- Library ----------

    // Number of tile columns on the Library page. Follows the window width so the tiles always fill the row.
    public static readonly DependencyProperty LibColumnsProperty =
        DependencyProperty.Register(nameof(LibColumns), typeof(int), typeof(MainWindow), new PropertyMetadata(3));

    public int LibColumns
    {
        get => (int)GetValue(LibColumnsProperty);
        set => SetValue(LibColumnsProperty, value);
    }

    private void LibScroll_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var usable = e.NewSize.Width - 24;   // room for the scrollbar and padding
        LibColumns = Math.Max(1, (int)(usable / 190));
    }

    private void RefreshLibrary()
    {
        var fav = S.Favorites.Select(ToRow).ToList();
        var recent = S.Recent.Select(ToRow).ToList();
        _libRows = fav.Concat(recent).ToList();
        FavList.ItemsSource = fav;
        RecentList.ItemsSource = recent;
        TranslateList(FavList);
        TranslateList(RecentList);
        FavCount.Text = fav.Count.ToString();
        RecentCount.Text = recent.Count.ToString();
        UpdateLibraryStats();
        FavEmpty.Visibility = S.Favorites.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        RecentEmpty.Visibility = S.Recent.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private LibRow ToRow(GameEntry g)
    {
        var rec = S.Recent.FirstOrDefault(r => r.PlaceId == g.PlaceId);
        var parts = new List<string>();
        if (!string.IsNullOrEmpty(rec?.LastRegion)) parts.Add(rec!.LastRegion!);
        if (rec != null && rec.LastPlayed != default) parts.Add(Ago(rec.LastPlayed));
        if (parts.Count == 0) parts.Add(Loc.T("Not played yet"));

        var url = g.IconUrl ?? rec?.IconUrl;

        var isFav = S.Favorites.Any(f => f.PlaceId == g.PlaceId);
        var row = new LibRow
        {
            PlaceId = g.PlaceId,
            Name = g.Name,
            Sub = string.Join("  |  ", parts),
            IconBrush = Dim,
            RejoinVisibility = string.IsNullOrEmpty(rec?.LastJobId) ? Visibility.Collapsed : Visibility.Visible,
            StarGlyph = isFav ? "\uE735" : "\uE734",
            StarBrush = isFav ? Gold : Brushes.White,
            Playtime = PlaytimeLine(g.PlaceId)
        };
        _ = LoadRowIconAsync(row, url, g.PlaceId);
        return row;
    }

    // Fills in a library card's picture after it is on screen. Games saved without an icon get it looked up.
    private async Task LoadRowIconAsync(LibRow row, string? url, long placeId)
    {
        try
        {
            if (string.IsNullOrEmpty(url)) url = await RobloxApi.GetIconUrlAsync(placeId);
            var img = await ImageService.LoadAsync(url);
            if (img != null) row.IconBrush = new ImageBrush(img) { Stretch = Stretch.UniformToFill };
        }
        catch (Exception ex)
        {
            Log.Warn("Library icon: " + ex.Message);
        }
    }

    private static string Ago(DateTime t)
    {
        var d = DateTime.Now - t;
        if (d.TotalMinutes < 1) return Loc.T("just now");
        if (d.TotalMinutes < 60) return Loc.T($"{(int)d.TotalMinutes} min ago");
        if (d.TotalHours < 24) return Loc.T($"{(int)d.TotalHours} h ago");
        return Loc.T($"{(int)d.TotalDays} d ago");
    }

    private bool CanUseWebStart()
    {
        if (ProtocolHandler.IsRegistered() || ProtocolHandler.IsRegisteredElsewhere()) return true;
        SetStatus("Register the launcher in Settings first. Otherwise Roblox's own launcher opens.", Bad);
        return false;
    }

    private void LibPlay_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button b) return;
        var placeId = Convert.ToInt64(b.Tag);

        // With an account selected, Play joins a random server right away, no browser involved.
        if (ActiveAccount() != null)
        {
            _ = QuickRandomAsync(placeId);
            return;
        }

        if (!CanUseWebStart()) return;
        SetStatus("Opening Roblox in your browser for a fresh login. Pick an account in the sidebar to skip this.", Busy);
        OpenUrl($"https://www.roblox.com/games/start?placeId={placeId}");
    }

    private void LibRejoin_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button b) return;
        var placeId = Convert.ToInt64(b.Tag);
        var job = S.Recent.FirstOrDefault(r => r.PlaceId == placeId)?.LastJobId;
        if (string.IsNullOrEmpty(job) || !Regex.IsMatch(job, @"^[0-9a-fA-F\-]{8,64}$")) return;

        if (ActiveAccount() != null)
        {
            _ = LaunchAsync(placeId, job);
            return;
        }

        if (!CanUseWebStart()) return;

        _autoLaunchPending = true;
        _autoLaunchPlace = placeId;
        _autoLaunchJob = job;
        _autoLaunchUntil = DateTime.Now.AddSeconds(90);

        SetStatus("Opening Roblox in your browser, then rejoining your last server...", Busy);
        OpenUrl($"https://www.roblox.com/games/start?placeId={placeId}&gameInstanceId={job}");
    }

    // Open the game page to choose a server yourself.
    private void LibServers_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button b) return;
        var placeId = Convert.ToInt64(b.Tag);

        if (_link != null && _link.PlaceId == placeId && !_linkUsed) OpenGame(placeId);
        else OpenPlace(placeId);

        if (ActiveAccount() == null && _link == null)
            SetStatus("Pick an account in the sidebar to join from here.", Busy);
    }

    private void LibStar_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button b) return;
        var placeId = Convert.ToInt64(b.Tag);

        var fav = S.Favorites.FirstOrDefault(f => f.PlaceId == placeId);
        if (fav != null)
        {
            S.Favorites.Remove(fav);
        }
        else
        {
            var src = S.Recent.FirstOrDefault(r => r.PlaceId == placeId);
            if (src != null)
                S.Favorites.Insert(0, new GameEntry
                {
                    PlaceId = src.PlaceId, Name = src.Name, IconUrl = src.IconUrl, LastPlayed = src.LastPlayed
                });
        }
        S.Save();
        RefreshLibrary();
        UpdateFavButton();
    }

    private void TryAutoLaunch()
    {
        if (_directWebLaunchPending)
        {
            if (!_clientReady || _link == null || _linkUsed) return;
            _directWebLaunchPending = false;
            _ = LaunchDirectWebLinkAsync(_link);
            return;
        }

        if (!_autoLaunchPending || _link == null || !_clientReady || _linkUsed) return;

        if (DateTime.Now > _autoLaunchUntil || _link.PlaceId != _autoLaunchPlace)
        {
            _autoLaunchPending = false;
            return;
        }

        _autoLaunchPending = false;
        LaunchGame(_autoLaunchJob);
    }
}
