namespace NovaGame.Application.Operations.AdminAccess;

public sealed class AdminAccessSeedOptions
{
    public const string SectionName = "NovaGame:AdminAccess:Seed";

    public string UserName { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    public string DisplayName { get; set; } = "Administrator";
}
