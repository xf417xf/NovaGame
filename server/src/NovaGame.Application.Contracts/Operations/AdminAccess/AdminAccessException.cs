using NovaCore.BuildingBlocks.Exceptions;

namespace NovaGame.Application.Contracts.Operations.AdminAccess;

public sealed class AdminAccessException : BusinessException
{
    public AdminAccessException(string code, string message)
        : base(code, message)
    {
    }
}
