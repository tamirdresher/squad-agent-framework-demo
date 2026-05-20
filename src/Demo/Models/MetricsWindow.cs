/// <summary>
/// Metrics/log window for the affected time range (deterministic enrichment).
/// </summary>
internal sealed record MetricsWindow(
    double ErrorRatePercent,
    double P99LatencyMs,
    int RequestsPerMinute,
    string[] TopErrors);
