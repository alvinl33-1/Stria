namespace Stria.Domain;

/// <summary>
/// The authenticated caller behind a change. <see cref="Id"/> is a stable identifier (the Entra
/// object ID, falling back to the token subject) and is what the audit trail relies on;
/// <see cref="DisplayName"/> is for people reading the audit trail and may change or be absent.
/// Built on the server from validated claims, never from request input.
/// </summary>
public sealed record Actor(string Id, string? DisplayName)
{
    public const int MaxDisplayNameLength = 200;
}
