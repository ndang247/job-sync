using System.Text.Json;
using core.Entities;
using core.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace infrastructure.Data.Configurations;

public class SyncJobConfiguration : IEntityTypeConfiguration<SyncJob>
{
    public void Configure(EntityTypeBuilder<SyncJob> builder)
    {
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Status)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();
        builder.Property(s => s.Stage).HasMaxLength(100);
        builder.Property(s => s.SyncStartUtc)
            .HasColumnType("timestamp with time zone")
            .IsRequired();
        builder.Property(s => s.SyncEndUtcExclusive)
            .HasColumnType("timestamp with time zone")
            .IsRequired();
        builder.Property(s => s.SyncTimeZone).HasMaxLength(100);
        // ValueConverter for JsonDocument needed for InMemory provider (test). 
        // Npgsql handle natively, but InMemory no handle JsonDocument. This fix test break.
        // For InMemory, we store the JSON as a string and parse it back to JsonDocument when reading.
        builder.Property(s => s.Result)
            .HasColumnType("jsonb")
            .HasConversion(
                v => v != null ? v.RootElement.GetRawText() : null,
                v => v != null ? JsonDocument.Parse(v, default) : null);
        builder.Property(s => s.Error).HasMaxLength(2000);
        builder.HasOne(s => s.User)
            .WithMany()
            .HasForeignKey(s => s.UserId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(s => s.EmailConnection)
            .WithMany()
            .HasForeignKey(s => s.EmailConnectionId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasQueryFilter(s => s.DeletedAt == null);
    }
}
