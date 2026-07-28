namespace NovaGame.Domain.Shared.Content.GameConfig;

public static class GameConfigValidationCodes
{
    public const string InvalidJson = "NovaGame.GameConfig.Validation.InvalidJson";
    public const string ContentTooLarge = "NovaGame.GameConfig.Validation.ContentTooLarge";
    public const string UnsupportedSchemaVersion = "NovaGame.GameConfig.Validation.UnsupportedSchemaVersion";
    public const string RootObjectRequired = "NovaGame.GameConfig.Validation.RootObjectRequired";
    public const string CollectionsRequired = "NovaGame.GameConfig.Validation.CollectionsRequired";
    public const string CollectionsMustBeObject = "NovaGame.GameConfig.Validation.CollectionsMustBeObject";
    public const string UnexpectedProperty = "NovaGame.GameConfig.Validation.UnexpectedProperty";
    public const string InvalidCollectionName = "NovaGame.GameConfig.Validation.InvalidCollectionName";
    public const string CollectionMustBeArray = "NovaGame.GameConfig.Validation.CollectionMustBeArray";
    public const string RecordMustBeObject = "NovaGame.GameConfig.Validation.RecordMustBeObject";
    public const string RecordIdRequired = "NovaGame.GameConfig.Validation.RecordIdRequired";
    public const string InvalidRecordId = "NovaGame.GameConfig.Validation.InvalidRecordId";
    public const string DuplicateRecordId = "NovaGame.GameConfig.Validation.DuplicateRecordId";
    public const string DuplicateProperty = "NovaGame.GameConfig.Validation.DuplicateProperty";
    public const string InvalidReference = "NovaGame.GameConfig.Validation.InvalidReference";
    public const string ReferenceNotFound = "NovaGame.GameConfig.Validation.ReferenceNotFound";
}
