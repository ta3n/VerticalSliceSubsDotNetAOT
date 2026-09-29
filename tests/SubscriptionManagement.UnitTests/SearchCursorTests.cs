using System.Buffers.Text;
using System.Text;
using SubscriptionManagement.Features.Subscriptions.SearchSubscriptions;
using SubscriptionManagement.Infrastructure;

namespace SubscriptionManagement.UnitTests;

public class SearchCursorTests
{
    [Fact]
    public void Round_trips()
    {
        var id = Guid.NewGuid();
        var cursor = SearchCursor.Encode(new DateOnly(2026, 9, 5), id);

        Assert.Equal((new DateOnly(2026, 9, 5), id), SearchCursor.Decode(cursor));
        Assert.DoesNotContain('=', cursor);
    }

    [Fact]
    public void Matches_the_java_reference_format()
    {
        // base64url("2026-09-05|<uuid>") without padding
        var id = Guid.Parse("11111111-2222-3333-4444-555555555555");
        var expected = Base64Url.EncodeToString(Encoding.UTF8.GetBytes("2026-09-05|11111111-2222-3333-4444-555555555555"));
        Assert.Equal(expected, SearchCursor.Encode(new DateOnly(2026, 9, 5), id));
    }

    [Theory]
    [InlineData("not base64 !!")]
    [InlineData("bm9waXBl")]                          // "nopipe"
    [InlineData("MjAyNi0xMy0wMXxhYmM")]               // bad date and uuid
    public void Garbage_is_an_invalid_cursor(string cursor)
    {
        var ex = Assert.Throws<ApiProblemException>(() => SearchCursor.Decode(cursor));
        Assert.Equal(400, ex.Status);
        Assert.Equal("invalid-cursor", ex.Slug);
    }

    [Fact]
    public void Valid_base64_with_a_bad_uuid_is_rejected()
    {
        var cursor = Base64Url.EncodeToString(Encoding.UTF8.GetBytes("2026-09-05|not-a-guid"));
        Assert.Equal("invalid-cursor", Assert.Throws<ApiProblemException>(() => SearchCursor.Decode(cursor)).Slug);
    }
}
