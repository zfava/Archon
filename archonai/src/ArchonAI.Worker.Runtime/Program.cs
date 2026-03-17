using ArchonAI.Agents;
using ArchonAI.Agents.Finance;
using ArchonAI.Agents.Marketing;
using ArchonAI.Agents.Operations;
using ArchonAI.Agents.Sales;
using ArchonAI.Agents.Support;
using ArchonAI.Common.Observability;
using ArchonAI.Connectors;
using ArchonAI.Infrastructure;
using ArchonAI.Infrastructure.Cluster;
using ArchonAI.Plugins;
using ArchonAI.Runtime;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddSerilog(config => config
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Service", "ArchonAI.Worker.Runtime")
    .WriteTo.Console());

builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService("ArchonAI.Worker.Runtime"))
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
builder.Services.AddArchonAIRuntime();
builder.Services.AddArchonAIOperations(builder.Configuration);
builder.Services.AddArchonAIFinance(builder.Configuration);
builder.Services.AddArchonAISales(builder.Configuration);
builder.Services.AddArchonAIMarketing(builder.Configuration);
builder.Services.AddArchonAISupport(builder.Configuration);

// Cluster: register as runtime worker for automatic workload distribution
builder.Services.Configure<ClusterNodeRegistrationOptions>(opts =>
{
    opts.Role = "runtime";
    opts.Capabilities = new List<string> { "task-execution", "agent-hosting", "workflow-execution" };
});
builder.Services.AddHostedService<ClusterNodeHeartbeatService>();

var host = builder.Build();
host.Run();
