using Microsoft.Agents.AI.Workflows;

/// <summary>
/// Authentication subsystem squad executor.
/// Investigates token validation failures, SSO/OIDC provider errors,
/// session store saturation, and MFA service outages.
/// </summary>
internal sealed class AuthSquadExecutor(ProviderAgentFactory provider)
    : Executor<IncidentWorkflowContext, IncidentWorkflowContext>("AuthSquad")
{
    private const string Charter =
        """
        You are the Authentication incident-response squad. Your charter:
        - Investigate token validation failures, SSO/OIDC provider errors,
          session store saturation, and MFA service outages.
        - Correlate auth metrics with downstream service failures.
        - Recommend read-only authentication diagnostic steps.
        Be concise: 3–5 sentences.
        """;

    public override async ValueTask<IncidentWorkflowContext> HandleAsync(
        IncidentWorkflowContext ctx,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        var analysis = await SubsystemSquadHelpers.RunSubsystemAnalysisAsync(
            provider, "auth-squad", Charter,
            SubsystemSquadHelpers.BuildPrompt("Authentication", ctx), cancellationToken);

        return ctx with { SubsystemAnalysis = analysis };
    }
}
