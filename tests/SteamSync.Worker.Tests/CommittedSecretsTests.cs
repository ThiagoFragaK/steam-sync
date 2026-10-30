using System.Text.Json;
using System.Text.RegularExpressions;

namespace SteamSync.Worker.Tests;

public class CommittedSecretsTests
{
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "SteamSync.slnx")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("Could not locate SteamSync.slnx.");
    }

    [Fact]
    public void Tracked_worker_appsettings_must_not_embed_steam_api_key()
    {
        var path = Path.Combine(FindRepoRoot(), "src", "SteamSync.Worker", "appsettings.json");
        Assert.True(File.Exists(path), path);

        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var apiKey = doc.RootElement.GetProperty("SteamApi").GetProperty("ApiKey").GetString() ?? "";

        Assert.True(string.IsNullOrWhiteSpace(apiKey));
        Assert.False(Regex.IsMatch(apiKey, "^[0-9A-Fa-f]{32}$"));
    }
}
