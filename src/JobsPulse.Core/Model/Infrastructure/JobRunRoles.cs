namespace JobsPulse.Core.Model.Infrastructure;

/// <summary>The role names a run is recorded under - the names of the host roles that run the jobs.</summary>
public static class JobRunRoles
{
    public const string Polling = "Polling";

    public const string Registry = "Registry";

    public const string Discovery = "Discovery";

    public const string Cleanup = "Cleanup";
}
