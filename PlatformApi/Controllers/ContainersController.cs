using Microsoft.AspNetCore.Mvc;
using PlatformApi.Services;

namespace PlatformApi.Controllers;

[ApiController]
[Route("api/containers")]
public class ContainersController(FailureSimulationService failureService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<string>>> GetAvailableContainers()
    {
        List<string> containers = await failureService.GetAvailableContainersAsync();
        return Ok(containers);
    }
}