using System.Text.Json;

namespace NovaGame.Application.Contracts.Content.GameConfig;

public sealed record GameConfigSnapshotIntegrityDto(
    string Algorithm,
    string Canonicalization,
    string ContentHash);

public sealed record GameConfigSnapshotDto(
    string FormatVersion,
    string ConfigurationSet,
    string SchemaVersion,
    Guid ConfigurationVersion,
    DateTime CreatedAtUtc,
    GameConfigSnapshotIntegrityDto Integrity,
    JsonElement Content);
