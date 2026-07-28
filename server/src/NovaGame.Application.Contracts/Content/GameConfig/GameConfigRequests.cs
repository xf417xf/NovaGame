using System.ComponentModel.DataAnnotations;

namespace NovaGame.Application.Contracts.Content.GameConfig;

public sealed class ListGameConfigDraftsRequest
{
    [Required]
    [RegularExpression(GameConfigContractLimits.ConfigurationSetPattern)]
    public string ConfigurationSet { get; init; } = GameConfigContractLimits.DefaultConfigurationSet;

    public GameConfigDraftState? State { get; init; }

    [StringLength(GameConfigContractLimits.SearchMaximumLength)]
    public string? Search { get; init; }

    [Range(1, int.MaxValue)]
    public int Page { get; init; } = 1;

    [Range(1, GameConfigContractLimits.MaximumPageSize)]
    public int PageSize { get; init; } = 20;
}

public sealed class CreateGameConfigDraftRequest
{
    [Required]
    [RegularExpression(GameConfigContractLimits.ConfigurationSetPattern)]
    public string ConfigurationSet { get; init; } = GameConfigContractLimits.DefaultConfigurationSet;

    [Required]
    [StringLength(GameConfigContractLimits.NameMaximumLength, MinimumLength = 1)]
    public string Name { get; init; } = string.Empty;

    [Required]
    [RegularExpression(GameConfigContractLimits.SchemaVersionPattern)]
    public string SchemaVersion { get; init; } = string.Empty;

    public Guid? BaseVersionId { get; init; }

    [Required(AllowEmptyStrings = true)]
    [StringLength(GameConfigContractLimits.ContentMaximumLength)]
    public string Content { get; init; } = string.Empty;
}

public sealed class UpdateGameConfigDraftRequest
{
    [Range(1, long.MaxValue)]
    public long ExpectedRevision { get; init; }

    [Required]
    [StringLength(GameConfigContractLimits.NameMaximumLength, MinimumLength = 1)]
    public string Name { get; init; } = string.Empty;

    [Required]
    [RegularExpression(GameConfigContractLimits.SchemaVersionPattern)]
    public string SchemaVersion { get; init; } = string.Empty;

    [Required(AllowEmptyStrings = true)]
    [StringLength(GameConfigContractLimits.ContentMaximumLength)]
    public string Content { get; init; } = string.Empty;
}

public sealed class ValidateGameConfigDraftRequest
{
    [Range(1, long.MaxValue)]
    public long ExpectedRevision { get; init; }
}

public sealed class PublishGameConfigDraftRequest
{
    [Range(1, long.MaxValue)]
    public long ExpectedRevision { get; init; }

    [Required]
    [StringLength(GameConfigContractLimits.ReleaseNotesMaximumLength, MinimumLength = 1)]
    public string ReleaseNotes { get; init; } = string.Empty;
}

public sealed class ListGameConfigVersionsRequest
{
    [Required]
    [RegularExpression(GameConfigContractLimits.ConfigurationSetPattern)]
    public string ConfigurationSet { get; init; } = GameConfigContractLimits.DefaultConfigurationSet;

    [RegularExpression(GameConfigContractLimits.SchemaVersionPattern)]
    public string? SchemaVersion { get; init; }

    [Range(1, int.MaxValue)]
    public int Page { get; init; } = 1;

    [Range(1, GameConfigContractLimits.MaximumPageSize)]
    public int PageSize { get; init; } = 20;
}

public sealed class GameConfigEnvironmentQuery
{
    [Required]
    [RegularExpression(GameConfigContractLimits.ConfigurationSetPattern)]
    public string ConfigurationSet { get; init; } = GameConfigContractLimits.DefaultConfigurationSet;
}

public sealed class ActivateGameConfigVersionRequest
{
    [Required]
    [RegularExpression(GameConfigContractLimits.ConfigurationSetPattern)]
    public string ConfigurationSet { get; init; } = GameConfigContractLimits.DefaultConfigurationSet;

    public Guid VersionId { get; init; }

    [Range(0, long.MaxValue)]
    public long ExpectedRevision { get; init; }

    [StringLength(GameConfigContractLimits.ReasonMaximumLength)]
    public string? Reason { get; init; }
}

public sealed class RollbackGameConfigEnvironmentRequest
{
    [Required]
    [RegularExpression(GameConfigContractLimits.ConfigurationSetPattern)]
    public string ConfigurationSet { get; init; } = GameConfigContractLimits.DefaultConfigurationSet;

    public Guid TargetVersionId { get; init; }

    [Range(1, long.MaxValue)]
    public long ExpectedRevision { get; init; }

    [Required]
    [StringLength(GameConfigContractLimits.ReasonMaximumLength, MinimumLength = 1)]
    public string Reason { get; init; } = string.Empty;
}
