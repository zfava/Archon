using ArchonAI.Common.Observability;
using ArchonAI.Infrastructure;
using ArchonAI.Runtime;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddSerilog(config => config
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Service", "ArchonAI.Worker.Scheduler")
    .WriteTo.Console());

builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService("ArchonAI.Worker.Scheduler"))
    .WithTracing(t => t.AddSource(Telemetry.ActivitySource.Name))
    .WithMetrics(m => m
        .AddMeter(Telemetry.Meter.Name)
        .AddRuntimeInstrumentation()
        .AddProcessInstrumentation()
        .AddPrometheusHttpListener());

builder.Services.AddArchonAIInfrastructure();
builder.Services.AddArchonAIRuntime();

var host = builder.Build();
host.Run();
