using NovaCore.BuildingBlocks.Exceptions;

namespace SwordMaster.Application.Contracts.PlayerAssets;

public enum PlayerAssetsFailureKind
{
    BadRequest = 1,
    Unauthorized = 2,
    NotFound = 3,
    Conflict = 4,
    UnprocessableEntity = 5,
    InternalError = 6
}

public sealed class PlayerAssetsException : BusinessException
{
    public PlayerAssetsException(
        PlayerAssetsFailureKind failureKind,
        string code,
        string message,
        IReadOnlyDictionary<string, string[]>? details = null)
        : base(code, message)
    {
        FailureKind = failureKind;
        Details = details;
    }

    public PlayerAssetsFailureKind FailureKind { get; }

    public IReadOnlyDictionary<string, string[]>? Details { get; }
}
