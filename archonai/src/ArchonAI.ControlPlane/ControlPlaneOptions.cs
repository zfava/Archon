namespace ArchonAI.ControlPlane;

public sealed class ControlPlaneOptions
{
    public const string SectionName = "ControlPlane";

    public int MaxTenants { get; set; } = 100;
    public int DefaultPolicyPriority { get; set; } = 100;
    public bool EnforceQuotas { get; set; } = true;
    public string[] RequiredRoles { get; set; } = ["Admin"];
}
