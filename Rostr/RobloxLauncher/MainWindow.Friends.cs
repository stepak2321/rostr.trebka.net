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
    // ---------- Friends ----------

    private static string FriendTitle(FriendInfo f)
    {
        if (string.IsNullOrEmpty(f.Name)) return string.IsNullOrEmpty(f.DisplayName) ? $"User {f.Id}" : f.DisplayName;
        return string.IsNullOrEmpty(f.DisplayName) || f.DisplayName == f.Name ? f.Name : $"{f.DisplayName} (@{f.Name})";
    }

    private static FriendRow ToFriendRow(FriendInfo f, FriendPresence? p)
    {
        var type = p?.Type ?? 0;
        var loc = p?.Location ?? "";
        return type switch
        {
            2 => new FriendRow
            {
                UserId = f.Id, Title = FriendTitle(f), Order = 0, CanJoin = true,
                Status = string.IsNullOrWhiteSpace(loc) ? "In a game" : "Playing " + loc,
                StatusBrush = Ok, PlaceId = p?.PlaceId ?? 0, Location = loc
            },
            1 => new FriendRow
            {
                UserId = f.Id, Title = FriendTitle(f), Order = 1, CanJoin = false,
                Status = "Online", StatusBrush = Accent
            },
            3 => new FriendRow
            {
                UserId = f.Id, Title = FriendTitle(f), Order = 2, CanJoin = false,
                Status = "In Roblox Studio", StatusBrush = Busy
            },
            _ => new FriendRow
            {
                UserId = f.Id, Title = FriendTitle(f), Order = 3, CanJoin = false,
                Status = "Offline", StatusBrush = Brushes.Gray
            }
        };
    }

    private void ShowFriends()
    {
        var offline = _friendRows.Count(r => r.Order == 3);
        var online = _friendRows.Count - offline;

        var shown = _friendRows
            .Where(r => _showOfflineFriends || r.Order != 3)
            .OrderBy(r => r.Order)
            .ThenBy(r => r.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();

        FriendsList.ItemsSource = shown;
        TranslateList(FriendsList);
        FriendsCount.Text = Loc.T($"{online} online, {offline} offline");
        FriendsOfflineBtn.Content = Loc.T(_showOfflineFriends ? "Offline friends: shown" : "Offline friends: hidden");

        if (shown.Count == 0)
        {
            FriendsEmpty.Text = _friendRows.Count == 0
                ? "This account has no friends yet."
                : "None of your friends are online right now.";
            FriendsEmpty.Visibility = Visibility.Visible;
        }
        else
        {
            FriendsEmpty.Visibility = Visibility.Collapsed;
        }
    }

    // Profile pictures arrive a batch at a time, online friends first.
    private async Task LoadFriendAvatarsAsync(List<FriendRow> rows, int req)
    {
        try
        {
            foreach (var chunk in rows.OrderBy(r => r.Order).Chunk(50))
            {
                if (req != _friendsReq) return;
                var map = await ImageService.EnsureHeadshotsAsync(chunk.Select(r => r.UserId));
                foreach (var r in chunk)
                    if (map.TryGetValue(r.UserId, out var img)) r.Avatar = img;
            }
        }
        catch (Exception ex)
        {
            Log.Warn("Friend pictures: " + ex.Message);
        }
    }

    private async Task RefreshFriendsAsync(bool force)
    {
        var acc = ActiveAccount();
        if (acc == null)
        {
            _friendsReq++;
            _friendRows = new();
            FriendsList.ItemsSource = null;
            FriendsCount.Text = Loc.T("");
            FriendsEmpty.Text = Loc.T("Pick an account in the sidebar to see its friends.");
            FriendsEmpty.Visibility = Visibility.Visible;
            return;
        }

        // Roblox rate limits these endpoints, so do not reload when the data is a few seconds old.
        var age = (DateTime.Now - _friendsLoadedAt).TotalSeconds;
        if (_friendsLoading || (_friendsLoadedFor == acc.UserId && age < (force ? 5 : 30))) return;

        var cookie = AccountStore.GetCookie(acc);
        if (cookie == null)
        {
            FriendsEmpty.Text = Loc.T($"Could not read the saved login for {acc.Name}. Remove the account and add it again.");
            FriendsEmpty.Visibility = Visibility.Visible;
            return;
        }

        var req = ++_friendsReq;
        var redo = false;
        _friendsLoading = true;
        FriendsRefreshBtn.IsEnabled = false;
        FriendsEmpty.Text = Loc.T("Loading friends...");
        FriendsEmpty.Visibility = Visibility.Visible;
        try
        {
            var friends = await RobloxAuth.GetFriendsAsync(cookie, acc.UserId);
            var presence = await RobloxAuth.GetPresenceAsync(cookie, friends.Select(f => f.Id));
            if (req != _friendsReq) return;
            if (ActiveAccount()?.UserId != acc.UserId) { redo = true; return; }   // account changed while loading

            _friendRows = friends
                .Select(f => ToFriendRow(f, presence.TryGetValue(f.Id, out var p) ? p : null))
                .ToList();
            _friendsLoadedAt = DateTime.Now;
            _friendsLoadedFor = acc.UserId;
            ShowFriends();
            _ = LoadFriendAvatarsAsync(_friendRows, req);
        }
        catch (Exception ex)
        {
            if (req != _friendsReq) return;
            Log.Error("Friends load failed", ex);
            FriendsEmpty.Text = Loc.T(ex.Message);
            FriendsEmpty.Visibility = Visibility.Visible;
        }
        finally
        {
            _friendsLoading = false;
            FriendsRefreshBtn.IsEnabled = true;
            if (redo) _ = RefreshFriendsAsync(true);
        }
    }

    private void FriendsRefresh_Click(object sender, RoutedEventArgs e) => _ = RefreshFriendsAsync(true);

    private void FriendsOffline_Click(object sender, RoutedEventArgs e)
    {
        _showOfflineFriends = !_showOfflineFriends;
        ShowFriends();
    }

    private void FriendJoin_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button b) return;
        var id = Convert.ToInt64(b.Tag);
        var row = _friendRows.FirstOrDefault(r => r.UserId == id);
        if (row == null) return;

        if (ActiveAccount() == null)
        {
            SetStatus("Pick an account in the sidebar first.", Bad);
            return;
        }
        _ = LaunchFriendAsync(row);
    }

    private async Task LaunchFriendAsync(FriendRow row)
    {
        var name = string.IsNullOrWhiteSpace(row.Location) ? "Friend's game" : row.Location;
        await LaunchCoreAsync(row.PlaceId, name, null, null, null,
            ticket => RobloxAuth.BuildFollowLink(ticket, row.UserId, row.PlaceId));
    }
}
