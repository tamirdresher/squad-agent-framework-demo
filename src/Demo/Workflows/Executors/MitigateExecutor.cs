using Microsoft.Agents.AI.Workflows;

/// <summary>
/// Mitigation: records the recommended remediation action and triggers the mock
/// runbook for the routed subsystem.  Pure C# — deterministic, no LLM.
/// </summary>
internal sealed class MitigateExecutor()
    : Executor<IncidentWorkflowContext, IncidentWorkflowContext>("Mitigate")
{
    private static readonly Dictionary<string, (string Mitigation, string Runbook)> Runbooks =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["Database"] = (
                "Increase connection pool max-size to 200; flush pending slow queries; restart pool manager.",
                "runbook-db-pool-recovery"),
            ["Network"] = (
                "Trigger BGP route refresh; verify LB health-check thresholds; flush DNS TTL cache.",
                "runbook-net-recovery"),
            ["Authentication"] = (
                "Roll back token-validation config; increase session-store replica count by 2.",
                "runbook-auth-recovery"),
            ["Payments"] = (
                "Enable payment-gateway circuit breaker; switch to fallback PSP endpoint; drain retry queue.",
                "runbook-payments-recovery"),
        };

    public override ValueTask<IncidentWorkflowContext> HandleAsync(
        IncidentWorkflowContext ctx,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        var subsystem = ctx.Subsystem ?? "Unknown";
        var (mitigation, runbook) = Runbooks.TryGetValue(subsystem, out var r)
            ? r
            : ($"Generic recovery for subsystem '{subsystem}'.", "runbook-generic");

        Console.WriteLine($"[Mitigate] subsystem={subsystem}  runbook={runbook}");
        return ValueTask.FromResult(ctx with
        {
            RecommendedMitigation = mitigation,
            RunbookTriggered      = runbook,
            MitigationApplied     = true
        });
    }
}
