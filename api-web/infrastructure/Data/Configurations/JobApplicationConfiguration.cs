using core.Entities;
using core.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace infrastructure.Data.Configurations;

public class JobApplicationConfiguration : IEntityTypeConfiguration<JobApplication>
{
    public void Configure(EntityTypeBuilder<JobApplication> builder)
    {
        builder.HasKey(ja => ja.Id);
        builder.Property(ja => ja.CompanyName).IsRequired();
        builder.Property(ja => ja.JobRole).IsRequired();
        builder.Property(ja => ja.AppliedDate).IsRequired();
        builder.Property(ja => ja.MessageId).IsRequired();
        builder.Property(ja => ja.Status)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();
        builder.HasIndex(ja => ja.MessageId).IsUnique();
        builder.HasIndex(ja => new { ja.CreatedAt, ja.Id })
            .IsDescending(true, true)
            .HasFilter("\"DeletedAt\" IS NULL");
        builder.HasIndex(ja => new { ja.UserId, ja.CreatedAt, ja.Id })
            .IsDescending(false, true, true)
            .HasFilter("\"DeletedAt\" IS NULL");
        builder.HasOne(ja => ja.User)
            .WithMany(user => user.JobApplications)
            .HasForeignKey(ja => ja.UserId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(ja => ja.EmailConnection)
            .WithMany()
            .HasForeignKey(ja => ja.EmailConnectionId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasQueryFilter(ja => ja.DeletedAt == null);
    }
}
