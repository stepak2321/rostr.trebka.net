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

public class ServerRow : System.ComponentModel.INotifyPropertyChanged
{
    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

    public string Id { get; init; } = "";
    public string PlayersText { get; init; } = "";
    public string PingText { get; init; } = "";
    public string FpsText { get; init; } = "";

    // Settable so auto region detection can fill it in without rebuilding (and scrolling) the list.
    private string _regionText = "";
    public string RegionText
    {
        get => _regionText;
        set
        {
            _regionText = value;
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(RegionText)));
        }
    }
    public Brush PingBrush { get; init; } = Brushes.Gray;
}

public class PresetRow
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public string StateText { get; init; } = "";
    public Brush CardBrush { get; init; } = Brushes.Transparent;
    public Brush EdgeBrush { get; init; } = Brushes.Transparent;
    public Brush PillBrush { get; init; } = Brushes.Gray;
}

public class SearchRow : System.ComponentModel.INotifyPropertyChanged
{
    private Brush _iconBrush = Brushes.Transparent;
    public Brush IconBrush
    {
        get => _iconBrush;
        set
        {
            _iconBrush = value;
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(IconBrush)));
        }
    }
    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

    public long PlaceId { get; init; }
    public string Name { get; init; } = "";
    public string Sub { get; init; } = "";
}

public class FriendRow : System.ComponentModel.INotifyPropertyChanged
{
    private ImageSource? _avatar;
    public ImageSource? Avatar
    {
        get => _avatar;
        set
        {
            _avatar = value;
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(Avatar)));
        }
    }
    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

    public long UserId { get; init; }
    public string Title { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public string Handle { get; init; } = "";
    public Visibility JoinVisibility => CanJoin ? Visibility.Visible : Visibility.Collapsed;
    public string Status { get; init; } = "";
    public Brush StatusBrush { get; init; } = Brushes.Gray;
    public bool CanJoin { get; init; }
    public long PlaceId { get; init; }
    public string Location { get; init; } = "";
    public int Order { get; init; }
}

public class LibRow : System.ComponentModel.INotifyPropertyChanged
{
    private Brush _iconBrush = Brushes.Transparent;
    public Brush IconBrush
    {
        get => _iconBrush;
        set
        {
            _iconBrush = value;
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(IconBrush)));
        }
    }
    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

    public long PlaceId { get; init; }
    public string Name { get; init; } = "";
    public string Sub { get; init; } = "";
    private string _playtime = "";
    public string Playtime
    {
        get => _playtime;
        set
        {
            _playtime = value;
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(Playtime)));
        }
    }

    public Visibility RejoinVisibility { get; init; } = Visibility.Collapsed;
    public string StarGlyph { get; init; } = "\uE734";
    public Brush StarBrush { get; init; } = Brushes.White;
}
