using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using SubscriptionManagement.Infrastructure;

namespace SubscriptionManagement.UnitTests;

public class ProblemWriterTests
{
    private static DefaultHttpContext NewContext() =>
        new() { RequestServices = new ServiceCollection().BuildServiceProvider() };

    [Theory]
    [InlineData("status")]
    [InlineData("type")]
    [InlineData("traceId")]
    public async Task Extensions_cannot_redefine_standard_members(string key)
    {
        var context = NewContext();
        context.Response.Body = new MemoryStream();
        var problem = new ApiProblemException(409, "x", "t", "d", new Dictionary<string, object> { [key] = "v" });

        await Assert.ThrowsAsync<InvalidOperationException>(() => ProblemWriter.WriteAsync(context, problem));
    }

    [Fact]
    public async Task Writes_the_common_members_extensions_and_errors()
    {
        var context = NewContext();
        context.TraceIdentifier = "trace-1";
        context.Request.Path = "/subscriptions";
        context.Response.Body = new MemoryStream();
        var problem = new ApiProblemException(400, "validation", "Bad", "Detail",
            new Dictionary<string, object> { ["n"] = 3, ["ok"] = true, ["id"] = Guid.Empty },
            new Dictionary<string, string[]> { ["price"] = ["must be greater than 0"] });

        await ProblemWriter.WriteAsync(context, problem);

        context.Response.Body.Position = 0;
        using var json = await System.Text.Json.JsonDocument.ParseAsync(context.Response.Body);
        var root = json.RootElement;
        Assert.Equal("https://api.demo/errors/validation", root.GetProperty("type").GetString());
        Assert.Equal(400, root.GetProperty("status").GetInt32());
        Assert.Equal("/subscriptions", root.GetProperty("instance").GetString());
        Assert.Equal("trace-1", root.GetProperty("traceId").GetString());
        Assert.Equal(3, root.GetProperty("n").GetInt32());
        Assert.Equal("must be greater than 0", root.GetProperty("errors").GetProperty("price")[0].GetString());
        Assert.Equal("application/problem+json", context.Response.ContentType);
    }
}
