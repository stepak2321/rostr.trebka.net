using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Draw = System.Drawing;
using Forms = System.Windows.Forms;

namespace RobloxLauncher.Core;

/// <summary>
/// One line of the tray menu. A plain entry is a clickable row. Toggle (or Checked) shows a small switch and keeps the
/// menu open. Header with a Hint is the big title card at the top; a Header without a Hint is a small section label.
/// </summary>
public sealed record TrayEntry(string Text, Action? Click = null, bool Checked = false,
                               bool Bold = false, bool Header = false, bool Separator = false,
                               string? Glyph = null, bool Toggle = false, bool Danger = false,
                               string? Hint = null, string? Badge = null, bool BadgeOn = false)
{
    public static TrayEntry Sep() => new("", Separator: true);
}

/// <summary>A button on a tray notice ("Open Rostr", "Exit").</summary>
public sealed record TrayAction(string Label, Action Run, bool Primary = false, bool Danger = false);

/// <summary>
/// The Rostr icon in the Windows notification area (system tray). Uses the NotifyIcon that comes with
/// Windows Forms only for the icon itself. Left click opens Rostr, right click opens a flyout that Rostr draws itself
/// (rounded, in the launcher's theme colors), and notices ("Rostr is still running") are small cards in the corner
/// of the screen instead of the default Windows balloon. The flyout is rebuilt every time it opens, so it always
/// shows the current state.
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private readonly Forms.NotifyIcon _icon;
    private readonly Func<IEnumerable<TrayEntry>> _build;
    private TrayFlyout? _flyout;
    private TrayToast? _toast;
    private DateTime _flyoutClosedUtc = DateTime.MinValue;

    public event Action? OpenRequested;

    public TrayIcon(Func<IEnumerable<TrayEntry>> buildMenu)
    {
        _build = buildMenu;

        _icon = new Forms.NotifyIcon
        {
            Icon = LoadIcon(),
            Text = "Rostr",
            Visible = true
        };
        _icon.MouseUp += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left) OpenRequested?.Invoke();
            else if (e.Button == Forms.MouseButtons.Right) ShowMenu();
        };
    }

    private void ShowMenu()
    {
        // The click that closed the flyout (it loses focus when you click the tray icon) must not open it again.
        if ((DateTime.UtcNow - _flyoutClosedUtc).TotalMilliseconds < 300) return;

        try
        {
            _flyout?.Close();
            var flyout = new TrayFlyout(_build);
            flyout.Closed += (_, _) =>
            {
                _flyoutClosedUtc = DateTime.UtcNow;
                if (ReferenceEquals(_flyout, flyout)) _flyout = null;
            };
            _flyout = flyout;
            flyout.Show();
        }
        catch (Exception ex)
        {
            Log.Warn("Tray menu failed: " + ex.Message);
        }
    }

    /// <summary>A short notice in the corner of the screen (replaces the Windows balloon).</summary>
    public void ShowBalloon(string title, string text) => ShowNotice(title, text, null, 5);

    /// <summary>
    /// A notice card with optional buttons. Clicking the card runs onClick (if given) and closes it; the small X only closes it.
    /// It fades out by itself and stays while the mouse is over it.
    /// </summary>
    public void ShowNotice(string title, string text, IReadOnlyList<TrayAction>? actions = null, double seconds = 6,
                           Action? onClick = null)
    {
        var app = Application.Current;
        if (app == null) return;
        if (!app.Dispatcher.CheckAccess())
        {
            app.Dispatcher.BeginInvoke(new Action(() => ShowNotice(title, text, actions, seconds, onClick)));
            return;
        }

        try
        {
            _toast?.CloseNow();
            var toast = new TrayToast(Loc.T(title), Loc.T(text), actions ?? Array.Empty<TrayAction>(), seconds, onClick);
            toast.Closed += (_, _) =>
            {
                if (ReferenceEquals(_toast, toast)) _toast = null;
            };
            _toast = toast;
            toast.Show();
        }
        catch (Exception ex)
        {
            Log.Warn("Tray notice failed: " + ex.Message);
        }
    }

    // The exe already carries the Rostr icon (ApplicationIcon in the project file).
    private static Draw.Icon LoadIcon()
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(exe))
            {
                var icon = Draw.Icon.ExtractAssociatedIcon(exe);
                if (icon != null) return icon;
            }
        }
        catch { }
        return Draw.SystemIcons.Application;
    }

    public void Dispose()
    {
        try { _flyout?.Close(); } catch { }
        try { _toast?.CloseNow(); } catch { }

        // Hide first, otherwise Windows keeps a dead icon in the tray until the mouse moves over it.
        _icon.Visible = false;
        _icon.Dispose();
    }
}

/// <summary>Shared look of the flyout and the notice: theme colors, icon font, logo, buttons, window helpers.</summary>
internal static class TrayUi
{
    public static readonly FontFamily IconFont = new("Segoe Fluent Icons, Segoe MDL2 Assets");
    public static readonly FontFamily TextFont = new("Segoe UI Variable Text, Segoe UI");
    public static readonly Color Green = Color.FromRgb(0x3D, 0xD6, 0x8C);
    public static readonly Color Red = Color.FromRgb(0xE5, 0x48, 0x4D);

    /// <summary>The current theme brush. Windows are short-lived, so a theme change shows the next time one opens.</summary>
    public static Brush Th(string key) => ThemeManager.B(key);

    public static Color ThColor(string key) => ThemeManager.B(key) is SolidColorBrush b ? b.Color : Colors.Gray;

    public static Brush Tint(Color c, byte alpha)
    {
        var b = new SolidColorBrush(Color.FromArgb(alpha, c.R, c.G, c.B));
        b.Freeze();
        return b;
    }

    private static Brush? _logo;

    /// <summary>The Rostr logo as a brush (built into the exe), or the accent color if it cannot be loaded.</summary>
    public static Brush LogoBrush()
    {
        if (_logo != null) return _logo;
        try
        {
            var img = new BitmapImage();
            img.BeginInit();
            img.UriSource = new Uri("pack://application:,,,/Assets/logo.png", UriKind.Absolute);
            img.CacheOption = BitmapCacheOption.OnLoad;
            img.EndInit();
            img.Freeze();
            var brush = new ImageBrush(img) { Stretch = Stretch.UniformToFill };
            brush.Freeze();
            _logo = brush;
        }
        catch
        {
            return Th("ThAccent");
        }
        return _logo;
    }

    private static ControlTemplate? _buttonTemplate;

    private static ControlTemplate ButtonTemplate() => _buttonTemplate ??= (ControlTemplate)System.Windows.Markup.XamlReader.Parse(
        "<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' " +
        "xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='Button'>" +
        "<Border x:Name='bd' Background='{TemplateBinding Background}' CornerRadius='9' Padding='{TemplateBinding Padding}'>" +
        "<ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/></Border>" +
        "<ControlTemplate.Triggers>" +
        "<Trigger Property='IsMouseOver' Value='True'><Setter TargetName='bd' Property='Opacity' Value='0.82'/></Trigger>" +
        "<Trigger Property='IsPressed' Value='True'><Setter TargetName='bd' Property='Opacity' Value='0.65'/></Trigger>" +
        "</ControlTemplate.Triggers></ControlTemplate>");

    public static Button MakeButton(string text, Brush background, Brush foreground, Action onClick)
    {
        var b = new Button
        {
            Content = text,
            Background = background,
            Foreground = foreground,
            Template = ButtonTemplate(),
            Padding = new Thickness(14, 7, 14, 7),
            Margin = new Thickness(0, 0, 8, 0),
            FontSize = 12.5,
            FontWeight = FontWeights.SemiBold,
            Cursor = Cursors.Hand,
            BorderThickness = new Thickness(0),
            Focusable = false
        };
        b.Click += (_, _) => onClick();
        return b;
    }

    public static DropShadowEffect Shadow() => new()
    {
        BlurRadius = 24, ShadowDepth = 4, Direction = 270, Opacity = 0.45, Color = Colors.Black
    };

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x80;

    /// <summary>Keeps the window out of Alt+Tab.</summary>
    public static void HideFromAltTab(Window w)
    {
        try
        {
            var h = new WindowInteropHelper(w).Handle;
            if (h == IntPtr.Zero) return;
            SetWindowLong(h, GWL_EXSTYLE, GetWindowLong(h, GWL_EXSTYLE) | WS_EX_TOOLWINDOW);
        }
        catch { }
    }

    public static void ForceForeground(Window w)
    {
        try { SetForegroundWindow(new WindowInteropHelper(w).Handle); }
        catch { }
    }
}

/// <summary>The tray menu: a rounded card with a title header, icon rows, switches and a red Exit row.</summary>
internal sealed class TrayFlyout : Window
{
    private readonly Func<IEnumerable<TrayEntry>> _build;
    private readonly StackPanel _root = new();
    private readonly Border _card;
    private readonly DispatcherTimer _watch = new() { Interval = TimeSpan.FromMilliseconds(300) };
    private bool _placed;
    private bool _closing;
    private DateTime _openedUtc;

    public TrayFlyout(Func<IEnumerable<TrayEntry>> build)
    {
        _build = build;

        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;
        FontFamily = TrayUi.TextFont;
        Title = "Rostr";
        Opacity = 0;   // invisible until it is placed next to the tray icon

        _card = new Border
        {
            Width = 284,
            Background = TrayUi.Th("ThBg2"),
            BorderBrush = TrayUi.Th("ThLine"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Child = _root,
            Effect = TrayUi.Shadow()
        };
        // The 16 px margin around the card is room for its shadow.
        Content = new Grid { Margin = new Thickness(16), Children = { _card } };

        SourceInitialized += (_, _) => TrayUi.HideFromAltTab(this);
        ContentRendered += (_, _) => OnRendered();
        Deactivated += (_, _) => { if (_placed) CloseSafe(); };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            e.Handled = true;
            CloseSafe();
        };
        // If Windows never gave the flyout focus, it would stay open forever. This closes it then.
        _watch.Tick += (_, _) =>
        {
            if (_placed && !IsActive && !IsMouseOver && (DateTime.UtcNow - _openedUtc).TotalMilliseconds > 600)
                CloseSafe();
        };
        Closed += (_, _) => _watch.Stop();

        Render();
    }

    private void CloseSafe()
    {
        if (_closing) return;
        _closing = true;
        try { Close(); } catch { }
    }

    private void OnRendered()
    {
        var above = Place();
        _placed = true;
        _openedUtc = DateTime.UtcNow;
        TrayUi.ForceForeground(this);
        Activate();
        _watch.Start();

        var from = above ? 10 : -10;
        var slide = new TranslateTransform(0, from);
        _card.RenderTransform = slide;
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(120)));
        slide.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(from, 0, TimeSpan.FromMilliseconds(170))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });
    }

    // Puts the flyout next to the mouse (which is on the tray icon), inside the work area of that screen.
    // Returns true when it opened above the mouse, false when the taskbar is at the top and it opened below.
    private bool Place()
    {
        var fromDevice = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        var cursor = Forms.Cursor.Position;
        var area = Forms.Screen.FromPoint(cursor).WorkingArea;

        var topLeft = fromDevice.Transform(new Point(area.Left, area.Top));
        var bottomRight = fromDevice.Transform(new Point(area.Right, area.Bottom));
        var mouse = fromDevice.Transform(new Point(cursor.X, cursor.Y));

        double w = ActualWidth, h = ActualHeight;
        const double edge = -10;   // lets the transparent shadow margin hang over the edge, so the card sits close to it

        var above = mouse.Y - h - 6 >= topLeft.Y;
        var left = mouse.X - w / 2;
        var top = above ? mouse.Y - h - 6 : mouse.Y + 6;

        Left = Math.Max(topLeft.X + edge, Math.Min(left, bottomRight.X - w - edge));
        Top = Math.Max(topLeft.Y + edge, Math.Min(top, bottomRight.Y - h - edge));
        return above;
    }

    // Draws all rows from the current state of the app.
    private void Render()
    {
        _root.Children.Clear();

        List<TrayEntry> entries;
        try { entries = _build().ToList(); }
        catch (Exception ex)
        {
            Log.Warn("Tray menu failed: " + ex.Message);
            CloseSafe();
            return;
        }

        var rows = new StackPanel { Margin = new Thickness(6, 6, 6, 8) };
        UIElement? header = null;

        foreach (var e in entries)
        {
            if (e.Separator) { rows.Children.Add(Divider(new Thickness(10, 5, 10, 5))); continue; }
            if (e.Header && e.Hint != null) { header = BuildHeader(e); continue; }
            if (e.Header) { rows.Children.Add(SectionLabel(e.Text)); continue; }
            rows.Children.Add(BuildRow(e));
        }

        if (header != null)
        {
            _root.Children.Add(header);
            _root.Children.Add(Divider(new Thickness(0)));
        }
        _root.Children.Add(rows);
    }

    private static UIElement Divider(Thickness margin) =>
        new Border { Height = 1, Background = TrayUi.Th("ThLine"), Margin = margin };

    private static UIElement SectionLabel(string text) => new TextBlock
    {
        Text = text.ToUpperInvariant(),
        FontSize = 10.5,
        FontWeight = FontWeights.SemiBold,
        Foreground = TrayUi.Th("ThFaint"),
        Margin = new Thickness(12, 10, 12, 3)
    };

    // Logo, "Rostr", version and account, and a small pill that says if Roblox is running.
    private static UIElement BuildHeader(TrayEntry e)
    {
        var grid = new Grid { Margin = new Thickness(14, 14, 14, 13) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var logo = new Border
        {
            Width = 38, Height = 38, CornerRadius = new CornerRadius(11),
            Background = TrayUi.LogoBrush(), VerticalAlignment = VerticalAlignment.Center
        };
        grid.Children.Add(logo);

        var texts = new StackPanel { Margin = new Thickness(11, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        texts.Children.Add(new TextBlock
        {
            Text = e.Text, FontSize = 15, FontWeight = FontWeights.SemiBold, Foreground = TrayUi.Th("ThText")
        });
        texts.Children.Add(new TextBlock
        {
            Text = e.Hint, FontSize = 11.5, Foreground = TrayUi.Th("ThMuted"),
            TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 1, 0, 0)
        });
        Grid.SetColumn(texts, 1);
        grid.Children.Add(texts);

        if (!string.IsNullOrEmpty(e.Badge))
        {
            var dotColor = e.BadgeOn ? TrayUi.Green : TrayUi.ThColor("ThFaint");
            var pill = new StackPanel { Orientation = Orientation.Horizontal };
            pill.Children.Add(new System.Windows.Shapes.Ellipse
            {
                Width = 6, Height = 6, Fill = new SolidColorBrush(dotColor),
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0)
            });
            pill.Children.Add(new TextBlock
            {
                Text = e.Badge, FontSize = 11, FontWeight = FontWeights.SemiBold,
                Foreground = e.BadgeOn ? new SolidColorBrush(TrayUi.Green) : TrayUi.Th("ThMuted")
            });
            var badge = new Border
            {
                CornerRadius = new CornerRadius(10), Padding = new Thickness(8, 4, 9, 4),
                Background = e.BadgeOn ? TrayUi.Tint(TrayUi.Green, 0x26) : TrayUi.Th("ThDim"),
                VerticalAlignment = VerticalAlignment.Center, Child = pill
            };
            Grid.SetColumn(badge, 2);
            grid.Children.Add(badge);
        }
        return grid;
    }

    private UIElement BuildRow(TrayEntry e)
    {
        var enabled = e.Click != null;
        var toggle = e.Toggle || e.Checked;
        var accent = TrayUi.ThColor("ThAccent");

        Brush rest = e.Bold ? TrayUi.Tint(accent, 0x22) : Brushes.Transparent;
        Brush hover = e.Danger ? TrayUi.Tint(TrayUi.Red, 0x26)
                    : e.Bold ? TrayUi.Tint(accent, 0x3A)
                    : TrayUi.Th("ThDim");
        Brush text = e.Danger ? new SolidColorBrush(TrayUi.Red)
                   : enabled ? TrayUi.Th("ThText")
                   : TrayUi.Th("ThFaint");
        Brush icon = e.Danger ? new SolidColorBrush(TrayUi.Red)
                   : e.Bold ? TrayUi.Th("ThAccent")
                   : TrayUi.Th("ThSoft");

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        grid.Children.Add(new TextBlock
        {
            Text = e.Glyph ?? "", FontFamily = TrayUi.IconFont, FontSize = 15, Foreground = icon,
            VerticalAlignment = VerticalAlignment.Center
        });

        var label = new TextBlock
        {
            Text = e.Text, FontSize = 13.5, Foreground = text,
            FontWeight = e.Bold ? FontWeights.SemiBold : FontWeights.Normal,
            TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(label, 1);
        grid.Children.Add(label);

        UIElement? right = null;
        if (toggle) right = Switch(e.Checked);
        else if (!string.IsNullOrEmpty(e.Hint))
            right = new TextBlock
            {
                Text = e.Hint, FontSize = 11.5, Foreground = TrayUi.Th("ThMuted"),
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0)
            };
        if (right != null)
        {
            Grid.SetColumn(right, 2);
            grid.Children.Add(right);
        }

        var row = new Border
        {
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(10, 8, 10, 8),
            Margin = new Thickness(0, 1, 0, 1),
            Background = rest,
            Cursor = enabled ? Cursors.Hand : Cursors.Arrow,
            Child = grid
        };

        if (!enabled) return row;

        row.MouseEnter += (_, _) => row.Background = hover;
        row.MouseLeave += (_, _) => row.Background = rest;
        row.MouseLeftButtonUp += (_, ev) =>
        {
            ev.Handled = true;
            var action = e.Click!;
            if (toggle)
            {
                // A switch keeps the menu open and redraws it, so you see the new state.
                try { action(); }
                catch (Exception ex) { Log.Warn("Tray action failed: " + ex.Message); }
                Dispatcher.BeginInvoke(new Action(Render), DispatcherPriority.Background);
                return;
            }

            CloseSafe();
            Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
            {
                try { action(); }
                catch (Exception ex) { Log.Warn("Tray action failed: " + ex.Message); }
            }));
        };
        return row;
    }

    private static UIElement Switch(bool on)
    {
        var knob = new System.Windows.Shapes.Ellipse
        {
            Width = 12, Height = 12, Fill = Brushes.White,
            HorizontalAlignment = on ? HorizontalAlignment.Right : HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(3, 0, 3, 0)
        };
        return new Border
        {
            Width = 34, Height = 18, CornerRadius = new CornerRadius(9),
            Background = on ? TrayUi.Th("ThAccent") : TrayUi.Th("ThTrack"),
            VerticalAlignment = VerticalAlignment.Center, Child = knob
        };
    }
}

/// <summary>A small card in the bottom right corner: logo, title, text and optional buttons. Replaces the Windows balloon.</summary>
internal sealed class TrayToast : Window
{
    private readonly double _seconds;
    private readonly ScaleTransform _timer = new(1, 1);
    private AnimationClock? _clock;
    private bool _closing;

    public TrayToast(string title, string text, IReadOnlyList<TrayAction> actions, double seconds, Action? onClick = null)
    {
        _seconds = seconds;

        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;   // never steals the keyboard from the game
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;
        FontFamily = TrayUi.TextFont;
        Title = "Rostr";
        Opacity = 0;

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        grid.Children.Add(new Border
        {
            Width = 40, Height = 40, CornerRadius = new CornerRadius(11),
            Background = TrayUi.LogoBrush(), VerticalAlignment = VerticalAlignment.Top
        });

        var texts = new StackPanel { Margin = new Thickness(12, 1, 8, 0) };
        texts.Children.Add(new TextBlock
        {
            Text = title, FontSize = 14.5, FontWeight = FontWeights.SemiBold,
            Foreground = TrayUi.Th("ThText"), TextTrimming = TextTrimming.CharacterEllipsis
        });
        texts.Children.Add(new TextBlock
        {
            Text = text, FontSize = 12.5, LineHeight = 17, TextWrapping = TextWrapping.Wrap,
            Foreground = TrayUi.Th("ThSoft"), Margin = new Thickness(0, 3, 0, 0)
        });
        Grid.SetColumn(texts, 1);
        grid.Children.Add(texts);

        var closeBrush = TrayUi.Th("ThDim");
        var close = new Border
        {
            Width = 26, Height = 26, CornerRadius = new CornerRadius(8), Background = Brushes.Transparent,
            VerticalAlignment = VerticalAlignment.Top, Cursor = Cursors.Hand,
            Child = new TextBlock
            {
                Text = "\uE711", FontFamily = TrayUi.IconFont, FontSize = 11, Foreground = TrayUi.Th("ThMuted"),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
            }
        };
        close.MouseEnter += (_, _) => close.Background = closeBrush;
        close.MouseLeave += (_, _) => close.Background = Brushes.Transparent;
        close.MouseLeftButtonUp += (_, e) => { e.Handled = true; Dismiss(); };
        Grid.SetColumn(close, 2);
        grid.Children.Add(close);

        if (actions.Count > 0)
        {
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(52, 13, 0, 0) };
            foreach (var a in actions)
            {
                var run = a.Run;
                Brush bg = a.Primary ? TrayUi.Th("ThAccent") : TrayUi.Th("ThDim");
                Brush fg = a.Primary ? Brushes.White
                         : a.Danger ? new SolidColorBrush(TrayUi.Red)
                         : TrayUi.Th("ThText2");
                buttons.Children.Add(TrayUi.MakeButton(a.Label, bg, fg, () =>
                {
                    Dismiss();
                    Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        try { run(); }
                        catch (Exception ex) { Log.Warn("Tray notice action failed: " + ex.Message); }
                    }));
                }));
            }
            Grid.SetRow(buttons, 1);
            Grid.SetColumnSpan(buttons, 3);
            grid.Children.Add(buttons);
        }

        // A thin bar that runs out: shows how long the card stays (it waits while the mouse is over it).
        var fill = new Border
        {
            Height = 3, CornerRadius = new CornerRadius(2), Background = TrayUi.Th("ThAccent"),
            RenderTransformOrigin = new Point(0, 0.5), RenderTransform = _timer
        };
        var bar = new Grid { Margin = new Thickness(0, 14, 0, 0), Height = 3 };
        bar.Children.Add(new Border { CornerRadius = new CornerRadius(2), Background = TrayUi.Th("ThTrack") });
        bar.Children.Add(fill);
        Grid.SetRow(bar, 2);
        Grid.SetColumnSpan(bar, 3);
        grid.Children.Add(bar);

        var card = new Border
        {
            Width = 372,
            Background = TrayUi.Th("ThBg2"),
            BorderBrush = TrayUi.Th("ThLine"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(16, 16, 12, 14),
            Child = grid,
            Effect = TrayUi.Shadow(),
            RenderTransform = new TranslateTransform(36, 0)
        };
        Content = new Grid { Margin = new Thickness(16), Children = { card } };

        // Click anywhere on the card to open Rostr. The X handles its own click, so it only closes the card.
        if (onClick != null)
        {
            card.Cursor = Cursors.Hand;
            card.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                Dismiss();
                Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
                {
                    try { onClick(); }
                    catch (Exception ex) { Log.Warn("Tray notice click failed: " + ex.Message); }
                }));
            };
        }

        SourceInitialized += (_, _) => TrayUi.HideFromAltTab(this);
        ContentRendered += (_, _) => OnRendered(card);
        MouseEnter += (_, _) => _clock?.Controller?.Pause();
        MouseLeave += (_, _) => _clock?.Controller?.Resume();
    }

    private void OnRendered(Border card)
    {
        // Bottom right corner of the work area (above the taskbar), with a small gap.
        var area = SystemParameters.WorkArea;
        Left = area.Right - ActualWidth + 6;
        Top = area.Bottom - ActualHeight + 6;

        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160)));
        ((TranslateTransform)card.RenderTransform).BeginAnimation(TranslateTransform.XProperty,
            new DoubleAnimation(36, 0, TimeSpan.FromMilliseconds(220))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });

        var countdown = new DoubleAnimation(1, 0, TimeSpan.FromSeconds(_seconds));
        _clock = countdown.CreateClock();
        _clock.Completed += (_, _) => Dismiss();
        _timer.ApplyAnimationClock(ScaleTransform.ScaleXProperty, _clock);
    }

    /// <summary>Fades the card out and closes it.</summary>
    public void Dismiss()
    {
        if (_closing) return;
        _closing = true;
        try
        {
            var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(160));
            fade.Completed += (_, _) => { try { Close(); } catch { } };
            BeginAnimation(OpacityProperty, fade);
        }
        catch
        {
            try { Close(); } catch { }
        }
    }

    /// <summary>Closes it right away (a newer notice is replacing it, or Rostr is exiting).</summary>
    public void CloseNow()
    {
        _closing = true;
        try { Close(); } catch { }
    }
}
