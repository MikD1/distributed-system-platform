using Microsoft.AspNetCore.Mvc;
using PlatformApi.Models;
using PlatformApi.Services;

namespace PlatformApi.Controllers;

[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class FailuresController : ControllerBase
{
    private readonly FailureSimulationService _failureService;
    private readonly ILogger<FailuresController> _logger;

    public FailuresController(FailureSimulationService failureService, ILogger<FailuresController> logger)
    {
        _failureService = failureService;
        _logger = logger;
    }

    /// <summary>
    /// Apply network delay to a container
    /// </summary>
    /// <param name="request">Network delay parameters</param>
    /// <returns>Failure simulation information</returns>
    [HttpPost("network/delay")]
    [ProducesResponseType(typeof(NetworkDelayResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(NetworkDelayResponse), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<NetworkDelayResponse>> ApplyNetworkDelay([FromBody] NetworkDelayRequest request)
    {
        _logger.LogInformation("Applying network delay of {DelayMs}ms to container {Container} for {Duration}s",
            request.DelayMs, request.ContainerName, request.DurationSeconds);

        var response = await _failureService.ApplyNetworkDelayAsync(request);

        if (response.Status == "failed")
        {
            return StatusCode(500, response);
        }

        return Ok(response);
    }

    /// <summary>
    /// Stop a failure simulation
    /// </summary>
    /// <param name="failureId">Failure ID to stop</param>
    /// <returns>Result of stop operation</returns>
    [HttpPost("stop/{failureId}")]
    [ProducesResponseType(typeof(NetworkDelayResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(NetworkDelayResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<NetworkDelayResponse>> StopFailure(string failureId)
    {
        _logger.LogInformation("Stopping failure simulation: {FailureId}", failureId);

        var response = await _failureService.StopFailureAsync(failureId);

        if (response.Status == "not_found")
        {
            return NotFound(response);
        }

        return Ok(response);
    }

    /// <summary>
    /// Get all active failure simulations
    /// </summary>
    /// <returns>List of active failures</returns>
    [HttpGet("active")]
    [ProducesResponseType(typeof(ActiveFailuresResponse), StatusCodes.Status200OK)]
    public ActionResult<ActiveFailuresResponse> GetActiveFailures()
    {
        return Ok(_failureService.GetActiveFailures());
    }

    /// <summary>
    /// Get list of available containers for failure injection
    /// </summary>
    /// <returns>List of container names</returns>
    [HttpGet("containers")]
    [ProducesResponseType(typeof(List<string>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<string>>> GetAvailableContainers()
    {
        var containers = await _failureService.GetAvailableContainersAsync();
        return Ok(containers);
    }
}
