using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using RobloxLauncher.Core;

namespace RobloxLauncher.Settings;

/// <summary>
/// Keeps the user's FastFlag overrides and writes them to ClientSettings\ClientAppSettings.json
/// inside the client folder before each launch. Roblox itself decides which flags it still honors
/// (it ignores ones that are not on its allowlist), so this only checks that names and values are
/// well formed and refuses anything that is not a plain flag.
/// </summary>
/// <summary>A named bundle of flags that can be switched on or off with one click.</summary>
public record FlagPreset(string Id, string Name, string Description, IReadOnlyDictionary<string, object> Flags);

public class FastFlagManager
{
    /// <summary>
    /// The only flags Roblox still reads from ClientAppSettings.json. Since 29 Sep 2025 the client ignores every
    /// other flag (its log says "Denied local configuration"). Source: Roblox DevForum, "Allowlist for local client
    /// configuration via Fast Flags". Roblox can change the list at any time, so a flag outside it is only a warning.
    /// </summary>
    public static readonly HashSet<string> Allowlist = new(StringComparer.Ordinal)
    {
        "DFIntCSGLevelOfDetailSwitchingDistance", "DFIntCSGLevelOfDetailSwitchingDistanceL12",
        "DFIntCSGLevelOfDetailSwitchingDistanceL23", "DFIntCSGLevelOfDetailSwitchingDistanceL34",
        "FFlagHandleAltEnterFullscreenManually", "DFFlagTextureQualityOverrideEnabled", "DFIntTextureQualityOverride",
        "FIntDebugForceMSAASamples", "DFFlagDisableDPIScale", "FFlagDebugGraphicsPreferD3D11", "FFlagDebugSkyGray",
        "DFFlagDebugPauseVoxelizer", "DFIntDebugFRMQualityLevelOverride", "FIntFRMMaxGrassDistance",
        "FIntFRMMinGrassDistance", "FFlagDebugGraphicsPreferVulkan", "FFlagDebugGraphicsPreferOpenGL",
        "FIntGrassMovementReducedMotionFactor"
    };

    /// <summary>The names from this list that Roblox is expected to ignore.</summary>
    public static List<string> Ignored(IEnumerable<string> names) =>
        names.Where(n => !Allowlist.Contains(n)).ToList();

    private static readonly Dictionary<string, object> LowGraphicsFlags = new()
    {
        ["DFIntDebugFRMQualityLevelOverride"] = 1,
        ["FIntDebugForceMSAASamples"] = 1,
        ["DFFlagTextureQualityOverrideEnabled"] = true,
        ["DFIntTextureQualityOverride"] = 0,
        ["FIntFRMMinGrassDistance"] = 0,
        ["FIntFRMMaxGrassDistance"] = 0
    };

    /// <summary>
    /// Ready-made presets. They only use flags that are on Roblox's allowlist, so they still do something.
    /// For an FPS limit use the FPS cap in Performance (it is not a FastFlag). They are written exactly like
    /// hand-typed flags, so they can be edited in the JSON box afterwards.
    /// </summary>
    public static IReadOnlyList<FlagPreset> Presets { get; } = new List<FlagPreset>
    {
        new("low", "Low graphics",
            "Lowest quality level, no grass, smallest textures and no anti-aliasing. For weak PCs.",
            LowGraphicsFlags),

        new("high", "High quality",
            "Top quality level, sharp textures and 4x anti-aliasing. Needs a strong GPU.",
            new Dictionary<string, object>
            {
                ["DFIntDebugFRMQualityLevelOverride"] = 21,
                ["FIntDebugForceMSAASamples"] = 4,
                ["DFFlagTextureQualityOverrideEnabled"] = true,
                ["DFIntTextureQualityOverride"] = 3
            }),

        new("d3d11", "Direct3D 11",
            "Makes Roblox draw with Direct3D 11. Can fix stutter or crashes on some graphics cards.",
            new Dictionary<string, object> { ["FFlagDebugGraphicsPreferD3D11"] = true })
    };

    private static bool SameValue(JsonElement e, object v) => e.GetRawText() == JsonSerializer.Serialize(v);

    /// <summary>True when every flag of the preset is present with the preset's value.</summary>
    public static bool IsActive(FlagPreset preset, IReadOnlyDictionary<string, JsonElement> current) =>
        preset.Flags.All(kv => current.TryGetValue(kv.Key, out var e) && SameValue(e, kv.Value));

    /// <summary>How many of the preset's flags are currently set to the preset's value.</summary>
    public static int MatchCount(FlagPreset preset, IReadOnlyDictionary<string, JsonElement> current) =>
        preset.Flags.Count(kv => current.TryGetValue(kv.Key, out var e) && SameValue(e, kv.Value));

    /// <summary>The given flags plus the preset's flags (the preset wins when both set the same one).</summary>
    public static Dictionary<string, JsonElement> WithPreset(
        FlagPreset preset, IReadOnlyDictionary<string, JsonElement> current)
    {
        var result = new Dictionary<string, JsonElement>(current);
        foreach (var (name, value) in preset.Flags)
            result[name] = JsonSerializer.SerializeToElement(value);
        return result;
    }

    /// <summary>The given flags without the preset's flags. Flags the user changed to another value are kept.</summary>
    public static Dictionary<string, JsonElement> WithoutPreset(
        FlagPreset preset, IReadOnlyDictionary<string, JsonElement> current)
    {
        var result = new Dictionary<string, JsonElement>(current);
        foreach (var (name, value) in preset.Flags)
            if (result.TryGetValue(name, out var e) && SameValue(e, value))
                result.Remove(name);
        return result;
    }

    /// <summary>The saved flags, for working out which presets are on.</summary>
    public IReadOnlyDictionary<string, JsonElement> Current => _flags;

    private static readonly Regex NameRx = new(
        @"^(FFlag|DFFlag|SFFlag|FInt|DFInt|FString|DFString|FLog|DFLog)[A-Za-z0-9_]{1,120}$", RegexOptions.Compiled);

    private readonly string _filePath;
    private SortedDictionary<string, JsonElement> _flags = new(StringComparer.Ordinal);

    public static FastFlagManager Shared { get; } = new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Rostr", "fastflags.json"));

    public FastFlagManager(string filePath)
    {
        _filePath = filePath;
        Load();
    }

    public int Count => _flags.Count;

    public void Load()
    {
        var loaded = SafeFile.ReadJson<Dictionary<string, JsonElement>>(_filePath);
        _flags = loaded == null
            ? new(StringComparer.Ordinal)
            : new SortedDictionary<string, JsonElement>(loaded, StringComparer.Ordinal);
    }

    /// <summary>The flags as indented JSON, for the editor box.</summary>
    public string ToJson() =>
        JsonSerializer.Serialize(_flags, new JsonSerializerOptions { WriteIndented = true });

    /// <summary>
    /// Checks pasted JSON. Returns the parsed flags, or null with a message that says what is wrong.
    /// An empty box means "no flags".
    /// </summary>
    public static Dictionary<string, JsonElement>? Validate(string json, out string message)
    {
        message = "";
        if (string.IsNullOrWhiteSpace(json)) return new();

        Dictionary<string, JsonElement>? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);
        }
        catch (JsonException ex)
        {
            message = "That is not valid JSON: " + ex.Message;
            return null;
        }
        if (parsed == null) { message = "Expected a JSON object like {\"FFlagName\": true}."; return null; }

        foreach (var (name, value) in parsed)
        {
            if (!NameRx.IsMatch(name))
            {
                message = $"\"{name}\" is not a valid flag name. Names start with FFlag, DFFlag, SFFlag, FInt, DFInt, FString, DFString, FLog or DFLog.";
                return null;
            }

            bool isBool = name.StartsWith("FFlag") || name.StartsWith("DFFlag") || name.StartsWith("SFFlag");
            bool isInt = name.StartsWith("FInt") || name.StartsWith("DFInt");
            bool isLog = name.StartsWith("FLog") || name.StartsWith("DFLog");
            bool isString = name.StartsWith("FString") || name.StartsWith("DFString");

            bool ok;
            if (isBool) ok = value.ValueKind == JsonValueKind.True || value.ValueKind == JsonValueKind.False;
            else if (isInt) ok = value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out _);
            else if (isLog) ok = value.ValueKind == JsonValueKind.Number || value.ValueKind == JsonValueKind.String;
            else ok = isString && value.ValueKind == JsonValueKind.String;
            if (!ok)
            {
                message = isBool ? $"{name} must be true or false."
                        : isInt ? $"{name} must be a whole number."
                        : isLog ? $"{name} must be a number or text."
                        : $"{name} must be text in quotes.";
                return null;
            }
        }
        return parsed;
    }

    public bool Replace(Dictionary<string, JsonElement> flags)
    {
        _flags = new SortedDictionary<string, JsonElement>(flags, StringComparer.Ordinal);
        return SafeFile.WriteAtomic(_filePath, ToJson());
    }

    /// <summary>
    /// Writes (or removes) ClientSettings\ClientAppSettings.json in a client folder. That folder
    /// belongs to this launcher, so with no flags the file is simply deleted.
    /// </summary>
    public void ApplyTo(string versionDir)
    {
        try
        {
            var dir = Path.Combine(versionDir, "ClientSettings");
            var file = Path.Combine(dir, "ClientAppSettings.json");

            if (_flags.Count == 0)
            {
                if (File.Exists(file)) File.Delete(file);
                return;
            }

            Directory.CreateDirectory(dir);
            File.WriteAllText(file, ToJson());
            var ignored = Ignored(_flags.Keys);
            if (ignored.Count > 0)
                Log.Warn($"{ignored.Count} FastFlag(s) are not on Roblox's allowlist and will be ignored: {string.Join(", ", ignored)}");
            Log.Info($"Applied {_flags.Count} FastFlag(s) to {Path.GetFileName(versionDir)}");
        }
        catch (Exception ex)
        {
            Log.Error("Could not write FastFlags", ex);
        }
    }
}
