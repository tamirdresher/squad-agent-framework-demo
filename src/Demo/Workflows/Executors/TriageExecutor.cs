using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;

/// <summary>
/// AI triage: uses Squad to classify severity, identify the affected subsystem,
/// and generate the initial hypothesis and required evidence list.
/// </summary>
internal sealed class TriageExecutor(AIAgent squad)
    : Executor<IncidentWorkflowContext, IncidentWorkflowContext>("Triage")
{
    public override async ValueTask<IncidentWorkflowContext> HandleAsync(
        IncidentWorkflowContext ctx,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        Console.WriteLine("[Triage] Running AI triage...");

        var refinedNote = ctx.DiagnosisIteration > 0
            ? $"\nRefined hypothesis from previous iteration: {ctx.Hypothesis}"
            : string.Empty;

        var prompt = $"""
            Analyze the following incident report and triage it.

            Title: {ctx.Report.Title}
            Region: {ctx.Report.Region}
            Customer: {ctx.Report.CustomerId}

            Description:
            {ctx.Report.Description}
            {refinedNote}

            Respond using EXACTLY this format (no extra text):
            Severity: <Sev1|Sev2|Sev3>
            Subsystem: <Database|Network|Authentication|Payments>
            Hypothesis: <one sentence>
            RequiredEvidence: <comma-separated list of 3-5 evidence items to collect>
            """;

        var response = await DemoRuntime.RunAgentAsync(squad, prompt, cancellationToken);
        return ParseTriage(ctx, response);
    }

    private static IncidentWorkflowContext ParseTriage(IncidentWorkflowContext ctx, string text)
    {
        var severity   = ExtractScalar(text, "Severity",  "Sev2");
        var subsystem  = ExtractScalar(text, "Subsystem", "Payments");
        var hypothesis = ExtractScalar(text, "Hypothesis",
            "Database connection pool exhaustion triggered by config change, " +
            "amplified by concurrent external payment gateway degradation.");
        var evidenceLine = ExtractScalar(text, "RequiredEvidence", string.Empty);
        var evidence = string.IsNullOrWhiteSpace(evidenceLine)
            ? ["connection-pool-metrics", "payment-gateway-status", "slow-query-log", "error-rate-trend"]
            : evidenceLine.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        Console.WriteLine($"[Triage] Severity={severity}  Subsystem={subsystem}");
        return ctx with
        {
            Severity         = severity,
            Subsystem        = subsystem,
            Hypothesis       = hypothesis,
            RequiredEvidence = evidence
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
