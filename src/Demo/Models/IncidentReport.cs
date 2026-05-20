/// <summary>
/// A raw incident report — the workflow's initial input.
/// </summary>
internal sealed record IncidentReport(
    string Title,
    string Description,
    string CustomerId,
    string Region);
