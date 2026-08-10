using NovaCore.BuildingBlocks.Domain;

namespace SwordMaster.Domain.PlayerAssets;

public abstract class PlayerAssetsValueObject : ValueObject
{
    protected sealed override IEnumerable<object?> GetAtomicValues()
    {
        yield return GetType();

        foreach (var value in GetPlayerAssetsAtomicValues())
        {
            yield return value;
        }
    }

    protected abstract IEnumerable<object?> GetPlayerAssetsAtomicValues();
}
