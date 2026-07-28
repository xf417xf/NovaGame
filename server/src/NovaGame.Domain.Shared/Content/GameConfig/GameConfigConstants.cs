namespace NovaGame.Domain.Shared.Content.GameConfig;

public static class GameConfigConstants
{
    public const string SupportedSchemaVersion = "1";
    public const string SnapshotFormatVersion = "1";
    public const string ValidationProfile = "NovaGame.GameConfig.Validation.v1";
    public const string CanonicalizationProfile = "NovaGame.GameConfig.CanonicalJson.v1";
    public const string ContentHashAlgorithm = "SHA-256";

    public const int ConfigurationSetKeyMaximumLength = 64;
    public const int EnvironmentNameMaximumLength = 64;
    public const int DraftNameMaximumLength = 128;
    public const int CollectionNameMaximumLength = 64;
    public const int RecordIdMaximumLength = 128;
    public const int SchemaVersionMaximumLength = 32;
    public const int RawContentMaximumLength = 4 * 1024 * 1024;
    public const int ReleaseNotesMaximumLength = 2_000;
    public const int RollbackReasonMaximumLength = 512;
}
