using System.Diagnostics;

namespace SubscriptionManagement.Infrastructure;

/// <summary>
/// Honours an inbound <c>X-Trace-Id</c> (if it is a short printable token), otherwise generates one,
/// exposes it as <see cref="HttpContext.TraceIdentifier"/>, echoes it on the response and adds it to the log scope.
/// </summary>
public sealed class CorrelationMiddleware(RequestDelegate next, ILogger<CorrelationMiddleware> logger)
{
    public const string HeaderName = "X-Trace-Id";
    private const int MaxLength = 128;

    public async Task InvokeAsync(HttpContext context)
    {
        var inbound = context.Request.Headers[HeaderName].FirstOrDefault();
        var traceId = IsAcceptable(inbound) ? inbound! : Guid.NewGuid().ToString();

        context.TraceIdentifier = traceId;
        // OnStarting, not an eager set: UseExceptionHandler clears the response (headers included) before it
        // renders the problem, and error responses are exactly where the caller needs the id.
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = traceId;
            return Task.CompletedTask;
        });
        Activity.Current?.SetTag("correlation.id", traceId);

        using (logger.BeginScope(new Dictionary<string, object> { ["traceId"] = traceId }))
        {
            await next(context);
        }
    }

    // Rejecting control characters/oversize values prevents header/log injection.
    private static bool IsAcceptable(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= MaxLength && value.All(c => c is > ' ' and < (char)127);
}
