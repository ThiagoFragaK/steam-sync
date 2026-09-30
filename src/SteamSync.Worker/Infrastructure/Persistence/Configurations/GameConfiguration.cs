using SteamSync.Worker.Domain.Entities;
using SteamSync.Worker.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SteamSync.Worker.Infrastructure.Persistence.Configurations;

public class GameConfiguration : IEntityTypeConfiguration<Game>
{
    public void Configure(EntityTypeBuilder<Game> builder)
    {
        builder.ToTable("games");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(e => e.ImageUrl)
            .HasMaxLength(500);

        builder.Property(e => e.HeaderImageUrl)
            .HasMaxLength(500);

        builder.Property(e => e.GameSteamId)
            .HasMaxLength(100);

        builder.Property(e => e.Developers)
            .HasMaxLength(500);

        builder.Property(e => e.Publishers)
            .HasMaxLength(500);

        builder.Property(e => e.HasCommunityVisibleStats);

        builder.Property(e => e.SchemaSyncedAt);

        builder.HasIndex(e => e.GameSteamId)
            .IsUnique()
            .HasFilter("\"GameSteamId\" IS NOT NULL");

        builder.HasMany(e => e.Achievements)
            .WithOne(a => a.Game)
            .HasForeignKey(a => a.GameId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(e => e.UsersGames)
            .WithOne(ug => ug.Game)
            .HasForeignKey(ug => ug.GameId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(e => e.Goals)
            .WithOne(g => g.Game)
            .HasForeignKey(g => g.GameId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(e => e.UsersAchievements)
            .WithOne(ua => ua.Game)
            .HasForeignKey(ua => ua.GameId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
