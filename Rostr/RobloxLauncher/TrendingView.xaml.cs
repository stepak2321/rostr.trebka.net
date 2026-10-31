using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using RobloxLauncher.Core;

namespace RobloxLauncher;

/// <summary>
/// Trending page: the games on the GitHub list (most players first) and, for the game you click,
/// a graph of its player count with date, time and number of players on hover.
/// </summary>
public partial class TrendingView : UserControl
{
    private List<TrendGame> _games = new();
    private readonly Dictionary<long, Border> _rows = new();
    private readonly Dictionary<long, Border> _icons = new();
    private long _selected;
    private int _hours = 24;
    private List<TrendPoint> _points = new();
    private List<TrendPoint> _shown = new();
    private DateTime _loadedAt = DateTime.MinValue;
    private int _listToken, _historyToken;

    // Where the graph is drawn (set by Redraw, used by the mouse hover).
    private double _pl, _pt, _pw, _ph, _tMin, _tSpan, _yMax;
    private Line? _hoverLine;
    private Ellipse? _hoverDot;
    private Border? _tip;
    private TextBlock? _tipText;

    public TrendingView()
    {
        InitializeComponent();
        UpdateRangeButtons();
    }

    private static Brush Th(string key) => ThemeManager.B(key);

    /// <summary>Called each time the page is opened. Loads the list when it is empty or more than 5 minutes old.</summary>
    public void Activate()
    {
        Loc.Apply(this);
        UpdateRangeButtons();
        if (_games.Count == 0 || DateTime.Now - _loadedAt > TimeSpan.FromMinutes(5))
            _ = LoadListAsync(false);
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => _ = LoadListAsync(true);

    // ---------- Game list ----------

    private async Task LoadListAsync(bool force)
    {
        var token = ++_listToken;
        RefreshBtn.IsEnabled = false;
        if (_games.Count == 0) ShowListMessage("Loading...");

        try
        {
            var (games, updated) = await Trending.GetListAsync(force);
            if (token != _listToken) return;

            _games = games;
            _loadedAt = DateTime.Now;
            UpdatedText.Text = Loc.T($"Updated {updated:d MMM, HH:mm}");

            BuildList();
            if (games.Count == 0) ShowListMessage("No games on the list yet.");
            else ListMessage.Visibility = Visibility.Collapsed;

            // The open game gets its newest numbers too.
            var open = games.FirstOrDefault(g => g.PlaceId == _selected);
            if (open != null) _ = ShowHistoryAsync(open, force);
        }
        catch (Exception ex)
        {
            Log.Warn("Trending list: " + ex.Message);
            if (token == _listToken && _games.Count == 0)
                ShowListMessage("Could not load the list. Check your connection and try Refresh.");
        }
        finally
        {
            if (token == _listToken) RefreshBtn.IsEnabled = true;
        }
    }

    private void ShowListMessage(string english)
    {
        ListMessage.Text = Loc.T(english);
        ListMessage.Visibility = Visibility.Visible;
    }

    private void BuildList()
    {
        ListPanel.Children.Clear();
        _rows.Clear();
        _icons.Clear();

        for (var i = 0; i < _games.Count; i++)
        {
            var row = MakeRow(i + 1, _games[i]);
            ListPanel.Children.Add(row);
            _rows[_games[i].PlaceId] = row;
        }
        HighlightSelected();
        _ = LoadIconsAsync(_games.ToList());
    }

    private Border MakeRow(int rank, TrendGame g)
    {
        var icon = new Border
        {
            Width = 40, Height = 40, CornerRadius = new CornerRadius(8),
            Background = Th("ThDim"), Margin = new Thickness(0, 0, 10, 0)
        };
        _icons[g.PlaceId] = icon;

        var rankText = new TextBlock
        {
            Text = rank.ToString(), Width = 26, FontSize = 12, FontWeight = FontWeights.SemiBold,
            Foreground = Th("ThMuted"), VerticalAlignment = VerticalAlignment.Center
        };

        var name = new TextBlock
        {
            Text = g.Name, FontSize = 13, FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis, Foreground = Th("ThText")
        };
        var sub = new TextBlock
        {
            Text = Loc.T($"{g.Playing:N0} playing"), FontSize = 11.5, Foreground = Th("ThMuted"),
            TextTrimming = TextTrimming.CharacterEllipsis
        };

        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        texts.Children.Add(name);
        texts.Children.Add(sub);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(icon, 1);
        Grid.SetColumn(texts, 2);
        grid.Children.Add(rankText);
        grid.Children.Add(icon);
        grid.Children.Add(texts);

        var row = new Border
        {
            Child = grid, Padding = new Thickness(8, 7, 8, 7), CornerRadius = new CornerRadius(10),
            Background = Brushes.Transparent, Cursor = Cursors.Hand, Margin = new Thickness(0, 0, 0, 2),
            ToolTip = g.Name
        };
        row.MouseEnter += (_, _) => { if (g.PlaceId != _selected) row.Background = Th("ThDim"); };
        row.MouseLeave += (_, _) => { if (g.PlaceId != _selected) row.Background = Brushes.Transparent; };
        row.MouseLeftButtonUp += (_, _) => Select(g);
        return row;
    }

    private async Task LoadIconsAsync(List<TrendGame> games)
    {
        try
        {
            var urls = new Dictionary<long, string>();
            foreach (var chunk in games.Select(g => g.UniverseId).Where(u => u > 0).Distinct().Chunk(50))
                foreach (var kv in await RobloxApi.GetIconUrlsAsync(chunk))
                    urls[kv.Key] = kv.Value;

            foreach (var g in games)
            {
                if (!urls.TryGetValue(g.UniverseId, out var url)) continue;
                var img = await ImageService.LoadAsync(url);
                if (img != null && _icons.TryGetValue(g.PlaceId, out var box))
                    box.Background = new ImageBrush(img) { Stretch = Stretch.UniformToFill };
            }
        }
        catch (Exception ex)
        {
            Log.Warn("Trending icons: " + ex.Message);
        }
    }

    private void HighlightSelected()
    {
        foreach (var kv in _rows)
            kv.Value.Background = kv.Key == _selected ? Th("ThAccentBg") : Brushes.Transparent;
    }

    private void Select(TrendGame g)
    {
        _selected = g.PlaceId;
        HighlightSelected();
        PickText.Visibility = Visibility.Collapsed;
        DetailGrid.Visibility = Visibility.Visible;
        DetailName.Text = g.Name;
        _ = ShowHistoryAsync(g, false);
    }

    // ---------- Graph ----------

    private async Task ShowHistoryAsync(TrendGame g, bool force)
    {
        var token = ++_historyToken;
        if (!force || _points.Count == 0)
        {
            _points = new();
            Chart.Children.Clear();
            StatNow.Text = StatPeak.Text = StatLow.Text = "-";
            ShowChartMessage("Loading...");
        }

        try
        {
            var points = await Trending.GetHistoryAsync(g.PlaceId, force);
            if (token != _historyToken) return;
            _points = points;
            Redraw();
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            // Added to the list, but the workflow has not counted it yet.
            if (token != _historyToken) return;
            _points = new();
            Chart.Children.Clear();
            ShowChartMessage("Not enough data yet. Check back in a few hours.");
        }
        catch (Exception ex)
        {
            Log.Warn("Trending history: " + ex.Message);
            if (token != _historyToken) return;
            _points = new();
            Chart.Children.Clear();
            ShowChartMessage("Could not load the list. Check your connection and try Refresh.");
        }
    }

    private void ShowChartMessage(string english)
    {
        ChartMessage.Text = Loc.T(english);
        ChartMessage.Visibility = Visibility.Visible;
    }

    private void Range_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button b || !int.TryParse(b.Tag?.ToString(), out var hours)) return;
        _hours = hours;
        UpdateRangeButtons();
        if (_points.Count > 0) Redraw();
    }

    private void UpdateRangeButtons()
    {
        Range24Btn.Background = _hours == 24 ? Th("ThLine") : Brushes.Transparent;
        Range7Btn.Background = _hours == 168 ? Th("ThLine") : Brushes.Transparent;
        Range30Btn.Background = _hours == 720 ? Th("ThLine") : Brushes.Transparent;
    }

    private void Chart_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_points.Count > 0) Redraw();
    }

    private static double NiceStep(double raw)
    {
        if (raw <= 1) return 1;
        var mag = Math.Pow(10, Math.Floor(Math.Log10(raw)));
        var n = raw / mag;
        var m = n <= 1 ? 1 : n <= 2 ? 2 : n <= 2.5 ? 2.5 : n <= 5 ? 5 : 10;
        return m * mag;
    }

    private static string Compact(double v)
    {
        if (v >= 1_000_000) return (v / 1_000_000).ToString("0.##") + "M";
        if (v >= 1_000) return (v / 1_000).ToString("0.##") + "k";
        return v.ToString("0");
    }

    private double X(DateTime t) => _pl + (t.Ticks - _tMin) / _tSpan * _pw;
    private double Y(double v) => _pt + _ph - v / _yMax * _ph;

    private void Redraw()
    {
        Chart.Children.Clear();
        _hoverLine = null; _hoverDot = null; _tip = null; _tipText = null;

        var cutoff = DateTime.Now.AddHours(-_hours);
        _shown = _points.Where(p => p.Time >= cutoff).ToList();

        var latest = _points.Count > 0 ? _points[^1].Players : (long?)null;
        if (_shown.Count < 2)
        {
            StatNow.Text = latest?.ToString("N0") ?? "-";
            StatPeak.Text = StatLow.Text = "-";
            ShowChartMessage("Not enough data yet. Check back in a few hours.");
            return;
        }
        ChartMessage.Visibility = Visibility.Collapsed;

        StatNow.Text = _shown[^1].Players.ToString("N0");
        StatPeak.Text = _shown.Max(p => p.Players).ToString("N0");
        StatLow.Text = _shown.Min(p => p.Players).ToString("N0");

        var w = Chart.ActualWidth;
        var h = Chart.ActualHeight;
        if (w < 120 || h < 100) return;

        _pl = 52; _pt = 10; _pw = w - _pl - 14; _ph = h - _pt - 26;
        _tMin = _shown[0].Time.Ticks;
        _tSpan = Math.Max(1, _shown[^1].Time.Ticks - _tMin);

        double max = _shown.Max(p => p.Players);
        var step = NiceStep(max / 4.0);
        _yMax = Math.Max(step, Math.Ceiling(max / step) * step);

        var grid = Th("ThLine");
        var muted = Th("ThMuted");

        // Horizontal lines with the player counts on the left.
        for (double v = 0; v <= _yMax + step / 1000; v += step)
        {
            var y = Y(v);
            Chart.Children.Add(new Line { X1 = _pl, X2 = _pl + _pw, Y1 = y, Y2 = y, Stroke = grid, StrokeThickness = 1 });
            var label = new TextBlock
            {
                Text = Compact(v), Width = 44, TextAlignment = TextAlignment.Right,
                FontSize = 11, Foreground = muted
            };
            Canvas.SetLeft(label, 0);
            Canvas.SetTop(label, y - 8);
            Chart.Children.Add(label);
        }

        // Time labels under the graph: hours for 24 hours, dates for longer.
        for (var i = 0; i <= 4; i++)
        {
            var t = new DateTime((long)(_tMin + _tSpan * i / 4.0));
            var label = new TextBlock
            {
                Text = t.ToString(_hours <= 24 ? "HH:mm" : "d MMM"), Width = 56,
                TextAlignment = TextAlignment.Center, FontSize = 11, Foreground = muted
            };
            var x = Math.Min(Math.Max(0, X(t) - 28), w - 56);
            Canvas.SetLeft(label, x);
            Canvas.SetTop(label, _pt + _ph + 6);
            Chart.Children.Add(label);
        }

        var accent = Th("ThAccent") is SolidColorBrush sb ? sb.Color : Colors.DodgerBlue;

        var pts = new PointCollection();
        foreach (var p in _shown) pts.Add(new Point(X(p.Time), Y(p.Players)));

        var area = new PointCollection(pts)
        {
            new Point(X(_shown[^1].Time), _pt + _ph),
            new Point(X(_shown[0].Time), _pt + _ph)
        };
        var fill = new LinearGradientBrush(
            Color.FromArgb(90, accent.R, accent.G, accent.B),
            Color.FromArgb(0, accent.R, accent.G, accent.B), 90);
        Chart.Children.Add(new Polygon { Points = area, Fill = fill });
        Chart.Children.Add(new Polyline
        {
            Points = pts, Stroke = new SolidColorBrush(accent), StrokeThickness = 2,
            StrokeLineJoin = PenLineJoin.Round
        });

        // Hover marker and tooltip (hidden until the mouse is over the graph).
        _hoverLine = new Line
        {
            Y1 = _pt, Y2 = _pt + _ph, Stroke = muted, StrokeThickness = 1,
            StrokeDashArray = new DoubleCollection { 3, 3 }, Visibility = Visibility.Collapsed,
            IsHitTestVisible = false
        };
        _hoverDot = new Ellipse
        {
            Width = 10, Height = 10, Fill = new SolidColorBrush(accent), Stroke = Th("ThCard"),
            StrokeThickness = 2, Visibility = Visibility.Collapsed, IsHitTestVisible = false
        };
        _tipText = new TextBlock { FontSize = 12, Foreground = Th("ThText") };
        _tip = new Border
        {
            Child = _tipText, Background = Th("ThDim"), BorderBrush = Th("ThEdge"), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 6, 10, 6),
            Visibility = Visibility.Collapsed, IsHitTestVisible = false
        };
        Chart.Children.Add(_hoverLine);
        Chart.Children.Add(_hoverDot);
        Chart.Children.Add(_tip);
    }

    private void Chart_MouseMove(object sender, MouseEventArgs e)
    {
        if (_shown.Count < 2 || _hoverLine == null || _hoverDot == null || _tip == null || _tipText == null) return;

        var mx = e.GetPosition(Chart).X;
        var best = _shown[0];
        var bestDist = double.MaxValue;
        foreach (var p in _shown)
        {
            var d = Math.Abs(X(p.Time) - mx);
            if (d < bestDist) { bestDist = d; best = p; }
        }

        var px = X(best.Time);
        var py = Y(best.Players);

        _hoverLine.X1 = _hoverLine.X2 = px;
        Canvas.SetLeft(_hoverDot, px - 5);
        Canvas.SetTop(_hoverDot, py - 5);

        _tipText.Inlines.Clear();
        _tipText.Inlines.Add(new System.Windows.Documents.Run(best.Time.ToString("ddd d MMM yyyy, HH:mm"))
            { Foreground = Th("ThMuted") });
        _tipText.Inlines.Add(new System.Windows.Documents.LineBreak());
        _tipText.Inlines.Add(new System.Windows.Documents.Run(Loc.T($"{best.Players:N0} players"))
            { FontWeight = FontWeights.Bold });

        _tip.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var tw = _tip.DesiredSize.Width;
        var th = _tip.DesiredSize.Height;
        var left = px + 14 + tw > Chart.ActualWidth ? px - 14 - tw : px + 14;
        var top = Math.Min(Math.Max(0, py - th - 8), Math.Max(0, Chart.ActualHeight - th));
        Canvas.SetLeft(_tip, Math.Max(0, left));
        Canvas.SetTop(_tip, top);

        _hoverLine.Visibility = _hoverDot.Visibility = _tip.Visibility = Visibility.Visible;
    }

    private void Chart_MouseLeave(object sender, MouseEventArgs e)
    {
        if (_hoverLine != null) _hoverLine.Visibility = Visibility.Collapsed;
        if (_hoverDot != null) _hoverDot.Visibility = Visibility.Collapsed;
        if (_tip != null) _tip.Visibility = Visibility.Collapsed;
    }
}
