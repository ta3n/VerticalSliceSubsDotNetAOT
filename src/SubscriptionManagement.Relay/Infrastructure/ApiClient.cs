using System.Net;

namespace SubscriptionManagement.Relay.Infrastructure;

/// <summary>
/// The API endpoints the Relay calls on behalf of the other services (base address: <c>Api:BaseUrl</c>). No service
/// but the API writes the subscriptions tables, so its handlers, row locks and cache stay the single write path.
/// </summary>
/// <remarks>
/// Each call returns the status when it is an answer about the resource (200, 404 or 409) and throws
/// <see cref="HttpRequestException"/> for anything else, which is what Wolverine's error policy retries.
/// </remarks>
public sealed class ApiClient(
    HttpClient http
)
{
    public Task<HttpStatusCode> SettleInvoiceAsync(
        Guid invoiceId,
        Guid paymentId,
        CancellationToken ct
    )
    {
        return PostAsync($"/invoices/{invoiceId}/settle", JsonContent.Create(new SettleInvoiceRequest(paymentId)), ct);
    }

    public Task<HttpStatusCode> VoidInvoiceAsync(
        Guid invoiceId,
        CancellationToken ct
    )
    {
        return PostAsync($"/invoices/{invoiceId}/void", null, ct);
    }

    public Task<HttpStatusCode> PauseSubscriptionAsync(
        Guid subscriptionId,
        CancellationToken ct
    )
    {
        return PostAsync($"/subscriptions/{subscriptionId}/pause", null, ct);
    }

    private async Task<HttpStatusCode> PostAsync(
        string path,
        HttpContent? content,
        CancellationToken ct
    )
    {
        using var response = await http.PostAsync(path, content, ct);
        return response.StatusCode switch
        {
            HttpStatusCode.OK or HttpStatusCode.NotFound or HttpStatusCode.Conflict => response.StatusCode,
            var status => throw new HttpRequestException($"POST {path} answered {(int)status}.", null, status)
        };
    }

    /// <summary>Body of <c>POST /invoices/{id}/settle</c>; serialized camelCase (<c>paymentId</c>).</summary>
    private sealed record SettleInvoiceRequest(
        Guid PaymentId
    );
}