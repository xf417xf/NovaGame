using System.Text.Json;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using SwordMaster.Domain.PlayerAssets;

namespace SwordMaster.Host.EntityFrameworkCore.PlayerAssets;

internal sealed class PlayerIdValueConverter : ValueConverter<PlayerId, Guid>
{
    public PlayerIdValueConverter()
        : base(playerId => playerId.Value, value => PlayerId.From(value))
    {
    }
}

internal sealed class CharacterIdValueConverter : ValueConverter<CharacterId, Guid>
{
    public CharacterIdValueConverter()
        : base(characterId => characterId.Value, value => CharacterId.From(value))
    {
    }
}

internal sealed class OwnedItemIdValueConverter : ValueConverter<OwnedItemId, Guid>
{
    public OwnedItemIdValueConverter()
        : base(ownedItemId => ownedItemId.Value, value => OwnedItemId.From(value))
    {
    }
}

internal sealed class EquipmentSlotValueConverter : ValueConverter<EquipmentSlot, string>
{
    public EquipmentSlotValueConverter()
        : base(slot => slot.Value, value => EquipmentSlot.Create(value))
    {
    }
}

internal sealed class StatCodeValueConverter : ValueConverter<StatCode, string>
{
    public StatCodeValueConverter()
        : base(code => code.Value, value => StatCode.Create(value))
    {
    }
}

internal sealed class StatValueValueConverter : ValueConverter<StatValue, int>
{
    public StatValueValueConverter()
        : base(value => value.Value, value => StatValue.From(value))
    {
    }
}

internal sealed class EquipmentModifierSnapshotJsonConverter :
    ValueConverter<EquipmentModifierSnapshot, string>
{
    public EquipmentModifierSnapshotJsonConverter()
        : base(
            snapshot => EquipmentModifierSnapshotPersistence.Serialize(snapshot),
            json => EquipmentModifierSnapshotPersistence.Deserialize(json))
    {
    }
}

internal sealed class EquipmentModifierSnapshotValueComparer :
    ValueComparer<EquipmentModifierSnapshot>
{
    public EquipmentModifierSnapshotValueComparer()
        : base(
            (left, right) => EquipmentModifierSnapshotPersistence.Equals(left, right),
            snapshot => EquipmentModifierSnapshotPersistence.GetHashCode(snapshot),
            snapshot => EquipmentModifierSnapshotPersistence.Copy(snapshot))
    {
    }
}

internal static class EquipmentModifierSnapshotPersistence
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public static string Serialize(EquipmentModifierSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var modifiers = snapshot.Modifiers
            .OrderBy(modifier => modifier.Code.Value, StringComparer.Ordinal)
            .Select(modifier => new PersistedModifier(
                modifier.Code.Value,
                modifier.Value.Value))
            .ToArray();
        return JsonSerializer.Serialize(modifiers, SerializerOptions);
    }

    public static EquipmentModifierSnapshot Deserialize(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new InvalidOperationException(
                "A persisted equipment-modifier snapshot cannot be empty.");
        }

        PersistedModifier[] modifiers;
        try
        {
            modifiers = JsonSerializer.Deserialize<PersistedModifier[]>(json, SerializerOptions)
                ?? throw new InvalidOperationException(
                    "A persisted equipment-modifier snapshot must be a JSON array.");
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(
                "A persisted equipment-modifier snapshot contains invalid JSON.",
                exception);
        }

        return new EquipmentModifierSnapshot(
            modifiers.Select(modifier => AttributeValue.Create(
                modifier.Code,
                modifier.Value)));
    }

    public static bool Equals(
        EquipmentModifierSnapshot? left,
        EquipmentModifierSnapshot? right)
    {
        return ReferenceEquals(left, right) ||
            (left is not null && right is not null && left.Equals(right));
    }

    public static int GetHashCode(EquipmentModifierSnapshot snapshot)
    {
        return snapshot.GetHashCode();
    }

    public static EquipmentModifierSnapshot Copy(EquipmentModifierSnapshot snapshot)
    {
        return new EquipmentModifierSnapshot(
            snapshot.Modifiers.Select(modifier => AttributeValue.Create(
                modifier.Code.Value,
                modifier.Value.Value)));
    }

    private sealed record PersistedModifier(string Code, int Value);
}
