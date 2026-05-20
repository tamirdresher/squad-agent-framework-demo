using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

internal static class DemoRuntime
{
    public const string SquadBoundaryInstructions =
        "You are the repo-local Squad facade. Stay read-only, route to the smallest relevant role, and do not execute tools.";

    public static AgentHandle CreateSquad(bool traceEvents = false, string? tracePath = null) =>
        new(
            new SquadAgent(
                "repo-local-squad",
                "repo-local-squad",
                "A minimal Squad wrapper that is itself a Microsoft.Agents.AI.AIAgent and delegates to GitHubCopilotAgent.",
                traceEvents,
                tracePath),
            "SquadAgent : AIAgent backed by GitHubCopilotAgent");

    public static async Task<string> RunAgentAsync(AIAgent agent, string prompt, CancellationToken cancellationToken = default)
    {
        var session = await agent.CreateSessionAsync(cancellationToken);
        var response = await agent.RunAsync(new ChatMessage(ChatRole.User, prompt), session, cancellationToken: cancellationToken);
        return response.Text;
    }

    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };
}

internal sealed record Participant(string Name, string Adapter, string Output);
