namespace PlatformApi.Models;

public record NetworkDelayRequest(
    string ContainerName,
    int DelayMs,
    int DurationSeconds,
    int? JitterMs = 0
);

public record NetworkDelayResponse(
    string Id,
    string Status,
    string Message
);

public record FailureStatus(
    string Id,
    string Type,
    string ContainerName,
    string Status,
    DateTime StartedAt,
    DateTime? ExpiresAt
);

public record ActiveFailuresResponse(
    List<FailureStatus> Failures
);
