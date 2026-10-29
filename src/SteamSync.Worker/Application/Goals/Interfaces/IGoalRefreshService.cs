namespace SteamSync.Worker.Application.Goals.Interfaces;

public interface IGoalRefreshService
{
    Task RefreshForGameAsync(int userId, int gameId, CancellationToken cancellationToken = default);
}
