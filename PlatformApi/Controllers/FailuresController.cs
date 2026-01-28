using Microsoft.AspNetCore.Mvc;
using PlatformApi.Models;
using PlatformApi.Services;

namespace PlatformApi.Controllers;

[ApiController]
[Route("api/failures")]
public class FailuresController(FailureSimulationService failureService, ILogger<FailuresController> logger)
    : ControllerBase
{
    [HttpPost("network/delay")]
    public async Task<ActionResult<NetworkDelayResponse>> ApplyNetworkDelay([FromBody] NetworkDelayRequest request)
    {
        logger.LogInformation("Applying network delay of {DelayMs}ms to container {Container} for {Duration}s",
            request.DelayMs, request.ContainerName, request.DurationSeconds);
        NetworkDelayResponse response = await failureService.ApplyNetworkDelay(request);
        return response.Status == "failed" ? StatusCode(500, response) : Ok(response);
    }

    [HttpPost("stop/{jobId}")]
    public async Task<ActionResult<NetworkDelayResponse>> StopFailure(string jobId)
    {
        logger.LogInformation("Stopping failure simulation: {FailureId}", jobId);
        NetworkDelayResponse response = await failureService.StopFailureAsync(jobId);
        if (response.Status == "not_found")
        {
            return NotFound(response);
        }

        return Ok(response);
    }

    [HttpGet("jobs")]
    [ProducesResponseType(typeof(ActiveFailuresResponse), StatusCodes.Status200OK)]
    public ActionResult<ActiveFailuresResponse> GetFailureJobs()
    {
        return Ok(failureService.GetActiveFailures());
    }
}