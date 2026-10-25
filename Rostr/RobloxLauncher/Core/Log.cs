using System.IO;
using System.Text;

namespace RobloxLauncher.Core;

/// <summary>Tiny file logger. Never throws. Rotates to launcher.log.old at 1 MB.</summary>
public static class Log
{
    private static readonly object Gate = new();
    private const long MaxBytes = 1024 * 1024;

    public static string Dir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Rostr");

    public static string FilePath => Path.Combine(Dir, "launcher.log");

    public static void Info(string message) => Write("INFO ", message);

    public static void Warn(string message) => Write("WARN ", message);

    public static void Error(string context, Exception? ex = null) =>
        Write("ERROR", ex == null ? context : $"{context}: {ex}");

    private static void Write(string level, string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Dir);
                var fi = new FileInfo(FilePath);
                if (fi.Exists && fi.Length > MaxBytes)
                {
                    var old = FilePath + ".old";
                    if (File.Exists(old)) File.Delete(old);
                    File.Move(FilePath, old);
                }
                File.AppendAllText(FilePath,
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {level} {message}{Environment.NewLine}",
                    Encoding.UTF8);
            }
        }
        catch { }
    }
}
