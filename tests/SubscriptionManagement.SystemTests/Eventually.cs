using Xunit.Sdk;

namespace SubscriptionManagement.SystemTests;

/// <summary>Polling for an outcome that arrives through the broker: the services have no "done" signal to await.</summary>
public static class Eventually
{
    /// <summary>
    /// Calls <paramref name="probe"/> until it reports done, and returns the value of that call. The failure names what
    /// was awaited and shows the last value seen, so a stuck saga is diagnosable from the test output.
    /// </summary>
    public static async Task<T> UntilAsync<T>(
        Func<Task<(bool Done, T Value)>> probe,
        TimeSpan timeout,
        string awaiting
    )
    {
        var deadline = DateTime.UtcNow + timeout;
        T last;
        while (true)
        {
            (var done, last) = await probe();
            if (done)
            {
                return last;
            }

            if (DateTime.UtcNow >= deadline)
            {
                break;
            }

            await Task.Delay(100);
        }

        throw new XunitException($"Timed out after {timeout.TotalSeconds:0} s waiting for {awaiting}. Last value: {last}");
    }
}
