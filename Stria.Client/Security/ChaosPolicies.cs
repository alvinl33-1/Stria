using Microsoft.AspNetCore.Authorization;

namespace Stria.Client.Security;

/// <summary>Entra app role values. They must match the app registration's role "Value" exactly.</summary>
public static class ChaosRoles
{
    public const string Viewer = "ChaosViewer";
    public const string Operator = "ChaosOperator";
    public const string Admin = "ChaosAdmin";
}

/// <summary>
/// Authorization policies shared by the server (endpoint enforcement) and the WebAssembly
/// dashboard (UI hiding only). Living in Stria.Client lets both sides use the same strings,
/// because Stria.API already references this project.
/// </summary>
public static class ChaosPolicies
{
    public const string View = "Chaos.View";
    public const string Operate = "Chaos.Operate";
    public const string Admin = "Chaos.Admin";

    public static void AddPolicies(AuthorizationOptions options)
    {
        options.AddPolicy(View, p => p.RequireRole(ChaosRoles.Viewer, ChaosRoles.Operator, ChaosRoles.Admin));
        options.AddPolicy(Operate, p => p.RequireRole(ChaosRoles.Operator, ChaosRoles.Admin));
        options.AddPolicy(Admin, p => p.RequireRole(ChaosRoles.Admin));
    }
}
