namespace SwordMaster.Application.PlayerAssets;

public interface ICurrentSwordMasterAdministrator
{
    bool IsAuthenticated { get; }

    Guid? AdministratorId { get; }
}

public interface IPlayerEquipmentGrantAuditContextAccessor
{
    PlayerEquipmentGrantAuditContext Current { get; }
}

public sealed record PlayerEquipmentGrantAuditContext(
    string? IpAddress,
    string? UserAgent,
    string? CorrelationId);
