using ArchonAI.Common.Observability;
using ArchonAI.Core.Interfaces;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;

namespace ArchonAI.Api.Security;

public static class ObservabilityServiceCollectionExtensions
{
    public static WebApplicationBuilder AddArchonAIObservability(this WebApplicationBuilder builder)
    {
        builder.Host.UseSerilog((context, _, configuration) =>
        {
            configuration
                .ReadFrom.Configuration(context.Configuration)
                .Enrich.FromLogContext()
                .Enrich.WithProperty("Service", global::ArchonAI.Common.Observability.Telemetry.ServiceName)
                .WriteTo.Console();
        });

        builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(global::ArchonAI.Common.Observability.Telemetry.ServiceName))
            .WithTracing(tracing => tracing
                .AddSource(global::ArchonAI.Common.Observability.Telemetry.ActivitySource.Name)
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation())
            .WithMetrics(metrics => metrics
                .AddMeter(global::ArchonAI.Common.Observability.Telemetry.Meter.Name)
                .AddAspNetCoreInstrumentation()
                .AddRuntimeInstrumentation()
                .AddProcessInstrumentation()
                .AddPrometheusExporter());

        builder.Services.AddSingleton<IObservabilityService, ObservabilityService>();
        builder.Services.AddSingleton<IAuditLogService, AuditLogService>();
        builder.Services.AddSingleton<TracingAgentExecutor>();

        return builder;
    }
}
