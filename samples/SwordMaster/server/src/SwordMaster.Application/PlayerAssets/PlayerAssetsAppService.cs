using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using SwordMaster.Application.Contracts.PlayerAssets;
using SwordMaster.Domain.PlayerAssets;
using ContractErrorCodes = SwordMaster.Application.Contracts.PlayerAssets.PlayerAssetsErrorCodes;
using DomainErrorCodes = SwordMaster.Domain.PlayerAssets.PlayerAssetsErrorCodes;

namespace SwordMaster.Application.PlayerAssets;

public sealed class PlayerAssetsAppService : IPlayerAssetsAppService
{
    private readonly ICurrentPlayerContext _currentPlayer;
    private readonly IEquipmentDefinitionProvider _equipmentDefinitionProvider;
    private readonly IPlayerAssetsProfileRepository _profileRepository;
    private readonly IPlayerAssetCommandReceiptRepository _receiptRepository;
    private readonly IPlayerAssetsTransactionManager _transactionManager;
    private readonly TimeProvider _timeProvider;

    public PlayerAssetsAppService(
        ICurrentPlayerContext currentPlayer,
        IEquipmentDefinitionProvider equipmentDefinitionProvider,
        IPlayerAssetsProfileRepository profileRepository,
        IPlayerAssetCommandReceiptRepository receiptRepository,
        IPlayerAssetsTransactionManager transactionManager,
        TimeProvider timeProvider)
    {
        _currentPlayer = currentPlayer;
        _equipmentDefinitionProvider = equipmentDefinitionProvider;
        _profileRepository = profileRepository;
        _receiptRepository = receiptRepository;
        _transactionManager = transactionManager;
        _timeProvider = timeProvider;
    }

    public async Task<PlayerAssetsMutationResultDto> EquipAsync(
        Guid characterId,
        string slot,
        EquipEquipmentRequest request,
        PlayerAssetsMutationContext mutationContext,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(mutationContext);
        var playerId = RequireCurrentPlayer();
        var parsedCharacterId = ParseCharacterId(characterId);
        var parsedSlot = ParseSlot(slot);
        var ownedItemId = ParseOwnedItemId(request.OwnedItemId);
        EnsureMutationContext(mutationContext);
        var fingerprint = CreateFingerprint(
            "equip",
            parsedCharacterId,
            parsedSlot,
            mutationContext.ExpectedRevision,
            ownedItemId);

        var replay = await FindReplayAsync(
            playerId,
            mutationContext.IdempotencyKey,
            fingerprint,
            forUpdate: false,
            cancellationToken);
        if (replay is not null)
        {
            return replay;
        }

        var readProfile = await _profileRepository.FindByPlayerIdAsync(playerId, cancellationToken);
        if (readProfile is null)
        {
            throw OwnedItemNotFound();
        }

        var ownedItem = TranslateDomain(() => readProfile.GetOwnedItem(ownedItemId));
        var resolution = await TranslateDomainAsync(() =>
            _equipmentDefinitionProvider.ResolveActiveAsync(
                ownedItem.ConfiguredItemId,
                cancellationToken));
        var definition = resolution.Status switch
        {
            EquipmentDefinitionResolutionStatus.Found when resolution.Definition is not null =>
                resolution.Definition,
            EquipmentDefinitionResolutionStatus.Missing => throw new PlayerAssetsException(
                PlayerAssetsFailureKind.UnprocessableEntity,
                ContractErrorCodes.ItemNotEquipable,
                "The selected owned item has no active equipment definition."),
            EquipmentDefinitionResolutionStatus.Invalid => throw new PlayerAssetsException(
                PlayerAssetsFailureKind.UnprocessableEntity,
                ContractErrorCodes.InvalidModifier,
                "The active equipment definition is invalid."),
            _ => throw new InvalidOperationException(
                "The equipment-definition provider returned an inconsistent resolution.")
        };

        await using var transaction = await _transactionManager.BeginAsync(cancellationToken);
        var profile = await _profileRepository.FindByPlayerIdForUpdateAsync(
            playerId,
            cancellationToken);
        if (profile is null)
        {
            throw OwnedItemNotFound();
        }

        replay = await FindReplayAsync(
            playerId,
            mutationContext.IdempotencyKey,
            fingerprint,
            forUpdate: true,
            cancellationToken);
        if (replay is not null)
        {
            await transaction.CompleteAsync(cancellationToken);
            return replay;
        }

        TranslateDomain(() => profile.Equip(
            mutationContext.ExpectedRevision,
            parsedCharacterId,
            parsedSlot,
            ownedItemId,
            definition));
        var result = new PlayerAssetsMutationResultDto(profile.Revision);
        await AddReceiptAsync(
            playerId,
            mutationContext.IdempotencyKey,
            fingerprint,
            result,
            cancellationToken);
        await transaction.CompleteAsync(cancellationToken);
        return result;
    }

    public async Task<PlayerAssetsMutationResultDto> UnequipAsync(
        Guid characterId,
        string slot,
        PlayerAssetsMutationContext mutationContext,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mutationContext);
        var playerId = RequireCurrentPlayer();
        var parsedCharacterId = ParseCharacterId(characterId);
        var parsedSlot = ParseSlot(slot);
        EnsureMutationContext(mutationContext);
        var fingerprint = CreateFingerprint(
            "unequip",
            parsedCharacterId,
            parsedSlot,
            mutationContext.ExpectedRevision);

        var replay = await FindReplayAsync(
            playerId,
            mutationContext.IdempotencyKey,
            fingerprint,
            forUpdate: false,
            cancellationToken);
        if (replay is not null)
        {
            return replay;
        }

        await using var transaction = await _transactionManager.BeginAsync(cancellationToken);
        var profile = await _profileRepository.FindByPlayerIdForUpdateAsync(
            playerId,
            cancellationToken);
        if (profile is null)
        {
            throw CharacterNotFound();
        }

        replay = await FindReplayAsync(
            playerId,
            mutationContext.IdempotencyKey,
            fingerprint,
            forUpdate: true,
            cancellationToken);
        if (replay is not null)
        {
            await transaction.CompleteAsync(cancellationToken);
            return replay;
        }

        TranslateDomain(() => profile.Unequip(
            mutationContext.ExpectedRevision,
            parsedCharacterId,
            parsedSlot));
        var result = new PlayerAssetsMutationResultDto(profile.Revision);
        await AddReceiptAsync(
            playerId,
            mutationContext.IdempotencyKey,
            fingerprint,
            result,
            cancellationToken);
        await transaction.CompleteAsync(cancellationToken);
        return result;
    }

    public async Task<CharacterAttributesDto> GetAttributesAsync(
        Guid characterId,
        CancellationToken cancellationToken = default)
    {
        var playerId = RequireCurrentPlayer();
        var parsedCharacterId = ParseCharacterId(characterId);
        var profile = await _profileRepository.FindByPlayerIdAsync(playerId, cancellationToken);
        if (profile is null)
        {
            throw CharacterNotFound();
        }

        var breakdown = TranslateDomain(() => profile.GetAttributeBreakdown(parsedCharacterId));
        return MapAttributes(profile, parsedCharacterId, breakdown);
    }

    private PlayerId RequireCurrentPlayer()
    {
        if (!_currentPlayer.IsAuthenticated ||
            !_currentPlayer.PlayerId.HasValue ||
            _currentPlayer.PlayerId.Value == Guid.Empty)
        {
            throw new PlayerAssetsException(
                PlayerAssetsFailureKind.Unauthorized,
                ContractErrorCodes.UnauthorizedPlayer,
                "An authenticated SwordMaster player is required.");
        }

        return PlayerId.From(_currentPlayer.PlayerId.Value);
    }

    private async Task<PlayerAssetsMutationResultDto?> FindReplayAsync(
        PlayerId playerId,
        Guid idempotencyKey,
        string fingerprint,
        bool forUpdate,
        CancellationToken cancellationToken)
    {
        var receipt = forUpdate
            ? await _receiptRepository.FindForUpdateAsync(
                playerId,
                idempotencyKey,
                cancellationToken)
            : await _receiptRepository.FindAsync(
                playerId,
                idempotencyKey,
                cancellationToken);
        if (receipt is null)
        {
            return null;
        }

        if (!receipt.MatchesFingerprint(fingerprint))
        {
            throw new PlayerAssetsException(
                PlayerAssetsFailureKind.Conflict,
                ContractErrorCodes.IdempotencyConflict,
                "The idempotency key was already used for a different player-assets request.");
        }

        return new PlayerAssetsMutationResultDto(receipt.ResultingRevision);
    }

    private async Task AddReceiptAsync(
        PlayerId playerId,
        Guid idempotencyKey,
        string fingerprint,
        PlayerAssetsMutationResultDto result,
        CancellationToken cancellationToken)
    {
        var receipt = new PlayerAssetCommandReceipt(
            playerId,
            idempotencyKey,
            fingerprint,
            result.Revision,
            _timeProvider.GetUtcNow().UtcDateTime);
        await _receiptRepository.AddAsync(receipt, cancellationToken);
    }

    private static CharacterAttributesDto MapAttributes(
        PlayerAssetsProfile profile,
        CharacterId characterId,
        CharacterAttributeBreakdown breakdown)
    {
        var baseAttributes = ToAttributeDictionary(breakdown.BaseAttributes);
        var equipmentAttributes = ToAttributeDictionary(breakdown.EquipmentAttributes);
        var effectiveAttributes = ToAttributeDictionary(breakdown.EffectiveAttributes);
        var statCodes = baseAttributes.Keys
            .Concat(equipmentAttributes.Keys)
            .Concat(effectiveAttributes.Keys)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(code => code, StringComparer.Ordinal);
        var attributes = statCodes
            .Select(code => new CharacterAttributeDto(
                code,
                baseAttributes.GetValueOrDefault(code),
                equipmentAttributes.GetValueOrDefault(code),
                effectiveAttributes.GetValueOrDefault(code)))
            .ToArray();
        var assignments = profile.EquipmentAssignments
            .Where(assignment => assignment.CharacterId.Equals(characterId))
            .OrderBy(assignment => assignment.Slot.Value, StringComparer.Ordinal)
            .Select(assignment => new EquipmentAssignmentDto(
                assignment.Slot.Value,
                assignment.OwnedItemId.Value,
                assignment.ConfiguredItemId,
                assignment.ConfigurationVersionId,
                assignment.Modifiers
                    .OrderBy(modifier => modifier.Code.Value, StringComparer.Ordinal)
                    .Select(modifier => new EquipmentModifierDto(
                        modifier.Code.Value,
                        modifier.Value.Value))
                    .ToArray()))
            .ToArray();

        return new CharacterAttributesDto(
            characterId.Value,
            profile.Revision,
            attributes,
            assignments);
    }

    private static IReadOnlyDictionary<string, int> ToAttributeDictionary(
        IEnumerable<AttributeValue> attributes)
    {
        return attributes.ToDictionary(
            attribute => attribute.Code.Value,
            attribute => attribute.Value.Value,
            StringComparer.Ordinal);
    }

    private static CharacterId ParseCharacterId(Guid characterId)
    {
        try
        {
            return CharacterId.From(characterId);
        }
        catch (ArgumentException exception)
        {
            throw InvalidRequest(exception.Message);
        }
    }

    private static OwnedItemId ParseOwnedItemId(Guid ownedItemId)
    {
        try
        {
            return OwnedItemId.From(ownedItemId);
        }
        catch (ArgumentException exception)
        {
            throw InvalidRequest(exception.Message);
        }
    }

    private static EquipmentSlot ParseSlot(string slot)
    {
        try
        {
            return EquipmentSlot.Create(slot);
        }
        catch (ArgumentException exception)
        {
            throw InvalidRequest(exception.Message);
        }
    }

    private static void EnsureMutationContext(PlayerAssetsMutationContext mutationContext)
    {
        if (mutationContext.ExpectedRevision <= 0)
        {
            throw InvalidRequest("Expected revision must be positive.");
        }

        if (mutationContext.IdempotencyKey == Guid.Empty)
        {
            throw InvalidRequest("An idempotency key is required.");
        }
    }

    private static string CreateFingerprint(
        string command,
        CharacterId characterId,
        EquipmentSlot slot,
        long expectedRevision,
        OwnedItemId? ownedItemId = null)
    {
        var canonical = string.Join(
            '|',
            command,
            characterId.Value.ToString("D"),
            slot.Value,
            expectedRevision.ToString(CultureInfo.InvariantCulture),
            ownedItemId?.Value.ToString("D") ?? string.Empty);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    private static T TranslateDomain<T>(Func<T> action)
    {
        try
        {
            return action();
        }
        catch (PlayerAssetsDomainException exception)
        {
            throw TranslateDomainException(exception);
        }
    }

    private static async Task<T> TranslateDomainAsync<T>(Func<Task<T>> action)
    {
        try
        {
            return await action();
        }
        catch (PlayerAssetsDomainException exception)
        {
            throw TranslateDomainException(exception);
        }
    }

    private static PlayerAssetsException TranslateDomainException(
        PlayerAssetsDomainException exception)
    {
        return new PlayerAssetsException(
            GetFailureKind(exception.Code),
            exception.Code,
            exception.Message);
    }

    private static PlayerAssetsFailureKind GetFailureKind(string code)
    {
        return code switch
        {
            DomainErrorCodes.CharacterNotFound or
            DomainErrorCodes.OwnedItemNotFound or
            DomainErrorCodes.SlotNotEquipped => PlayerAssetsFailureKind.NotFound,
            DomainErrorCodes.ItemAlreadyEquipped or
            DomainErrorCodes.SlotOccupied or
            DomainErrorCodes.ConcurrencyConflict or
            DomainErrorCodes.IdempotencyConflict => PlayerAssetsFailureKind.Conflict,
            DomainErrorCodes.ItemNotEquipable or
            DomainErrorCodes.SlotMismatch or
            DomainErrorCodes.InvalidModifier => PlayerAssetsFailureKind.UnprocessableEntity,
            _ => PlayerAssetsFailureKind.InternalError
        };
    }

    private static PlayerAssetsException InvalidRequest(string message)
    {
        return new PlayerAssetsException(
            PlayerAssetsFailureKind.BadRequest,
            ContractErrorCodes.InvalidRequest,
            message);
    }

    private static PlayerAssetsException CharacterNotFound()
    {
        return new PlayerAssetsException(
            PlayerAssetsFailureKind.NotFound,
            ContractErrorCodes.CharacterNotFound,
            "The requested character was not found for the current player.");
    }

    private static PlayerAssetsException OwnedItemNotFound()
    {
        return new PlayerAssetsException(
            PlayerAssetsFailureKind.NotFound,
            ContractErrorCodes.OwnedItemNotFound,
            "The requested owned equipment item was not found for the current player.");
    }
}
