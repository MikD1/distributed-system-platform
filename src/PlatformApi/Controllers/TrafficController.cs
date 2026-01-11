using Microsoft.AspNetCore.Mvc;
using PlatformApi.Models;
using PlatformApi.Services;

namespace PlatformApi.Controllers;

[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class TrafficController : ControllerBase
{
    private readonly TrafficGeneratorService _trafficService;
    private readonly ILogger<TrafficController> _logger;

    public TrafficController(TrafficGeneratorService trafficService, ILogger<TrafficController> logger)
    {
        _trafficService = trafficService;
        _logger = logger;
    }

    /// <summary>
    /// Start traffic generation with specified RPS and duration
    /// </summary>
    /// <param name="request">Traffic generation parameters</param>
    /// <returns>Job information</returns>
    [HttpPost("start")]
    [ProducesResponseType(typeof(TrafficGenerationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(TrafficGenerationResponse), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<TrafficGenerationResponse>> StartTraffic([FromBody] TrafficGenerationRequest request)
    {
        _logger.LogInformation("Starting traffic generation: {TargetUrl} at {Rps} RPS for {Duration}s",
            request.TargetUrl, request.Rps, request.DurationSeconds);

        var response = await _trafficService.StartTrafficGenerationAsync(request);

        if (response.Status == "failed")
        {
            return StatusCode(500, response);
        }

        return Ok(response);
    }

    /// <summary>
    /// Stop a running traffic generation job
    /// </summary>
    /// <param name="jobId">Job ID to stop</param>
    /// <returns>Result of stop operation</returns>
    [HttpPost("stop/{jobId}")]
    [ProducesResponseType(typeof(TrafficGenerationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(TrafficGenerationResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<TrafficGenerationResponse>> StopTraffic(string jobId)
    {
        _logger.LogInformation("Stopping traffic generation job: {JobId}", jobId);

        var response = await _trafficService.StopTrafficGenerationAsync(jobId);

        if (response.Status == "not_found")
        {
            return NotFound(response);
        }

        return Ok(response);
    }

    /// <summary>
    /// Get all traffic generation jobs
    /// </summary>
    /// <returns>List of all jobs</returns>
    [HttpGet("jobs")]
    [ProducesResponseType(typeof(IEnumerable<TrafficJobStatus>), StatusCodes.Status200OK)]
    public ActionResult<IEnumerable<TrafficJobStatus>> GetAllJobs()
    {
        return Ok(_trafficService.GetAllJobs());
    }

    /// <summary>
    /// Get status of a specific job
    /// </summary>
    /// <param name="jobId">Job ID</param>
    /// <returns>Job status</returns>
    [HttpGet("jobs/{jobId}")]
    [ProducesResponseType(typeof(TrafficJobStatus), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<TrafficJobStatus> GetJob(string jobId)
    {
        var job = _trafficService.GetJob(jobId);
        if (job == null)
        {
            return NotFound();
        }
        return Ok(job);
    }
}
