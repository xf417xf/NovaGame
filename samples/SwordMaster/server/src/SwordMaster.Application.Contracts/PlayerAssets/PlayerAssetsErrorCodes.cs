namespace SwordMaster.Application.Contracts.PlayerAssets;

public static class PlayerAssetsErrorCodes
{
    public const string InvalidRequest = "SwordMaster.PlayerAssets.InvalidRequest";
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
    public const string UnauthorizedPlayer = "SwordMaster.PlayerAssets.UnauthorizedPlayer";
}
