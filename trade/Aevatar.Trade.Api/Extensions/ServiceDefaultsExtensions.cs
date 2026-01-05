namespace Aevatar.Trade.Api.Extensions;

/// <summary>
/// Aspire ServiceDefaults extensions
/// </summary>
public static class ServiceDefaultsExtensions
{
    /// <summary>
    /// Add Aspire default service configuration
    /// </summary>
    public static IHostApplicationBuilder AddServiceDefaults(this IHostApplicationBuilder builder)
    {
        // OTLP endpoint configuration
        var otlpEndpoint = builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]
            ?? Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT");

        if (!string.IsNullOrEmpty(otlpEndpoint))
        {
            builder.Services.Configure<OpenTelemetry.Exporter.OtlpExporterOptions>(options =>
            {
                options.Endpoint = new Uri(otlpEndpoint);
            });
        }

        // Health checks
        builder.Services.AddHealthChecks();

        return builder;
    }

    /// <summary>
    /// Map default endpoints
    /// </summary>
    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        app.MapHealthChecks("/health");
        app.MapHealthChecks("/alive");

        return app;
    }
}
