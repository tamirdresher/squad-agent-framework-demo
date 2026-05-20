/// <summary>
/// Simulates reaching out to a customer to gather additional diagnostic
/// information (e.g., reproduction steps, client SDK version).
/// </summary>
internal static class MockCustomerOutreachService
{
    public static async Task<string> RequestDiagnosticsAsync(
        CustomerInfo customer,
        string incidentTitle,
        CancellationToken cancellationToken = default)
    {
        // Simulates sending a comms request and waiting for an async reply.
        await Task.Delay(200, cancellationToken);
        return
            $"[CustomerComms] Reached {customer.PrimaryContact} for '{incidentTitle}'. " +
            $"Customer confirmed: error started ~14:20 UTC, " +
            $"affects checkout only (cart and browse are fine). " +
            $"They are using payment-gateway SDK v3.5 on their integration side as well.";
    }
}
