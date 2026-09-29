using Microsoft.AspNetCore.Diagnostics;
using Npgsql;
using PostgresErrorCodes = Npgsql.PostgresErrorCodes;

namespace SubscriptionManagement.Infrastructure;

/// <summary>
/// Turns every unhandled exception into a problem+json response. Domain failures carry their own
/// problem; framework binding failures become 400; anything else is an opaque, logged 500.
/// </summary>
public sealed class ApiExceptionHandler(ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        switch (exception)
        {
            case ApiProblemException problem:
                await ProblemWriter.WriteAsync(context, problem);
                return true;

            case BadHttpRequestException bad:
                await ProblemWriter.WriteAsync(context, StatusCodes.Status400BadRequest, "malformed-request",
                    "The request could not be understood.", "The request body or parameters are malformed.");
                logger.LogDebug(bad, "Bad request");
                return true;

            case PostgresException { SqlState: PostgresErrorCodes.NumericValueOutOfRange }:
                await ProblemWriter.WriteAsync(context, StatusCodes.Status400BadRequest, "value-out-of-range",
                    "A numeric value is out of range.", "A numeric value exceeds the supported range.");
                return true;

            case OperationCanceledException when context.RequestAborted.IsCancellationRequested:
                return true; // client went away; nothing to write

            default:
                logger.LogError(exception, "Unhandled exception [traceId={TraceId}]", context.TraceIdentifier);
                await ProblemWriter.WriteAsync(context, StatusCodes.Status500InternalServerError, "internal",
                    "An unexpected error occurred.", null);
                return true;
        }
    }
}
