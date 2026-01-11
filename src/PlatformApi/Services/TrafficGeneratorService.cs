using Docker.DotNet;
using Docker.DotNet.Models;
using PlatformApi.Models;
using System.Collections.Concurrent;

namespace PlatformApi.Services;

public class TrafficGeneratorService
{
    private readonly DockerClient _dockerClient;
    private readonly ILogger<TrafficGeneratorService> _logger;
    private readonly ConcurrentDictionary<string, TrafficJobStatus> _jobs = new();
    private readonly string _k6Image = "grafana/k6:0.54.0";
    private readonly string _networkName;

    public TrafficGeneratorService(ILogger<TrafficGeneratorService> logger, IConfiguration configuration)
    {
        _logger = logger;
        _networkName = configuration["Docker:NetworkName"] ?? "distributed-system-platform_default";

        var dockerHost = configuration["Docker:Host"] ?? "unix:///var/run/docker.sock";
        _dockerClient = new DockerClientConfiguration(new Uri(dockerHost)).CreateClient();
    }

    public async Task<TrafficGenerationResponse> StartTrafficGenerationAsync(TrafficGenerationRequest request)
    {
        var jobId = $"k6-job-{Guid.NewGuid():N}";

        try
        {
            await PullImageIfNotExistsAsync(_k6Image);

            var envVars = new List<string>
            {
                $"TARGET_URL={request.TargetUrl}",
                $"RPS={request.Rps}",
                $"DURATION={request.DurationSeconds}s",
                $"VUS={Math.Min(request.Rps * 2, request.MaxVUs ?? 100)}",
                $"MAX_VUS={request.MaxVUs ?? 100}",
                "K6_PROMETHEUS_RW_SERVER_URL=http://prometheus:9090/api/v1/write"
            };

            var createResponse = await _dockerClient.Containers.CreateContainerAsync(new CreateContainerParameters
            {
                Image = _k6Image,
                Name = jobId,
                Env = envVars,
                Cmd = new[] { "run", "-o", "experimental-prometheus-rw", "/scripts/load-test.js" },
                HostConfig = new HostConfig
                {
                    Binds = new[] { $"{GetScriptsPath()}:/scripts:ro" },
                    NetworkMode = _networkName,
                    AutoRemove = false
                },
                Labels = new Dictionary<string, string>
                {
                    ["platform"] = "distributed-system-platform",
                    ["type"] = "k6-job"
                }
            });

            await _dockerClient.Containers.StartContainerAsync(createResponse.ID, new ContainerStartParameters());

            var job = new TrafficJobStatus(
                JobId: jobId,
                Status: "running",
                StartedAt: DateTime.UtcNow,
                FinishedAt: null,
                Error: null
            );
            _jobs[jobId] = job;

            _ = MonitorJobAsync(jobId, createResponse.ID);

            _logger.LogInformation("Started traffic generation job {JobId} targeting {TargetUrl} at {Rps} RPS for {Duration}s",
                jobId, request.TargetUrl, request.Rps, request.DurationSeconds);

            return new TrafficGenerationResponse(jobId, "started", $"Traffic generation started with {request.Rps} RPS for {request.DurationSeconds}s");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start traffic generation job");
            return new TrafficGenerationResponse(jobId, "failed", ex.Message);
        }
    }

    public async Task<TrafficGenerationResponse> StopTrafficGenerationAsync(string jobId)
    {
        try
        {
            var containers = await _dockerClient.Containers.ListContainersAsync(new ContainersListParameters
            {
                All = true,
                Filters = new Dictionary<string, IDictionary<string, bool>>
                {
                    ["name"] = new Dictionary<string, bool> { [jobId] = true }
                }
            });

            var container = containers.FirstOrDefault();
            if (container == null)
            {
                return new TrafficGenerationResponse(jobId, "not_found", "Job not found");
            }

            await _dockerClient.Containers.StopContainerAsync(container.ID, new ContainerStopParameters
            {
                WaitBeforeKillSeconds = 5
            });

            await _dockerClient.Containers.RemoveContainerAsync(container.ID, new ContainerRemoveParameters { Force = true });

            if (_jobs.TryGetValue(jobId, out var job))
            {
                _jobs[jobId] = job with { Status = "stopped", FinishedAt = DateTime.UtcNow };
            }

            _logger.LogInformation("Stopped traffic generation job {JobId}", jobId);
            return new TrafficGenerationResponse(jobId, "stopped", "Traffic generation stopped");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to stop traffic generation job {JobId}", jobId);
            return new TrafficGenerationResponse(jobId, "error", ex.Message);
        }
    }

    public IEnumerable<TrafficJobStatus> GetAllJobs() => _jobs.Values.ToList();

    public TrafficJobStatus? GetJob(string jobId) => _jobs.TryGetValue(jobId, out var job) ? job : null;

    private async Task MonitorJobAsync(string jobId, string containerId)
    {
        try
        {
            var waitResponse = await _dockerClient.Containers.WaitContainerAsync(containerId);

            if (_jobs.TryGetValue(jobId, out var job))
            {
                var status = waitResponse.StatusCode == 0 ? "completed" : "failed";
                var error = waitResponse.StatusCode != 0 ? $"Exit code: {waitResponse.StatusCode}" : null;
                _jobs[jobId] = job with { Status = status, FinishedAt = DateTime.UtcNow, Error = error };
            }

            try
            {
                await _dockerClient.Containers.RemoveContainerAsync(containerId, new ContainerRemoveParameters());
            }
            catch { }
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
        catch (Docker.DotNet.DockerImageNotFoundException)
        {
            _logger.LogInformation("Pulling image {Image}...", imageName);
            await _dockerClient.Images.CreateImageAsync(
                new ImagesCreateParameters { FromImage = imageName },
                null,
                new Progress<JSONMessage>(m =>
                {
                    if (!string.IsNullOrEmpty(m.Status))
                        _logger.LogDebug("Pull: {Status}", m.Status);
                }));
            _logger.LogInformation("Image {Image} pulled successfully", imageName);
        }
    }

    private string GetScriptsPath()
    {
        var path = Environment.GetEnvironmentVariable("K6_SCRIPTS_PATH");
        return path ?? "/app/k6/scripts";
    }
}
