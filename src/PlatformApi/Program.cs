using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using PlatformApi.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "Distributed System Platform API",
        Version = "v1",
        Description = "API for managing distributed system experiments: traffic generation and failure simulation"
    });
});

builder.Services.AddSingleton<TrafficGeneratorService>();
builder.Services.AddSingleton<FailureSimulationService>();

var otelEndpoint = builder.Configuration["OpenTelemetry:Endpoint"] ?? "http://otel-collector:4317";

builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource
        .AddService(
            serviceName: "platform-api",
            serviceVersion: "1.0.0")
        .AddAttributes(new Dictionary<string, object>
        {
            ["deployment.environment"] = builder.Environment.EnvironmentName,
            ["platform"] = "distributed-system-platform"
        }))
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddOtlpExporter(options =>
        {
            options.Endpoint = new Uri(otelEndpoint);
        }))
    .WithMetrics(metrics => metrics
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddRuntimeInstrumentation()
        .AddPrometheusExporter()
        .AddOtlpExporter((exporterOptions, metricReaderOptions) =>
        {
            exporterOptions.Endpoint = new Uri(otelEndpoint);
            metricReaderOptions.PeriodicExportingMetricReaderOptions.ExportIntervalMilliseconds = 1000;
        }));

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
            .AllowAnyMethod()
            .AllowAnyHeader();
    });
});

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "Platform API v1");
    options.RoutePrefix = string.Empty;
});

app.UseCors();
app.MapControllers();
app.MapPrometheusScrapingEndpoint();

app.Run();
