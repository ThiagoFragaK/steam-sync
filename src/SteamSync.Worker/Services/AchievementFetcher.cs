using System.Text.Json;
using SteamSync.Worker.Models;
using SteamSync.Worker.Options;
using SteamSync.Worker.Services;
using Microsoft.Extensions.Options;

namespace SteamSync.Worker.Services;

public class AchievementFetcher : IAchievementFetcher
{
    private const string SteamApiBaseUrl = "https://api.steampowered.com";
    private const string StoreApiBaseUrl = "https://store.steampowered.com/api";

    private readonly HttpClient _httpClient;
    private readonly ILogger<AchievementFetcher> _logger;
    private readonly string _apiKey;

    public AchievementFetcher(HttpClient httpClient, IOptions<SteamApiOptions> options, ILogger<AchievementFetcher> logger)
    {
        _httpClient = httpClient;
        _logger = logger;

        _apiKey = options.Value.ApiKey;
        if (string.IsNullOrWhiteSpace(_apiKey))
        {
            throw new InvalidOperationException("Steam API Key is not configured. Set SteamApi:ApiKey in User Secrets or appsettings.");
        }

        _httpClient.BaseAddress = new Uri(SteamApiBaseUrl);
        _httpClient.Timeout = TimeSpan.FromSeconds(30);
    }

    public async Task<Player?> GetPlayerBySteamIdAsync(string steamId, CancellationToken cancellationToken = default)
    {
        try
        {
            var url = $"/ISteamUser/GetPlayerSummaries/v0002/?key={_apiKey}&steamids={steamId}";
            using var document = await GetJsonAsync(url, cancellationToken);
            if (document is null)
            {
                return null;
            }

            if (!TryGetProperty(document.RootElement, "response", out var response) ||
                !TryGetProperty(response, "players", out var players) ||
                players.ValueKind != JsonValueKind.Array ||
                players.GetArrayLength() == 0)
            {
                return null;
            }

            var player = players[0];
            return new Player
            {
                SteamId = GetString(player, "steamid"),
                PersonaName = GetString(player, "personaname"),
                ProfileUrl = GetString(player, "profileurl"),
                Avatar = GetString(player, "avatar"),
                AvatarFull = GetString(player, "avatarfull"),
                CommunityVisibilityState = GetInt(player, "communityvisibilitystate"),
                PersonaState = GetInt(player, "personastate")
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching player profile for Steam ID {SteamId}", steamId);
            return null;
        }
    }

    public async Task<IReadOnlyList<RecentlyPlayedGame>> GetRecentlyPlayedGamesAsync(string steamId, CancellationToken cancellationToken = default)
    {
        try
        {
            var url = $"/IPlayerService/GetRecentlyPlayedGames/v0001/?key={_apiKey}&steamid={steamId}&format=json";
            using var document = await GetJsonAsync(url, cancellationToken);
            if (document is null ||
                !TryGetProperty(document.RootElement, "response", out var response) ||
                !TryGetProperty(response, "games", out var games) ||
                games.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            var result = new List<RecentlyPlayedGame>();
            foreach (var game in games.EnumerateArray())
            {
                result.Add(new RecentlyPlayedGame
                {
                    AppId = GetInt(game, "appid") ?? 0,
                    Name = GetString(game, "name"),
                    Playtime2WeeksMinutes = GetInt(game, "playtime_2weeks") ?? 0,
                    PlaytimeForeverMinutes = GetInt(game, "playtime_forever") ?? 0,
                    ImgIconUrl = GetString(game, "img_icon_url")
                });
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching recently played games for Steam ID {SteamId}", steamId);
            return [];
        }
    }

    public async Task<IReadOnlyList<OwnedGame>> GetOwnedGamesAsync(string steamId, CancellationToken cancellationToken = default)
    {
        try
        {
            var url = $"/IPlayerService/GetOwnedGames/v0001/?key={_apiKey}&steamid={steamId}&include_appinfo=true&include_played_free_games=true&format=json";
            using var document = await GetJsonAsync(url, cancellationToken);
            if (document is null ||
                !TryGetProperty(document.RootElement, "response", out var response) ||
                !TryGetProperty(response, "games", out var games) ||
                games.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            var result = new List<OwnedGame>();
            foreach (var game in games.EnumerateArray())
            {
                result.Add(new OwnedGame
                {
                    AppId = GetInt(game, "appid") ?? 0,
                    Name = GetString(game, "name"),
                    ImgIconUrl = GetString(game, "img_icon_url"),
                    PlaytimeForeverMinutes = GetInt(game, "playtime_forever") ?? 0,
                    LastPlayedUnix = GetLong(game, "rtime_last_played"),
                    HasCommunityVisibleStats = GetBool(game, "has_community_visible_stats")
                });
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching owned games for Steam ID {SteamId}", steamId);
            return [];
        }
    }

    public async Task<PlayerAchievementsResult?> GetPlayerAchievementsAsync(string steamId, int appId, CancellationToken cancellationToken = default)
    {
        try
        {
            var url = $"/ISteamUserStats/GetPlayerAchievements/v0001/?appid={appId}&key={_apiKey}&steamid={steamId}";
            using var document = await GetJsonAsync(url, cancellationToken, requireSuccessStatusCode: false);
            if (document is null || !TryGetProperty(document.RootElement, "playerstats", out var playerStats))
            {
                return null;
            }

            var success = GetBool(playerStats, "success");
            var error = GetString(playerStats, "error");
            var achievements = new List<PlayerAchievement>();
            if (TryGetProperty(playerStats, "achievements", out var achievementArray) &&
                achievementArray.ValueKind == JsonValueKind.Array)
            {
                foreach (var achievement in achievementArray.EnumerateArray())
                {
                    achievements.Add(new PlayerAchievement
                    {
                        ApiName = GetString(achievement, "apiname"),
                        Achieved = GetInt(achievement, "achieved") ?? 0,
                        UnlockTimeUnix = GetLong(achievement, "unlocktime") ?? 0
                    });
                }
            }

            return new PlayerAchievementsResult
            {
                Success = success,
                Error = error,
                GameName = GetString(playerStats, "gameName"),
                Achievements = achievements
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching player achievements for Steam ID {SteamId}, App ID {AppId}", steamId, appId);
            return null;
        }
    }

    public async Task<GameSchema?> GetGameSchemaAsync(int appId, CancellationToken cancellationToken = default)
    {
        try
        {
            var url = $"/ISteamUserStats/GetSchemaForGame/v2/?key={_apiKey}&appid={appId}&l=english";
            using var document = await GetJsonAsync(url, cancellationToken, requireSuccessStatusCode: false);
            if (document is null)
            {
                return null;
            }

            var root = document.RootElement.Clone();
            if (!TryGetProperty(root, "game", out var game))
            {
                return null;
            }

            var achievements = new List<GameSchemaAchievement>();
            if (TryGetProperty(game, "availableGameStats", out var stats) &&
                TryGetProperty(stats, "achievements", out var achievementArray) &&
                achievementArray.ValueKind == JsonValueKind.Array)
            {
                foreach (var achievement in achievementArray.EnumerateArray())
                {
                    achievements.Add(new GameSchemaAchievement
                    {
                        Name = GetString(achievement, "name"),
                        DisplayName = GetString(achievement, "displayName"),
                        Hidden = GetInt(achievement, "hidden") ?? 0,
                        Description = GetString(achievement, "description"),
                        Icon = GetString(achievement, "icon"),
                        IconGray = GetString(achievement, "icongray")
                    });
                }
            }

            return new GameSchema
            {
                GameName = GetString(game, "gameName"),
                Achievements = achievements
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching game schema for App ID {AppId}", appId);
            return null;
        }
    }

    public async Task<StoreGame?> GetStoreGameDetailsAsync(int appId, CancellationToken cancellationToken = default)
    {
        try
        {
            var url = $"{StoreApiBaseUrl}/appdetails?appids={appId}";
            using var document = await GetJsonAsync(url, cancellationToken);
            if (document is null ||
                !TryGetProperty(document.RootElement, appId.ToString(), out var appElement) ||
                !GetBool(appElement, "success") ||
                !TryGetProperty(appElement, "data", out var data))
            {
                return null;
            }

            return new StoreGame
            {
                Name = GetString(data, "name"),
                HeaderImage = GetString(data, "header_image"),
                Developers = GetStringArray(data, "developers"),
                Publishers = GetStringArray(data, "publishers")
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching store details for App ID {AppId}", appId);
            return null;
        }
    }

    private async Task<JsonDocument?> GetJsonAsync(string url, CancellationToken cancellationToken, bool requireSuccessStatusCode = true)
    {
        using var response = await _httpClient.GetAsync(url, cancellationToken);
        if (requireSuccessStatusCode && !response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Steam request failed with {StatusCode}", response.StatusCode);
            return null;
        }

        try
        {
            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            return JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Steam response was not valid JSON");
            return null;
        }
    }

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            value = default;
            return false;
        }

        if (element.TryGetProperty(name, out value))
        {
            return true;
        }

        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static string? GetString(JsonElement element, string name)
    {
        if (!TryGetProperty(element, name, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
    }

    private static int? GetInt(JsonElement element, string name)
    {
        if (!TryGetProperty(element, name, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number => value.TryGetInt32(out var number) ? number : null,
            JsonValueKind.String => int.TryParse(value.GetString(), out var parsed) ? parsed : null,
            JsonValueKind.True => 1,
            JsonValueKind.False => 0,
            _ => null
        };
    }

    private static long? GetLong(JsonElement element, string name)
    {
        if (!TryGetProperty(element, name, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number => value.TryGetInt64(out var number) ? number : null,
            JsonValueKind.String => long.TryParse(value.GetString(), out var parsed) ? parsed : null,
            _ => null
        };
    }

    private static bool GetBool(JsonElement element, string name)
    {
        if (!TryGetProperty(element, name, out var value))
        {
            return false;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number => value.TryGetInt32(out var number) && number != 0,
            JsonValueKind.String => bool.TryParse(value.GetString(), out var parsed) && parsed,
            _ => false
        };
    }

    private static List<string> GetStringArray(JsonElement element, string name)
    {
        if (!TryGetProperty(element, name, out var value) || value.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var items = new List<string>();
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                var text = item.GetString();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    items.Add(text);
                }
            }
        }

        return items;
    }
}
