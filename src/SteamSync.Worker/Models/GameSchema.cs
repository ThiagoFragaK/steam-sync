namespace SteamSync.Worker.Models;

public class GameSchema
{
    public string? GameName { get; set; }
    public List<GameSchemaAchievement> Achievements { get; set; } = [];
}
