using __PROJECT_NAMESPACE__.Domain.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace __PROJECT_NAMESPACE__.Infrastructure.Persistence.Configurations;

public sealed class RefreshTokenConfiguration
    : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(
        EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("auth_refresh_tokens");

        builder.HasKey(token => token.Id);

        builder.Property(token => token.Id)
            .ValueGeneratedNever();

        builder.Property(token => token.UserId)
            .IsRequired();

        builder.Property(token => token.FamilyId)
            .IsRequired();

        builder.Property(token => token.TokenHash)
            .HasColumnType("bytea")
            .IsRequired();

        builder.Property(token => token.CreatedAtUtc)
            .IsRequired();

        builder.Property(token => token.ExpiresAtUtc)
            .IsRequired();

        builder.Property(token => token.CreatedByIpAddress)
            .HasMaxLength(64);

        builder.Property(token => token.RevokedByIpAddress)
            .HasMaxLength(64);

        builder.Property(token => token.UserAgent)
            .HasMaxLength(512);

        builder.Property(token => token.RevocationReason)
            .HasMaxLength(256);

        builder.HasIndex(token => token.TokenHash)
            .IsUnique()
            .HasDatabaseName(
                "ux_auth_refresh_tokens_token_hash");

        builder.HasIndex(token => token.FamilyId)
            .HasDatabaseName(
                "ix_auth_refresh_tokens_family_id");

        builder.HasIndex(token => new
            {
                token.UserId,
                token.ExpiresAtUtc
            })
            .HasDatabaseName(
                "ix_auth_refresh_tokens_user_expiry");

        builder.HasOne(token => token.User)
            .WithMany(user => user.RefreshTokens)
            .HasForeignKey(token => token.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}