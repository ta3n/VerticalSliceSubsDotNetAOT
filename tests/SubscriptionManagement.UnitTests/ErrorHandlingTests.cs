using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SubscriptionManagement.Features.Invoices;
using SubscriptionManagement.Features.Plans.CreatePlan;
using SubscriptionManagement.Features.Subscriptions;
using SubscriptionManagement.Infrastructure;

namespace SubscriptionManagement.UnitTests;

/// <summary>Failure paths: the exception handler, problem rendering, cache outages and invalid persisted values.</summary>
public class ErrorHandlingTests
{
  private static DefaultHttpContext NewContext()
  {
    var context = new DefaultHttpContext { RequestServices = new ServiceCollection().BuildServiceProvider() };
    context.Response.Body = new MemoryStream();
    return context;
  }

  private static async Task<JsonElement> BodyAsync(
    HttpContext context
  )
  {
    context.Response.Body.Position = 0;
    using var json = await JsonDocument.ParseAsync(context.Response.Body);
    return json.RootElement.Clone();
  }

  private static ApiExceptionHandler NewHandler()
  {
    return new ApiExceptionHandler(NullLogger<ApiExceptionHandler>.Instance);
  }

  [Fact]
  public async Task Unexpected_exceptions_become_an_opaque_500()
  {
    var context = NewContext();

    var handled = await NewHandler()
      .TryHandleAsync(context, new InvalidOperationException("secret detail"), CancellationToken.None);

    Assert.True(handled);
    Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);
    var body = await BodyAsync(context);
    Assert.Equal("https://api.demo/errors/internal", body.GetProperty("type").GetString());
    Assert.DoesNotContain("secret detail", body.GetRawText());
  }

  [Fact]
  public async Task A_client_that_went_away_gets_no_response_body()
  {
    var context = NewContext();
    context.RequestAborted = new CancellationToken(true);

    var handled =
      await NewHandler().TryHandleAsync(context, new OperationCanceledException(), CancellationToken.None);

    Assert.True(handled);
    Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
    Assert.Equal(0, context.Response.Body.Length);
  }

  [Fact]
  public async Task A_cancellation_the_client_did_not_ask_for_is_an_internal_error()
  {
    var context = NewContext();

    await NewHandler().TryHandleAsync(context, new OperationCanceledException(), CancellationToken.None);

    Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);
  }

  [Fact]
  public async Task Problem_result_renders_the_problem_without_throwing()
  {
    var context = NewContext();
    var result =
      new ProblemResult(new ApiProblemException(404, "plan-not-found", "Plan not found", "No plan 'x'."));

    await result.ExecuteAsync(context);

    Assert.Equal(404, context.Response.StatusCode);
    Assert.Equal(
      "https://api.demo/errors/plan-not-found",
      (await BodyAsync(context)).GetProperty("type").GetString()
    );
  }

  [Fact]
  public async Task Unsupported_extension_values_are_rejected()
  {
    var context = NewContext();
    var problem = new ApiProblemException(400, "x", "t", "d", new Dictionary<string, object> { ["amount"] = 1.5m });

    await Assert.ThrowsAsync<NotSupportedException>(() => ProblemWriter.WriteAsync(context, problem));
  }

  [Fact]
  public void Unknown_status_values_fail_loudly()
  {
    Assert.Throws<ArgumentOutOfRangeException>(() => ((SubscriptionStatus)99).ToDb());
    Assert.Throws<InvalidOperationException>(() => SubscriptionStatusNames.FromDb("DELETED"));
    Assert.Throws<ArgumentOutOfRangeException>(() => ((InvoiceStatus)99).ToDb());
    Assert.Throws<InvalidOperationException>(() => InvoiceStatusNames.FromDb("REFUNDED"));
  }

  [Theory]
  [InlineData("currency", null)]
  [InlineData("currency", " ")]
  [InlineData("interval", null)]
  [InlineData("interval", "")]
  public void Plan_blank_currency_or_interval_is_rejected(
    string field,
    string? value
  )
  {
    var command = new CreatePlanCommand("pro", "Pro", 12m, "USD", "MONTH", 14);
    command = field == "currency" ? command with { Currency = value } : command with { Interval = value };

    var problem = Assert.Throws<ApiProblemException>(() => CreatePlanValidator.Validate(command));

    Assert.Equal(400, problem.Status);
  }

  [Fact]
  public async Task Cache_outages_degrade_to_misses_but_cancellation_still_propagates()
  {
    var cache = new SubscriptionCache(
      new BrokenCache(),
      Options.Create(new CacheOptions()),
      NullLogger<SubscriptionCache>.Instance
    );
    var id = Guid.NewGuid();

    Assert.Null(await cache.GetAsync(id, CancellationToken.None));
    await cache.EvictAsync(id, CancellationToken.None); // logged, not thrown

    using var cts = new CancellationTokenSource();
    await cts.CancelAsync();
    await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cache.EvictAsync(id, cts.Token));
  }

  /// <summary>A Redis outage: every call fails, except that cancellation is reported as such.</summary>
  private sealed class BrokenCache : IDistributedCache
  {
    public byte[]? Get(
      string key
    )
    {
      throw Fail(default);
    }

    public Task<byte[]?> GetAsync(
      string key,
      CancellationToken token = default
    )
    {
      throw Fail(token);
    }

    public void Set(
      string key,
      byte[] value,
      DistributedCacheEntryOptions options
    )
    {
      throw Fail(default);
    }

    public Task SetAsync(
      string key,
      byte[] value,
      DistributedCacheEntryOptions options,
      CancellationToken token = default
    )
    {
      throw Fail(token);
    }

    public void Refresh(
      string key
    )
    {
      throw Fail(default);
    }

    public Task RefreshAsync(
      string key,
      CancellationToken token = default
    )
    {
      throw Fail(token);
    }

    public void Remove(
      string key
    )
    {
      throw Fail(default);
    }

    public Task RemoveAsync(
      string key,
      CancellationToken token = default
    )
    {
      throw Fail(token);
    }

    private static Exception Fail(
      CancellationToken ct
    )
    {
      return ct.IsCancellationRequested
        ? new OperationCanceledException(ct)
        : new InvalidOperationException("redis is down");
    }
  }
}