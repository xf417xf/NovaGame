namespace NovaGame.Domain.Shared.Operations.AdminAccess;

public enum AdminAccessFailureKind
{
    BadRequest,
    Unauthorized,
    Forbidden,
    NotFound,
    Conflict,
    UnprocessableEntity,
    InternalError
}
