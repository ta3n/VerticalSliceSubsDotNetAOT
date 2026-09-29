namespace SubscriptionManagement.Infrastructure;

public static class HealthProbe
{
    public static async Task<int> RunAsync(string[] args)
    {
        var url = args.Length > 1 ? args[1] : DefaultUrl();
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            using var response = await client.GetAsync(url);
            return response.IsSuccessStatusCode ? 0 : 1;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return 1;
        }
    }

    private static string DefaultUrl()
    {
        var port = (Environment.GetEnvironmentVariable("ASPNETCORE_HTTP_PORTS") ?? "8080").Split(';')[0];
        return $"http://127.0.0.1:{port}/health/live";
    }
}
