/// <summary>
/// Single flowing context object carried through every executor in the
/// durable incident-response workflow.  All executors read this, add their
/// piece (using <c>with</c> expressions), and return the enriched value.
/// </summary>
internal sealed record IncidentWorkflowContext(
    IncidentReport Report,
    // ── triage (AI) ──────────────────────────────────────────────────────
    string? Severity               = null,   // Sev1 / Sev2 / Sev3
    string? Subsystem              = null,   // Database / Network / Authentication / Payments
    string? Hypothesis             = null,
    string[]? RequiredEvidence     = null,
    // ── deterministic enrichment ─────────────────────────────────────────
    CustomerInfo? Customer         = null,
    MetricsWindow? Metrics         = null,
    string[]? RecentAlerts         = null,
    // ── external comms (mock HTTP) ───────────────────────────────────────
    string? ExternalCommsResult    = null,
    // ── subsystem squad analysis (AI, per-subsystem system prompt) ───────
    string? SubsystemAnalysis      = null,
    // ── mitigation (deterministic) ───────────────────────────────────────
    string? RecommendedMitigation  = null,
    bool MitigationApplied         = false,
    string? RunbookTriggered       = null,
    // ── diagnosis loop (AI) ──────────────────────────────────────────────
    string? RootCause              = null,
    string DiagnosisStatus         = "Pending",   // Pending | Resolved | NeedsMoreInvestigation | Inconclusive
    int DiagnosisIteration         = 0);
