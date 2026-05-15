using core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace infrastructure.Data.Configurations;

public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.HasKey(u => u.Id);
        builder.Property(u => u.FirstName).HasMaxLength(100).IsRequired();
        builder.Property(u => u.LastName).HasMaxLength(100).IsRequired();
        builder.HasMany(u => u.EmailConnections)
            .WithOne(ec => ec.User)
            .HasForeignKey(ec => ec.UserId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasQueryFilter(u => u.DeletedAt == null);
    }
}
