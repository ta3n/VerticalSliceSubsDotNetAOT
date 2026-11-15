using System.Net;
using SubscriptionManagement.Contracts;
using SubscriptionManagement.Relay.Infrastructure;

namespace SubscriptionManagement.Relay.Features.Invoices.Settle;

public static class SettleInvoiceHandler
{
    /// <summary>
    /// Asks the API to mark the saga's invoice paid and answers the saga with what the API said. A repeated delivery
    /// is harmless: the API settles the same payment twice as a no-op. Any other status (and no answer at all)
    /// throws out of <see cref="ApiClient"/>, so the command is retried rather than answered.
    /// </summary>
    public static async Task<object> Handle(
        SettleInvoice command,
        ApiClient api,
        CancellationToken ct
    )
    {
        return await api.SettleInvoiceAsync(command.RenewalSagaId, command.PaymentId, ct) switch
        {
            HttpStatusCode.OK => new InvoiceSettled(command.RenewalSagaId),
            HttpStatusCode.NotFound => new InvoiceSettlementRejected(command.RenewalSagaId, "invoice-not-found"),
            // The invoice is no longer open for this payment: voided, or paid by other means.
            _ => new InvoiceSettlementRejected(command.RenewalSagaId, "invoice-not-payable")
        };
    }
}