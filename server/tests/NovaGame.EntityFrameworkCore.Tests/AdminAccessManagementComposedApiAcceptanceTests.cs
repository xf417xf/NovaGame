using System.Collections.Concurrent;
using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using MySqlConnector;
using NovaCore.BuildingBlocks.Exceptions;
using NovaGame.Api.Operations.AdminAccess;
using NovaGame.Application.Contracts.Operations.AdminAccess;
using NovaGame.Domain.Operations.AdminAccess;
using NovaGame.Domain.Shared.Operations.AdminAccess;
using Xunit;

namespace NovaGame.EntityFrameworkCore.Tests;

public sealed class AdminAccessManagementComposedApiAcceptanceTests
{
    private const string ApiUrlVariable = "NOVAGAME_ADMIN_MANAGEMENT_COMPOSED_API_URL";
    private const string MySqlConnectionStringVariable =
        "NOVAGAME_ADMIN_MANAGEMENT_COMPOSED_MYSQL_CONNECTION_STRING";
    private const string ProjectNameVariable = "NOVAGAME_ADMIN_MANAGEMENT_COMPOSED_PROJECT_NAME";
    private const string DisposableMarkerVariable =
        "NOVAGAME_ADMIN_MANAGEMENT_COMPOSED_DISPOSABLE_MARKER";
    private const string BootstrapUserNameVariable =
        "NOVAGAME_ADMIN_MANAGEMENT_COMPOSED_BOOTSTRAP_USER_NAME";
    private const string BootstrapPasswordVariable =
        "NOVAGAME_ADMIN_MANAGEMENT_COMPOSED_BOOTSTRAP_PASSWORD";
    private const string ApiStandardOutputPathVariable =
        "NOVAGAME_ADMIN_MANAGEMENT_COMPOSED_API_STDOUT_PATH";
    private const string ApiStandardErrorPathVariable =
        "NOVAGAME_ADMIN_MANAGEMENT_COMPOSED_API_STDERR_PATH";
    private const string DbMigratorStandardOutputPathVariable =
        "NOVAGAME_ADMIN_MANAGEMENT_COMPOSED_DBMIGRATOR_STDOUT_PATH";
    private const string DbMigratorStandardErrorPathVariable =
        "NOVAGAME_ADMIN_MANAGEMENT_COMPOSED_DBMIGRATOR_STDERR_PATH";
    private const string DisposableMarker = "novagame-admin-management-composed-v1";
    private const string UserAgent = "NovaGame-M356-T02/1.0";
    private const string AuditFailureConstraintName = "ck_m356_t03_audit_failure";
    private const string AdminAccessManagementMigrationId =
        "20260815030331_AddAdminAccessManagement";
    private static readonly HashSet<string> HttpMethods =
    [
        "delete", "get", "head", "options", "patch", "post", "put", "trace"
    ];
    private static readonly ConditionalWeakTable<HttpClient, RedactionRecorder> RedactionRecorders = new();

    private readonly Uri _apiBaseUri;
    private readonly string _mySqlConnectionString;
    private readonly string _bootstrapUserName;
    private readonly string _bootstrapPassword;
    private readonly IReadOnlyList<string> _composedLogPaths;
    private readonly ConcurrentQueue<string> _managementResponseBodies = new();
    private readonly ConcurrentQueue<string> _recordedRequestBodies = new();
    private readonly ConcurrentQueue<string> _recordedTransportSecrets = new();

    public AdminAccessManagementComposedApiAcceptanceTests()
    {
        _apiBaseUri = CreateApiBaseUri(RequireEnvironmentVariable(ApiUrlVariable));
        _mySqlConnectionString = RequireEnvironmentVariable(MySqlConnectionStringVariable);
        _bootstrapUserName = RequireEnvironmentVariable(BootstrapUserNameVariable);
        _bootstrapPassword = RequireEnvironmentVariable(BootstrapPasswordVariable);
        _composedLogPaths =
        [
            RequireEnvironmentVariable(ApiStandardOutputPathVariable),
            RequireEnvironmentVariable(ApiStandardErrorPathVariable),
            RequireEnvironmentVariable(DbMigratorStandardOutputPathVariable),
            RequireEnvironmentVariable(DbMigratorStandardErrorPathVariable)
        ];

        Assert.Equal(DisposableMarker, RequireEnvironmentVariable(DisposableMarkerVariable));
        Assert.Matches(
            "^novagame-test-[a-f0-9]{12}$",
            RequireEnvironmentVariable(ProjectNameVariable));
        Assert.False(string.IsNullOrWhiteSpace(new MySqlConnectionStringBuilder(_mySqlConnectionString).Database));
    }

    [Fact]
    public async Task RealComposedServerExecutesTheFrozenAdministratorManagementLifecycleAsync()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var prefix = $"m356-{suffix}";
        var bootstrapCorrelationId = $"{prefix}-bootstrap";
        var targetCorrelationId = $"{prefix}-target";
        var initialPassword = $"M356-Initial-{suffix}-Password!";
        var resetPassword = $"M356-Reset-{suffix}-Password!";
        var targetUserName = $"{prefix}-administrator-b";
        var secondaryUserName = $"{prefix}-administrator-a";
        var targetDisplayName = "M356 Target Administrator";
        var updatedDisplayName = "M356 Updated Target Administrator";
        var roleAName = $"{prefix}-role-a";
        var renamedRoleAName = $"{prefix}-role-a-renamed";
        var roleBName = $"{prefix}-role-b";

        using var bootstrapClient = CreateClient(bootstrapCorrelationId);
        using var targetClient = CreateClient(targetCorrelationId);

        var bootstrapSession = await LoginAsync(
            bootstrapClient,
            _bootstrapUserName,
            _bootstrapPassword);
        AssertAccessTokenLifetime(bootstrapSession.AccessToken);
        AssertRefreshCookie(bootstrapSession.RefreshCookie);
        await AssertBootstrapMigrationAndSeedAsync();

        var permissionCatalog = await GetPermissionCatalogAsync(
            bootstrapClient,
            bootstrapSession.AccessToken);
        var registeredPermissions = permissionCatalog
            .SelectMany(group => group.Permissions)
            .Select(permission => permission.Name)
            .ToHashSet(StringComparer.Ordinal);
        var accessPermission = RequireRegisteredPermission(
            registeredPermissions,
            AdminAccessPermissions.Access);
        var administratorsReadPermission = RequireRegisteredPermission(
            registeredPermissions,
            AdminAccessPermissions.AdministratorsRead);

        var primaryRole = await CreateRoleAsync(
            bootstrapClient,
            bootstrapSession.AccessToken,
            roleAName,
            [accessPermission]);
        Assert.Equal(1, primaryRole.Revision);

        primaryRole = await ReplaceRolePermissionsAsync(
            bootstrapClient,
            bootstrapSession.AccessToken,
            primaryRole,
            [accessPermission, administratorsReadPermission]);
        Assert.Equal(2, primaryRole.Revision);

        primaryRole = await RenameRoleAsync(
            bootstrapClient,
            bootstrapSession.AccessToken,
            primaryRole,
            renamedRoleAName);
        Assert.Equal(3, primaryRole.Revision);

        var loadedPrimaryRole = await GetRoleAsync(
            bootstrapClient,
            bootstrapSession.AccessToken,
            primaryRole.Id);
        AssertRoleProjectionEqual(primaryRole, loadedPrimaryRole);

        var alternateRole = await CreateRoleAsync(
            bootstrapClient,
            bootstrapSession.AccessToken,
            roleBName,
            [accessPermission, administratorsReadPermission]);
        Assert.Equal(1, alternateRole.Revision);

        var targetAdministrator = await CreateAdministratorAsync(
            bootstrapClient,
            bootstrapSession.AccessToken,
            targetUserName,
            targetDisplayName,
            initialPassword,
            [primaryRole.Id]);
        Assert.Equal(1, targetAdministrator.Revision);

        var secondaryAdministrator = await CreateAdministratorAsync(
            bootstrapClient,
            bootstrapSession.AccessToken,
            secondaryUserName,
            "M356 Secondary Administrator",
            $"M356-Secondary-{suffix}-Password!",
            [alternateRole.Id]);
        Assert.Equal(1, secondaryAdministrator.Revision);

        await AssertAdministratorPagingAsync(
            bootstrapClient,
            bootstrapSession.AccessToken,
            prefix,
            targetAdministrator.Id,
            secondaryAdministrator.Id);

        var loadedTarget = await GetAdministratorAsync(
            bootstrapClient,
            bootstrapSession.AccessToken,
            targetAdministrator.Id);
        AssertAdministratorProjectionEqual(targetAdministrator, loadedTarget);

        targetAdministrator = await ChangeAdministratorDisplayNameAsync(
            bootstrapClient,
            bootstrapSession.AccessToken,
            targetAdministrator,
            updatedDisplayName);
        Assert.Equal(2, targetAdministrator.Revision);

        targetAdministrator = await ReplaceAdministratorRolesAsync(
            bootstrapClient,
            bootstrapSession.AccessToken,
            targetAdministrator,
            [alternateRole.Id]);
        Assert.Equal(3, targetAdministrator.Revision);

        targetAdministrator = await ReplaceAdministratorRolesAsync(
            bootstrapClient,
            bootstrapSession.AccessToken,
            targetAdministrator,
            [primaryRole.Id]);
        Assert.Equal(4, targetAdministrator.Revision);

        var targetSession = await LoginAsync(targetClient, targetUserName, initialPassword);
        AssertAccessTokenLifetime(targetSession.AccessToken);
        await AssertTargetHasCurrentRolePermissionsAsync(
            targetClient,
            targetSession.AccessToken,
            targetAdministrator.Id,
            primaryRole.Name,
            [accessPermission, administratorsReadPermission]);

        primaryRole = await SetRoleActiveStateAsync(
            bootstrapClient,
            bootstrapSession.AccessToken,
            primaryRole,
            isActive: false);
        Assert.Equal(4, primaryRole.Revision);
        await AssertRoleStateBlocksExistingSessionAsync(targetClient, targetSession.AccessToken);

        primaryRole = await SetRoleActiveStateAsync(
            bootstrapClient,
            bootstrapSession.AccessToken,
            primaryRole,
            isActive: true);
        Assert.Equal(5, primaryRole.Revision);
        await AssertTargetHasCurrentRolePermissionsAsync(
            targetClient,
            targetSession.AccessToken,
            targetAdministrator.Id,
            primaryRole.Name,
            [accessPermission, administratorsReadPermission]);

        primaryRole = await ReplaceRolePermissionsAsync(
            bootstrapClient,
            bootstrapSession.AccessToken,
            primaryRole,
            [accessPermission]);
        Assert.Equal(6, primaryRole.Revision);
        await AssertLeafPermissionRemovalUpdatesTheExistingSessionAsync(
            targetClient,
            targetSession.AccessToken,
            targetAdministrator.Id,
            primaryRole.Name,
            accessPermission);

        primaryRole = await ReplaceRolePermissionsAsync(
            bootstrapClient,
            bootstrapSession.AccessToken,
            primaryRole,
            [accessPermission, administratorsReadPermission]);
        Assert.Equal(7, primaryRole.Revision);
        await AssertTargetHasCurrentRolePermissionsAsync(
            targetClient,
            targetSession.AccessToken,
            targetAdministrator.Id,
            primaryRole.Name,
            [accessPermission, administratorsReadPermission]);

        targetAdministrator = await ResetAdministratorPasswordAsync(
            bootstrapClient,
            bootstrapSession.AccessToken,
            targetAdministrator,
            resetPassword);
        Assert.Equal(5, targetAdministrator.Revision);

        var afterResetSessions = await ListSessionsAsync(
            bootstrapClient,
            bootstrapSession.AccessToken,
            targetAdministrator.Id,
            state: null,
            page: 1,
            pageSize: 100);
        var passwordResetFamily = Assert.Single(
            afterResetSessions.Items,
            family => family.State == (int)AdminSessionFamilyState.Revoked &&
                family.RevocationReason == (int)AdminSessionRevocationReason.PasswordReset);
        AssertRedactedSessionBody(afterResetSessions.RawBody, targetSession.RefreshToken, initialPassword, resetPassword);

        await AssertRefreshRejectedAsync(targetClient, targetSession.RefreshToken);
        await AssertCurrentAdministratorAsync(
            targetClient,
            targetSession.AccessToken,
            targetAdministrator.Id,
            primaryRole.Name,
            [accessPermission, administratorsReadPermission]);

        var postResetSession = await LoginAsync(targetClient, targetUserName, resetPassword);
        AssertAccessTokenLifetime(postResetSession.AccessToken);

        targetAdministrator = await SetAdministratorActiveStateAsync(
            bootstrapClient,
            bootstrapSession.AccessToken,
            targetAdministrator,
            isActive: false);
        Assert.Equal(6, targetAdministrator.Revision);

        var afterDeactivationSessions = await ListSessionsAsync(
            bootstrapClient,
            bootstrapSession.AccessToken,
            targetAdministrator.Id,
            state: null,
            page: 1,
            pageSize: 100);
        _ = Assert.Single(
            afterDeactivationSessions.Items,
            family => family.State == (int)AdminSessionFamilyState.Revoked &&
                family.RevocationReason == (int)AdminSessionRevocationReason.AdministratorInactive);
        Assert.Contains(afterDeactivationSessions.Items, family => family.FamilyId == passwordResetFamily.FamilyId);
        await AssertRefreshRejectedAsync(targetClient, postResetSession.RefreshToken);

        targetAdministrator = await SetAdministratorActiveStateAsync(
            bootstrapClient,
            bootstrapSession.AccessToken,
            targetAdministrator,
            isActive: true);
        Assert.Equal(7, targetAdministrator.Revision);

        var postActivationSession = await LoginAsync(targetClient, targetUserName, resetPassword);
        AssertAccessTokenLifetime(postActivationSession.AccessToken);
        var activeSessions = await ListSessionsAsync(
            bootstrapClient,
            bootstrapSession.AccessToken,
            targetAdministrator.Id,
            state: (int)AdminSessionFamilyState.Active,
            page: 1,
            pageSize: 100);
        var activeFamily = Assert.Single(activeSessions.Items);

        var revokedFamily = await RevokeSessionFamilyAsync(
            bootstrapClient,
            bootstrapSession.AccessToken,
            activeFamily.FamilyId);
        Assert.Equal(activeFamily.FamilyId, revokedFamily.FamilyId);
        Assert.Equal((int)AdminSessionFamilyState.Revoked, revokedFamily.State);
        Assert.Equal((int)AdminSessionRevocationReason.ForcedByAdministrator, revokedFamily.RevocationReason);
        AssertRedactedSessionBody(revokedFamily.RawBody, postActivationSession.RefreshToken, resetPassword);
        await AssertRefreshRejectedAsync(targetClient, postActivationSession.RefreshToken);

        await AssertRolePagingAsync(
            bootstrapClient,
            bootstrapSession.AccessToken,
            prefix,
            primaryRole.Id,
            alternateRole.Id);
        await AssertSessionPagingAsync(
            bootstrapClient,
            bootstrapSession.AccessToken,
            targetAdministrator.Id);
        await AssertAuditPagingAsync(
            bootstrapClient,
            bootstrapSession.AccessToken,
            bootstrapCorrelationId,
            primaryRole.Id);
        await AssertDurableLifecycleStateAsync(
            targetUserName,
            secondaryUserName,
            targetAdministrator.Id,
            primaryRole.Id,
            alternateRole.Id,
            bootstrapCorrelationId);
        await AssertRemoteOpenApiContractAsync(bootstrapClient);
        await AssertFrozenAdministratorManagementSecurityContractAsync();
    }

    private async Task AssertFrozenAdministratorManagementSecurityContractAsync()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var prefix = $"m356-t03-{suffix}";
        var bootstrapCorrelationId = $"{prefix}-bootstrap";
        var accessOnlyCorrelationId = $"{prefix}-access-only";
        var faultCorrelationId = $"{prefix}-audit-fault";
        var accessOnlyPassword = $"M356-T03-Access-{suffix}-Password!";
        var leafPassword = $"M356-T03-Leaf-{suffix}-Password!";
        var managerPassword = $"M356-T03-Manager-{suffix}-Password!";
        var firstInitialPassword = $"M356-T03-First-Initial-{suffix}-Password!";
        var secondInitialPassword = $"M356-T03-Second-Initial-{suffix}-Password!";
        var firstResetPassword = $"M356-T03-First-Reset-{suffix}-Password!";
        var secondResetPassword = $"M356-T03-Second-Reset-{suffix}-Password!";
        var sensitiveValues = new List<string>
        {
            _bootstrapPassword,
            _mySqlConnectionString,
            RequireEnvironmentVariable("NovaGame__AdminAccess__Jwt__SigningKey"),
            accessOnlyPassword,
            leafPassword,
            managerPassword,
            firstInitialPassword,
            secondInitialPassword,
            firstResetPassword,
            secondResetPassword
        };
        var requestBodies = new List<string>
        {
            CreateLoginRequestBody(_bootstrapUserName, _bootstrapPassword)
        };

        using var bootstrapClient = CreateClient(bootstrapCorrelationId);
        var bootstrapSession = await LoginAsync(
            bootstrapClient,
            _bootstrapUserName,
            _bootstrapPassword);
        RecordAuthenticationSecrets(sensitiveValues, bootstrapSession);

        var permissionCatalog = await GetPermissionCatalogAsync(
            bootstrapClient,
            bootstrapSession.AccessToken);
        var registeredPermissions = permissionCatalog
            .SelectMany(group => group.Permissions)
            .Select(permission => permission.Name)
            .ToHashSet(StringComparer.Ordinal);
        var accessPermission = RequireRegisteredPermission(
            registeredPermissions,
            AdminAccessPermissions.Access);
        var administratorsManagePermission = RequireRegisteredPermission(
            registeredPermissions,
            AdminAccessPermissions.AdministratorsManage);
        var rolesManagePermission = RequireRegisteredPermission(
            registeredPermissions,
            AdminAccessPermissions.RolesManage);
        var leafPermissions = new[]
        {
            RequireRegisteredPermission(registeredPermissions, AdminAccessPermissions.AdministratorsRead),
            RequireRegisteredPermission(registeredPermissions, AdminAccessPermissions.AdministratorsManage),
            RequireRegisteredPermission(registeredPermissions, AdminAccessPermissions.AdministratorsResetPassword),
            RequireRegisteredPermission(registeredPermissions, AdminAccessPermissions.RolesRead),
            RequireRegisteredPermission(registeredPermissions, AdminAccessPermissions.RolesManage),
            RequireRegisteredPermission(registeredPermissions, AdminAccessPermissions.SessionsRead),
            RequireRegisteredPermission(registeredPermissions, AdminAccessPermissions.SessionsRevoke),
            RequireRegisteredPermission(registeredPermissions, AdminAccessPermissions.AuditLogsRead)
        };
        var unknownId = Guid.NewGuid();

        using (var anonymousClient = CreateClient($"{prefix}-anonymous"))
        {
            using var anonymousRequest = new HttpRequestMessage(
                HttpMethod.Get,
                "/api/admin-access/administrators?page=0&pageSize=999");
            var anonymousResponse = await SendAsync(anonymousClient, anonymousRequest);
            AssertFailure(
                anonymousResponse,
                HttpStatusCode.Unauthorized,
                AdminAccessErrorCodes.Unauthorized);
        }

        var accessOnlyRole = await CreateRoleAsync(
            bootstrapClient,
            bootstrapSession.AccessToken,
            $"{prefix}-access-only-role",
            [accessPermission]);
        var accessOnlyUserName = $"{prefix}-access-only";
        requestBodies.Add(CreateAdministratorRequestBody(
            accessOnlyUserName,
            "M356 T03 Access Only",
            accessOnlyPassword,
            [accessOnlyRole.Id]));
        _ = await CreateAdministratorAsync(
            bootstrapClient,
            bootstrapSession.AccessToken,
            accessOnlyUserName,
            "M356 T03 Access Only",
            accessOnlyPassword,
            [accessOnlyRole.Id]);

        using var accessOnlyClient = CreateClient(accessOnlyCorrelationId);
        requestBodies.Add(CreateLoginRequestBody(accessOnlyUserName, accessOnlyPassword));
        var accessOnlySession = await LoginAsync(
            accessOnlyClient,
            accessOnlyUserName,
            accessOnlyPassword);
        RecordAuthenticationSecrets(sensitiveValues, accessOnlySession);

        foreach (var request in CreateAuthorizationProbeRequests(
                     accessOnlySession.AccessToken,
                     unknownId,
                     prefix))
        {
            var response = await SendAsync(accessOnlyClient, request());
            AssertFailure(response, HttpStatusCode.Forbidden, AdminAccessErrorCodes.Forbidden);
        }

        var deniedRoleName = $"{prefix}-denied-role";
        var deniedRoleKey = Guid.NewGuid();
        var deniedRoleBody = CreateRoleRequestBody(deniedRoleName, []);
        requestBodies.Add(deniedRoleBody);
        var deniedRoleResponse = await SendCreateRoleAsync(
            accessOnlyClient,
            accessOnlySession.AccessToken,
            deniedRoleBody,
            deniedRoleKey);
        AssertFailure(
            deniedRoleResponse,
            HttpStatusCode.Forbidden,
            AdminAccessErrorCodes.Forbidden);
        await AssertAbsentRoleCommandStateAsync(deniedRoleName, deniedRoleKey, correlationId: null);

        var deniedResetKey = Guid.NewGuid();
        var deniedResetBody = CreatePasswordResetRequestBody(firstResetPassword);
        requestBodies.Add(deniedResetBody);
        var deniedResetResponse = await SendAdministratorPasswordResetAsync(
            accessOnlyClient,
            accessOnlySession.AccessToken,
            unknownId,
            "\"1\"",
            deniedResetBody,
            deniedResetKey);
        AssertFailure(
            deniedResetResponse,
            HttpStatusCode.Forbidden,
            AdminAccessErrorCodes.Forbidden);
        AssertNoSensitiveText(deniedResetResponse.Body, [firstResetPassword, deniedResetBody]);
        await AssertAbsentAdministratorCommandStateAsync(
            unknownId,
            deniedResetKey,
            AdminAccessCommandKind.AdministratorPasswordReset);

        for (var leafIndex = 0; leafIndex < leafPermissions.Length; leafIndex++)
        {
            var leafPermission = leafPermissions[leafIndex];
            var leafRole = await CreateRoleAsync(
                bootstrapClient,
                bootstrapSession.AccessToken,
                $"{prefix}-leaf-role-{leafIndex + 1}",
                [leafPermission]);
            var leafUserName = $"{prefix}-leaf-{leafIndex + 1}";
            requestBodies.Add(CreateAdministratorRequestBody(
                leafUserName,
                $"M356 T03 Leaf {leafIndex + 1}",
                leafPassword,
                [leafRole.Id]));
            _ = await CreateAdministratorAsync(
                bootstrapClient,
                bootstrapSession.AccessToken,
                leafUserName,
                $"M356 T03 Leaf {leafIndex + 1}",
                leafPassword,
                [leafRole.Id]);

            using var leafClient = CreateClient($"{prefix}-leaf-{leafIndex + 1}");
            requestBodies.Add(CreateLoginRequestBody(leafUserName, leafPassword));
            var leafSession = await LoginAsync(leafClient, leafUserName, leafPassword);
            RecordAuthenticationSecrets(sensitiveValues, leafSession);

            var leafResponse = await SendAsync(
                leafClient,
                CreateLeafAuthorizationProbeRequest(
                    leafPermission,
                    leafSession.AccessToken,
                    unknownId,
                    prefix));
            AssertFailure(leafResponse, HttpStatusCode.Forbidden, AdminAccessErrorCodes.Forbidden);
        }

        var bootstrapAdministratorId = await GetCurrentAdministratorIdAsync(
            bootstrapClient,
            bootstrapSession.AccessToken);
        var bootstrapAdministrator = await GetAdministratorAsync(
            bootstrapClient,
            bootstrapSession.AccessToken,
            bootstrapAdministratorId);
        var selfAuditCount = await CountAdministratorAuditRecordsAsync(bootstrapAdministratorId);
        var selfResetKey = Guid.NewGuid();
        var selfResetBody = CreatePasswordResetRequestBody(firstResetPassword);
        requestBodies.Add(selfResetBody);

        var selfDeactivationResponse = await SendAdministratorActiveStateAsync(
            bootstrapClient,
            bootstrapSession.AccessToken,
            bootstrapAdministrator.Id,
            bootstrapAdministrator.ETag,
            isActive: false);
        AssertFailure(
            selfDeactivationResponse,
            HttpStatusCode.UnprocessableEntity,
            AdminAccessManagementErrorCodes.SelfDeactivationForbidden);
        var selfRoleReplacementResponse = await SendAdministratorRoleReplacementAsync(
            bootstrapClient,
            bootstrapSession.AccessToken,
            bootstrapAdministrator.Id,
            bootstrapAdministrator.ETag,
            bootstrapAdministrator.Roles.Select(role => role.Id).ToArray());
        AssertFailure(
            selfRoleReplacementResponse,
            HttpStatusCode.UnprocessableEntity,
            AdminAccessManagementErrorCodes.SelfRoleMutationForbidden);
        var selfResetResponse = await SendAdministratorPasswordResetAsync(
            bootstrapClient,
            bootstrapSession.AccessToken,
            bootstrapAdministrator.Id,
            bootstrapAdministrator.ETag,
            selfResetBody,
            selfResetKey);
        AssertFailure(
            selfResetResponse,
            HttpStatusCode.UnprocessableEntity,
            AdminAccessManagementErrorCodes.SelfPasswordResetForbidden);
        AssertNoSensitiveText(selfResetResponse.Body, [firstResetPassword, selfResetBody]);
        AssertAdministratorProjectionEqual(
            bootstrapAdministrator,
            await GetAdministratorAsync(
                bootstrapClient,
                bootstrapSession.AccessToken,
                bootstrapAdministrator.Id));
        Assert.Equal(selfAuditCount, await CountAdministratorAuditRecordsAsync(bootstrapAdministrator.Id));
        await AssertAbsentCommandReceiptAsync(
            selfResetKey,
            AdminAccessCommandKind.AdministratorPasswordReset);

        var managerRole = await CreateRoleAsync(
            bootstrapClient,
            bootstrapSession.AccessToken,
            $"{prefix}-manager-role",
            [accessPermission, administratorsManagePermission, rolesManagePermission]);
        var managerUserName = $"{prefix}-manager";
        requestBodies.Add(CreateAdministratorRequestBody(
            managerUserName,
            "M356 T03 Manager",
            managerPassword,
            [managerRole.Id]));
        _ = await CreateAdministratorAsync(
            bootstrapClient,
            bootstrapSession.AccessToken,
            managerUserName,
            "M356 T03 Manager",
            managerPassword,
            [managerRole.Id]);
        using var managerClient = CreateClient($"{prefix}-manager");
        requestBodies.Add(CreateLoginRequestBody(managerUserName, managerPassword));
        var managerSession = await LoginAsync(managerClient, managerUserName, managerPassword);
        RecordAuthenticationSecrets(sensitiveValues, managerSession);
        var lastBuiltInAuditCount = await CountAdministratorAuditRecordsAsync(bootstrapAdministrator.Id);
        var lastBuiltInResponse = await SendAdministratorActiveStateAsync(
            managerClient,
            managerSession.AccessToken,
            bootstrapAdministrator.Id,
            bootstrapAdministrator.ETag,
            isActive: false);
        AssertFailure(
            lastBuiltInResponse,
            HttpStatusCode.UnprocessableEntity,
            AdminAccessManagementErrorCodes.LastActiveBuiltInAdministratorRequired);
        var lastBuiltInRoleReplacementResponse = await SendAdministratorRoleReplacementAsync(
            managerClient,
            managerSession.AccessToken,
            bootstrapAdministrator.Id,
            bootstrapAdministrator.ETag,
            []);
        AssertFailure(
            lastBuiltInRoleReplacementResponse,
            HttpStatusCode.UnprocessableEntity,
            AdminAccessManagementErrorCodes.LastActiveBuiltInAdministratorRequired);
        AssertAdministratorProjectionEqual(
            bootstrapAdministrator,
            await GetAdministratorAsync(
                bootstrapClient,
                bootstrapSession.AccessToken,
                bootstrapAdministrator.Id));
        Assert.Equal(lastBuiltInAuditCount, await CountAdministratorAuditRecordsAsync(bootstrapAdministrator.Id));

        var authorizationReceiptRoleName = $"{prefix}-receipt-role";
        var authorizationReceiptKey = Guid.NewGuid();
        var authorizationReceiptBody = CreateRoleRequestBody(
            authorizationReceiptRoleName,
            [accessPermission]);
        requestBodies.Add(authorizationReceiptBody);
        var authorizationReceiptResponse = await SendCreateRoleAsync(
            managerClient,
            managerSession.AccessToken,
            authorizationReceiptBody,
            authorizationReceiptKey);
        var authorizationReceiptRole = ParseRole(ReadEntitySuccess(authorizationReceiptResponse));
        managerRole = await ReplaceRolePermissionsAsync(
            bootstrapClient,
            bootstrapSession.AccessToken,
            managerRole,
            [accessPermission, administratorsManagePermission]);
        var deniedReceiptReplayResponse = await SendCreateRoleAsync(
            managerClient,
            managerSession.AccessToken,
            authorizationReceiptBody,
            authorizationReceiptKey);
        AssertFailure(
            deniedReceiptReplayResponse,
            HttpStatusCode.Forbidden,
            AdminAccessErrorCodes.Forbidden);
        await AssertRoleCommandStateAsync(
            authorizationReceiptRoleName,
            authorizationReceiptKey,
            authorizationReceiptRole.Id);

        var builtInRole = await GetBuiltInRoleAsync(bootstrapClient, bootstrapSession.AccessToken);
        var builtInAuditCount = await CountRoleAuditRecordsAsync(builtInRole.Id);
        var builtInRenameResponse = await SendRoleNameChangeAsync(
            bootstrapClient,
            bootstrapSession.AccessToken,
            builtInRole.Id,
            builtInRole.ETag,
            builtInRole.Name);
        AssertFailure(
            builtInRenameResponse,
            HttpStatusCode.UnprocessableEntity,
            AdminAccessManagementErrorCodes.BuiltInRoleImmutable);
        var builtInStateResponse = await SendRoleActiveStateAsync(
            bootstrapClient,
            bootstrapSession.AccessToken,
            builtInRole.Id,
            builtInRole.ETag,
            isActive: true);
        AssertFailure(
            builtInStateResponse,
            HttpStatusCode.UnprocessableEntity,
            AdminAccessManagementErrorCodes.BuiltInRoleImmutable);
        var builtInPermissionResponse = await SendRolePermissionReplacementAsync(
            bootstrapClient,
            bootstrapSession.AccessToken,
            builtInRole.Id,
            builtInRole.ETag,
            builtInRole.PermissionNames);
        AssertFailure(
            builtInPermissionResponse,
            HttpStatusCode.UnprocessableEntity,
            AdminAccessManagementErrorCodes.BuiltInRoleImmutable);
        AssertRoleProjectionEqual(
            builtInRole,
            await GetRoleAsync(bootstrapClient, bootstrapSession.AccessToken, builtInRole.Id));
        Assert.Equal(builtInAuditCount, await CountRoleAuditRecordsAsync(builtInRole.Id));

        var staleRole = await CreateRoleAsync(
            bootstrapClient,
            bootstrapSession.AccessToken,
            $"{prefix}-stale-role",
            [accessPermission]);
        var staleEtag = staleRole.ETag;
        var currentRole = await RenameRoleAsync(
            bootstrapClient,
            bootstrapSession.AccessToken,
            staleRole,
            $"{prefix}-stale-role-current");
        var roleAuditCount = await CountRoleAuditRecordsAsync(currentRole.Id);
        var staleChangeResponse = await SendRoleNameChangeAsync(
            bootstrapClient,
            bootstrapSession.AccessToken,
            currentRole.Id,
            staleEtag,
            $"{prefix}-stale-role-conflict");
        AssertFailure(
            staleChangeResponse,
            HttpStatusCode.Conflict,
            AdminAccessManagementErrorCodes.RevisionConflict);
        var staleNoOpResponse = await SendRoleNameChangeAsync(
            bootstrapClient,
            bootstrapSession.AccessToken,
            currentRole.Id,
            staleEtag,
            $"  {currentRole.Name}  ");
        var staleNoOp = ParseRole(ReadEntitySuccess(staleNoOpResponse));
        AssertRoleProjectionEqual(currentRole, staleNoOp);
        Assert.Equal(roleAuditCount, await CountRoleAuditRecordsAsync(currentRole.Id));

        var replayRoleName = $"{prefix}-replay-role";
        var replayKey = Guid.NewGuid();
        var replayBody = CreateRoleRequestBody(replayRoleName, [accessPermission]);
        requestBodies.Add(replayBody);
        var replayFirstResponse = await SendCreateRoleAsync(
            bootstrapClient,
            bootstrapSession.AccessToken,
            replayBody,
            replayKey);
        var replayFirst = ParseRole(ReadEntitySuccess(replayFirstResponse));
        var replaySecondResponse = await SendCreateRoleAsync(
            bootstrapClient,
            bootstrapSession.AccessToken,
            replayBody,
            replayKey);
        var replaySecond = ParseRole(ReadEntitySuccess(replaySecondResponse));
        AssertRoleProjectionEqual(replayFirst, replaySecond);
        var fingerprintConflictBody = CreateRoleRequestBody(
            $"{prefix}-replay-role-conflict",
            [accessPermission]);
        requestBodies.Add(fingerprintConflictBody);
        var fingerprintConflictResponse = await SendCreateRoleAsync(
            bootstrapClient,
            bootstrapSession.AccessToken,
            fingerprintConflictBody,
            replayKey);
        AssertFailure(
            fingerprintConflictResponse,
            HttpStatusCode.Conflict,
            AdminAccessManagementErrorCodes.IdempotencyConflict);
        await AssertRoleCommandStateAsync(replayRoleName, replayKey, replayFirst.Id);
        await AssertAbsentRoleCommandStateAsync(
            $"{prefix}-replay-role-conflict",
            idempotencyKey: null,
            correlationId: null);

        var firstSecretRole = await CreateRoleAsync(
            bootstrapClient,
            bootstrapSession.AccessToken,
            $"{prefix}-first-secret-role",
            [accessPermission]);
        var firstSecretUserName = $"{prefix}-first-secret";
        var firstSecretDisplayName = "M356 T03 First Secret";
        var firstSecretCreateKey = Guid.NewGuid();
        var firstSecretCreateBody = CreateAdministratorRequestBody(
            firstSecretUserName,
            firstSecretDisplayName,
            firstInitialPassword,
            [firstSecretRole.Id]);
        var secondSecretCreateBody = CreateAdministratorRequestBody(
            firstSecretUserName,
            firstSecretDisplayName,
            secondInitialPassword,
            [firstSecretRole.Id]);
        requestBodies.Add(firstSecretCreateBody);
        requestBodies.Add(secondSecretCreateBody);
        var firstSecretCreateResponse = await SendCreateAdministratorAsync(
            bootstrapClient,
            bootstrapSession.AccessToken,
            firstSecretCreateBody,
            firstSecretCreateKey);
        var firstSecretAdministrator = ParseAdministrator(ReadEntitySuccess(firstSecretCreateResponse));
        var secondSecretCreateResponse = await SendCreateAdministratorAsync(
            bootstrapClient,
            bootstrapSession.AccessToken,
            secondSecretCreateBody,
            firstSecretCreateKey);
        AssertAdministratorProjectionEqual(
            firstSecretAdministrator,
            ParseAdministrator(ReadEntitySuccess(secondSecretCreateResponse)));
        await AssertAdministratorCommandStateAsync(
            firstSecretAdministrator.Id,
            firstSecretUserName,
            firstSecretCreateKey,
            AdminAccessCommandKind.AdministratorCreate,
            AdminAccessAuditAction.AdministratorCreated,
            expectedRevision: 1);

        using var firstSecretClient = CreateClient($"{prefix}-first-secret");
        requestBodies.Add(CreateLoginRequestBody(firstSecretUserName, firstInitialPassword));
        var firstSecretSession = await LoginAsync(
            firstSecretClient,
            firstSecretUserName,
            firstInitialPassword);
        RecordAuthenticationSecrets(sensitiveValues, firstSecretSession);
        requestBodies.Add(CreateLoginRequestBody(firstSecretUserName, secondInitialPassword));
        var rejectedSecondInitialPassword = await SendLoginAsync(
            firstSecretClient,
            firstSecretUserName,
            secondInitialPassword);
        AssertFailure(
            rejectedSecondInitialPassword,
            HttpStatusCode.Unauthorized,
            AdminAccessErrorCodes.InvalidCredentials);

        var firstResetKey = Guid.NewGuid();
        var firstResetBody = CreatePasswordResetRequestBody(firstResetPassword);
        var secondResetBody = CreatePasswordResetRequestBody(secondResetPassword);
        requestBodies.Add(firstResetBody);
        requestBodies.Add(secondResetBody);
        var firstResetResponse = await SendAdministratorPasswordResetAsync(
            bootstrapClient,
            bootstrapSession.AccessToken,
            firstSecretAdministrator.Id,
            firstSecretAdministrator.ETag,
            firstResetBody,
            firstResetKey);
        var firstResetAdministrator = ParseAdministrator(ReadEntitySuccess(firstResetResponse));
        var secondResetResponse = await SendAdministratorPasswordResetAsync(
            bootstrapClient,
            bootstrapSession.AccessToken,
            firstSecretAdministrator.Id,
            firstSecretAdministrator.ETag,
            secondResetBody,
            firstResetKey);
        AssertAdministratorProjectionEqual(
            firstResetAdministrator,
            ParseAdministrator(ReadEntitySuccess(secondResetResponse)));
        await AssertAdministratorCommandStateAsync(
            firstSecretAdministrator.Id,
            firstSecretUserName,
            firstResetKey,
            AdminAccessCommandKind.AdministratorPasswordReset,
            AdminAccessAuditAction.AdministratorPasswordReset,
            expectedRevision: 2);
        requestBodies.Add(CreateLoginRequestBody(firstSecretUserName, firstResetPassword));
        var firstResetSession = await LoginAsync(
            firstSecretClient,
            firstSecretUserName,
            firstResetPassword);
        RecordAuthenticationSecrets(sensitiveValues, firstResetSession);
        requestBodies.Add(CreateLoginRequestBody(firstSecretUserName, secondResetPassword));
        var rejectedSecondResetPassword = await SendLoginAsync(
            firstSecretClient,
            firstSecretUserName,
            secondResetPassword);
        AssertFailure(
            rejectedSecondResetPassword,
            HttpStatusCode.Unauthorized,
            AdminAccessErrorCodes.InvalidCredentials);

        var concurrentRoleName = $"{prefix}-concurrent-role";
        var concurrentKey = Guid.NewGuid();
        var concurrentBody = CreateRoleRequestBody(concurrentRoleName, [accessPermission]);
        requestBodies.Add(concurrentBody);
        using var firstConcurrentClient = CreateClient($"{prefix}-concurrent-a");
        using var secondConcurrentClient = CreateClient($"{prefix}-concurrent-b");
        var concurrentResponses = await Task.WhenAll(
            SendCreateRoleAsync(
                firstConcurrentClient,
                bootstrapSession.AccessToken,
                concurrentBody,
                concurrentKey),
            SendCreateRoleAsync(
                secondConcurrentClient,
                bootstrapSession.AccessToken,
                concurrentBody,
                concurrentKey));
        var firstConcurrentRole = ParseRole(ReadEntitySuccess(concurrentResponses[0]));
        var secondConcurrentRole = ParseRole(ReadEntitySuccess(concurrentResponses[1]));
        AssertRoleProjectionEqual(firstConcurrentRole, secondConcurrentRole);
        await AssertRoleCommandStateAsync(concurrentRoleName, concurrentKey, firstConcurrentRole.Id);

        var faultRoleName = $"{prefix}-audit-fault-role";
        var faultKey = Guid.NewGuid();
        var faultBody = CreateRoleRequestBody(faultRoleName, [accessPermission]);
        requestBodies.Add(faultBody);
        var constraintAdded = false;
        try
        {
            await AddAuditFailureConstraintAsync(faultCorrelationId);
            constraintAdded = true;
            using var faultClient = CreateClient(faultCorrelationId);
            var faultResponse = await SendCreateRoleAsync(
                faultClient,
                bootstrapSession.AccessToken,
                faultBody,
                faultKey);
            AssertFailure(
                faultResponse,
                HttpStatusCode.InternalServerError,
                NovaCoreErrorCodes.InternalServerError);
            AssertSanitizedUnexpectedFailure(faultResponse);
            AssertNoSensitiveText(
                faultResponse.Body,
                [faultBody, _mySqlConnectionString, _bootstrapPassword]);
        }
        finally
        {
            if (constraintAdded)
            {
                await DropAuditFailureConstraintAsync();
            }
        }

        await AssertAbsentRoleCommandStateAsync(faultRoleName, faultKey, faultCorrelationId);
        using (var faultRetryClient = CreateClient(faultCorrelationId))
        {
            var faultRetryResponse = await SendCreateRoleAsync(
                faultRetryClient,
                bootstrapSession.AccessToken,
                faultBody,
                faultKey);
            var faultRetryRole = ParseRole(ReadEntitySuccess(faultRetryResponse));
            await AssertRoleCommandStateAsync(faultRoleName, faultKey, faultRetryRole.Id);
        }

        sensitiveValues.AddRange(requestBodies);
        sensitiveValues.AddRange(_recordedRequestBodies);
        sensitiveValues.AddRange(_recordedTransportSecrets);
        sensitiveValues.Add(new MySqlConnectionStringBuilder(_mySqlConnectionString).Password);
        sensitiveValues.AddRange(await ReadStoredSensitiveRepresentationsAsync());
        AssertManagementResponsesAreRedacted(sensitiveValues);
        await AssertComposedLogsAreRedactedAsync(sensitiveValues);
    }

    private static void RecordAuthenticationSecrets(
        ICollection<string> sensitiveValues,
        AuthenticationSession session)
    {
        sensitiveValues.Add(session.AccessToken);
        sensitiveValues.Add(session.RefreshToken);
        sensitiveValues.Add(session.RefreshCookie);
    }

    private static string CreateLoginRequestBody(string userName, string password)
    {
        return JsonSerializer.Serialize(new { userName, password });
    }

    private static string CreateRoleRequestBody(
        string name,
        IReadOnlyCollection<string> permissionNames)
    {
        return JsonSerializer.Serialize(new
        {
            name,
            permissionNames = permissionNames.ToArray()
        });
    }

    private static string CreateAdministratorRequestBody(
        string userName,
        string displayName,
        string initialPassword,
        IReadOnlyCollection<Guid> roleIds)
    {
        return JsonSerializer.Serialize(new
        {
            userName,
            displayName,
            initialPassword,
            roleIds = roleIds.Select(roleId => roleId.ToString("D")).ToArray()
        });
    }

    private static string CreatePasswordResetRequestBody(string newPassword)
    {
        return JsonSerializer.Serialize(new { newPassword });
    }

    private static Task<HttpResult> SendLoginAsync(
        HttpClient client,
        string userName,
        string password)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = new StringContent(
                CreateLoginRequestBody(userName, password),
                Encoding.UTF8,
                "application/json")
        };
        return SendAsync(client, request);
    }

    private static Task<HttpResult> SendCreateRoleAsync(
        HttpClient client,
        string accessToken,
        string requestBody,
        Guid idempotencyKey)
    {
        var request = CreateAuthorizedRequest(
            HttpMethod.Post,
            "/api/admin-access/roles",
            accessToken);
        request.Content = new StringContent(requestBody, Encoding.UTF8, "application/json");
        request.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey.ToString("D"));
        return SendAsync(client, request);
    }

    private static Task<HttpResult> SendCreateAdministratorAsync(
        HttpClient client,
        string accessToken,
        string requestBody,
        Guid idempotencyKey)
    {
        var request = CreateAuthorizedRequest(
            HttpMethod.Post,
            "/api/admin-access/administrators",
            accessToken);
        request.Content = new StringContent(requestBody, Encoding.UTF8, "application/json");
        request.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey.ToString("D"));
        return SendAsync(client, request);
    }

    private static Task<HttpResult> SendAdministratorPasswordResetAsync(
        HttpClient client,
        string accessToken,
        Guid administratorId,
        string eTag,
        string requestBody,
        Guid idempotencyKey)
    {
        var request = CreateAuthorizedRequest(
            HttpMethod.Post,
            $"/api/admin-access/administrators/{administratorId:D}/password-reset",
            accessToken);
        request.Content = new StringContent(requestBody, Encoding.UTF8, "application/json");
        request.Headers.TryAddWithoutValidation("If-Match", eTag);
        request.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey.ToString("D"));
        return SendAsync(client, request);
    }

    private static Task<HttpResult> SendAdministratorActiveStateAsync(
        HttpClient client,
        string accessToken,
        Guid administratorId,
        string eTag,
        bool isActive)
    {
        var request = CreateAuthorizedRequest(
            HttpMethod.Put,
            $"/api/admin-access/administrators/{administratorId:D}/active-state",
            accessToken,
            new { isActive });
        request.Headers.TryAddWithoutValidation("If-Match", eTag);
        return SendAsync(client, request);
    }

    private static Task<HttpResult> SendAdministratorRoleReplacementAsync(
        HttpClient client,
        string accessToken,
        Guid administratorId,
        string eTag,
        IReadOnlyCollection<Guid> roleIds)
    {
        var request = CreateAuthorizedRequest(
            HttpMethod.Put,
            $"/api/admin-access/administrators/{administratorId:D}/roles",
            accessToken,
            new { roleIds = roleIds.Select(roleId => roleId.ToString("D")).ToArray() });
        request.Headers.TryAddWithoutValidation("If-Match", eTag);
        return SendAsync(client, request);
    }

    private static Task<HttpResult> SendRoleNameChangeAsync(
        HttpClient client,
        string accessToken,
        Guid roleId,
        string eTag,
        string name)
    {
        var request = CreateAuthorizedRequest(
            HttpMethod.Put,
            $"/api/admin-access/roles/{roleId:D}/name",
            accessToken,
            new { name });
        request.Headers.TryAddWithoutValidation("If-Match", eTag);
        return SendAsync(client, request);
    }

    private static Task<HttpResult> SendRoleActiveStateAsync(
        HttpClient client,
        string accessToken,
        Guid roleId,
        string eTag,
        bool isActive)
    {
        var request = CreateAuthorizedRequest(
            HttpMethod.Put,
            $"/api/admin-access/roles/{roleId:D}/active-state",
            accessToken,
            new { isActive });
        request.Headers.TryAddWithoutValidation("If-Match", eTag);
        return SendAsync(client, request);
    }

    private static Task<HttpResult> SendRolePermissionReplacementAsync(
        HttpClient client,
        string accessToken,
        Guid roleId,
        string eTag,
        IReadOnlyCollection<string> permissionNames)
    {
        var request = CreateAuthorizedRequest(
            HttpMethod.Put,
            $"/api/admin-access/roles/{roleId:D}/permissions",
            accessToken,
            new { permissionNames = permissionNames.ToArray() });
        request.Headers.TryAddWithoutValidation("If-Match", eTag);
        return SendAsync(client, request);
    }

    private static async Task<Guid> GetCurrentAdministratorIdAsync(
        HttpClient client,
        string accessToken)
    {
        using var request = CreateAuthorizedRequest(HttpMethod.Get, "/api/user/info", accessToken);
        var response = await SendAsync(client, request);
        return ReadSuccessData(response, managementResponse: false).GetProperty("id").GetGuid();
    }

    private static async Task<RoleProjection> GetBuiltInRoleAsync(
        HttpClient client,
        string accessToken)
    {
        var page = await ListRolesAsync(
            client,
            accessToken,
            "isBuiltIn=true&page=1&pageSize=20");
        var role = Assert.Single(page.Items);
        return await GetRoleAsync(client, accessToken, role.Id);
    }

    private static IReadOnlyList<Func<HttpRequestMessage>> CreateAuthorizationProbeRequests(
        string accessToken,
        Guid unknownId,
        string prefix)
    {
        return
        [
            () => CreateAuthorizedRequest(
                HttpMethod.Get,
                "/api/admin-access/administrators?page=0&pageSize=999",
                accessToken),
            () => CreateRequestWithHeader(
                CreateAuthorizedRequest(
                    HttpMethod.Post,
                    "/api/admin-access/administrators",
                    accessToken,
                    new
                    {
                        userName = string.Empty,
                        displayName = string.Empty,
                        initialPassword = string.Empty,
                        roleIds = Array.Empty<string>()
                    }),
                "Idempotency-Key",
                "not-a-guid"),
            () => CreateAuthorizedRequest(
                HttpMethod.Get,
                $"/api/admin-access/administrators/{unknownId:D}",
                accessToken),
            () => CreateAuthorizedRequest(
                HttpMethod.Put,
                $"/api/admin-access/administrators/{unknownId:D}/display-name",
                accessToken,
                new { displayName = string.Empty }),
            () => CreateAuthorizedRequest(
                HttpMethod.Put,
                $"/api/admin-access/administrators/{unknownId:D}/roles",
                accessToken,
                new { roleIds = Array.Empty<string>() }),
            () => CreateAuthorizedRequest(
                HttpMethod.Put,
                $"/api/admin-access/administrators/{unknownId:D}/active-state",
                accessToken,
                new { isActive = false }),
            () => CreateRequestWithHeader(
                CreateAuthorizedRequest(
                    HttpMethod.Post,
                    $"/api/admin-access/administrators/{unknownId:D}/password-reset",
                    accessToken,
                    new { newPassword = string.Empty }),
                "Idempotency-Key",
                "not-a-guid"),
            () => CreateAuthorizedRequest(
                HttpMethod.Get,
                "/api/admin-access/roles?page=0&pageSize=999",
                accessToken),
            () => CreateRequestWithHeader(
                CreateAuthorizedRequest(
                    HttpMethod.Post,
                    "/api/admin-access/roles",
                    accessToken,
                    new { name = $"{prefix}-forbidden-role", permissionNames = Array.Empty<string>() }),
                "Idempotency-Key",
                "not-a-guid"),
            () => CreateAuthorizedRequest(
                HttpMethod.Get,
                $"/api/admin-access/roles/{unknownId:D}",
                accessToken),
            () => CreateAuthorizedRequest(
                HttpMethod.Put,
                $"/api/admin-access/roles/{unknownId:D}/name",
                accessToken,
                new { name = string.Empty }),
            () => CreateAuthorizedRequest(
                HttpMethod.Put,
                $"/api/admin-access/roles/{unknownId:D}/active-state",
                accessToken,
                new { isActive = false }),
            () => CreateAuthorizedRequest(
                HttpMethod.Put,
                $"/api/admin-access/roles/{unknownId:D}/permissions",
                accessToken,
                new { permissionNames = Array.Empty<string>() }),
            () => CreateAuthorizedRequest(HttpMethod.Get, "/api/admin-access/permissions", accessToken),
            () => CreateAuthorizedRequest(
                HttpMethod.Get,
                "/api/admin-access/sessions?page=0&pageSize=999&state=999",
                accessToken),
            () => CreateAuthorizedRequest(
                HttpMethod.Post,
                $"/api/admin-access/sessions/{unknownId:D}/revoke",
                accessToken),
            () => CreateAuthorizedRequest(
                HttpMethod.Get,
                "/api/admin-access/audit-records?fromUtc=invalid&toUtc=invalid&page=0&pageSize=999",
                accessToken)
        ];
    }

    private static HttpRequestMessage CreateLeafAuthorizationProbeRequest(
        string leafPermission,
        string accessToken,
        Guid unknownId,
        string prefix)
    {
        return leafPermission switch
        {
            AdminAccessPermissions.AdministratorsRead => CreateAuthorizedRequest(
                HttpMethod.Get,
                "/api/admin-access/administrators?page=0&pageSize=999",
                accessToken),
            AdminAccessPermissions.AdministratorsManage => CreateRequestWithHeader(
                CreateAuthorizedRequest(
                    HttpMethod.Post,
                    "/api/admin-access/administrators",
                    accessToken,
                    new
                    {
                        userName = string.Empty,
                        displayName = string.Empty,
                        initialPassword = string.Empty,
                        roleIds = Array.Empty<string>()
                    }),
                "Idempotency-Key",
                "not-a-guid"),
            AdminAccessPermissions.AdministratorsResetPassword => CreateRequestWithHeader(
                CreateAuthorizedRequest(
                    HttpMethod.Post,
                    $"/api/admin-access/administrators/{unknownId:D}/password-reset",
                    accessToken,
                    new { newPassword = string.Empty }),
                "Idempotency-Key",
                "not-a-guid"),
            AdminAccessPermissions.RolesRead => CreateAuthorizedRequest(
                HttpMethod.Get,
                "/api/admin-access/roles?page=0&pageSize=999",
                accessToken),
            AdminAccessPermissions.RolesManage => CreateRequestWithHeader(
                CreateAuthorizedRequest(
                    HttpMethod.Post,
                    "/api/admin-access/roles",
                    accessToken,
                    new { name = $"{prefix}-leaf-forbidden-role", permissionNames = Array.Empty<string>() }),
                "Idempotency-Key",
                "not-a-guid"),
            AdminAccessPermissions.SessionsRead => CreateAuthorizedRequest(
                HttpMethod.Get,
                "/api/admin-access/sessions?page=0&pageSize=999&state=999",
                accessToken),
            AdminAccessPermissions.SessionsRevoke => CreateAuthorizedRequest(
                HttpMethod.Post,
                $"/api/admin-access/sessions/{unknownId:D}/revoke",
                accessToken),
            AdminAccessPermissions.AuditLogsRead => CreateAuthorizedRequest(
                HttpMethod.Get,
                "/api/admin-access/audit-records?fromUtc=invalid&toUtc=invalid&page=0&pageSize=999",
                accessToken),
            _ => throw new ArgumentOutOfRangeException(nameof(leafPermission))
        };
    }

    private static HttpRequestMessage CreateRequestWithHeader(
        HttpRequestMessage request,
        string name,
        string value)
    {
        request.Headers.TryAddWithoutValidation(name, value);
        return request;
    }

    private async Task AssertAbsentRoleCommandStateAsync(
        string roleName,
        Guid? idempotencyKey,
        string? correlationId)
    {
        await using var connection = new MySqlConnection(_mySqlConnectionString);
        await connection.OpenAsync();

        Assert.Equal(
            0,
            await CountAsync(
                connection,
                "SELECT COUNT(*) FROM `admin_roles` WHERE `Name` = @roleName",
                ("@roleName", roleName)));
        if (idempotencyKey.HasValue)
        {
            Assert.Equal(
                0,
                await CountAsync(
                    connection,
                    "SELECT COUNT(*) FROM `admin_access_command_receipts` " +
                    "WHERE `IdempotencyKey` = @idempotencyKey AND `CommandKind` = 'AdminRoleCreate'",
                    ("@idempotencyKey", idempotencyKey.Value.ToString("D"))));
        }

        if (correlationId is not null)
        {
            Assert.Equal(
                0,
                await CountAsync(
                    connection,
                    "SELECT COUNT(*) FROM `admin_audit_records` WHERE `CorrelationId` = @correlationId",
                    ("@correlationId", correlationId)));
        }
    }

    private async Task AssertAbsentAdministratorCommandStateAsync(
        Guid administratorId,
        Guid idempotencyKey,
        AdminAccessCommandKind commandKind)
    {
        await using var connection = new MySqlConnection(_mySqlConnectionString);
        await connection.OpenAsync();

        Assert.Equal(
            0,
            await CountAsync(
                connection,
                "SELECT COUNT(*) FROM `admin_administrators` WHERE `Id` = @administratorId",
                ("@administratorId", administratorId.ToString("D"))));
        Assert.Equal(
            0,
            await CountAsync(
                connection,
                "SELECT COUNT(*) FROM `admin_access_command_receipts` " +
                "WHERE `IdempotencyKey` = @idempotencyKey AND `CommandKind` = @commandKind",
                ("@idempotencyKey", idempotencyKey.ToString("D")),
                ("@commandKind", commandKind.ToString())));
    }

    private async Task AssertAbsentCommandReceiptAsync(
        Guid idempotencyKey,
        AdminAccessCommandKind commandKind)
    {
        await using var connection = new MySqlConnection(_mySqlConnectionString);
        await connection.OpenAsync();

        Assert.Equal(
            0,
            await CountAsync(
                connection,
                "SELECT COUNT(*) FROM `admin_access_command_receipts` " +
                "WHERE `IdempotencyKey` = @idempotencyKey AND `CommandKind` = @commandKind",
                ("@idempotencyKey", idempotencyKey.ToString("D")),
                ("@commandKind", commandKind.ToString())));
    }

    private async Task AssertRoleCommandStateAsync(
        string roleName,
        Guid idempotencyKey,
        Guid roleId)
    {
        await using var connection = new MySqlConnection(_mySqlConnectionString);
        await connection.OpenAsync();

        Assert.Equal(
            1,
            await CountAsync(
                connection,
                "SELECT COUNT(*) FROM `admin_roles` WHERE `Id` = @roleId AND `Name` = @roleName",
                ("@roleId", roleId.ToString("D")),
                ("@roleName", roleName)));
        Assert.Equal(
            1,
            await CountAsync(
                connection,
                "SELECT COUNT(*) FROM `admin_access_command_receipts` " +
                "WHERE `IdempotencyKey` = @idempotencyKey AND `CommandKind` = 'AdminRoleCreate' " +
                "AND `TargetResourceId` = @roleId AND `ResultingRevision` = 1",
                ("@idempotencyKey", idempotencyKey.ToString("D")),
                ("@roleId", roleId.ToString("D"))));
        Assert.Equal(
            1,
            await CountAsync(
                connection,
                "SELECT COUNT(*) FROM `admin_audit_records` " +
                "WHERE `Action` = 'RoleCreated' AND `RoleId` = @roleId AND `TargetRevision` = 1",
                ("@roleId", roleId.ToString("D"))));
    }

    private async Task AssertAdministratorCommandStateAsync(
        Guid administratorId,
        string userName,
        Guid idempotencyKey,
        AdminAccessCommandKind commandKind,
        AdminAccessAuditAction auditAction,
        long expectedRevision)
    {
        await using var connection = new MySqlConnection(_mySqlConnectionString);
        await connection.OpenAsync();

        Assert.Equal(
            1,
            await CountAsync(
                connection,
                "SELECT COUNT(*) FROM `admin_administrators` " +
                "WHERE `Id` = @administratorId AND `UserName` = @userName AND `Revision` = @revision",
                ("@administratorId", administratorId.ToString("D")),
                ("@userName", userName),
                ("@revision", expectedRevision)));
        Assert.Equal(
            1,
            await CountAsync(
                connection,
                "SELECT COUNT(*) FROM `admin_access_command_receipts` " +
                "WHERE `IdempotencyKey` = @idempotencyKey AND `CommandKind` = @commandKind " +
                "AND `TargetResourceId` = @administratorId AND `ResultingRevision` = @revision",
                ("@idempotencyKey", idempotencyKey.ToString("D")),
                ("@commandKind", commandKind.ToString()),
                ("@administratorId", administratorId.ToString("D")),
                ("@revision", expectedRevision)));
        Assert.Equal(
            1,
            await CountAsync(
                connection,
                "SELECT COUNT(*) FROM `admin_audit_records` " +
                "WHERE `Action` = @action AND `AdministratorId` = @administratorId " +
                "AND `TargetRevision` = @revision",
                ("@action", auditAction.ToString()),
                ("@administratorId", administratorId.ToString("D")),
                ("@revision", expectedRevision)));
    }

    private async Task<long> CountAdministratorAuditRecordsAsync(Guid administratorId)
    {
        await using var connection = new MySqlConnection(_mySqlConnectionString);
        await connection.OpenAsync();
        return await CountAsync(
            connection,
            "SELECT COUNT(*) FROM `admin_audit_records` WHERE `AdministratorId` = @administratorId",
            ("@administratorId", administratorId.ToString("D")));
    }

    private async Task<long> CountRoleAuditRecordsAsync(Guid roleId)
    {
        await using var connection = new MySqlConnection(_mySqlConnectionString);
        await connection.OpenAsync();
        return await CountAsync(
            connection,
            "SELECT COUNT(*) FROM `admin_audit_records` WHERE `RoleId` = @roleId",
            ("@roleId", roleId.ToString("D")));
    }

    private async Task AddAuditFailureConstraintAsync(string correlationId)
    {
        Assert.Matches("^[a-z0-9-]+$", correlationId);
        await using var connection = new MySqlConnection(_mySqlConnectionString);
        await connection.OpenAsync();
        await using var command = new MySqlCommand($"""
            ALTER TABLE `admin_audit_records`
            ADD CONSTRAINT `{AuditFailureConstraintName}`
            CHECK (
                `Action` <> 'RoleCreated' OR
                `CorrelationId` IS NULL OR
                `CorrelationId` <> '{correlationId}')
            """, connection);
        _ = await command.ExecuteNonQueryAsync();
    }

    private async Task DropAuditFailureConstraintAsync()
    {
        await using var connection = new MySqlConnection(_mySqlConnectionString);
        await connection.OpenAsync();
        await using var command = new MySqlCommand(
            $"ALTER TABLE `admin_audit_records` DROP CHECK `{AuditFailureConstraintName}`",
            connection);
        _ = await command.ExecuteNonQueryAsync();
    }

    private async Task<IReadOnlyList<string>> ReadStoredSensitiveRepresentationsAsync()
    {
        var values = new List<string>();
        await using var connection = new MySqlConnection(_mySqlConnectionString);
        await connection.OpenAsync();

        await using (var passwordCommand = new MySqlCommand(
                         "SELECT `PasswordHash` FROM `admin_administrators`",
                         connection))
        await using (var reader = await passwordCommand.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                if (!reader.IsDBNull(0))
                {
                    values.Add(reader.GetString(0));
                }
            }
        }

        await using (var hashCommand = new MySqlCommand(
                         "SELECT `TokenHash` FROM `admin_refresh_token_sessions` " +
                         "UNION ALL SELECT `RequestFingerprint` FROM `admin_access_command_receipts`",
                         connection))
        await using (var reader = await hashCommand.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                if (reader.IsDBNull(0))
                {
                    continue;
                }

                var value = reader.GetFieldValue<byte[]>(0);
                var hexadecimal = Convert.ToHexString(value);
                var base64 = Convert.ToBase64String(value);
                values.Add(hexadecimal);
                values.Add(hexadecimal.ToLowerInvariant());
                values.Add("0x" + hexadecimal);
                values.Add(BitConverter.ToString(value));
                values.Add(base64);
                values.Add(base64.TrimEnd('=').Replace('+', '-').Replace('/', '_'));
            }
        }

        return values;
    }

    private async Task AssertComposedLogsAreRedactedAsync(IReadOnlyCollection<string> sensitiveValues)
    {
        foreach (var logPath in _composedLogPaths)
        {
            Assert.True(File.Exists(logPath), "A composed process log is missing.");
            var content = await ReadSharedTextAsync(logPath);
            AssertNoSensitiveText(content, sensitiveValues);
            var normalizedContent = NormalizePotentialJsonEscapes(content);
            AssertNoSensitiveText(normalizedContent, sensitiveValues);
            foreach (var fieldName in new[]
                     {
                         "\"password\"",
                         "\"initialPassword\"",
                         "\"newPassword\"",
                         "\"accessToken\"",
                         "\"refreshToken\"",
                         "\"passwordHash\"",
                         "\"tokenHash\"",
                         "\"requestBody\"",
                         "\"requestFingerprint\"",
                         "\"connectionString\""
                     })
            {
                Assert.False(
                    content.Contains(fieldName, StringComparison.OrdinalIgnoreCase),
                    "A composed process log exposed a prohibited payload field.");
                Assert.False(
                    normalizedContent.Contains(fieldName, StringComparison.OrdinalIgnoreCase),
                    "A composed process log exposed an escaped prohibited payload field.");
            }

            Assert.False(
                Regex.IsMatch(
                    content,
                    @"\beyJ[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}\b"),
                "A composed process log exposed a JWT-shaped token.");
        }
    }

    private void AssertManagementResponsesAreRedacted(IReadOnlyCollection<string> sensitiveValues)
    {
        foreach (var body in _managementResponseBodies)
        {
            AssertNoSensitiveText(body, sensitiveValues);
            AssertNoSensitiveText(NormalizePotentialJsonEscapes(body), sensitiveValues);
            AssertManagementResponseIsRedacted(ParseJson(body));
        }
    }

    private static async Task<string> ReadSharedTextAsync(string path)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite);
        using var reader = new StreamReader(
            stream,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true),
            detectEncodingFromByteOrderMarks: true,
            bufferSize: 1024,
            leaveOpen: false);
        return await reader.ReadToEndAsync();
    }

    private static void AssertNoSensitiveText(string source, IEnumerable<string> sensitiveValues)
    {
        foreach (var sensitiveValue in sensitiveValues.Where(value => !string.IsNullOrEmpty(value)))
        {
            Assert.False(
                source.Contains(sensitiveValue, StringComparison.Ordinal),
                "A response or composed process log exposed a prohibited sensitive value.");
        }
    }

    private static string NormalizePotentialJsonEscapes(string source)
    {
        for (var attempt = 0; attempt < 4; attempt++)
        {
            var normalized = Regex.Replace(
                source,
                @"\\u(?<hex>[0-9a-fA-F]{4})",
                match => ((char)Convert.ToInt32(match.Groups["hex"].Value, 16)).ToString())
                .Replace("\\\"", "\"", StringComparison.Ordinal);
            if (string.Equals(source, normalized, StringComparison.Ordinal))
            {
                return source;
            }

            source = normalized;
        }

        return source;
    }

    private HttpClient CreateClient(string correlationId)
    {
        var client = new HttpClient(new HttpClientHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false
        }, disposeHandler: true)
        {
            BaseAddress = _apiBaseUri,
            Timeout = TimeSpan.FromSeconds(60)
        };
        RedactionRecorders.Add(client, new RedactionRecorder(
            _recordedRequestBodies,
            _recordedTransportSecrets,
            _managementResponseBodies));
        client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-Correlation-Id", correlationId);
        return client;
    }

    private async Task AssertBootstrapMigrationAndSeedAsync()
    {
        await using var connection = new MySqlConnection(_mySqlConnectionString);
        await connection.OpenAsync();

        Assert.Equal(
            1,
            await CountAsync(
                connection,
                "SELECT COUNT(*) FROM `__EFMigrationsHistory` WHERE `MigrationId` = @migrationId",
                ("@migrationId", AdminAccessManagementMigrationId)));
        Assert.Equal(
            1,
            await CountAsync(
                connection,
                "SELECT COUNT(*) FROM `admin_administrators` WHERE `UserName` = @userName",
                ("@userName", _bootstrapUserName)));
        Assert.Equal(
            1,
            await CountAsync(connection, "SELECT COUNT(*) FROM `admin_roles` WHERE `IsBuiltIn` = 1"));
        Assert.Equal(
            0,
            await CountAsync(connection, "SELECT COUNT(*) FROM `admin_roles` WHERE `IsBuiltIn` = 0"));
    }

    private async Task AssertDurableLifecycleStateAsync(
        string targetUserName,
        string secondaryUserName,
        Guid targetAdministratorId,
        Guid primaryRoleId,
        Guid alternateRoleId,
        string bootstrapCorrelationId)
    {
        await using var connection = new MySqlConnection(_mySqlConnectionString);
        await connection.OpenAsync();

        Assert.Equal(
            1,
            await CountAsync(
                connection,
                "SELECT COUNT(*) FROM `admin_administrators` WHERE `UserName` = @userName",
                ("@userName", targetUserName)));
        Assert.Equal(
            1,
            await CountAsync(
                connection,
                "SELECT COUNT(*) FROM `admin_administrators` WHERE `UserName` = @userName",
                ("@userName", secondaryUserName)));
        Assert.Equal(
            1,
            await CountAsync(
                connection,
                "SELECT COUNT(*) FROM `admin_roles` WHERE `Id` = @roleId",
                ("@roleId", primaryRoleId.ToString("D"))));
        Assert.Equal(
            1,
            await CountAsync(
                connection,
                "SELECT COUNT(*) FROM `admin_roles` WHERE `Id` = @roleId",
                ("@roleId", alternateRoleId.ToString("D"))));
        Assert.Equal(
            2,
            await CountAsync(
                connection,
                "SELECT COUNT(*) FROM `admin_role_permissions` WHERE `RoleId` = @roleId",
                ("@roleId", primaryRoleId.ToString("D"))));
        Assert.Equal(
            1,
            await CountAsync(
                connection,
                "SELECT COUNT(*) FROM `admin_role_memberships` WHERE `AdministratorId` = @administratorId",
                ("@administratorId", targetAdministratorId.ToString("D"))));
        Assert.True(
            await CountAsync(
                connection,
                "SELECT COUNT(*) FROM `admin_refresh_token_sessions` WHERE `AdministratorId` = @administratorId",
                ("@administratorId", targetAdministratorId.ToString("D"))) >= 3);
        Assert.True(
            await CountAsync(
                connection,
                "SELECT COUNT(*) FROM `admin_audit_records` WHERE `CorrelationId` = @correlationId",
                ("@correlationId", bootstrapCorrelationId)) >= 12);
    }

    private static async Task<long> CountAsync(
        MySqlConnection connection,
        string commandText,
        params (string Name, object? Value)[] parameters)
    {
        await using var command = new MySqlCommand(commandText, connection);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        return Convert.ToInt64(
            await command.ExecuteScalarAsync(),
            CultureInfo.InvariantCulture);
    }

    private static async Task<AuthenticationSession> LoginAsync(
        HttpClient client,
        string userName,
        string password)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new { userName, password })
        };
        var response = await SendAsync(client, request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = ReadSuccessData(response, managementResponse: false);
        Assert.Equal("Bearer", data.GetProperty("tokenType").GetString());
        var accessToken = data.GetProperty("accessToken").GetString();
        Assert.False(string.IsNullOrWhiteSpace(accessToken));
        var expiresAtUtc = data.GetProperty("expiresAtUtc").GetDateTime();
        Assert.Equal(DateTimeKind.Utc, expiresAtUtc.Kind);
        var refreshCookie = Assert.Single(
            response.SetCookieHeaders,
            value => value.StartsWith(
                AdminAccessController.RefreshTokenCookieName + "=",
                StringComparison.Ordinal));
        var refreshToken = ExtractCookieValue(refreshCookie);
        Assert.False(response.Body.Contains(refreshToken, StringComparison.Ordinal));

        return new AuthenticationSession(accessToken!, expiresAtUtc, refreshToken, refreshCookie);
    }

    private static async Task AssertRefreshRejectedAsync(HttpClient client, string refreshToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh");
        request.Headers.TryAddWithoutValidation(
            "Cookie",
            $"{AdminAccessController.RefreshTokenCookieName}={refreshToken}");
        var response = await SendAsync(client, request);
        AssertFailure(response, HttpStatusCode.Unauthorized, AdminAccessErrorCodes.RefreshTokenInvalid);
    }

    private static async Task<IReadOnlyList<PermissionGroupProjection>> GetPermissionCatalogAsync(
        HttpClient client,
        string accessToken)
    {
        using var request = CreateAuthorizedRequest(HttpMethod.Get, "/api/admin-access/permissions", accessToken);
        var response = await SendAsync(client, request);
        var data = ReadSuccessData(response, managementResponse: true);
        return data.EnumerateArray()
            .Select(group => new PermissionGroupProjection(
                group.GetProperty("name").GetString()!,
                group.GetProperty("permissions").EnumerateArray()
                    .Select(permission => new PermissionProjection(
                        permission.GetProperty("name").GetString()!))
                    .ToArray()))
            .ToArray();
    }

    private static async Task<RoleProjection> CreateRoleAsync(
        HttpClient client,
        string accessToken,
        string name,
        IReadOnlyCollection<string> permissionNames)
    {
        using var request = CreateAuthorizedRequest(
            HttpMethod.Post,
            "/api/admin-access/roles",
            accessToken,
            new { name, permissionNames = permissionNames.ToArray() });
        request.Headers.TryAddWithoutValidation("Idempotency-Key", Guid.NewGuid().ToString("D"));
        var response = await SendAsync(client, request);
        return ParseRole(ReadEntitySuccess(response));
    }

    private static async Task<RoleProjection> GetRoleAsync(
        HttpClient client,
        string accessToken,
        Guid roleId)
    {
        using var request = CreateAuthorizedRequest(
            HttpMethod.Get,
            $"/api/admin-access/roles/{roleId:D}",
            accessToken);
        var response = await SendAsync(client, request);
        return ParseRole(ReadEntitySuccess(response));
    }

    private static async Task<RoleProjection> RenameRoleAsync(
        HttpClient client,
        string accessToken,
        RoleProjection role,
        string name)
    {
        using var request = CreateAuthorizedRequest(
            HttpMethod.Put,
            $"/api/admin-access/roles/{role.Id:D}/name",
            accessToken,
            new { name });
        request.Headers.TryAddWithoutValidation("If-Match", role.ETag);
        var response = await SendAsync(client, request);
        return ParseRole(ReadEntitySuccess(response));
    }

    private static async Task<RoleProjection> SetRoleActiveStateAsync(
        HttpClient client,
        string accessToken,
        RoleProjection role,
        bool isActive)
    {
        using var request = CreateAuthorizedRequest(
            HttpMethod.Put,
            $"/api/admin-access/roles/{role.Id:D}/active-state",
            accessToken,
            new { isActive });
        request.Headers.TryAddWithoutValidation("If-Match", role.ETag);
        var response = await SendAsync(client, request);
        return ParseRole(ReadEntitySuccess(response));
    }

    private static async Task<RoleProjection> ReplaceRolePermissionsAsync(
        HttpClient client,
        string accessToken,
        RoleProjection role,
        IReadOnlyCollection<string> permissionNames)
    {
        using var request = CreateAuthorizedRequest(
            HttpMethod.Put,
            $"/api/admin-access/roles/{role.Id:D}/permissions",
            accessToken,
            new { permissionNames = permissionNames.ToArray() });
        request.Headers.TryAddWithoutValidation("If-Match", role.ETag);
        var response = await SendAsync(client, request);
        return ParseRole(ReadEntitySuccess(response));
    }

    private static async Task<AdministratorProjection> CreateAdministratorAsync(
        HttpClient client,
        string accessToken,
        string userName,
        string displayName,
        string initialPassword,
        IReadOnlyCollection<Guid> roleIds)
    {
        using var request = CreateAuthorizedRequest(
            HttpMethod.Post,
            "/api/admin-access/administrators",
            accessToken,
            new
            {
                userName,
                displayName,
                initialPassword,
                roleIds = roleIds.Select(roleId => roleId.ToString("D")).ToArray()
            });
        request.Headers.TryAddWithoutValidation("Idempotency-Key", Guid.NewGuid().ToString("D"));
        var response = await SendAsync(client, request);
        return ParseAdministrator(ReadEntitySuccess(response));
    }

    private static async Task<AdministratorProjection> GetAdministratorAsync(
        HttpClient client,
        string accessToken,
        Guid administratorId)
    {
        using var request = CreateAuthorizedRequest(
            HttpMethod.Get,
            $"/api/admin-access/administrators/{administratorId:D}",
            accessToken);
        var response = await SendAsync(client, request);
        return ParseAdministrator(ReadEntitySuccess(response));
    }

    private static async Task<AdministratorProjection> ChangeAdministratorDisplayNameAsync(
        HttpClient client,
        string accessToken,
        AdministratorProjection administrator,
        string displayName)
    {
        using var request = CreateAuthorizedRequest(
            HttpMethod.Put,
            $"/api/admin-access/administrators/{administrator.Id:D}/display-name",
            accessToken,
            new { displayName });
        request.Headers.TryAddWithoutValidation("If-Match", administrator.ETag);
        var response = await SendAsync(client, request);
        return ParseAdministrator(ReadEntitySuccess(response));
    }

    private static async Task<AdministratorProjection> ReplaceAdministratorRolesAsync(
        HttpClient client,
        string accessToken,
        AdministratorProjection administrator,
        IReadOnlyCollection<Guid> roleIds)
    {
        using var request = CreateAuthorizedRequest(
            HttpMethod.Put,
            $"/api/admin-access/administrators/{administrator.Id:D}/roles",
            accessToken,
            new { roleIds = roleIds.Select(roleId => roleId.ToString("D")).ToArray() });
        request.Headers.TryAddWithoutValidation("If-Match", administrator.ETag);
        var response = await SendAsync(client, request);
        return ParseAdministrator(ReadEntitySuccess(response));
    }

    private static async Task<AdministratorProjection> SetAdministratorActiveStateAsync(
        HttpClient client,
        string accessToken,
        AdministratorProjection administrator,
        bool isActive)
    {
        using var request = CreateAuthorizedRequest(
            HttpMethod.Put,
            $"/api/admin-access/administrators/{administrator.Id:D}/active-state",
            accessToken,
            new { isActive });
        request.Headers.TryAddWithoutValidation("If-Match", administrator.ETag);
        var response = await SendAsync(client, request);
        return ParseAdministrator(ReadEntitySuccess(response));
    }

    private static async Task<AdministratorProjection> ResetAdministratorPasswordAsync(
        HttpClient client,
        string accessToken,
        AdministratorProjection administrator,
        string newPassword)
    {
        using var request = CreateAuthorizedRequest(
            HttpMethod.Post,
            $"/api/admin-access/administrators/{administrator.Id:D}/password-reset",
            accessToken,
            new { newPassword });
        request.Headers.TryAddWithoutValidation("If-Match", administrator.ETag);
        request.Headers.TryAddWithoutValidation("Idempotency-Key", Guid.NewGuid().ToString("D"));
        var response = await SendAsync(client, request);
        return ParseAdministrator(ReadEntitySuccess(response));
    }

    private static async Task AssertAdministratorPagingAsync(
        HttpClient client,
        string accessToken,
        string prefix,
        Guid firstAdministratorId,
        Guid secondAdministratorId)
    {
        var firstPage = await ListAdministratorsAsync(
            client,
            accessToken,
            $"search={Uri.EscapeDataString(prefix)}&isActive=true&page=1&pageSize=1");
        var secondPage = await ListAdministratorsAsync(
            client,
            accessToken,
            $"search={Uri.EscapeDataString(prefix)}&isActive=true&page=2&pageSize=1");

        Assert.Equal(2, firstPage.TotalCount);
        Assert.Equal(2, secondPage.TotalCount);
        var firstAdministrator = Assert.Single(firstPage.Items);
        var secondAdministrator = Assert.Single(secondPage.Items);
        Assert.NotEqual(firstAdministrator.Id, secondAdministrator.Id);
        Assert.Contains(firstAdministrator.Id, new[] { firstAdministratorId, secondAdministratorId });
        Assert.Contains(secondAdministrator.Id, new[] { firstAdministratorId, secondAdministratorId });
        Assert.True(
            string.CompareOrdinal(firstAdministrator.UserName, secondAdministrator.UserName) < 0);
    }

    private static async Task<PagedResponse<AdministratorProjection>> ListAdministratorsAsync(
        HttpClient client,
        string accessToken,
        string query)
    {
        using var request = CreateAuthorizedRequest(
            HttpMethod.Get,
            $"/api/admin-access/administrators?{query}",
            accessToken);
        var response = await SendAsync(client, request);
        return ParsePage(
            ReadSuccessData(response, managementResponse: true),
            ParseAdministratorWithoutEtag,
            response.Body);
    }

    private static async Task AssertRolePagingAsync(
        HttpClient client,
        string accessToken,
        string prefix,
        Guid firstRoleId,
        Guid secondRoleId)
    {
        var firstPage = await ListRolesAsync(
            client,
            accessToken,
            $"search={Uri.EscapeDataString(prefix)}&isBuiltIn=false&page=1&pageSize=1");
        var secondPage = await ListRolesAsync(
            client,
            accessToken,
            $"search={Uri.EscapeDataString(prefix)}&isBuiltIn=false&page=2&pageSize=1");

        Assert.Equal(2, firstPage.TotalCount);
        Assert.Equal(2, secondPage.TotalCount);
        var firstRole = Assert.Single(firstPage.Items);
        var secondRole = Assert.Single(secondPage.Items);
        Assert.NotEqual(firstRole.Id, secondRole.Id);
        Assert.Contains(firstRole.Id, new[] { firstRoleId, secondRoleId });
        Assert.Contains(secondRole.Id, new[] { firstRoleId, secondRoleId });
        Assert.True(string.CompareOrdinal(firstRole.Name, secondRole.Name) < 0);
    }

    private static async Task<PagedResponse<RoleProjection>> ListRolesAsync(
        HttpClient client,
        string accessToken,
        string query)
    {
        using var request = CreateAuthorizedRequest(
            HttpMethod.Get,
            $"/api/admin-access/roles?{query}",
            accessToken);
        var response = await SendAsync(client, request);
        return ParsePage(ReadSuccessData(response, managementResponse: true), ParseRoleWithoutEtag, response.Body);
    }

    private static async Task<PagedResponse<SessionFamilyProjection>> ListSessionsAsync(
        HttpClient client,
        string accessToken,
        Guid administratorId,
        int? state,
        int page,
        int pageSize)
    {
        var stateQuery = state.HasValue ? $"&state={state.Value.ToString(CultureInfo.InvariantCulture)}" : string.Empty;
        using var request = CreateAuthorizedRequest(
            HttpMethod.Get,
            $"/api/admin-access/sessions?administratorId={administratorId:D}{stateQuery}&page={page.ToString(CultureInfo.InvariantCulture)}&pageSize={pageSize.ToString(CultureInfo.InvariantCulture)}",
            accessToken);
        var response = await SendAsync(client, request);
        return ParsePage(ReadSuccessData(response, managementResponse: true), ParseSessionFamily, response.Body);
    }

    private static async Task<SessionFamilyProjection> RevokeSessionFamilyAsync(
        HttpClient client,
        string accessToken,
        Guid familyId)
    {
        using var request = CreateAuthorizedRequest(
            HttpMethod.Post,
            $"/api/admin-access/sessions/{familyId:D}/revoke",
            accessToken);
        var response = await SendAsync(client, request);
        var session = ParseSessionFamily(ReadSuccessData(response, managementResponse: true));
        return session with { RawBody = response.Body };
    }

    private static async Task AssertSessionPagingAsync(
        HttpClient client,
        string accessToken,
        Guid administratorId)
    {
        var firstPage = await ListSessionsAsync(
            client,
            accessToken,
            administratorId,
            state: null,
            page: 1,
            pageSize: 1);
        var secondPage = await ListSessionsAsync(
            client,
            accessToken,
            administratorId,
            state: null,
            page: 2,
            pageSize: 1);

        Assert.True(firstPage.TotalCount >= 3);
        Assert.Equal(firstPage.TotalCount, secondPage.TotalCount);
        Assert.NotEqual(firstPage.Items[0].FamilyId, secondPage.Items[0].FamilyId);
        Assert.True(
            firstPage.Items[0].LastActivityAtUtc >= secondPage.Items[0].LastActivityAtUtc);
    }

    private static async Task AssertAuditPagingAsync(
        HttpClient client,
        string accessToken,
        string correlationId,
        Guid roleId)
    {
        var fromUtc = DateTime.UtcNow.AddDays(-1);
        var toUtc = DateTime.UtcNow.AddDays(1);
        var query =
            $"fromUtc={Uri.EscapeDataString(fromUtc.ToString("O", CultureInfo.InvariantCulture))}" +
            $"&toUtc={Uri.EscapeDataString(toUtc.ToString("O", CultureInfo.InvariantCulture))}" +
            $"&succeeded=true&roleId={roleId:D}&correlationId={Uri.EscapeDataString(correlationId)}";
        var firstPage = await ListAuditRecordsAsync(client, accessToken, query + "&page=1&pageSize=1");
        var secondPage = await ListAuditRecordsAsync(client, accessToken, query + "&page=2&pageSize=1");

        Assert.True(firstPage.TotalCount >= 5);
        Assert.Equal(firstPage.TotalCount, secondPage.TotalCount);
        Assert.NotEqual(firstPage.Items[0].Id, secondPage.Items[0].Id);
        Assert.True(firstPage.Items[0].OccurredAtUtc >= secondPage.Items[0].OccurredAtUtc);

        var roleCreateQuery = query +
            $"&action={(int)AdminAccessAuditActionDto.RoleCreated}&page=1&pageSize=20";
        var roleCreatePage = await ListAuditRecordsAsync(client, accessToken, roleCreateQuery);
        Assert.Contains(roleCreatePage.Items, record => record.RoleId == roleId);
    }

    private static async Task<PagedResponse<AuditProjection>> ListAuditRecordsAsync(
        HttpClient client,
        string accessToken,
        string query)
    {
        using var request = CreateAuthorizedRequest(
            HttpMethod.Get,
            $"/api/admin-access/audit-records?{query}",
            accessToken);
        var response = await SendAsync(client, request);
        return ParsePage(ReadSuccessData(response, managementResponse: true), ParseAuditRecord, response.Body);
    }

    private static async Task AssertTargetHasCurrentRolePermissionsAsync(
        HttpClient client,
        string accessToken,
        Guid administratorId,
        string roleName,
        IReadOnlyCollection<string> expectedPermissionNames)
    {
        await AssertCurrentAdministratorAsync(
            client,
            accessToken,
            administratorId,
            roleName,
            expectedPermissionNames);
        var codes = await GetPermissionCodesAsync(client, accessToken);
        Assert.Equal(
            expectedPermissionNames.Order(StringComparer.Ordinal),
            codes.Order(StringComparer.Ordinal));
        var administrators = await ListAdministratorsAsync(
            client,
            accessToken,
            "page=1&pageSize=20");
        Assert.NotEmpty(administrators.Items);
    }

    private static async Task AssertRoleStateBlocksExistingSessionAsync(
        HttpClient client,
        string accessToken)
    {
        await AssertProtectedRequestForbiddenAsync(client, accessToken, "/api/auth/codes");
        await AssertProtectedRequestForbiddenAsync(client, accessToken, "/api/user/info");
        await AssertProtectedRequestForbiddenAsync(
            client,
            accessToken,
            "/api/admin-access/administrators?page=1&pageSize=20");
    }

    private static async Task AssertLeafPermissionRemovalUpdatesTheExistingSessionAsync(
        HttpClient client,
        string accessToken,
        Guid administratorId,
        string roleName,
        string accessPermission)
    {
        await AssertCurrentAdministratorAsync(
            client,
            accessToken,
            administratorId,
            roleName,
            [accessPermission]);
        var codes = await GetPermissionCodesAsync(client, accessToken);
        Assert.Equal([accessPermission], codes);
        await AssertProtectedRequestForbiddenAsync(
            client,
            accessToken,
            "/api/admin-access/administrators?page=1&pageSize=20");
    }

    private static async Task AssertCurrentAdministratorAsync(
        HttpClient client,
        string accessToken,
        Guid administratorId,
        string roleName,
        IReadOnlyCollection<string> expectedPermissionNames)
    {
        using var request = CreateAuthorizedRequest(HttpMethod.Get, "/api/user/info", accessToken);
        var response = await SendAsync(client, request);
        var data = ReadSuccessData(response, managementResponse: false);
        Assert.Equal(administratorId, data.GetProperty("id").GetGuid());
        Assert.Contains(roleName, data.GetProperty("roles").EnumerateArray().Select(value => value.GetString()));
        Assert.Equal(
            expectedPermissionNames.Order(StringComparer.Ordinal),
            data.GetProperty("permissionCodes").EnumerateArray()
                .Select(value => value.GetString())
                .Order(StringComparer.Ordinal));
    }

    private static async Task<IReadOnlyList<string>> GetPermissionCodesAsync(
        HttpClient client,
        string accessToken)
    {
        using var request = CreateAuthorizedRequest(HttpMethod.Get, "/api/auth/codes", accessToken);
        var response = await SendAsync(client, request);
        var data = ReadSuccessData(response, managementResponse: false);
        return data.EnumerateArray().Select(value => value.GetString()!).ToArray();
    }

    private static async Task AssertProtectedRequestForbiddenAsync(
        HttpClient client,
        string accessToken,
        string path)
    {
        using var request = CreateAuthorizedRequest(HttpMethod.Get, path, accessToken);
        var response = await SendAsync(client, request);
        AssertFailure(response, HttpStatusCode.Forbidden, AdminAccessErrorCodes.Forbidden);
    }

    private static async Task AssertRemoteOpenApiContractAsync(HttpClient client)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/swagger/v1/swagger.json");
        var response = await SendAsync(client, request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var trackedContractPath = AdminAccessManagementOpenApiContract.GetTrackedContractPath();
        Assert.True(File.Exists(trackedContractPath));
        var expected = ParseJsonObject(
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true)
                .GetString(File.ReadAllBytes(trackedContractPath)));
        var actual = ParseJsonObject(response.Body);

        var expectedOpenApiVersion = expected["openapi"]?.GetValue<string>();
        var actualOpenApiVersion = actual["openapi"]?.GetValue<string>();
        Assert.False(string.IsNullOrWhiteSpace(expectedOpenApiVersion));
        Assert.Equal(expectedOpenApiVersion, actualOpenApiVersion);
        AssertJsonEquivalent(RequireJsonObject(expected, "info"), RequireJsonObject(actual, "info"));

        var expectedPaths = RequireJsonObject(expected, "paths");
        var actualPaths = RequireJsonObject(actual, "paths");
        var actualManagementPaths = actualPaths
            .Where(path => IsManagementPath(path.Key))
            .ToDictionary(path => path.Key, path => path.Value, StringComparer.Ordinal);
        Assert.Equal(15, expectedPaths.Count);
        Assert.Equal(17, expectedPaths.Sum(path => GetHttpMethods(RequireJsonObject(expectedPaths, path.Key)).Count));
        Assert.Equal(
            expectedPaths.Select(path => path.Key).Order(StringComparer.Ordinal),
            actualManagementPaths.Keys.Order(StringComparer.Ordinal));
        foreach (var expectedPathEntry in expectedPaths)
        {
            var expectedPathItem = RequireJsonObject(expectedPaths, expectedPathEntry.Key);
            var actualPathItem = actualManagementPaths[expectedPathEntry.Key] as JsonObject;
            Assert.NotNull(actualPathItem);
            Assert.Equal(
                GetHttpMethods(expectedPathItem).Order(StringComparer.Ordinal),
                GetHttpMethods(actualPathItem!).Order(StringComparer.Ordinal));
            AssertJsonEquivalent(expectedPathItem, actualPathItem!);
        }

        var expectedComponents = RequireJsonObject(expected, "components");
        var actualComponents = RequireJsonObject(actual, "components");
        AssertJsonEquivalent(
            RequireJsonObject(expectedComponents, "securitySchemes"),
            RequireJsonObject(actualComponents, "securitySchemes"));
        var expectedSchemas = RequireJsonObject(expectedComponents, "schemas");
        var actualSchemas = RequireJsonObject(actualComponents, "schemas");
        Assert.Equal(
            expectedSchemas.Select(schema => schema.Key).Order(StringComparer.Ordinal),
            actualSchemas.Select(schema => schema.Key)
            .Where(expectedSchemas.ContainsKey)
            .Order(StringComparer.Ordinal));
        foreach (var expectedSchema in expectedSchemas)
        {
            AssertJsonEquivalent(
                expectedSchema.Value!,
                actualSchemas[expectedSchema.Key]!);
        }
    }

    private static JsonObject ParseJsonObject(string source)
    {
        return JsonNode.Parse(source) as JsonObject
            ?? throw new InvalidOperationException("Expected an OpenAPI JSON object.");
    }

    private static JsonObject RequireJsonObject(JsonObject owner, string name)
    {
        return owner[name] as JsonObject
            ?? throw new InvalidOperationException($"OpenAPI JSON is missing object '{name}'.");
    }

    private static IReadOnlyList<string> GetHttpMethods(JsonObject pathItem)
    {
        return pathItem
            .Where(property => HttpMethods.Contains(property.Key))
            .Select(property => property.Key)
            .ToArray();
    }

    private static bool IsManagementPath(string path)
    {
        return string.Equals(path, "/api/admin-access", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/api/admin-access/", StringComparison.OrdinalIgnoreCase);
    }

    private static void AssertJsonEquivalent(JsonNode expected, JsonNode actual)
    {
        Assert.True(JsonNode.DeepEquals(expected, actual));
    }

    private static HttpRequestMessage CreateAuthorizedRequest(
        HttpMethod method,
        string path,
        string accessToken,
        object? body = null)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return request;
    }

    private static async Task<HttpResult> SendAsync(HttpClient client, HttpRequestMessage request)
    {
        var recorder = RedactionRecorders.TryGetValue(client, out var value) ? value : null;
        if (recorder is not null)
        {
            await recorder.RecordRequestAsync(request);
        }

        using (request)
        using (var response = await client.SendAsync(request))
        {
            var body = await response.Content.ReadAsStringAsync();
            var setCookieHeaders = response.Headers.TryGetValues("Set-Cookie", out var values)
                ? values.ToArray()
                : [];
            recorder?.RecordResponse(request.RequestUri, body, setCookieHeaders);
            return new HttpResult(response.StatusCode, body, response.Headers.ETag, setCookieHeaders);
        }
    }

    private static JsonElement ReadSuccessData(HttpResult response, bool managementResponse)
    {
        var root = ParseJson(response.Body);
        if (response.StatusCode != HttpStatusCode.OK)
        {
            var errorCode = root.TryGetProperty("error", out var error) &&
                error.TryGetProperty("code", out var code) &&
                code.ValueKind == JsonValueKind.String
                    ? code.GetString()
                    : "unavailable";
            Assert.Fail($"Expected HTTP 200, received {(int)response.StatusCode} ({errorCode}).");
        }

        Assert.True(root.GetProperty("succeeded").GetBoolean());
        Assert.True(root.TryGetProperty("data", out var data));
        if (managementResponse)
        {
            AssertManagementResponseIsRedacted(root);
        }

        return data.Clone();
    }

    private static EntityPayload ReadEntitySuccess(HttpResult response)
    {
        var data = ReadSuccessData(response, managementResponse: true);
        var revision = data.GetProperty("revision").GetInt64();
        Assert.True(revision > 0);
        var entityTag = Assert.IsType<EntityTagHeaderValue>(response.ETag);
        Assert.False(entityTag.IsWeak);
        Assert.Matches(@"^""[1-9]\d*""$", entityTag.Tag!);
        Assert.Equal($"\"{revision.ToString(CultureInfo.InvariantCulture)}\"", entityTag.Tag);
        return new EntityPayload(data, entityTag.Tag!);
    }

    private static void AssertFailure(HttpResult response, HttpStatusCode expectedStatus, string expectedCode)
    {
        Assert.Equal(expectedStatus, response.StatusCode);
        var root = ParseJson(response.Body);
        Assert.False(root.GetProperty("succeeded").GetBoolean());
        Assert.Equal(expectedCode, root.GetProperty("error").GetProperty("code").GetString());
    }

    private static void AssertSanitizedUnexpectedFailure(HttpResult response)
    {
        var error = ParseJson(response.Body).GetProperty("error");
        Assert.Equal("An internal server error occurred.", error.GetProperty("message").GetString());
        foreach (var prohibitedDetail in new[]
                 {
                     AuditFailureConstraintName,
                     "admin_audit_records",
                     "DbUpdateException",
                     "EntityFrameworkCore",
                     "MySql",
                     "SQLSTATE"
                 })
        {
            Assert.False(
                response.Body.Contains(prohibitedDetail, StringComparison.OrdinalIgnoreCase),
                "An unexpected management failure exposed an implementation detail.");
        }
    }

    private static JsonElement ParseJson(string body)
    {
        using var document = JsonDocument.Parse(body);
        return document.RootElement.Clone();
    }

    private static AdministratorProjection ParseAdministrator(EntityPayload payload)
    {
        return ParseAdministrator(payload.Data, payload.ETag);
    }

    private static AdministratorProjection ParseAdministratorWithoutEtag(JsonElement data)
    {
        return ParseAdministrator(data, string.Empty);
    }

    private static AdministratorProjection ParseAdministrator(JsonElement data, string eTag)
    {
        return new AdministratorProjection(
            data.GetProperty("id").GetGuid(),
            data.GetProperty("userName").GetString()!,
            data.GetProperty("displayName").GetString()!,
            data.GetProperty("isActive").GetBoolean(),
            data.GetProperty("revision").GetInt64(),
            data.GetProperty("roles").EnumerateArray()
                .Select(role => new RoleReferenceProjection(
                    role.GetProperty("id").GetGuid(),
                    role.GetProperty("name").GetString()!))
                .ToArray(),
            eTag);
    }

    private static RoleProjection ParseRole(EntityPayload payload)
    {
        var data = payload.Data;
        return new RoleProjection(
            data.GetProperty("id").GetGuid(),
            data.GetProperty("name").GetString()!,
            data.GetProperty("isActive").GetBoolean(),
            data.GetProperty("revision").GetInt64(),
            data.GetProperty("permissionNames").EnumerateArray()
                .Select(value => value.GetString()!)
                .ToArray(),
            payload.ETag);
    }

    private static RoleProjection ParseRoleWithoutEtag(JsonElement data)
    {
        return new RoleProjection(
            data.GetProperty("id").GetGuid(),
            data.GetProperty("name").GetString()!,
            data.GetProperty("isActive").GetBoolean(),
            data.GetProperty("revision").GetInt64(),
            [],
            string.Empty);
    }

    private static void AssertRoleProjectionEqual(RoleProjection expected, RoleProjection actual)
    {
        Assert.Equal(expected.Id, actual.Id);
        Assert.Equal(expected.Name, actual.Name);
        Assert.Equal(expected.IsActive, actual.IsActive);
        Assert.Equal(expected.Revision, actual.Revision);
        Assert.Equal(expected.ETag, actual.ETag);
        Assert.Equal(
            expected.PermissionNames.Order(StringComparer.Ordinal),
            actual.PermissionNames.Order(StringComparer.Ordinal));
    }

    private static void AssertAdministratorProjectionEqual(
        AdministratorProjection expected,
        AdministratorProjection actual)
    {
        Assert.Equal(expected.Id, actual.Id);
        Assert.Equal(expected.UserName, actual.UserName);
        Assert.Equal(expected.DisplayName, actual.DisplayName);
        Assert.Equal(expected.IsActive, actual.IsActive);
        Assert.Equal(expected.Revision, actual.Revision);
        Assert.Equal(expected.ETag, actual.ETag);
        Assert.Equal(
            expected.Roles.Select(role => (role.Id, role.Name)),
            actual.Roles.Select(role => (role.Id, role.Name)));
    }

    private static SessionFamilyProjection ParseSessionFamily(JsonElement data)
    {
        return new SessionFamilyProjection(
            data.GetProperty("familyId").GetGuid(),
            data.GetProperty("administratorId").GetGuid(),
            data.GetProperty("lastActivityAtUtc").GetDateTime(),
            data.GetProperty("state").GetInt32(),
            data.TryGetProperty("revocationReason", out var reason) && reason.ValueKind != JsonValueKind.Null
                ? reason.GetInt32()
                : null,
            string.Empty);
    }

    private static AuditProjection ParseAuditRecord(JsonElement data)
    {
        return new AuditProjection(
            data.GetProperty("id").GetGuid(),
            data.GetProperty("occurredAtUtc").GetDateTime(),
            data.TryGetProperty("roleId", out var roleId) && roleId.ValueKind != JsonValueKind.Null
                ? roleId.GetGuid()
                : null);
    }

    private static PagedResponse<T> ParsePage<T>(
        JsonElement data,
        Func<JsonElement, T> map,
        string rawBody)
    {
        return new PagedResponse<T>(
            data.GetProperty("items").EnumerateArray().Select(map).ToArray(),
            data.GetProperty("totalCount").GetInt64(),
            data.GetProperty("page").GetInt32(),
            data.GetProperty("pageSize").GetInt32(),
            rawBody);
    }

    private static void AssertAccessTokenLifetime(string accessToken)
    {
        var token = new JwtSecurityTokenHandler().ReadJwtToken(accessToken);
        Assert.Equal(TimeSpan.FromMinutes(15), token.ValidTo - token.ValidFrom);
    }

    private static void AssertRefreshCookie(string refreshCookie)
    {
        Assert.Contains("HttpOnly", refreshCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Secure", refreshCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("SameSite=Strict", refreshCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Path=/api/auth", refreshCookie, StringComparison.OrdinalIgnoreCase);
    }

    private static void AssertRedactedSessionBody(string body, params string[] sensitiveValues)
    {
        var root = ParseJson(body);
        AssertManagementResponseIsRedacted(root);
        foreach (var sensitiveValue in sensitiveValues.Where(value => !string.IsNullOrEmpty(value)))
        {
            Assert.False(body.Contains(sensitiveValue, StringComparison.Ordinal));
        }
    }

    private static void AssertManagementResponseIsRedacted(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    AssertManagementResponseIsRedacted(item);
                }

                return;
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    var name = property.Name;
                    Assert.False(
                        name.Contains("password", StringComparison.OrdinalIgnoreCase) ||
                        name.Contains("token", StringComparison.OrdinalIgnoreCase) ||
                        name.Contains("hash", StringComparison.OrdinalIgnoreCase) ||
                        name.Contains("secret", StringComparison.OrdinalIgnoreCase) ||
                        name.Contains("receipt", StringComparison.OrdinalIgnoreCase) ||
                        name is "sessionId" or "replacedBySessionId" or "requestBody" or
                            "requestFingerprint" or "idempotencyKey" or "commandKind",
                        $"Management response exposes sensitive property '{name}'.");
                    AssertManagementResponseIsRedacted(property.Value);
                }

                return;
        }
    }

    private static string RequireRegisteredPermission(
        IReadOnlySet<string> registeredPermissions,
        string expectedPermission)
    {
        Assert.Contains(expectedPermission, registeredPermissions);
        return expectedPermission;
    }

    private static string ExtractCookieValue(string setCookieHeader)
    {
        var firstSegment = setCookieHeader.Split(';', 2)[0];
        var prefix = AdminAccessController.RefreshTokenCookieName + "=";
        Assert.StartsWith(prefix, firstSegment, StringComparison.Ordinal);
        var value = firstSegment[prefix.Length..];
        Assert.False(string.IsNullOrWhiteSpace(value));
        return value;
    }

    private static Uri CreateApiBaseUri(string value)
    {
        var normalized = value.TrimEnd('/') + "/";
        return Uri.TryCreate(normalized, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            ? uri
            : throw new InvalidOperationException(
                $"{ApiUrlVariable} must be an absolute HTTP(S) URL.");
    }

    private static string RequireEnvironmentVariable(string name)
    {
        return Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
            ? value
            : throw new InvalidOperationException($"{name} is required for composed API acceptance.");
    }

    private sealed class RedactionRecorder
    {
        private readonly ConcurrentQueue<string> _managementResponseBodies;
        private readonly ConcurrentQueue<string> _requestBodies;
        private readonly ConcurrentQueue<string> _transportSecrets;

        public RedactionRecorder(
            ConcurrentQueue<string> requestBodies,
            ConcurrentQueue<string> transportSecrets,
            ConcurrentQueue<string> managementResponseBodies)
        {
            _requestBodies = requestBodies;
            _transportSecrets = transportSecrets;
            _managementResponseBodies = managementResponseBodies;
        }

        public async Task RecordRequestAsync(HttpRequestMessage request)
        {
            if (request.Content is not null)
            {
                var body = await request.Content.ReadAsStringAsync();
                if (!string.IsNullOrEmpty(body))
                {
                    _requestBodies.Enqueue(body);
                }
            }

            if (request.Headers.Authorization?.Parameter is { Length: > 0 } accessToken)
            {
                _transportSecrets.Enqueue(accessToken);
            }

            if (request.Headers.TryGetValues("Cookie", out var cookieHeaders))
            {
                foreach (var cookieHeader in cookieHeaders)
                {
                    _transportSecrets.Enqueue(cookieHeader);
                    var prefix = AdminAccessController.RefreshTokenCookieName + "=";
                    foreach (var cookie in cookieHeader.Split(
                                 ';',
                                 StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    {
                        if (cookie.StartsWith(prefix, StringComparison.Ordinal))
                        {
                            _transportSecrets.Enqueue(cookie[prefix.Length..]);
                        }
                    }
                }
            }
        }

        public void RecordResponse(
            Uri? requestUri,
            string responseBody,
            IReadOnlyList<string> setCookieHeaders)
        {
            RecordResponseCookies(setCookieHeaders);
            var path = GetRequestPath(requestUri);
            if (IsManagementPath(path))
            {
                if (!string.IsNullOrEmpty(responseBody))
                {
                    _managementResponseBodies.Enqueue(responseBody);
                }
            }
            else if (IsAuthenticationPath(path))
            {
                RecordAuthenticationResponse(responseBody);
            }
        }

        private static bool IsAuthenticationPath(string path)
        {
            return string.Equals(path, "/api/auth/login", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(path, "/api/auth/refresh", StringComparison.OrdinalIgnoreCase);
        }

        private static string GetRequestPath(Uri? requestUri)
        {
            var path = requestUri is { IsAbsoluteUri: true }
                ? requestUri.AbsolutePath
                : requestUri?.OriginalString ?? string.Empty;
            var queryIndex = path.IndexOf('?', StringComparison.Ordinal);
            return queryIndex >= 0
                ? path[..queryIndex]
                : path;
        }

        private void RecordAuthenticationResponse(string responseBody)
        {
            try
            {
                using var document = JsonDocument.Parse(responseBody);
                if (document.RootElement.TryGetProperty("data", out var data) &&
                    data.ValueKind == JsonValueKind.Object &&
                    data.TryGetProperty("accessToken", out var accessToken) &&
                    accessToken.ValueKind == JsonValueKind.String &&
                    accessToken.GetString() is { Length: > 0 } value)
                {
                    _transportSecrets.Enqueue(value);
                }
            }
            catch (JsonException)
            {
                // The request-level assertions own malformed authentication responses.
            }
        }

        private void RecordResponseCookies(IReadOnlyList<string> setCookieHeaders)
        {
            var prefix = AdminAccessController.RefreshTokenCookieName + "=";
            foreach (var cookieHeader in setCookieHeaders)
            {
                _transportSecrets.Enqueue(cookieHeader);
                var firstSegment = cookieHeader.Split(';', 2)[0];
                if (firstSegment.StartsWith(prefix, StringComparison.Ordinal))
                {
                    _transportSecrets.Enqueue(firstSegment[prefix.Length..]);
                }
            }
        }
    }

    private sealed record HttpResult(
        HttpStatusCode StatusCode,
        string Body,
        EntityTagHeaderValue? ETag,
        IReadOnlyList<string> SetCookieHeaders);

    private sealed record EntityPayload(JsonElement Data, string ETag);

    private sealed record AuthenticationSession(
        string AccessToken,
        DateTime ExpiresAtUtc,
        string RefreshToken,
        string RefreshCookie);

    private sealed record PermissionGroupProjection(
        string Name,
        IReadOnlyList<PermissionProjection> Permissions);

    private sealed record PermissionProjection(string Name);

    private sealed record RoleReferenceProjection(Guid Id, string Name);

    private sealed record AdministratorProjection(
        Guid Id,
        string UserName,
        string DisplayName,
        bool IsActive,
        long Revision,
        IReadOnlyList<RoleReferenceProjection> Roles,
        string ETag);

    private sealed record RoleProjection(
        Guid Id,
        string Name,
        bool IsActive,
        long Revision,
        IReadOnlyList<string> PermissionNames,
        string ETag);

    private sealed record SessionFamilyProjection(
        Guid FamilyId,
        Guid AdministratorId,
        DateTime LastActivityAtUtc,
        int State,
        int? RevocationReason,
        string RawBody);

    private sealed record AuditProjection(Guid Id, DateTime OccurredAtUtc, Guid? RoleId);

    private sealed record PagedResponse<T>(
        IReadOnlyList<T> Items,
        long TotalCount,
        int Page,
        int PageSize,
        string RawBody);
}
