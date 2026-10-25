using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;

namespace RobloxLauncher.Core;

/// <summary>One entry of Roblox's package manifest. Md5 is null when the manifest has no hash for it.</summary>
public record PackageInfo(string Name, string? Md5);

public class PackageDownloader
{
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(10) };
    private const string CdnBase = "https://setup.rbxcdn.com/";
    private const int Attempts = 3;

    /// <summary>
    /// The manifest is "v0" followed by groups of four lines: file name, md5 of the zip,
    /// compressed size, uncompressed size. If it does not look like that, we fall back to
    /// plain names with no hashes (still downloads, just cannot verify).
    /// </summary>
    public async Task<List<PackageInfo>> GetPackagesAsync(string versionGuid)
    {
        var url = $"{CdnBase}{versionGuid}-rbxPkgManifest.txt";
        var text = await _http.GetStringAsync(url);

        var lines = text.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
        if (lines.Count > 0 && lines[0].StartsWith("v", StringComparison.OrdinalIgnoreCase) &&
            !lines[0].EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            lines.RemoveAt(0);

        var list = new List<PackageInfo>();
        var structured = lines.Count % 4 == 0 && lines.Count > 0;
        if (structured)
        {
            for (int i = 0; i < lines.Count; i += 4)
            {
                var name = lines[i];
                var md5 = lines[i + 1];
                if (!name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ||
                    md5.Length != 32 || !md5.All(Uri.IsHexDigit))
                {
                    structured = false;
                    break;
                }
                list.Add(new PackageInfo(name, md5));
            }
        }

        if (!structured)
        {
            Log.Warn("Package manifest is not in the expected format; downloads will not be verified.");
            list = lines.Where(l => l.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                        .Select(l => new PackageInfo(l, null)).ToList();
        }
        return list;
    }

    /// <summary>
    /// Streams the package to a temp file (not into memory), checks its md5, retries a few times,
    /// then extracts it. A package whose hash never matches is rejected, never extracted.
    /// </summary>
    public async Task DownloadAndExtractAsync(
        string versionGuid, PackageInfo package, string targetDir, IProgress<int>? percent = null)
    {
        var url = $"{CdnBase}{versionGuid}-{package.Name}";
        var tmp = Path.Combine(Path.GetTempPath(), $"mrl_{Guid.NewGuid():N}.zip");

        try
        {
            Exception? last = null;
            for (int attempt = 1; attempt <= Attempts; attempt++)
            {
                try
                {
                    var hash = await DownloadToFileAsync(url, tmp, percent);
                    if (package.Md5 != null && !hash.Equals(package.Md5, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException(
                            $"{package.Name} failed its checksum (expected {package.Md5}, got {hash}).");
                    last = null;
                    break;
                }
                catch (Exception ex) when (attempt < Attempts)
                {
                    last = ex;
                    Log.Warn($"Download of {package.Name} failed (try {attempt}/{Attempts}): {ex.Message}");
                    await Task.Delay(1000 * attempt);
                }
            }
            if (last != null) throw last;

            Extract(tmp, targetDir);
        }
        finally
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
        }
    }

    private static async Task<string> DownloadToFileAsync(string url, string path, IProgress<int>? percent)
    {
        using var res = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        res.EnsureSuccessStatusCode();
        var total = res.Content.Headers.ContentLength ?? -1;

        using var md5 = MD5.Create();
        await using var input = await res.Content.ReadAsStreamAsync();
        await using var output = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);

        var buffer = new byte[81920];
        long done = 0;
        int lastPct = -1;
        int n;
        while ((n = await input.ReadAsync(buffer)) > 0)
        {
            md5.TransformBlock(buffer, 0, n, null, 0);
            await output.WriteAsync(buffer.AsMemory(0, n));
            done += n;
            if (total > 0)
            {
                var pct = (int)(done * 100 / total);
                if (pct != lastPct) { lastPct = pct; percent?.Report(pct); }
            }
        }
        md5.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        return Convert.ToHexString(md5.Hash!).ToLowerInvariant();
    }

    private static void Extract(string zipPath, string targetDir)
    {
        Directory.CreateDirectory(targetDir);
        var root = Path.GetFullPath(targetDir);
        if (!root.EndsWith(Path.DirectorySeparatorChar))
            root += Path.DirectorySeparatorChar;

        using var zip = ZipFile.OpenRead(zipPath);
        foreach (var entry in zip.Entries)
        {
            // Normalize the name: use one kind of slash, drop leading slashes
            var name = entry.FullName.Replace('\\', '/').TrimStart('/');
            if (string.IsNullOrEmpty(name)) continue;

            // Safety: never allow ".." to climb out of the folder
            if (name.Split('/').Contains("..")) continue;

            var dest = Path.GetFullPath(
                Path.Combine(root, name.Replace('/', Path.DirectorySeparatorChar)));
            if (!dest.StartsWith(root, StringComparison.OrdinalIgnoreCase)) continue;

            // Entries ending in "/" are folders
            if (name.EndsWith("/"))
            {
                Directory.CreateDirectory(dest);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            entry.ExtractToFile(dest, overwrite: true);
        }
    }
}
