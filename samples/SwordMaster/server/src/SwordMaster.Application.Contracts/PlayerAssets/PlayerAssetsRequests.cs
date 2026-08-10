namespace SwordMaster.Application.Contracts.PlayerAssets;

public sealed record EquipEquipmentRequest(Guid OwnedItemId);

public sealed record PlayerAssetsMutationContext(
    long ExpectedRevision,
    Guid IdempotencyKey);
