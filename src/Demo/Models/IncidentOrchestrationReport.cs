/// <summary>Final JSON report written to stdout by Program.cs.</summary>
internal sealed record IncidentOrchestrationReport(
    string Example,
    string Status,
    ProviderSummary Provider,
    IncidentReport Input,
    string? WorkflowRunId,
    IncidentWorkflowContext? FinalContext,
    string Runtime);
