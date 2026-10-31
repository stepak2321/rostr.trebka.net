using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace RobloxLauncher.Core;

public class SavedAccount
{
    public long UserId { get; set; }
    public string Name { get; set; } = "";
    public string DisplayName { get; set; } = "";
    // The .ROBLOSECURITY cookie, encrypted with Windows DPAPI (this Windows user only), base64.
    public string Protected { get; set; } = "";

    public string Label => string.IsNullOrEmpty(DisplayName) || DisplayName == Name
        ? Name : $"{DisplayName} (@{Name})";
}

public static class AccountStore
{
    private static readonly List<SavedAccount> _accounts = Load();
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("MyRobloxLauncher.accounts.v1");

    public static IReadOnlyList<SavedAccount> All => _accounts;

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Rostr", "accounts.json");

    private static List<SavedAccount> Load() =>
        SafeFile.ReadJson<List<SavedAccount>>(FilePath) ?? new();

    private static void Save() =>
        SafeFile.WriteAtomic(FilePath,
            JsonSerializer.Serialize(_accounts, new JsonSerializerOptions { WriteIndented = true }));

    public static SavedAccount? Find(long userId) => _accounts.FirstOrDefault(a => a.UserId == userId);

    public static void AddOrUpdate(long userId, string name, string displayName, string cookie)
    {
        var protectedBytes = ProtectedData.Protect(
            Encoding.UTF8.GetBytes(cookie), Entropy, DataProtectionScope.CurrentUser);

        var acc = Find(userId);
        if (acc == null)
        {
            acc = new SavedAccount { UserId = userId };
            _accounts.Add(acc);
        }
        acc.Name = name;
        acc.DisplayName = displayName;
        acc.Protected = Convert.ToBase64String(protectedBytes);
        Save();
    }

    public static void Remove(long userId)
    {
        _accounts.RemoveAll(a => a.UserId == userId);
        Save();
    }

    public static string? GetCookie(SavedAccount acc)
    {
        try
        {
            var bytes = ProtectedData.Unprotect(
                Convert.FromBase64String(acc.Protected), Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(bytes);
        }
        catch (Exception ex)
        {
            Log.Error($"Could not decrypt the saved login for {acc.Name}", ex);
            return null;
        }
    }
}
