using System.Net.Http.Json;

namespace Stria.Client.Services;

/// <summary>
/// Sends state-changing requests to the Stria API with an antiforgery token. Cookie-authenticated
/// JSON POSTs are rejected without one, so dashboard mutations go through here rather than
/// HttpClient.PostAsJsonAsync. A fresh token per POST avoids stale tokens after re-sign-in;
/// mutations are rare, so the extra GET is cheap.
/// </summary>
public sealed class ChaosApiClient(HttpClient http)
{
    public const string AntiforgeryHeaderName = "X-XSRF-TOKEN";

    public async Task<HttpResponseMessage> PostAsJsonAsync<T>(string url, T body, CancellationToken ct = default)
    {
        var antiforgery = await http.GetFromJsonAsync<AntiforgeryTokenResponse>("/api/v1/antiforgery/token", ct)
                          ?? throw new InvalidOperationException("The server returned no antiforgery token.");

        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
        request.Headers.Add(AntiforgeryHeaderName, antiforgery.Token);
        return await http.SendAsync(request, ct);
    }

    private sealed record AntiforgeryTokenResponse(string Token);
}
