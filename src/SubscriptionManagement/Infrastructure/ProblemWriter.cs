using System.Text.Json;

namespace SubscriptionManagement.Infrastructure;

/// <summary>
/// Writes RFC 9457 <c>application/problem+json</c> by hand with <see cref="Utf8JsonWriter"/>, so no
/// reflection-based serialization of <c>object</c> extension values is needed (Native AOT safe).
/// Adds the common members <c>instance</c>, <c>timestamp</c> and <c>traceId</c> to every problem.
/// </summary>
public static class ProblemWriter
{
    private const string TypeBase = "https://api.demo/errors/";

    private static readonly HashSet<string> Reserved =
        new(["type", "title", "status", "detail", "instance", "timestamp", "traceId", "errors"], StringComparer.Ordinal);

    public static Task WriteAsync(HttpContext context, ApiProblemException problem) =>
        WriteAsync(context, problem.Status, problem.Slug, problem.Title, problem.Detail, problem.Extensions, problem.Errors);

    public static async Task WriteAsync(
        HttpContext context,
        int status,
        string slug,
        string title,
        string? detail,
        IReadOnlyDictionary<string, object>? extensions = null,
        IReadOnlyDictionary<string, string[]>? errors = null)
    {
        var response = context.Response;
        response.StatusCode = status;
        response.ContentType = "application/problem+json";

        var timeProvider = context.RequestServices.GetService<TimeProvider>() ?? TimeProvider.System;
        await using (var writer = new Utf8JsonWriter(response.BodyWriter))
        {
            WriteBody(writer, context, timeProvider, status, slug, title, detail, extensions, errors);
        }

        await response.BodyWriter.FlushAsync();
    }

    private static void WriteBody(
        Utf8JsonWriter writer,
        HttpContext context,
        TimeProvider timeProvider,
        int status,
        string slug,
        string title,
        string? detail,
        IReadOnlyDictionary<string, object>? extensions,
        IReadOnlyDictionary<string, string[]>? errors)
    {
        writer.WriteStartObject();
        writer.WriteString("type", TypeBase + slug);
        writer.WriteString("title", title);
        writer.WriteNumber("status", status);
        if (detail is not null)
        {
            writer.WriteString("detail", detail);
        }

        writer.WriteString("instance", context.Request.Path.Value);
        writer.WriteString("timestamp", timeProvider.GetUtcNow().UtcDateTime);
        writer.WriteString("traceId", context.TraceIdentifier);

        if (extensions is not null)
        {
            foreach (var (key, value) in extensions)
            {
                if (Reserved.Contains(key))
                {
                    // RFC 9457 §3.2: extensions must not redefine standard members (would emit duplicate keys).
                    throw new InvalidOperationException($"Problem extension '{key}' collides with a standard member.");
                }

                writer.WritePropertyName(key);
                WriteValue(writer, value);
            }
        }

        if (errors is not null)
        {
            writer.WriteStartObject("errors");
            foreach (var (field, messages) in errors)
            {
                writer.WriteStartArray(field);
                foreach (var message in messages)
                {
                    writer.WriteStringValue(message);
                }

                writer.WriteEndArray();
            }

            writer.WriteEndObject();
        }

        writer.WriteEndObject();
    }

    private static void WriteValue(Utf8JsonWriter writer, object value)
    {
        switch (value)
        {
            case string s: writer.WriteStringValue(s); break;
            case int i: writer.WriteNumberValue(i); break;
            case bool b: writer.WriteBooleanValue(b); break;
            case Guid g: writer.WriteStringValue(g); break;
            default: throw new NotSupportedException($"Unsupported problem extension type {value.GetType()}.");
        }
    }
}
