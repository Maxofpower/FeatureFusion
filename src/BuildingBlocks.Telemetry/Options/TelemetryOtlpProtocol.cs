namespace BuildingBlocks.Telemetry;

/// <summary>
/// OTLP wire protocol for BuildingBlocks.Telemetry exporters.
/// </summary>
public enum TelemetryOtlpProtocol
{
    /// <summary>gRPC (typical port 4317).</summary>
    Grpc = 0,

    /// <summary>HTTP protobuf (typical port 4318).</summary>
    HttpProtobuf = 1,
}
