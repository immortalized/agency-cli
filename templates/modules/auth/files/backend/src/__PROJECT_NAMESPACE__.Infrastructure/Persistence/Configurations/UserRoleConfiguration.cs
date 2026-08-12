using __PROJECT_NAMESPACE__.Domain.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace __PROJECT_NAMESPACE__.Infrastructure.Persistence.Configurations;

public sealed class UserRoleConfiguration
    : IEntityTypeConfiguration<UserRole>
{
    public void Configure(
        EntityTypeBuilder<UserRole> builder)
    {
        builder.ToTable("auth_user_roles");

        builder.HasKey(assignment => new
        {
            assignment.UserId,
            assignment.RoleId
        });

        builder.Property(assignment => assignment.CreatedAtUtc)
            .IsRequired();

        builder.HasIndex(assignment => assignment.RoleId)
            .HasDatabaseName(
                "ix_auth_user_roles_role_id");
    }
}
