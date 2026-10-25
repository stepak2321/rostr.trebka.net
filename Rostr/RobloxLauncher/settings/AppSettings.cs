using System.IO;
using System.Text.Json;
using RobloxLauncher.Core;

namespace RobloxLauncher.Settings;

public class GameEntry
{
    public long PlaceId { get; set; }
    public string Name { get; set; } = "";
    public string? IconUrl { get; set; }
    public string? LastJobId { get; set; }
    public string? LastRegion { get; set; }
    public DateTime LastPlayed { get; set; }
}

/// <summary>How long a game has been played, kept per place id so it survives the Recent list rolling over.</summary>
public class PlayStat
{
    public string Name { get; set; } = "";
    public long Seconds { get; set; }
    public int Sessions { get; set; }
    public DateTime LastPlayed { get; set; }
}

public class AppSettings
{
    public int FpsCap { get; set; } = 144;
    public bool OverlayEnabled { get; set; } = true;
    public int OverlayCorner { get; set; } = 3;
    public bool MinimizeOnLaunch { get; set; } = true;
    // The X button hides Rostr in the tray instead of closing it. Exit is in the tray menu.
    public bool CloseToTray { get; set; } = true;
    // Install (Start menu + desktop) also makes Rostr start with Windows, in the tray. Can be turned off in Settings.
    public bool RunAtStartup { get; set; } = true;
    // Version of the Rostr.exe copy kept in the data folder. Saved on Install, and refreshed whenever Rostr starts.
    public string InstalledVersion { get; set; } = "";
    // Version of the Rostr that ran last. A newer exe uses it to know an older Rostr may still be running.
    public string LastRunVersion { get; set; } = "";
    // The release version that was installed last, and the SHA-256 of the exe it came with. Rostr uses it to know
    // which version an exe is even when the version number inside the exe was not raised for that release.
    public string UpdatedToVersion { get; set; } = "";
    public string UpdatedToSha256 { get; set; } = "";
    // Play skips the server choice: it joins a random server among the ones with the best ping.
    public bool AutoBestServer { get; set; } = false;
    public bool AutoCleanup { get; set; } = true;
    public bool MultiRoblox { get; set; } = false;
    // Registers the roblox-player: handler by itself on every start. Turned off when the user presses Unregister.
    public bool AutoRegister { get; set; } = true;
    public bool OverlayHidden { get; set; } = false;
    // Global hotkey that shows/hides the overlay. Mods use the Win32 bits: 1 Alt, 2 Ctrl, 4 Shift, 8 Win.
    public uint OverlayHotkeyMods { get; set; } = 0x2 | 0x4;
    public uint OverlayHotkeyVk { get; set; } = 0x4F;   // O
    // Which lines the overlay shows.
    public bool OverlayShowPing { get; set; } = true;
    public bool OverlayShowCpu { get; set; } = true;
    public bool OverlayShowRam { get; set; } = true;
    public bool OverlayShowTime { get; set; } = true;
    public bool OverlayShowRegion { get; set; } = true;
    public bool OverlayShowUser { get; set; } = true;
    public long ActiveAccountId { get; set; } = 0;   // 0 = use the browser login from roblox.com
    public List<GameEntry> Favorites { get; set; } = new();
    public List<GameEntry> Recent { get; set; } = new();
    public Dictionary<string, string> Regions { get; set; } = new();
    public Dictionary<long, PlayStat> Playtime { get; set; } = new();
    public string Theme { get; set; } = "Dark";
    public string Language { get; set; } = "";   // empty = follow Windows
    public bool DiscordEnabled { get; set; } = true;     // show the game you are in on your Discord profile
    public bool DiscordHideGame { get; set; } = false;   // show "Playing a Roblox game" instead of the game name

    public static AppSettings Current { get; } = Load();

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Rostr", "settings.json");

    public static AppSettings Load() =>
        SafeFile.ReadJson<AppSettings>(FilePath) ?? new();

    public void Save() =>
        SafeFile.WriteAtomic(FilePath,
            JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
}
