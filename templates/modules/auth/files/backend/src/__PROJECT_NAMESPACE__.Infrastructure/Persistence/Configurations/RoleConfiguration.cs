using __PROJECT_NAMESPACE__.Domain.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace __PROJECT_NAMESPACE__.Infrastructure.Persistence.Configurations;

public sealed class RoleConfiguration
    : IEntityTypeConfiguration<Role>
{
    public void Configure(
        EntityTypeBuilder<Role> builder)
    {
        builder.ToTable("auth_roles");

        builder.HasKey(role => role.Id);

        builder.Property(role => role.Id)
            .ValueGeneratedNever();

        builder.Property(role => role.Name)
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(role => role.NormalizedName)
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(role => role.DisplayName)
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(role => role.IsSystem)
            .IsRequired();

        builder.Property(role => role.IsActive)
            .IsRequired();

        builder.Property(role => role.CreatedAtUtc)
            .IsRequired();

        builder.Property(role => role.UpdatedAtUtc)
            .IsRequired();

        builder.HasIndex(role => role.NormalizedName)
            .IsUnique()
            .HasDatabaseName(
                "ux_auth_roles_normalized_name");

        builder.HasMany(role => role.Users)
            .WithOne(user => user.Role)
            .HasForeignKey(user => user.RoleId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}