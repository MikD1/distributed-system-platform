using System.Collections.Concurrent;
using Docker.DotNet;
using Docker.DotNet.Models;
using PlatformApi.Models;

namespace PlatformApi.Services;

public class FailureSimulationService
{
    public FailureSimulationService(ILogger<FailureSimulationService> logger, IConfiguration configuration)
    {
        _logger = logger;
        string dockerHost = configuration["Docker:Host"] ?? "unix:///var/run/docker.sock";
        _dockerClient = new DockerClientConfiguration(new Uri(dockerHost)).CreateClient();
    }

    public async Task<NetworkDelayResponse> ApplyNetworkDelay(NetworkDelayRequest request)
    {
        string failureId = $"pumba-delay-{Guid.NewGuid():N}";

        try
        {
            await PullImageIfNotExistsAsync(PumbaImage);
            await PullImageIfNotExistsAsync(TcImage);

            List<string> cmd =
            [
                "--log-level", "info",
                "netem",
                "--duration", $"{request.DurationSeconds}s",
                "--tc-image", TcImage,
                "delay",
                "--time", request.DelayMs.ToString()
            ];

            if (request.JitterMs > 0)
            {
                cmd.Add("--jitter");
                cmd.Add(request.JitterMs.ToString()!);
            }

            cmd.Add($"re2:^{request.ContainerName}$");

            CreateContainerResponse? createResponse = await _dockerClient.Containers.CreateContainerAsync(new()
            {
                Image = PumbaImage,
                Name = failureId,
                Cmd = cmd,
                HostConfig = new()
                {
                    Binds = ["/var/run/docker.sock:/var/run/docker.sock:ro"],
                    AutoRemove = true,
                    Privileged = true
                },
                Labels = new Dictionary<string, string>
                {
                    ["platform"] = "distributed-system-platform",
                    ["type"] = "pumba-failure",
                    ["failure-type"] = "network-delay"
                }
            });

            await _dockerClient.Containers.StartContainerAsync(createResponse.ID, new());

            FailureStatus failure = new(
                Id: failureId,
                Type: "network-delay",
                ContainerName: request.ContainerName,
                Status: "active",
                StartedAt: DateTime.UtcNow,
                ExpiresAt: DateTime.UtcNow.AddSeconds(request.DurationSeconds));
            _activeFailures[failureId] = failure;

            _ = MonitorFailureAsync(failureId, createResponse.ID);

            _logger.LogInformation(
                "Applied network delay of {DelayMs}ms (jitter: {JitterMs}ms) to container {Container} for {Duration}s",
                request.DelayMs, request.JitterMs, request.ContainerName, request.DurationSeconds);

            return new(
                failureId,
                "active",
                $"Network delay of {request.DelayMs}ms applied to {request.ContainerName} for {request.DurationSeconds}s");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to apply network delay to container {Container}", request.ContainerName);
            return new(failureId, "failed", ex.Message);
        }
    }

    public async Task<NetworkDelayResponse> StopFailureAsync(string failureId)
    {
        try
        {
            IList<ContainerListResponse>? containers = await _dockerClient.Containers.ListContainersAsync(new()
            {
                All = true,
                Filters = new Dictionary<string, IDictionary<string, bool>>
                {
                    ["name"] = new Dictionary<string, bool> { [failureId] = true }
                }
            });

            ContainerListResponse? container = containers.FirstOrDefault();
            if (container == null)
            {
                _activeFailures.TryRemove(failureId, out _);
                return new(failureId, "not_found", "Failure simulation not found");
            }

            await _dockerClient.Containers.StopContainerAsync(container.ID, new()
            {
                WaitBeforeKillSeconds = 2
            });

            await _dockerClient.Containers.RemoveContainerAsync(container.ID,
                new() { Force = true });

            if (_activeFailures.TryGetValue(failureId, out FailureStatus? failure))
            {
                _activeFailures[failureId] = failure with { Status = "stopped" };
            }

            _logger.LogInformation("Stopped failure simulation {FailureId}", failureId);
            return new(failureId, "stopped", "Failure simulation stopped");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to stop failure simulation {FailureId}", failureId);
            return new(failureId, "error", ex.Message);
        }
    }

    public ActiveFailuresResponse GetActiveFailures()
    {
        CleanupExpiredFailures();
        return new(_activeFailures.Values.ToList());
    }

    public async Task<List<string>> GetAvailableContainersAsync()
    {
        IList<ContainerListResponse>? containers = await _dockerClient.Containers.ListContainersAsync(new()
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
            ContainerWaitResponse? waitResponse = await _dockerClient.Containers.WaitContainerAsync(containerId);

            if (_activeFailures.TryGetValue(failureId, out FailureStatus? failure))
            {
                string status = waitResponse.StatusCode == 0 ? "completed" : "failed";
                _activeFailures[failureId] = failure with { Status = status };
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
            _logger.LogError(ex, "Error monitoring failure {FailureId}", failureId);
        }
    }

    private void CleanupExpiredFailures()
    {
        DateTime now = DateTime.UtcNow;
        List<string> expired = _activeFailures
            .Where(kvp => kvp.Value.ExpiresAt.HasValue && kvp.Value.ExpiresAt < now)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (string id in expired)
        {
            if (_activeFailures.TryGetValue(id, out FailureStatus? failure) && failure.Status == "active")
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

    private const string PumbaImage = "gaiaadm/pumba:latest";
    private const string TcImage = "ghcr.io/alexei-led/pumba-alpine-nettools:latest";
    private readonly DockerClient _dockerClient;
    private readonly ILogger<FailureSimulationService> _logger;
    private readonly ConcurrentDictionary<string, FailureStatus> _activeFailures = new();
}