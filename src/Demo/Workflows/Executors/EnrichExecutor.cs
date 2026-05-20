using Microsoft.Agents.AI.Workflows;

/// <summary>
/// Deterministic enrichment: fetches customer tier/SLA, metrics window, and
/// correlated alerts.  Pure C#, no LLM.  Runs on initial entry AND on every
/// loop-back from DiagnoseExecutor to incorporate the refined hypothesis.
/// </summary>
internal sealed class EnrichExecutor()
    : Executor<IncidentWorkflowContext, IncidentWorkflowContext>("Enrich")
{
    public override async ValueTask<IncidentWorkflowContext> HandleAsync(
        IncidentWorkflowContext ctx,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        Console.WriteLine(
            $"[Enrich] Fetching customer/metrics/alerts (iter={ctx.DiagnosisIteration})...");

        var customerTask = MockCustomerService.GetCustomerAsync(ctx.Report.CustomerId, cancellationToken);
        var metricsTask  = MockMetricsService.GetMetricsWindowAsync(ctx.Report.Region, cancellationToken);
        var alertsTask   = MockAlertCorrelationService.GetRecentAlertsAsync(ctx.Report.Region, cancellationToken);

        await Task.WhenAll(customerTask, metricsTask, alertsTask);

        var customer = await customerTask;
        var metrics  = await metricsTask;
        var alerts   = await alertsTask;

        Console.WriteLine(
            $"[Enrich] tier={customer.Tier}  sla={customer.Sla}  " +
            $"errorRate={metrics.ErrorRatePercent:0.0}%  p99={metrics.P99LatencyMs:0}ms");

        return ctx with
        {
            Customer     = customer,
            Metrics      = metrics,
            RecentAlerts = alerts
        };
    }
}
