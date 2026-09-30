using Microsoft.EntityFrameworkCore;
using SteamSync.Worker.Domain.Entities;
using SteamSync.Worker.Domain.Interfaces;

namespace SteamSync.Worker.Infrastructure.Persistence;

public class WorkerDbContext : DbContext
{
    public WorkerDbContext(DbContextOptions<WorkerDbContext> options) : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();
    public DbSet<Game> Games => Set<Game>();
    public DbSet<Achievement> Achievements => Set<Achievement>();
    public DbSet<Goal> Goals => Set<Goal>();
    public DbSet<UsersGame> UsersGames => Set<UsersGame>();
    public DbSet<UsersAchievement> UsersAchievements => Set<UsersAchievement>();
    public DbSet<UserSyncStatus> UserSyncStatuses => Set<UserSyncStatus>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(WorkerDbContext).Assembly);
    }
}
