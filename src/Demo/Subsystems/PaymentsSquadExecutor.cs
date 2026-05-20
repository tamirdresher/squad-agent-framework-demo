using Microsoft.Agents.AI.Workflows;

/// <summary>
/// Payments subsystem squad executor.
/// Investigates payment gateway timeouts, idempotency key conflicts,
/// card network latency, retry storms, and PSP API degradations.
/// </summary>
internal sealed class PaymentsSquadExecutor(ProviderAgentFactory provider)
    : Executor<IncidentWorkflowContext, IncidentWorkflowContext>("PaymentsSquad")
{
    private const string Charter =
        """
        You are the Payments incident-response squad. Your charter:
        - Investigate payment gateway timeouts, idempotency key conflicts,
          card network latency, retry storms, and PSP API degradations.
        - Correlate payments metrics with downstream error patterns.
        - Recommend read-only payments diagnostic steps.
        Be concise: 3–5 sentences.
        """;

    public override async ValueTask<IncidentWorkflowContext> HandleAsync(
        IncidentWorkflowContext ctx,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        var analysis = await SubsystemSquadHelpers.RunSubsystemAnalysisAsync(
            provider, "payments-squad", Charter,
            SubsystemSquadHelpers.BuildPrompt("Payments", ctx), cancellationToken);

        return ctx with { SubsystemAnalysis = analysis };
    }
}
