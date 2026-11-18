namespace Stria.DTOs;

/// <summary>Body for engaging or releasing the kill switch. The actor comes from the caller's claims.</summary>
public record KillSwitchRequest(string? Reason);
