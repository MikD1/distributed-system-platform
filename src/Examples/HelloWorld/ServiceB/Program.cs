using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using OpenTelemetry.Metrics;

var builder = WebApplication.CreateBuilder(args);

var otelEndpoint = builder.Configuration["OpenTelemetry:Endpoint"] ?? "http://otel-collector:4317";

builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource
        .AddService(
            serviceName: "service-b",
            serviceVersion: "1.0.0")
        .AddAttributes(new Dictionary<string, object>
        {
            ["deployment.environment"] = builder.Environment.EnvironmentName,
            ["platform"] = "distributed-system-platform"
        }))
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation()
        .AddOtlpExporter(options =>
        {
            options.Endpoint = new Uri(otelEndpoint);
        }))
    .WithMetrics(metrics => metrics
        .AddMeter("Microsoft.AspNetCore.Hosting")
        .AddMeter("Microsoft.AspNetCore.Server.Kestrel")
        .AddAspNetCoreInstrumentation()
        .AddRuntimeInstrumentation()
        .AddOtlpExporter((exporterOptions, metricReaderOptions) =>
        {
            exporterOptions.Endpoint = new Uri(otelEndpoint);
            metricReaderOptions.PeriodicExportingMetricReaderOptions.ExportIntervalMilliseconds = 1000;
        }));

var app = builder.Build();

app.MapPost("/api/process", (ProcessRequest request) =>
{
    var result = $"Processed: {request.Data} at {DateTime.UtcNow:O}";
    return Results.Ok(new ProcessResponse(result));
});

app.MapGet("/health", () => Results.Ok("healthy"));

app.Run();

record ProcessRequest(string Data);
record ProcessResponse(string Result);
