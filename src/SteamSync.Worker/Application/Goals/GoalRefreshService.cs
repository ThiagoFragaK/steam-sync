using Microsoft.EntityFrameworkCore;
using SteamSync.Worker.Application.Goals.Interfaces;
using SteamSync.Worker.Domain.Entities;
using SteamSync.Worker.Domain.Enums;
using SteamSync.Worker.Infrastructure.Persistence;

namespace SteamSync.Worker.Application.Goals;

public class GoalRefreshService : IGoalRefreshService
{
    private readonly WorkerDbContext _db;

    public GoalRefreshService(WorkerDbContext db)
    {
        _db = db;
    }

    public async Task RefreshForGameAsync(int userId, int gameId, CancellationToken cancellationToken = default)
    {
        var goal = await _db.Goals
            .Include(g => g.GoalAchievements)
            .FirstOrDefaultAsync(
                g => g.UserId == userId && g.GameId == gameId && g.Status == GoalStatus.Active,
                cancellationToken);

        if (goal is null)
        {
            return;
        }

        var currentPercent = await GetUserPercentageAsync(userId, gameId, cancellationToken);
        await RefreshGoalEntityAsync(goal, currentPercent, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task RefreshGoalEntityAsync(Goal goal, decimal currentPercent, CancellationToken cancellationToken)
    {
        if (goal.Status != GoalStatus.Active)
        {
            return;
        }

        var achievementIds = goal.GoalAchievements.Select(ga => ga.AchievementId).ToList();
        HashSet<int> unlockedIds = [];
        if (achievementIds.Count > 0)
        {
            unlockedIds = (await _db.UsersAchievements
                    .AsNoTracking()
                    .Where(ua => ua.UserId == goal.UserId && ua.GameId == goal.GameId && achievementIds.Contains(ua.AchievementId))
                    .Select(ua => ua.AchievementId)
                    .ToListAsync(cancellationToken))
                .ToHashSet();
        }

        foreach (var row in goal.GoalAchievements)
        {
            if (unlockedIds.Contains(row.AchievementId))
            {
                row.Status = GoalAchievementStatus.Completed;
            }
            else if (row.Status != GoalAchievementStatus.Completed)
            {
                row.Status = GoalAchievementStatus.Pending;
            }
        }

        var total = goal.GoalAchievements.Count;
        var missing = goal.GoalAchievements.Count(ga => ga.Status == GoalAchievementStatus.Pending);
        goal.AchievementsMissing = missing;

        var percentReached = currentPercent >= goal.PercentageGoal;
        var allAttachedDone = total > 0 && missing == 0;
        if (percentReached || allAttachedDone)
        {
            goal.Status = GoalStatus.Completed;
            goal.CompletedAt = DateTimeOffset.UtcNow;
        }
    }

    private async Task<decimal> GetUserPercentageAsync(int userId, int gameId, CancellationToken cancellationToken)
    {
        var percent = await _db.UsersGames
            .AsNoTracking()
            .Where(ug => ug.UserId == userId && ug.GameId == gameId)
            .Select(ug => (decimal?)ug.AchievementsPercentage)
            .FirstOrDefaultAsync(cancellationToken);
        return percent ?? 0m;
    }
}
