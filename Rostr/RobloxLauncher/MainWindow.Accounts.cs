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
    // ---------- Accounts ----------

    private SavedAccount? ActiveAccount() =>
        S.ActiveAccountId == 0 ? null : AccountStore.Find(S.ActiveAccountId);

    private void UpdateAccountUi()
    {
        var acc = ActiveAccount();
        if (acc == null && S.ActiveAccountId != 0)
        {
            S.ActiveAccountId = 0;   // the saved account was removed
            S.Save();
        }
        AccountBtnText.Text = acc?.Name ?? Loc.T("Browser login");
        AccountAvatar.Visibility = Visibility.Collapsed;
        AccountGlyph.Visibility = Visibility.Visible;
        UpdatePlayButtons();
        _ = UpdateAccountAvatarsAsync();
    }

    // Loads the profile pictures of all saved accounts (the menu uses them too) and shows the active one.
    private async Task UpdateAccountAvatarsAsync()
    {
        try
        {
            if (AccountStore.All.Count == 0) return;
            var map = await ImageService.EnsureHeadshotsAsync(AccountStore.All.Select(a => a.UserId));
            var active = ActiveAccount();
            if (active != null && map.TryGetValue(active.UserId, out var img))
            {
                AccountAvatar.Fill = new ImageBrush(img) { Stretch = Stretch.UniformToFill };
                AccountAvatar.Visibility = Visibility.Visible;
                AccountGlyph.Visibility = Visibility.Collapsed;
            }
        }
        catch (Exception ex)
        {
            Log.Warn("Account pictures: " + ex.Message);
        }
    }

    private static FrameworkElement MenuHeader(string text, long userId = 0)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        var img = userId == 0 ? null : ImageService.CachedHeadshot(userId);
        if (img != null)
        {
            panel.Children.Add(new System.Windows.Shapes.Ellipse
            {
                Width = 22,
                Height = 22,
                Margin = new Thickness(0, 0, 8, 0),
                Fill = new ImageBrush(img) { Stretch = Stretch.UniformToFill }
            });
        }
        // A TextBlock, so underscores in usernames are not eaten as access keys.
        panel.Children.Add(new TextBlock
        {
            Text = text,
            Foreground = Brushes.White,
            VerticalAlignment = VerticalAlignment.Center
        });
        return panel;
    }

    private void SetActiveAccount(long id)
    {
        S.ActiveAccountId = id;
        S.Save();
        UpdateAccountUi();
        _friendsLoadedFor = 0;
        if (FriendsPage.Visibility == Visibility.Visible) _ = RefreshFriendsAsync(true);
        SetStatus(id == 0
            ? "Using the login from the roblox.com Play button."
            : $"Playing as {ActiveAccount()?.Name}.", Ok);
    }

    private void AccountBtn_Click(object sender, RoutedEventArgs e)
    {
        var white = Brushes.White;
        var menu = new ContextMenu
        {
            PlacementTarget = AccountBtn,
            Placement = PlacementMode.Top,
            Style = (Style)FindResource("DarkMenu"),
            Foreground = white
        };

        foreach (var acc in AccountStore.All)
        {
            var id = acc.UserId;
            var item = new MenuItem
            {
                Style = (Style)FindResource("DarkMenuItem"),
                Header = MenuHeader(acc.Label, id),
                IsChecked = S.ActiveAccountId == id
            };
            item.Click += (_, _) => SetActiveAccount(id);
            menu.Items.Add(item);
        }

        var web = new MenuItem
        {
            Style = (Style)FindResource("DarkMenuItem"),
            Header = new TextBlock { Text = Loc.T("Browser login (roblox.com Play button)"), Foreground = white },
            IsChecked = S.ActiveAccountId == 0
        };
        web.Click += (_, _) => SetActiveAccount(0);
        menu.Items.Add(web);

        menu.Items.Add(new Separator { Style = (Style)FindResource("DarkSeparator") });

        var manage = new MenuItem { Style = (Style)FindResource("DarkMenuItem"), Header = new TextBlock { Text = Loc.T("Manage accounts..."), Foreground = white } };
        manage.Click += (_, _) => OpenAccounts();
        menu.Items.Add(manage);

        menu.IsOpen = true;
    }

    private void OpenAccounts()
    {
        var win = new AccountsWindow(Resources) { Owner = this };
        win.Changed += () =>
        {
            // The first account you add becomes the active one.
            if (S.ActiveAccountId == 0 && AccountStore.All.Count == 1)
            {
                S.ActiveAccountId = AccountStore.All[0].UserId;
                S.Save();
            }
            UpdateAccountUi();
        };
        win.ShowDialog();
        UpdateAccountUi();
    }
}
