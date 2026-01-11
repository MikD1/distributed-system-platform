using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using OpenTelemetry.Metrics;

var builder = WebApplication.CreateBuilder(args);

var serviceBUrl = builder.Configuration["ServiceB:Url"] ?? "http://service-b:8080";
var otelEndpoint = builder.Configuration["OpenTelemetry:Endpoint"] ?? "http://otel-collector:4317";

builder.Services.AddHttpClient("ServiceB", client =>
{
    client.BaseAddress = new Uri(serviceBUrl);
    client.Timeout = TimeSpan.FromSeconds(30);
});

builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource
        .AddService(
            serviceName: "service-a",
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
        .AddMeter("Microsoft.AspNetCore.Hosting")
        .AddMeter("Microsoft.AspNetCore.Server.Kestrel")
        .AddMeter("System.Net.Http")
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddRuntimeInstrumentation()
        .AddOtlpExporter((exporterOptions, metricReaderOptions) =>
        {
            exporterOptions.Endpoint = new Uri(otelEndpoint);
            metricReaderOptions.PeriodicExportingMetricReaderOptions.ExportIntervalMilliseconds = 1000;
        }));

var app = builder.Build();

app.MapPost("/api/hello", async (HelloRequest request, IHttpClientFactory httpClientFactory) =>
{
    var client = httpClientFactory.CreateClient("ServiceB");

    var response = await client.PostAsJsonAsync("/api/process", new ProcessRequest(request.Message));
    response.EnsureSuccessStatusCode();

    var result = await response.Content.ReadFromJsonAsync<ProcessResponse>();

    return Results.Ok(new HelloResponse($"ServiceA received: {request.Message}, ServiceB responded: {result?.Result}"));
});

app.MapGet("/health", () => Results.Ok("healthy"));

app.Run();

record HelloRequest(string Message);
record HelloResponse(string Result);
record ProcessRequest(string Data);
record ProcessResponse(string Result);
