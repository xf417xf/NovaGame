using System.Globalization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
using NovaCore.BuildingBlocks.Authorization;
using NovaCore.BuildingBlocks.UnitOfWork;
using NovaCore.Shared.Results;
using SwordMaster.Application.Contracts.PlayerAssets;

namespace SwordMaster.Api.PlayerAssets;

[ApiController]
[Route("api/swordmaster/operations/player-assets/{playerId}/equipment-grants")]
[NovaAuthorize(PlayerEquipmentGrantPermissions.Manage)]
[UnitOfWork(IsDisabled = true)]
public sealed class PlayerEquipmentGrantsController : ControllerBase
{
    public const string IdempotencyKeyHeaderName = "Idempotency-Key";
    public const string IfMatchHeaderName = "If-Match";

    private readonly IPlayerEquipmentGrantAppService _appService;
    private readonly PlayerEquipmentGrantOperationLogger _operationLogger;

    public PlayerEquipmentGrantsController(
        IPlayerEquipmentGrantAppService appService,
        PlayerEquipmentGrantOperationLogger operationLogger)
    {
        _appService = appService;
        _operationLogger = operationLogger;
    }

    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<PlayerEquipmentGrantPageDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<ApiResponse<PlayerEquipmentGrantPageDto>>> GetPageAsync(
        Guid playerId,
        int page = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await _appService.GetPageAsync(
            playerId,
            page,
            pageSize,
            cancellationToken);
        SetRevisionEntityTag(result.Revision);
        _operationLogger.LogSucceeded(
            HttpContext,
            PlayerEquipmentGrantOperationLogger.GetPageCommandType,
            playerId,
            grantId: null,
            result.Revision);
        return Ok(ApiResponse<PlayerEquipmentGrantPageDto>.Success(result));
    }

    [HttpPost]
    [ProducesResponseType(typeof(ApiResponse<PlayerEquipmentGrantMutationResultDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<ApiResponse<PlayerEquipmentGrantMutationResultDto>>> GrantAsync(
        Guid playerId,
        GrantPlayerEquipmentRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _appService.GrantAsync(
            playerId,
            request,
            ReadMutationContext(),
            cancellationToken);
        SetRevisionEntityTag(result.ResultingRevision);
        _operationLogger.LogSucceeded(
            HttpContext,
            PlayerEquipmentGrantOperationLogger.GrantCommandType,
            playerId,
            result.GrantId,
            result.ResultingRevision);
        return Ok(ApiResponse<PlayerEquipmentGrantMutationResultDto>.Success(result));
    }

    [HttpPost("{grantId}/rollback")]
    [ProducesResponseType(typeof(ApiResponse<PlayerEquipmentGrantMutationResultDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<ApiResponse<PlayerEquipmentGrantMutationResultDto>>> RollbackAsync(
        Guid playerId,
        Guid grantId,
        RollbackPlayerEquipmentGrantRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _appService.RollbackAsync(
            playerId,
            grantId,
            request,
            ReadMutationContext(),
            cancellationToken);
        SetRevisionEntityTag(result.ResultingRevision);
        _operationLogger.LogSucceeded(
            HttpContext,
            PlayerEquipmentGrantOperationLogger.RollbackCommandType,
            playerId,
            result.GrantId,
            result.ResultingRevision);
        return Ok(ApiResponse<PlayerEquipmentGrantMutationResultDto>.Success(result));
    }

    private PlayerEquipmentGrantMutationContext ReadMutationContext()
    {
        return new PlayerEquipmentGrantMutationContext(
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
            values.Count != 1)
        {
            throw InvalidHeader(
                IdempotencyKeyHeaderName,
                "Exactly one non-empty canonical lowercase D-format GUID is required.");
        }

        var value = values[0];
        if (!Guid.TryParseExact(value, "D", out var idempotencyKey) ||
            idempotencyKey == Guid.Empty ||
            !string.Equals(
                value,
                idempotencyKey.ToString("D"),
                StringComparison.Ordinal))
        {
            throw InvalidHeader(
                IdempotencyKeyHeaderName,
                "Exactly one non-empty canonical lowercase D-format GUID is required.");
        }

        return idempotencyKey;
    }

    private void SetRevisionEntityTag(long revision)
    {
        Response.GetTypedHeaders().ETag = new EntityTagHeaderValue(
            $"\"{revision.ToString(CultureInfo.InvariantCulture)}\"");
    }

    private static PlayerEquipmentGrantException InvalidHeader(
        string headerName,
        string message)
    {
        return new PlayerEquipmentGrantException(
            PlayerEquipmentGrantFailureKind.BadRequest,
            PlayerEquipmentGrantErrorCodes.InvalidRequest,
            $"{headerName}: {message}");
    }
}
