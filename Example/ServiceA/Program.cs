using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

string serviceBUrl = builder.Configuration["ServiceB:Url"]!;
string otelEndpoint = builder.Configuration["OpenTelemetry:Endpoint"]!;
builder.Services.AddHttpClient("ServiceB", client => { client.BaseAddress = new(serviceBUrl); });
builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource
        .AddService("service-a"))
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddOtlpExporter(options => { options.Endpoint = new(otelEndpoint); }))
    .WithMetrics(metrics => metrics
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

app.MapPost("/api/error", Results.StatusCode);

app.UseSwagger();
app.UseSwaggerUI();
app.Run();