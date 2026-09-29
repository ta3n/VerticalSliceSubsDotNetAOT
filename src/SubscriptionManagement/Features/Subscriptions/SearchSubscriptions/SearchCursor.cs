using System.Buffers.Text;
using System.Globalization;
using System.Text;

namespace SubscriptionManagement.Features.Subscriptions.SearchSubscriptions;

/// <summary>Opaque keyset-pagination cursor: base64url("yyyy-MM-dd|id"), no padding (same format as the Java reference).</summary>
public static class SearchCursor
{
    public static string Encode(DateOnly startDate, Guid id) =>
        Base64Url.EncodeToString(Encoding.UTF8.GetBytes(
            string.Create(CultureInfo.InvariantCulture, $"{startDate:yyyy-MM-dd}|{id}")));

    public static (DateOnly StartDate, Guid Id) Decode(string cursor)
    {
        try
        {
            var raw = Encoding.UTF8.GetString(Base64Url.DecodeFromChars(cursor));
            var parts = raw.Split('|', 2);
            if (parts.Length == 2
                && DateOnly.TryParseExact(parts[0], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                && Guid.TryParse(parts[1], out var id))
            {
                return (date, id);
            }
        }
        catch (FormatException)
        {
            // fall through to the shared problem below
        }

        throw SubscriptionProblems.InvalidCursor();
    }
}
