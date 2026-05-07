namespace ArchonAI.Api.Security;

public sealed class JwtOptions
{
    public const string SectionName = "Security:Jwt";

    public string Issuer { get; set; } = "ArchonAI";
    public string Audience { get; set; } = "ArchonAI.Api";
    public string SigningKey { get; set; } = string.Empty;
    public int ClockSkewSeconds { get; set; } = 60;
}
