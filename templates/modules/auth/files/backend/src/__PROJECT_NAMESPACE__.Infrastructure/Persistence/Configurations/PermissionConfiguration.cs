using __PROJECT_NAMESPACE__.Domain.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace __PROJECT_NAMESPACE__.Infrastructure.Persistence.Configurations;

public sealed class PermissionConfiguration
    : IEntityTypeConfiguration<Permission>
{
    public void Configure(
        EntityTypeBuilder<Permission> builder)
    {
        builder.ToTable("auth_permissions");

        builder.HasKey(permission => permission.Id);

        builder.Property(permission => permission.Id)
            .ValueGeneratedNever();

        builder.Property(permission => permission.Name)
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(permission => permission.Module)
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(permission => permission.Description)
            .HasMaxLength(256)
            .IsRequired();

        builder.Property(permission => permission.CreatedAtUtc)
            .IsRequired();

        builder.HasIndex(permission => permission.Name)
            .IsUnique()
            .HasDatabaseName(
                "ux_auth_permissions_name");

        builder.HasIndex(permission => permission.Module)
            .HasDatabaseName(
                "ix_auth_permissions_module");

        builder.HasMany(permission => permission.RolePermissions)
            .WithOne(rolePermission => rolePermission.Permission)
            .HasForeignKey(rolePermission => rolePermission.PermissionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(permission => permission.RolePermissions)
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
