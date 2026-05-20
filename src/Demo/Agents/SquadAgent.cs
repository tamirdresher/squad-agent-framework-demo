using GitHub.Copilot.SDK;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using OpenTelemetry;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Agents.AI.GitHub.Copilot;

internal sealed class SquadAgent : AIAgent, IAsyncDisposable
{
    private readonly string id;
    private readonly string name;
    private readonly string description;
    private readonly bool traceEvents;
    private readonly string? tracePath;
    private readonly SemaphoreSlim traceFileSemaphore = new(1, 1);
    private readonly ConcurrentDictionary<string, string> traceToolNames = new();
    private AIAgent? inner = null;
    private CopilotClient? copilotClient = null;

    // ── OTel: tracing ──────────────────────────────────────────────────────
    // Registered in Program.cs alongside the demo activity source so all
    // SquadAgent spans appear in the Aspire dashboard under "Squad.AgentFramework.SquadAgent".
    internal static readonly ActivitySource SquadAgentActivitySource =
        new("Squad.AgentFramework.SquadAgent", "1.0.0");

    // ── OTel: metrics ──────────────────────────────────────────────────────
    internal static readonly Meter SquadAgentMeter =
        new("Squad.AgentFramework.SquadAgent", "1.0.0");

    private static readonly Counter<long> AgentRunsStarted =
        SquadAgentMeter.CreateCounter<long>(
            "squad.agent.runs_started",
            description: "Number of agent runs started.");

    private static readonly Counter<long> AgentRunsCompleted =
        SquadAgentMeter.CreateCounter<long>(
            "squad.agent.runs_completed",
            description: "Number of agent runs completed (tags: squad.outcome = success|error).");

    private static readonly Histogram<double> AgentRunDurationMs =
        SquadAgentMeter.CreateHistogram<double>(
            "squad.agent.run_duration_ms",
            unit: "ms",
            description: "Duration of agent run invocations.");

    private static readonly Counter<long> SessionsCreated =
        SquadAgentMeter.CreateCounter<long>(
            "squad.agent.sessions_created",
            description: "Number of agent sessions created.");

    // TODO: hook token counters when Copilot SDK exposes Usage on session response objects.

    public SquadAgent(
        string id,
        string name,
        string description,
        bool traceEvents = false,
        string? tracePath = null)
    {
        this.id = id;
        this.name = name;
        this.description = description;
        this.traceEvents = traceEvents;
        this.tracePath = string.IsNullOrWhiteSpace(tracePath) ? null : tracePath;
    }

    protected override string? IdCore => id;

    public override string? Name => name;

    public override string? Description => description;

    protected override async ValueTask<AgentSession> CreateSessionCoreAsync(CancellationToken cancellationToken = default)
    {
        var hadInner = inner is not null;
        using var activity = SquadAgentActivitySource.StartActivity("SquadAgent.CreateSession", ActivityKind.Internal);
        activity?.SetTag("squad.agent.id", id);
        activity?.SetTag("squad.agent.name", name);

        var agent = await EnsureInnerAsync(cancellationToken);
        activity?.SetTag("squad.copilot.client.created", !hadInner);

        SessionsCreated.Add(1, new KeyValuePair<string, object?>("squad.agent.name", name));
        return await agent.CreateSessionAsync(cancellationToken);
    }

    protected override async ValueTask<JsonElement> SerializeSessionCoreAsync(
        AgentSession session,
        JsonSerializerOptions? jsonSerializerOptions = null,
        CancellationToken cancellationToken = default)
    {
        using var activity = SquadAgentActivitySource.StartActivity("SquadAgent.SessionSerialize", ActivityKind.Internal);
        activity?.SetTag("squad.agent.id", id);
        activity?.SetTag("squad.agent.name", name);
        return await (inner ?? throw new InvalidOperationException("Create a SquadAgent session before serializing it."))
            .SerializeSessionAsync(session, jsonSerializerOptions, cancellationToken);
    }

    protected override async ValueTask<AgentSession> DeserializeSessionCoreAsync(
        JsonElement serializedState,
        JsonSerializerOptions? jsonSerializerOptions = null,
        CancellationToken cancellationToken = default)
    {
        using var activity = SquadAgentActivitySource.StartActivity("SquadAgent.SessionDeserialize", ActivityKind.Internal);
        activity?.SetTag("squad.agent.id", id);
        activity?.SetTag("squad.agent.name", name);
        return await (inner ?? throw new InvalidOperationException("Create a SquadAgent session before deserializing it."))
            .DeserializeSessionAsync(serializedState, jsonSerializerOptions, cancellationToken);
    }

    protected override async Task<AgentResponse> RunCoreAsync(
        IEnumerable<ChatMessage> messages,
        AgentSession? session = null,
        AgentRunOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        AgentRunsStarted.Add(1, new KeyValuePair<string, object?>("squad.agent.name", name));
        using var activity = SquadAgentActivitySource.StartActivity("SquadAgent.Run", ActivityKind.Client);
        activity?.SetTag("squad.agent.id", id);
        activity?.SetTag("squad.agent.name", name);
        activity?.SetTag("squad.input.length", messages.Sum(m => m.Text?.Length ?? 0));
        if (session is not null)
        {
            activity?.SetTag("squad.session.id", GetSessionId(session));
        }

        var sw = Stopwatch.StartNew();
        try
        {
            AgentResponse result;
            if (traceEvents)
            {
                result = await AgentResponseExtensions.ToAgentResponseAsync(
                    RunTraceableStreamingAsync(messages, session, cancellationToken),
                    cancellationToken);
            }
            else
            {
                var agent = await EnsureInnerAsync(cancellationToken);
                var guardedMessages = AddSquadBoundary(messages);
                result = await agent.RunAsync(guardedMessages, session, options, cancellationToken);
            }

            AgentRunsCompleted.Add(1,
                new KeyValuePair<string, object?>("squad.agent.name", name),
                new KeyValuePair<string, object?>("squad.outcome", "success"));
            return result;
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.SetTag("exception.type", ex.GetType().Name);
            activity?.SetTag("exception.message", ex.Message);
            AgentRunsCompleted.Add(1,
                new KeyValuePair<string, object?>("squad.agent.name", name),
                new KeyValuePair<string, object?>("squad.outcome", "error"));
            throw;
        }
        finally
        {
            sw.Stop();
            AgentRunDurationMs.Record(
                sw.Elapsed.TotalMilliseconds,
                new KeyValuePair<string, object?>("squad.agent.name", name));
        }
    }

    protected override async IAsyncEnumerable<AgentResponseUpdate> RunCoreStreamingAsync(
        IEnumerable<ChatMessage> messages,
        AgentSession? session = null,
        AgentRunOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        AgentRunsStarted.Add(1, new KeyValuePair<string, object?>("squad.agent.name", name));
        using var activity = SquadAgentActivitySource.StartActivity("SquadAgent.RunStreaming", ActivityKind.Client);
        activity?.SetTag("squad.agent.id", id);
        activity?.SetTag("squad.agent.name", name);
        activity?.SetTag("squad.input.length", messages.Sum(m => m.Text?.Length ?? 0));
        if (session is not null)
        {
            activity?.SetTag("squad.session.id", GetSessionId(session));
        }

        var sw = Stopwatch.StartNew();
        // Note: yield return is not allowed inside a try/catch block in C# iterators,
        // so error detection uses a 'completed' sentinel instead of a catch clause.
        var completed = false;
        try
        {
            if (traceEvents)
            {
                await foreach (var update in RunTraceableStreamingAsync(messages, session, cancellationToken).ConfigureAwait(false))
                {
                    yield return update;
                }
            }
            else
            {
                var agent = await EnsureInnerAsync(cancellationToken);
                var guardedMessages = AddSquadBoundary(messages);
                await foreach (var update in agent.RunStreamingAsync(guardedMessages, session, options, cancellationToken).ConfigureAwait(false))
                {
                    yield return update;
                }
            }

            completed = true;
        }
        finally
        {
            sw.Stop();
            var outcome = completed ? "success" : "error";
            if (!completed) activity?.SetStatus(ActivityStatusCode.Error);
            AgentRunsCompleted.Add(1,
                new KeyValuePair<string, object?>("squad.agent.name", name),
                new KeyValuePair<string, object?>("squad.outcome", outcome));
            AgentRunDurationMs.Record(
                sw.Elapsed.TotalMilliseconds,
                new KeyValuePair<string, object?>("squad.agent.name", name));
        }
    }

    private async ValueTask<AIAgent> EnsureInnerAsync(CancellationToken cancellationToken)
    {
        if (inner is not null)
        {
            return inner;
        }

        // Enable the Copilot SDK's native OTel when an OTLP endpoint is configured.
        // With TelemetryConfig set, the CLI server (Node.js process) emits GenAI + MCP
        // semantic-convention spans (model invocations, tool calls, sub-agent spawns,
        // token usage) directly to the OTLP endpoint — these flow to the Aspire dashboard
        // alongside the .NET wrapper spans from SquadAgentActivitySource.
        // W3C trace-context propagation is automatic; no extra .NET ActivitySource is needed.
        var otlpEndpoint = Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT");
        CopilotClientOptions? clientOptions = string.IsNullOrWhiteSpace(otlpEndpoint)
            ? null
            : new CopilotClientOptions
              {
                  Telemetry = new TelemetryConfig { OtlpEndpoint = otlpEndpoint }
              };

        copilotClient = new CopilotClient(clientOptions);
        await copilotClient.StartAsync(cancellationToken);

        inner = new GitHubCopilotAgent(
            copilotClient,
            new SessionConfig()
            {
                Agent = "Squad",
                OnPermissionRequest = PermissionHandler.ApproveAll,
                OnEvent = traceEvents ? TraceCopilotEvent : null,
                Streaming = traceEvents,
                IncludeSubAgentStreamingEvents = traceEvents
            });

        return inner;
    }

    private async IAsyncEnumerable<AgentResponseUpdate> RunTraceableStreamingAsync(
        IEnumerable<ChatMessage> messages,
        AgentSession? session,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await EnsureInnerAsync(cancellationToken);

        session ??= await CreateSessionCoreAsync(cancellationToken);
        var sessionId = GetSessionId(session);

        await using var copilotSession = sessionId is null
            ? await copilotClient!.CreateSessionAsync(CreateTraceSessionConfig(), cancellationToken).ConfigureAwait(false)
            : await copilotClient!.ResumeSessionAsync(sessionId, CreateTraceResumeConfig(), cancellationToken).ConfigureAwait(false);

        if (sessionId is null)
        {
            SetSessionId(session, copilotSession.SessionId);
        }

        var channel = Channel.CreateUnbounded<AgentResponseUpdate>();
        using var subscription = copilotSession.On(evt =>
        {
            try
            {
                switch (evt)
                {
                    case AssistantMessageDeltaEvent e when !string.IsNullOrEmpty(e.Data.DeltaContent):
                        channel.Writer.TryWrite(CreateUpdate(e, e.Data.DeltaContent, e.Data.MessageId));
                        break;

                    case SessionIdleEvent:
                        channel.Writer.TryComplete();
                        break;

                    case SessionErrorEvent e:
                        channel.Writer.TryComplete(new InvalidOperationException($"Session error: {e.Data.Message ?? "unknown"}"));
                        break;
                }
            }
            catch (Exception ex)
            {
                channel.Writer.TryComplete(ex);
            }
        });

        await copilotSession.SendAsync(
            new MessageOptions { Prompt = BuildPrompt(AddSquadBoundary(messages)) },
            cancellationToken).ConfigureAwait(false);

        await foreach (var update in channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            yield return update;
        }
    }

    private SessionConfig CreateTraceSessionConfig() => new()
    {
        OnPermissionRequest = PermissionHandler.ApproveAll,
        OnEvent = TraceCopilotEvent,
        Streaming = true,
        IncludeSubAgentStreamingEvents = true
    };

    private ResumeSessionConfig CreateTraceResumeConfig() => new()
    {
        OnPermissionRequest = PermissionHandler.ApproveAll,
        OnEvent = TraceCopilotEvent,
        Streaming = true,
        IncludeSubAgentStreamingEvents = true
    };

    private static AgentResponseUpdate CreateUpdate(SessionEvent sessionEvent, string text, string? messageId) => new(ChatRole.Assistant, text)
    {
        AgentId = sessionEvent.AgentId,
        CreatedAt = sessionEvent.Timestamp,
        MessageId = messageId,
        RawRepresentation = sessionEvent,
        ResponseId = sessionEvent.Id.ToString()
    };

    private static string? GetSessionId(AgentSession session) =>
        session.GetType().GetProperty("SessionId")?.GetValue(session) as string;

    private static void SetSessionId(AgentSession session, string? sessionId) =>
        session.GetType().GetProperty("SessionId")?.SetValue(session, sessionId);

    private static string BuildPrompt(IEnumerable<ChatMessage> messages)
    {
        var prompt = new StringBuilder();
        foreach (var message in messages)
        {
            prompt.Append(message.Role).Append(": ").AppendLine(message.Text);
        }

        return prompt.ToString();
    }

    private void TraceCopilotEvent(SessionEvent sessionEvent)
    {
        // Note: OTel child spans for individual Copilot events (tool calls, sub-agent spawns)
        // are not emitted here because the subscription callback fires on the CopilotSession's
        // internal event-dispatch thread — outside the AsyncLocal execution context that carries
        // the parent SquadAgent.Run activity. Emitting spans here would produce root-level spans
        // disconnected from the run trace. A future enhancement could capture the parent
        // ActivityContext before the subscription and restore it inside the callback.
        try
        {
            Console.WriteLine(FormatTraceLine(sessionEvent));

            if (tracePath is not null)
            {
                traceFileSemaphore.Wait();
                try
                {
                    var directory = Path.GetDirectoryName(tracePath);
                    if (!string.IsNullOrEmpty(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }

                    File.AppendAllText(tracePath, sessionEvent.ToJson() + Environment.NewLine);
                }
                finally
                {
                    traceFileSemaphore.Release();
                }
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[copilot-trace] event handler failed: {ex.Message}");
        }
    }

    private string FormatTraceLine(SessionEvent sessionEvent)
    {
        var agent = sessionEvent.AgentId ?? "root";
        var prefix = sessionEvent switch
        {
            AssistantReasoningEvent or AssistantReasoningDeltaEvent => "🧠 reasoning",
            AbortEvent or SessionErrorEvent => "❌",
            SessionWarningEvent => "⚠️",
            _ => "·"
        };

        var suffix = GetTraceSuffix(sessionEvent);
        return string.IsNullOrWhiteSpace(suffix)
            ? $"{prefix} {sessionEvent.Type} agent={agent} id={sessionEvent.Id}"
            : $"{prefix} {sessionEvent.Type} agent={agent} id={sessionEvent.Id} {suffix}";
    }

    private string? GetTraceSuffix(SessionEvent sessionEvent) => sessionEvent switch
    {
        SubagentSelectedEvent e => $"subagent={FormatName(e.Data.AgentDisplayName, e.Data.AgentName)}",
        SubagentDeselectedEvent => "subagent=deselected",
        SubagentStartedEvent e => $"subagent={FormatName(e.Data.AgentDisplayName, e.Data.AgentName)}",
        SubagentCompletedEvent e => $"subagent={FormatName(e.Data.AgentDisplayName, e.Data.AgentName)}",
        SubagentFailedEvent e => $"subagent={FormatName(e.Data.AgentDisplayName, e.Data.AgentName)} error={TrimForTrace(e.Data.Error)}",
        ToolUserRequestedEvent e => TrackToolName(e.Data.ToolCallId, e.Data.ToolName),
        ToolExecutionStartEvent e => TrackToolName(e.Data.ToolCallId, e.Data.ToolName),
        ToolExecutionCompleteEvent e => GetToolSuffix(e.Data.ToolCallId, $"success={e.Data.Success}"),
        ToolExecutionProgressEvent e => GetToolSuffix(e.Data.ToolCallId, TrimForTrace(e.Data.ProgressMessage)),
        ToolExecutionPartialResultEvent e => GetToolSuffix(e.Data.ToolCallId, "partial-result"),
        PermissionRequestedEvent e => $"permission={e.Data.PermissionRequest?.Kind ?? e.Data.PromptRequest?.Kind ?? "unknown"}",
        PermissionCompletedEvent e => $"permission={e.Data.Result?.Kind ?? "unknown"}",
        AbortEvent e => $"reason={TrimForTrace(e.Data.Reason)}",
        SessionErrorEvent e => $"error={TrimForTrace(e.Data.Message ?? e.Data.ErrorType ?? e.Data.ErrorCode)}",
        SessionWarningEvent e => $"warning={TrimForTrace(e.Data.Message ?? e.Data.WarningType)}",
        _ => null
    };

    private string TrackToolName(string? toolCallId, string? toolName)
    {
        var name = string.IsNullOrWhiteSpace(toolName) ? "unknown" : toolName;
        if (!string.IsNullOrWhiteSpace(toolCallId))
        {
            traceToolNames[toolCallId] = name;
        }

        return $"tool={name}";
    }

    private string GetToolSuffix(string? toolCallId, string? detail)
    {
        var name = !string.IsNullOrWhiteSpace(toolCallId) && traceToolNames.TryGetValue(toolCallId, out var toolName)
            ? toolName
            : "unknown";

        return string.IsNullOrWhiteSpace(detail) ? $"tool={name}" : $"tool={name} {detail}";
    }

    private static string FormatName(string? displayName, string? name) =>
        string.IsNullOrWhiteSpace(displayName)
            ? (string.IsNullOrWhiteSpace(name) ? "unknown" : name)
            : string.IsNullOrWhiteSpace(name) || string.Equals(displayName, name, StringComparison.Ordinal)
                ? displayName
                : $"{displayName} ({name})";

    private static string TrimForTrace(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "unknown";
        }

        value = value.ReplaceLineEndings(" ").Trim();
        return value.Length <= 80 ? value : value[..77] + "...";
    }

    private IEnumerable<ChatMessage> AddSquadBoundary(IEnumerable<ChatMessage> messages)
    {
        yield return new ChatMessage(
            ChatRole.System,
            DemoRuntime.SquadBoundaryInstructions);

        foreach (var message in messages)
        {
            yield return message;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (inner is IAsyncDisposable asyncDisposable)
        {
            await asyncDisposable.DisposeAsync();
            return;
        }

        if (inner is IDisposable disposable)
        {
            disposable.Dispose();
        }

        copilotClient?.Dispose();
    }
}
