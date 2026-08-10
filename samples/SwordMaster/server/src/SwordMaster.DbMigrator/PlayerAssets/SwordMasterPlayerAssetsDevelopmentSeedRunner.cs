using Microsoft.Extensions.Options;
using SwordMaster.Application.PlayerAssets;
using SwordMaster.Domain.PlayerAssets;

namespace SwordMaster.DbMigrator.PlayerAssets;

public sealed class SwordMasterPlayerAssetsDevelopmentSeedRunner
{
    private readonly SwordMasterPlayerAssetsDevelopmentSeedOptions _options;
    private readonly IPlayerAssetsProfileRepository _profileRepository;
    private readonly IPlayerAssetsTransactionManager _transactionManager;

    public SwordMasterPlayerAssetsDevelopmentSeedRunner(
        IOptions<SwordMasterPlayerAssetsDevelopmentSeedOptions> options,
        IPlayerAssetsProfileRepository profileRepository,
        IPlayerAssetsTransactionManager transactionManager)
    {
        _options = options.Value;
        _profileRepository = profileRepository;
        _transactionManager = transactionManager;
    }

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        var playerId = PlayerId.From(_options.PlayerId);
        await using var transaction = await _transactionManager.BeginAsync(cancellationToken);
        var existingProfile = await _profileRepository.FindByPlayerIdForUpdateAsync(
            playerId,
            cancellationToken);
        if (existingProfile is null)
        {
            var profile = CreateProfile(playerId);
            await _profileRepository.AddAsync(profile, cancellationToken);
        }

        await transaction.CompleteAsync(cancellationToken);
    }

    private PlayerAssetsProfile CreateProfile(PlayerId playerId)
    {
        return new PlayerAssetsProfile(
            playerId,
            new PlayerCharacter(
                CharacterId.From(_options.CharacterId),
                [
                    AttributeValue.Create("attack", _options.BaseAttack),
                    AttributeValue.Create("health", _options.BaseHealth)
                ]),
            [
                new OwnedEquipmentItem(
                    OwnedItemId.From(_options.OwnedItemId),
                    _options.ConfiguredItemId)
            ]);
    }
}

public sealed class SwordMasterPlayerAssetsDevelopmentSeedOptions
{
    public const string SectionName = "SwordMaster:PlayerAssets:DevelopmentSeed";

    public Guid PlayerId { get; set; }

    public Guid CharacterId { get; set; }

    public Guid OwnedItemId { get; set; }

    public string ConfiguredItemId { get; set; } = string.Empty;

    public int BaseAttack { get; set; }

    public int BaseHealth { get; set; }

    public static bool IsValid(SwordMasterPlayerAssetsDevelopmentSeedOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.PlayerId == Guid.Empty ||
            options.CharacterId == Guid.Empty ||
            options.OwnedItemId == Guid.Empty ||
            string.IsNullOrWhiteSpace(options.ConfiguredItemId))
        {
            return false;
        }

        try
        {
            _ = new PlayerAssetsProfile(
                global::SwordMaster.Domain.PlayerAssets.PlayerId.From(options.PlayerId),
                new PlayerCharacter(
                    global::SwordMaster.Domain.PlayerAssets.CharacterId.From(options.CharacterId),
                    [
                        AttributeValue.Create("attack", options.BaseAttack),
                        AttributeValue.Create("health", options.BaseHealth)
                    ]),
                [
                    new OwnedEquipmentItem(
                        global::SwordMaster.Domain.PlayerAssets.OwnedItemId.From(options.OwnedItemId),
                        options.ConfiguredItemId)
                ]);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (PlayerAssetsDomainException)
        {
            return false;
        }
    }
}
