using Microsoft.AspNetCore.Mvc;
using PlatformApi.Models;
using PlatformApi.Services;

namespace PlatformApi.Controllers;

[ApiController]
[Route("api/traffic")]
public class TrafficController(TrafficGeneratorService trafficService, ILogger<TrafficController> logger)
    : ControllerBase
{
    [HttpPost("start")]
    public async Task<ActionResult<TrafficGenerationResponse>> StartTraffic([FromBody] TrafficGenerationRequest request)
    {
        logger.LogInformation("Starting traffic generation: {TargetUrl} at {Rps} RPS for {Duration}s",
            request.TargetUrl, request.Rps, request.DurationSeconds);

        TrafficGenerationResponse response = await trafficService.StartTrafficGenerationAsync(request);
        return response.Status == "failed" ? StatusCode(500, response) : Ok(response);
    }

    [HttpPost("stop/{jobId}")]
    public async Task<ActionResult<TrafficGenerationResponse>> StopTraffic(string jobId)
    {
        logger.LogInformation("Stopping traffic generation job: {JobId}", jobId);

        TrafficGenerationResponse response = await trafficService.StopTrafficGenerationAsync(jobId);
        if (response.Status == "not_found")
        {
            return NotFound(response);
        }

        return Ok(response);
    }

    [HttpGet("jobs")]
    public ActionResult<IEnumerable<TrafficJobStatus>> GetAllJobs()
    {
        return Ok(trafficService.GetAllJobs());
    }

    [HttpGet("jobs/{jobId}")]
    public ActionResult<TrafficJobStatus> GetJob(string jobId)
    {
        TrafficJobStatus? job = trafficService.GetJob(jobId);
        if (job == null)
        {
            return NotFound();
        }

        return Ok(job);
    }
}