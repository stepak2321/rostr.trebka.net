using System.IO;

namespace RobloxLauncher.Core;

public class ClientInstaller
{
    private readonly PackageDownloader _downloader = new();

    // Maps each package to the subfolder it extracts into
    private static readonly Dictionary<string, string> PackageDirs = new()
    {
        ["RobloxApp.zip"] = "",
        ["shaders.zip"] = "shaders",
        ["ssl.zip"] = "ssl",
        ["content-avatar.zip"] = "content/avatar",
        ["content-configs.zip"] = "content/configs",
        ["content-fonts.zip"] = "content/fonts",
        ["content-sky.zip"] = "content/sky",
        ["content-sounds.zip"] = "content/sounds",
        ["content-textures2.zip"] = "content/textures",
        ["content-models.zip"] = "content/models",
        ["content-textures3.zip"] = "PlatformContent/pc/textures",
        ["content-terrain.zip"] = "PlatformContent/pc/terrain",
        ["content-platform-fonts.zip"] = "PlatformContent/pc/fonts",
        ["extracontent-luapackages.zip"] = "ExtraContent/LuaPackages",
        ["extracontent-translations.zip"] = "ExtraContent/translations",
        ["extracontent-models.zip"] = "ExtraContent/models",
        ["extracontent-textures.zip"] = "ExtraContent/textures",
        ["extracontent-places.zip"] = "ExtraContent/places",
    };

    public async Task InstallAsync(
        string versionGuid, string installRoot, IProgress<string>? progress = null)
    {
        var packages = await _downloader.GetPackagesAsync(versionGuid);
        if (packages.Count == 0)
            throw new InvalidOperationException("Roblox's package list was empty. Try again in a minute.");

        var versionDir = Path.Combine(installRoot, versionGuid);

        // Half-installed from an earlier try? The marker file is written last, so remove it first.
        var marker = Path.Combine(versionDir, "AppSettings.xml");
        if (File.Exists(marker)) File.Delete(marker);

        int done = 0;
        foreach (var package in packages)
        {
            var sub = PackageDirs.TryGetValue(package.Name, out var d) ? d : "";
            var target = Path.Combine(versionDir, sub.Replace('/', Path.DirectorySeparatorChar));

            var index = ++done;
            var label = $"({index}/{packages.Count}) {package.Name}";
            progress?.Report(label);
            var pct = new Progress<int>(p => progress?.Report($"{label} {p}%"));
            await _downloader.DownloadAndExtractAsync(versionGuid, package, target, pct);
        }

        // Roblox expects this file to exist next to the exe. It is written last, so its presence
        // means every package above was downloaded, verified and extracted.
        File.WriteAllText(marker,
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<Settings>\n" +
            "\t<ContentFolder>content</ContentFolder>\n" +
            "\t<BaseUrl>http://www.roblox.com</BaseUrl>\n</Settings>\n");
        Log.Info($"Installed Roblox client {versionGuid} ({packages.Count} packages)");
    }
}
