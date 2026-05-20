using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using InProcessExecution = WorkflowExecutionCompat;

internal static class WorkflowExample
{
    public static async Task<WorkflowExampleReport> RunAsync(
        ProviderAgentFactory provider,
        AIAgent squad,
        CancellationToken cancellationToken = default)
    {
        const string prompt = "Write one punchy marketing sentence for Squad as a single Agent Framework participant.";

        IChatClient chatClient = provider.CreateChatClient();

        ChatClientAgent writer = new(
            chatClient,
            "You are a concise copywriter. Provide a single, punchy marketing sentence based on the prompt.",
            "writer");

        var workflow = AgentWorkflowBuilder.BuildSequential([writer, squad]);

        Console.WriteLine("Streaming writer -> Squad workflow:");

        await using StreamingRun run = await InProcessExecution.StreamAsync(workflow, input: prompt);
        await run.TrySendMessageAsync(new TurnToken(emitEvents: true));
        await foreach (WorkflowEvent evt in run.WatchStreamAsync().WithCancellation(cancellationToken))
        {
            if (evt is AgentResponseUpdateEvent e)
            {
                Console.Write(e.Update.Text);
            }
        }

        Console.WriteLine();

        return new WorkflowExampleReport(
            Example: "workflow",
            Status: "PASS",
            Provider: provider.Summary,
            Runtime: "Microsoft Agent Framework in-process streaming workflow.",
            Pattern: "writer ChatClientAgent -> SquadAgent sequential workflow",
            Prompt: prompt,
            Participants:
            [
                new Participant(writer.Name ?? "writer", "ChatClientAgent over ProviderAgentFactory IChatClient", "streamed"),
                new Participant(squad.Name ?? "repo-local-squad", "SquadAgent : AIAgent backed by GitHubCopilotAgent", "streamed")
            ]);
    }
}

internal sealed record WorkflowExampleReport(
    string Example,
    string Status,
    ProviderSummary Provider,
    string Runtime,
    string Pattern,
    string Prompt,
    IReadOnlyList<Participant> Participants);

internal static class WorkflowExecutionCompat
{
    public static ValueTask<StreamingRun> StreamAsync(
        Workflow workflow,
        string input,
        CancellationToken cancellationToken = default) =>
        Microsoft.Agents.AI.Workflows.InProcessExecution.RunStreamingAsync(
            workflow,
            input,
            "writer-squad-workflow",
            cancellationToken);
}
