using System.Windows;
using System.Windows.Media;

namespace RobloxLauncher.Core;

/// <summary>Colour themes. Every colour in the UI is a DynamicResource (Th...), so changing the theme repaints the app live.</summary>
public static class ThemeManager
{
    // Order of colours in every row: Bg, Bg2, Card, Edge, Line, Dim, Track, Disabled, Faint, Muted, Soft, Text2, Text, Accent, AccentBg
    private static readonly string[] Keys =
    {
        "ThBg", "ThBg2", "ThCard", "ThEdge", "ThLine", "ThDim", "ThTrack", "ThDisabled",
        "ThFaint", "ThMuted", "ThSoft", "ThText2", "ThText", "ThAccent", "ThAccentBg"
    };

    public static readonly Dictionary<string, string[]> Themes = new()
    {
        ["Dark"]     = new[] { "0F1117","12141B","171A22","1F2432","232838","262B38","2A3040","3A4155","5C6377","7C8499","9AA1B5","C9CEDC","E6E8EE","3D6BFF","1B2547" },
        ["Midnight"] = new[] { "070A14","0A0E1C","0E1428","16203A","1A2548","1C2750","22305C","34456E","5B6B94","7886B0","98A6CC","C3CCE6","E8EDFB","6C8CFF","16254F" },
        ["AMOLED"]   = new[] { "000000","050505","0D0D0D","1A1A1A","1F1F1F","242424","2C2C2C","3A3A3A","5E5E5E","808080","A0A0A0","CCCCCC","F2F2F2","3D6BFF","101A3A" },
        ["Ocean"]    = new[] { "0A1418","0D1A1F","11222A","19313B","1C3A45","1F404C","27505E","3A6674","5E8794","7BA3B1","9CC0CC","C6DDE5","E6F3F7","14B8A6","0F3A3A" },
        ["Forest"]   = new[] { "0E1410","111A14","16201A","1F2D24","233629","27392D","2F4636","45614D","6A8574","86A18F","A5BDAD","CDDCD2","EAF3EC","3FB950","14301C" },
        ["Sunset"]   = new[] { "16100F","1C1413","241A18","352623","3B2A26","42302B","503A33","6A5048","8C7168","A88C83","C4ABA2","E3D2CB","F7EEEA","F97316","3F2210" },
        ["Rose"]     = new[] { "150F13","1B1318","23181F","352530","3B2A36","432F3C","523B49","6D5260","90727F","AD8F9C","C8AEBA","E6D2DB","F8EEF2","EC4899","40162B" },
        ["Light"]    = new[] { "F3F5F9","E9ECF3","FFFFFF","E1E5EE","E4E8F0","7F8AA3","C3CADB","B8C0D2","7E889E","5F6A82","4A5468","2E3648","141A28","3D6BFF","DCE5FF" },
    };

    public static string Current { get; private set; } = "Dark";

    public static void Apply(string? name)
    {
        if (name == null || !Themes.TryGetValue(name, out var c)) name = "Dark";
        c = Themes[name];
        Current = name;
        var res = Application.Current.Resources;
        for (int i = 0; i < Keys.Length; i++)
            res[Keys[i]] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#" + c[i]));
    }

    /// <summary>The current brush for a theme key. Use it in code instead of a fixed colour.</summary>
    public static Brush B(string key) =>
        Application.Current.Resources[key] as Brush ?? Brushes.Gray;
}
