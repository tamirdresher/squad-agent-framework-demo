using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;

/// <summary>
/// Diagnose: Squad reviews the full accumulated context and decides:
/// <list type="bullet">
///   <item><c>Resolved</c> — workflow terminates</item>
///   <item><c>NeedsMoreInvestigation</c> — loop-back edge fires; returns to EnrichExecutor
///         with a refined hypothesis (capped by <see cref="IncidentExample.MaxDiagnosisIterations"/>)</item>
///   <item><c>Inconclusive</c> — max iterations reached; workflow terminates with partial result</item>
/// </list>
/// </summary>
internal sealed partial class DiagnoseExecutor(AIAgent squad)
    : Executor<IncidentWorkflowContext, IncidentWorkflowContext>("Diagnose")
{
    public override async ValueTask<IncidentWorkflowContext> HandleAsync(
        IncidentWorkflowContext ctx,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        var iteration = ctx.DiagnosisIteration + 1;
        Console.WriteLine(
            $"[Diagnose] Squad diagnosis iteration {iteration}/{IncidentExample.MaxDiagnosisIterations}...");

        var finalNote = iteration >= IncidentExample.MaxDiagnosisIterations
            ? "\nThis is the FINAL allowed iteration — you MUST reach a conclusion now."
            : string.Empty;

        var prompt = $"""
            You are the incident-response Squad analyst. Review all available context
            and determine whether the incident is resolved or needs further investigation.

            == Incident ==
            Title: {ctx.Report.Title}
            Region: {ctx.Report.Region}
            Customer tier: {ctx.Customer?.Tier ?? "unknown"}  SLA: {ctx.Customer?.Sla ?? "unknown"}

            == Triage ==
            Severity: {ctx.Severity}
            Subsystem: {ctx.Subsystem}
            Hypothesis: {ctx.Hypothesis}

            == Enrichment ==
            Error rate : {ctx.Metrics?.ErrorRatePercent:0.0}%
            p99 latency: {ctx.Metrics?.P99LatencyMs:0} ms
            Top errors  : {string.Join("; ", ctx.Metrics?.TopErrors ?? [])}
            Recent alerts: {string.Join("; ", ctx.RecentAlerts ?? [])}

            == External comms ==
            {ctx.ExternalCommsResult ?? "(none)"}

            == Subsystem-squad analysis ==
            {ctx.SubsystemAnalysis ?? "(none)"}

            == Mitigation applied ==
            {(ctx.MitigationApplied ? ctx.RecommendedMitigation : "(none)")}

            Iteration {iteration} of {IncidentExample.MaxDiagnosisIterations}.{finalNote}

            Respond using EXACTLY this format:
            RootCause: <one sentence>
            Status: <Resolved|NeedsMoreInvestigation>
            RefinedHypothesis: <updated hypothesis for next iteration, or "(none)" if Resolved>
            """;

        var response = await DemoRuntime.RunAgentAsync(squad, prompt, cancellationToken);
        return ParseDiagnosis(ctx, response, iteration);
    }

    private static IncidentWorkflowContext ParseDiagnosis(
        IncidentWorkflowContext ctx,
        string text,
        int iteration)
    {
        var rootCause = ExtractScalar(text, "RootCause",
            "Database connection pool exhaustion (config change pool max=50) " +
            "combined with concurrent external payment gateway degradation in EU-WEST.");

        var rawStatus = ExtractScalar(text, "Status", "Resolved");
        // Enforce termination at max iterations regardless of LLM output.
        var status = iteration >= IncidentExample.MaxDiagnosisIterations
            ? "Inconclusive"
            : rawStatus.Equals("NeedsMoreInvestigation", StringComparison.OrdinalIgnoreCase)
                ? "NeedsMoreInvestigation"
                : "Resolved";

        var refinedHypothesis = status == "NeedsMoreInvestigation"
            ? ExtractScalar(text, "RefinedHypothesis", ctx.Hypothesis ?? string.Empty)
            : ctx.Hypothesis;

        Console.WriteLine($"[Diagnose] Status={status}");
        return ctx with
        {
            RootCause          = rootCause,
            DiagnosisStatus    = status,
            DiagnosisIteration = iteration,
            Hypothesis         = refinedHypothesis  // used by loop-back EnrichExecutor
        };
    }

    private static string ExtractScalar(string text, string label, string fallback)
    {
        foreach (var line in text.Split('\n'))
        {
            var t = line.Trim();
            if (t.StartsWith(label + ":", StringComparison.OrdinalIgnoreCase))
            {
                var v = t[(label.Length + 1)..].Trim();
                return string.IsNullOrWhiteSpace(v) ? fallback : v;
            }
        }
        return fallback;
    }
}
