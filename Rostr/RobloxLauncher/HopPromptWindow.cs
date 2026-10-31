using System.Windows;
using System.Windows.Controls;
using RobloxLauncher.Core;

namespace RobloxLauncher;

/// <summary>Shown after Server hop joined a server: Stay here, or Hop again.</summary>
public sealed class HopPromptWindow : Window
{
    public bool HopAgain { get; private set; }

    public HopPromptWindow(Window owner)
    {
        Title = Loc.T("Server hop");
        Width = 400;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Topmost = true;               // sits above the game window
        ShowInTaskbar = false;
        FontFamily = owner.FontFamily;
        SetResourceReference(BackgroundProperty, "ThBg");
        SetResourceReference(ForegroundProperty, "ThText");

        var buttonStyle = owner.TryFindResource(typeof(Button)) as Style;
        Button MakeButton(string text, bool hop)
        {
            var b = new Button { Content = Loc.T(text), Padding = new Thickness(18, 9, 18, 9), FontSize = 13,
                                 Margin = new Thickness(0, 0, 8, 0) };
            if (buttonStyle != null) b.Style = buttonStyle;
            if (hop) b.SetResourceReference(BackgroundProperty, "ThDim");
            b.Click += (_, _) => { HopAgain = hop; Close(); };
            return b;
        }

        var panel = new StackPanel { Margin = new Thickness(24, 22, 24, 22) };
        panel.Children.Add(new TextBlock
        {
            Text = Loc.T("You are in a new server. Stay here or server hop again?"),
            TextWrapping = TextWrapping.Wrap, FontSize = 14, Margin = new Thickness(0, 0, 0, 18)
        });

        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        buttons.Children.Add(MakeButton("Stay", false));
        buttons.Children.Add(MakeButton("Hop again", true));
        panel.Children.Add(buttons);

        Content = panel;
    }
}
