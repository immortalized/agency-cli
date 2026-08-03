using __PROJECT_NAMESPACE__.Api.Contracts.Menu;
using __PROJECT_NAMESPACE__.Domain.Menu;
using __PROJECT_NAMESPACE__.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace __PROJECT_NAMESPACE__.Api.Controllers;

[ApiController]
[Route("api/menu/items")]
public sealed class MenuItemsController(AppDbContext dbContext) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<MenuItemResponse>>> GetAll(
        [FromQuery] Guid? categoryId,
        CancellationToken cancellationToken)
    {
        var query = dbContext
            .Set<MenuItem>()
            .AsNoTracking();

        if (categoryId.HasValue)
        {
            query = query.Where(item => item.CategoryId == categoryId.Value);
        }

        var items = await query
            .OrderBy(item => item.CategoryId)
            .ThenBy(item => item.SortOrder)
            .ThenBy(item => item.Name)
            .Select(item => ToResponse(item))
            .ToListAsync(cancellationToken);

        return Ok(items);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<MenuItemResponse>> GetById(
        Guid id,
        CancellationToken cancellationToken)
    {
        var item = await dbContext
            .Set<MenuItem>()
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);

        if (item is null)
        {
            return NotFound();
        }

        return Ok(ToResponse(item));
    }

    [HttpPost]
    public async Task<ActionResult<MenuItemResponse>> Create(
        CreateMenuItemRequest request,
        CancellationToken cancellationToken)
    {
        if (!await CategoryExists(request.CategoryId, cancellationToken))
        {
            return CreateCategoryNotFound(request.CategoryId);
        }

        var item = new MenuItem(
            request.CategoryId,
            request.Name.Trim(),
            request.Description.Trim(),
            request.Price,
            request.SortOrder);

        dbContext.Set<MenuItem>().Add(item);
        await dbContext.SaveChangesAsync(cancellationToken);

        return CreatedAtAction(
            nameof(GetById),
            new { id = item.Id },
            ToResponse(item));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<MenuItemResponse>> Update(
        Guid id,
        UpdateMenuItemRequest request,
        CancellationToken cancellationToken)
    {
        var item = await dbContext
            .Set<MenuItem>()
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);

        if (item is null)
        {
            return NotFound();
        }

        if (!await CategoryExists(request.CategoryId, cancellationToken))
        {
            return CreateCategoryNotFound(request.CategoryId);
        }

        item.Update(
            request.CategoryId,
            request.Name.Trim(),
            request.Description.Trim(),
            request.Price,
            request.SortOrder,
            request.IsVisible);

        await dbContext.SaveChangesAsync(cancellationToken);

        return Ok(ToResponse(item));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(
        Guid id,
        CancellationToken cancellationToken)
    {
        var item = await dbContext
            .Set<MenuItem>()
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);

        if (item is null)
        {
            return NotFound();
        }

        dbContext.Set<MenuItem>().Remove(item);
        await dbContext.SaveChangesAsync(cancellationToken);

        return NoContent();
    }

    private async Task<bool> CategoryExists(
        Guid categoryId,
        CancellationToken cancellationToken)
    {
        return await dbContext
            .Set<MenuCategory>()
            .AnyAsync(category => category.Id == categoryId, cancellationToken);
    }

    private static NotFoundObjectResult CreateCategoryNotFound(Guid categoryId)
    {
        return new NotFoundObjectResult(new ProblemDetails
        {
            Title = "Menu category not found.",
            Detail = $"Menu category '{categoryId}' does not exist.",
            Status = StatusCodes.Status404NotFound
        });
    }

    private static MenuItemResponse ToResponse(MenuItem item)
    {
        return new MenuItemResponse(
            item.Id,
            item.CategoryId,
            item.Name,
            item.Description,
            item.Price,
            item.SortOrder,
            item.IsVisible,
            item.CreatedAtUtc,
            item.UpdatedAtUtc);
    }
}