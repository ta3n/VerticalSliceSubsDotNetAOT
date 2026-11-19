using System.Net;
using SubscriptionManagement.Contracts;
using SubscriptionManagement.Relay.Infrastructure;

namespace SubscriptionManagement.Relay.Features.Renewals.Cancel;

public static class CancelRenewalHandler
{
    /// <summary>
    /// Compensation of a renewal that was not collected: voids its invoice, then pauses the subscription. There is no
    /// reply, and a repeated delivery ends the same way (the API voids a void invoice as a no-op).
    /// </summary>
    public static async Task Handle(
        CancelRenewal command,
        ApiClient api,
        CancellationToken ct
    )
    {
        // 409: the invoice was paid by other means, so the subscription keeps running. 404: there is nothing to give up on.
        if (await api.VoidInvoiceAsync(command.InvoiceId, ct) != HttpStatusCode.OK)
        {
            return;
        }

        // 404 and 409 (already paused or canceled) leave nothing to pause.
        await api.PauseSubscriptionAsync(command.SubscriptionId, ct);
    }
}