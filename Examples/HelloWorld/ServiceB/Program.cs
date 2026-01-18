using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

string otelEndpoint = builder.Configuration["OpenTelemetry:Endpoint"] ?? "http://otel-collector:4317";
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
        .AddOtlpExporter(options => { options.Endpoint = new(otelEndpoint); }))
    .WithMetrics(metrics => metrics
        .AddMeter("Microsoft.AspNetCore.Hosting")
        .AddMeter("Microsoft.AspNetCore.Server.Kestrel")
        .AddAspNetCoreInstrumentation()
        .AddRuntimeInstrumentation()
        .AddOtlpExporter((exporterOptions, metricReaderOptions) =>
        {
            exporterOptions.Endpoint = new(otelEndpoint);
            metricReaderOptions.PeriodicExportingMetricReaderOptions.ExportIntervalMilliseconds = 1000;
        }));

WebApplication app = builder.Build();
app.UseSwagger();
app.UseSwaggerUI();

app.MapPost("/api/message-b", () => Results.Ok());
app.MapGet("/health", () => Results.Ok("healthy"));
app.Run();