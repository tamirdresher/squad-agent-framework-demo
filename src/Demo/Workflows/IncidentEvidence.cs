internal static class IncidentEvidence
{
    /// <summary>
    /// Creates a realistic synthetic incident report — Payments subsystem slowdown
    /// caused by a downstream database connection pool exhaustion after a config
    /// change, plus a concurrent third-party payment gateway degradation.
    /// </summary>
    public static IncidentReport CreateSyntheticIncidentReport() =>
        new(
            Title: "Payment checkout latency spike — p99 > 8 s, error rate 14 %",
            Description:
                """
                Alert fired at 14:22 UTC. The checkout service is reporting p99 latency
                above 8 000 ms (baseline 250 ms) and a 14 % error rate on /checkout/confirm.
                Recent changes: (1) database connection-pool max-size reduced from 200 to 50
                during a cost-optimisation pass at 13:55 UTC; (2) payment-gateway SDK
                upgraded from v3.4 to v3.5 at 13:40 UTC; (3) feature flag
                'new_pricing_engine' enabled for 10 % of traffic at 14:10 UTC.
                Customer CUST-9921 (Enterprise, SLA 2 h) has already opened a ticket.
                Affecting EU-WEST region only. No infra alerts from the network or auth
                layers. Database slow-query log shows lock contention on the orders table.
                """,
            CustomerId: "CUST-9921",
            Region: "EU-WEST");
}
