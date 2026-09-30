using SteamSync.Worker.Domain.Entities;
using SteamSync.Worker.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SteamSync.Worker.Infrastructure.Persistence.Configurations;

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
