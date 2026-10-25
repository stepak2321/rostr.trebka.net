using System.Net.Http;
using System.Text.Json;

namespace RobloxLauncher.Core;

public class ClientVersionInfo
{
    public string Version { get; set; } = "";
    public string VersionGuid { get; set; } = "";
}

public class VersionChecker
{
    private static readonly HttpClient _http = new();

    public async Task<ClientVersionInfo> GetLatestAsync()
    {
        var url = "https://clientsettingscdn.roblox.com/v2/client-version/WindowsPlayer";
        var json = await _http.GetStringAsync(url);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        return new ClientVersionInfo
        {
            Version = root.GetProperty("version").GetString() ?? "",
            VersionGuid = root.GetProperty("clientVersionUpload").GetString() ?? ""
        };
    }
}