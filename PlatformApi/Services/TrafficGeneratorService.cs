using System.Collections.Concurrent;
using Docker.DotNet;
using Docker.DotNet.Models;
using PlatformApi.Models;

namespace PlatformApi.Services;

public class TrafficGeneratorService
{
    public TrafficGeneratorService(ILogger<TrafficGeneratorService> logger, IConfiguration configuration)
    {
        _logger = logger;
        _networkName = configuration["Docker:NetworkName"] ?? "distributed-system-platform_default";

        string dockerHost = configuration["Docker:Host"] ?? "unix:///var/run/docker.sock";
        _dockerClient = new DockerClientConfiguration(new Uri(dockerHost)).CreateClient();
    }

    public async Task<TrafficGenerationResponse> StartTrafficGenerationAsync(TrafficGenerationRequest request)
    {
        string jobId = $"k6-job-{Guid.NewGuid():N}";

        try
        {
            await PullImageIfNotExistsAsync(K6Image);

            List<string> envVars =
            [
                $"TARGET_URL={request.TargetUrl}",
                $"RPS={request.Rps}",
                $"DURATION={request.DurationSeconds}s",
                $"VUS={Math.Min(request.Rps * 2, request.MaxVUs ?? 100)}",
                $"MAX_VUS={request.MaxVUs ?? 100}",
                "K6_PROMETHEUS_RW_SERVER_URL=http://prometheus:9090/api/v1/write"
            ];

            CreateContainerResponse? createResponse = await _dockerClient.Containers.CreateContainerAsync(new()
            {
                Image = K6Image,
                Name = jobId,
                Env = envVars,
                Cmd = ["run", "-o", "experimental-prometheus-rw", "/scripts/load-test.js"],
                HostConfig = new()
                {
                    Binds = [$"{GetScriptsPath()}:/scripts:ro"],
                    NetworkMode = _networkName,
                    AutoRemove = false
                },
                Labels = new Dictionary<string, string>
                {
                    ["platform"] = "distributed-system-platform",
                    ["type"] = "k6-job"
                }
            });

            await _dockerClient.Containers.StartContainerAsync(createResponse.ID, new());

            TrafficJobStatus job = new(
                JobId: jobId,
                Status: "running",
                StartedAt: DateTime.UtcNow,
                FinishedAt: null,
                Error: null);
            _jobs[jobId] = job;

            _ = MonitorJobAsync(jobId, createResponse.ID);

            _logger.LogInformation(
                "Started traffic generation job {JobId} targeting {TargetUrl} at {Rps} RPS for {Duration}s",
                jobId, request.TargetUrl, request.Rps, request.DurationSeconds);

            return new(jobId, "started",
                $"Traffic generation started with {request.Rps} RPS for {request.DurationSeconds}s");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start traffic generation job");
            return new(jobId, "failed", ex.Message);
        }
    }

    public async Task<TrafficGenerationResponse> StopTrafficGenerationAsync(string jobId)
    {
        try
        {
            IList<ContainerListResponse>? containers = await _dockerClient.Containers.ListContainersAsync(new()
            {
                All = true,
                Filters = new Dictionary<string, IDictionary<string, bool>>
                {
                    ["name"] = new Dictionary<string, bool> { [jobId] = true }
                }
            });

            ContainerListResponse? container = containers.FirstOrDefault();
            if (container == null)
            {
                return new(jobId, "not_found", "Job not found");
            }

            await _dockerClient.Containers.StopContainerAsync(container.ID, new()
            {
                WaitBeforeKillSeconds = 5
            });

            await _dockerClient.Containers.RemoveContainerAsync(container.ID, new() { Force = true });

            if (_jobs.TryGetValue(jobId, out TrafficJobStatus? job))
            {
                _jobs[jobId] = job with { Status = "stopped", FinishedAt = DateTime.UtcNow };
            }

            _logger.LogInformation("Stopped traffic generation job {JobId}", jobId);
            return new(jobId, "stopped", "Traffic generation stopped");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to stop traffic generation job {JobId}", jobId);
            return new(jobId, "error", ex.Message);
        }
    }

    public IEnumerable<TrafficJobStatus> GetAllJobs() => _jobs.Values.ToList();

    public TrafficJobStatus? GetJob(string jobId) => _jobs.TryGetValue(jobId, out TrafficJobStatus? job) ? job : null;

    private async Task MonitorJobAsync(string jobId, string containerId)
    {
        try
        {
            ContainerWaitResponse? waitResponse = await _dockerClient.Containers.WaitContainerAsync(containerId);

            if (_jobs.TryGetValue(jobId, out TrafficJobStatus? job))
            {
                string status = waitResponse.StatusCode == 0 ? "completed" : "failed";
                string? error = waitResponse.StatusCode != 0 ? $"Exit code: {waitResponse.StatusCode}" : null;
                _jobs[jobId] = job with { Status = status, FinishedAt = DateTime.UtcNow, Error = error };
            }

            try
            {
                await _dockerClient.Containers.RemoveContainerAsync(containerId, new());
            }
            catch
            {
                // ignored
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error monitoring job {JobId}", jobId);
        }
    }

    private async Task PullImageIfNotExistsAsync(string imageName)
    {
        try
        {
            await _dockerClient.Images.InspectImageAsync(imageName);
            _logger.LogDebug("Image {Image} already exists", imageName);
        }
        catch (DockerImageNotFoundException)
        {
            _logger.LogInformation("Pulling image {Image}...", imageName);
            await _dockerClient.Images.CreateImageAsync(
                new() { FromImage = imageName },
                null,
                new Progress<JSONMessage>(m =>
                {
                    if (!string.IsNullOrEmpty(m.Status))
                    {
                        _logger.LogDebug("Pull: {Status}", m.Status);
                    }
                }));
            _logger.LogInformation("Image {Image} pulled successfully", imageName);
        }
    }

    private string GetScriptsPath()
    {
        string? path = Environment.GetEnvironmentVariable("K6_SCRIPTS_PATH");
        return path ?? "/app/k6/scripts";
    }

    private const string K6Image = "grafana/k6:0.54.0";
    private readonly DockerClient _dockerClient;
    private readonly ILogger<TrafficGeneratorService> _logger;
    private readonly ConcurrentDictionary<string, TrafficJobStatus> _jobs = new();
    private readonly string _networkName;
}