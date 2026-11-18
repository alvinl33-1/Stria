using System.Net;

namespace Stria.Client.Services;

/// <summary>
/// Maps API failures to short messages an operator can act on. Raw exception text and server
/// details stay in logs (the browser console for WebAssembly), never on the page.
/// </summary>
public static class OperatorMessages
{
    public static string For(HttpStatusCode? status, string action) => status switch
    {
        HttpStatusCode.Unauthorized => "Your session has ended. Reload the page to sign in again.",
        HttpStatusCode.Forbidden => $"You don't have permission to {action}.",
        HttpStatusCode.Conflict => "Fault injection is suspended by the kill switch.",
        HttpStatusCode.TooManyRequests => "Too many requests. Wait a moment and try again.",
        HttpStatusCode.BadRequest => $"The request to {action} was rejected. Reload the page and try again.",
        _ => $"Could not {action}. Try again shortly."
    };
}
