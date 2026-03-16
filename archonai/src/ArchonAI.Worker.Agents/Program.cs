using ArchonAI.Agents;
using ArchonAI.Agents.Finance;
using ArchonAI.Agents.Marketing;
using ArchonAI.Agents.Operations;
using ArchonAI.Agents.Sales;
using ArchonAI.Agents.Support;
using ArchonAI.Common.Observability;
using ArchonAI.Connectors;
using ArchonAI.Infrastructure;
using ArchonAI.Plugins;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddSerilog(config => config
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Service", "ArchonAI.Worker.Agents")
    .WriteTo.Console());

builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService("ArchonAI.Worker.Agents"))
    .WithTracing(t => t.AddSource(Telemetry.ActivitySource.Name))
    .WithMetrics(m => m
        .AddMeter(Telemetry.Meter.Name)
        .AddRuntimeInstrumentation()
        .AddProcessInstrumentation()
        .AddPrometheusHttpListener());

builder.Services.AddArchonAIInfrastructure();
builder.Services.AddArchonAIConnectors(builder.Configuration);
builder.Services.AddArchonAIAgentTooling();
builder.Services.AddArchonAIPlugins(builder.Configuration);
builder.Services.AddArchonAIOperations(builder.Configuration);
builder.Services.AddArchonAIFinance(builder.Configuration);
builder.Services.AddArchonAISales(builder.Configuration);
builder.Services.AddArchonAIMarketing(builder.Configuration);
builder.Services.AddArchonAISupport(builder.Configuration);

var host = builder.Build();
host.Run();
