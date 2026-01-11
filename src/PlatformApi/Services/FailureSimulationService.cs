using Docker.DotNet;
using Docker.DotNet.Models;
using PlatformApi.Models;
using System.Collections.Concurrent;

namespace PlatformApi.Services;

public class FailureSimulationService
{
    private readonly DockerClient _dockerClient;
    private readonly ILogger<FailureSimulationService> _logger;
    private readonly ConcurrentDictionary<string, FailureStatus> _activeFailures = new();
    private readonly string _pumbaImage = "gaiaadm/pumba:0.10.0";
    private readonly string _iproute2Image = "gaiadocker/iproute2";
    private readonly string _networkName;

    public FailureSimulationService(ILogger<FailureSimulationService> logger, IConfiguration configuration)
    {
        _logger = logger;
        _networkName = configuration["Docker:NetworkName"] ?? "distributed-system-platform_default";

        var dockerHost = configuration["Docker:Host"] ?? "unix:///var/run/docker.sock";
        _dockerClient = new DockerClientConfiguration(new Uri(dockerHost)).CreateClient();
    }

    public async Task<NetworkDelayResponse> ApplyNetworkDelayAsync(NetworkDelayRequest request)
    {
        var failureId = $"pumba-delay-{Guid.NewGuid():N}";

        try
        {
            await PullImageIfNotExistsAsync(_pumbaImage);
            await PullImageIfNotExistsAsync(_iproute2Image);

            var cmd = new List<string>
            {
                "--log-level", "info",
                "netem",
                "--duration", $"{request.DurationSeconds}s",
                "--tc-image", _iproute2Image,
                "delay",
                "--time", request.DelayMs.ToString()
            };

            if (request.JitterMs > 0)
            {
                cmd.Add("--jitter");
                cmd.Add(request.JitterMs.ToString());
            }

            cmd.Add($"re2:^{request.ContainerName}$");

            var createResponse = await _dockerClient.Containers.CreateContainerAsync(new CreateContainerParameters
            {
                Image = _pumbaImage,
                Name = failureId,
                Cmd = cmd,
                HostConfig = new HostConfig
                {
                    Binds = new[] { "/var/run/docker.sock:/var/run/docker.sock:ro" },
                    NetworkMode = "host",
                    AutoRemove = false,
                    Privileged = true
                },
                Labels = new Dictionary<string, string>
                {
                    ["platform"] = "distributed-system-platform",
                    ["type"] = "pumba-failure",
                    ["failure-type"] = "network-delay"
                }
            });

            await _dockerClient.Containers.StartContainerAsync(createResponse.ID, new ContainerStartParameters());

            var failure = new FailureStatus(
                Id: failureId,
                Type: "network-delay",
                ContainerName: request.ContainerName,
                Status: "active",
                StartedAt: DateTime.UtcNow,
                ExpiresAt: DateTime.UtcNow.AddSeconds(request.DurationSeconds)
            );
            _activeFailures[failureId] = failure;

            _ = MonitorFailureAsync(failureId, createResponse.ID);

            _logger.LogInformation(
                "Applied network delay of {DelayMs}ms (jitter: {JitterMs}ms) to container {Container} for {Duration}s",
                request.DelayMs, request.JitterMs, request.ContainerName, request.DurationSeconds);

            return new NetworkDelayResponse(
                failureId,
                "active",
                $"Network delay of {request.DelayMs}ms applied to {request.ContainerName} for {request.DurationSeconds}s"
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to apply network delay to container {Container}", request.ContainerName);
            return new NetworkDelayResponse(failureId, "failed", ex.Message);
        }
    }

    public async Task<NetworkDelayResponse> StopFailureAsync(string failureId)
    {
        try
        {
            var containers = await _dockerClient.Containers.ListContainersAsync(new ContainersListParameters
            {
                All = true,
                Filters = new Dictionary<string, IDictionary<string, bool>>
                {
                    ["name"] = new Dictionary<string, bool> { [failureId] = true }
                }
            });

            var container = containers.FirstOrDefault();
            if (container == null)
            {
                _activeFailures.TryRemove(failureId, out _);
                return new NetworkDelayResponse(failureId, "not_found", "Failure simulation not found");
            }

            await _dockerClient.Containers.StopContainerAsync(container.ID, new ContainerStopParameters
            {
                WaitBeforeKillSeconds = 2
            });

            await _dockerClient.Containers.RemoveContainerAsync(container.ID, new ContainerRemoveParameters { Force = true });

            if (_activeFailures.TryGetValue(failureId, out var failure))
            {
                _activeFailures[failureId] = failure with { Status = "stopped" };
            }

            _logger.LogInformation("Stopped failure simulation {FailureId}", failureId);
            return new NetworkDelayResponse(failureId, "stopped", "Failure simulation stopped");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to stop failure simulation {FailureId}", failureId);
            return new NetworkDelayResponse(failureId, "error", ex.Message);
        }
    }

    public ActiveFailuresResponse GetActiveFailures()
    {
        CleanupExpiredFailures();
        return new ActiveFailuresResponse(_activeFailures.Values.ToList());
    }

    public async Task<List<string>> GetAvailableContainersAsync()
    {
        var containers = await _dockerClient.Containers.ListContainersAsync(new ContainersListParameters
        {
            Filters = new Dictionary<string, IDictionary<string, bool>>
            {
                ["label"] = new Dictionary<string, bool> { ["platform=distributed-system-platform"] = true },
                ["status"] = new Dictionary<string, bool> { ["running"] = true }
            }
        });

        return containers
            .Where(c => !c.Names.Any(n => n.Contains("pumba") || n.Contains("k6-job")))
            .SelectMany(c => c.Names)
            .Select(n => n.TrimStart('/'))
            .ToList();
    }

    private async Task MonitorFailureAsync(string failureId, string containerId)
    {
        try
        {
            var waitResponse = await _dockerClient.Containers.WaitContainerAsync(containerId);

            if (_activeFailures.TryGetValue(failureId, out var failure))
            {
                var status = waitResponse.StatusCode == 0 ? "completed" : "failed";
                _activeFailures[failureId] = failure with { Status = status };
            }

            try
            {
                await _dockerClient.Containers.RemoveContainerAsync(containerId, new ContainerRemoveParameters());
            }
            catch { }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error monitoring failure {FailureId}", failureId);
        }
    }

    private void CleanupExpiredFailures()
    {
        var now = DateTime.UtcNow;
        var expired = _activeFailures
            .Where(kvp => kvp.Value.ExpiresAt.HasValue && kvp.Value.ExpiresAt < now)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var id in expired)
        {
            if (_activeFailures.TryGetValue(id, out var failure) && failure.Status == "active")
            {
                _activeFailures[id] = failure with { Status = "completed" };
            }
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
}
