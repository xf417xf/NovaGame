using SwordMaster.Domain.PlayerAssets;
using Xunit;

namespace SwordMaster.Domain.Tests.PlayerAssets;

public sealed class PlayerAssetCommandReceiptTests
{
    private static readonly PlayerId TestPlayerId = PlayerId.From(
        Guid.Parse("11111111-1111-1111-1111-111111111111"));

    [Fact]
    public void ReceiptCapturesRetryIdentityAndUtcResult()
    {
        var idempotencyKey = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var completedAtUtc = new DateTime(2026, 8, 1, 1, 2, 3, DateTimeKind.Utc);

        var receipt = new PlayerAssetCommandReceipt(
            TestPlayerId,
            idempotencyKey,
            "equip:request-hash",
            2,
            completedAtUtc);

        Assert.Equal(TestPlayerId, receipt.PlayerId);
        Assert.Equal(idempotencyKey, receipt.IdempotencyKey);
        Assert.Equal(2, receipt.ResultingRevision);
        Assert.Equal(completedAtUtc, receipt.CompletedAtUtc);
        Assert.True(receipt.MatchesFingerprint("equip:request-hash"));
        Assert.False(receipt.MatchesFingerprint("unequip:request-hash"));
    }

    [Fact]
    public void ReceiptRejectsInvalidPersistenceInputs()
    {
        var nowUtc = DateTime.SpecifyKind(DateTime.UnixEpoch, DateTimeKind.Utc);

        Assert.Throws<ArgumentException>(() => new PlayerAssetCommandReceipt(
            TestPlayerId,
            Guid.Empty,
            "fingerprint",
            2,
            nowUtc));
        Assert.Throws<ArgumentException>(() => new PlayerAssetCommandReceipt(
            TestPlayerId,
            Guid.NewGuid(),
            "",
            2,
            nowUtc));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PlayerAssetCommandReceipt(
            TestPlayerId,
            Guid.NewGuid(),
            "fingerprint",
            0,
            nowUtc));
        Assert.Throws<ArgumentException>(() => new PlayerAssetCommandReceipt(
            TestPlayerId,
            Guid.NewGuid(),
            "fingerprint",
            2,
            DateTime.Now));
    }
}
