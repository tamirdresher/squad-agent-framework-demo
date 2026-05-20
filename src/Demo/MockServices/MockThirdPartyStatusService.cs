/// <summary>
/// Simulates querying a third-party operational status page.
/// In production this would be a real HttpClient call; here it is
/// a deterministic mock that returns a canned status response.
/// </summary>
internal static class MockThirdPartyStatusService
{
    public static async Task<string> GetStatusAsync(
        string serviceName,
        CancellationToken cancellationToken = default)
    {
        await Task.Delay(150, cancellationToken);
        return serviceName.ToLowerInvariant() switch
        {
            "payment-gateway" =>
                "[StatusPage] payment-gateway: DEGRADED — elevated API response times " +
                "observed in EU-WEST since 14:15 UTC. Investigating.",
            _ =>
                $"[StatusPage] {serviceName}: OPERATIONAL — no known issues."
        };
    }
}
