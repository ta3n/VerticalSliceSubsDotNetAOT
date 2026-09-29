using Npgsql;
using SubscriptionManagement.Infrastructure;

namespace SubscriptionManagement.Features.Subscriptions.SearchSubscriptions;

/// <summary>The POST /subscriptions/search body. All members are optional; an empty body is the first unfiltered page.</summary>
public sealed record SearchSubscriptionsQuery(string? Status, int? Limit, string? Cursor);

public sealed record SearchSubscriptionsResult(IReadOnlyList<SubscriptionResponse> Items, string? NextCursor);

public static class SearchSubscriptionsHandler
{
    public const int DefaultLimit = 20;
    public const int MaxLimit = 100;

    public static async Task<SearchSubscriptionsResult> Handle(
        SearchSubscriptionsQuery query,
        NpgsqlDataSource db,
        CancellationToken ct)
    {
        SubscriptionStatus? status = null;
        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            status = SubscriptionStatusNames.TryParse(query.Status, out var parsed)
                ? parsed
                : throw SubscriptionProblems.InvalidStatusFilter(query.Status);
        }

        (DateOnly, Guid)? after = null;
        if (!string.IsNullOrWhiteSpace(query.Cursor))
        {
            after = SearchCursor.Decode(query.Cursor);
        }

        // Out-of-range limits fall back to the default rather than failing (reference behaviour).
        var limit = query.Limit is > 0 and <= MaxLimit ? query.Limit.Value : DefaultLimit;

        // Ask for one extra row to learn whether another page exists.
        var rows = await db.WithConnectionAsync(
            (connection, token) => SubscriptionStore.SearchAsync(connection, status, after, limit + 1, token), ct);

        string? nextCursor = null;
        if (rows.Count > limit)
        {
            var last = rows[limit - 1];
            nextCursor = SearchCursor.Encode(last.StartDate, last.Id);
            rows = rows[..limit];
        }

        return new SearchSubscriptionsResult(rows.Select(SubscriptionResponse.From).ToList(), nextCursor);
    }
}
