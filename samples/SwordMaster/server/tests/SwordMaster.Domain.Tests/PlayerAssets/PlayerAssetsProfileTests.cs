using System.Reflection;
using SwordMaster.Domain.PlayerAssets;
using Xunit;

namespace SwordMaster.Domain.Tests.PlayerAssets;

public sealed class PlayerAssetsProfileTests
{
    private static readonly PlayerId PlayerId = PlayerId.From(Guid.Parse("11111111-1111-1111-1111-111111111111"));
    private static readonly CharacterId CharacterId = CharacterId.From(Guid.Parse("22222222-2222-2222-2222-222222222222"));
    private static readonly OwnedItemId WeaponItemId = OwnedItemId.From(Guid.Parse("33333333-3333-3333-3333-333333333333"));
    private static readonly OwnedItemId SecondWeaponItemId = OwnedItemId.From(Guid.Parse("44444444-4444-4444-4444-444444444444"));
    private static readonly OwnedItemId ArmorItemId = OwnedItemId.From(Guid.Parse("55555555-5555-5555-5555-555555555555"));
    private static readonly Guid ConfigurationVersionId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly EquipmentSlot WeaponSlot = EquipmentSlot.Create("weapon");
    private static readonly EquipmentSlot ArmorSlot = EquipmentSlot.Create("armor");

    [Fact]
    public void NewProfileOwnsOneCharacterItemsAndInitialRevision()
    {
        var profile = CreateProfile();

        Assert.Equal(PlayerId, profile.PlayerId);
        Assert.Equal(CharacterId, profile.Character.CharacterId);
        Assert.Equal(3, profile.OwnedItems.Count);
        Assert.Empty(profile.EquipmentAssignments);
        Assert.Equal(1, profile.Revision);
    }

    [Fact]
    public void EquipPinsConfigurationAndAddsDeterministicAttributes()
    {
        var profile = CreateProfile();
        var sourceModifiers = new List<AttributeValue>
        {
            AttributeValue.Create("attack", 7),
            AttributeValue.Create("health", 20)
        };
        var definition = new EquipmentDefinition(
            "training-sword",
            WeaponSlot,
            ConfigurationVersionId,
            sourceModifiers);

        var assignment = profile.Equip(
            profile.Revision,
            CharacterId,
            WeaponSlot,
            WeaponItemId,
            definition);
        sourceModifiers[0] = AttributeValue.Create("attack", 999);

        Assert.Equal(2, profile.Revision);
        Assert.Equal(ConfigurationVersionId, assignment.ConfigurationVersionId);
        Assert.Equal(WeaponItemId, assignment.OwnedItemId);
        Assert.Equal(7, GetStat(assignment.Modifiers, "attack"));

        var attributes = profile.GetAttributeBreakdown(CharacterId);
        Assert.Equal(10, GetStat(attributes.BaseAttributes, "attack"));
        Assert.Equal(7, GetStat(attributes.EquipmentAttributes, "attack"));
        Assert.Equal(17, GetStat(attributes.EffectiveAttributes, "attack"));
        Assert.Equal(120, GetStat(attributes.EffectiveAttributes, "health"));
    }

    [Fact]
    public void UnequipRemovesExactlyThePinnedModifiers()
    {
        var profile = CreateProfile();
        var originalDefinition = CreateWeaponDefinition(ConfigurationVersionId, attack: 7);
        profile.Equip(profile.Revision, CharacterId, WeaponSlot, WeaponItemId, originalDefinition);

        _ = CreateWeaponDefinition(Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"), attack: 30);
        var removed = profile.Unequip(profile.Revision, CharacterId, WeaponSlot);

        Assert.Equal(ConfigurationVersionId, removed.ConfigurationVersionId);
        Assert.Equal(7, GetStat(removed.Modifiers, "attack"));
        Assert.Equal(3, profile.Revision);
        Assert.Empty(profile.EquipmentAssignments);
        var attributes = profile.GetAttributeBreakdown(CharacterId);
        Assert.Empty(attributes.EquipmentAttributes);
        Assert.Equal(10, GetStat(attributes.EffectiveAttributes, "attack"));
    }

    [Fact]
    public void OccupiedSlotMustBeExplicitlyUnequipped()
    {
        var profile = CreateProfile();
        profile.Equip(
            profile.Revision,
            CharacterId,
            WeaponSlot,
            WeaponItemId,
            CreateWeaponDefinition(ConfigurationVersionId, 7));

        var exception = Assert.Throws<PlayerAssetsDomainException>(() => profile.Equip(
            profile.Revision,
            CharacterId,
            WeaponSlot,
            SecondWeaponItemId,
            new EquipmentDefinition(
                "second-sword",
                WeaponSlot,
                ConfigurationVersionId,
                [AttributeValue.Create("attack", 3)])));

        Assert.Equal(PlayerAssetsErrorCodes.SlotOccupied, exception.Code);
        Assert.Equal(2, profile.Revision);
        Assert.Single(profile.EquipmentAssignments);
    }

    [Fact]
    public void OwnedItemMayOnlyBeAssignedOnce()
    {
        var profile = CreateProfile();
        var definition = CreateWeaponDefinition(ConfigurationVersionId, 7);
        profile.Equip(profile.Revision, CharacterId, WeaponSlot, WeaponItemId, definition);

        var exception = Assert.Throws<PlayerAssetsDomainException>(() => profile.Equip(
            profile.Revision,
            CharacterId,
            EquipmentSlot.Create("off-hand"),
            WeaponItemId,
            new EquipmentDefinition(
                "training-sword",
                EquipmentSlot.Create("off-hand"),
                ConfigurationVersionId,
                [AttributeValue.Create("attack", 2)])));

        Assert.Equal(PlayerAssetsErrorCodes.ItemAlreadyEquipped, exception.Code);
        Assert.Equal(2, profile.Revision);
    }

    [Theory]
    [InlineData("character")]
    [InlineData("owned-item")]
    [InlineData("definition")]
    [InlineData("slot")]
    [InlineData("revision")]
    public void FailedEquipLeavesAggregateUnchanged(string failure)
    {
        var profile = CreateProfile();
        var characterId = failure == "character"
            ? CharacterId.From(Guid.Parse("99999999-9999-9999-9999-999999999999"))
            : CharacterId;
        var itemId = failure == "owned-item"
            ? OwnedItemId.From(Guid.Parse("88888888-8888-8888-8888-888888888888"))
            : WeaponItemId;
        var definition = failure == "definition"
            ? new EquipmentDefinition(
                "different-item",
                WeaponSlot,
                ConfigurationVersionId,
                [AttributeValue.Create("attack", 7)])
            : CreateWeaponDefinition(ConfigurationVersionId, 7);
        var slot = failure == "slot" ? ArmorSlot : WeaponSlot;
        var expectedRevision = failure == "revision" ? 0 : profile.Revision;

        var exception = Assert.Throws<PlayerAssetsDomainException>(() => profile.Equip(
            expectedRevision,
            characterId,
            slot,
            itemId,
            definition));

        var expectedCode = failure switch
        {
            "character" => PlayerAssetsErrorCodes.CharacterNotFound,
            "owned-item" => PlayerAssetsErrorCodes.OwnedItemNotFound,
            "definition" => PlayerAssetsErrorCodes.ItemNotEquipable,
            "slot" => PlayerAssetsErrorCodes.SlotMismatch,
            _ => PlayerAssetsErrorCodes.ConcurrencyConflict
        };
        Assert.Equal(expectedCode, exception.Code);
        Assert.Equal(1, profile.Revision);
        Assert.Empty(profile.EquipmentAssignments);
    }

    [Fact]
    public void ModifierOverflowFailsBeforeMutation()
    {
        var profile = CreateProfile(baseAttack: int.MaxValue);
        var definition = CreateWeaponDefinition(ConfigurationVersionId, 1);

        var exception = Assert.Throws<PlayerAssetsDomainException>(() => profile.Equip(
            profile.Revision,
            CharacterId,
            WeaponSlot,
            WeaponItemId,
            definition));

        Assert.Equal(PlayerAssetsErrorCodes.InvalidModifier, exception.Code);
        Assert.Equal(1, profile.Revision);
        Assert.Empty(profile.EquipmentAssignments);
    }

    [Fact]
    public void AttributeAggregationDoesNotDependOnAssignmentEnumerationOrder()
    {
        var profile = CreateProfile(baseAttack: 0);
        profile.Equip(
            profile.Revision,
            CharacterId,
            WeaponSlot,
            WeaponItemId,
            CreateWeaponDefinition(ConfigurationVersionId, -int.MaxValue));
        profile.Equip(
            profile.Revision,
            CharacterId,
            ArmorSlot,
            ArmorItemId,
            CreateArmorDefinition(ConfigurationVersionId, int.MaxValue));
        profile.Equip(
            profile.Revision,
            CharacterId,
            EquipmentSlot.Create("off-hand"),
            SecondWeaponItemId,
            new EquipmentDefinition(
                "second-sword",
                EquipmentSlot.Create("off-hand"),
                ConfigurationVersionId,
                [AttributeValue.Create("attack", 1)]));

        var assignmentsField = typeof(PlayerAssetsProfile).GetField(
            "_equipmentAssignments",
            BindingFlags.Instance | BindingFlags.NonPublic);
        var assignments = Assert.IsType<List<EquipmentAssignment>>(assignmentsField?.GetValue(profile));
        assignments.Reverse();

        var attributes = profile.GetAttributeBreakdown(CharacterId);

        Assert.Equal(1, GetStat(attributes.EquipmentAttributes, "attack"));
        Assert.Equal(1, GetStat(attributes.EffectiveAttributes, "attack"));
    }

    [Fact]
    public void UnequipRejectsAProspectiveEffectiveAttributeOverflowWithoutMutation()
    {
        var profile = CreateProfile(baseAttack: int.MaxValue);
        profile.Equip(
            profile.Revision,
            CharacterId,
            WeaponSlot,
            WeaponItemId,
            CreateWeaponDefinition(ConfigurationVersionId, -1));
        profile.Equip(
            profile.Revision,
            CharacterId,
            ArmorSlot,
            ArmorItemId,
            CreateArmorDefinition(ConfigurationVersionId, 1));
        var revisionBeforeUnequip = profile.Revision;

        var exception = Assert.Throws<PlayerAssetsDomainException>(() => profile.Unequip(
            revisionBeforeUnequip,
            CharacterId,
            WeaponSlot));

        Assert.Equal(PlayerAssetsErrorCodes.InvalidModifier, exception.Code);
        Assert.Equal(revisionBeforeUnequip, profile.Revision);
        Assert.Equal(2, profile.EquipmentAssignments.Count);
        Assert.Contains(profile.EquipmentAssignments, assignment => assignment.Slot.Equals(WeaponSlot));
        Assert.Equal(int.MaxValue, GetStat(
            profile.GetAttributeBreakdown(CharacterId).EffectiveAttributes,
            "attack"));
    }

    [Fact]
    public void EmptyAndDuplicateModifierListsAreRejected()
    {
        var empty = Assert.Throws<PlayerAssetsDomainException>(() => new EquipmentDefinition(
            "training-sword",
            WeaponSlot,
            ConfigurationVersionId,
            Array.Empty<AttributeValue>()));
        var duplicate = Assert.Throws<PlayerAssetsDomainException>(() => new EquipmentDefinition(
            "training-sword",
            WeaponSlot,
            ConfigurationVersionId,
            [AttributeValue.Create("attack", 1), AttributeValue.Create("attack", 2)]));

        Assert.Equal(PlayerAssetsErrorCodes.InvalidModifier, empty.Code);
        Assert.Equal(PlayerAssetsErrorCodes.InvalidModifier, duplicate.Code);
    }

    [Fact]
    public void UnequippingEmptySlotDoesNotAdvanceRevision()
    {
        var profile = CreateProfile();

        var exception = Assert.Throws<PlayerAssetsDomainException>(() => profile.Unequip(
            profile.Revision,
            CharacterId,
            WeaponSlot));

        Assert.Equal(PlayerAssetsErrorCodes.SlotNotEquipped, exception.Code);
        Assert.Equal(1, profile.Revision);
    }

    private static PlayerAssetsProfile CreateProfile(int baseAttack = 10)
    {
        var character = new PlayerCharacter(
            CharacterId,
            [
                AttributeValue.Create("attack", baseAttack),
                AttributeValue.Create("health", 100)
            ]);

        return new PlayerAssetsProfile(
            PlayerId,
            character,
            [
                new OwnedEquipmentItem(WeaponItemId, "training-sword"),
                new OwnedEquipmentItem(SecondWeaponItemId, "second-sword"),
                new OwnedEquipmentItem(ArmorItemId, "training-armor")
            ]);
    }

    private static EquipmentDefinition CreateWeaponDefinition(
        Guid configurationVersionId,
        int attack)
    {
        return new EquipmentDefinition(
            "training-sword",
            WeaponSlot,
            configurationVersionId,
            [AttributeValue.Create("attack", attack)]);
    }

    private static EquipmentDefinition CreateArmorDefinition(
        Guid configurationVersionId,
        int attack)
    {
        return new EquipmentDefinition(
            "training-armor",
            ArmorSlot,
            configurationVersionId,
            [AttributeValue.Create("attack", attack)]);
    }

    private static int GetStat(IEnumerable<AttributeValue> attributes, string code)
    {
        return Assert.Single(attributes, attribute => attribute.Code.Value == code).Value.Value;
    }
}
