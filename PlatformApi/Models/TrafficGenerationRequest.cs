namespace PlatformApi.Models;

public record TrafficGenerationRequest(
    string TargetUrl,
    int Rps,
    int DurationSeconds,
    int? MaxVUs = 100);

public record TrafficGenerationResponse(
    string JobId,
    string Status,
    string Message);

public record TrafficJobStatus(
    string JobId,
    string Status,
    DateTime StartedAt,
    DateTime? FinishedAt,
    string? Error);
