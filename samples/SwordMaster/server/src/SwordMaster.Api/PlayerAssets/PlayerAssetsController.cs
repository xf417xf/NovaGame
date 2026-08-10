using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
using NovaCore.BuildingBlocks.UnitOfWork;
using NovaCore.Shared.Results;
using SwordMaster.Application.Contracts.PlayerAssets;

namespace SwordMaster.Api.PlayerAssets;

[ApiController]
[Route("api/player-assets")]
[Authorize(AuthenticationSchemes = SwordMasterPlayerAuthentication.Scheme)]
[UnitOfWork(IsDisabled = true)]
public sealed class PlayerAssetsController : ControllerBase
{
    public const string IdempotencyKeyHeaderName = "Idempotency-Key";
    public const string IfMatchHeaderName = "If-Match";

    private readonly PlayerAssetsOperationLogger _operationLogger;
    private readonly IPlayerAssetsAppService _playerAssetsAppService;

    public PlayerAssetsController(
        IPlayerAssetsAppService playerAssetsAppService,
        PlayerAssetsOperationLogger operationLogger)
    {
        _playerAssetsAppService = playerAssetsAppService;
        _operationLogger = operationLogger;
    }

    [HttpPut("characters/{characterId:guid}/equipment/{slot}")]
    [ProducesResponseType(typeof(ApiResponse<PlayerAssetsMutationResultDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<ApiResponse<PlayerAssetsMutationResultDto>>> EquipAsync(
        Guid characterId,
        string slot,
        EquipEquipmentRequest request,
        CancellationToken cancellationToken)
    {
        var mutationContext = ReadMutationContext();
        var result = await _playerAssetsAppService.EquipAsync(
            characterId,
            slot,
            request,
            mutationContext,
            cancellationToken);
        SetRevisionEntityTag(result.Revision);
        _operationLogger.LogSucceeded(
            HttpContext,
            PlayerAssetsOperationLogger.EquipCommandType,
            characterId,
            result.Revision);
        return Ok(ApiResponse<PlayerAssetsMutationResultDto>.Success(result));
    }

    [HttpDelete("characters/{characterId:guid}/equipment/{slot}")]
    [ProducesResponseType(typeof(ApiResponse<PlayerAssetsMutationResultDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<ApiResponse<PlayerAssetsMutationResultDto>>> UnequipAsync(
        Guid characterId,
        string slot,
        CancellationToken cancellationToken)
    {
        var mutationContext = ReadMutationContext();
        var result = await _playerAssetsAppService.UnequipAsync(
            characterId,
            slot,
            mutationContext,
            cancellationToken);
        SetRevisionEntityTag(result.Revision);
        _operationLogger.LogSucceeded(
            HttpContext,
            PlayerAssetsOperationLogger.UnequipCommandType,
            characterId,
            result.Revision);
        return Ok(ApiResponse<PlayerAssetsMutationResultDto>.Success(result));
    }

    [HttpGet("characters/{characterId:guid}/attributes")]
    [ProducesResponseType(typeof(ApiResponse<CharacterAttributesDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<ApiResponse<CharacterAttributesDto>>> GetAttributesAsync(
        Guid characterId,
        CancellationToken cancellationToken)
    {
        var result = await _playerAssetsAppService.GetAttributesAsync(
            characterId,
            cancellationToken);
        SetRevisionEntityTag(result.Revision);
        _operationLogger.LogSucceeded(
            HttpContext,
            PlayerAssetsOperationLogger.GetAttributesCommandType,
            characterId,
            result.Revision);
        return Ok(ApiResponse<CharacterAttributesDto>.Success(result));
    }

    private PlayerAssetsMutationContext ReadMutationContext()
    {
        return new PlayerAssetsMutationContext(
            ReadExpectedRevision(),
            ReadIdempotencyKey());
    }

    private long ReadExpectedRevision()
    {
        try
        {
            var entityTags = Request.GetTypedHeaders().IfMatch;
            if (entityTags is null || entityTags.Count != 1)
            {
                throw InvalidHeader(
                    IfMatchHeaderName,
                    "Exactly one strong revision ETag is required.");
            }

            var entityTag = entityTags[0];
            var tag = entityTag.Tag.Value;
            if (entityTag.IsWeak ||
                string.Equals(tag, "*", StringComparison.Ordinal) ||
                string.IsNullOrEmpty(tag) ||
                tag.Length < 3 ||
                tag[0] != '"' ||
                tag[^1] != '"')
            {
                throw InvalidHeader(
                    IfMatchHeaderName,
                    "The header must contain one strong quoted positive decimal revision ETag.");
            }

            var revisionText = tag.AsSpan(1, tag.Length - 2);
            if (!long.TryParse(
                    revisionText,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var revision) ||
                revision <= 0 ||
                !revisionText.SequenceEqual(
                    revision.ToString(CultureInfo.InvariantCulture).AsSpan()))
            {
                throw InvalidHeader(
                    IfMatchHeaderName,
                    "The header must contain one strong quoted positive decimal revision ETag.");
            }

            return revision;
        }
        catch (FormatException)
        {
            throw InvalidHeader(
                IfMatchHeaderName,
                "The header must contain one strong quoted positive decimal revision ETag.");
        }
    }

    private Guid ReadIdempotencyKey()
    {
        if (!Request.Headers.TryGetValue(IdempotencyKeyHeaderName, out var values) ||
            values.Count != 1 ||
            !Guid.TryParseExact(values[0], "D", out var idempotencyKey) ||
            idempotencyKey == Guid.Empty)
        {
            throw InvalidHeader(
                IdempotencyKeyHeaderName,
                "Exactly one non-empty GUID idempotency key is required.");
        }

        return idempotencyKey;
    }

    private void SetRevisionEntityTag(long revision)
    {
        Response.GetTypedHeaders().ETag = new EntityTagHeaderValue(
            $"\"{revision.ToString(CultureInfo.InvariantCulture)}\"");
    }

    private static PlayerAssetsException InvalidHeader(string headerName, string message)
    {
        return new PlayerAssetsException(
            PlayerAssetsFailureKind.BadRequest,
            PlayerAssetsErrorCodes.InvalidRequest,
            $"{headerName}: {message}");
    }
}
