using Polifemo.Contracts.Errors;

namespace Polifemo.Platform.Privacy;

/// <summary>
/// Telemetry is off unless an exporter points at the local boundary. Standard OpenTelemetry endpoint variables
/// are validated like any other destination, so an exporter cannot be pointed at a hosted collector.
/// </summary>
public static class TelemetryPolicy
{
    public static readonly string[] EndpointVariables =
    [
        "OTEL_EXPORTER_OTLP_ENDPOINT",
        "OTEL_EXPORTER_OTLP_TRACES_ENDPOINT",
        "OTEL_EXPORTER_OTLP_METRICS_ENDPOINT",
        "OTEL_EXPORTER_OTLP_LOGS_ENDPOINT",
    ];

    /// <summary>Throws <c>POLICY_EXTERNAL_ENDPOINT</c> when any configured exporter is outside the local boundary.</summary>
    public static void Validate(LocalEndpointPolicy policy, Func<string, string?> getVariable)
    {
        foreach (var variable in EndpointVariables)
        {
            if (getVariable(variable) is { Length: > 0 } value)
                policy.Validate(ProcessingCapability.Telemetry, value, variable);
        }
    }
}
