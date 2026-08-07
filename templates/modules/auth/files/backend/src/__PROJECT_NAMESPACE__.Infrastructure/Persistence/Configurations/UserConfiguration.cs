using __PROJECT_NAMESPACE__.Domain.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace __PROJECT_NAMESPACE__.Infrastructure.Persistence.Configurations;

public sealed class UserConfiguration
    : IEntityTypeConfiguration<User>
{
    public void Configure(
        EntityTypeBuilder<User> builder)
    {
        builder.ToTable("auth_users");

        builder.HasKey(user => user.Id);

        builder.Property(user => user.Id)
            .ValueGeneratedNever();

        builder.Property(user => user.RoleId)
            .IsRequired();

        builder.Property(user => user.Username)
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(user => user.NormalizedUsername)
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(user => user.Email)
            .HasMaxLength(320);

        builder.Property(user => user.NormalizedEmail)
            .HasMaxLength(320);

        builder.Property(user => user.PasswordHash)
            .HasMaxLength(512)
            .IsRequired();

        builder.Property(user => user.IsActive)
            .IsRequired();

        builder.Property(user => user.MustChangePassword)
            .IsRequired();

        builder.Property(user => user.AuthVersion)
            .IsRequired();

        builder.Property(user => user.CreatedAtUtc)
            .IsRequired();

        builder.Property(user => user.UpdatedAtUtc)
            .IsRequired();

        builder.HasIndex(user => user.NormalizedUsername)
            .IsUnique()
            .HasDatabaseName(
                "ux_auth_users_normalized_username");

        builder.HasIndex(user => user.NormalizedEmail)
            .IsUnique()
            .HasFilter("\"NormalizedEmail\" IS NOT NULL")
            .HasDatabaseName(
                "ux_auth_users_normalized_email");

        builder.HasIndex(user => user.RoleId)
            .HasDatabaseName(
                "ix_auth_users_role_id");

        builder.HasMany(user => user.RefreshTokens)
            .WithOne(token => token.User)
            .HasForeignKey(token => token.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(user => user.RefreshTokens)
            .UsePropertyAccessMode(
                PropertyAccessMode.Field);
    }
}