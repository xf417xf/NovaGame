using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using NovaGame.Domain.Operations.AdminAccess;
using NovaGame.Host.EntityFrameworkCore;
using Xunit;

namespace NovaGame.EntityFrameworkCore.Tests;

[Collection(MySqlDatabaseCollection.Name)]
public sealed class AdminAccessManagementMigrationTests
{
    private readonly MySqlDatabaseFixture _fixture;

    public AdminAccessManagementMigrationTests(MySqlDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task AddAdminAccessManagementMigrationIsAppliedAndTheModelHasNoPendingChanges()
    {
        await using var dbContext = _fixture.CreateDbContext();

        var migrations = await dbContext.Database.GetAppliedMigrationsAsync();

        Assert.Contains(MySqlDatabaseFixture.AdminAccessManagementMigrationId, migrations);
        Assert.False(dbContext.Database.HasPendingModelChanges());
    }

    [Fact]
    public async Task MigrationCreatesTheFrozenAdminAccessIndexesAndConstraints()
    {
        await using var dbContext = _fixture.CreateDbContext();

        var indexNames = (await ReadValuesAsync(
            dbContext,
            """
            SELECT DISTINCT `INDEX_NAME`
            FROM `information_schema`.`STATISTICS`
            WHERE `TABLE_SCHEMA` = DATABASE()
                AND `TABLE_NAME` IN (
                    'admin_administrators',
                    'admin_roles',
                    'admin_refresh_token_sessions',
                    'admin_audit_records',
                    'admin_access_command_receipts')
            """))
            .ToHashSet(StringComparer.Ordinal);
        var constraintNames = (await ReadValuesAsync(
            dbContext,
            """
            SELECT `CONSTRAINT_NAME`
            FROM `information_schema`.`TABLE_CONSTRAINTS`
            WHERE `TABLE_SCHEMA` = DATABASE()
                AND `CONSTRAINT_TYPE` = 'CHECK'
                AND `TABLE_NAME` IN (
                    'admin_administrators',
                    'admin_roles',
                    'admin_refresh_token_sessions',
                    'admin_audit_records',
                    'admin_access_command_receipts')
            """))
            .ToHashSet(StringComparer.Ordinal);

        Assert.Subset(
            indexNames,
            new HashSet<string>(StringComparer.Ordinal)
            {
                "ix_admin_administrators_active_name_id",
                "ux_admin_roles_built_in_slot",
                "ix_admin_roles_list",
                "ix_admin_roles_active_list",
                "ix_admin_refresh_sessions_family_order",
                "ix_admin_refresh_sessions_admin_family_order",
                "ix_admin_refresh_sessions_family_replacement",
                "ix_admin_audit_records_time_id",
                "ix_admin_audit_records_correlation_time_id",
                "ux_admin_access_receipts_actor_kind_key"
            });
        Assert.Subset(
            constraintNames,
            new HashSet<string>(StringComparer.Ordinal)
            {
                "ck_admin_administrators_revision",
                "ck_admin_roles_revision",
                "ck_admin_roles_built_in_identity",
                "ck_admin_refresh_sessions_revocation",
                "ck_admin_audit_records_target_revision",
                "ck_admin_access_receipts_revision",
                "ck_admin_access_receipts_command_kind"
            });
    }

    [Fact]
    public async Task MigrationUpgradesLegacyRowsAndRoundTripsTheFrozenMySqlSchema()
    {
        var legacy = LegacyAdminAccessRows.Create();
        await using var dbContext = _fixture.CreateDbContext();

        try
        {
            await ClearAdminAccessTablesAsync(dbContext);
            await MigrateToAsync(dbContext, MySqlDatabaseFixture.GameConfigMigrationId);
            await AssertLegacyMembershipSchemaAsync(dbContext);
            await SeedLegacyRowsAsync(dbContext, legacy);

            await MigrateToAsync(dbContext, MySqlDatabaseFixture.AdminAccessManagementMigrationId);
            await AssertUpgradedLegacyRowsAsync(dbContext, legacy);
            await AssertCurrentMembershipSchemaAsync(dbContext);
            await AssertFrozenCollationsAsync(dbContext);
            await AssertFrozenConstraintsAsync(dbContext, legacy);
            await AssertDatabaseDefaultsAsync(dbContext, legacy);
            await InsertValidReceiptAsync(dbContext, legacy);

            await MigrateToAsync(dbContext, MySqlDatabaseFixture.GameConfigMigrationId);
            await AssertLegacyMembershipSchemaAsync(dbContext);
            Assert.False(await TableExistsAsync(dbContext, "admin_access_command_receipts"));
            Assert.False(await ColumnExistsAsync(dbContext, "admin_administrators", "Revision"));
            Assert.False(await ColumnExistsAsync(dbContext, "admin_roles", "IsActive"));
            Assert.False(await ColumnExistsAsync(dbContext, "admin_roles", "BuiltInRoleSlot"));
            Assert.False(await ColumnExistsAsync(dbContext, "admin_audit_records", "TargetRevision"));
            await AssertLegacyRowsRemainAfterDownAsync(dbContext, legacy);

            await MigrateToAsync(dbContext, MySqlDatabaseFixture.AdminAccessManagementMigrationId);
            await AssertUpgradedLegacyRowsAsync(dbContext, legacy);
            await AssertCurrentMembershipSchemaAsync(dbContext);
            await AssertFrozenCollationsAsync(dbContext);
            await AssertBinaryPermissionUniquenessAsync(dbContext, legacy);
        }
        finally
        {
            await MigrateToAsync(dbContext, MySqlDatabaseFixture.AdminAccessManagementMigrationId);
        }
    }

    private static async Task ClearAdminAccessTablesAsync(NovaGameDbContext dbContext)
    {
        foreach (var command in new[]
        {
            "DELETE FROM `admin_access_command_receipts`",
            "DELETE FROM `admin_audit_records`",
            "DELETE FROM `admin_refresh_token_sessions`",
            "DELETE FROM `admin_role_memberships`",
            "DELETE FROM `admin_role_permissions`",
            "DELETE FROM `admin_administrators`",
            "DELETE FROM `admin_roles`"
        })
        {
            await dbContext.Database.ExecuteSqlRawAsync(command);
        }
    }

    private static async Task MigrateToAsync(NovaGameDbContext dbContext, string migrationId)
    {
        var migrator = dbContext.Database.GetService<IMigrator>();
        await migrator.MigrateAsync(migrationId);
    }

    private static async Task SeedLegacyRowsAsync(
        NovaGameDbContext dbContext,
        LegacyAdminAccessRows legacy)
    {
        await ExecuteAsync(
            dbContext,
            """
            INSERT INTO `admin_administrators` (
                `Id`, `UserName`, `NormalizedUserName`, `DisplayName`, `PasswordHash`, `IsActive`,
                `CreatedAtUtc`, `UpdatedAtUtc`, `LastLoginAtUtc`)
            VALUES (
                @administratorId, @userName, @normalizedUserName, @displayName, @passwordHash, 1,
                @createdAtUtc, NULL, NULL)
            """,
            P("@administratorId", legacy.AdministratorId.ToString("D")),
            P("@userName", legacy.UserName),
            P("@normalizedUserName", legacy.NormalizedUserName),
            P("@displayName", "Legacy migration administrator"),
            P("@passwordHash", "legacy-password-hash"),
            P("@createdAtUtc", legacy.CreatedAtUtc));
        await ExecuteAsync(
            dbContext,
            """
            INSERT INTO `admin_roles` (`Id`, `Name`, `NormalizedName`, `IsBuiltIn`, `CreatedAtUtc`)
            VALUES (@roleId, 'Administrators', 'ADMINISTRATORS', 1, @createdAtUtc)
            """,
            P("@roleId", legacy.BuiltInRoleId.ToString("D")),
            P("@createdAtUtc", legacy.CreatedAtUtc));
        await ExecuteAsync(
            dbContext,
            """
            INSERT INTO `admin_roles` (`Id`, `Name`, `NormalizedName`, `IsBuiltIn`, `CreatedAtUtc`)
            VALUES (@roleId, @roleName, @normalizedRoleName, 0, @createdAtUtc)
            """,
            P("@roleId", legacy.CustomRoleId.ToString("D")),
            P("@roleName", legacy.CustomRoleName),
            P("@normalizedRoleName", legacy.CustomRoleName.ToUpperInvariant()),
            P("@createdAtUtc", legacy.CreatedAtUtc));
        await ExecuteAsync(
            dbContext,
            """
            INSERT INTO `admin_role_memberships` (
                `Id`, `AdministratorId`, `RoleId`, `AssignedAtUtc`, `AssignedByAdministratorId`)
            VALUES (@membershipId, @administratorId, @roleId, @createdAtUtc, NULL)
            """,
            P("@membershipId", legacy.MembershipId.ToString("D")),
            P("@administratorId", legacy.AdministratorId.ToString("D")),
            P("@roleId", legacy.BuiltInRoleId.ToString("D")),
            P("@createdAtUtc", legacy.CreatedAtUtc));
        await ExecuteAsync(
            dbContext,
            """
            INSERT INTO `admin_role_permissions` (
                `Id`, `RoleId`, `Name`, `GrantedAtUtc`, `GrantedByAdministratorId`)
            VALUES (@permissionId, @roleId, @permissionName, @createdAtUtc, NULL)
            """,
            P("@permissionId", legacy.PermissionId.ToString("D")),
            P("@roleId", legacy.CustomRoleId.ToString("D")),
            P("@permissionName", legacy.PermissionName),
            P("@createdAtUtc", legacy.CreatedAtUtc));
        await ExecuteAsync(
            dbContext,
            """
            INSERT INTO `admin_refresh_token_sessions` (
                `Id`, `AdministratorId`, `FamilyId`, `TokenHash`, `CreatedAtUtc`, `ExpiresAtUtc`,
                `CreatedFromIpAddress`, `RevokedAtUtc`, `RevocationReason`, `RevokedFromIpAddress`,
                `ReplacedBySessionId`)
            VALUES (
                @sessionId, @administratorId, @familyId,
                UNHEX('000102030405060708090A0B0C0D0E0F101112131415161718191A1B1C1D1E1F'),
                @createdAtUtc, @expiresAtUtc, '203.0.113.10', NULL, NULL, NULL, NULL)
            """,
            P("@sessionId", legacy.SessionId.ToString("D")),
            P("@administratorId", legacy.AdministratorId.ToString("D")),
            P("@familyId", legacy.SessionFamilyId.ToString("D")),
            P("@createdAtUtc", legacy.CreatedAtUtc),
            P("@expiresAtUtc", legacy.CreatedAtUtc.AddDays(7)));
        await ExecuteAsync(
            dbContext,
            """
            INSERT INTO `admin_audit_records` (
                `Id`, `Action`, `OccurredAtUtc`, `Succeeded`, `AdministratorId`, `ActorAdministratorId`,
                `RoleId`, `SubjectUserName`, `PermissionName`, `ErrorCode`, `IpAddress`, `UserAgent`,
                `CorrelationId`)
            VALUES (
                @auditId, 'Login', @createdAtUtc, 1, @administratorId, @administratorId,
                @roleId, @userName, @permissionName, NULL, '203.0.113.10', 'migration-test',
                @correlationId)
            """,
            P("@auditId", legacy.AuditId.ToString("D")),
            P("@administratorId", legacy.AdministratorId.ToString("D")),
            P("@roleId", legacy.BuiltInRoleId.ToString("D")),
            P("@userName", legacy.UserName),
            P("@permissionName", legacy.PermissionName),
            P("@createdAtUtc", legacy.CreatedAtUtc),
            P("@correlationId", legacy.CorrelationId));
        await ExecuteAsync(
            dbContext,
            """
            INSERT INTO `game_config_drafts` (
                `Id`, `ConfigurationSetKey`, `Name`, `BaseVersionId`, `SchemaVersion`, `RawContentJson`,
                `Revision`, `CreatedByAdministratorId`, `CreatedAtUtc`, `UpdatedByAdministratorId`,
                `UpdatedAtUtc`, `ValidationStatus`, `LastValidatedRevision`, `ValidationProfile`,
                `LastValidatedByAdministratorId`, `LastValidatedAtUtc`, `PublishedVersionId`)
            VALUES (
                @draftId, @configurationSetKey, @draftName, NULL, '1', '{"collections":{}}',
                1, @administratorId, @createdAtUtc, @administratorId, @createdAtUtc, 'NotValidated',
                NULL, NULL, NULL, NULL, NULL)
            """,
            P("@draftId", legacy.GameConfigDraftId.ToString("D")),
            P("@configurationSetKey", legacy.ConfigurationSetKey),
            P("@draftName", legacy.GameConfigDraftName),
            P("@administratorId", legacy.AdministratorId.ToString("D")),
            P("@createdAtUtc", legacy.CreatedAtUtc));
    }

    private static async Task AssertUpgradedLegacyRowsAsync(
        NovaGameDbContext dbContext,
        LegacyAdminAccessRows legacy)
    {
        Assert.Equal(
            1L,
            await ReadLongAsync(
                dbContext,
                "SELECT `Revision` AS `Value` FROM `admin_administrators` WHERE `Id` = @administratorId",
                P("@administratorId", legacy.AdministratorId.ToString("D"))));
        Assert.Equal(
            legacy.UserName,
            await ReadRequiredStringAsync(
                dbContext,
                "SELECT `UserName` AS `Value` FROM `admin_administrators` WHERE `Id` = @administratorId",
                P("@administratorId", legacy.AdministratorId.ToString("D"))));
        Assert.Equal(
            1L,
            await ReadLongAsync(
                dbContext,
                "SELECT `Revision` AS `Value` FROM `admin_roles` WHERE `Id` = @roleId",
                P("@roleId", legacy.BuiltInRoleId.ToString("D"))));
        Assert.Equal(
            1L,
            await ReadLongAsync(
                dbContext,
                "SELECT `IsActive` AS `Value` FROM `admin_roles` WHERE `Id` = @roleId",
                P("@roleId", legacy.BuiltInRoleId.ToString("D"))));
        Assert.Equal(
            1L,
            await ReadLongAsync(
                dbContext,
                "SELECT `BuiltInRoleSlot` AS `Value` FROM `admin_roles` WHERE `Id` = @roleId",
                P("@roleId", legacy.BuiltInRoleId.ToString("D"))));
        Assert.Null(
            await ReadValueAsync(
                dbContext,
                "SELECT `UpdatedAtUtc` AS `Value` FROM `admin_roles` WHERE `Id` = @roleId",
                P("@roleId", legacy.BuiltInRoleId.ToString("D"))));
        Assert.Equal(
            1L,
            await ReadLongAsync(
                dbContext,
                """
                SELECT COUNT(*) AS `Value`
                FROM `admin_role_memberships`
                WHERE `Id` = @membershipId
                    AND `AdministratorId` = @administratorId
                    AND `RoleId` = @roleId
                """,
                P("@membershipId", legacy.MembershipId.ToString("D")),
                P("@administratorId", legacy.AdministratorId.ToString("D")),
                P("@roleId", legacy.BuiltInRoleId.ToString("D"))));
        Assert.Equal(
            1L,
            await ReadLongAsync(
                dbContext,
                "SELECT COUNT(*) AS `Value` FROM `admin_role_permissions` WHERE `Id` = @permissionId",
                P("@permissionId", legacy.PermissionId.ToString("D"))));
        Assert.Equal(
            1L,
            await ReadLongAsync(
                dbContext,
                "SELECT COUNT(*) AS `Value` FROM `admin_refresh_token_sessions` WHERE `Id` = @sessionId",
                P("@sessionId", legacy.SessionId.ToString("D"))));
        Assert.Equal(
            1L,
            await ReadLongAsync(
                dbContext,
                "SELECT COUNT(*) AS `Value` FROM `admin_audit_records` WHERE `Id` = @auditId",
                P("@auditId", legacy.AuditId.ToString("D"))));
        Assert.Null(
            await ReadValueAsync(
                dbContext,
                "SELECT `SessionFamilyId` AS `Value` FROM `admin_audit_records` WHERE `Id` = @auditId",
                P("@auditId", legacy.AuditId.ToString("D"))));
        Assert.Null(
            await ReadValueAsync(
                dbContext,
                "SELECT `TargetRevision` AS `Value` FROM `admin_audit_records` WHERE `Id` = @auditId",
                P("@auditId", legacy.AuditId.ToString("D"))));
        Assert.Equal(
            1L,
            await ReadLongAsync(
                dbContext,
                "SELECT COUNT(*) AS `Value` FROM `game_config_drafts` WHERE `Id` = @draftId",
                P("@draftId", legacy.GameConfigDraftId.ToString("D"))));

        var administratorRevision = dbContext.Model
            .FindEntityType(typeof(Administrator))!
            .FindProperty("Revision")!;
        var roleRevision = dbContext.Model
            .FindEntityType(typeof(AdminRole))!
            .FindProperty("Revision")!;
        Assert.True(administratorRevision.IsConcurrencyToken);
        Assert.True(roleRevision.IsConcurrencyToken);
    }

    private static async Task AssertLegacyRowsRemainAfterDownAsync(
        NovaGameDbContext dbContext,
        LegacyAdminAccessRows legacy)
    {
        Assert.Equal(
            legacy.UserName,
            await ReadRequiredStringAsync(
                dbContext,
                "SELECT `UserName` AS `Value` FROM `admin_administrators` WHERE `Id` = @administratorId",
                P("@administratorId", legacy.AdministratorId.ToString("D"))));
        Assert.Equal(
            1L,
            await ReadLongAsync(
                dbContext,
                "SELECT COUNT(*) AS `Value` FROM `admin_roles` WHERE `Id` = @roleId",
                P("@roleId", legacy.BuiltInRoleId.ToString("D"))));
        Assert.Equal(
            1L,
            await ReadLongAsync(
                dbContext,
                "SELECT COUNT(*) AS `Value` FROM `admin_refresh_token_sessions` WHERE `Id` = @sessionId",
                P("@sessionId", legacy.SessionId.ToString("D"))));
        Assert.Equal(
            1L,
            await ReadLongAsync(
                dbContext,
                "SELECT COUNT(*) AS `Value` FROM `admin_audit_records` WHERE `Id` = @auditId",
                P("@auditId", legacy.AuditId.ToString("D"))));
        Assert.Equal(
            1L,
            await ReadLongAsync(
                dbContext,
                "SELECT COUNT(*) AS `Value` FROM `game_config_drafts` WHERE `Id` = @draftId",
                P("@draftId", legacy.GameConfigDraftId.ToString("D"))));
    }

    private static async Task AssertLegacyMembershipSchemaAsync(NovaGameDbContext dbContext)
    {
        Assert.Equal(
            ["RoleId"],
            await ReadIndexColumnsAsync(dbContext, "ix_admin_role_memberships_role"));
        Assert.Empty(await ReadIndexColumnsAsync(dbContext, "ix_admin_role_memberships_role_admin"));
    }

    private static async Task AssertCurrentMembershipSchemaAsync(NovaGameDbContext dbContext)
    {
        Assert.Equal(
            ["RoleId", "AdministratorId"],
            await ReadIndexColumnsAsync(dbContext, "ix_admin_role_memberships_role_admin"));
        Assert.Empty(await ReadIndexColumnsAsync(dbContext, "ix_admin_role_memberships_role"));
    }

    private static async Task AssertFrozenCollationsAsync(NovaGameDbContext dbContext)
    {
        foreach (var expected in new[]
        {
            new ColumnCollation("admin_administrators", "NormalizedUserName", "utf8mb4_bin"),
            new ColumnCollation("admin_roles", "Name", "utf8mb4_bin"),
            new ColumnCollation("admin_roles", "NormalizedName", "utf8mb4_bin"),
            new ColumnCollation("admin_role_permissions", "Name", "utf8mb4_bin"),
            new ColumnCollation("admin_audit_records", "Action", "utf8mb4_bin"),
            new ColumnCollation("admin_audit_records", "PermissionName", "utf8mb4_bin"),
            new ColumnCollation("admin_audit_records", "CorrelationId", "utf8mb4_bin"),
            new ColumnCollation("admin_access_command_receipts", "CommandKind", "utf8mb4_bin"),
            new ColumnCollation("admin_access_command_receipts", "IdempotencyKey", "ascii_bin")
        })
        {
            Assert.Equal(
                expected.Collation,
                await ReadRequiredStringAsync(
                    dbContext,
                    """
                    SELECT `COLLATION_NAME` AS `Value`
                    FROM `information_schema`.`COLUMNS`
                    WHERE `TABLE_SCHEMA` = DATABASE()
                        AND `TABLE_NAME` = @tableName
                        AND `COLUMN_NAME` = @columnName
                    """,
                    P("@tableName", expected.TableName),
                    P("@columnName", expected.ColumnName)));
        }
    }

    private static async Task AssertFrozenConstraintsAsync(
        NovaGameDbContext dbContext,
        LegacyAdminAccessRows legacy)
    {
        await AssertDatabaseRejectsAsync(() => ExecuteAsync(
            dbContext,
            "UPDATE `admin_administrators` SET `Revision` = 0 WHERE `Id` = @administratorId",
            P("@administratorId", legacy.AdministratorId.ToString("D"))));
        await AssertDatabaseRejectsAsync(() => ExecuteAsync(
            dbContext,
            "UPDATE `admin_roles` SET `Revision` = 0 WHERE `Id` = @roleId",
            P("@roleId", legacy.BuiltInRoleId.ToString("D"))));
        await AssertDatabaseRejectsAsync(() => ExecuteAsync(
            dbContext,
            "UPDATE `admin_roles` SET `IsActive` = 0 WHERE `Id` = @roleId",
            P("@roleId", legacy.BuiltInRoleId.ToString("D"))));
        await AssertDatabaseRejectsAsync(() => ExecuteAsync(
            dbContext,
            "UPDATE `admin_audit_records` SET `TargetRevision` = 0 WHERE `Id` = @auditId",
            P("@auditId", legacy.AuditId.ToString("D"))));
        await AssertDatabaseRejectsAsync(() => ExecuteAsync(
            dbContext,
            "UPDATE `admin_refresh_token_sessions` SET `RevokedAtUtc` = @revokedAtUtc WHERE `Id` = @sessionId",
            P("@revokedAtUtc", legacy.CreatedAtUtc.AddHours(1)),
            P("@sessionId", legacy.SessionId.ToString("D"))));
        await AssertDatabaseRejectsAsync(() => ExecuteAsync(
            dbContext,
            """
            INSERT INTO `admin_access_command_receipts` (
                `Id`, `ActorAdministratorId`, `CommandKind`, `IdempotencyKey`, `RequestFingerprint`,
                `TargetResourceId`, `ResultingRevision`, `CreatedAtUtc`)
            VALUES (
                @receiptId, @administratorId, 'NotACommand', @idempotencyKey,
                UNHEX('000102030405060708090A0B0C0D0E0F101112131415161718191A1B1C1D1E1F'),
                @targetResourceId, 1, @createdAtUtc)
            """,
            P("@receiptId", Guid.NewGuid().ToString("D")),
            P("@administratorId", legacy.AdministratorId.ToString("D")),
            P("@idempotencyKey", Guid.NewGuid().ToString("D")),
            P("@targetResourceId", Guid.NewGuid().ToString("D")),
            P("@createdAtUtc", legacy.CreatedAtUtc)));
    }

    private static async Task AssertDatabaseDefaultsAsync(
        NovaGameDbContext dbContext,
        LegacyAdminAccessRows legacy)
    {
        await ExecuteAsync(
            dbContext,
            """
            INSERT INTO `admin_roles` (`Id`, `Name`, `NormalizedName`, `IsBuiltIn`, `CreatedAtUtc`)
            VALUES (@roleId, @roleName, @normalizedRoleName, 0, @createdAtUtc)
            """,
            P("@roleId", legacy.PostUpgradeRoleId.ToString("D")),
            P("@roleName", legacy.PostUpgradeRoleName),
            P("@normalizedRoleName", legacy.PostUpgradeRoleName.ToUpperInvariant()),
            P("@createdAtUtc", legacy.CreatedAtUtc));

        Assert.Equal(
            1L,
            await ReadLongAsync(
                dbContext,
                "SELECT `IsActive` AS `Value` FROM `admin_roles` WHERE `Id` = @roleId",
                P("@roleId", legacy.PostUpgradeRoleId.ToString("D"))));
        Assert.Equal(
            1L,
            await ReadLongAsync(
                dbContext,
                "SELECT `Revision` AS `Value` FROM `admin_roles` WHERE `Id` = @roleId",
                P("@roleId", legacy.PostUpgradeRoleId.ToString("D"))));
    }

    private static async Task InsertValidReceiptAsync(
        NovaGameDbContext dbContext,
        LegacyAdminAccessRows legacy)
    {
        await ExecuteAsync(
            dbContext,
            """
            INSERT INTO `admin_access_command_receipts` (
                `Id`, `ActorAdministratorId`, `CommandKind`, `IdempotencyKey`, `RequestFingerprint`,
                `TargetResourceId`, `ResultingRevision`, `CreatedAtUtc`)
            VALUES (
                @receiptId, @administratorId, 'AdministratorCreate', @idempotencyKey,
                UNHEX('000102030405060708090A0B0C0D0E0F101112131415161718191A1B1C1D1E1F'),
                @targetResourceId, 1, @createdAtUtc)
            """,
            P("@receiptId", legacy.ReceiptId.ToString("D")),
            P("@administratorId", legacy.AdministratorId.ToString("D")),
            P("@idempotencyKey", legacy.IdempotencyKey.ToString("D")),
            P("@targetResourceId", legacy.AdministratorId.ToString("D")),
            P("@createdAtUtc", legacy.CreatedAtUtc));
    }

    private static async Task AssertBinaryPermissionUniquenessAsync(
        NovaGameDbContext dbContext,
        LegacyAdminAccessRows legacy)
    {
        await ExecuteAsync(
            dbContext,
            """
            INSERT INTO `admin_role_permissions` (
                `Id`, `RoleId`, `Name`, `GrantedAtUtc`, `GrantedByAdministratorId`)
            VALUES (@permissionId, @roleId, @permissionName, @createdAtUtc, NULL)
            """,
            P("@permissionId", Guid.NewGuid().ToString("D")),
            P("@roleId", legacy.PostUpgradeRoleId.ToString("D")),
            P("@permissionName", "Permission.CaseSensitive"),
            P("@createdAtUtc", legacy.CreatedAtUtc));
        await ExecuteAsync(
            dbContext,
            """
            INSERT INTO `admin_role_permissions` (
                `Id`, `RoleId`, `Name`, `GrantedAtUtc`, `GrantedByAdministratorId`)
            VALUES (@permissionId, @roleId, @permissionName, @createdAtUtc, NULL)
            """,
            P("@permissionId", Guid.NewGuid().ToString("D")),
            P("@roleId", legacy.PostUpgradeRoleId.ToString("D")),
            P("@permissionName", "permission.casesensitive"),
            P("@createdAtUtc", legacy.CreatedAtUtc));

        Assert.Equal(
            2L,
            await ReadLongAsync(
                dbContext,
                """
                SELECT COUNT(*) AS `Value`
                FROM `admin_role_permissions`
                WHERE `RoleId` = @roleId
                    AND `Name` IN ('Permission.CaseSensitive', 'permission.casesensitive')
                """,
                P("@roleId", legacy.PostUpgradeRoleId.ToString("D"))));
    }

    private static async Task AssertDatabaseRejectsAsync(Func<Task> operation)
    {
        var exception = await Record.ExceptionAsync(operation);
        Assert.NotNull(exception);
    }

    private static async Task<bool> TableExistsAsync(NovaGameDbContext dbContext, string tableName)
    {
        return await ReadLongAsync(
            dbContext,
            """
            SELECT COUNT(*) AS `Value`
            FROM `information_schema`.`TABLES`
            WHERE `TABLE_SCHEMA` = DATABASE()
                AND `TABLE_NAME` = @tableName
            """,
            P("@tableName", tableName)) == 1;
    }

    private static async Task<bool> ColumnExistsAsync(
        NovaGameDbContext dbContext,
        string tableName,
        string columnName)
    {
        return await ReadLongAsync(
            dbContext,
            """
            SELECT COUNT(*) AS `Value`
            FROM `information_schema`.`COLUMNS`
            WHERE `TABLE_SCHEMA` = DATABASE()
                AND `TABLE_NAME` = @tableName
                AND `COLUMN_NAME` = @columnName
            """,
            P("@tableName", tableName),
            P("@columnName", columnName)) == 1;
    }

    private static Task<IReadOnlyList<string>> ReadIndexColumnsAsync(
        NovaGameDbContext dbContext,
        string indexName)
    {
        return ReadValuesAsync(
            dbContext,
            """
            SELECT `COLUMN_NAME`
            FROM `information_schema`.`STATISTICS`
            WHERE `TABLE_SCHEMA` = DATABASE()
                AND `TABLE_NAME` = 'admin_role_memberships'
                AND `INDEX_NAME` = @indexName
            ORDER BY `SEQ_IN_INDEX`
            """,
            P("@indexName", indexName));
    }

    private static async Task<long> ReadLongAsync(
        NovaGameDbContext dbContext,
        string commandText,
        params SqlParameterValue[] parameters)
    {
        var value = await ReadValueAsync(dbContext, commandText, parameters);
        return Convert.ToInt64(value ?? throw new InvalidOperationException("Expected a scalar value."));
    }

    private static async Task<string> ReadRequiredStringAsync(
        NovaGameDbContext dbContext,
        string commandText,
        params SqlParameterValue[] parameters)
    {
        return Convert.ToString(
                   await ReadValueAsync(dbContext, commandText, parameters) ??
                   throw new InvalidOperationException("Expected a scalar value.")) ??
               throw new InvalidOperationException("Expected a string value.");
    }

    private static Task ExecuteAsync(
        NovaGameDbContext dbContext,
        string commandText,
        params SqlParameterValue[] parameters)
    {
        return WithCommandAsync(
            dbContext,
            commandText,
            parameters,
            async command =>
            {
                await command.ExecuteNonQueryAsync();
                return true;
            });
    }

    private static Task<object?> ReadValueAsync(
        NovaGameDbContext dbContext,
        string commandText,
        params SqlParameterValue[] parameters)
    {
        return WithCommandAsync(
            dbContext,
            commandText,
            parameters,
            async command =>
            {
                var value = await command.ExecuteScalarAsync();
                return value is DBNull ? null : value;
            });
    }

    private static async Task<IReadOnlyList<string>> ReadValuesAsync(
        NovaGameDbContext dbContext,
        string commandText,
        params SqlParameterValue[] parameters)
    {
        return await WithCommandAsync<IReadOnlyList<string>>(
            dbContext,
            commandText,
            parameters,
            async command =>
            {
                await using var reader = await command.ExecuteReaderAsync();
                var values = new List<string>();
                while (await reader.ReadAsync())
                {
                    values.Add(reader.GetString(0));
                }

                return values;
            });
    }

    private static async Task<T> WithCommandAsync<T>(
        NovaGameDbContext dbContext,
        string commandText,
        IReadOnlyCollection<SqlParameterValue> parameters,
        Func<DbCommand, Task<T>> operation)
    {
        var connection = dbContext.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        foreach (var parameterValue in parameters)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = parameterValue.Name;
            parameter.Value = parameterValue.Value ?? DBNull.Value;
            command.Parameters.Add(parameter);
        }

        return await operation(command);
    }

    private sealed record SqlParameterValue(string Name, object? Value);

    private static SqlParameterValue P(string name, object? value)
    {
        return new SqlParameterValue(name, value);
    }

    private sealed record ColumnCollation(string TableName, string ColumnName, string Collation);

    private sealed record LegacyAdminAccessRows(
        Guid AdministratorId,
        Guid BuiltInRoleId,
        Guid CustomRoleId,
        Guid PostUpgradeRoleId,
        Guid MembershipId,
        Guid PermissionId,
        Guid SessionId,
        Guid SessionFamilyId,
        Guid AuditId,
        Guid ReceiptId,
        Guid IdempotencyKey,
        Guid GameConfigDraftId,
        string UserName,
        string NormalizedUserName,
        string CustomRoleName,
        string PostUpgradeRoleName,
        string PermissionName,
        string CorrelationId,
        string ConfigurationSetKey,
        string GameConfigDraftName,
        DateTime CreatedAtUtc)
    {
        public static LegacyAdminAccessRows Create()
        {
            var suffix = Guid.NewGuid().ToString("N");
            var userName = $"migration-{suffix}";
            return new LegacyAdminAccessRows(
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                userName,
                userName.ToUpperInvariant(),
                $"Operators-{suffix}",
                $"Defaults-{suffix}",
                "NovaGame.Test.Permission",
                $"migration-{suffix}",
                $"migration-{suffix}",
                $"draft-{suffix}",
                new DateTime(2026, 8, 15, 6, 0, 0, DateTimeKind.Utc));
        }
    }
}
