using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NovaCore.BuildingBlocks.Authorization;
using NovaCore.BuildingBlocks.UnitOfWork;
using NovaCore.Shared.Results;
using NovaGame.Application.Contracts.Content.GameConfig;
using NovaGame.Domain.Shared.Content.GameConfig;

namespace NovaGame.Api.Content.GameConfig;

[ApiController]
[Route("api/game-config")]
[NovaAuthorize(GameConfigPermissions.Access)]
[UnitOfWork(IsDisabled = true)]
public sealed class GameConfigController : ControllerBase
{
    private const string SnapshotContentType = "application/json";

    private readonly IGameConfigAppService _gameConfigAppService;

    public GameConfigController(IGameConfigAppService gameConfigAppService)
    {
        _gameConfigAppService = gameConfigAppService;
    }

    [HttpGet("drafts")]
    [ProducesResponseType(typeof(ApiResponse<GameConfigPageDto<GameConfigDraftSummaryDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<ApiResponse<GameConfigPageDto<GameConfigDraftSummaryDto>>>> ListDraftsAsync(
        [FromQuery] ListGameConfigDraftsRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _gameConfigAppService.ListDraftsAsync(request, cancellationToken);
        return Ok(ApiResponse<GameConfigPageDto<GameConfigDraftSummaryDto>>.Success(result));
    }

    [HttpPost("drafts")]
    [NovaAuthorize(GameConfigPermissions.ManageDrafts)]
    [ProducesResponseType(typeof(ApiResponse<GameConfigDraftDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<ApiResponse<GameConfigDraftDto>>> CreateDraftAsync(
        CreateGameConfigDraftRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _gameConfigAppService.CreateDraftAsync(request, cancellationToken);
        return Ok(ApiResponse<GameConfigDraftDto>.Success(result));
    }

    [HttpGet("drafts/{draftId:guid}")]
    [ProducesResponseType(typeof(ApiResponse<GameConfigDraftDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<ApiResponse<GameConfigDraftDto>>> GetDraftAsync(
        Guid draftId,
        CancellationToken cancellationToken)
    {
        var result = await _gameConfigAppService.GetDraftAsync(draftId, cancellationToken);
        return Ok(ApiResponse<GameConfigDraftDto>.Success(result));
    }

    [HttpPut("drafts/{draftId:guid}")]
    [NovaAuthorize(GameConfigPermissions.ManageDrafts)]
    [ProducesResponseType(typeof(ApiResponse<GameConfigDraftDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<ApiResponse<GameConfigDraftDto>>> UpdateDraftAsync(
        Guid draftId,
        UpdateGameConfigDraftRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _gameConfigAppService.UpdateDraftAsync(draftId, request, cancellationToken);
        return Ok(ApiResponse<GameConfigDraftDto>.Success(result));
    }

    [HttpPost("drafts/{draftId:guid}/validate")]
    [NovaAuthorize(GameConfigPermissions.ManageDrafts)]
    [ProducesResponseType(typeof(ApiResponse<GameConfigValidationResultDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<ApiResponse<GameConfigValidationResultDto>>> ValidateDraftAsync(
        Guid draftId,
        ValidateGameConfigDraftRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _gameConfigAppService.ValidateDraftAsync(draftId, request, cancellationToken);
        return Ok(ApiResponse<GameConfigValidationResultDto>.Success(result));
    }

    [HttpPost("drafts/{draftId:guid}/publish")]
    [NovaAuthorize(GameConfigPermissions.Publish)]
    [ProducesResponseType(typeof(ApiResponse<GameConfigVersionDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<ApiResponse<GameConfigVersionDto>>> PublishDraftAsync(
        Guid draftId,
        PublishGameConfigDraftRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _gameConfigAppService.PublishDraftAsync(draftId, request, cancellationToken);
        return Ok(ApiResponse<GameConfigVersionDto>.Success(result));
    }

    [HttpGet("versions")]
    [ProducesResponseType(typeof(ApiResponse<GameConfigPageDto<GameConfigVersionSummaryDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<ApiResponse<GameConfigPageDto<GameConfigVersionSummaryDto>>>> ListVersionsAsync(
        [FromQuery] ListGameConfigVersionsRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _gameConfigAppService.ListVersionsAsync(request, cancellationToken);
        return Ok(ApiResponse<GameConfigPageDto<GameConfigVersionSummaryDto>>.Success(result));
    }

    [HttpGet("versions/{versionId:guid}")]
    [ProducesResponseType(typeof(ApiResponse<GameConfigVersionDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<ApiResponse<GameConfigVersionDto>>> GetVersionAsync(
        Guid versionId,
        CancellationToken cancellationToken)
    {
        var result = await _gameConfigAppService.GetVersionAsync(versionId, cancellationToken);
        return Ok(ApiResponse<GameConfigVersionDto>.Success(result));
    }

    [HttpGet("versions/{versionId:guid}/snapshot")]
    [Produces(SnapshotContentType)]
    [ProducesResponseType(typeof(GameConfigSnapshotDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status304NotModified)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> DownloadVersionSnapshotAsync(
        Guid versionId,
        CancellationToken cancellationToken)
    {
        var snapshot = await _gameConfigAppService.DownloadVersionSnapshotAsync(
            versionId,
            cancellationToken);
        return CreateSnapshotResult(snapshot, immutable: true, download: true);
    }

    [HttpGet("environments/{name}")]
    [ProducesResponseType(typeof(ApiResponse<GameConfigEnvironmentDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<ApiResponse<GameConfigEnvironmentDto>>> GetEnvironmentAsync(
        [RegularExpression(GameConfigContractLimits.EnvironmentNamePattern)] string name,
        [FromQuery] GameConfigEnvironmentQuery query,
        CancellationToken cancellationToken)
    {
        var result = await _gameConfigAppService.GetEnvironmentAsync(
            name,
            query.ConfigurationSet,
            cancellationToken);
        return Ok(ApiResponse<GameConfigEnvironmentDto>.Success(result));
    }

    [HttpPost("environments/{name}/activate")]
    [NovaAuthorize(GameConfigPermissions.Activate)]
    [ProducesResponseType(typeof(ApiResponse<GameConfigEnvironmentDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<ApiResponse<GameConfigEnvironmentDto>>> ActivateVersionAsync(
        [RegularExpression(GameConfigContractLimits.EnvironmentNamePattern)] string name,
        ActivateGameConfigVersionRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _gameConfigAppService.ActivateVersionAsync(name, request, cancellationToken);
        return Ok(ApiResponse<GameConfigEnvironmentDto>.Success(result));
    }

    [HttpPost("environments/{name}/rollback")]
    [NovaAuthorize(GameConfigPermissions.Activate)]
    [ProducesResponseType(typeof(ApiResponse<GameConfigEnvironmentDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<ApiResponse<GameConfigEnvironmentDto>>> RollbackEnvironmentAsync(
        [RegularExpression(GameConfigContractLimits.EnvironmentNamePattern)] string name,
        RollbackGameConfigEnvironmentRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _gameConfigAppService.RollbackEnvironmentAsync(name, request, cancellationToken);
        return Ok(ApiResponse<GameConfigEnvironmentDto>.Success(result));
    }

    [AllowAnonymous]
    [HttpGet("environments/{name}/snapshot")]
    [Produces(SnapshotContentType)]
    [ProducesResponseType(typeof(GameConfigSnapshotDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status304NotModified)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> DownloadActiveSnapshotAsync(
        [RegularExpression(GameConfigContractLimits.EnvironmentNamePattern)] string name,
        [FromQuery] GameConfigEnvironmentQuery query,
        CancellationToken cancellationToken)
    {
        var snapshot = await _gameConfigAppService.DownloadActiveSnapshotAsync(
            name,
            query.ConfigurationSet,
            cancellationToken);
        return CreateSnapshotResult(snapshot, immutable: false, download: false);
    }

    private IActionResult CreateSnapshotResult(
        GameConfigSnapshotDownload snapshot,
        bool immutable,
        bool download)
    {
        var entityTag = $"\"{snapshot.SnapshotHash}\"";
        Response.Headers.ETag = entityTag;
        Response.Headers.CacheControl = immutable
            ? "public, max-age=31536000, immutable"
            : "public, max-age=0, must-revalidate";

        if (MatchesIfNoneMatch(entityTag))
        {
            return StatusCode(StatusCodes.Status304NotModified);
        }

        if (!download)
        {
            return File(snapshot.Content, SnapshotContentType);
        }

        var fileName = Path.GetFileName(snapshot.FileName);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            fileName = "game-config-snapshot.json";
        }

        return File(snapshot.Content, SnapshotContentType, fileName);
    }

    private bool MatchesIfNoneMatch(string entityTag)
    {
        var candidates = Request.Headers.IfNoneMatch
            .ToString()
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return candidates.Any(candidate => candidate == "*" || candidate == entityTag);
    }
}
