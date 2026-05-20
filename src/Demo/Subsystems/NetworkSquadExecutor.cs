using Microsoft.Agents.AI.Workflows;

/// <summary>
/// Network subsystem squad executor.
/// Investigates DNS failures, packet loss, routing issues, load-balancer
/// health, CDN edge problems, and TLS certificate anomalies.
/// </summary>
internal sealed class NetworkSquadExecutor(ProviderAgentFactory provider)
    : Executor<IncidentWorkflowContext, IncidentWorkflowContext>("NetworkSquad")
{
    private const string Charter =
        """
        You are the Network incident-response squad. Your charter:
        - Investigate DNS failures, packet loss, routing issues, load-balancer
          health, CDN edge problems, and TLS certificate anomalies.
        - Correlate network telemetry with service error rates.
        - Recommend read-only network diagnostic steps.
        Be concise: 3–5 sentences.
        """;

    public override async ValueTask<IncidentWorkflowContext> HandleAsync(
        IncidentWorkflowContext ctx,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        var analysis = await SubsystemSquadHelpers.RunSubsystemAnalysisAsync(
            provider, "network-squad", Charter,
            SubsystemSquadHelpers.BuildPrompt("Network", ctx), cancellationToken);

        return ctx with { SubsystemAnalysis = analysis };
    }
}
