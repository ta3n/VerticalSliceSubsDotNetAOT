namespace SubscriptionManagement.Infrastructure;

/// <summary>
/// A failure that maps to an RFC 9457 problem response. <see cref="Slug"/> becomes the tail of the
/// <c>type</c> URI; <see cref="Extensions"/> are written as extra members (values: string, int, bool, Guid).
/// </summary>
public sealed class ApiProblemException(
    int status,
    string slug,
    string title,
    string detail,
    IReadOnlyDictionary<string, object>? extensions = null,
    IReadOnlyDictionary<string, string[]>? errors = null) : Exception(detail)
{
    public int Status { get; } = status;
    public string Slug { get; } = slug;
    public string Title { get; } = title;
    public string Detail { get; } = detail;
    public IReadOnlyDictionary<string, object>? Extensions { get; } = extensions;
    public IReadOnlyDictionary<string, string[]>? Errors { get; } = errors;

    public static ApiProblemException Validation(IReadOnlyDictionary<string, string[]> errors) =>
        new(StatusCodes.Status400BadRequest, "validation", "One or more validation errors occurred.",
            "One or more validation errors occurred.", errors: errors);
}
