using Microsoft.Extensions.Configuration;
using OpenTelemetry.Exporter;

namespace BuildingBlocks.Telemetry.Internal.Exporters;

/// <summary>
/// OTLP enablement, fast-path detection, and per-signal option application.
/// </summary>
internal static class OtlpExporterRegistration
{
    /// <summary>
    /// Returns <c>true</c> when OTLP should be registered (options flag, explicit endpoint, or env).
    /// </summary>
    public static bool ShouldUseOtlp(TelemetryOptions options, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(configuration);

        return options.Exporters.Otlp.Enabled
            || !string.IsNullOrWhiteSpace(options.Exporters.Otlp.Endpoint)
            || !string.IsNullOrWhiteSpace(configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]);
    }

    /// <summary>
    /// Prefer <c>UseOtlpExporter()</c> when OTLP is env-driven and no Console / explicit Endpoint/Headers override.
    /// </summary>
    public static bool CanUseOtlpExporterFastPath(TelemetryOptions options, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(configuration);

        if (!ShouldUseOtlp(options, configuration))
        {
            return false;
        }

        if (options.Exporters.Console.Enabled)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(options.Exporters.Otlp.Endpoint)
            || !string.IsNullOrWhiteSpace(options.Exporters.Otlp.Headers))
        {
            return false;
        }

        return !string.IsNullOrWhiteSpace(configuration["OTEL_EXPORTER_OTLP_ENDPOINT"])
            || options.Exporters.Otlp.Enabled;
    }

    /// <summary>
    /// Applies BuildingBlocks OTLP options onto an OpenTelemetry <see cref="OtlpExporterOptions"/> instance.
    /// </summary>
    public static void ApplyOtlpOptions(OtlpExporterOptions otlp, TelemetryOptions options)
    {
        ArgumentNullException.ThrowIfNull(otlp);
        ArgumentNullException.ThrowIfNull(options);

        if (!string.IsNullOrWhiteSpace(options.Exporters.Otlp.Endpoint)
            && Uri.TryCreate(options.Exporters.Otlp.Endpoint, UriKind.Absolute, out var endpoint))
        {
            otlp.Endpoint = endpoint;
        }

        if (!string.IsNullOrWhiteSpace(options.Exporters.Otlp.Headers))
        {
            otlp.Headers = options.Exporters.Otlp.Headers;
        }

        otlp.Protocol = ResolveProtocol(options.Exporters.Otlp);
    }

    private static OtlpExportProtocol ResolveProtocol(TelemetryOtlpExporterOptions otlpOptions)
    {
        if (!string.IsNullOrWhiteSpace(otlpOptions.ProtocolName))
        {
            if (string.Equals(otlpOptions.ProtocolName, "http/protobuf", StringComparison.OrdinalIgnoreCase)
                || string.Equals(otlpOptions.ProtocolName, "httpprotobuf", StringComparison.OrdinalIgnoreCase)
                || string.Equals(otlpOptions.ProtocolName, "http", StringComparison.OrdinalIgnoreCase))
            {
                return OtlpExportProtocol.HttpProtobuf;
            }

            return OtlpExportProtocol.Grpc;
        }

        return otlpOptions.Protocol == TelemetryOtlpProtocol.HttpProtobuf
            ? OtlpExportProtocol.HttpProtobuf
            : OtlpExportProtocol.Grpc;
    }
}
