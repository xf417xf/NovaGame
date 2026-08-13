using NovaCore.BuildingBlocks.Domain;

namespace SwordMaster.Domain.PlayerAssets;

public sealed class PlayerAssetsProfile : AggregateRoot<Guid>
{
    private readonly List<OwnedEquipmentItem> _ownedItems = [];
    private readonly List<EquipmentAssignment> _equipmentAssignments = [];

    private PlayerAssetsProfile()
    {
        Character = null!;
    }

    public PlayerAssetsProfile(
        PlayerId playerId,
        PlayerCharacter character,
        IEnumerable<OwnedEquipmentItem> ownedItems)
    {
        ArgumentNullException.ThrowIfNull(playerId);
        Character = character ?? throw new ArgumentNullException(nameof(character));
        ArgumentNullException.ThrowIfNull(ownedItems);

        var copiedItems = ownedItems
            .Select(item => item ?? throw new ArgumentException("Owned items cannot contain null values.", nameof(ownedItems)))
            .ToArray();
        if (copiedItems
            .GroupBy(item => item.OwnedItemId.Value)
            .Any(group => group.Count() > 1))
        {
            throw new ArgumentException("Owned-item identifiers must be unique within a player profile.", nameof(ownedItems));
        }

        Id = playerId.Value;
        Revision = 1;
        _ownedItems.AddRange(copiedItems);
    }

    public PlayerId PlayerId => global::SwordMaster.Domain.PlayerAssets.PlayerId.From(Id);

    public PlayerCharacter Character { get; private set; }

    public long Revision { get; private set; }

    public IReadOnlyCollection<OwnedEquipmentItem> OwnedItems => _ownedItems.AsReadOnly();

    public IReadOnlyCollection<EquipmentAssignment> EquipmentAssignments => _equipmentAssignments.AsReadOnly();

    public OwnedEquipmentItem GetOwnedItem(OwnedItemId ownedItemId)
    {
        ArgumentNullException.ThrowIfNull(ownedItemId);
        return _ownedItems.SingleOrDefault(item => item.OwnedItemId.Equals(ownedItemId)) ??
            throw new PlayerAssetsDomainException(
                PlayerAssetsErrorCodes.OwnedItemNotFound,
                "The requested owned equipment item does not belong to this player.");
    }

    public OwnedEquipmentItem GrantEquipment(
        long expectedRevision,
        OwnedItemId ownedItemId,
        string configuredItemId)
    {
        ArgumentNullException.ThrowIfNull(ownedItemId);
        EnsureRevision(expectedRevision);

        var ownedItem = new OwnedEquipmentItem(ownedItemId, configuredItemId);
        if (_ownedItems.Any(item => item.OwnedItemId.Equals(ownedItemId)))
        {
            throw new ArgumentException(
                "The owned-item identifier already exists in this player profile.",
                nameof(ownedItemId));
        }

        var nextRevision = NextRevision();
        _ownedItems.Add(ownedItem);
        Revision = nextRevision;
        return ownedItem;
    }

    public OwnedEquipmentItem RollbackGrantedEquipment(
        long expectedRevision,
        OwnedItemId ownedItemId)
    {
        ArgumentNullException.ThrowIfNull(ownedItemId);
        EnsureRevision(expectedRevision);

        var ownedItem = _ownedItems.SingleOrDefault(
            item => item.OwnedItemId.Equals(ownedItemId));
        if (ownedItem is null)
        {
            throw new PlayerAssetsDomainException(
                PlayerAssetsErrorCodes.EquipmentGrantGrantedItemMissing,
                "The equipment item created by this active grant is no longer owned by the player.");
        }

        if (_equipmentAssignments.Any(
                assignment => assignment.OwnedItemId.Equals(ownedItemId)))
        {
            throw new PlayerAssetsDomainException(
                PlayerAssetsErrorCodes.EquipmentGrantRollbackBlockedItemEquipped,
                "The equipment item created by this grant must be unequipped before rollback.");
        }

        var nextRevision = NextRevision();
        _ownedItems.Remove(ownedItem);
        Revision = nextRevision;
        return ownedItem;
    }

    public EquipmentAssignment Equip(
        long expectedRevision,
        CharacterId characterId,
        EquipmentSlot slot,
        OwnedItemId ownedItemId,
        EquipmentDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(characterId);
        ArgumentNullException.ThrowIfNull(slot);
        ArgumentNullException.ThrowIfNull(ownedItemId);
        ArgumentNullException.ThrowIfNull(definition);

        EnsureRevision(expectedRevision);
        EnsureCharacter(characterId);
        var ownedItem = GetOwnedItem(ownedItemId);

        if (!string.Equals(ownedItem.ConfiguredItemId, definition.ConfiguredItemId, StringComparison.Ordinal))
        {
            throw new PlayerAssetsDomainException(
                PlayerAssetsErrorCodes.ItemNotEquipable,
                "The equipment definition does not describe the selected owned item.");
        }

        if (!slot.Equals(definition.CompatibleSlot))
        {
            throw new PlayerAssetsDomainException(
                PlayerAssetsErrorCodes.SlotMismatch,
                "The selected equipment item is not compatible with the requested slot.");
        }

        if (_equipmentAssignments.Any(assignment => assignment.OwnedItemId.Equals(ownedItemId)))
        {
            throw new PlayerAssetsDomainException(
                PlayerAssetsErrorCodes.ItemAlreadyEquipped,
                "The selected owned item is already equipped.");
        }

        if (_equipmentAssignments.Any(assignment => assignment.Slot.Equals(slot)))
        {
            throw new PlayerAssetsDomainException(
                PlayerAssetsErrorCodes.SlotOccupied,
                "The requested equipment slot is already occupied and must be unequipped first.");
        }

        EnsureAttributeTotalsDoNotOverflow(
            _equipmentAssignments
                .SelectMany(assignment => assignment.Modifiers)
                .Concat(definition.ModifierSnapshot.Modifiers));
        var nextRevision = NextRevision();
        var assignment = new EquipmentAssignment(
            characterId,
            slot,
            ownedItemId,
            ownedItem.ConfiguredItemId,
            definition.ConfigurationVersionId,
            definition.ModifierSnapshot);

        _equipmentAssignments.Add(assignment);
        Revision = nextRevision;
        return assignment;
    }

    public EquipmentAssignment Unequip(
        long expectedRevision,
        CharacterId characterId,
        EquipmentSlot slot)
    {
        ArgumentNullException.ThrowIfNull(characterId);
        ArgumentNullException.ThrowIfNull(slot);

        EnsureRevision(expectedRevision);
        EnsureCharacter(characterId);
        var assignment = _equipmentAssignments.SingleOrDefault(candidate => candidate.Slot.Equals(slot));
        if (assignment is null)
        {
            throw new PlayerAssetsDomainException(
                PlayerAssetsErrorCodes.SlotNotEquipped,
                "The requested equipment slot is not equipped.");
        }

        EnsureAttributeTotalsDoNotOverflow(
            _equipmentAssignments
                .Where(candidate => !ReferenceEquals(candidate, assignment))
                .SelectMany(candidate => candidate.Modifiers));
        var nextRevision = NextRevision();
        _equipmentAssignments.Remove(assignment);
        Revision = nextRevision;
        return assignment;
    }

    public CharacterAttributeBreakdown GetAttributeBreakdown(CharacterId characterId)
    {
        ArgumentNullException.ThrowIfNull(characterId);
        EnsureCharacter(characterId);

        var baseTotals = SumAttributes(Character.BaseAttributes);
        var equipmentTotals = SumAttributes(
            _equipmentAssignments.SelectMany(assignment => assignment.Modifiers));
        var effectiveTotals = SumAttributes(
            Character.BaseAttributes.Concat(
                _equipmentAssignments.SelectMany(assignment => assignment.Modifiers)));

        return new CharacterAttributeBreakdown(
            ToAttributeValues(baseTotals),
            ToAttributeValues(equipmentTotals),
            ToAttributeValues(effectiveTotals));
    }

    private void EnsureCharacter(CharacterId characterId)
    {
        if (!Character.CharacterId.Equals(characterId))
        {
            throw new PlayerAssetsDomainException(
                PlayerAssetsErrorCodes.CharacterNotFound,
                "The requested character does not belong to this player profile.");
        }
    }

    private void EnsureRevision(long expectedRevision)
    {
        if (expectedRevision != Revision)
        {
            throw new PlayerAssetsDomainException(
                PlayerAssetsErrorCodes.ConcurrencyConflict,
                $"Player-assets revision {expectedRevision} is stale; the current revision is {Revision}.");
        }
    }

    private long NextRevision()
    {
        try
        {
            return checked(Revision + 1);
        }
        catch (OverflowException)
        {
            throw new PlayerAssetsDomainException(
                PlayerAssetsErrorCodes.ConcurrencyConflict,
                "The player-assets revision cannot be incremented further.");
        }
    }

    private void EnsureAttributeTotalsDoNotOverflow(IEnumerable<AttributeValue> prospectiveEquipmentModifiers)
    {
        var copiedModifiers = prospectiveEquipmentModifiers.ToArray();
        _ = SumAttributes(copiedModifiers);
        _ = SumAttributes(Character.BaseAttributes.Concat(copiedModifiers));
    }

    private static Dictionary<string, int> SumAttributes(IEnumerable<AttributeValue> attributes)
    {
        var wideTotals = new Dictionary<string, long>(StringComparer.Ordinal);
        try
        {
            foreach (var attribute in attributes)
            {
                wideTotals.TryGetValue(attribute.Code.Value, out var current);
                wideTotals[attribute.Code.Value] = checked(current + (long)attribute.Value.Value);
            }
        }
        catch (OverflowException)
        {
            throw new PlayerAssetsDomainException(
                PlayerAssetsErrorCodes.InvalidModifier,
                "Equipment modifiers would overflow an effective integer attribute value.");
        }

        if (wideTotals.Values.Any(value => value is < int.MinValue or > int.MaxValue))
        {
            throw new PlayerAssetsDomainException(
                PlayerAssetsErrorCodes.InvalidModifier,
                "Equipment modifiers would overflow an effective integer attribute value.");
        }

        return wideTotals.ToDictionary(
            pair => pair.Key,
            pair => (int)pair.Value,
            StringComparer.Ordinal);
    }

    private static IReadOnlyList<AttributeValue> ToAttributeValues(
        IReadOnlyDictionary<string, int> attributes)
    {
        return attributes
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => AttributeValue.Create(pair.Key, pair.Value))
            .ToArray();
    }
}
