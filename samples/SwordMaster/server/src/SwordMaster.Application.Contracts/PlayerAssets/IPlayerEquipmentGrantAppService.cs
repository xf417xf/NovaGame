namespace SwordMaster.Application.Contracts.PlayerAssets;

public interface IPlayerEquipmentGrantAppService
{
    Task<PlayerEquipmentGrantPageDto> GetPageAsync(
        Guid playerId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<PlayerEquipmentGrantMutationResultDto> GrantAsync(
        Guid playerId,
        GrantPlayerEquipmentRequest request,
        PlayerEquipmentGrantMutationContext context,
        CancellationToken cancellationToken = default);

    Task<PlayerEquipmentGrantMutationResultDto> RollbackAsync(
        Guid playerId,
        Guid grantId,
        RollbackPlayerEquipmentGrantRequest request,
        PlayerEquipmentGrantMutationContext context,
        CancellationToken cancellationToken = default);
}
