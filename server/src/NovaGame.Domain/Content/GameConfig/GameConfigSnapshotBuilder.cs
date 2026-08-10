using System.Buffers;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NovaGame.Domain.Shared.Content.GameConfig;

namespace NovaGame.Domain.Content.GameConfig;

public sealed class GameConfigSnapshot
{
    internal GameConfigSnapshot(
        Guid configurationVersionId,
        string configurationSetKey,
        string schemaVersion,
        DateTime createdAtUtc,
        string contentHash,
        string snapshotHash,
        string json)
    {
        ConfigurationVersionId = configurationVersionId;
        ConfigurationSetKey = configurationSetKey;
        SchemaVersion = schemaVersion;
        CreatedAtUtc = createdAtUtc;
        ContentHash = contentHash;
        SnapshotHash = snapshotHash;
        Json = json;
    }

    public Guid ConfigurationVersionId { get; }

    public string ConfigurationSetKey { get; }

    public string SchemaVersion { get; }

    public DateTime CreatedAtUtc { get; }

    public string ContentHash { get; }

    public string SnapshotHash { get; }

    public string Json { get; }
}

public sealed class GameConfigSnapshotBuilder
{
    public GameConfigSnapshot Build(
        Guid configurationVersionId,
        GameConfigSetKey configurationSetKey,
        DateTime createdAtUtc,
        GameConfigProcessedContent content)
    {
        if (configurationVersionId == Guid.Empty)
        {
            throw new ArgumentException("The configuration-version identifier cannot be empty.", nameof(configurationVersionId));
        }

        ArgumentNullException.ThrowIfNull(configurationSetKey);
        ArgumentNullException.ThrowIfNull(content);
        if (createdAtUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("The snapshot timestamp must use UTC.", nameof(createdAtUtc));
        }

        var contentHash = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(content.CanonicalJson)))
            .ToLowerInvariant();
        var buffer = new ArrayBufferWriter<byte>();
        using var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = false });

        writer.WriteStartObject();
        writer.WriteString("formatVersion", GameConfigConstants.SnapshotFormatVersion);
        writer.WriteString("schemaVersion", content.SchemaVersion);
        writer.WriteString("configurationSet", configurationSetKey.Value);
        writer.WriteString("configurationVersion", configurationVersionId.ToString("D"));
        writer.WriteString(
            "createdAtUtc",
            createdAtUtc.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture));
        writer.WritePropertyName("integrity");
        writer.WriteStartObject();
        writer.WriteString("algorithm", GameConfigConstants.ContentHashAlgorithm);
        writer.WriteString("canonicalization", content.CanonicalizationProfile);
        writer.WriteString("contentHash", contentHash);
        writer.WriteEndObject();
        writer.WritePropertyName("content");
        writer.WriteRawValue(content.CanonicalJson, skipInputValidation: true);
        writer.WriteEndObject();
        writer.Flush();

        if (buffer.WrittenCount > GameConfigConstants.SnapshotMaximumByteLength)
        {
            throw new InvalidOperationException(
                $"The snapshot exceeds {GameConfigConstants.SnapshotMaximumByteLength} bytes.");
        }

        var snapshotJson = Encoding.UTF8.GetString(buffer.WrittenSpan);
        var snapshotHash = ComputeHash(snapshotJson);
        return new GameConfigSnapshot(
            configurationVersionId,
            configurationSetKey.Value,
            content.SchemaVersion,
            createdAtUtc,
            contentHash,
            snapshotHash,
            snapshotJson);
    }

    private static string ComputeHash(string value)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();
    }
}
