namespace NovaGame.Application.Contracts.Content.GameConfig;

public static class GameConfigContractLimits
{
    public const int ConfigurationSetMaximumLength =
        Domain.Shared.Content.GameConfig.GameConfigConstants.ConfigurationSetKeyMaximumLength;
    public const int EnvironmentNameMaximumLength =
        Domain.Shared.Content.GameConfig.GameConfigConstants.EnvironmentNameMaximumLength;
    public const int NameMaximumLength =
        Domain.Shared.Content.GameConfig.GameConfigConstants.DraftNameMaximumLength;
    public const int SchemaVersionMaximumLength =
        Domain.Shared.Content.GameConfig.GameConfigConstants.SchemaVersionMaximumLength;
    public const int SearchMaximumLength = 128;
    public const int ReleaseNotesMaximumLength =
        Domain.Shared.Content.GameConfig.GameConfigConstants.ReleaseNotesMaximumLength;
    public const int ReasonMaximumLength =
        Domain.Shared.Content.GameConfig.GameConfigConstants.RollbackReasonMaximumLength;
    public const int ContentMaximumLength =
        Domain.Shared.Content.GameConfig.GameConfigConstants.RawContentMaximumLength;
    public const int MaximumPageSize = 100;

    public const string DefaultConfigurationSet = "default";
    public const string ConfigurationSetPattern = "^[a-z][a-z0-9-]{0,63}$";
    public const string EnvironmentNamePattern = "^[a-z][a-z0-9-]{0,63}$";
    public const string SchemaVersionPattern = "^[A-Za-z0-9][A-Za-z0-9._+-]{0,31}$";
}
