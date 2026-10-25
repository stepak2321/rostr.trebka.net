using System.Windows;
using System.Windows.Controls;
using RobloxLauncher.Core;

namespace RobloxLauncher;

public enum UpdateChoice { Later, Install, Changelog }

/// <summary>"A new version is available": Install now, See change log, or Not now.</summary>
public sealed class UpdatePromptWindow : Window
{
    public UpdateChoice Choice { get; private set; } = UpdateChoice.Later;

    public UpdatePromptWindow(Window owner, UpdateInfo update, Version current)
    {
        Owner = owner;
        Title = Loc.T("Rostr update");
        Width = 480;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        FontFamily = owner.FontFamily;
        SetResourceReference(BackgroundProperty, "ThBg");
        SetResourceReference(ForegroundProperty, "ThText");

        var buttonStyle = owner.TryFindResource(typeof(Button)) as Style;
        Button MakeButton(string text, UpdateChoice choice, bool primary)
        {
            var b = new Button { Content = Loc.T(text), Padding = new Thickness(18, 9, 18, 9), FontSize = 13,
                                 Margin = new Thickness(0, 0, 8, 0) };
            if (buttonStyle != null) b.Style = buttonStyle;
            if (!primary) b.SetResourceReference(BackgroundProperty, "ThDim");
            b.Click += (_, _) => { Choice = choice; Close(); };
            return b;
        }

        var panel = new StackPanel { Margin = new Thickness(24, 22, 24, 22) };

        var heading = new TextBlock { Text = Loc.T($"Rostr {update.Version} is available"), FontSize = 18,
                                      FontWeight = FontWeights.Bold };
        panel.Children.Add(heading);

        var sub = new TextBlock { Text = Loc.T($"You have {current}."), FontSize = 13, Margin = new Thickness(0, 4, 0, 0) };
        sub.SetResourceReference(TextBlock.ForegroundProperty, "ThMuted");
        panel.Children.Add(sub);

        if (update.Notes.Trim().Length > 0)
        {
            var notes = new TextBlock { Text = update.Notes.Trim(), TextWrapping = TextWrapping.Wrap, FontSize = 12.5 };
            notes.SetResourceReference(TextBlock.ForegroundProperty, "ThText2");
            panel.Children.Add(new ScrollViewer
            {
                Content = notes, MaxHeight = 140, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Margin = new Thickness(0, 14, 0, 0)
            });
        }

        var ask = new TextBlock
        {
            Text = Loc.T("Install it now? Rostr will close, a CMD window will do the update, and Rostr opens again when it is done."),
            TextWrapping = TextWrapping.Wrap, FontSize = 13, Margin = new Thickness(0, 14, 0, 18)
        };
        ask.SetResourceReference(TextBlock.ForegroundProperty, "ThText2");
        panel.Children.Add(ask);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        buttons.Children.Add(MakeButton("Install now", UpdateChoice.Install, true));
        buttons.Children.Add(MakeButton("See change log", UpdateChoice.Changelog, false));
        buttons.Children.Add(MakeButton("Not now", UpdateChoice.Later, false));
        panel.Children.Add(buttons);

        Content = panel;
    }
}
