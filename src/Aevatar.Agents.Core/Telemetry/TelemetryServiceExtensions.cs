using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Aevatar.Agents.Core.Telemetry;

// ============================================================
//  OpenTelemetry Service Extensions
//  Complete Aspire Dashboard Integration
// ============================================================

public static class TelemetryServiceExtensions
{
    /// <summary>
    /// Add complete Aevatar OpenTelemetry integration.
    /// Includes: Agent, LLM, Workflow telemetry sources.
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <param name="serviceName">Service name</param>
    /// <param name="serviceVersion">Service version</param>
    /// <param name="configureTracing">Tracing configuration callback</param>
    /// <param name="configureMetrics">Metrics configuration callback</param>
    public static IServiceCollection AddAevatarTelemetry(
        this IServiceCollection services,
        string serviceName = "aevatar-agents",
        string? serviceVersion = null,
        Action<TracerProviderBuilder>? configureTracing = null,
        Action<MeterProviderBuilder>? configureMetrics = null)
    {
        var version = serviceVersion
            ?? typeof(TelemetryServiceExtensions).Assembly.GetName().Version?.ToString()
            ?? "1.0.0";

        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource
                .AddService(
                    serviceName: serviceName,
                    serviceVersion: version,
                    serviceInstanceId: Environment.MachineName)
                .AddAttributes(new[]
                {
                    new KeyValuePair<string, object>("deployment.environment",
                        Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production"),
                    new KeyValuePair<string, object>("host.name", Environment.MachineName),
                    new KeyValuePair<string, object>("process.runtime.name", ".NET"),
                    new KeyValuePair<string, object>("process.runtime.version",
                        Environment.Version.ToString())
                }))
            .WithTracing(builder =>
            {
                // ─────────────────────────────────────────────
                //  Agent Telemetry Sources
                // ─────────────────────────────────────────────
                builder.AddSource(AgentTelemetry.SourceName);

                // ─────────────────────────────────────────────
                //  LLM Telemetry Sources
                // ─────────────────────────────────────────────
                builder.AddSource(LLMTelemetry.SourceName);

                // ─────────────────────────────────────────────
                //  Workflow Telemetry Sources
                // ─────────────────────────────────────────────
                builder.AddSource(WorkflowTelemetry.SourceName);

                // ─────────────────────────────────────────────
                //  Legacy Sources (backward compatibility)
                // ─────────────────────────────────────────────
                builder.AddSource(AevatarActivitySource.SourceName);

                // ─────────────────────────────────────────────
                //  Built-in Instrumentation
                // ─────────────────────────────────────────────
                builder.AddHttpClientInstrumentation(options =>
                {
                    // Filter out health checks and other noise
                    options.FilterHttpRequestMessage = request =>
                        !request.RequestUri?.PathAndQuery.Contains("/health") ?? true;
                });

                // Custom configuration
                configureTracing?.Invoke(builder);
            })
            .WithMetrics(builder =>
            {
                // ─────────────────────────────────────────────
                //  Agent Metrics
                // ─────────────────────────────────────────────
                builder.AddMeter(AgentTelemetry.MeterName);

                // ─────────────────────────────────────────────
                //  LLM Metrics
                // ─────────────────────────────────────────────
                builder.AddMeter(LLMTelemetry.MeterName);

                // ─────────────────────────────────────────────
                //  Workflow Metrics
                // ─────────────────────────────────────────────
                builder.AddMeter(WorkflowTelemetry.MeterName);

                // ─────────────────────────────────────────────
                //  Legacy Meters (backward compatibility)
                // ─────────────────────────────────────────────
                builder.AddMeter(AevatarMetrics.MeterName);

                // ─────────────────────────────────────────────
                //  Built-in Instrumentation
                // ─────────────────────────────────────────────
                builder.AddRuntimeInstrumentation();
                builder.AddHttpClientInstrumentation();

                // Custom configuration
                configureMetrics?.Invoke(builder);
            });

        return services;
    }

    /// <summary>
    /// Add console exporter (development environment).
    /// </summary>
    public static IServiceCollection AddAevatarTelemetryConsoleExporter(
        this IServiceCollection services,
        string serviceName = "aevatar-agents")
    {
        return services.AddAevatarTelemetry(
            serviceName,
            configureTracing: builder => builder.AddConsoleExporter(),
            configureMetrics: builder => builder.AddConsoleExporter());
    }

    /// <summary>
    /// Add OTLP exporter (production / Aspire Dashboard).
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <param name="serviceName">Service name</param>
    /// <param name="otlpEndpoint">OTLP Collector endpoint</param>
    public static IServiceCollection AddAevatarTelemetryOtlpExporter(
        this IServiceCollection services,
        string serviceName = "aevatar-agents",
        string? otlpEndpoint = null)
    {
        // Aspire uses OTEL_EXPORTER_OTLP_ENDPOINT environment variable by default
        var endpoint = otlpEndpoint
            ?? Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT")
            ?? "http://localhost:4317";

        return services.AddAevatarTelemetry(
            serviceName,
            configureTracing: builder =>
            {
                builder.AddOtlpExporter(options =>
                {
                    options.Endpoint = new Uri(endpoint);
                });
            },
            configureMetrics: builder =>
            {
                builder.AddOtlpExporter(options =>
                {
                    options.Endpoint = new Uri(endpoint);
                });
            });
    }

    /// <summary>
    /// Add Aspire integration (auto-detect environment variables).
    /// 
    /// Aspire Dashboard automatically sets the following environment variables:
    /// - OTEL_EXPORTER_OTLP_ENDPOINT
    /// - OTEL_SERVICE_NAME
    /// </summary>
    public static IServiceCollection AddAevatarAspireTelemetry(
        this IServiceCollection services,
        string? serviceName = null)
    {
        var resolvedServiceName = serviceName
            ?? Environment.GetEnvironmentVariable("OTEL_SERVICE_NAME")
            ?? "aevatar-agents";

        var otlpEndpoint = Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT");

        if (string.IsNullOrEmpty(otlpEndpoint))
        {
            // Development environment: use console
            return services.AddAevatarTelemetryConsoleExporter(resolvedServiceName);
        }

        // Aspire environment: use OTLP
        return services.AddAevatarTelemetryOtlpExporter(resolvedServiceName, otlpEndpoint);
    }

    /// <summary>
    /// Configure structured logging for Aspire Dashboard support.
    /// </summary>
    public static ILoggingBuilder AddAevatarStructuredLogging(
        this ILoggingBuilder builder)
    {
        // Add OpenTelemetry logging
        builder.AddOpenTelemetry(options =>
        {
            options.IncludeScopes = true;
            options.IncludeFormattedMessage = true;
            options.ParseStateValues = true;
        });

        return builder;
    }
}

// ============================================================
//  Aspire-Friendly Configuration Extensions
// ============================================================

public static class AspireIntegrationExtensions
{
    /// <summary>
    /// One-stop Aspire integration.
    /// Auto-configure Telemetry + Logging.
    /// </summary>
    public static IServiceCollection AddAevatarAspireIntegration(
        this IServiceCollection services,
        string? serviceName = null)
    {
        services.AddAevatarAspireTelemetry(serviceName);

        // Configure logging
        services.AddLogging(logging =>
        {
            logging.AddAevatarStructuredLogging();
        });

        return services;
    }
}
