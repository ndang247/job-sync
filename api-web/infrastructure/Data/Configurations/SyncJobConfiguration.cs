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
        builder.Property(s => s.Result).HasColumnType("jsonb");
        builder.Property(s => s.Error).HasMaxLength(2000);
        builder.HasOne(s => s.User)
            .WithMany()
            .HasForeignKey(s => s.UserId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasQueryFilter(s => s.DeletedAt == null);
    }
}
