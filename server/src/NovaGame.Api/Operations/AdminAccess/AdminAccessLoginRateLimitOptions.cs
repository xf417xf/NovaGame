namespace NovaGame.Api.Operations.AdminAccess;

public sealed class AdminAccessLoginRateLimitOptions
{
    public const string SectionName = "NovaGame:AdminAccess:LoginRateLimit";

    public int PermitLimit { get; set; } = 5;

    public TimeSpan Window { get; set; } = TimeSpan.FromMinutes(1);

    public int QueueLimit { get; set; }

    public static bool IsValid(AdminAccessLoginRateLimitOptions options)
    {
        return options.PermitLimit is >= 1 and <= 1000 &&
            options.Window >= TimeSpan.FromSeconds(1) &&
            options.Window <= TimeSpan.FromHours(1) &&
            options.QueueLimit is >= 0 and <= 1000;
    }
}
