using NovaCore.BuildingBlocks.Domain;

namespace NovaGame.Domain.Operations.AdminAccess;

public sealed class Administrator : AggregateRoot<Guid>
{
    private readonly List<AdministratorRole> _roleMemberships = [];

    private Administrator()
    {
    }

    public Administrator(
        Guid id,
        AdministratorUserName userName,
        string displayName,
        string passwordHash,
        DateTime createdAtUtc,
        bool isActive = true)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Administrator ID cannot be empty.", nameof(id));
        }

        Id = id;
        UserName = userName.Value;
        NormalizedUserName = userName.NormalizedValue;
        DisplayName = NormalizeDisplayName(displayName);
        PasswordHash = RequirePasswordHash(passwordHash);
        IsActive = isActive;
        CreatedAtUtc = RequireUtc(createdAtUtc, nameof(createdAtUtc));
    }

    public string UserName { get; private set; } = string.Empty;

    public string NormalizedUserName { get; private set; } = string.Empty;

    public string DisplayName { get; private set; } = string.Empty;

    public string PasswordHash { get; private set; } = string.Empty;

    public bool IsActive { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime? UpdatedAtUtc { get; private set; }

    public DateTime? LastLoginAtUtc { get; private set; }

    public IReadOnlyCollection<AdministratorRole> RoleMemberships => _roleMemberships.AsReadOnly();

    public void SetPasswordHash(string passwordHash, DateTime changedAtUtc)
    {
        PasswordHash = RequirePasswordHash(passwordHash);
        UpdatedAtUtc = RequireUtc(changedAtUtc, nameof(changedAtUtc));
    }

    public void RecordSuccessfulLogin(DateTime loggedInAtUtc)
    {
        LastLoginAtUtc = RequireUtc(loggedInAtUtc, nameof(loggedInAtUtc));
        UpdatedAtUtc = loggedInAtUtc;
    }

    public AdministratorRole AssignRole(
        Guid membershipId,
        Guid roleId,
        DateTime assignedAtUtc,
        Guid? assignedByAdministratorId = null)
    {
        if (_roleMemberships.Any(membership => membership.RoleId == roleId))
        {
            throw new InvalidOperationException("The administrator already belongs to this role.");
        }

        var membership = new AdministratorRole(
            membershipId,
            Id,
            roleId,
            RequireUtc(assignedAtUtc, nameof(assignedAtUtc)),
            assignedByAdministratorId);
        _roleMemberships.Add(membership);
        UpdatedAtUtc = assignedAtUtc;
        return membership;
    }

    public void Activate(DateTime changedAtUtc)
    {
        IsActive = true;
        UpdatedAtUtc = RequireUtc(changedAtUtc, nameof(changedAtUtc));
    }

    public void Deactivate(DateTime changedAtUtc)
    {
        IsActive = false;
        UpdatedAtUtc = RequireUtc(changedAtUtc, nameof(changedAtUtc));
    }

    private static string NormalizeDisplayName(string value)
    {
        var displayName = value?.Trim();
        if (string.IsNullOrWhiteSpace(displayName) || displayName.Length > 128)
        {
            throw new ArgumentException("Administrator display names must contain 1 to 128 characters.", nameof(value));
        }

        return displayName;
    }

    private static string RequirePasswordHash(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 512)
        {
            throw new ArgumentException("A valid password hash is required.", nameof(value));
        }

        return value;
    }

    private static DateTime RequireUtc(DateTime value, string parameterName)
    {
        if (value.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("The timestamp must use UTC.", parameterName);
        }

        return value;
    }
}
