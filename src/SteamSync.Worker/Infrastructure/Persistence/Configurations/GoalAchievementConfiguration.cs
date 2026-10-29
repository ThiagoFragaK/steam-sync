using SteamSync.Worker.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SteamSync.Worker.Infrastructure.Persistence.Configurations;

public class GoalAchievementConfiguration : IEntityTypeConfiguration<GoalAchievement>
{
    public void Configure(EntityTypeBuilder<GoalAchievement> builder)
    {
        builder.ToTable("goal_achievements");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Status)
            .HasConversion<int>()
            .HasDefaultValue(GoalAchievementStatus.Pending);

        builder.HasIndex(e => new { e.GoalId, e.AchievementId }).IsUnique();

        builder.HasOne(e => e.Achievement)
            .WithMany()
            .HasForeignKey(e => e.AchievementId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
