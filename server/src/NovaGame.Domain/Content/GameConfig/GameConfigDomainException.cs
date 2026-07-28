using NovaCore.BuildingBlocks.Exceptions;

namespace NovaGame.Domain.Content.GameConfig;

public sealed class GameConfigDomainException : BusinessException
{
    public GameConfigDomainException(string code, string message)
        : base(code, message)
    {
    }
}
