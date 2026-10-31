using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace RobloxLauncher;

/// <summary>
/// A small vertical dock on the left side of the dim, in the middle of the height. It holds the buttons of the
/// dim overlay: Friends (opens and closes the player list), Wiki (the Fandom wiki of the game), Notes (a note pad per game) and the quick actions
/// Server hop, Rejoin and Copy join link. It never takes the focus from the game.
/// </summary>
public partial class DimDockWindow : Window
{
    private static readonly Brush FriendsOnBrush = new SolidColorBrush(Color.FromRgb(0x6F, 0x95, 0xFF));

    private int _x = int.MinValue, _y, _w, _h;
    private bool _friendsOpen, _wikiOpen, _notesOpen;

    public event Action? FriendsClicked;
    public event Action? WikiClicked;
    public event Action? NotesClicked;
    public event Action? HopClicked;
    public event Action? RejoinClicked;
    public event Func<bool>? CopyLinkClicked;   // returns true when the link was copied

    private static readonly Brush OkBrush = new SolidColorBrush(Color.FromRgb(0x5B, 0xD9, 0x8A));
    private static readonly Brush BadBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x6B));
    private int _flashId;

    public DimDockWindow()
    {
        InitializeComponent();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        OverlayNative.MakeNoActivate(new WindowInteropHelper(this).Handle);
    }

    private void Friends_Click(object sender, RoutedEventArgs e) => FriendsClicked?.Invoke();
    private void Wiki_Click(object sender, RoutedEventArgs e) => WikiClicked?.Invoke();
    private void Notes_Click(object sender, RoutedEventArgs e) => NotesClicked?.Invoke();
    private void Hop_Click(object sender, RoutedEventArgs e) => HopClicked?.Invoke();
    private void Rejoin_Click(object sender, RoutedEventArgs e) => RejoinClicked?.Invoke();

    private async void Link_Click(object sender, RoutedEventArgs e)
    {
        var ok = CopyLinkClicked?.Invoke() ?? false;

        // A check mark (or a cross when there was nothing to copy) for a moment, then the link icon again.
        LinkGlyph.Text = ok ? "\uE73E" : "\uE711";
        LinkGlyph.Foreground = ok ? OkBrush : BadBrush;
        var id = ++_flashId;
        await Task.Delay(1500);
        if (id != _flashId) return;
        LinkGlyph.Text = "\uE71B";
        LinkGlyph.Foreground = Brushes.White;
    }

    /// <summary>Puts the dock on the left side, in the middle of the height. Returns true when it just appeared.</summary>
    public bool Present(IntPtr game, bool friendsOpen, bool wikiOpen, bool notesOpen)
    {
        if (!OverlayNative.GameRect(game, out var gx, out var gy, out var gw, out var gh)) return false;

        if (friendsOpen != _friendsOpen || wikiOpen != _wikiOpen || notesOpen != _notesOpen || !IsVisible)
        {
            _friendsOpen = friendsOpen;
            _wikiOpen = wikiOpen;
            _notesOpen = notesOpen;
            // A button is lit in the accent color while its window is open.
            FriendsGlyph.Foreground = friendsOpen ? FriendsOnBrush : Brushes.White;
            FriendsActive.Visibility = friendsOpen ? Visibility.Visible : Visibility.Collapsed;
            WikiGlyph.Foreground = wikiOpen ? FriendsOnBrush : Brushes.White;
            WikiActive.Visibility = wikiOpen ? Visibility.Visible : Visibility.Collapsed;
            NotesGlyph.Foreground = notesOpen ? FriendsOnBrush : Brushes.White;
            NotesActive.Visibility = notesOpen ? Visibility.Visible : Visibility.Collapsed;
        }

        // Size and place in real pixels, so display scaling cannot move it.
        var dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
        Dock.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var pw = (int)Math.Ceiling(Dock.DesiredSize.Width * dpi);
        var ph = (int)Math.Ceiling(Dock.DesiredSize.Height * dpi);
        var margin = (int)Math.Round(24 * dpi);
        var x = gx + margin;
        var y = gy + (gh - ph) / 2;

        var appeared = !IsVisible;
        if (appeared) Show();

        if (appeared || x != _x || y != _y || pw != _w || ph != _h)
        {
            _x = x; _y = y; _w = pw; _h = ph;
            OverlayNative.SetWindowPos(new WindowInteropHelper(this).Handle,
                appeared ? OverlayNative.HWND_TOPMOST : IntPtr.Zero, x, y, pw, ph,
                OverlayNative.SWP_NOACTIVATE | (appeared ? 0u : OverlayNative.SWP_NOZORDER));
        }
        return appeared;
    }

    /// <summary>Moves this window back above the other topmost windows.</summary>
    public void BringToTop()
    {
        if (!IsVisible) return;
        OverlayNative.SetWindowPos(new WindowInteropHelper(this).Handle, OverlayNative.HWND_TOPMOST, 0, 0, 0, 0,
            OverlayNative.SWP_NOSIZE | OverlayNative.SWP_NOMOVE | OverlayNative.SWP_NOACTIVATE);
    }

    public void HideDock()
    {
        if (!IsVisible) return;
        Hide();
        _x = int.MinValue;
    }
}
