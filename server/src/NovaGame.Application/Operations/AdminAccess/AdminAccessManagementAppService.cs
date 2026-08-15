using System.Text;
using NovaCore.BuildingBlocks.CurrentUser;
using NovaCore.BuildingBlocks.Infrastructure;
using NovaCore.BuildingBlocks.Permissions;
using NovaCore.BuildingBlocks.Timing;
using NovaCore.BuildingBlocks.UnitOfWork;
using NovaGame.Application.Auditing;
using NovaGame.Application.Contracts.Operations.AdminAccess;
using NovaGame.Domain.Operations.AdminAccess;
using NovaGame.Domain.Shared.Operations.AdminAccess;

namespace NovaGame.Application.Operations.AdminAccess;

public sealed class AdminAccessManagementAppService : IAdminAccessManagementAppService
{
    private static readonly TimeSpan MaximumAuditRange =
        TimeSpan.FromDays(AdminAccessManagementContractLimits.MaximumAuditRangeDays);

    private readonly IUnitOfWorkManager _unitOfWorkManager;
    private readonly IAdminAccessManagementRepository _repository;
    private readonly IAdminAccessCommandReceiptRepository _receiptRepository;
    private readonly AdminAccessManagementPolicy _policy;
    private readonly RefreshTokenFamilyPolicy _refreshTokenFamilyPolicy;
    private readonly IPasswordHashingService _passwordHashingService;
    private readonly IPermissionChecker _permissionChecker;
    private readonly IPermissionDefinitionManager _permissionDefinitionManager;
    private readonly IRequestAuditContextAccessor _requestAuditContextAccessor;
    private readonly ICurrentUser _currentUser;
    private readonly IGuidGenerator _guidGenerator;
    private readonly IClock _clock;

    public AdminAccessManagementAppService(
        IUnitOfWorkManager unitOfWorkManager,
        IAdminAccessManagementRepository repository,
        IAdminAccessCommandReceiptRepository receiptRepository,
        AdminAccessManagementPolicy policy,
        RefreshTokenFamilyPolicy refreshTokenFamilyPolicy,
        IPasswordHashingService passwordHashingService,
        IPermissionChecker permissionChecker,
        IPermissionDefinitionManager permissionDefinitionManager,
        IRequestAuditContextAccessor requestAuditContextAccessor,
        ICurrentUser currentUser,
        IGuidGenerator guidGenerator,
        IClock clock)
    {
        _unitOfWorkManager = unitOfWorkManager;
        _repository = repository;
        _receiptRepository = receiptRepository;
        _policy = policy;
        _refreshTokenFamilyPolicy = refreshTokenFamilyPolicy;
        _passwordHashingService = passwordHashingService;
        _permissionChecker = permissionChecker;
        _permissionDefinitionManager = permissionDefinitionManager;
        _requestAuditContextAccessor = requestAuditContextAccessor;
        _currentUser = currentUser;
        _guidGenerator = guidGenerator;
        _clock = clock;
    }

    public async Task<AdminAccessPageDto<AdministratorDto>> ListAdministratorsAsync(
        ListAdministratorsRequest request,
        CancellationToken cancellationToken = default)
    {
        await RequirePermissionAsync(AdminAccessPermissions.AdministratorsRead, cancellationToken);
        var paging = ValidatePaging(request?.Page, request?.PageSize);
        var search = NormalizeSearch(request!.Search, normalizeForIdentity: true);
        ValidateOptionalIdentifier(request.RoleId, nameof(request.RoleId));

        await using var unitOfWork = await BeginUnitOfWorkAsync(cancellationToken);
        var page = await _repository.GetAdministratorPageAsync(
            search,
            request.IsActive,
            request.RoleId,
            paging.SkipCount,
            paging.PageSize,
            cancellationToken);
        var result = new AdminAccessPageDto<AdministratorDto>(
            page.Items.Select(MapAdministrator).ToArray(),
            page.TotalCount,
            paging.Page,
            paging.PageSize);
        await unitOfWork.CompleteAsync(cancellationToken);
        return result;
    }

    public async Task<AdministratorDto> CreateAdministratorAsync(
        CreateAdministratorRequest request,
        Guid idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        var actorId = await RequirePermissionAsync(
            AdminAccessPermissions.AdministratorsManage,
            cancellationToken);
        RequireRequest(request);
        RequireIdentifier(idempotencyKey, nameof(idempotencyKey));
        var userName = TranslateValidation(() => AdministratorUserName.Create(request.UserName));
        var displayName = TranslateValidation(() =>
            AdministratorDisplayName.Create(request.DisplayName).Value);
        ValidatePassword(request.InitialPassword, nameof(request.InitialPassword));
        var roleIds = ValidateRoleIds(request.RoleIds);
        var fingerprint = AdminAccessCommandFingerprint.ForAdministratorCreate(
            userName.NormalizedValue,
            displayName,
            roleIds);
        var now = GetUtcNow();
        var candidate = new AdminAccessCommandReceipt(
            _guidGenerator.Create(),
            actorId,
            AdminAccessCommandKind.AdministratorCreate,
            idempotencyKey,
            fingerprint,
            _guidGenerator.Create(),
            resultingRevision: 1,
            now);

        await using var unitOfWork = await BeginUnitOfWorkAsync(cancellationToken);
        var claim = await ClaimReceiptAsync(candidate, cancellationToken);
        if (!claim.IsNew)
        {
            var replay = await GetAdministratorReplayAsync(claim.Receipt, cancellationToken);
            await unitOfWork.CompleteAsync(cancellationToken);
            return replay;
        }

        var roles = await _repository.GetRolesForUpdateInGlobalOrderAsync(
            roleIds,
            cancellationToken);
        var activeRoles = TranslateDomain(() => _policy.RequireActiveRequestedRoles(roleIds, roles));
        var duplicate = await _repository.FindAdministratorByNormalizedUserNameForUpdateAsync(
            userName.NormalizedValue,
            cancellationToken);
        if (duplicate is not null)
        {
            throw Failure(
                AdminAccessManagementErrorCodes.UserNameAlreadyExists,
                "The normalized administrator user name is already in use.");
        }

        var roleTargets = activeRoles
            .Select(role => new AdministratorRoleTarget(_guidGenerator.Create(), role.Id))
            .ToArray();
        var passwordHash = _passwordHashingService.Hash(request.InitialPassword);
        var administrator = TranslateValidation(() => new Administrator(
            claim.Receipt.TargetResourceId,
            userName,
            displayName,
            passwordHash,
            now,
            roleTargets,
            actorId));
        await _repository.AddAdministratorAsync(administrator, cancellationToken);
        await AddAuditAsync(
            AdminAccessAuditAction.AdministratorCreated,
            now,
            actorId,
            administratorId: administrator.Id,
            subjectUserName: administrator.UserName,
            targetRevision: administrator.Revision,
            cancellationToken: cancellationToken);
        foreach (var role in activeRoles.OrderBy(role => role.Id))
        {
            await AddAuditAsync(
                AdminAccessAuditAction.RoleAssigned,
                now,
                actorId,
                administratorId: administrator.Id,
                roleId: role.Id,
                subjectUserName: administrator.UserName,
                targetRevision: administrator.Revision,
                cancellationToken: cancellationToken);
        }

        var result = MapAdministrator(administrator, activeRoles);
        await unitOfWork.CompleteAsync(cancellationToken);
        return result;
    }

    public async Task<AdministratorDto> GetAdministratorAsync(
        Guid administratorId,
        CancellationToken cancellationToken = default)
    {
        await RequirePermissionAsync(AdminAccessPermissions.AdministratorsRead, cancellationToken);
        RequireIdentifier(administratorId, nameof(administratorId));

        await using var unitOfWork = await BeginUnitOfWorkAsync(cancellationToken);
        var administrator = await _repository.FindAdministratorSnapshotAsync(
            administratorId,
            cancellationToken) ?? throw Failure(
                AdminAccessManagementErrorCodes.AdministratorNotFound,
                "The administrator was not found.");
        var result = MapAdministrator(administrator);
        await unitOfWork.CompleteAsync(cancellationToken);
        return result;
    }

    public async Task<AdministratorDto> ChangeAdministratorDisplayNameAsync(
        Guid administratorId,
        long expectedRevision,
        ChangeAdministratorDisplayNameRequest request,
        CancellationToken cancellationToken = default)
    {
        var actorId = await RequirePermissionAsync(
            AdminAccessPermissions.AdministratorsManage,
            cancellationToken);
        RequireIdentifier(administratorId, nameof(administratorId));
        ValidateExpectedRevision(expectedRevision);
        RequireRequest(request);
        var displayName = TranslateValidation(() =>
            AdministratorDisplayName.Create(request.DisplayName).Value);
        var now = GetUtcNow();

        await using var unitOfWork = await BeginUnitOfWorkAsync(cancellationToken);
        var administrator = await GetAdministratorForUpdateAsync(
            administratorId,
            lockMemberships: false,
            cancellationToken);
        var transition = TranslateDomain(() => administrator.ChangeDisplayName(
            expectedRevision,
            displayName,
            now));
        if (transition.Changed)
        {
            await AddAuditAsync(
                AdminAccessAuditAction.AdministratorDisplayNameChanged,
                now,
                actorId,
                administratorId: administrator.Id,
                subjectUserName: administrator.UserName,
                targetRevision: transition.Revision,
                cancellationToken: cancellationToken);
        }

        var assignedRoles = await GetAssignedRolesAsync(
            administrator,
            membershipsLocked: false,
            cancellationToken);
        var result = MapAdministrator(administrator, assignedRoles.Roles, assignedRoles.RoleIds);
        await unitOfWork.CompleteAsync(cancellationToken);
        return result;
    }

    public async Task<AdministratorDto> ReplaceAdministratorRolesAsync(
        Guid administratorId,
        long expectedRevision,
        ReplaceAdministratorRolesRequest request,
        CancellationToken cancellationToken = default)
    {
        var actorId = await RequirePermissionAsync(
            AdminAccessPermissions.AdministratorsManage,
            cancellationToken);
        RequireIdentifier(administratorId, nameof(administratorId));
        ValidateExpectedRevision(expectedRevision);
        RequireRequest(request);
        var requestedRoleIds = ValidateRoleIds(request.RoleIds);
        var now = GetUtcNow();

        await using var unitOfWork = await BeginUnitOfWorkAsync(cancellationToken);
        var builtInRoles = await _repository.GetBuiltInRolesForUpdateAsync(cancellationToken);
        var builtInRole = TranslateDomain(() => _policy.ResolveBuiltInRole(
            builtInRoles,
            administratorsExist: true)) ?? throw InvariantViolation(
                "The built-in administrator role is missing.");
        var probedRoleIds = await _repository.GetAdministratorRoleIdsAsync(
            administratorId,
            cancellationToken);
        var affectedRoleIds = probedRoleIds
            .Concat(requestedRoleIds)
            .Where(roleId => roleId != builtInRole.Id)
            .Distinct()
            .Order()
            .ToArray();
        var otherRoles = await _repository.GetRolesForUpdateInGlobalOrderAsync(
            affectedRoleIds,
            cancellationToken);
        var lockedRoles = otherRoles.Append(builtInRole).ToArray();
        var administrator = await GetAdministratorForUpdateAsync(
            administratorId,
            lockMemberships: true,
            cancellationToken);
        EnsureMembershipProbeMatches(probedRoleIds, administrator.RoleMemberships);
        var requestedRoles = TranslateDomain(() => _policy.RequireActiveRequestedRoles(
            requestedRoleIds,
            lockedRoles));
        var targetHasBuiltInRole = administrator.RoleMemberships.Any(
            membership => membership.RoleId == builtInRole.Id);
        var requestedIncludesBuiltInRole = requestedRoleIds.Contains(builtInRole.Id);
        var activeBuiltInCount = await _repository.CountActiveAdministratorsInRoleAsync(
            builtInRole.Id,
            cancellationToken);
        TranslateDomain(() => _policy.EnsureCanReplaceAdministratorRoles(
            actorId,
            administrator.Id,
            administrator.IsActive,
            targetHasBuiltInRole,
            requestedIncludesBuiltInRole,
            activeBuiltInCount));

        var currentMemberships = administrator.RoleMemberships.ToDictionary(
            membership => membership.RoleId);
        var targets = requestedRoleIds
            .Select(roleId => new AdministratorRoleTarget(
                currentMemberships.TryGetValue(roleId, out var membership)
                    ? membership.Id
                    : _guidGenerator.Create(),
                roleId))
            .ToArray();
        var transition = TranslateDomain(() => administrator.ReplaceRoles(
            expectedRevision,
            targets,
            actorId,
            now));
        foreach (var roleId in transition.AddedRoleIds)
        {
            await AddAuditAsync(
                AdminAccessAuditAction.RoleAssigned,
                now,
                actorId,
                administratorId: administrator.Id,
                roleId: roleId,
                subjectUserName: administrator.UserName,
                targetRevision: transition.Revision,
                cancellationToken: cancellationToken);
        }

        foreach (var roleId in transition.RemovedRoleIds)
        {
            await AddAuditAsync(
                AdminAccessAuditAction.RoleRemoved,
                now,
                actorId,
                administratorId: administrator.Id,
                roleId: roleId,
                subjectUserName: administrator.UserName,
                targetRevision: transition.Revision,
                cancellationToken: cancellationToken);
        }

        var result = MapAdministrator(administrator, requestedRoles);
        await unitOfWork.CompleteAsync(cancellationToken);
        return result;
    }

    public async Task<AdministratorDto> SetAdministratorActiveStateAsync(
        Guid administratorId,
        long expectedRevision,
        SetAdministratorActiveStateRequest request,
        CancellationToken cancellationToken = default)
    {
        var actorId = await RequirePermissionAsync(
            AdminAccessPermissions.AdministratorsManage,
            cancellationToken);
        RequireIdentifier(administratorId, nameof(administratorId));
        ValidateExpectedRevision(expectedRevision);
        RequireRequest(request);
        var now = GetUtcNow();

        await using var unitOfWork = await BeginUnitOfWorkAsync(cancellationToken);
        var builtInRoles = await _repository.GetBuiltInRolesForUpdateAsync(cancellationToken);
        var builtInRole = TranslateDomain(() => _policy.ResolveBuiltInRole(
            builtInRoles,
            administratorsExist: true)) ?? throw InvariantViolation(
                "The built-in administrator role is missing.");
        var administrator = await GetAdministratorForUpdateAsync(
            administratorId,
            lockMemberships: true,
            cancellationToken);
        var targetHasBuiltInRole = administrator.RoleMemberships.Any(
            membership => membership.RoleId == builtInRole.Id);
        var activeBuiltInCount = await _repository.CountActiveAdministratorsInRoleAsync(
            builtInRole.Id,
            cancellationToken);
        TranslateDomain(() => _policy.EnsureCanSetAdministratorActiveState(
            actorId,
            administrator.Id,
            administrator.IsActive,
            request.IsActive,
            targetHasBuiltInRole,
            activeBuiltInCount));
        var lockedFamilies = request.IsActive
            ? Array.Empty<LockedSessionFamily>()
            : await LockAdministratorFamiliesAsync(administrator.Id, cancellationToken);
        var transition = TranslateDomain(() => administrator.SetActiveState(
            expectedRevision,
            request.IsActive,
            now));
        if (transition.Changed)
        {
            await AddAuditAsync(
                request.IsActive
                    ? AdminAccessAuditAction.AdministratorActivated
                    : AdminAccessAuditAction.AdministratorDeactivated,
                now,
                actorId,
                administratorId: administrator.Id,
                subjectUserName: administrator.UserName,
                targetRevision: transition.Revision,
                cancellationToken: cancellationToken);
        }

        if (!request.IsActive)
        {
            await RevokeFamiliesAsync(
                lockedFamilies,
                administrator,
                RefreshTokenRevocationReason.AdministratorInactive,
                now,
                actorId,
                cancellationToken);
        }

        var assignedRoles = await GetAssignedRolesAsync(
            administrator,
            membershipsLocked: true,
            cancellationToken);
        var result = MapAdministrator(administrator, assignedRoles.Roles, assignedRoles.RoleIds);
        await unitOfWork.CompleteAsync(cancellationToken);
        return result;
    }

    public async Task<AdministratorDto> ResetAdministratorPasswordAsync(
        Guid administratorId,
        long expectedRevision,
        ResetAdministratorPasswordRequest request,
        Guid idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        var actorId = await RequirePermissionAsync(
            AdminAccessPermissions.AdministratorsResetPassword,
            cancellationToken);
        RequireIdentifier(administratorId, nameof(administratorId));
        ValidateExpectedRevision(expectedRevision);
        RequireIdentifier(idempotencyKey, nameof(idempotencyKey));
        RequireRequest(request);
        ValidatePassword(request.NewPassword, nameof(request.NewPassword));
        var fingerprint = AdminAccessCommandFingerprint.ForAdministratorPasswordReset(
            administratorId,
            expectedRevision);
        var now = GetUtcNow();
        var candidate = new AdminAccessCommandReceipt(
            _guidGenerator.Create(),
            actorId,
            AdminAccessCommandKind.AdministratorPasswordReset,
            idempotencyKey,
            fingerprint,
            administratorId,
            GetNextRevision(expectedRevision),
            now);

        await using var unitOfWork = await BeginUnitOfWorkAsync(cancellationToken);
        var claim = await ClaimReceiptAsync(candidate, cancellationToken);
        if (!claim.IsNew)
        {
            if (claim.Receipt.TargetResourceId != administratorId)
            {
                throw InvariantViolation("The stored password-reset receipt target is malformed.");
            }

            var replay = await GetAdministratorReplayAsync(claim.Receipt, cancellationToken);
            await unitOfWork.CompleteAsync(cancellationToken);
            return replay;
        }

        var administrator = await GetAdministratorForUpdateAsync(
            administratorId,
            lockMemberships: false,
            cancellationToken);
        TranslateDomain(() => _policy.EnsureCanResetAdministratorPassword(actorId, administrator.Id));
        var lockedFamilies = await LockAdministratorFamiliesAsync(
            administrator.Id,
            cancellationToken);
        var passwordHash = _passwordHashingService.Hash(request.NewPassword);
        var transition = TranslateDomain(() => administrator.ResetPasswordHash(
            expectedRevision,
            passwordHash,
            now));
        await AddAuditAsync(
            AdminAccessAuditAction.AdministratorPasswordReset,
            now,
            actorId,
            administratorId: administrator.Id,
            subjectUserName: administrator.UserName,
            targetRevision: transition.Revision,
            cancellationToken: cancellationToken);
        await RevokeFamiliesAsync(
            lockedFamilies,
            administrator,
            RefreshTokenRevocationReason.PasswordReset,
            now,
            actorId,
            cancellationToken);

        var assignedRoles = await GetAssignedRolesAsync(
            administrator,
            membershipsLocked: false,
            cancellationToken);
        var result = MapAdministrator(administrator, assignedRoles.Roles, assignedRoles.RoleIds);
        await unitOfWork.CompleteAsync(cancellationToken);
        return result;
    }

    public async Task<AdminAccessPageDto<AdminRoleSummaryDto>> ListAdminRolesAsync(
        ListAdminRolesRequest request,
        CancellationToken cancellationToken = default)
    {
        await RequirePermissionAsync(AdminAccessPermissions.RolesRead, cancellationToken);
        var paging = ValidatePaging(request?.Page, request?.PageSize);
        var search = NormalizeSearch(request!.Search, normalizeForIdentity: true);
        var registeredPermissionNames = GetRegisteredPermissionNames();

        await using var unitOfWork = await BeginUnitOfWorkAsync(cancellationToken);
        var page = await _repository.GetRolePageAsync(
            search,
            request.IsActive,
            request.IsBuiltIn,
            registeredPermissionNames,
            paging.SkipCount,
            paging.PageSize,
            cancellationToken);
        var result = new AdminAccessPageDto<AdminRoleSummaryDto>(
            page.Items.Select(MapRoleSummary).ToArray(),
            page.TotalCount,
            paging.Page,
            paging.PageSize);
        await unitOfWork.CompleteAsync(cancellationToken);
        return result;
    }

    public async Task<AdminRoleDto> CreateAdminRoleAsync(
        CreateAdminRoleRequest request,
        Guid idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        var actorId = await RequirePermissionAsync(AdminAccessPermissions.RolesManage, cancellationToken);
        RequireRequest(request);
        RequireIdentifier(idempotencyKey, nameof(idempotencyKey));
        var roleName = TranslateDomain(() => AdminRoleName.Create(request.Name));
        var requestedPermissionNames = ValidatePermissionNames(request.PermissionNames);
        var registeredPermissionNames = GetRegisteredPermissionNames();
        var fingerprint = AdminAccessCommandFingerprint.ForAdminRoleCreate(
            roleName.NormalizedValue,
            requestedPermissionNames);
        var now = GetUtcNow();
        var candidate = new AdminAccessCommandReceipt(
            _guidGenerator.Create(),
            actorId,
            AdminAccessCommandKind.AdminRoleCreate,
            idempotencyKey,
            fingerprint,
            _guidGenerator.Create(),
            resultingRevision: 1,
            now);

        await using var unitOfWork = await BeginUnitOfWorkAsync(cancellationToken);
        var claim = await ClaimReceiptAsync(candidate, cancellationToken);
        if (!claim.IsNew)
        {
            var replay = await GetRoleReplayAsync(
                claim.Receipt,
                registeredPermissionNames,
                cancellationToken);
            await unitOfWork.CompleteAsync(cancellationToken);
            return replay;
        }

        var permissionNames = TranslateDomain(() => _policy.RequireRegisteredPermissionSubset(
            requestedPermissionNames,
            registeredPermissionNames));

        var duplicate = await _repository.FindRoleByNormalizedNameForUpdateAsync(
            roleName.NormalizedValue,
            cancellationToken);
        if (duplicate is not null)
        {
            throw Failure(
                AdminAccessManagementErrorCodes.RoleNameAlreadyExists,
                "The normalized role name is already in use.");
        }

        var role = TranslateDomain(() => _policy.CreateCustomRole(
            claim.Receipt.TargetResourceId,
            roleName.Value,
            permissionNames,
            registeredPermissionNames,
            actorId,
            now,
            _guidGenerator.Create));
        await _repository.AddRoleAsync(role, cancellationToken);
        await AddAuditAsync(
            AdminAccessAuditAction.RoleCreated,
            now,
            actorId,
            roleId: role.Id,
            targetRevision: role.Revision,
            cancellationToken: cancellationToken);
        foreach (var permission in role.Permissions.OrderBy(permission => permission.Name, StringComparer.Ordinal))
        {
            await AddAuditAsync(
                AdminAccessAuditAction.PermissionGranted,
                now,
                actorId,
                roleId: role.Id,
                permissionName: permission.Name,
                targetRevision: role.Revision,
                cancellationToken: cancellationToken);
        }

        var result = MapRole(role, permissionNames, administratorCount: 0);
        await unitOfWork.CompleteAsync(cancellationToken);
        return result;
    }

    public async Task<AdminRoleDto> GetAdminRoleAsync(
        Guid roleId,
        CancellationToken cancellationToken = default)
    {
        await RequirePermissionAsync(AdminAccessPermissions.RolesRead, cancellationToken);
        RequireIdentifier(roleId, nameof(roleId));
        var registeredPermissionNames = GetRegisteredPermissionNames();

        await using var unitOfWork = await BeginUnitOfWorkAsync(cancellationToken);
        var role = await _repository.FindRoleSnapshotAsync(
            roleId,
            registeredPermissionNames,
            cancellationToken) ?? throw Failure(
                AdminAccessManagementErrorCodes.RoleNotFound,
                "The role was not found.");
        var result = MapRole(role);
        await unitOfWork.CompleteAsync(cancellationToken);
        return result;
    }

    public async Task<AdminRoleDto> RenameAdminRoleAsync(
        Guid roleId,
        long expectedRevision,
        RenameAdminRoleRequest request,
        CancellationToken cancellationToken = default)
    {
        var actorId = await RequirePermissionAsync(AdminAccessPermissions.RolesManage, cancellationToken);
        RequireIdentifier(roleId, nameof(roleId));
        ValidateExpectedRevision(expectedRevision);
        RequireRequest(request);
        var roleName = TranslateDomain(() => AdminRoleName.Create(request.Name));
        var registeredPermissionNames = GetRegisteredPermissionNames();
        var now = GetUtcNow();
        var probedRole = await _repository.FindRoleByNormalizedNameAsync(
            roleName.NormalizedValue,
            cancellationToken);

        await using var unitOfWork = await BeginUnitOfWorkAsync(cancellationToken);
        var affectedRoleIds = new[] { roleId, probedRole?.Id ?? roleId }
            .Distinct()
            .Order()
            .ToArray();
        var lockedRoles = await _repository.GetRolesForUpdateInGlobalOrderAsync(
            affectedRoleIds,
            cancellationToken);
        if (lockedRoles.All(candidate => candidate.Id != roleId))
        {
            throw Failure(AdminAccessManagementErrorCodes.RoleNotFound, "The role was not found.");
        }

        EnsureRoleSetMatches(affectedRoleIds, lockedRoles);
        var role = lockedRoles.Single(candidate => candidate.Id == roleId);
        var revalidatedRole = await _repository.FindRoleByNormalizedNameAsync(
            roleName.NormalizedValue,
            cancellationToken);

        if (role.IsBuiltIn)
        {
            _ = TranslateDomain(() => role.Rename(expectedRevision, roleName.Value, now));
        }

        TranslateDomain(() => _policy.EnsureRoleNameAvailable(revalidatedRole, role.Id));
        var changed = TranslateDomain(() => role.Rename(expectedRevision, roleName.Value, now));

        if (changed)
        {
            await AddAuditAsync(
                AdminAccessAuditAction.RoleRenamed,
                now,
                actorId,
                roleId: role.Id,
                targetRevision: role.Revision,
                cancellationToken: cancellationToken);
        }

        var administratorCount = await _repository.CountAdministratorsInRoleAsync(
            role.Id,
            cancellationToken);
        var result = MapRole(role, registeredPermissionNames, administratorCount);
        await unitOfWork.CompleteAsync(cancellationToken);
        return result;
    }

    public async Task<AdminRoleDto> SetAdminRoleActiveStateAsync(
        Guid roleId,
        long expectedRevision,
        SetAdminRoleActiveStateRequest request,
        CancellationToken cancellationToken = default)
    {
        var actorId = await RequirePermissionAsync(AdminAccessPermissions.RolesManage, cancellationToken);
        RequireIdentifier(roleId, nameof(roleId));
        ValidateExpectedRevision(expectedRevision);
        RequireRequest(request);
        var registeredPermissionNames = GetRegisteredPermissionNames();
        var now = GetUtcNow();

        await using var unitOfWork = await BeginUnitOfWorkAsync(cancellationToken);
        var role = await GetRoleForUpdateAsync(roleId, lockPermissions: false, cancellationToken);
        var changed = TranslateDomain(() => role.SetActiveState(
            expectedRevision,
            request.IsActive,
            now));
        if (changed)
        {
            await AddAuditAsync(
                request.IsActive
                    ? AdminAccessAuditAction.RoleActivated
                    : AdminAccessAuditAction.RoleDeactivated,
                now,
                actorId,
                roleId: role.Id,
                targetRevision: role.Revision,
                cancellationToken: cancellationToken);
        }

        var administratorCount = await _repository.CountAdministratorsInRoleAsync(
            role.Id,
            cancellationToken);
        var result = MapRole(role, registeredPermissionNames, administratorCount);
        await unitOfWork.CompleteAsync(cancellationToken);
        return result;
    }

    public async Task<AdminRoleDto> ReplaceAdminRolePermissionsAsync(
        Guid roleId,
        long expectedRevision,
        ReplaceAdminRolePermissionsRequest request,
        CancellationToken cancellationToken = default)
    {
        var actorId = await RequirePermissionAsync(AdminAccessPermissions.RolesManage, cancellationToken);
        RequireIdentifier(roleId, nameof(roleId));
        ValidateExpectedRevision(expectedRevision);
        RequireRequest(request);
        var requestedPermissionNames = ValidatePermissionNames(request.PermissionNames);
        var registeredPermissionNames = GetRegisteredPermissionNames();
        var now = GetUtcNow();

        await using var unitOfWork = await BeginUnitOfWorkAsync(cancellationToken);
        var role = await GetRoleForUpdateAsync(roleId, lockPermissions: true, cancellationToken);
        var changeSet = TranslateDomain(() => _policy.ReplaceRolePermissions(
            role,
            expectedRevision,
            requestedPermissionNames,
            registeredPermissionNames,
            actorId,
            now,
            _guidGenerator.Create));
        foreach (var permission in changeSet.Added.OrderBy(permission => permission.Name, StringComparer.Ordinal))
        {
            await AddAuditAsync(
                AdminAccessAuditAction.PermissionGranted,
                now,
                actorId,
                roleId: role.Id,
                permissionName: permission.Name,
                targetRevision: role.Revision,
                cancellationToken: cancellationToken);
        }

        foreach (var permission in changeSet.Removed.OrderBy(permission => permission.Name, StringComparer.Ordinal))
        {
            await AddAuditAsync(
                AdminAccessAuditAction.PermissionRevoked,
                now,
                actorId,
                roleId: role.Id,
                permissionName: permission.Name,
                targetRevision: role.Revision,
                cancellationToken: cancellationToken);
        }

        var administratorCount = await _repository.CountAdministratorsInRoleAsync(
            role.Id,
            cancellationToken);
        var result = MapRole(role, registeredPermissionNames, administratorCount);
        await unitOfWork.CompleteAsync(cancellationToken);
        return result;
    }

    public async Task<IReadOnlyList<PermissionGroupDto>> GetPermissionCatalogAsync(
        CancellationToken cancellationToken = default)
    {
        await RequirePermissionAsync(AdminAccessPermissions.RolesRead, cancellationToken);
        return GetPermissionCatalog();
    }

    public async Task<AdminAccessPageDto<AdminSessionFamilyDto>> ListSessionFamiliesAsync(
        ListAdminSessionFamiliesRequest request,
        CancellationToken cancellationToken = default)
    {
        await RequirePermissionAsync(AdminAccessPermissions.SessionsRead, cancellationToken);
        var paging = ValidatePaging(request?.Page, request?.PageSize);
        ValidateOptionalIdentifier(request!.AdministratorId, nameof(request.AdministratorId));
        ValidateOptionalEnum(request.State, nameof(request.State));
        var state = request.State.HasValue
            ? (AdminSessionFamilyStateSnapshot?)(int)request.State.Value
            : null;
        var now = GetUtcNow();

        await using var unitOfWork = await BeginUnitOfWorkAsync(cancellationToken);
        var page = await _repository.GetSessionFamilyPageAsync(
            request.AdministratorId,
            state,
            now,
            paging.SkipCount,
            paging.PageSize,
            cancellationToken);
        var result = new AdminAccessPageDto<AdminSessionFamilyDto>(
            page.Items.Select(MapSessionFamily).ToArray(),
            page.TotalCount,
            paging.Page,
            paging.PageSize);
        await unitOfWork.CompleteAsync(cancellationToken);
        return result;
    }

    public async Task<AdminSessionFamilyDto> RevokeSessionFamilyAsync(
        Guid familyId,
        CancellationToken cancellationToken = default)
    {
        var actorId = await RequirePermissionAsync(AdminAccessPermissions.SessionsRevoke, cancellationToken);
        RequireIdentifier(familyId, nameof(familyId));
        var now = GetUtcNow();

        await using var unitOfWork = await BeginUnitOfWorkAsync(cancellationToken);
        var ownerIds = await _repository.GetSessionFamilyOwnerIdsAsync(familyId, cancellationToken);
        if (ownerIds.Count == 0)
        {
            throw Failure(
                AdminAccessManagementErrorCodes.SessionFamilyNotFound,
                "The refresh-token family was not found.");
        }

        if (ownerIds.Count != 1 || ownerIds[0] == Guid.Empty)
        {
            throw InvariantViolation("The refresh-token family has malformed owner state.");
        }

        var ownerId = ownerIds[0];
        var owner = await _repository.FindAdministratorForUpdateAsync(ownerId, cancellationToken) ??
            throw InvariantViolation("The refresh-token family owner is missing.");
        var sessions = await _repository.GetSessionFamilyForUpdateAsync(familyId, cancellationToken);
        var identity = TranslateDomain(() =>
            _refreshTokenFamilyPolicy.RequireValidIdentity(sessions));
        if (identity.FamilyId != familyId || identity.AdministratorId != ownerId)
        {
            throw InvariantViolation("The locked refresh-token family does not match its owner probe.");
        }

        var transition = TranslateDomain(() => _refreshTokenFamilyPolicy.Revoke(
            sessions,
            RefreshTokenRevocationReason.ForcedByAdministrator,
            now,
            _requestAuditContextAccessor.Current.IpAddress));
        if (transition.FamilyId != identity.FamilyId ||
            transition.AdministratorId != identity.AdministratorId)
        {
            throw InvariantViolation("The locked refresh-token family does not match its owner probe.");
        }

        if (transition.Changed)
        {
            await AddAuditAsync(
                AdminAccessAuditAction.SessionFamilyRevoked,
                now,
                actorId,
                administratorId: ownerId,
                sessionFamilyId: familyId,
                subjectUserName: owner.UserName,
                cancellationToken: cancellationToken);
        }

        var snapshot = TranslateDomain(() => _refreshTokenFamilyPolicy.CreateSnapshot(
            sessions,
            owner.UserName,
            owner.DisplayName,
            now));
        var result = MapSessionFamily(snapshot);
        await unitOfWork.CompleteAsync(cancellationToken);
        return result;
    }

    public async Task<AdminAccessPageDto<AdminAccessAuditRecordDto>> ListAuditRecordsAsync(
        ListAdminAccessAuditRecordsRequest request,
        CancellationToken cancellationToken = default)
    {
        await RequirePermissionAsync(AdminAccessPermissions.AuditLogsRead, cancellationToken);
        var paging = ValidatePaging(request?.Page, request?.PageSize);
        var query = ValidateAuditQuery(request!);

        await using var unitOfWork = await BeginUnitOfWorkAsync(cancellationToken);
        var page = await _repository.GetAuditPageAsync(
            query,
            paging.SkipCount,
            paging.PageSize,
            cancellationToken);
        var result = new AdminAccessPageDto<AdminAccessAuditRecordDto>(
            page.Items.Select(MapAuditRecord).ToArray(),
            page.TotalCount,
            paging.Page,
            paging.PageSize);
        await unitOfWork.CompleteAsync(cancellationToken);
        return result;
    }

    private async Task<Guid> RequirePermissionAsync(
        string leafPermission,
        CancellationToken cancellationToken)
    {
        if (!_currentUser.IsAuthenticated ||
            !AdministratorIdentity.TryGetId(_currentUser, out var administratorId))
        {
            throw Failure(AdminAccessErrorCodes.Unauthorized, "Authentication is required.");
        }

        if (!await _permissionChecker.IsGrantedAsync(
                AdminAccessPermissions.Access,
                cancellationToken) ||
            !await _permissionChecker.IsGrantedAsync(leafPermission, cancellationToken))
        {
            throw Failure(
                AdminAccessErrorCodes.Forbidden,
                "The current administrator does not have permission to perform this action.");
        }

        return administratorId;
    }

    private Task<IUnitOfWorkScope> BeginUnitOfWorkAsync(CancellationToken cancellationToken)
    {
        return _unitOfWorkManager.BeginAsync(cancellationToken: cancellationToken);
    }

    private async Task<ReceiptClaim> ClaimReceiptAsync(
        AdminAccessCommandReceipt candidate,
        CancellationToken cancellationToken)
    {
        var stored = await _receiptRepository.ClaimAsync(candidate, cancellationToken);
        if (stored.ActorAdministratorId != candidate.ActorAdministratorId ||
            stored.CommandKind != candidate.CommandKind ||
            stored.IdempotencyKey != candidate.IdempotencyKey)
        {
            throw InvariantViolation("The command-receipt claim returned a mismatched scope.");
        }

        if (stored.Id == candidate.Id)
        {
            if (!stored.MatchesFingerprint(candidate.RequestFingerprint) ||
                stored.TargetResourceId != candidate.TargetResourceId ||
                stored.ResultingRevision != candidate.ResultingRevision)
            {
                throw InvariantViolation("The new command-receipt claim is malformed.");
            }

            return new ReceiptClaim(true, stored);
        }

        if (!stored.MatchesFingerprint(candidate.RequestFingerprint))
        {
            throw Failure(
                AdminAccessManagementErrorCodes.IdempotencyConflict,
                "The idempotency key was already used for different non-secret intent.");
        }

        return new ReceiptClaim(false, stored);
    }

    private async Task<AdministratorDto> GetAdministratorReplayAsync(
        AdminAccessCommandReceipt receipt,
        CancellationToken cancellationToken)
    {
        var snapshot = await _repository.FindAdministratorSnapshotAsync(
            receipt.TargetResourceId,
            cancellationToken) ?? throw InvariantViolation(
                "The accepted command receipt references a missing administrator.");
        return MapAdministrator(snapshot);
    }

    private async Task<AdminRoleDto> GetRoleReplayAsync(
        AdminAccessCommandReceipt receipt,
        IReadOnlyCollection<string> registeredPermissionNames,
        CancellationToken cancellationToken)
    {
        var snapshot = await _repository.FindRoleSnapshotAsync(
            receipt.TargetResourceId,
            registeredPermissionNames,
            cancellationToken) ?? throw InvariantViolation(
                "The accepted command receipt references a missing role.");
        return MapRole(snapshot);
    }

    private async Task<Administrator> GetAdministratorForUpdateAsync(
        Guid administratorId,
        bool lockMemberships,
        CancellationToken cancellationToken)
    {
        var administrator = await _repository.FindAdministratorForUpdateAsync(
            administratorId,
            cancellationToken) ?? throw Failure(
                AdminAccessManagementErrorCodes.AdministratorNotFound,
                "The administrator was not found.");
        if (lockMemberships)
        {
            var memberships = await _repository.GetAdministratorRoleMembershipsForUpdateAsync(
                administratorId,
                cancellationToken);
            EnsureMembershipRowsMatch(administrator, memberships);
        }

        return administrator;
    }

    private async Task<AdminRole> GetRoleForUpdateAsync(
        Guid roleId,
        bool lockPermissions,
        CancellationToken cancellationToken)
    {
        var role = await _repository.FindRoleForUpdateAsync(roleId, cancellationToken) ??
            throw Failure(AdminAccessManagementErrorCodes.RoleNotFound, "The role was not found.");
        if (lockPermissions)
        {
            var permissions = await _repository.GetRolePermissionsForUpdateAsync(roleId, cancellationToken);
            EnsurePermissionRowsMatch(role, permissions);
        }

        return role;
    }

    private async Task<AssignedRoleSet> GetAssignedRolesAsync(
        Administrator administrator,
        bool membershipsLocked,
        CancellationToken cancellationToken)
    {
        var roleIds = membershipsLocked
            ? administrator.RoleMemberships
                .Select(membership => membership.RoleId)
                .Order()
                .ToArray()
            : (await _repository.GetAdministratorRoleIdsAsync(
                    administrator.Id,
                    cancellationToken))
                .Order()
                .ToArray();
        var roles = await _repository.GetRolesAsync(roleIds, cancellationToken);
        EnsureRoleSetMatches(roleIds, roles);
        return new AssignedRoleSet(roleIds, roles);
    }

    private async Task<IReadOnlyList<LockedSessionFamily>> LockAdministratorFamiliesAsync(
        Guid administratorId,
        CancellationToken cancellationToken)
    {
        var familyIds = await _repository.GetAdministratorSessionFamilyIdsAsync(
            administratorId,
            cancellationToken);
        if (familyIds.Any(familyId => familyId == Guid.Empty) ||
            familyIds.Distinct().Count() != familyIds.Count)
        {
            throw InvariantViolation("The administrator refresh-token family identifiers are malformed.");
        }

        var lockedFamilies = new List<LockedSessionFamily>(familyIds.Count);
        foreach (var familyId in familyIds.Order())
        {
            var sessions = await _repository.GetSessionFamilyForUpdateAsync(
                familyId,
                cancellationToken);
            var identity = TranslateDomain(() =>
                _refreshTokenFamilyPolicy.RequireValidIdentity(sessions));
            if (identity.FamilyId != familyId || identity.AdministratorId != administratorId)
            {
                throw InvariantViolation(
                    "A locked refresh-token family does not match the administrator probe.");
            }

            lockedFamilies.Add(new LockedSessionFamily(familyId, sessions));
        }

        return lockedFamilies.AsReadOnly();
    }

    private async Task RevokeFamiliesAsync(
        IReadOnlyCollection<LockedSessionFamily> families,
        Administrator administrator,
        RefreshTokenRevocationReason reason,
        DateTime now,
        Guid actorId,
        CancellationToken cancellationToken)
    {
        foreach (var family in families.OrderBy(family => family.FamilyId))
        {
            var transition = TranslateDomain(() => _refreshTokenFamilyPolicy.Revoke(
                family.Sessions,
                reason,
                now,
                _requestAuditContextAccessor.Current.IpAddress));
            if (transition.AdministratorId != administrator.Id)
            {
                throw InvariantViolation("A locked refresh-token family has the wrong owner.");
            }

            if (transition.FamilyId != family.FamilyId)
            {
                throw InvariantViolation("A locked refresh-token family has the wrong identifier.");
            }

            if (transition.Changed)
            {
                await AddAuditAsync(
                    AdminAccessAuditAction.SessionFamilyRevoked,
                    now,
                    actorId,
                    administratorId: administrator.Id,
                    sessionFamilyId: transition.FamilyId,
                    subjectUserName: administrator.UserName,
                    cancellationToken: cancellationToken);
            }
        }
    }

    private Task AddAuditAsync(
        AdminAccessAuditAction action,
        DateTime occurredAtUtc,
        Guid actorAdministratorId,
        Guid? administratorId = null,
        Guid? roleId = null,
        Guid? sessionFamilyId = null,
        string? subjectUserName = null,
        string? permissionName = null,
        long? targetRevision = null,
        CancellationToken cancellationToken = default)
    {
        var context = _requestAuditContextAccessor.Current;
        return _repository.AddAuditAsync(
            new AdminAccessAuditRecord(
                _guidGenerator.Create(),
                action,
                occurredAtUtc,
                succeeded: true,
                administratorId: administratorId,
                actorAdministratorId: actorAdministratorId,
                roleId: roleId,
                subjectUserName: subjectUserName,
                permissionName: permissionName,
                ipAddress: context.IpAddress,
                userAgent: context.UserAgent,
                correlationId: context.CorrelationId,
                sessionFamilyId: sessionFamilyId,
                targetRevision: targetRevision),
            cancellationToken);
    }

    private IReadOnlyList<string> GetRegisteredPermissionNames()
    {
        var names = _permissionDefinitionManager.Groups
            .SelectMany(group => group.Permissions)
            .Select(permission => permission.Name)
            .ToArray();
        if (names.Any(name =>
                string.IsNullOrEmpty(name) ||
                name.Length > AdminAccessManagementContractLimits.PermissionNameMaximumLength ||
                name.Any(char.IsControl)) ||
            names.Distinct(StringComparer.Ordinal).Count() != names.Length)
        {
            throw InvariantViolation("The registered permission catalog is malformed or duplicated.");
        }

        return Array.AsReadOnly(names.Order(StringComparer.Ordinal).ToArray());
    }

    private IReadOnlyList<PermissionGroupDto> GetPermissionCatalog()
    {
        _ = GetRegisteredPermissionNames();
        return _permissionDefinitionManager.Groups
            .OrderBy(group => group.Name, StringComparer.Ordinal)
            .Select(group => new PermissionGroupDto(
                group.Name,
                group.DisplayName,
                group.Permissions
                    .OrderBy(permission => permission.Name, StringComparer.Ordinal)
                    .Select(permission => new PermissionDefinitionDto(
                        permission.Name,
                        permission.DisplayName,
                        permission.Description))
                    .ToArray()))
            .ToArray();
    }

    private static AdministratorDto MapAdministrator(AdministratorManagementSnapshot snapshot)
    {
        return new AdministratorDto(
            snapshot.Id,
            snapshot.UserName,
            snapshot.DisplayName,
            snapshot.IsActive,
            snapshot.Roles
                .OrderBy(role => role.Name, StringComparer.Ordinal)
                .ThenBy(role => role.Id)
                .Select(role => new AdminRoleReferenceDto(
                    role.Id,
                    role.Name,
                    role.IsBuiltIn,
                    role.IsActive))
                .ToArray(),
            snapshot.CreatedAtUtc,
            snapshot.UpdatedAtUtc,
            snapshot.LastLoginAtUtc,
            snapshot.Revision);
    }

    private static AdministratorDto MapAdministrator(
        Administrator administrator,
        IReadOnlyCollection<AdminRole> roles,
        IReadOnlyCollection<Guid>? assignedRoleIds = null)
    {
        var roleById = roles.ToDictionary(role => role.Id);
        var roleReferences = new List<AdminRoleReferenceDto>();
        var roleIds = assignedRoleIds ?? administrator.RoleMemberships
            .Select(membership => membership.RoleId)
            .ToArray();
        foreach (var roleId in roleIds.Order())
        {
            if (!roleById.TryGetValue(roleId, out var role))
            {
                throw InvariantViolation("An administrator membership references a missing locked role.");
            }

            roleReferences.Add(new AdminRoleReferenceDto(
                role.Id,
                role.Name,
                role.IsBuiltIn,
                role.IsActive));
        }

        return new AdministratorDto(
            administrator.Id,
            administrator.UserName,
            administrator.DisplayName,
            administrator.IsActive,
            roleReferences
                .OrderBy(role => role.Name, StringComparer.Ordinal)
                .ThenBy(role => role.Id)
                .ToArray(),
            administrator.CreatedAtUtc,
            administrator.UpdatedAtUtc,
            administrator.LastLoginAtUtc,
            administrator.Revision);
    }

    private static AdminRoleSummaryDto MapRoleSummary(AdminRoleManagementSnapshot snapshot)
    {
        return new AdminRoleSummaryDto(
            snapshot.Id,
            snapshot.Name,
            snapshot.IsBuiltIn,
            snapshot.IsActive,
            snapshot.PermissionNames.Count,
            snapshot.AdministratorCount,
            snapshot.CreatedAtUtc,
            snapshot.UpdatedAtUtc,
            snapshot.Revision);
    }

    private static AdminRoleDto MapRole(AdminRoleManagementSnapshot snapshot)
    {
        var permissionNames = snapshot.PermissionNames.Order(StringComparer.Ordinal).ToArray();
        return new AdminRoleDto(
            snapshot.Id,
            snapshot.Name,
            snapshot.IsBuiltIn,
            snapshot.IsActive,
            permissionNames.Length,
            snapshot.AdministratorCount,
            snapshot.CreatedAtUtc,
            snapshot.UpdatedAtUtc,
            snapshot.Revision,
            permissionNames);
    }

    private static AdminRoleDto MapRole(
        AdminRole role,
        IReadOnlyCollection<string> registeredPermissionNames,
        long administratorCount)
    {
        var registered = registeredPermissionNames.ToHashSet(StringComparer.Ordinal);
        var permissionNames = role.Permissions
            .Select(permission => permission.Name)
            .Where(registered.Contains)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        return new AdminRoleDto(
            role.Id,
            role.Name,
            role.IsBuiltIn,
            role.IsActive,
            permissionNames.Length,
            administratorCount,
            role.CreatedAtUtc,
            role.UpdatedAtUtc,
            role.Revision,
            permissionNames);
    }

    private static AdminSessionFamilyDto MapSessionFamily(AdminSessionFamilySnapshot snapshot)
    {
        return new AdminSessionFamilyDto(
            snapshot.FamilyId,
            snapshot.AdministratorId,
            snapshot.UserName,
            snapshot.DisplayName,
            snapshot.FirstCreatedAtUtc,
            snapshot.LastActivityAtUtc,
            snapshot.ExpiresAtUtc,
            snapshot.LastIpAddress,
            snapshot.State switch
            {
                AdminSessionFamilyStateSnapshot.Active => AdminSessionFamilyState.Active,
                AdminSessionFamilyStateSnapshot.Expired => AdminSessionFamilyState.Expired,
                AdminSessionFamilyStateSnapshot.Revoked => AdminSessionFamilyState.Revoked,
                _ => throw InvariantViolation("The session-family state is invalid.")
            },
            snapshot.RevokedAtUtc,
            MapRevocationReason(snapshot.RevocationReason));
    }

    private static AdminSessionRevocationReason? MapRevocationReason(
        RefreshTokenRevocationReason? reason)
    {
        return reason switch
        {
            null => null,
            RefreshTokenRevocationReason.Rotated => AdminSessionRevocationReason.Rotated,
            RefreshTokenRevocationReason.Logout => AdminSessionRevocationReason.Logout,
            RefreshTokenRevocationReason.ReuseDetected => AdminSessionRevocationReason.ReuseDetected,
            RefreshTokenRevocationReason.Expired => AdminSessionRevocationReason.Expired,
            RefreshTokenRevocationReason.AdministratorInactive =>
                AdminSessionRevocationReason.AdministratorInactive,
            RefreshTokenRevocationReason.PasswordReset => AdminSessionRevocationReason.PasswordReset,
            RefreshTokenRevocationReason.ForcedByAdministrator =>
                AdminSessionRevocationReason.ForcedByAdministrator,
            _ => throw InvariantViolation("The session revocation reason is invalid.")
        };
    }

    private static AdminAccessAuditRecordDto MapAuditRecord(AdminAccessAuditRecordSnapshot snapshot)
    {
        return new AdminAccessAuditRecordDto(
            snapshot.Id,
            MapAuditAction(snapshot.Action),
            snapshot.OccurredAtUtc,
            snapshot.Succeeded,
            snapshot.SubjectAdministratorId,
            snapshot.ActorAdministratorId,
            snapshot.RoleId,
            snapshot.SessionFamilyId,
            snapshot.SubjectUserName,
            snapshot.PermissionName,
            snapshot.ErrorCode,
            snapshot.IpAddress,
            snapshot.UserAgent,
            snapshot.CorrelationId,
            snapshot.TargetRevision);
    }

    private static AdminAccessAuditActionDto MapAuditAction(AdminAccessAuditAction action)
    {
        return action switch
        {
            AdminAccessAuditAction.Login => AdminAccessAuditActionDto.Login,
            AdminAccessAuditAction.Refresh => AdminAccessAuditActionDto.Refresh,
            AdminAccessAuditAction.Logout => AdminAccessAuditActionDto.Logout,
            AdminAccessAuditAction.RefreshTokenReuseDetected =>
                AdminAccessAuditActionDto.RefreshTokenReuseDetected,
            AdminAccessAuditAction.AdministratorCreated => AdminAccessAuditActionDto.AdministratorCreated,
            AdminAccessAuditAction.RoleCreated => AdminAccessAuditActionDto.RoleCreated,
            AdminAccessAuditAction.RoleAssigned => AdminAccessAuditActionDto.RoleAssigned,
            AdminAccessAuditAction.PermissionGranted => AdminAccessAuditActionDto.PermissionGranted,
            AdminAccessAuditAction.PermissionRevoked => AdminAccessAuditActionDto.PermissionRevoked,
            AdminAccessAuditAction.PasswordRehashed => AdminAccessAuditActionDto.PasswordRehashed,
            AdminAccessAuditAction.AdministratorDisplayNameChanged =>
                AdminAccessAuditActionDto.AdministratorDisplayNameChanged,
            AdminAccessAuditAction.AdministratorActivated => AdminAccessAuditActionDto.AdministratorActivated,
            AdminAccessAuditAction.AdministratorDeactivated =>
                AdminAccessAuditActionDto.AdministratorDeactivated,
            AdminAccessAuditAction.AdministratorPasswordReset =>
                AdminAccessAuditActionDto.AdministratorPasswordReset,
            AdminAccessAuditAction.RoleRemoved => AdminAccessAuditActionDto.RoleRemoved,
            AdminAccessAuditAction.RoleRenamed => AdminAccessAuditActionDto.RoleRenamed,
            AdminAccessAuditAction.RoleActivated => AdminAccessAuditActionDto.RoleActivated,
            AdminAccessAuditAction.RoleDeactivated => AdminAccessAuditActionDto.RoleDeactivated,
            AdminAccessAuditAction.SessionFamilyRevoked => AdminAccessAuditActionDto.SessionFamilyRevoked,
            _ => throw InvariantViolation("The audit action is invalid.")
        };
    }

    private static AdminAccessAuditQuery ValidateAuditQuery(
        ListAdminAccessAuditRecordsRequest request)
    {
        if (request.FromUtc.Kind != DateTimeKind.Utc || request.ToUtc.Kind != DateTimeKind.Utc ||
            request.FromUtc >= request.ToUtc || request.ToUtc - request.FromUtc > MaximumAuditRange)
        {
            throw InvalidRequest(
                "Audit timestamps must define a UTC half-open range no longer than 31 days.");
        }

        ValidateOptionalEnum(request.Action, nameof(request.Action));
        ValidateOptionalIdentifier(request.ActorAdministratorId, nameof(request.ActorAdministratorId));
        ValidateOptionalIdentifier(request.SubjectAdministratorId, nameof(request.SubjectAdministratorId));
        ValidateOptionalIdentifier(request.RoleId, nameof(request.RoleId));
        var correlationId = NormalizeCorrelationId(request.CorrelationId);
        return new AdminAccessAuditQuery(
            request.FromUtc,
            request.ToUtc,
            request.Action.HasValue ? MapAuditAction(request.Action.Value) : null,
            request.Succeeded,
            request.ActorAdministratorId,
            request.SubjectAdministratorId,
            request.RoleId,
            correlationId);
    }

    private static AdminAccessAuditAction MapAuditAction(AdminAccessAuditActionDto action)
    {
        return action switch
        {
            AdminAccessAuditActionDto.Login => AdminAccessAuditAction.Login,
            AdminAccessAuditActionDto.Refresh => AdminAccessAuditAction.Refresh,
            AdminAccessAuditActionDto.Logout => AdminAccessAuditAction.Logout,
            AdminAccessAuditActionDto.RefreshTokenReuseDetected =>
                AdminAccessAuditAction.RefreshTokenReuseDetected,
            AdminAccessAuditActionDto.AdministratorCreated => AdminAccessAuditAction.AdministratorCreated,
            AdminAccessAuditActionDto.RoleCreated => AdminAccessAuditAction.RoleCreated,
            AdminAccessAuditActionDto.RoleAssigned => AdminAccessAuditAction.RoleAssigned,
            AdminAccessAuditActionDto.PermissionGranted => AdminAccessAuditAction.PermissionGranted,
            AdminAccessAuditActionDto.PermissionRevoked => AdminAccessAuditAction.PermissionRevoked,
            AdminAccessAuditActionDto.PasswordRehashed => AdminAccessAuditAction.PasswordRehashed,
            AdminAccessAuditActionDto.AdministratorDisplayNameChanged =>
                AdminAccessAuditAction.AdministratorDisplayNameChanged,
            AdminAccessAuditActionDto.AdministratorActivated => AdminAccessAuditAction.AdministratorActivated,
            AdminAccessAuditActionDto.AdministratorDeactivated =>
                AdminAccessAuditAction.AdministratorDeactivated,
            AdminAccessAuditActionDto.AdministratorPasswordReset =>
                AdminAccessAuditAction.AdministratorPasswordReset,
            AdminAccessAuditActionDto.RoleRemoved => AdminAccessAuditAction.RoleRemoved,
            AdminAccessAuditActionDto.RoleRenamed => AdminAccessAuditAction.RoleRenamed,
            AdminAccessAuditActionDto.RoleActivated => AdminAccessAuditAction.RoleActivated,
            AdminAccessAuditActionDto.RoleDeactivated => AdminAccessAuditAction.RoleDeactivated,
            AdminAccessAuditActionDto.SessionFamilyRevoked => AdminAccessAuditAction.SessionFamilyRevoked,
            _ => throw InvalidRequest("The audit action is invalid.")
        };
    }

    private static Paging ValidatePaging(int? page, int? pageSize)
    {
        if (page is null || pageSize is null || page <= 0 ||
            pageSize <= 0 || pageSize > AdminAccessManagementContractLimits.MaximumPageSize)
        {
            throw InvalidRequest("Page and page size are outside the permitted range.");
        }

        try
        {
            return new Paging(
                page.Value,
                pageSize.Value,
                checked((page.Value - 1) * pageSize.Value));
        }
        catch (OverflowException)
        {
            throw InvalidRequest("The requested page offset is too large.");
        }
    }

    private static string? NormalizeSearch(string? value, bool normalizeForIdentity)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string normalized;
        try
        {
            normalized = value.Normalize(NormalizationForm.FormKC).Trim();
        }
        catch (ArgumentException)
        {
            throw InvalidRequest("Search text must contain valid Unicode text.");
        }

        if (normalized.Length > AdminAccessManagementContractLimits.SearchMaximumLength ||
            normalized.Any(char.IsControl))
        {
            throw InvalidRequest("Search text is invalid or too long.");
        }

        if (normalizeForIdentity)
        {
            normalized = normalized.ToUpperInvariant();
            if (normalized.Length > AdminAccessManagementContractLimits.SearchMaximumLength)
            {
                throw InvalidRequest("Normalized search text is too long.");
            }
        }

        return normalized;
    }

    private static IReadOnlyList<Guid> ValidateRoleIds(IReadOnlyCollection<Guid> roleIds)
    {
        if (roleIds is null ||
            roleIds.Count > AdminAccessManagementContractLimits.MaximumRoleIds ||
            roleIds.Any(roleId => roleId == Guid.Empty) ||
            roleIds.Distinct().Count() != roleIds.Count)
        {
            throw InvalidRequest("Role identifiers must be non-empty, unique, and within the allowed limit.");
        }

        return Array.AsReadOnly(roleIds.Order().ToArray());
    }

    private static IReadOnlyList<string> ValidatePermissionNames(
        IReadOnlyCollection<string> permissionNames)
    {
        if (permissionNames is null ||
            permissionNames.Count > AdminAccessManagementContractLimits.MaximumPermissionNames ||
            permissionNames.Any(name =>
                string.IsNullOrWhiteSpace(name) ||
                name.Length > AdminAccessManagementContractLimits.PermissionNameMaximumLength ||
                name.Any(char.IsControl)) ||
            permissionNames.Distinct(StringComparer.Ordinal).Count() != permissionNames.Count)
        {
            throw InvalidRequest(
                "Permission names must be non-empty, unique, case-sensitive, and within the allowed limits.");
        }

        return Array.AsReadOnly(permissionNames.Order(StringComparer.Ordinal).ToArray());
    }

    private static void ValidatePassword(string value, string parameterName)
    {
        if (value is null || value.Length is < AdminAccessManagementContractLimits.PasswordMinimumLength or
            > AdminAccessManagementContractLimits.PasswordMaximumLength)
        {
            throw InvalidRequest(
                $"{parameterName} must contain 12 to 256 characters.");
        }
    }

    private static string? NormalizeCorrelationId(string? value)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrEmpty(normalized))
        {
            return null;
        }

        if (normalized.Length > AdminAccessManagementContractLimits.CorrelationIdMaximumLength ||
            normalized.Any(char.IsControl))
        {
            throw InvalidRequest("The correlation identifier is invalid or too long.");
        }

        return normalized;
    }

    private static void ValidateExpectedRevision(long expectedRevision)
    {
        if (expectedRevision <= 0)
        {
            throw InvalidRequest("Expected revisions must be positive.");
        }
    }

    private static long GetNextRevision(long expectedRevision)
    {
        try
        {
            return checked(expectedRevision + 1);
        }
        catch (OverflowException)
        {
            throw InvalidRequest("The expected revision cannot be advanced.");
        }
    }

    private DateTime GetUtcNow()
    {
        var now = _clock.UtcNow;
        if (now.Kind != DateTimeKind.Utc)
        {
            throw InvariantViolation("The configured clock did not return UTC.");
        }

        return new DateTime(now.Ticks - (now.Ticks % 10), DateTimeKind.Utc);
    }

    private static void RequireRequest<T>(T? request)
        where T : class
    {
        if (request is null)
        {
            throw InvalidRequest("A request body is required.");
        }
    }

    private static void RequireIdentifier(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw InvalidRequest($"{parameterName} cannot be empty.");
        }
    }

    private static void ValidateOptionalIdentifier(Guid? value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw InvalidRequest($"{parameterName} cannot be empty when present.");
        }
    }

    private static void ValidateOptionalEnum<TEnum>(TEnum? value, string parameterName)
        where TEnum : struct, Enum
    {
        if (value.HasValue && !Enum.IsDefined(value.Value))
        {
            throw InvalidRequest($"{parameterName} is invalid.");
        }
    }

    private static void EnsureMembershipProbeMatches(
        IReadOnlyCollection<Guid> probedRoleIds,
        IReadOnlyCollection<AdministratorRole> memberships)
    {
        var probed = probedRoleIds.Order().ToArray();
        var locked = memberships.Select(membership => membership.RoleId).Order().ToArray();
        if (!probed.SequenceEqual(locked))
        {
            throw InvariantViolation("Administrator memberships changed between probe and ordered locks.");
        }
    }

    private static void EnsureMembershipRowsMatch(
        Administrator administrator,
        IReadOnlyCollection<AdministratorRole> memberships)
    {
        if (memberships.Any(membership => membership.AdministratorId != administrator.Id) ||
            memberships.Select(membership => membership.Id).Distinct().Count() != memberships.Count)
        {
            throw InvariantViolation("The locked administrator membership rows are malformed.");
        }

        var aggregateIds = administrator.RoleMemberships.Select(membership => membership.Id).Order().ToArray();
        var lockedIds = memberships.Select(membership => membership.Id).Order().ToArray();
        if (!aggregateIds.SequenceEqual(lockedIds))
        {
            throw InvariantViolation("The locked membership rows do not match the administrator aggregate.");
        }
    }

    private static void EnsurePermissionRowsMatch(
        AdminRole role,
        IReadOnlyCollection<RolePermission> permissions)
    {
        if (permissions.Any(permission => permission.RoleId != role.Id) ||
            permissions.Select(permission => permission.Id).Distinct().Count() != permissions.Count)
        {
            throw InvariantViolation("The locked role-permission rows are malformed.");
        }

        var aggregateIds = role.Permissions.Select(permission => permission.Id).Order().ToArray();
        var lockedIds = permissions.Select(permission => permission.Id).Order().ToArray();
        if (!aggregateIds.SequenceEqual(lockedIds))
        {
            throw InvariantViolation("The locked permission rows do not match the role aggregate.");
        }
    }

    private static void EnsureRoleSetMatches(
        IReadOnlyCollection<Guid> roleIds,
        IReadOnlyCollection<AdminRole> roles)
    {
        var expected = roleIds.Order().ToArray();
        var actual = roles.Select(role => role.Id).Distinct().Order().ToArray();
        if (!expected.SequenceEqual(actual) || actual.Length != roles.Count)
        {
            throw InvariantViolation("The requested role-reference set is incomplete or duplicated.");
        }
    }

    private static T TranslateDomain<T>(Func<T> action)
    {
        try
        {
            return action();
        }
        catch (AdminAccessManagementDomainException exception)
        {
            throw Failure(exception.Code, exception.Message);
        }
        catch (ArgumentException exception)
        {
            throw InvalidRequest(exception.Message);
        }
        catch (OverflowException)
        {
            throw InvariantViolation("An administrator-access revision overflowed.");
        }
    }

    private static void TranslateDomain(Action action)
    {
        _ = TranslateDomain(() =>
        {
            action();
            return true;
        });
    }

    private static T TranslateValidation<T>(Func<T> action)
    {
        try
        {
            return action();
        }
        catch (ArgumentException exception)
        {
            throw InvalidRequest(exception.Message);
        }
    }

    private static AdminAccessManagementException InvalidRequest(string message)
    {
        return Failure(AdminAccessManagementErrorCodes.InvalidRequest, message);
    }

    private static AdminAccessManagementException InvariantViolation(string message)
    {
        return Failure(AdminAccessManagementErrorCodes.InvariantViolation, message);
    }

    private static AdminAccessManagementException Failure(string code, string message)
    {
        return new AdminAccessManagementException(
            AdminAccessManagementErrorCodes.GetFailureKind(code),
            code,
            message);
    }

    private readonly record struct Paging(int Page, int PageSize, int SkipCount);

    private readonly record struct ReceiptClaim(bool IsNew, AdminAccessCommandReceipt Receipt);

    private sealed record LockedSessionFamily(
        Guid FamilyId,
        IReadOnlyList<RefreshTokenSession> Sessions);

    private readonly record struct AssignedRoleSet(
        IReadOnlyList<Guid> RoleIds,
        IReadOnlyList<AdminRole> Roles);
}
