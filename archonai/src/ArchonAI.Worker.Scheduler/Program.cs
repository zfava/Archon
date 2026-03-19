using ArchonAI.Common.Observability;
using ArchonAI.Infrastructure;
using ArchonAI.Infrastructure.Cluster;
using ArchonAI.Infrastructure.Health;
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

// Cluster-aware scheduling: register this node and enable rebalancing
builder.Services.Configure<ClusterNodeRegistrationOptions>(opts =>
{
    opts.Role = "scheduler";
    opts.Capabilities = new List<string> { "scheduling", "task-distribution", "workflow-coordination" };
});
builder.Services.AddHostedService<ClusterNodeHeartbeatService>();
builder.Services.AddHostedService<ClusterRebalanceService>();

// Health: /healthz/live and /healthz/ready on port 8081
builder.Services.AddWorkerHealthEndpoint("scheduler");

var host = builder.Build();
host.Run();
