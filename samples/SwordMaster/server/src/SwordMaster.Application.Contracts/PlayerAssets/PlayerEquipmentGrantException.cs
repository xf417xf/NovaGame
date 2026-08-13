using NovaCore.BuildingBlocks.Exceptions;

namespace SwordMaster.Application.Contracts.PlayerAssets;

public enum PlayerEquipmentGrantFailureKind
{
    BadRequest = 1,
    Unauthorized = 2,
    Forbidden = 3,
    NotFound = 4,
    Conflict = 5,
    UnprocessableEntity = 6,
    InternalError = 7
}

public sealed class PlayerEquipmentGrantException : BusinessException
{
    public PlayerEquipmentGrantException(
        PlayerEquipmentGrantFailureKind failureKind,
        string code,
        string message,
        IReadOnlyDictionary<string, string[]>? details = null)
        : base(code, message)
    {
        FailureKind = failureKind;
        Details = details;
    }

    public PlayerEquipmentGrantFailureKind FailureKind { get; }

    public IReadOnlyDictionary<string, string[]>? Details { get; }
}
