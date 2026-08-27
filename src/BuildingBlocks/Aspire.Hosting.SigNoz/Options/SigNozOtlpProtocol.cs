namespace Aspire.Hosting;

/// <summary>
/// OTLP wire protocol for <see cref="SigNozBuilderExtensions.WithSigNozOtlpExporter"/>.
/// </summary>
public enum SigNozOtlpProtocol
{
    /// <summary>gRPC (port 4317).</summary>
    Grpc = 0,

    /// <summary>HTTP protobuf (port 4318).</summary>
    HttpProtobuf = 1,
}
