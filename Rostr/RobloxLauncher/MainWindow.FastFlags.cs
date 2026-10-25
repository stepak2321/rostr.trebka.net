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
    // ---------- FastFlags ----------

    private const string OfficialAllowlistUrl =
        "https://devforum.roblox.com/t/allowlist-for-local-client-configuration-via-fast-flags/3966569";
    private const string CommunityAllowlistUrl = "https://github.com/Anonymouscoderman/allowlist";

    private void AllowlistOfficial_Click(object sender, RoutedEventArgs e) => OpenInChrome(OfficialAllowlistUrl);
    private void AllowlistCommunity_Click(object sender, RoutedEventArgs e) => OpenInChrome(CommunityAllowlistUrl);

    // Chrome when it is installed, otherwise the default browser.
    private void OpenInChrome(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo("chrome.exe", url) { UseShellExecute = true });
        }
        catch
        {
            OpenUrl(url);
        }
    }

    private void FlagsSave_Click(object sender, RoutedEventArgs e)
    {
        var flags = FastFlagManager.Validate(FlagsBox.Text, out var msg);
        if (flags == null)
        {
            FlagsMsg.Text = Loc.T(msg);
            FlagsMsg.Foreground = Bad;
            return;
        }

        if (!FastFlagManager.Shared.Replace(flags))
        {
            FlagsMsg.Text = Loc.T("Could not save the flags file. Details are in the log.");
            FlagsMsg.Foreground = Bad;
            return;
        }

        FlagsBox.Text = FastFlagManager.Shared.ToJson();
        RefreshPresetCards();
        var ignored = FastFlagManager.Ignored(flags.Keys);
        FlagsMsg.Text = flags.Count == 0
            ? "No flags set. Nothing will be changed."
            : ignored.Count == 0
                ? $"Saved {flags.Count} flag(s). They are written to the client on the next launch."
                : $"Saved {flags.Count} flag(s), but Roblox ignores {ignored.Count} of them (not on its allowlist): " +
                  string.Join(", ", ignored.Take(4)) + (ignored.Count > 4 ? "..." : "");
        FlagsMsg.Foreground = ignored.Count == 0 ? Ok : Busy;
    }

    private void FlagsReload_Click(object sender, RoutedEventArgs e)
    {
        FastFlagManager.Shared.Load();
        FlagsBox.Text = FastFlagManager.Shared.ToJson();
        RefreshPresetCards();
        FlagsMsg.Text = Loc.T("Reloaded the saved flags.");
        FlagsMsg.Foreground = Busy;
    }

    private void FlagsClear_Click(object sender, RoutedEventArgs e)
    {
        if (!FastFlagManager.Shared.Replace(new Dictionary<string, System.Text.Json.JsonElement>()))
        {
            FlagsMsg.Text = Loc.T("Could not save the flags file. Details are in the log.");
            FlagsMsg.Foreground = Bad;
            return;
        }
        FlagsBox.Text = FastFlagManager.Shared.ToJson();
        RefreshPresetCards();
        FlagsMsg.Text = Loc.T("All flags removed. Nothing will be changed.");
        FlagsMsg.Foreground = Ok;
    }

    private void FlagsEditorBtn_Click(object sender, RoutedEventArgs e)
    {
        var show = FlagsEditor.Visibility != Visibility.Visible;
        FlagsEditor.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        FlagsEditorBtn.Content = Loc.T(show ? "Hide editor" : "Show editor");
    }

    private void RefreshPresetCards()
    {
        var current = FastFlagManager.Shared.Current;
        PresetList.ItemsSource = FastFlagManager.Presets.Select(p =>
        {
            var active = FastFlagManager.IsActive(p, current);
            var partial = !active && FastFlagManager.MatchCount(p, current) > 0;
            return new PresetRow
            {
                Id = p.Id,
                Name = p.Name,
                Description = p.Description,
                StateText = Loc.T(active ? "On" : partial ? "Partly" : "Off"),
                CardBrush = active ? PresetOnCard : PresetOffCard,
                EdgeBrush = active ? Accent : PresetOffEdge,
                PillBrush = active ? Accent : Dim
            };
        }).ToList();
    }

    // One click: switch the preset on, or off when it is already on. Works on what is in the editor box,
    // so edits you have not saved yet are kept.
    private void Preset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button b || b.Tag is not string id) return;
        var preset = FastFlagManager.Presets.FirstOrDefault(p => p.Id == id);
        if (preset == null) return;

        var current = FastFlagManager.Validate(FlagsBox.Text, out var msg);
        if (current == null)
        {
            PresetMsg.Text = Loc.T("Fix the JSON in the editor first. " + msg);
            PresetMsg.Foreground = Bad;
            return;
        }

        var turnOff = FastFlagManager.IsActive(preset, current);
        var next = turnOff
            ? FastFlagManager.WithoutPreset(preset, current)
            : FastFlagManager.WithPreset(preset, current);

        if (!FastFlagManager.Shared.Replace(next))
        {
            PresetMsg.Text = Loc.T("Could not save the flags file. Details are in the log.");
            PresetMsg.Foreground = Bad;
            return;
        }

        FlagsBox.Text = FastFlagManager.Shared.ToJson();
        RefreshPresetCards();
        PresetMsg.Text = Loc.T($"{preset.Name} is now {(turnOff ? "off" : "on")}. It is written to the client on the next launch.");
        PresetMsg.Foreground = Ok;
    }
}
