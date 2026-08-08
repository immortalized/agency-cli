using Microsoft.AspNetCore.Mvc;

namespace __PROJECT_NAMESPACE__.Api.Controllers;

[ApiController]
[Route("api/health")]
public sealed class HealthController : ControllerBase
{
    [HttpGet]
    public IActionResult Get() => Ok(new
    {
        status = "healthy",
        timestamp = DateTimeOffset.UtcNow
    });
}
