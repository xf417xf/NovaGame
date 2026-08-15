using NovaCore.BuildingBlocks.Exceptions;
using NovaGame.Domain.Shared.Operations.AdminAccess;

namespace NovaGame.Domain.Operations.AdminAccess;

public sealed class AdminAccessManagementDomainException : BusinessException
{
    public AdminAccessManagementDomainException(string code, string message)
        : base(code, message)
    {
        FailureKind = AdminAccessManagementErrorCodes.GetFailureKind(code);
    }

    public AdminAccessFailureKind FailureKind { get; }
}
