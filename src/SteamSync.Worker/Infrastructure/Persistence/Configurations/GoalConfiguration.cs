using SteamSync.Worker.Domain.Entities;
using SteamSync.Worker.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SteamSync.Worker.Infrastructure.Persistence.Configurations;

public class GoalConfiguration : IEntityTypeConfiguration<Goal>
{
    public void Configure(EntityTypeBuilder<Goal> builder)
    {
        builder.ToTable("goals");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.PercentageGoal)
            .HasPrecision(5, 2);

        builder.Property(e => e.AchievementsMissing)
            .HasDefaultValue(0);

        builder.Property(e => e.Status)
            .HasConversion<int>()
            .HasDefaultValue(GoalStatus.Active);

        builder.Property(e => e.CreatedAt)
            .HasDefaultValueSql("NOW()");

        builder.HasIndex(e => new { e.UserId, e.GameId })
            .IsUnique()
            .HasFilter($"\"Status\" = {(int)GoalStatus.Active}");

        builder.HasMany(e => e.GoalAchievements)
            .WithOne(ga => ga.Goal)
            .HasForeignKey(ga => ga.GoalId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
