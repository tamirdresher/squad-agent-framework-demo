# Squad as an Agent Framework Agent

> **Companion demo for the ["Make It So — But Let the Computer Handle the Math"](https://tamirdresher.com/2026/05/20/deterministic-meets-squads/) blog post.**
> Composes Durable Task Scheduler (DTS) with non-deterministic AI agents from Microsoft Agent Framework (MAF).

## Where to Start Reading

Arriving from the blog post? Here are the most interesting files:

| File | What it shows |
|------|--------------|
| [`src/Demo/Workflows/IncidentExample.cs`](src/Demo/Workflows/IncidentExample.cs) | **Start here.** The durable workflow that orchestrates the incident-triage scenario — AI triage → deterministic enrichment → dynamic subsystem routing → mitigation → diagnose-loop. This is the deterministic backbone. |
| [`src/Demo/Agents/SquadAgent.cs`](src/Demo/Agents/SquadAgent.cs) | The MAF `AIAgent` wrapper around the GitHub Copilot CLI. This is where deterministic workflows meet the non-deterministic Squad, with OTel spans at both layers. |
| [`src/Demo/Subsystems/`](src/Demo/Subsystems/) | The four subsystem-specific Squads (Database, Network, Auth, Payments) that the workflow routes to dynamically based on the AI triage result. |
| [`src/Demo/Workflows/Executors/`](src/Demo/Workflows/Executors/) | Individual workflow executors: `TriageExecutor`, `EnrichExecutor`, `ExternalCommsExecutor`, `MitigateExecutor`, `DiagnoseExecutor`. |
| [`src/AppHost/AppHost.cs`](src/AppHost/AppHost.cs) | The Aspire host that wires DTS (via Docker), the Squad demo project, and the OTLP endpoint together. |

## Run via Aspire AppHost (Recommended)

```powershell
cd src\AppHost
dotnet run
```

This brings up the Aspire dashboard. AppHost orchestrates Foundry Local, creates the `chat` deployment, starts the DTS emulator, and wires everything into the demo project automatically — **no `SQUAD_AF_*` environment variables required**.

**First run:** downloads `phi-3.5-mini` (~5 GB). Wait for the `chat` resource to show **Healthy** in the Aspire dashboard before expecting the console child to start.

**Optional — pre-select an example before starting AppHost:**

```powershell
$env:SQUAD_AF_EXAMPLE = "incident"   # or: squad-as-agent, workflow
$env:SQUAD_AF_TRACE   = "true"
cd src\AppHost
dotnet run
```

## Repository Layout

```
squad-agent-framework-demo/
├── src/
│   ├── AppHost/                          # Aspire host — wires DTS, Foundry Local, OTLP
│   │   ├── AppHost.cs
│   │   └── Squad.AgentFramework.Demo.AppHost.csproj
│   └── Demo/                             # The demo workflow project
│       ├── Program.cs                    # Entry point — example selector
│       ├── Agents/
│       │   └── SquadAgent.cs             # MAF AIAgent wrapping GitHubCopilotAgent
│       ├── Workflows/
│       │   ├── IncidentExample.cs        # Durable workflow graph (9 executors)
│       │   ├── IncidentEvidence.cs       # Synthetic incident report factory
│       │   └── Executors/               # One executor per file
│       │       ├── TriageExecutor.cs
│       │       ├── EnrichExecutor.cs
│       │       ├── ExternalCommsExecutor.cs
│       │       ├── MitigateExecutor.cs
│       │       └── DiagnoseExecutor.cs
│       ├── Subsystems/                   # Subsystem-specific squad executors
│       │   ├── DatabaseSquadExecutor.cs
│       │   ├── NetworkSquadExecutor.cs
│       │   ├── AuthSquadExecutor.cs
│       │   ├── PaymentsSquadExecutor.cs
│       │   └── SubsystemSquadHelpers.cs
│       ├── Examples/                     # Two simpler examples (non-incident)
│       │   ├── SquadAsAgentExample.cs
│       │   └── WorkflowExample.cs
│       ├── Models/                       # Shared record types
│       │   ├── IncidentReport.cs
│       │   ├── IncidentWorkflowContext.cs
│       │   ├── CustomerInfo.cs
│       │   ├── MetricsWindow.cs
│       │   └── IncidentOrchestrationReport.cs
│       ├── Infrastructure/               # Runtime helpers and provider factory
│       │   ├── DemoRuntime.cs
│       │   └── ProviderAgentFactory.cs
│       └── MockServices/                 # In-process mock services (no real network calls)
│           ├── MockCustomerService.cs
│           ├── MockMetricsService.cs
│           ├── MockAlertCorrelationService.cs
│           ├── MockCustomerOutreachService.cs
│           └── MockThirdPartyStatusService.cs
├── squad-agent-framework.slnx
├── README.md
└── LICENSE
```

## Build

```powershell
dotnet restore .\squad-agent-framework.slnx
dotnet build .\squad-agent-framework.slnx --no-restore
```

## Examples

Available examples (select at the console prompt or pre-set `SQUAD_AF_EXAMPLE`):

- **`incident`** — Durable incident-response workflow: AI triage → enrichment → external comms → dynamic subsystem-squad routing (DB/Net/Auth/Pay) → mitigation → diagnose-loop. DTS-backed. **This is the blog post demo.**
- **`squad-as-agent`** — Planner → Squad → reviewer flow showing SquadAgent as a plain MAF participant.
- **`workflow`** — Writer (Foundry Local) → Squad sequential MAF workflow.

## Tracing

When running via AppHost, set `SQUAD_AF_TRACE` in your shell before starting to stream Copilot SDK session events:

```powershell
$env:SQUAD_AF_TRACE = "true"
cd src\AppHost
dotnet run
```

Provide a file path to write JSONL events:

```powershell
$env:SQUAD_AF_TRACE = "copilot-events.jsonl"
```

For standalone runs (without AppHost), use the `--trace` flag:

```powershell
dotnet run --project .\src\Demo -- --example incident --trace
dotnet run --project .\src\Demo -- --example workflow --trace=copilot-events.jsonl
```

## GitHub Copilot Requirement

Every sample uses the real Copilot-backed `SquadAgent`. The first run requires the GitHub Copilot SDK CLI to be available and may require interactive GitHub Copilot login. There is no scripted fallback.

## Standalone (Advanced): Run without Aspire AppHost

**Skip this section if using the AppHost (recommended).** For advanced scenarios running directly against your own provider.

```powershell
# Azure OpenAI
$env:SQUAD_AF_PROVIDER = "azure-openai"
$env:SQUAD_AF_ENDPOINT = "https://<resource>.openai.azure.com/"
$env:SQUAD_AF_DEPLOYMENT = "<deployment>"
dotnet run --project .\src\Demo -- --example squad-as-agent

# OpenAI-compatible endpoint (e.g. Ollama)
$env:SQUAD_AF_PROVIDER = "openai-compatible"
$env:SQUAD_AF_ENDPOINT = "http://localhost:11434/v1"
$env:SQUAD_AF_MODEL = "<model>"
dotnet run --project .\src\Demo -- --example workflow

# Foundry Local standalone
$env:SQUAD_AF_PROVIDER = "foundry-local"
$env:SQUAD_AF_ENDPOINT = "http://localhost:<foundry-port>/v1"
$env:SQUAD_AF_MODEL = "Phi-3.5-mini-instruct-cuda-gpu"
dotnet run --project .\src\Demo -- --example workflow
```

## License

MIT © 2026 Tamir Dresher. See [LICENSE](LICENSE).

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md).
