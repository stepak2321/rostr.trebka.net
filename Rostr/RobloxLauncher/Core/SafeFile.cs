using System.IO;
using System.Text.Json;

namespace RobloxLauncher.Core;

/// <summary>
/// Safe JSON storage: a damaged file is moved aside (never overwritten by an empty one),
/// and writes go to a temp file first so a crash cannot leave half a file behind.
/// </summary>
public static class SafeFile
{
    public static T? ReadJson<T>(string path) where T : class
    {
        try
        {
            if (!File.Exists(path)) return null;
            var text = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(text)) return null;
            return JsonSerializer.Deserialize<T>(text);
        }
        catch (Exception ex)
        {
            Quarantine(path, ex);
            return null;
        }
    }

    /// <summary>Keeps the unreadable file as name.corrupt-yyyyMMdd-HHmmss so nothing is lost.</summary>
    public static void Quarantine(string path, Exception? why = null)
    {
        try
        {
            if (!File.Exists(path)) return;
            var dest = $"{path}.corrupt-{DateTime.Now:yyyyMMdd-HHmmss}";
            File.Move(path, dest);
            Log.Error($"{Path.GetFileName(path)} could not be read; kept a copy as {Path.GetFileName(dest)}", why);
        }
        catch (Exception ex)
        {
            Log.Error("Could not set aside the damaged file " + path, ex);
        }
    }

    public static bool WriteAtomic(string path, string text)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, text);
            if (File.Exists(path)) File.Replace(tmp, path, null);
            else File.Move(tmp, path);
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("Could not save " + path, ex);
            return false;
        }
    }
}
