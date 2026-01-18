using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

string serviceBUrl = builder.Configuration["ServiceB:Url"] ?? "http://service-b";
string otelEndpoint = builder.Configuration["OpenTelemetry:Endpoint"] ?? "http://otel-collector:4317";
builder.Services.AddHttpClient("ServiceB", client => { client.BaseAddress = new(serviceBUrl); });
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
        .AddOtlpExporter(options => { options.Endpoint = new(otelEndpoint); }))
    .WithMetrics(metrics => metrics
        .AddMeter("Microsoft.AspNetCore.Hosting")
        .AddMeter("Microsoft.AspNetCore.Server.Kestrel")
        .AddMeter("System.Net.Http")
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddRuntimeInstrumentation()
        .AddOtlpExporter((exporterOptions, metricReaderOptions) =>
        {
            exporterOptions.Endpoint = new(otelEndpoint);
            metricReaderOptions.PeriodicExportingMetricReaderOptions.ExportIntervalMilliseconds = 1000;
        }));

WebApplication app = builder.Build();

app.MapPost("/api/message-a", async (IHttpClientFactory httpClientFactory) =>
{
    HttpClient client = httpClientFactory.CreateClient("ServiceB");
    HttpResponseMessage response = await client.PostAsync("/api/message-b", null);
    response.EnsureSuccessStatusCode();
    return Results.Ok();
});

app.UseSwagger();
app.UseSwaggerUI();
app.Run();