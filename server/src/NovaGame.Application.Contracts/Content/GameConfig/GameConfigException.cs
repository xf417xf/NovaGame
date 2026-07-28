using NovaCore.BuildingBlocks.Exceptions;

namespace NovaGame.Application.Contracts.Content.GameConfig;

public enum GameConfigFailureKind
{
    BadRequest = 1,
    Unauthorized = 2,
    Forbidden = 3,
    NotFound = 4,
    Conflict = 5,
    UnprocessableEntity = 6,
    InternalError = 7
}

public sealed class GameConfigException : BusinessException
{
    public GameConfigException(
        GameConfigFailureKind failureKind,
        string code,
        string message,
        IReadOnlyDictionary<string, string[]>? details = null)
        : base(code, message)
    {
        FailureKind = failureKind;
        Details = details;
    }

    public GameConfigFailureKind FailureKind { get; }

    public IReadOnlyDictionary<string, string[]>? Details { get; }
}
