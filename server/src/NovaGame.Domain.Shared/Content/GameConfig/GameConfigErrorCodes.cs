namespace NovaGame.Domain.Shared.Content.GameConfig;

public static class GameConfigErrorCodes
{
    public const string InvalidRequest = "NovaGame.GameConfig.InvalidRequest";
    public const string Unauthorized = "NovaGame.GameConfig.Unauthorized";
    public const string Forbidden = "NovaGame.GameConfig.Forbidden";
    public const string DraftNotFound = "NovaGame.GameConfig.DraftNotFound";
    public const string PublishedVersionNotFound = "NovaGame.GameConfig.PublishedVersionNotFound";
    public const string ActiveVersionNotFound = "NovaGame.GameConfig.ActiveVersionNotFound";
    public const string DraftRevisionConflict = "NovaGame.GameConfig.DraftRevisionConflict";
    public const string DraftNotValidated = "NovaGame.GameConfig.DraftNotValidated";
    public const string DraftRevisionAlreadyPublished = "NovaGame.GameConfig.DraftRevisionAlreadyPublished";
    public const string ActivationConflict = "NovaGame.GameConfig.ActivationConflict";
    public const string ConfigurationSetMismatch = "NovaGame.GameConfig.ConfigurationSetMismatch";
    public const string SchemaIncompatible = "NovaGame.GameConfig.SchemaIncompatible";
    public const string InvalidRollbackTarget = "NovaGame.GameConfig.InvalidRollbackTarget";
    public const string SnapshotIntegrityInvalid = "NovaGame.GameConfig.SnapshotIntegrityInvalid";
}
