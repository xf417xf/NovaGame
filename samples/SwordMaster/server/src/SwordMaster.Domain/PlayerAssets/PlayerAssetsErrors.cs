using NovaCore.BuildingBlocks.Exceptions;

namespace SwordMaster.Domain.PlayerAssets;

public static class PlayerAssetsErrorCodes
{
    public const string CharacterNotFound = "SwordMaster.PlayerAssets.CharacterNotFound";
    public const string OwnedItemNotFound = "SwordMaster.PlayerAssets.OwnedItemNotFound";
    public const string ItemNotEquipable = "SwordMaster.PlayerAssets.ItemNotEquipable";
    public const string SlotMismatch = "SwordMaster.PlayerAssets.SlotMismatch";
    public const string ItemAlreadyEquipped = "SwordMaster.PlayerAssets.ItemAlreadyEquipped";
    public const string SlotOccupied = "SwordMaster.PlayerAssets.SlotOccupied";
    public const string SlotNotEquipped = "SwordMaster.PlayerAssets.SlotNotEquipped";
    public const string InvalidModifier = "SwordMaster.PlayerAssets.InvalidModifier";
    public const string ConcurrencyConflict = "SwordMaster.PlayerAssets.ConcurrencyConflict";
    public const string IdempotencyConflict = "SwordMaster.PlayerAssets.IdempotencyConflict";
}

public sealed class PlayerAssetsDomainException : BusinessException
{
    public PlayerAssetsDomainException(string code, string message)
        : base(code, message)
    {
    }
}
