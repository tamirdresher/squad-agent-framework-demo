/// <summary>
/// Customer metadata fetched from the customer service (deterministic enrichment).
/// </summary>
internal sealed record CustomerInfo(
    string CustomerId,
    string Tier,
    string Sla,
    string PrimaryContact);
