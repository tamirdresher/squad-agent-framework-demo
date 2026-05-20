using Microsoft.Agents.AI.Workflows;

/// <summary>
/// External communications: simulates reaching out to the customer for additional
/// diagnostic data and querying a third-party operational status page.
/// Uses real async delay to produce realistic DTS dashboard timelines.
/// </summary>
internal sealed class ExternalCommsExecutor()
    : Executor<IncidentWorkflowContext, IncidentWorkflowContext>("ExternalComms")
{
    public override async ValueTask<IncidentWorkflowContext> HandleAsync(
        IncidentWorkflowContext ctx,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        Console.WriteLine("[ExternalComms] Reaching out to customer + querying third-party status...");

        var customerTask = MockCustomerOutreachService.RequestDiagnosticsAsync(
            ctx.Customer!, ctx.Report.Title, cancellationToken);
        var statusTask = MockThirdPartyStatusService.GetStatusAsync(
            "payment-gateway", cancellationToken);

        await Task.WhenAll(customerTask, statusTask);

        var commsResult = $"{await customerTask} | {await statusTask}";
        Console.WriteLine($"[ExternalComms] {commsResult[..Math.Min(120, commsResult.Length)]}...");

        return ctx with { ExternalCommsResult = commsResult };
    }
}
