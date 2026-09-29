using SteamSync.Worker.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SteamSync.Worker.Data.Configurations;

public class UsersAchievementConfiguration : IEntityTypeConfiguration<UsersAchievement>
{
    public void Configure(EntityTypeBuilder<UsersAchievement> builder)
    {
        builder.ToTable("users_achievements");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.UnlockDate)
            .HasColumnName("unlock_date");

        builder.HasIndex(e => new { e.UserId, e.AchievementId }).IsUnique();
    }
}
