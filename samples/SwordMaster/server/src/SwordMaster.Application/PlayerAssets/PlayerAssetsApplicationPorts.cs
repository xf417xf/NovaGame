using SwordMaster.Domain.PlayerAssets;

namespace SwordMaster.Application.PlayerAssets;

public interface ICurrentPlayerContext
{
    bool IsAuthenticated { get; }

    Guid? PlayerId { get; }
}

public interface IEquipmentDefinitionProvider
{
    Task<EquipmentDefinitionResolution> ResolveActiveAsync(
        string configuredItemId,
        CancellationToken cancellationToken = default);
}

public interface IPlayerAssetsTransactionManager
{
    Task<IPlayerAssetsTransactionScope> BeginAsync(
        CancellationToken cancellationToken = default);
}

public interface IPlayerAssetsTransactionScope : IAsyncDisposable
{
    Task CompleteAsync(CancellationToken cancellationToken = default);
}

public enum EquipmentDefinitionResolutionStatus
{
    Found = 1,
    Missing = 2,
    Invalid = 3
}

public sealed class EquipmentDefinitionResolution
{
    private EquipmentDefinitionResolution(
        EquipmentDefinitionResolutionStatus status,
        EquipmentDefinition? definition)
    {
        Status = status;
        Definition = definition;
    }

    public EquipmentDefinitionResolutionStatus Status { get; }

    public EquipmentDefinition? Definition { get; }

    public static EquipmentDefinitionResolution Found(EquipmentDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return new EquipmentDefinitionResolution(
            EquipmentDefinitionResolutionStatus.Found,
            definition);
    }

    public static EquipmentDefinitionResolution Missing()
    {
        return new EquipmentDefinitionResolution(
            EquipmentDefinitionResolutionStatus.Missing,
            definition: null);
    }

    public static EquipmentDefinitionResolution Invalid()
    {
        return new EquipmentDefinitionResolution(
            EquipmentDefinitionResolutionStatus.Invalid,
            definition: null);
    }
}
