using SteamSync.Worker.Domain.Interfaces;

namespace SteamSync.Worker.Domain.Entities;

public class Achievement : IEntity
{
    public int Id { get; set; }
    public int GameId { get; set; }
    public string? ApiName { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? ImageUrlLock { get; set; }
    public string? ImageUrlUnlock { get; set; }
    public decimal? GlobalPercentage { get; set; }

    public Game Game { get; set; } = null!;
    public ICollection<UsersAchievement> UsersAchievements { get; set; } = [];
}
