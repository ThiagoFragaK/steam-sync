using SteamSync.Worker.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SteamSync.Worker.Data.Configurations;

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
    }
}
