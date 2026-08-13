namespace SwordMaster.Application.Contracts.PlayerAssets;

public sealed record GrantPlayerEquipmentRequest(
    string ConfiguredItemId,
    string Reason);

public sealed record RollbackPlayerEquipmentGrantRequest(string Reason);

public sealed record PlayerEquipmentGrantMutationContext(
    long ExpectedRevision,
    Guid IdempotencyKey);
