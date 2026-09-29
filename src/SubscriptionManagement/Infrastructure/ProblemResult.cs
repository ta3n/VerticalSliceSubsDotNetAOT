namespace SubscriptionManagement.Infrastructure;

/// <summary>An <see cref="IResult"/> that renders an <see cref="ApiProblemException"/> without throwing it.</summary>
public sealed class ProblemResult(ApiProblemException problem) : IResult
{
    public Task ExecuteAsync(HttpContext httpContext) => ProblemWriter.WriteAsync(httpContext, problem);
}
