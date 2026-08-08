using __PROJECT_NAMESPACE__.Api.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using __PROJECT_NAMESPACE__.Api.Contracts.Menu;
using __PROJECT_NAMESPACE__.Domain.Menu;
using __PROJECT_NAMESPACE__.Infrastructure.Persistence;
using __PROJECT_NAMESPACE__.Application.Menu;

namespace __PROJECT_NAMESPACE__.Api.Controllers;

[ApiController]
[Route("api/admin/menu/categories")]
public sealed class MenuCategoriesController(AppDbContext dbContext) : ControllerBase
{
    [HttpGet]
    [HasPermission(MenuPermissions.Read)]
    public async Task<ActionResult<IReadOnlyList<MenuCategoryResponse>>> GetAll(
        CancellationToken cancellationToken)
    {
        var categories = await dbContext
            .Set<MenuCategory>()
            .AsNoTracking()
            .OrderBy(category => category.SortOrder)
            .ThenBy(category => category.Name)
            .Select(category => ToResponse(category))
            .ToListAsync(cancellationToken);

        return Ok(categories);
    }

    [HttpGet("{id:guid}")]
    [HasPermission(MenuPermissions.Read)]
    public async Task<ActionResult<MenuCategoryResponse>> GetById(
        Guid id,
        CancellationToken cancellationToken)
    {
        var category = await dbContext
            .Set<MenuCategory>()
            .AsNoTracking()
            .SingleOrDefaultAsync(category => category.Id == id, cancellationToken);

        if (category is null)
        {
            return NotFound();
        }

        return Ok(ToResponse(category));
    }

    [HttpPost]
    [HasPermission(MenuPermissions.Create)]
    public async Task<ActionResult<MenuCategoryResponse>> Create(
        CreateMenuCategoryRequest request,
        CancellationToken cancellationToken)
    {
        var name = request.Name.Trim();
        var slug = request.Slug.Trim().ToLowerInvariant();

        if (await SlugExists(slug, null, cancellationToken))
        {
            return CreateSlugConflict(slug);
        }

        var category = new MenuCategory(name, slug, request.SortOrder);

        dbContext.Set<MenuCategory>().Add(category);
        await dbContext.SaveChangesAsync(cancellationToken);

        return CreatedAtAction(
            nameof(GetById),
            new { id = category.Id },
            ToResponse(category));
    }

    [HttpPut("{id:guid}")]
    [HasPermission(MenuPermissions.Update)]
    public async Task<ActionResult<MenuCategoryResponse>> Update(
        Guid id,
        UpdateMenuCategoryRequest request,
        CancellationToken cancellationToken)
    {
        var category = await dbContext
            .Set<MenuCategory>()
            .SingleOrDefaultAsync(category => category.Id == id, cancellationToken);

        if (category is null)
        {
            return NotFound();
        }

        var name = request.Name.Trim();
        var slug = request.Slug.Trim().ToLowerInvariant();

        if (await SlugExists(slug, id, cancellationToken))
        {
            return CreateSlugConflict(slug);
        }

        category.Update(
            name,
            slug,
            request.SortOrder,
            request.IsVisible);

        await dbContext.SaveChangesAsync(cancellationToken);

        return Ok(ToResponse(category));
    }

    [HttpDelete("{id:guid}")]
    [HasPermission(MenuPermissions.Delete)]
    public async Task<IActionResult> Delete(
        Guid id,
        CancellationToken cancellationToken)
    {
        var category = await dbContext
            .Set<MenuCategory>()
            .SingleOrDefaultAsync(category => category.Id == id, cancellationToken);

        if (category is null)
        {
            return NotFound();
        }

        var containsItems = await dbContext
            .Set<MenuItem>()
            .AnyAsync(item => item.CategoryId == id, cancellationToken);

        if (containsItems)
        {
            return Conflict(new ProblemDetails
            {
                Title = "Menu category is not empty.",
                Detail = "Delete or move every menu item before deleting this category.",
                Status = StatusCodes.Status409Conflict
            });
        }

        dbContext.Set<MenuCategory>().Remove(category);
        await dbContext.SaveChangesAsync(cancellationToken);

        return NoContent();
    }

    private async Task<bool> SlugExists(
        string slug,
        Guid? excludedCategoryId,
        CancellationToken cancellationToken)
    {
        return await dbContext
            .Set<MenuCategory>()
            .AnyAsync(
                category =>
                    category.Slug == slug &&
                    (!excludedCategoryId.HasValue || category.Id != excludedCategoryId.Value),
                cancellationToken);
    }

    private static ConflictObjectResult CreateSlugConflict(string slug)
    {
        return new ConflictObjectResult(new ProblemDetails
        {
            Title = "Menu category slug already exists.",
            Detail = $"A menu category with slug '{slug}' already exists.",
            Status = StatusCodes.Status409Conflict
        });
    }

    private static MenuCategoryResponse ToResponse(MenuCategory category)
    {
        return new MenuCategoryResponse(
            category.Id,
            category.Name,
            category.Slug,
            category.SortOrder,
            category.IsVisible,
            category.CreatedAtUtc,
            category.UpdatedAtUtc);
    }
}
