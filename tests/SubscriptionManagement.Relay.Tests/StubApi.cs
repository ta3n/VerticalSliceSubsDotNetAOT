using System.Collections.Concurrent;
using System.Net;

namespace SubscriptionManagement.Relay.Tests;

/// <summary>
/// Stands in for the API behind the Relay's <c>ApiClient</c>: answers every request with the status a test chose
/// and records what was asked. Statuses only, because that is all the Relay reads from an answer.
/// </summary>
public sealed class StubApi : HttpMessageHandler
{
  private readonly ConcurrentQueue<Call> _calls = new();
  private Func<Call, HttpStatusCode> _respond = _ => HttpStatusCode.OK;

  public IReadOnlyList<Call> Calls => [.. _calls];

  /// <summary>Forgets the recorded calls and sets how the following ones are answered.</summary>
  public void Respond(
    Func<Call, HttpStatusCode> respond
  )
  {
    _calls.Clear();
    _respond = respond;
  }

  protected override async Task<HttpResponseMessage> SendAsync(
    HttpRequestMessage request,
    CancellationToken ct
  )
  {
    var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
    var call = new Call(request.Method.Method, request.RequestUri!.AbsolutePath, body);
    _calls.Enqueue(call);
    return new HttpResponseMessage(_respond(call));
  }

  public sealed record Call(
    string Method,
    string Path,
    string? Body
  );
}