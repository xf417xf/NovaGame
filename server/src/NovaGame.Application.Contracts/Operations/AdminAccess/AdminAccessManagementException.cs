using NovaCore.BuildingBlocks.Exceptions;
using NovaGame.Domain.Shared.Operations.AdminAccess;

namespace NovaGame.Application.Contracts.Operations.AdminAccess;

public sealed class AdminAccessManagementException : BusinessException
{
    public AdminAccessManagementException(
        AdminAccessFailureKind failureKind,
        string code,
        string message,
        IReadOnlyDictionary<string, string[]>? details = null)
        : base(code, message)
    {
        FailureKind = failureKind;
        Details = details;
    }

    public AdminAccessFailureKind FailureKind { get; }

    public IReadOnlyDictionary<string, string[]>? Details { get; }
}
