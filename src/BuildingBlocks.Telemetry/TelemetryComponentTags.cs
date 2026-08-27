namespace BuildingBlocks.Telemetry;

/// <summary>
/// Well-known <c>telemetry.component</c> tag values applied to trace spans by BuildingBlocks defaults.
/// </summary>
/// <remarks>
/// Filter in SigNoz Traces with attribute <see cref="TagName"/> (e.g. <c>telemetry.component = npgsql</c>).
/// <see cref="EntityFrameworkCore"/>, <see cref="Redis"/>, and <see cref="GrpcClient"/> values are applied
/// when those ActivitySources appear (consumer-added contrib packages).
/// </remarks>
public static class TelemetryComponentTags
{
    /// <summary>OpenTelemetry span attribute for component identification.</summary>
    public const string TagName = "telemetry.component";

    /// <summary>ASP.NET Core incoming HTTP.</summary>
    public const string AspNetCore = "aspnetcore";

    /// <summary>Outgoing <see cref="System.Net.Http.HttpClient"/>.</summary>
    public const string HttpClient = "httpclient";

    /// <summary>Entity Framework Core.</summary>
    public const string EntityFrameworkCore = "efcore";

    /// <summary>Npgsql (PostgreSQL driver).</summary>
    public const string Npgsql = "npgsql";

    /// <summary>StackExchange.Redis.</summary>
    public const string Redis = "redis";

    /// <summary>gRPC client.</summary>
    public const string GrpcClient = "grpcclient";

    /// <summary>Microsoft.Data.SqlClient.</summary>
    public const string SqlClient = "sqlclient";

    /// <summary>EventBusRabbitMQ (<see cref="TelemetryDefaults.EventBusActivitySource"/>).</summary>
    public const string EventBus = "eventbus";

    /// <summary>BuildingBlocks.Mediator (<see cref="TelemetryDefaults.MediatorActivitySource"/>).</summary>
    public const string Mediator = "mediator";

    /// <summary>MassTransit (<see cref="TelemetryDefaults.MassTransitActivitySource"/>).</summary>
    public const string MassTransit = "masstransit";
}
