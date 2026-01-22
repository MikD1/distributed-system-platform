using System.Diagnostics;
using System.Diagnostics.Metrics;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

string serviceBUrl = builder.Configuration["ServiceB:Url"]!;
string otelEndpoint = builder.Configuration["OpenTelemetry:Endpoint"]!;

builder.Services.AddHttpClient("ServiceB", client =>
{
    client.BaseAddress = new(serviceBUrl);
    client.Timeout = TimeSpan.FromSeconds(1);
});

Meter meter = new("DeliveryGuarantees.ServiceA", "1.0.0");
Counter<long> messagesSentCounter = meter.CreateCounter<long>("delivery_messages_sent_total");

builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource
        .AddService("delivery-sender"))
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddOtlpExporter(options => { options.Endpoint = new(otelEndpoint); }))
    .WithMetrics(metrics => metrics
        .AddMeter("DeliveryGuarantees.ServiceA")
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddRuntimeInstrumentation()
        .AddOtlpExporter((exporterOptions, metricReaderOptions) =>
        {
            exporterOptions.Endpoint = new(otelEndpoint);
            metricReaderOptions.PeriodicExportingMetricReaderOptions.ExportIntervalMilliseconds = 1000;
        }));

WebApplication app = builder.Build();

app.MapPost("/api/message", async (IHttpClientFactory httpClientFactory) =>
{
    string messageId = Guid.NewGuid().ToString();
    messagesSentCounter.Add(1, new TagList { { "message_id", messageId } });

    HttpClient client = httpClientFactory.CreateClient("ServiceB");
    await client.PostAsJsonAsync("/api/message", new { MessageId = messageId });
    return Results.Ok();
});

app.UseSwagger();
app.UseSwaggerUI();
app.Run();