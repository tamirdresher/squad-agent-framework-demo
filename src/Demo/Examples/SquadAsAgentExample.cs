using Microsoft.Agents.AI;

internal static class SquadAsAgentExample
{
    public static async Task<DemoReport> RunAsync(
        ProviderAgentFactory provider,
        AgentHandle squad,
        CancellationToken cancellationToken = default)
    {
        await using var planner = provider.CreateAgent(
            name: "planner",
            instructions: "Plan one short step for a focused Agent Framework demo.");

        await using var reviewer = provider.CreateAgent(
            name: "reviewer",
            instructions: "Review the demo outcome in one short sentence.");

        var plan = await DemoRuntime.RunAgentAsync(planner.Agent, "Prepare the demo flow.", cancellationToken);
        var squadOutput = await DemoRuntime.RunAgentAsync(squad.Agent, $"Given this plan, describe Squad's role: {plan}", cancellationToken);
        var review = await DemoRuntime.RunAgentAsync(reviewer.Agent, $"Review this Squad output: {squadOutput}", cancellationToken);

        return new DemoReport(
            Example: "squad-as-agent",
            Status: "PASS",
            Provider: provider.Summary,
            Demonstrates:
            [
                "SquadAgent derives from Microsoft.Agents.AI.AIAgent.",
                "SquadAgent delegates to GitHubCopilotAgent under the hood.",
                "When configured, provider participants are created through IChatClient.AsAIAgent(...).",
                "The console app treats Squad as one participant in the Agent Framework flow."
            ],
            Participants:
            [
                new Participant(planner.Agent.Name ?? "planner", planner.Kind, plan),
                new Participant(squad.Agent.Name ?? "repo-local-squad", squad.Kind, squadOutput),
                new Participant(reviewer.Agent.Name ?? "reviewer", reviewer.Kind, review)
            ]);
    }
}

internal sealed record DemoReport(
    string Example,
    string Status,
    ProviderSummary Provider,
    IReadOnlyList<string> Demonstrates,
    IReadOnlyList<Participant> Participants);
