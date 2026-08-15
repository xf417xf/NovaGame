using System.Runtime.CompilerServices;
using System.Data;
using Microsoft.EntityFrameworkCore;
using NovaGame.Domain.Operations.AdminAccess;
using NovaGame.Domain.Shared.Operations.AdminAccess;

namespace NovaGame.Host.EntityFrameworkCore.Operations.AdminAccess;

public sealed class AdminAccessManagementRepository :
    IAdminAccessManagementRepository,
    IAdminAccessManagementReadSnapshot
{
    private const string SessionFamilyValidationSql = """
        WITH RECURSIVE
        `ScopedFamilies` AS (
            SELECT DISTINCT `scope`.`FamilyId`
            FROM `admin_refresh_token_sessions` AS `scope`
            WHERE ({0} = 0 OR `scope`.`AdministratorId` = {1})
        ),
        `FamilyRows` AS (
            SELECT
                `session`.`Id`,
                `session`.`AdministratorId`,
                `session`.`FamilyId`,
                `session`.`RevokedAtUtc`,
                `session`.`RevocationReason`,
                `session`.`ReplacedBySessionId`
            FROM `admin_refresh_token_sessions` AS `session`
            INNER JOIN `ScopedFamilies` AS `family`
                ON `family`.`FamilyId` = `session`.`FamilyId`
        ),
        `FamilyStats` AS (
            SELECT
                `FamilyId`,
                COUNT(*) AS `SessionCount`,
                COUNT(DISTINCT `AdministratorId`) AS `AdministratorCount`,
                MIN(`AdministratorId`) AS `OwnerAdministratorId`,
                SUM(CASE WHEN `RevokedAtUtc` IS NULL THEN 1 ELSE 0 END) AS `UnrevokedCount`,
                SUM(CASE WHEN `ReplacedBySessionId` IS NULL THEN 1 ELSE 0 END) AS `TerminalCount`
            FROM `FamilyRows`
            GROUP BY `FamilyId`
        ),
        `InvalidReplacementFamilies` AS (
            SELECT DISTINCT `source`.`FamilyId`
            FROM `FamilyRows` AS `source`
            LEFT JOIN `admin_refresh_token_sessions` AS `target`
                ON `target`.`Id` = `source`.`ReplacedBySessionId`
            WHERE (`source`.`ReplacedBySessionId` IS NULL AND
                    `source`.`RevocationReason` = 'Rotated')
                OR (`source`.`ReplacedBySessionId` IS NOT NULL AND (
                    `source`.`RevocationReason` IS NULL OR
                    `source`.`RevocationReason` <> 'Rotated' OR
                    `source`.`ReplacedBySessionId` = `source`.`Id` OR
                    `target`.`Id` IS NULL OR
                    `target`.`FamilyId` <> `source`.`FamilyId`))
        ),
        `DuplicateReplacementTargetFamilies` AS (
            SELECT `FamilyId`
            FROM `FamilyRows`
            WHERE `ReplacedBySessionId` IS NOT NULL
            GROUP BY `FamilyId`, `ReplacedBySessionId`
            HAVING COUNT(*) > 1
        ),
        `RootRows` AS (
            SELECT `row`.`FamilyId`, `row`.`Id`
            FROM `FamilyRows` AS `row`
            WHERE NOT EXISTS (
                SELECT 1
                FROM `FamilyRows` AS `incoming`
                WHERE `incoming`.`FamilyId` = `row`.`FamilyId`
                    AND `incoming`.`ReplacedBySessionId` = `row`.`Id`)
        ),
        `RootCounts` AS (
            SELECT `FamilyId`, COUNT(*) AS `RootCount`
            FROM `RootRows`
            GROUP BY `FamilyId`
        ),
        `ValidChains` AS (
            SELECT `stats`.`FamilyId`, `stats`.`SessionCount`
            FROM `FamilyStats` AS `stats`
            INNER JOIN `RootCounts` AS `roots`
                ON `roots`.`FamilyId` = `stats`.`FamilyId`
            INNER JOIN `admin_administrators` AS `owner`
                ON `owner`.`Id` = `stats`.`OwnerAdministratorId`
            LEFT JOIN `InvalidReplacementFamilies` AS `invalidReplacement`
                ON `invalidReplacement`.`FamilyId` = `stats`.`FamilyId`
            LEFT JOIN `DuplicateReplacementTargetFamilies` AS `duplicateTarget`
                ON `duplicateTarget`.`FamilyId` = `stats`.`FamilyId`
            WHERE `stats`.`AdministratorCount` = 1
                AND `stats`.`UnrevokedCount` <= 1
                AND `stats`.`TerminalCount` = 1
                AND `roots`.`RootCount` = 1
                AND `invalidReplacement`.`FamilyId` IS NULL
                AND `duplicateTarget`.`FamilyId` IS NULL
        ),
        `ReachableRows` (`FamilyId`, `SessionId`, `SessionCount`, `Depth`) AS (
            SELECT `root`.`FamilyId`, `root`.`Id`, `valid`.`SessionCount`, 1
            FROM `RootRows` AS `root`
            INNER JOIN `ValidChains` AS `valid`
                ON `valid`.`FamilyId` = `root`.`FamilyId`
            UNION ALL
            SELECT
                `reachable`.`FamilyId`,
                `next`.`Id`,
                `reachable`.`SessionCount`,
                `reachable`.`Depth` + 1
            FROM `ReachableRows` AS `reachable`
            INNER JOIN `FamilyRows` AS `source`
                ON `source`.`FamilyId` = `reachable`.`FamilyId`
                    AND `source`.`Id` = `reachable`.`SessionId`
            INNER JOIN `FamilyRows` AS `next`
                ON `next`.`FamilyId` = `reachable`.`FamilyId`
                    AND `next`.`Id` = `source`.`ReplacedBySessionId`
            WHERE `reachable`.`Depth` < `reachable`.`SessionCount`
        ),
        `ReachabilityCounts` AS (
            SELECT `FamilyId`, COUNT(*) AS `ReachableCount`
            FROM `ReachableRows`
            GROUP BY `FamilyId`
        )
        SELECT `stats`.`FamilyId`
        FROM `FamilyStats` AS `stats`
        LEFT JOIN `RootCounts` AS `roots`
            ON `roots`.`FamilyId` = `stats`.`FamilyId`
        LEFT JOIN `InvalidReplacementFamilies` AS `invalidReplacement`
            ON `invalidReplacement`.`FamilyId` = `stats`.`FamilyId`
        LEFT JOIN `DuplicateReplacementTargetFamilies` AS `duplicateTarget`
            ON `duplicateTarget`.`FamilyId` = `stats`.`FamilyId`
        LEFT JOIN `admin_administrators` AS `owner`
            ON `owner`.`Id` = `stats`.`OwnerAdministratorId`
        LEFT JOIN `ValidChains` AS `valid`
            ON `valid`.`FamilyId` = `stats`.`FamilyId`
        LEFT JOIN `ReachabilityCounts` AS `reachable`
            ON `reachable`.`FamilyId` = `stats`.`FamilyId`
        WHERE `stats`.`AdministratorCount` <> 1
            OR `stats`.`UnrevokedCount` > 1
            OR `stats`.`TerminalCount` <> 1
            OR `roots`.`RootCount` IS NULL
            OR `roots`.`RootCount` <> 1
            OR `owner`.`Id` IS NULL
            OR `invalidReplacement`.`FamilyId` IS NOT NULL
            OR `duplicateTarget`.`FamilyId` IS NOT NULL
            OR (`valid`.`FamilyId` IS NOT NULL AND
                COALESCE(`reachable`.`ReachableCount`, 0) <> `stats`.`SessionCount`)
        LIMIT 1
        """;

    private readonly NovaGameDbContext _dbContext;
    private readonly RefreshTokenFamilyPolicy _refreshTokenFamilyPolicy;

    public AdminAccessManagementRepository(
        NovaGameDbContext dbContext,
        RefreshTokenFamilyPolicy refreshTokenFamilyPolicy)
    {
        _dbContext = dbContext;
        _refreshTokenFamilyPolicy = refreshTokenFamilyPolicy;
    }

    public async Task<TResult> ExecuteAsync<TResult>(
        Func<CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        if (_dbContext.Database.CurrentTransaction is not null)
        {
            throw new InvalidOperationException(
                "Management list queries require a root transactional unit of work.");
        }

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(
            IsolationLevel.RepeatableRead,
            cancellationToken);
        var result = await operation(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    public async Task<AdminAccessPageResult<AdministratorManagementSnapshot>> GetAdministratorPageAsync(
        string? normalizedUserNamePrefix,
        bool? isActive,
        Guid? roleId,
        int skipCount,
        int maxResultCount,
        CancellationToken cancellationToken = default)
    {
        var query = _dbContext.Administrators.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(normalizedUserNamePrefix))
        {
            var pattern = EscapeLikePattern(normalizedUserNamePrefix) + "%";
            query = query.Where(administrator => EF.Functions.Like(
                administrator.NormalizedUserName,
                pattern,
                "\\"));
        }

        if (isActive.HasValue)
        {
            query = query.Where(administrator => administrator.IsActive == isActive.Value);
        }

        if (roleId.HasValue)
        {
            query = query.Where(administrator => _dbContext.AdministratorRoles.Any(membership =>
                membership.AdministratorId == administrator.Id && membership.RoleId == roleId.Value));
        }

        var totalCount = await query.LongCountAsync(cancellationToken);
        var rows = await query
            .OrderBy(administrator => administrator.NormalizedUserName)
            .ThenBy(administrator => administrator.Id)
            .Skip(skipCount)
            .Take(maxResultCount)
            .Select(administrator => new AdministratorPageRow(
                administrator.Id,
                administrator.UserName,
                administrator.DisplayName,
                administrator.IsActive,
                administrator.CreatedAtUtc,
                administrator.UpdatedAtUtc,
                administrator.LastLoginAtUtc,
                administrator.Revision))
            .ToArrayAsync(cancellationToken);

        var snapshots = await CreateAdministratorSnapshotsAsync(rows, cancellationToken);
        return new AdminAccessPageResult<AdministratorManagementSnapshot>(snapshots, totalCount);
    }

    public async Task<AdministratorManagementSnapshot?> FindAdministratorSnapshotAsync(
        Guid administratorId,
        CancellationToken cancellationToken = default)
    {
        var row = await _dbContext.Administrators
            .AsNoTracking()
            .Where(administrator => administrator.Id == administratorId)
            .Select(administrator => new AdministratorPageRow(
                administrator.Id,
                administrator.UserName,
                administrator.DisplayName,
                administrator.IsActive,
                administrator.CreatedAtUtc,
                administrator.UpdatedAtUtc,
                administrator.LastLoginAtUtc,
                administrator.Revision))
            .SingleOrDefaultAsync(cancellationToken);
        if (row is null)
        {
            return null;
        }

        var snapshots = await CreateAdministratorSnapshotsAsync([row], cancellationToken);
        return snapshots.Single();
    }

    public async Task<AdminAccessPageResult<AdminRoleManagementSnapshot>> GetRolePageAsync(
        string? normalizedNamePrefix,
        bool? isActive,
        bool? isBuiltIn,
        IReadOnlyCollection<string> registeredPermissionNames,
        int skipCount,
        int maxResultCount,
        CancellationToken cancellationToken = default)
    {
        var query = _dbContext.AdminRoles.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(normalizedNamePrefix))
        {
            var pattern = EscapeLikePattern(normalizedNamePrefix) + "%";
            query = query.Where(role => EF.Functions.Like(role.NormalizedName, pattern, "\\"));
        }

        if (isActive.HasValue)
        {
            query = query.Where(role => role.IsActive == isActive.Value);
        }

        if (isBuiltIn.HasValue)
        {
            query = query.Where(role => role.IsBuiltIn == isBuiltIn.Value);
        }

        var totalCount = await query.LongCountAsync(cancellationToken);
        var rows = await query
            .OrderByDescending(role => role.IsBuiltIn)
            .ThenBy(role => role.NormalizedName)
            .ThenBy(role => role.Id)
            .Skip(skipCount)
            .Take(maxResultCount)
            .Select(role => new AdminRolePageRow(
                role.Id,
                role.Name,
                role.IsBuiltIn,
                role.IsActive,
                role.CreatedAtUtc,
                role.UpdatedAtUtc,
                role.Revision))
            .ToArrayAsync(cancellationToken);

        var snapshots = await CreateRoleSnapshotsAsync(
            rows,
            registeredPermissionNames,
            cancellationToken);
        return new AdminAccessPageResult<AdminRoleManagementSnapshot>(snapshots, totalCount);
    }

    public async Task<AdminRoleManagementSnapshot?> FindRoleSnapshotAsync(
        Guid roleId,
        IReadOnlyCollection<string> registeredPermissionNames,
        CancellationToken cancellationToken = default)
    {
        var row = await _dbContext.AdminRoles
            .AsNoTracking()
            .Where(role => role.Id == roleId)
            .Select(role => new AdminRolePageRow(
                role.Id,
                role.Name,
                role.IsBuiltIn,
                role.IsActive,
                role.CreatedAtUtc,
                role.UpdatedAtUtc,
                role.Revision))
            .SingleOrDefaultAsync(cancellationToken);
        if (row is null)
        {
            return null;
        }

        var snapshots = await CreateRoleSnapshotsAsync(
            [row],
            registeredPermissionNames,
            cancellationToken);
        return snapshots.Single();
    }

    public async Task<AdminAccessPageResult<AdminSessionFamilySnapshot>> GetSessionFamilyPageAsync(
        Guid? administratorId,
        AdminSessionFamilyStateSnapshot? state,
        DateTime nowUtc,
        int skipCount,
        int maxResultCount,
        CancellationToken cancellationToken = default)
    {
        if (nowUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("The current timestamp must use UTC.", nameof(nowUtc));
        }

        var stateCode = state switch
        {
            AdminSessionFamilyStateSnapshot.Active => 0,
            AdminSessionFamilyStateSnapshot.Expired => 1,
            AdminSessionFamilyStateSnapshot.Revoked => 2,
            null => -1,
            _ => throw new ArgumentOutOfRangeException(nameof(state))
        };
        const string familyPageSql = """
            SELECT
                `FamilyId`,
                MAX(CASE
                    WHEN `RevokedAtUtc` IS NOT NULL AND `RevokedAtUtc` > `CreatedAtUtc`
                        THEN `RevokedAtUtc`
                    ELSE `CreatedAtUtc`
                END) AS `LastActivityAtUtc`
            FROM `admin_refresh_token_sessions`
            WHERE ({2} = 0 OR `FamilyId` IN (
                SELECT `filteredSession`.`FamilyId`
                FROM `admin_refresh_token_sessions` AS `filteredSession`
                WHERE `filteredSession`.`AdministratorId` = {3}
            ))
            GROUP BY `FamilyId`
            HAVING {1} = -1
                OR ({1} = 0 AND SUM(CASE
                    WHEN `RevokedAtUtc` IS NULL AND `ExpiresAtUtc` > {0} THEN 1
                    ELSE 0
                END) > 0)
                OR ({1} = 1 AND SUM(CASE
                    WHEN `RevokedAtUtc` IS NULL AND `ExpiresAtUtc` > {0} THEN 1
                    ELSE 0
                END) = 0 AND SUM(CASE
                    WHEN `ReplacedBySessionId` IS NULL AND
                        (`RevocationReason` = 'Expired' OR
                            (`RevokedAtUtc` IS NULL AND `ExpiresAtUtc` <= {0})) THEN 1
                    ELSE 0
                END) > 0)
                OR ({1} = 2 AND SUM(CASE
                    WHEN `RevokedAtUtc` IS NULL AND `ExpiresAtUtc` > {0} THEN 1
                    ELSE 0
                END) = 0 AND SUM(CASE
                    WHEN `ReplacedBySessionId` IS NULL AND
                        (`RevocationReason` = 'Expired' OR
                            (`RevokedAtUtc` IS NULL AND `ExpiresAtUtc` <= {0})) THEN 1
                    ELSE 0
                END) = 0)
            """;
        var familyQuery = _dbContext.Database.SqlQueryRaw<SessionFamilyPageRow>(
            familyPageSql,
            nowUtc,
            stateCode,
            administratorId.HasValue,
            administratorId?.ToString() ?? string.Empty);

        await EnsureSessionFamiliesAreValidAsync(administratorId, cancellationToken);
        var totalCount = await familyQuery.LongCountAsync(cancellationToken);
        var page = await familyQuery
            .OrderByDescending(family => family.LastActivityAtUtc)
            .ThenBy(family => family.FamilyId)
            .Skip(skipCount)
            .Take(maxResultCount)
            .ToArrayAsync(cancellationToken);
        if (page.Length == 0)
        {
            return new AdminAccessPageResult<AdminSessionFamilySnapshot>([], totalCount);
        }

        var familyIds = page.Select(family => family.FamilyId).ToArray();
        var familySessions = await _dbContext.RefreshTokenSessions
            .AsNoTracking()
            .Where(session => familyIds.Contains(session.FamilyId))
            .OrderBy(session => session.FamilyId)
            .ThenBy(session => session.CreatedAtUtc)
            .ThenBy(session => session.Id)
            .ToArrayAsync(cancellationToken);
        var administratorIds = familySessions
            .Select(session => session.AdministratorId)
            .Distinct()
            .ToArray();
        var administrators = await _dbContext.Administrators
            .AsNoTracking()
            .Where(administrator => administratorIds.Contains(administrator.Id))
            .Select(administrator => new AdministratorIdentityRow(
                administrator.Id,
                administrator.UserName,
                administrator.DisplayName))
            .ToDictionaryAsync(administrator => administrator.Id, cancellationToken);
        var sessionsByFamily = familySessions
            .GroupBy(session => session.FamilyId)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<RefreshTokenSession>)group.ToArray());
        var snapshots = new List<AdminSessionFamilySnapshot>(page.Length);

        foreach (var pageFamily in page)
        {
            if (!sessionsByFamily.TryGetValue(pageFamily.FamilyId, out var familyRows))
            {
                throw InvariantViolation("The paged refresh-token family has no rows.");
            }

            var identity = _refreshTokenFamilyPolicy.RequireValidIdentity(familyRows);
            if (identity.FamilyId != pageFamily.FamilyId ||
                !administrators.TryGetValue(identity.AdministratorId, out var administrator))
            {
                throw InvariantViolation("The refresh-token family owner is missing or malformed.");
            }

            var snapshot = _refreshTokenFamilyPolicy.CreateSnapshot(
                familyRows,
                administrator.UserName,
                administrator.DisplayName,
                nowUtc);
            if (snapshot.LastActivityAtUtc != pageFamily.LastActivityAtUtc ||
                state.HasValue && snapshot.State != state.Value)
            {
                throw InvariantViolation("The refresh-token family projection does not match the database group.");
            }

            snapshots.Add(snapshot);
        }

        return new AdminAccessPageResult<AdminSessionFamilySnapshot>(snapshots, totalCount);
    }

    public async Task<AdminAccessPageResult<AdminAccessAuditRecordSnapshot>> GetAuditPageAsync(
        AdminAccessAuditQuery query,
        int skipCount,
        int maxResultCount,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var records = _dbContext.AdminAccessAuditRecords
            .AsNoTracking()
            .Where(record => record.OccurredAtUtc >= query.FromUtc && record.OccurredAtUtc < query.ToUtc);
        if (query.Action.HasValue)
        {
            records = records.Where(record => record.Action == query.Action.Value);
        }

        if (query.Succeeded.HasValue)
        {
            records = records.Where(record => record.Succeeded == query.Succeeded.Value);
        }

        if (query.ActorAdministratorId.HasValue)
        {
            records = records.Where(record => record.ActorAdministratorId == query.ActorAdministratorId.Value);
        }

        if (query.SubjectAdministratorId.HasValue)
        {
            records = records.Where(record => record.AdministratorId == query.SubjectAdministratorId.Value);
        }

        if (query.RoleId.HasValue)
        {
            records = records.Where(record => record.RoleId == query.RoleId.Value);
        }

        if (query.CorrelationId is not null)
        {
            records = records.Where(record => record.CorrelationId == query.CorrelationId);
        }

        var totalCount = await records.LongCountAsync(cancellationToken);
        var items = await records
            .OrderByDescending(record => record.OccurredAtUtc)
            .ThenByDescending(record => record.Id)
            .Skip(skipCount)
            .Take(maxResultCount)
            .Select(record => new AdminAccessAuditRecordSnapshot(
                record.Id,
                record.Action,
                record.OccurredAtUtc,
                record.Succeeded,
                record.AdministratorId,
                record.ActorAdministratorId,
                record.RoleId,
                record.SessionFamilyId,
                record.SubjectUserName,
                record.PermissionName,
                record.ErrorCode,
                record.IpAddress,
                record.UserAgent,
                record.CorrelationId,
                record.TargetRevision))
            .ToArrayAsync(cancellationToken);
        return new AdminAccessPageResult<AdminAccessAuditRecordSnapshot>(items, totalCount);
    }

    public async Task<IReadOnlyList<AdminRole>> GetBuiltInRolesForUpdateAsync(
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.AdminRoles
            .FromSqlRaw("""
                SELECT *
                FROM `admin_roles`
                WHERE `IsBuiltIn` = 1
                ORDER BY `Id`
                FOR UPDATE
                """)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Guid>> GetAdministratorRoleIdsAsync(
        Guid administratorId,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.AdministratorRoles
            .AsNoTracking()
            .Where(membership => membership.AdministratorId == administratorId)
            .OrderBy(membership => membership.RoleId)
            .ThenBy(membership => membership.Id)
            .Select(membership => membership.RoleId)
            .ToArrayAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AdminRole>> GetRolesAsync(
        IReadOnlyCollection<Guid> roleIds,
        CancellationToken cancellationToken = default)
    {
        var ids = CanonicalizeIds(roleIds);
        if (ids.Length == 0)
        {
            return [];
        }

        return await _dbContext.AdminRoles
            .AsNoTracking()
            .Where(role => ids.Contains(role.Id))
            .OrderBy(role => role.Id)
            .ToArrayAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AdminRole>> GetRolesForUpdateInGlobalOrderAsync(
        IReadOnlyCollection<Guid> roleIds,
        CancellationToken cancellationToken = default)
    {
        var ids = CanonicalizeIds(roleIds);
        if (ids.Length == 0)
        {
            return [];
        }

        var parameters = ids
            .Select(id => (object)id.ToString())
            .ToArray();
        var placeholders = string.Join(", ", Enumerable.Range(0, parameters.Length)
            .Select(index => $"{{{index}}}"));
        var sql = FormattableStringFactory.Create($"""
            SELECT *
            FROM `admin_roles`
            WHERE `Id` IN ({placeholders})
            ORDER BY `IsBuiltIn` DESC, `Id`
            FOR UPDATE
            """, parameters);
        return await _dbContext.AdminRoles
            .FromSqlInterpolated(sql)
            .ToListAsync(cancellationToken);
    }

    public async Task<AdminRole?> FindRoleForUpdateAsync(
        Guid roleId,
        CancellationToken cancellationToken = default)
    {
        var roleIdText = roleId.ToString();
        var roles = await _dbContext.AdminRoles
            .FromSqlInterpolated($"""
                SELECT *
                FROM `admin_roles`
                WHERE `Id` = {roleIdText}
                ORDER BY `Id`
                FOR UPDATE
                """)
            .ToListAsync(cancellationToken);
        return roles.SingleOrDefault();
    }

    public async Task<IReadOnlyList<RolePermission>> GetRolePermissionsForUpdateAsync(
        Guid roleId,
        CancellationToken cancellationToken = default)
    {
        var roleIdText = roleId.ToString();
        return await _dbContext.RolePermissions
            .FromSqlInterpolated($"""
                SELECT *
                FROM `admin_role_permissions`
                WHERE `RoleId` = {roleIdText}
                ORDER BY `Name`, `Id`
                FOR UPDATE
                """)
            .ToListAsync(cancellationToken);
    }

    public async Task<AdminRole?> FindRoleByNormalizedNameForUpdateAsync(
        string normalizedName,
        CancellationToken cancellationToken = default)
    {
        var roles = await _dbContext.AdminRoles
            .FromSqlInterpolated($"""
                SELECT *
                FROM `admin_roles`
                WHERE `NormalizedName` = {normalizedName}
                ORDER BY `NormalizedName`, `Id`
                FOR UPDATE
                """)
            .ToListAsync(cancellationToken);
        return roles.SingleOrDefault();
    }

    public Task<AdminRole?> FindRoleByNormalizedNameAsync(
        string normalizedName,
        CancellationToken cancellationToken = default)
    {
        return _dbContext.AdminRoles
            .AsNoTracking()
            .SingleOrDefaultAsync(role => role.NormalizedName == normalizedName, cancellationToken);
    }

    public async Task<Administrator?> FindAdministratorForUpdateAsync(
        Guid administratorId,
        CancellationToken cancellationToken = default)
    {
        var administratorIdText = administratorId.ToString();
        var administrators = await _dbContext.Administrators
            .FromSqlInterpolated($"""
                SELECT *
                FROM `admin_administrators`
                WHERE `Id` = {administratorIdText}
                ORDER BY `Id`
                FOR UPDATE
                """)
            .ToListAsync(cancellationToken);
        return administrators.SingleOrDefault();
    }

    public async Task<IReadOnlyList<AdministratorRole>> GetAdministratorRoleMembershipsForUpdateAsync(
        Guid administratorId,
        CancellationToken cancellationToken = default)
    {
        var administratorIdText = administratorId.ToString();
        return await _dbContext.AdministratorRoles
            .FromSqlInterpolated($"""
                SELECT *
                FROM `admin_role_memberships`
                WHERE `AdministratorId` = {administratorIdText}
                ORDER BY `AdministratorId`, `RoleId`, `Id`
                FOR UPDATE
                """)
            .ToListAsync(cancellationToken);
    }

    public async Task<Administrator?> FindAdministratorByNormalizedUserNameForUpdateAsync(
        string normalizedUserName,
        CancellationToken cancellationToken = default)
    {
        var administrators = await _dbContext.Administrators
            .FromSqlInterpolated($"""
                SELECT *
                FROM `admin_administrators`
                WHERE `NormalizedUserName` = {normalizedUserName}
                ORDER BY `NormalizedUserName`, `Id`
                FOR UPDATE
                """)
            .ToListAsync(cancellationToken);
        return administrators.SingleOrDefault();
    }

    public Task<int> CountActiveAdministratorsInRoleAsync(
        Guid roleId,
        CancellationToken cancellationToken = default)
    {
        return (from membership in _dbContext.AdministratorRoles.AsNoTracking()
                join administrator in _dbContext.Administrators.AsNoTracking()
                    on membership.AdministratorId equals administrator.Id
                where membership.RoleId == roleId && administrator.IsActive
                select administrator.Id)
            .CountAsync(cancellationToken);
    }

    public Task<long> CountAdministratorsInRoleAsync(
        Guid roleId,
        CancellationToken cancellationToken = default)
    {
        return _dbContext.AdministratorRoles
            .AsNoTracking()
            .LongCountAsync(membership => membership.RoleId == roleId, cancellationToken);
    }

    public async Task<IReadOnlyList<Guid>> GetAdministratorSessionFamilyIdsAsync(
        Guid administratorId,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.RefreshTokenSessions
            .AsNoTracking()
            .Where(session => session.AdministratorId == administratorId)
            .Select(session => session.FamilyId)
            .Distinct()
            .OrderBy(familyId => familyId)
            .ToArrayAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Guid>> GetSessionFamilyOwnerIdsAsync(
        Guid familyId,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.RefreshTokenSessions
            .AsNoTracking()
            .Where(session => session.FamilyId == familyId)
            .Select(session => session.AdministratorId)
            .Distinct()
            .OrderBy(administratorId => administratorId)
            .ToArrayAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<RefreshTokenSession>> GetSessionFamilyForUpdateAsync(
        Guid familyId,
        CancellationToken cancellationToken = default)
    {
        var familyIdText = familyId.ToString();
        return await _dbContext.RefreshTokenSessions
            .FromSqlInterpolated($"""
                SELECT *
                FROM `admin_refresh_token_sessions`
                WHERE `FamilyId` = {familyIdText}
                ORDER BY `FamilyId`, `CreatedAtUtc`, `Id`
                FOR UPDATE
                """)
            .ToListAsync(cancellationToken);
    }

    public async Task AddAdministratorAsync(
        Administrator administrator,
        CancellationToken cancellationToken = default)
    {
        await _dbContext.Administrators.AddAsync(administrator, cancellationToken);
    }

    public async Task AddRoleAsync(AdminRole role, CancellationToken cancellationToken = default)
    {
        await _dbContext.AdminRoles.AddAsync(role, cancellationToken);
    }

    public async Task AddAuditAsync(
        AdminAccessAuditRecord auditRecord,
        CancellationToken cancellationToken = default)
    {
        await _dbContext.AdminAccessAuditRecords.AddAsync(auditRecord, cancellationToken);
    }

    private async Task<IReadOnlyList<AdministratorManagementSnapshot>> CreateAdministratorSnapshotsAsync(
        IReadOnlyList<AdministratorPageRow> rows,
        CancellationToken cancellationToken)
    {
        if (rows.Count == 0)
        {
            return [];
        }

        var administratorIds = rows.Select(row => row.Id).ToArray();
        var roles = await (from membership in _dbContext.AdministratorRoles.AsNoTracking()
                           join role in _dbContext.AdminRoles.AsNoTracking()
                               on membership.RoleId equals role.Id
                           where administratorIds.Contains(membership.AdministratorId)
                           orderby membership.AdministratorId, role.Name, role.Id
                           select new AdministratorRoleRow(
                               membership.AdministratorId,
                               role.Id,
                               role.Name,
                               role.IsBuiltIn,
                               role.IsActive))
            .ToArrayAsync(cancellationToken);
        var rolesByAdministrator = roles
            .GroupBy(role => role.AdministratorId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<AdminRoleReferenceSnapshot>)group
                    .OrderBy(role => role.Name, StringComparer.Ordinal)
                    .ThenBy(role => role.RoleId)
                    .Select(role => new AdminRoleReferenceSnapshot(
                        role.RoleId,
                        role.Name,
                        role.IsBuiltIn,
                        role.IsActive))
                    .ToArray());

        return rows.Select(row => new AdministratorManagementSnapshot(
                row.Id,
                row.UserName,
                row.DisplayName,
                row.IsActive,
                rolesByAdministrator.GetValueOrDefault(row.Id, []),
                row.CreatedAtUtc,
                row.UpdatedAtUtc,
                row.LastLoginAtUtc,
                row.Revision))
            .ToArray();
    }

    private async Task EnsureSessionFamiliesAreValidAsync(
        Guid? administratorId,
        CancellationToken cancellationToken)
    {
        // This validates the complete administrator-filtered universe before the state projection.
        // The page below remains domain-revalidated to defend against projection drift.
        var invalidFamilies = await _dbContext.Database
            .SqlQueryRaw<SessionFamilyValidationRow>(
                SessionFamilyValidationSql,
                administratorId.HasValue,
                administratorId?.ToString() ?? string.Empty)
            .ToArrayAsync(cancellationToken);
        if (invalidFamilies.Length != 0)
        {
            throw InvariantViolation("A refresh-token family is structurally invalid.");
        }
    }

    private async Task<IReadOnlyList<AdminRoleManagementSnapshot>> CreateRoleSnapshotsAsync(
        IReadOnlyList<AdminRolePageRow> rows,
        IReadOnlyCollection<string> registeredPermissionNames,
        CancellationToken cancellationToken)
    {
        if (rows.Count == 0)
        {
            return [];
        }

        var roleIds = rows.Select(row => row.Id).ToArray();
        var registeredNames = registeredPermissionNames
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var permissions = registeredNames.Length == 0
            ? []
            : await _dbContext.RolePermissions
                .AsNoTracking()
                .Where(permission => roleIds.Contains(permission.RoleId) &&
                    registeredNames.Contains(permission.Name))
                .OrderBy(permission => permission.RoleId)
                .ThenBy(permission => permission.Name)
                .ThenBy(permission => permission.Id)
                .Select(permission => new RolePermissionRow(permission.RoleId, permission.Name))
                .ToArrayAsync(cancellationToken);
        var administratorCounts = await _dbContext.AdministratorRoles
            .AsNoTracking()
            .Where(membership => roleIds.Contains(membership.RoleId))
            .GroupBy(membership => membership.RoleId)
            .Select(group => new RoleAdministratorCount(group.Key, group.LongCount()))
            .ToDictionaryAsync(count => count.RoleId, count => count.Count, cancellationToken);
        var permissionsByRole = permissions
            .GroupBy(permission => permission.RoleId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<string>)group
                    .Select(permission => permission.Name)
                    .Order(StringComparer.Ordinal)
                    .ToArray());

        return rows.Select(row => new AdminRoleManagementSnapshot(
                row.Id,
                row.Name,
                row.IsBuiltIn,
                row.IsActive,
                permissionsByRole.GetValueOrDefault(row.Id, []),
                administratorCounts.GetValueOrDefault(row.Id),
                row.CreatedAtUtc,
                row.UpdatedAtUtc,
                row.Revision))
            .ToArray();
    }

    private static Guid[] CanonicalizeIds(IReadOnlyCollection<Guid> ids)
    {
        ArgumentNullException.ThrowIfNull(ids);
        return ids.Distinct().Order().ToArray();
    }

    private static string EscapeLikePattern(string value)
    {
        return value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);
    }

    private static AdminAccessManagementDomainException InvariantViolation(string message)
    {
        return new AdminAccessManagementDomainException(
            AdminAccessManagementErrorCodes.InvariantViolation,
            message);
    }

    private sealed record AdministratorPageRow(
        Guid Id,
        string UserName,
        string DisplayName,
        bool IsActive,
        DateTime CreatedAtUtc,
        DateTime? UpdatedAtUtc,
        DateTime? LastLoginAtUtc,
        long Revision);

    private sealed record AdministratorIdentityRow(Guid Id, string UserName, string DisplayName);

    private sealed record AdministratorRoleRow(
        Guid AdministratorId,
        Guid RoleId,
        string Name,
        bool IsBuiltIn,
        bool IsActive);

    private sealed record AdminRolePageRow(
        Guid Id,
        string Name,
        bool IsBuiltIn,
        bool IsActive,
        DateTime CreatedAtUtc,
        DateTime? UpdatedAtUtc,
        long Revision);

    private sealed record RolePermissionRow(Guid RoleId, string Name);

    private sealed record RoleAdministratorCount(Guid RoleId, long Count);

    private sealed record SessionFamilyPageRow(
        Guid FamilyId,
        DateTime LastActivityAtUtc);

    private sealed record SessionFamilyValidationRow(Guid FamilyId);
}
