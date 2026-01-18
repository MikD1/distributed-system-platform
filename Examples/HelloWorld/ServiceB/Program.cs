using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

string otelEndpoint = builder.Configuration["OpenTelemetry:Endpoint"]!;
builder.Services.AddOpenTelemetry()
    .ConfigureResource(x => x
        .AddService("service-b"))
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation()
        .AddOtlpExporter(options => { options.Endpoint = new(otelEndpoint); }))
    .WithMetrics(metrics => metrics
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
app.Run();