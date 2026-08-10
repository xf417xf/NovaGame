namespace SwordMaster.Application.Contracts.PlayerAssets;

public interface IPlayerAssetsAppService
{
    Task<PlayerAssetsMutationResultDto> EquipAsync(
        Guid characterId,
        string slot,
        EquipEquipmentRequest request,
        PlayerAssetsMutationContext mutationContext,
        CancellationToken cancellationToken = default);

    Task<PlayerAssetsMutationResultDto> UnequipAsync(
        Guid characterId,
        string slot,
        PlayerAssetsMutationContext mutationContext,
        CancellationToken cancellationToken = default);

    Task<CharacterAttributesDto> GetAttributesAsync(
        Guid characterId,
        CancellationToken cancellationToken = default);
}
