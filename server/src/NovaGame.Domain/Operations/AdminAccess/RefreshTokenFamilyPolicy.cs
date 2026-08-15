using NovaGame.Domain.Shared.Operations.AdminAccess;

namespace NovaGame.Domain.Operations.AdminAccess;

public sealed record RefreshTokenFamilyTransition(
    Guid FamilyId,
    Guid AdministratorId,
    bool Changed);

public sealed record RefreshTokenFamilyIdentity(
    Guid FamilyId,
    Guid AdministratorId);

public sealed class RefreshTokenFamilyPolicy
{
    public RefreshTokenFamilyIdentity RequireValidIdentity(
        IReadOnlyCollection<RefreshTokenSession> sessions)
    {
        var ordered = ValidateAndOrder(sessions);
        return new RefreshTokenFamilyIdentity(
            ordered[0].FamilyId,
            ordered[0].AdministratorId);
    }

    public RefreshTokenFamilyTransition Revoke(
        IReadOnlyCollection<RefreshTokenSession> sessions,
        RefreshTokenRevocationReason reason,
        DateTime revokedAtUtc,
        string? revokedFromIpAddress)
    {
        var ordered = ValidateAndOrder(sessions);
        var changed = false;
        foreach (var session in ordered.Where(session => !session.IsRevoked))
        {
            session.Revoke(reason, revokedAtUtc, revokedFromIpAddress);
            changed = true;
        }

        return new RefreshTokenFamilyTransition(
            ordered[0].FamilyId,
            ordered[0].AdministratorId,
            changed);
    }

    public AdminSessionFamilySnapshot CreateSnapshot(
        IReadOnlyCollection<RefreshTokenSession> sessions,
        string userName,
        string displayName,
        DateTime nowUtc)
    {
        var ordered = ValidateAndOrder(sessions);
        if (nowUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("The current timestamp must use UTC.", nameof(nowUtc));
        }

        var terminal = ordered.Single(session => session.ReplacedBySessionId is null);
        var state = ordered.Any(session => !session.IsRevoked && session.ExpiresAtUtc > nowUtc)
            ? AdminSessionFamilyStateSnapshot.Active
            : terminal.RevocationReason == RefreshTokenRevocationReason.Expired ||
                (!terminal.IsRevoked && terminal.ExpiresAtUtc <= nowUtc)
                ? AdminSessionFamilyStateSnapshot.Expired
                : AdminSessionFamilyStateSnapshot.Revoked;
        var latestEvent = ordered
            .SelectMany(session => CreateActivityEvents(session))
            .OrderByDescending(activity => activity.OccurredAtUtc)
            .ThenByDescending(activity => activity.IsRevocation)
            .ThenByDescending(activity => activity.SessionId)
            .First();

        return new AdminSessionFamilySnapshot(
            terminal.FamilyId,
            terminal.AdministratorId,
            userName,
            displayName,
            ordered.Min(session => session.CreatedAtUtc),
            latestEvent.OccurredAtUtc,
            terminal.ExpiresAtUtc,
            latestEvent.IpAddress,
            state,
            terminal.RevokedAtUtc,
            terminal.RevocationReason);
    }

    private static IReadOnlyList<RefreshTokenSession> ValidateAndOrder(
        IReadOnlyCollection<RefreshTokenSession> sessions)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        if (sessions.Count == 0)
        {
            throw InvariantViolation("A refresh-token family cannot be empty.");
        }

        var byId = new Dictionary<Guid, RefreshTokenSession>();
        foreach (var session in sessions)
        {
            if (session is null || !byId.TryAdd(session.Id, session))
            {
                throw InvariantViolation("Refresh-token family rows are null or duplicated.");
            }
        }

        var familyIds = byId.Values.Select(session => session.FamilyId).Distinct().ToArray();
        var administratorIds = byId.Values
            .Select(session => session.AdministratorId)
            .Distinct()
            .ToArray();
        if (familyIds.Length != 1 || administratorIds.Length != 1)
        {
            throw InvariantViolation("A refresh-token family must have one family and one owner.");
        }

        if (byId.Values.Count(session => !session.IsRevoked) > 1)
        {
            throw InvariantViolation("A refresh-token family cannot have multiple active rows.");
        }

        var referencedIds = new HashSet<Guid>();
        foreach (var session in byId.Values)
        {
            if (session.ReplacedBySessionId is not { } replacementId)
            {
                if (session.RevocationReason == RefreshTokenRevocationReason.Rotated)
                {
                    throw InvariantViolation("A rotated session must reference its replacement.");
                }

                continue;
            }

            if (session.RevocationReason != RefreshTokenRevocationReason.Rotated ||
                replacementId == session.Id ||
                !byId.ContainsKey(replacementId) ||
                !referencedIds.Add(replacementId))
            {
                throw InvariantViolation("A refresh-token replacement chain is malformed.");
            }
        }

        var roots = byId.Keys.Where(id => !referencedIds.Contains(id)).ToArray();
        var terminals = byId.Values.Where(session => session.ReplacedBySessionId is null).ToArray();
        if (roots.Length != 1 || terminals.Length != 1)
        {
            throw InvariantViolation("A refresh-token family must have one root and one terminal row.");
        }

        var ordered = new List<RefreshTokenSession>(byId.Count);
        var visited = new HashSet<Guid>();
        var current = byId[roots[0]];
        while (visited.Add(current.Id))
        {
            ordered.Add(current);
            if (current.ReplacedBySessionId is not { } replacementId)
            {
                break;
            }

            current = byId[replacementId];
        }

        if (ordered.Count != byId.Count ||
            ordered[^1].Id != terminals[0].Id)
        {
            throw InvariantViolation("A refresh-token family must be one connected acyclic chain.");
        }

        return ordered.AsReadOnly();
    }

    private static IEnumerable<RefreshTokenActivity> CreateActivityEvents(
        RefreshTokenSession session)
    {
        yield return new RefreshTokenActivity(
            session.Id,
            session.CreatedAtUtc,
            IsRevocation: false,
            session.CreatedFromIpAddress);
        if (session.RevokedAtUtc is { } revokedAtUtc)
        {
            yield return new RefreshTokenActivity(
                session.Id,
                revokedAtUtc,
                IsRevocation: true,
                session.RevokedFromIpAddress);
        }
    }

    private static AdminAccessManagementDomainException InvariantViolation(string message)
    {
        return new AdminAccessManagementDomainException(
            AdminAccessManagementErrorCodes.InvariantViolation,
            message);
    }

    private readonly record struct RefreshTokenActivity(
        Guid SessionId,
        DateTime OccurredAtUtc,
        bool IsRevocation,
        string? IpAddress);
}
