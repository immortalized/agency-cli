using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using __PROJECT_NAMESPACE__.Infrastructure.Persistence;

namespace __PROJECT_NAMESPACE__.Api.Controllers;

[ApiController]
[Route("api/health")]
public sealed class HealthController(AppDbContext dbContext) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        var databaseAvailable = await dbContext.Database.CanConnectAsync(cancellationToken);

        if (!databaseAvailable)
        {
            return StatusCode(
                StatusCodes.Status503ServiceUnavailable,
                new
                {
                    status = "unhealthy",
                    database = "unavailable",
                    timestamp = DateTimeOffset.UtcNow
                });
        }

        return Ok(new
        {
            status = "healthy",
            database = "available",
            timestamp = DateTimeOffset.UtcNow
        });
    }
}