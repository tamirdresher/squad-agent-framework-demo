using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Hosting;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

if (args.Any(arg => arg is "--list-examples" or "-h" or "--help"))
{
    Console.WriteLine("Available examples:");
    Console.WriteLine("  squad-as-agent   Planner -> Squad -> reviewer flow with real provider-backed participants.");
    Console.WriteLine("  workflow         Sequential writer -> Squad streaming workflow.");
    Console.WriteLine("  incident         Durable incident-response workflow: AI triage → enrichment → external");
    Console.WriteLine("                   comms → dynamic subsystem-squad routing (DB/Net/Auth/Pay) → mitigation");
    Console.WriteLine("                   → diagnose-loop. Backed by the DTS emulator via Aspire.");
    Console.WriteLine();
    Console.WriteLine("Run (via Aspire AppHost — recommended):");
    Console.WriteLine("  dotnet run --project .\\src\\AppHost");
    Console.WriteLine();
    Console.WriteLine("Run individual examples:");
    Console.WriteLine("  dotnet run --project .\\src\\Demo -- --example squad-as-agent");
    Console.WriteLine("  dotnet run --project .\\src\\Demo -- --example workflow");
    Console.WriteLine("  dotnet run --project .\\src\\Demo -- --example incident");
    Console.WriteLine();
    Console.WriteLine("AppHost:");
    Console.WriteLine("  SQUAD_AF_EXAMPLE=<name>      Preselect an example for AppHost.");
    Console.WriteLine("  SQUAD_AF_TRACE=1             Enable Copilot session tracing for AppHost or direct runs.");
    Console.WriteLine();
    Console.WriteLine("Tracing:");
    Console.WriteLine("  --trace[=<path>]  Stream GitHub Copilot SDK session events (tool calls, sub-agent spawns, reasoning, permission flow).");
    Console.WriteLine("                   Without a path, trace output is console-only; with a path, JSONL is appended too.");
    Console.WriteLine("  SQUAD_AF_TRACE_PATH=<path>   Append JSONL trace events to a file when tracing is enabled.");
    Console.WriteLine();
    Console.WriteLine("squad-as-agent and workflow need the AppHost chat connection or SQUAD_AF_PROVIDER configuration.");
    return;
}

var example = GetExample(args);
var (traceEvents, tracePath) = GetTrace(args);

// ── OpenTelemetry ──────────────────────────────────────────────────────────
// When running via Aspire AppHost, OTEL_EXPORTER_OTLP_ENDPOINT is injected
// automatically and all MAF workflow spans (executor runs, edge routing,
// loop iterations) stream to the Aspire dashboard in real time.
//
// Dual-dashboard story:
//   • Aspire dashboard  — .NET MAF workflow spans, DTS task activity
//     (open the URL printed by `dotnet run --project Squad.AgentFramework.Demo.AppHost`)
//   • DTS dashboard     — http://localhost:8082 (task-hub level orchestration view)
//   • Squad CLI traces  — run: $env:OTEL_EXPORTER_OTLP_ENDPOINT="<aspire-otlp-url>"
//                         then: squad run "investigate incident …"
//                         Both the MAF workflow and Squad agent spans appear
//                         in the same Aspire dashboard under their service names.
//
// Copilot SDK native OTel (PR #8):
//   When OTEL_EXPORTER_OTLP_ENDPOINT is set, SquadAgent passes TelemetryConfig
//   to CopilotClientOptions so the CLI server (Node.js) emits its own spans —
//   model invocations, tool calls, sub-agent spawns, token usage — directly to
//   the same OTLP endpoint via the OTel GenAI + MCP semantic conventions.
//   These CLI-originated spans bypass the .NET TracerProvider (they come from the
//   Node.js process), so no extra .AddSource() is needed on the builder below.
//   W3C trace-context propagation is automatic, linking CLI spans to the .NET
//   parent Activity from SquadAgent.Run / SquadAgent.RunStreaming.
//
// OTLP protocol: Aspire uses gRPC (port 4317 / path /opentelemetry.proto.collector.*).
// The Node.js Copilot CLI OTLP SDK defaults to HTTP/protobuf, which Aspire's gRPC-only
// endpoint rejects. AppHost.cs sets OTEL_EXPORTER_OTLP_PROTOCOL=grpc so the Node.js
// child process uses HTTP/2, matching Aspire's expectation.
var otlpEndpoint = Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT");
using var tracerProvider = !string.IsNullOrWhiteSpace(otlpEndpoint)
    ? Sdk.CreateTracerProviderBuilder()
          .AddSource(IncidentExample.DemoActivitySource.Name)
          .AddSource(SquadAgent.SquadAgentActivitySource.Name)
          .AddOtlpExporter()   // reads OTEL_EXPORTER_OTLP_ENDPOINT from env
          .Build()
    : null;

using var meterProvider = !string.IsNullOrWhiteSpace(otlpEndpoint)
    ? Sdk.CreateMeterProviderBuilder()
          .AddMeter(SquadAgent.SquadAgentMeter.Name)
          .AddOtlpExporter()   // reads OTEL_EXPORTER_OTLP_ENDPOINT from env
          .Build()
    : null;

if (tracerProvider is not null)
{
    Console.WriteLine($"[otel] Tracing  → {IncidentExample.DemoActivitySource.Name}");
    Console.WriteLine($"[otel]            {SquadAgent.SquadAgentActivitySource.Name}");
    Console.WriteLine($"[otel] Metrics  → {SquadAgent.SquadAgentMeter.Name}");
    Console.WriteLine($"[otel] Copilot SDK native OTel → CLI server will emit GenAI+MCP spans to {otlpEndpoint}");
}

var builder = Host.CreateApplicationBuilder(args);
var hasAspireChatConnection = !string.IsNullOrWhiteSpace(builder.Configuration.GetConnectionString("chat"));

if (hasAspireChatConnection)
{
    builder.AddAzureChatCompletionsClient(connectionName: "chat")
        .AddChatClient();
}

using var host = hasAspireChatConnection ? builder.Build() : null;
var provider = hasAspireChatConnection
    ? ProviderAgentFactory.FromChatClient(host!.Services.GetRequiredService<IChatClient>())
    : ProviderAgentFactory.FromEnvironment();

await using var squad = DemoRuntime.CreateSquad(
    traceEvents: traceEvents,
    tracePath: tracePath);

object report = example switch
{
    "squad-as-agent" => await SquadAsAgentExample.RunAsync(provider, squad),
    "workflow" => await WorkflowExample.RunAsync(provider, squad.Agent),
    "incident" => await IncidentExample.RunAsync(provider, squad.Agent),
    _ => throw new ArgumentException($"Unknown example '{example}'. Use --list-examples.")
};

Console.WriteLine(JsonSerializer.Serialize(report, DemoRuntime.JsonOptions));

static string GetExample(string[] args)
{
    for (var i = 0; i < args.Length; i++)
    {
        if (args[i] == "--example")
        {
            if (i + 1 >= args.Length)
            {
                throw new ArgumentException("--example requires a value.");
            }

            return args[i + 1];
        }

        if (args[i].StartsWith("--example=", StringComparison.Ordinal))
        {
            return args[i]["--example=".Length..];
        }
    }

    var fromEnvironment = Environment.GetEnvironmentVariable("SQUAD_AF_EXAMPLE");
    return string.IsNullOrWhiteSpace(fromEnvironment) ? "squad-as-agent" : fromEnvironment.Trim();
}

static (bool TraceEvents, string? TracePath) GetTrace(string[] args)
{
    for (var i = 0; i < args.Length; i++)
    {
        if (args[i] == "--trace")
        {
            if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
            {
                return (true, args[i + 1]);
            }

            return (true, null);
        }

        if (args[i].StartsWith("--trace=", StringComparison.Ordinal))
        {
            var path = args[i]["--trace=".Length..];
            return (true, string.IsNullOrWhiteSpace(path) ? null : path);
        }
    }

    var tracePath = NormalizeTracePath(Environment.GetEnvironmentVariable("SQUAD_AF_TRACE_PATH"));
    var traceEnabled = IsTruthy(Environment.GetEnvironmentVariable("SQUAD_AF_TRACE")) || tracePath is not null;
    return (traceEnabled, tracePath);
}

static string? NormalizeTracePath(string? path) =>
    string.IsNullOrWhiteSpace(path) ? null : path.Trim();

static bool IsTruthy(string? value) =>
    !string.IsNullOrWhiteSpace(value)
    && !string.Equals(value, "0", StringComparison.OrdinalIgnoreCase)
    && !string.Equals(value, "false", StringComparison.OrdinalIgnoreCase)
    && !string.Equals(value, "no", StringComparison.OrdinalIgnoreCase);
