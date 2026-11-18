using System.Diagnostics.CodeAnalysis;
using System.Security.Claims;
using Microsoft.Identity.Web;
using Stria.Domain;

namespace Stria.Security;

public static class ActorResolver
{
    /// <summary>
    /// Derives the audit actor from validated claims: the Entra object ID (short "oid" or the
    /// long URI form), then "sub", then NameIdentifier. Never uses a display name or email as
    /// the identifier, because those can change or be reassigned.
    /// </summary>
    public static bool TryResolve(ClaimsPrincipal user, [NotNullWhen(true)] out Actor? actor)
    {
        var id = user.GetObjectId()
                 ?? user.FindFirstValue("sub")
                 ?? user.FindFirstValue(ClaimTypes.NameIdentifier);

        if (string.IsNullOrWhiteSpace(id) || id.Length > ChaosRules.MaxTriggeredByLength)
        {
            actor = null;
            return false;
        }

        var name = user.FindFirstValue("name") ?? user.Identity?.Name;
        if (string.IsNullOrWhiteSpace(name))
            name = null;
        else if (name.Length > Actor.MaxDisplayNameLength)
            name = name[..Actor.MaxDisplayNameLength];

        actor = new Actor(id.Trim(), name);
        return true;
    }
}
