namespace SwordMaster.Application.Contracts.PlayerAssets;

public enum PlayerEquipmentGrantStatusDto
{
    Active = 1,
    RolledBack = 2
}

public sealed record PlayerEquipmentGrantDto(
    Guid GrantId,
    Guid PlayerId,
    Guid OwnedItemId,
    string ConfiguredItemId,
    Guid ConfigurationVersionId,
    PlayerEquipmentGrantStatusDto Status,
    long GrantResultingRevision,
    Guid GrantedByAdministratorId,
    DateTime GrantedAtUtc,
    string GrantReason,
    long? RollbackResultingRevision,
    Guid? RolledBackByAdministratorId,
    DateTime? RolledBackAtUtc,
    string? RollbackReason);

public sealed record PlayerEquipmentGrantMutationResultDto(
    Guid GrantId,
    Guid PlayerId,
    Guid OwnedItemId,
    string ConfiguredItemId,
    Guid ConfigurationVersionId,
    PlayerEquipmentGrantStatusDto Status,
    long ResultingRevision,
    Guid GrantedByAdministratorId,
    DateTime GrantedAtUtc,
    string GrantReason,
    Guid? RolledBackByAdministratorId,
    DateTime? RolledBackAtUtc,
    string? RollbackReason);

public sealed record PlayerEquipmentGrantPageDto(
    Guid PlayerId,
    long Revision,
    int Page,
    int PageSize,
    long TotalCount,
    IReadOnlyList<PlayerEquipmentGrantDto> Items);
