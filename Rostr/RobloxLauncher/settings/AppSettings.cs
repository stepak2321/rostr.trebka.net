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
    // Dim overlay: a key that darkens the whole Roblox window (only while Roblox is in front).
    // DimHotkey holds Windows virtual-key codes: the modifiers (left and right kept apart), then the key. Default: Left Ctrl + Tab.
    public bool DimEnabled { get; set; } = true;
    public List<uint> DimHotkey { get; set; } = new() { 0xA2, 0x09 };
    // Friends panel inside the dim: open or closed, and where it sits (0..1 across the free space of the game window).
    public bool DimFriendsOpen { get; set; } = true;
    public double DimFriendsX { get; set; } = 0.5;
    public double DimFriendsY { get; set; } = 0.5;
    // Where the wiki window of the dim sits (0..1 across the free space of the game window).
    public double DimWikiX { get; set; } = 0.5;
    public double DimWikiY { get; set; } = 0.5;
    // The notes of the dim menu: one text per game (place id), and where the notes window sits.
    public Dictionary<long, string> GameNotes { get; set; } = new();
    public double DimNotesX { get; set; } = 0.96;
    public double DimNotesY { get; set; } = 0.5;
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
    // Server ids you have already been in, per place. Server hop skips these.
    public Dictionary<long, List<string>> VisitedServers { get; set; } = new();
    public string Theme { get; set; } = "Dark";
    public string Language { get; set; } = "";   // empty = follow Windows
    public bool DiscordEnabled { get; set; } = true;     // show the game you are in on your Discord profile
    public bool DiscordHideGame { get; set; } = false;   // show "Playing a Roblox game" instead of the game name
    public bool DiscordShowBrowsing { get; set; } = true;   // show "Browsing servers" while a game page is open in Rostr
    // Where screenshots and recordings are saved. Empty = Pictures\Rostr.
    public string MediaFolder { get; set; } = "";
    // Global hotkeys for the screenshot and the recording. Mods use the Win32 bits: 1 Alt, 2 Ctrl, 4 Shift, 8 Win.
    public uint ShotHotkeyMods { get; set; } = 0x2 | 0x4;
    public uint ShotHotkeyVk { get; set; } = 0x79;   // F10
    public uint RecHotkeyMods { get; set; } = 0x2 | 0x4;
    public uint RecHotkeyVk { get; set; } = 0x7A;    // F11
    // Recording settings. RecQuality: 0 low, 1 medium, 2 high. Sound is captured from the Roblox Player process.
    public int RecFps { get; set; } = 30;            // 30 or 60
    public int RecQuality { get; set; } = 1;
    public bool RecAudio { get; set; } = true;
    public int? RecAudioPreferenceVersion { get; set; }
    // Instant replay: keeps the selected number of seconds in the background and saves them as a clip.
    public bool ReplayEnabled { get; set; } = false;
    public int ReplaySeconds { get; set; } = 30;
    public uint ReplayHotkeyMods { get; set; } = 0x2 | 0x4;
    public uint ReplayHotkeyVk { get; set; } = 0x78;   // F9
    // Full paths of the gallery files marked as favorites.
    public List<string> MediaFavorites { get; set; } = new();

    public static AppSettings Current { get; } = Load();

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Rostr", "settings.json");

    public static AppSettings Load()
    {
        var settings = SafeFile.ReadJson<AppSettings>(FilePath) ?? new();
        if (File.Exists(FilePath) && settings.RecAudioPreferenceVersion is null)
        {
            settings.RecAudio = true;
            settings.RecAudioPreferenceVersion = 1;
            settings.Save();
        }
        return settings;
    }

    public void Save()
    {
        RecAudioPreferenceVersion ??= 1;
        SafeFile.WriteAtomic(FilePath,
            JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}
