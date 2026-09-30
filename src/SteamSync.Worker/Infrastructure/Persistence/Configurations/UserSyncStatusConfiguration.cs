using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SteamSync.Worker.Domain.Entities;
using SteamSync.Worker.Domain.Interfaces;

namespace SteamSync.Worker.Infrastructure.Persistence.Configurations;

public class UserSyncStatusConfiguration : IEntityTypeConfiguration<UserSyncStatus>
{
    public void Configure(EntityTypeBuilder<UserSyncStatus> builder)
    {
        builder.ToTable("user_sync_status");
        builder.HasKey(e => e.UserId);

        builder.Property(e => e.UserId)
            .HasColumnName("user_id");

        builder.Property(e => e.LastPartialSync)
            .HasColumnName("last_partial_sync");

        builder.Property(e => e.LastFullSync)
            .HasColumnName("last_full_sync");

        builder.Property(e => e.SyncProgressPercent)
            .HasColumnName("sync_progress_percent")
            .HasPrecision(5, 2)
            .IsRequired()
            .HasDefaultValue(0);

        builder.Property(e => e.GamesSyncedCount)
            .HasColumnName("games_synced_count")
            .IsRequired()
            .HasDefaultValue(0);

        builder.Property(e => e.TotalGamesCount)
            .HasColumnName("total_games_count")
            .IsRequired()
            .HasDefaultValue(0);

        builder.Property(e => e.Status)
            .HasColumnName("status")
            .IsRequired()
            .HasConversion<int>()
            .HasDefaultValue(SteamSync.Shared.SyncStatus.Pending);

        builder.Property(e => e.LastError)
            .HasColumnName("last_error")
            .HasMaxLength(2000);

        builder.Property(e => e.LastJobId)
            .HasColumnName("last_job_id");

        builder.Property(e => e.UpdatedAt)
            .HasColumnName("updated_at")
            .IsRequired();

        builder.HasOne(e => e.User)
            .WithOne()
            .HasForeignKey<UserSyncStatus>(e => e.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
