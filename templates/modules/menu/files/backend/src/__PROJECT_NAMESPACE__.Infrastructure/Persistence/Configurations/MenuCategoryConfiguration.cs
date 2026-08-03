using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using __PROJECT_NAMESPACE__.Domain.Menu;

namespace __PROJECT_NAMESPACE__.Infrastructure.Persistence.Configurations;

public sealed class MenuCategoryConfiguration : IEntityTypeConfiguration<MenuCategory>
{
    public void Configure(EntityTypeBuilder<MenuCategory> builder)
    {
        builder.ToTable("menu_categories");

        builder.HasKey(category => category.Id);

        builder.Property(category => category.Name)
            .HasMaxLength(120)
            .IsRequired();

        builder.Property(category => category.Slug)
            .HasMaxLength(120)
            .IsRequired();

        builder.HasIndex(category => category.Slug)
            .IsUnique();

        builder.Property(category => category.SortOrder)
            .IsRequired();

        builder.Property(category => category.IsVisible)
            .IsRequired();

        builder.Property(category => category.CreatedAtUtc)
            .IsRequired();

        builder.Property(category => category.UpdatedAtUtc)
            .IsRequired();
    }
}