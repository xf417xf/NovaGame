namespace SwordMaster.Application.Contracts.PlayerAssets;

public static class PlayerEquipmentGrantErrorCodes
{
    private const string Prefix = "SwordMaster.PlayerAssets.EquipmentGrants";

    public const string InvalidRequest = Prefix + ".InvalidRequest";
    public const string UnauthorizedAdministrator = Prefix + ".UnauthorizedAdministrator";
    public const string Forbidden = Prefix + ".Forbidden";
    public const string PlayerNotFound = Prefix + ".PlayerNotFound";
    public const string GrantNotFound = Prefix + ".GrantNotFound";
    public const string ConfigurationNotFound = Prefix + ".ConfigurationNotFound";
    public const string ConfigurationInvalid = Prefix + ".ConfigurationInvalid";
    public const string ConcurrencyConflict = Prefix + ".ConcurrencyConflict";
    public const string IdempotencyConflict = Prefix + ".IdempotencyConflict";
    public const string AlreadyRolledBack = Prefix + ".AlreadyRolledBack";
    public const string RollbackBlockedItemEquipped =
        Prefix + ".RollbackBlockedItemEquipped";
    public const string GrantedItemMissing = Prefix + ".GrantedItemMissing";
}
