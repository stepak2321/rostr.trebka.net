using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using RobloxLauncher.Settings;

namespace RobloxLauncher.Core;

public record UpdateInfo(Version Version, string Url, string Sha256, string Notes);

/// <summary>
/// Self-update straight from the GitHub releases of the Rostr repository. The newest release is read from
/// the GitHub API (the tag, like v1.4.0, is the version). When it is newer than the running exe, Rostr
/// closes and a CMD window does the update: it downloads the release's Rostr.exe, checks it against the
/// SHA-256 that GitHub recorded for the upload (or a Rostr.exe.sha256 file next to it), unregisters and
/// uninstalls the old copy, installs and registers the new one, and starts it.
/// </summary>
public static class Updater
{
    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("Rostr-Updater/1.0");
        return c;
    }

    private static Version? _current;

    /// <summary>The version of the running Rostr.exe. See VersionOfFile for how it is worked out.</summary>
    public static Version CurrentVersion => _current ??= ResolveCurrent();

    private static Version ResolveCurrent()
    {
        var exe = ExePath;
        if (!string.IsNullOrEmpty(exe) && File.Exists(exe)) return VersionOfFile(exe);
        return Normalize(Assembly.GetExecutingAssembly().GetName().Version);
    }

    private static Version Normalize(Version? v) =>
        v == null ? new Version(1, 0, 0) : new Version(v.Major, v.Minor, Math.Max(v.Build, 0));

    /// <summary>
    /// The version of a Rostr.exe. Normally the number inside the exe. If the exe is exactly the one that was
    /// last installed from a release (same SHA-256), the release's own version wins, so a release whose exe
    /// still carries the old number is not offered again and again.
    /// </summary>
    public static Version VersionOfFile(string path)
    {
        Version inside;
        try { inside = Normalize(Version.TryParse(FileVersionInfo.GetVersionInfo(path).FileVersion, out var f) ? f : null); }
        catch { inside = new Version(1, 0, 0); }

        var s = AppSettings.Current;
        if (Version.TryParse(s.UpdatedToVersion, out var saved) && saved > inside && IsSha256(s.UpdatedToSha256) &&
            string.Equals(Sha256Of(path), s.UpdatedToSha256, StringComparison.OrdinalIgnoreCase))
            return Normalize(saved);
        return inside;
    }

    private static readonly Dictionary<string, (long Len, DateTime Time, string Sha)> ShaCache =
        new(StringComparer.OrdinalIgnoreCase);

    private static string? Sha256Of(string path)
    {
        try
        {
            var info = new FileInfo(path);
            lock (ShaCache)
            {
                if (ShaCache.TryGetValue(path, out var c) && c.Len == info.Length && c.Time == info.LastWriteTimeUtc)
                    return c.Sha;
            }
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var sha = Convert.ToHexString(SHA256.HashData(fs)).ToLowerInvariant();
            lock (ShaCache) ShaCache[path] = (info.Length, info.LastWriteTimeUtc, sha);
            return sha;
        }
        catch (Exception ex)
        {
            Log.Warn("Could not hash " + path + ": " + ex.Message);
            return null;
        }
    }

    /// <summary>Remembers that the exe with this SHA-256 is this release version.</summary>
    private static void RecordApplied(Version version, string sha)
    {
        var s = AppSettings.Current;
        s.UpdatedToVersion = version.ToString();
        s.UpdatedToSha256 = sha;
        s.Save();
        _current = null;
        Log.Info("Saved the installed release version " + version);
    }

    private static string? ExePath => Environment.ProcessPath;

    /// <summary>Updating only makes sense for the published exe, not a "dotnet run" build under bin\.</summary>
    public static bool CanSelfUpdate()
    {
        var exe = ExePath;
        if (string.IsNullOrEmpty(exe)) return false;
        if (!Path.GetFileName(exe).Equals("Rostr.exe", StringComparison.OrdinalIgnoreCase)) return false;
        return !exe.Contains(@"\bin\", StringComparison.OrdinalIgnoreCase) &&
               !exe.Contains(@"\obj\", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Removes the previous exe that an update renamed out of the way.</summary>
    public static void CleanupAfterUpdate()
    {
        try
        {
            var exe = ExePath;
            if (string.IsNullOrEmpty(exe)) return;
            var old = exe + ".old";
            if (File.Exists(old)) File.Delete(old);
        }
        catch (Exception ex)
        {
            Log.Warn("Could not delete the old exe: " + ex.Message);
        }
    }

    // Updates come from the releases of this GitHub repository.
    private const string Repo = ProjectInfo.GitHubRepo;
    private const string AssetName = "Rostr.exe";

    private static bool IsGitHubUrl(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u) &&
        u.Scheme == Uri.UriSchemeHttps &&
        u.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase);

    private static bool IsSha256(string s) => s.Length == 64 && s.All(Uri.IsHexDigit);

    private static string Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? (v.GetString() ?? "") : "";

    /// <summary>Returns the update when a newer release is published, otherwise null.</summary>
    public static async Task<UpdateInfo?> CheckAsync()
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{Repo}/releases/latest");
        req.Headers.Accept.ParseAdd("application/vnd.github+json");
        using var res = await Http.SendAsync(req);

        if (res.StatusCode == HttpStatusCode.NotFound) return null;   // nothing released yet
        if (res.StatusCode == HttpStatusCode.Forbidden || (int)res.StatusCode == 429)
            throw new InvalidOperationException("GitHub is limiting update checks right now. Try again later.");
        res.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        var r = doc.RootElement;

        var m = Regex.Match(Str(r, "tag_name"), @"^[vV]?(\d+(\.\d+){1,3})");
        if (!m.Success || !Version.TryParse(m.Groups[1].Value, out var version))
            throw new InvalidOperationException("The latest release tag is not a version number. Tag releases like v1.2.1.");

        var normalized = new Version(version.Major, version.Minor, Math.Max(version.Build, 0));
        if (normalized <= CurrentVersion) return null;

        string url = "", sha = "", shaFileUrl = "";
        if (r.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
        {
            foreach (var a in assets.EnumerateArray())
            {
                var name = Str(a, "name");
                if (name.Equals(AssetName, StringComparison.OrdinalIgnoreCase))
                {
                    url = Str(a, "browser_download_url");
                    var digest = Str(a, "digest");
                    if (digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
                        sha = digest[7..].Trim().ToLowerInvariant();
                }
                else if (name.Equals(AssetName + ".sha256", StringComparison.OrdinalIgnoreCase))
                {
                    shaFileUrl = Str(a, "browser_download_url");
                }
            }
        }

        if (url.Length == 0)
            throw new InvalidOperationException($"The latest release has no {AssetName} file attached.");
        if (!IsGitHubUrl(url))
            throw new InvalidOperationException("The release download is not a github.com address.");

        // Older uploads have no GitHub digest: fall back to a Rostr.exe.sha256 file next to the exe.
        if (!IsSha256(sha) && IsGitHubUrl(shaFileUrl))
        {
            var text = await Http.GetStringAsync(shaFileUrl);
            sha = (text.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                       .FirstOrDefault() ?? "").Trim().ToLowerInvariant();
        }
        if (!IsSha256(sha))
            throw new InvalidOperationException("The release has no SHA-256 checksum. Updates without a checksum are refused.");

        // The exe that is running may already BE this release (downloaded by hand, or its number was never
        // raised): then there is nothing to update. Remember its version and stop offering it.
        var exe = ExePath;
        if (!string.IsNullOrEmpty(exe) && File.Exists(exe))
        {
            var mine = await Task.Run(() => Sha256Of(exe));
            if (string.Equals(mine, sha, StringComparison.OrdinalIgnoreCase))
            {
                RecordApplied(normalized, sha);
                return null;
            }
        }

        var notes = Str(r, "body").Trim();
        if (notes.Length > 600) notes = notes[..600].TrimEnd() + "...";
        return new UpdateInfo(normalized, url, sha, notes);
    }

    /// <summary>
    /// Opens the update window (a CMD script) and returns. The caller must shut Rostr down right after,
    /// the script waits for Rostr.exe to be gone before it touches anything.
    /// </summary>
    public static void StartUpdaterWindow(UpdateInfo update)
    {
        if (!CanSelfUpdate())
            throw new InvalidOperationException("Updating only works from the published Rostr.exe, not a development build.");
        if (!IsGitHubUrl(update.Url) || !IsSha256(update.Sha256))
            throw new InvalidOperationException("The update has no safe download address or checksum.");

        var exe = ExePath!;
        var installed = Shortcuts.HasInstalledCopy || Shortcuts.HasStartMenu || Shortcuts.HasDesktop;
        var script = Path.Combine(Path.GetTempPath(), "Rostr-update.cmd");
        // The script is plain ASCII. Every path (which may contain any letters) goes in through environment variables.
        File.WriteAllText(script, UpdateScript.Replace("\r\n", "\n").Replace("\n", "\r\n"), System.Text.Encoding.ASCII);

        var psi = new ProcessStartInfo("cmd.exe", "/d /c \"\"" + script + "\"\"")
        {
            UseShellExecute = false,
            CreateNoWindow = false,   // a visible CMD window
            WorkingDirectory = Path.GetTempPath()
        };
        psi.Environment["ROSTR_OLD"] = exe;
        psi.Environment["ROSTR_INSTALLED"] = installed ? "1" : "0";
        psi.Environment["ROSTR_INSTALLEXE"] = Shortcuts.InstalledExe;
        psi.Environment["ROSTR_URL"] = update.Url;
        psi.Environment["ROSTR_SHA"] = update.Sha256;
        psi.Environment["ROSTR_VERSION"] = update.Version.ToString();
        // Keep --startup, so an update at boot does not pop the window open.
        psi.Environment["ROSTR_ARGS"] = Environment.GetCommandLineArgs().Contains(StartupManager.Arg)
            ? "--updated " + StartupManager.Arg : "--updated";

        Log.Info($"Starting the update window: {CurrentVersion} -> {update.Version} (installed copy: {installed})");
        Process.Start(psi);
    }

    /// <summary>
    /// Headless jobs for the update window, started as "Rostr.exe --uninstall" (old exe) and
    /// "Rostr.exe --install" (new exe). They show no window. Returns the exit code.
    /// </summary>
    public static int RunSetupCommand(string[] args)
    {
        try
        {
            if (args.Contains("--uninstall"))
            {
                // Give roblox-player: links back to Roblox.
                try { ProtocolHandler.Unregister(); }
                catch (Exception ex) { Log.Warn("Unregister failed: " + ex.Message); }

                // Remove the shortcuts, the startup entry and the installed copy, but only if Rostr was installed.
                if (Shortcuts.HasInstalledCopy || Shortcuts.HasStartMenu || Shortcuts.HasDesktop)
                {
                    Shortcuts.Uninstall();
                    StartupManager.Disable();   // the RunAtStartup setting stays, so --install can turn it on again
                }
                Log.Info("Uninstalled for update");
                return 0;
            }

            if (args.Contains("--install"))
            {
                Shortcuts.Install();   // copies this exe to the data folder + Start menu and desktop shortcuts
                if (AppSettings.Current.RunAtStartup) StartupManager.Enable();
                ProtocolHandler.EnsureRegistered();   // skipped when you pressed Unregister yourself
                Log.Info("Installed after update");
                return 0;
            }
        }
        catch (Exception ex)
        {
            Log.Error("Update setup step failed", ex);
            return 1;
        }
        return 0;
    }

    // The CMD window. Order matters: the new exe is downloaded and checked BEFORE anything is removed,
    // so a failed download never leaves you without Rostr.
    private const string UpdateScript = """
        @echo off
        setlocal
        title Rostr updater
        color 0B
        echo ============================================================
        echo   Rostr updater - installing version %ROSTR_VERSION%
        echo ============================================================
        echo.

        echo [1/6] Closing Rostr ...
        set /a TRIES=0
        :waitclose
        tasklist /fi "imagename eq Rostr.exe" 2>nul | find /i "Rostr.exe" >nul
        if errorlevel 1 goto closed
        set /a TRIES+=1
        if %TRIES% geq 10 goto forceclose
        timeout /t 1 /nobreak >nul
        goto waitclose
        :forceclose
        taskkill /f /im Rostr.exe >nul 2>&1
        timeout /t 2 /nobreak >nul
        :closed
        echo Rostr is closed.
        echo.

        echo [2/6] Downloading version %ROSTR_VERSION% from GitHub ...
        set "WORK=%TEMP%\Rostr-update"
        set "NEWEXE=%WORK%\Rostr.exe"
        if exist "%WORK%" rd /s /q "%WORK%"
        mkdir "%WORK%"
        where curl >nul 2>&1
        if errorlevel 1 goto psdownload
        curl.exe -L --fail --retry 3 -o "%NEWEXE%" "%ROSTR_URL%"
        if errorlevel 1 goto failearly
        goto downloaded
        :psdownload
        powershell -NoProfile -ExecutionPolicy Bypass -Command "[Net.ServicePointManager]::SecurityProtocol=[Net.SecurityProtocolType]::Tls12; $ProgressPreference='SilentlyContinue'; Invoke-WebRequest -UseBasicParsing -Uri $env:ROSTR_URL -OutFile $env:NEWEXE"
        if errorlevel 1 goto failearly
        :downloaded
        if not exist "%NEWEXE%" goto failearly
        echo.

        echo [3/6] Checking the download (SHA-256) ...
        powershell -NoProfile -ExecutionPolicy Bypass -Command "if ((Get-FileHash -Algorithm SHA256 -LiteralPath $env:NEWEXE).Hash -ieq $env:ROSTR_SHA) { exit 0 } else { exit 1 }"
        if errorlevel 1 goto badhash
        echo Checksum OK.
        echo.

        echo [4/6] Unregistering and uninstalling the old version ...
        start "" /wait "%ROSTR_OLD%" --uninstall
        if "%ROSTR_INSTALLED%"=="1" if exist "%ROSTR_INSTALLEXE%" del /f /q "%ROSTR_INSTALLEXE%"
        echo Done.
        echo.

        echo [5/6] Installing and registering version %ROSTR_VERSION% ...
        if "%ROSTR_INSTALLED%"=="1" goto doinstall
        copy /y "%NEWEXE%" "%ROSTR_OLD%" >nul
        if errorlevel 1 goto failinstall
        set "LAUNCH=%ROSTR_OLD%"
        echo Replaced the old Rostr.exe. It registers itself when it starts.
        goto installed
        :doinstall
        start "" /wait "%NEWEXE%" --install
        if errorlevel 1 goto failinstall
        set "LAUNCH=%ROSTR_INSTALLEXE%"
        if not exist "%LAUNCH%" goto failinstall
        echo Installed.
        :installed
        echo.

        echo [6/6] Starting Rostr ...
        rd /s /q "%WORK%" >nul 2>&1
        start "" "%LAUNCH%" %ROSTR_ARGS%
        echo.
        echo Done. Rostr %ROSTR_VERSION% is installed. This window closes by itself.
        timeout /t 3 >nul
        exit /b 0

        :failearly
        echo.
        echo DOWNLOAD FAILED. Nothing was changed. Starting the old version again.
        if exist "%ROSTR_OLD%" start "" "%ROSTR_OLD%"
        goto endfail

        :badhash
        echo.
        echo The download does not match its checksum, so it was NOT used. Nothing was changed.
        echo Starting the old version again.
        if exist "%ROSTR_OLD%" start "" "%ROSTR_OLD%"
        goto endfail

        :failinstall
        echo.
        echo INSTALL FAILED. The new Rostr.exe is in: %NEWEXE%
        if "%ROSTR_INSTALLED%"=="1" goto failinstall2
        if exist "%ROSTR_OLD%" start "" "%ROSTR_OLD%"
        goto endfail
        :failinstall2
        if exist "%NEWEXE%" start "" "%NEWEXE%"
        goto endfail

        :endfail
        echo.
        pause
        exit /b 1
        """;
}
