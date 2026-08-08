using __PROJECT_NAMESPACE__.Api.Contracts.Menu;
using __PROJECT_NAMESPACE__.Domain.Menu;
using __PROJECT_NAMESPACE__.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace __PROJECT_NAMESPACE__.Api.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/menu")]
public sealed class PublicMenuController(AppDbContext dbContext)
    : ControllerBase
{
    [HttpGet("categories")]
    public async Task<ActionResult<
        IReadOnlyList<PublicMenuCategoryResponse>>> GetCategories(
        CancellationToken cancellationToken)
    {
        var categories = await dbContext.Set<MenuCategory>()
            .AsNoTracking()
            .Where(category => category.IsVisible)
            .OrderBy(category => category.SortOrder)
            .ThenBy(category => category.Name)
            .Select(category => new PublicMenuCategoryResponse(
                category.Id,
                category.Name,
                category.Slug,
                category.SortOrder))
            .ToListAsync(cancellationToken);

        return Ok(categories);
    }

    [HttpGet("items")]
    public async Task<ActionResult<
        IReadOnlyList<PublicMenuItemResponse>>> GetItems(
        [FromQuery] Guid? categoryId,
        CancellationToken cancellationToken)
    {
        var visibleCategoryIds = dbContext
            .Set<MenuCategory>()
            .Where(category => category.IsVisible)
            .Select(category => category.Id);

        var query = dbContext.Set<MenuItem>()
            .AsNoTracking()
            .Where(item =>
                item.IsVisible
                && visibleCategoryIds.Contains(item.CategoryId));

        if (categoryId.HasValue)
        {
            query = query.Where(
                item => item.CategoryId == categoryId.Value);
        }

        var items = await query
            .OrderBy(item => item.CategoryId)
            .ThenBy(item => item.SortOrder)
            .ThenBy(item => item.Name)
            .Select(item => new PublicMenuItemResponse(
                item.Id,
                item.CategoryId,
                item.Name,
                item.Description,
                item.Price,
                item.SortOrder))
            .ToListAsync(cancellationToken);

        return Ok(items);
    }
}
