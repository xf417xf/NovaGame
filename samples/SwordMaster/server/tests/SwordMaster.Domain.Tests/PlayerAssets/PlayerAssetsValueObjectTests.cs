using SwordMaster.Domain.PlayerAssets;
using Xunit;

namespace SwordMaster.Domain.Tests.PlayerAssets;

public sealed class PlayerAssetsValueObjectTests
{
    [Fact]
    public void IdentifierValueObjectsRejectEmptyIdentifiers()
    {
        Assert.Throws<ArgumentException>(() => PlayerId.From(Guid.Empty));
        Assert.Throws<ArgumentException>(() => CharacterId.From(Guid.Empty));
        Assert.Throws<ArgumentException>(() => OwnedItemId.From(Guid.Empty));
    }

    [Fact]
    public void ConfigurableCodesAreTrimmedAndComparedByValue()
    {
        var firstSlot = EquipmentSlot.Create(" weapon ");
        var secondSlot = EquipmentSlot.Create("weapon");
        var firstStat = StatCode.Create("attack");
        var secondStat = StatCode.Create("attack");

        Assert.Equal("weapon", firstSlot.Value);
        Assert.Equal(firstSlot, secondSlot);
        Assert.Equal(firstStat, secondStat);
    }

    [Fact]
    public void DifferentValueObjectTypesAreNeverEqualForTheSameAtomicValue()
    {
        var identifier = Guid.Parse("11111111-1111-1111-1111-111111111111");
        object playerId = PlayerId.From(identifier);
        object characterId = CharacterId.From(identifier);
        object slot = EquipmentSlot.Create("attack");
        object statCode = StatCode.Create("attack");

        Assert.NotEqual(playerId, characterId);
        Assert.NotEqual(characterId, playerId);
        Assert.NotEqual(slot, statCode);
        Assert.NotEqual(statCode, slot);
    }

    [Theory]
    [InlineData("")]
    [InlineData("two words")]
    [InlineData("line\nbreak")]
    public void ConfigurableCodesRejectInvalidValues(string value)
    {
        Assert.Throws<ArgumentException>(() => EquipmentSlot.Create(value));
        Assert.Throws<ArgumentException>(() => StatCode.Create(value));
    }

    [Theory]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("/")]
    [InlineData("\\")]
    [InlineData("main/hand")]
    [InlineData("main\\hand")]
    public void EquipmentSlotsRejectUnsafeRouteSegments(string value)
    {
        Assert.Throws<ArgumentException>(() => EquipmentSlot.Create(value));
    }
}
