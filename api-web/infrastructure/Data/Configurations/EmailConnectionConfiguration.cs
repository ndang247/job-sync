using core.Entities;
using core.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace infrastructure.Data.Configurations;

public class EmailConnectionConfiguration : IEntityTypeConfiguration<EmailConnection>
{
    public void Configure(EntityTypeBuilder<EmailConnection> builder)
    {
        builder.HasKey(ec => ec.Id);
        builder.Property(ec => ec.Email).IsRequired();
        builder.Property(ec => ec.SubjectId).IsRequired();
        builder.Property(ec => ec.RefreshToken).IsRequired();
        builder.Property(ec => ec.GrantedScopes).IsRequired();
        builder.Property(ec => ec.Status)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();
        builder.Property(ec => ec.Provider)
                .HasConversion<string>()
                .HasMaxLength(30)
                .IsRequired();
        builder.HasIndex(ec => new { ec.Provider, ec.SubjectId }).IsUnique();
        builder.HasQueryFilter(ec => ec.DeletedAt == null);
    }
}
