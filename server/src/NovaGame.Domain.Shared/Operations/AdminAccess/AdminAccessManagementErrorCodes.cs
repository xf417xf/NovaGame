namespace NovaGame.Domain.Shared.Operations.AdminAccess;

public static class AdminAccessManagementErrorCodes
{
    private const string Prefix = "NovaGame.AdminAccess.";

    public const string InvalidRequest = Prefix + "InvalidRequest";
    public const string AdministratorNotFound = Prefix + "AdministratorNotFound";
    public const string RoleNotFound = Prefix + "RoleNotFound";
    public const string SessionFamilyNotFound = Prefix + "SessionFamilyNotFound";
    public const string UserNameAlreadyExists = Prefix + "UserNameAlreadyExists";
    public const string RoleNameAlreadyExists = Prefix + "RoleNameAlreadyExists";
    public const string RevisionConflict = Prefix + "RevisionConflict";
    public const string IdempotencyConflict = Prefix + "IdempotencyConflict";
    public const string SelfDeactivationForbidden = Prefix + "SelfDeactivationForbidden";
    public const string SelfRoleMutationForbidden = Prefix + "SelfRoleMutationForbidden";
    public const string SelfPasswordResetForbidden = Prefix + "SelfPasswordResetForbidden";
    public const string LastActiveBuiltInAdministratorRequired =
        Prefix + "LastActiveBuiltInAdministratorRequired";
    public const string BuiltInRoleImmutable = Prefix + "BuiltInRoleImmutable";
    public const string RoleInactive = Prefix + "RoleInactive";
    public const string UnknownPermission = Prefix + "UnknownPermission";
    public const string InvariantViolation = Prefix + "InvariantViolation";

    public static AdminAccessFailureKind GetFailureKind(string code)
    {
        return code switch
        {
            InvalidRequest => AdminAccessFailureKind.BadRequest,
            AdminAccessErrorCodes.Unauthorized => AdminAccessFailureKind.Unauthorized,
            AdminAccessErrorCodes.Forbidden => AdminAccessFailureKind.Forbidden,
            AdministratorNotFound or RoleNotFound or SessionFamilyNotFound =>
                AdminAccessFailureKind.NotFound,
            UserNameAlreadyExists or RoleNameAlreadyExists or RevisionConflict or IdempotencyConflict =>
                AdminAccessFailureKind.Conflict,
            SelfDeactivationForbidden or SelfRoleMutationForbidden or SelfPasswordResetForbidden or
                LastActiveBuiltInAdministratorRequired or BuiltInRoleImmutable or RoleInactive or
                UnknownPermission => AdminAccessFailureKind.UnprocessableEntity,
            InvariantViolation => AdminAccessFailureKind.InternalError,
            _ => throw new ArgumentOutOfRangeException(nameof(code), code, "Unknown administrator-access error code.")
        };
    }
}
