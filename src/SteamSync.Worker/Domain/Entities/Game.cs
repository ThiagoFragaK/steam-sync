using SteamSync.Worker.Domain.Interfaces;

namespace SteamSync.Worker.Domain.Entities;

public class Game : IEntity
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    /// <summary>App icon hash or absolute URL from Steam Web API owned/recent games.</summary>
    public string? ImageUrl { get; set; }
    /// <summary>Store header image absolute URL from Steam Store appdetails.</summary>
    public string? HeaderImageUrl { get; set; }
    public string? GameSteamId { get; set; }
    public string? Developers { get; set; }
    public string? Publishers { get; set; }
    public bool? HasCommunityVisibleStats { get; set; }
    public DateTimeOffset? SchemaSyncedAt { get; set; }

    public ICollection<Achievement> Achievements { get; set; } = [];
    public ICollection<UsersGame> UsersGames { get; set; } = [];
    public ICollection<Goal> Goals { get; set; } = [];
    public ICollection<UsersAchievement> UsersAchievements { get; set; } = [];
}
