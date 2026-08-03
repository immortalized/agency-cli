using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using __PROJECT_NAMESPACE__.Domain.Menu;

namespace __PROJECT_NAMESPACE__.Infrastructure.Persistence.Configurations;

public sealed class MenuItemConfiguration : IEntityTypeConfiguration<MenuItem>
{
    public void Configure(EntityTypeBuilder<MenuItem> builder)
    {
        builder.ToTable("menu_items");

        builder.HasKey(item => item.Id);

        builder.Property(item => item.CategoryId)
            .IsRequired();

        builder.Property(item => item.Name)
            .HasMaxLength(160)
            .IsRequired();

        builder.Property(item => item.Description)
            .HasMaxLength(1000)
            .IsRequired();

        builder.Property(item => item.Price)
            .HasPrecision(12, 2)
            .IsRequired();

        builder.Property(item => item.SortOrder)
            .IsRequired();

        builder.Property(item => item.IsVisible)
            .IsRequired();

        builder.Property(item => item.CreatedAtUtc)
            .IsRequired();

        builder.Property(item => item.UpdatedAtUtc)
            .IsRequired();

        builder.HasOne<MenuCategory>()
            .WithMany()
            .HasForeignKey(item => item.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(item => item.CategoryId);

        builder.HasIndex(item => new
        {
            item.CategoryId,
            item.SortOrder
        });
    }
}