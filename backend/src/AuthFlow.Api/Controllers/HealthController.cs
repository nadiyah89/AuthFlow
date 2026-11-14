using Microsoft.AspNetCore.Mvc;

namespace AuthFlow.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class HealthController : ControllerBase
{
    private readonly IWebHostEnvironment _environment;

    public HealthController(IWebHostEnvironment environment)
    {
        _environment = environment;
    }

    [HttpGet]
    public IActionResult Get()
    {
        return Ok(new
        {
            status = "Healthy",
            service = "AuthFlow.Api",
            environment = _environment.EnvironmentName,
            timestampUtc = DateTime.UtcNow
        });
    }
}
