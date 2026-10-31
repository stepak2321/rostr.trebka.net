using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using RobloxLauncher.Core;

namespace RobloxLauncher;

public partial class AccountsWindow : Window
{
    public event Action? Changed;

    private static readonly Brush Good = new SolidColorBrush(Color.FromRgb(0x3D, 0xD6, 0x8C));
    private static readonly Brush Bad = new SolidColorBrush(Color.FromRgb(0xF2, 0x5C, 0x5C));
    private static readonly Brush Busy = new SolidColorBrush(Color.FromRgb(0xF5, 0xB8, 0x3D));

    // Receives the main window's resources so buttons look the same as in the main app.
    public AccountsWindow(ResourceDictionary appStyles)
    {
        foreach (System.Collections.DictionaryEntry e in appStyles)
            Resources[e.Key] = e.Value;

        InitializeComponent();
        Loc.Apply(this);
        Render();
    }

    private void Say(string text, Brush color)
    {
        Msg.Text = Loc.T(text);
        Msg.Foreground = color;
    }

    private void Render()
    {
        ListPanel.Children.Clear();
        EmptyText.Visibility = AccountStore.All.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        foreach (var acc in AccountStore.All.ToList())
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var border = new Border
            {
                Background = ThemeManager.B("ThCard"),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(14, 10, 14, 10)
            };
            var inner = new Grid();
            inner.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            inner.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            inner.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var avatar = new Ellipse
            {
                Width = 40,
                Height = 40,
                Margin = new Thickness(0, 0, 12, 0),
                Fill = ThemeManager.B("ThDim"),
                VerticalAlignment = VerticalAlignment.Center
            };
            _ = FillAvatarAsync(avatar, acc.UserId);

            var label = new TextBlock
            {
                Text = acc.Label,
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            Grid.SetColumn(label, 1);

            var remove = new Button
            {
                Content = Loc.T("Remove"),
                FontSize = 12,
                Padding = new Thickness(12, 6, 12, 6),
                Background = ThemeManager.B("ThDim"),
                Tag = acc.UserId
            };
            remove.Click += Remove_Click;
            Grid.SetColumn(remove, 2);

            inner.Children.Add(avatar);
            inner.Children.Add(label);
            inner.Children.Add(remove);
            border.Child = inner;
            Grid.SetColumnSpan(border, 2);
            row.Children.Add(border);
            ListPanel.Children.Add(row);
        }
    }

    private static async Task FillAvatarAsync(Ellipse target, long userId)
    {
        try
        {
            var map = await ImageService.EnsureHeadshotsAsync(new[] { userId });
            if (map.TryGetValue(userId, out var img))
                target.Fill = new ImageBrush(img) { Stretch = Stretch.UniformToFill };
        }
        catch (Exception ex)
        {
            Log.Warn("Account picture: " + ex.Message);
        }
    }

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button b || b.Tag is not long id) return;
        var acc = AccountStore.Find(id);
        if (acc == null) return;

        var ok = MessageBox.Show(this, Loc.T($"Remove {acc.Label} from the launcher?\n\nThis only deletes the saved login here. It does not log the account out of Roblox."),
            Loc.T("Remove account"), MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (ok != MessageBoxResult.Yes) return;

        AccountStore.Remove(id);
        Render();
        Say("Removed.", Good);
        Changed?.Invoke();
    }

    private async void AddBtn_Click(object sender, RoutedEventArgs e)
    {
        var win = new LoginWindow { Owner = this };
        if (win.ShowDialog() == true && win.Cookie != null)
            await AddCookieAsync(win.Cookie);
    }

    private async void CookieAdd_Click(object sender, RoutedEventArgs e)
    {
        var cookie = CookieBox.Password.Trim();
        if (cookie.StartsWith(".ROBLOSECURITY=", StringComparison.OrdinalIgnoreCase))
            cookie = cookie.Substring(".ROBLOSECURITY=".Length);
        cookie = cookie.Trim().TrimEnd(';');

        CookieBox.Clear();
        await AddCookieAsync(cookie);
    }

    private async Task AddCookieAsync(string cookie)
    {
        if (cookie.Length < 50)
        {
            Say("That does not look like a Roblox cookie.", Bad);
            return;
        }

        Say("Checking the login with Roblox...", Busy);
        try
        {
            var user = await RobloxAuth.GetUserAsync(cookie);
            if (user == null)
            {
                Say("Roblox rejected that login. The cookie may be expired or wrong.", Bad);
                return;
            }

            AccountStore.AddOrUpdate(user.Id, user.Name, user.DisplayName, cookie);
            Render();
            Say($"Added {user.Name}.", Good);
            Changed?.Invoke();
        }
        catch (Exception ex)
        {
            Say("Could not add the account: " + ex.Message, Bad);
        }
    }
}
