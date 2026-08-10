using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NovaGame.Domain.Shared.Content.GameConfig;

namespace NovaGame.Domain.Content.GameConfig;

/// <summary>
/// Represents a consumer-verified NovaGame GameConfig snapshot.
/// </summary>
/// <remarks>
/// This contract remains experimental after M2 and does not promise source or binary compatibility.
/// </remarks>
public sealed class ValidatedGameConfigSnapshot
{
    internal ValidatedGameConfigSnapshot(
        Guid configurationVersionId,
        GameConfigSetKey configurationSet,
        string schemaVersion,
        DateTime createdAtUtc,
        string snapshotHash,
        string contentHash,
        string canonicalContentJson)
    {
        ConfigurationVersionId = configurationVersionId;
        ConfigurationSet = configurationSet;
        SchemaVersion = schemaVersion;
        CreatedAtUtc = createdAtUtc;
        SnapshotHash = snapshotHash;
        ContentHash = contentHash;
        CanonicalContentJson = canonicalContentJson;
    }

    public Guid ConfigurationVersionId { get; }

    public GameConfigSetKey ConfigurationSet { get; }

    public string SchemaVersion { get; }

    public DateTime CreatedAtUtc { get; }

    public string SnapshotHash { get; }

    public string ContentHash { get; }

    public string CanonicalContentJson { get; }
}

/// <summary>
/// Validates the neutral protocol and integrity boundaries of a GameConfig snapshot.
/// </summary>
/// <remarks>
/// This contract remains experimental after M2 and does not promise source or binary compatibility.
/// Transport metadata and game-specific content policy remain caller-owned.
/// </remarks>
public sealed class GameConfigSnapshotValidator
{
    private const int SnapshotMaximumDepth = 65;

    private const string CreatedAtUtcFormat = "yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'";

    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    private static readonly string[] SnapshotPropertyNames =
    [
        "formatVersion",
        "schemaVersion",
        "configurationSet",
        "configurationVersion",
        "createdAtUtc",
        "integrity",
        "content"
    ];

    private static readonly string[] IntegrityPropertyNames =
    [
        "algorithm",
        "canonicalization",
        "contentHash"
    ];

    private readonly GameConfigContentProcessor _contentProcessor;

    public GameConfigSnapshotValidator(GameConfigContentProcessor contentProcessor)
    {
        _contentProcessor = contentProcessor ??
            throw new ArgumentNullException(nameof(contentProcessor));
    }

    public bool TryValidate(
        ReadOnlyMemory<byte> snapshotUtf8,
        string trustedSnapshotHash,
        GameConfigSetKey expectedConfigurationSet,
        string expectedSchemaVersion,
        Guid? expectedConfigurationVersionId,
        [NotNullWhen(true)] out ValidatedGameConfigSnapshot? snapshot)
    {
        ArgumentNullException.ThrowIfNull(expectedConfigurationSet);
        if (!GameConfigSchemaVersionRules.IsValid(expectedSchemaVersion))
        {
            throw new ArgumentException(
                "The expected schema version is invalid.",
                nameof(expectedSchemaVersion));
        }

        if (expectedConfigurationVersionId == Guid.Empty)
        {
            throw new ArgumentException(
                "The expected configuration-version identifier cannot be empty.",
                nameof(expectedConfigurationVersionId));
        }

        snapshot = null;
        if (!IsLowercaseSha256(trustedSnapshotHash) ||
            snapshotUtf8.Length > GameConfigConstants.SnapshotMaximumByteLength)
        {
            return false;
        }

        var snapshotBytes = snapshotUtf8.ToArray();
        if (!string.Equals(
                ComputeHash(snapshotBytes),
                trustedSnapshotHash,
                StringComparison.Ordinal) ||
            HasUtf8ByteOrderMark(snapshotBytes))
        {
            return false;
        }

        string source;
        try
        {
            source = StrictUtf8.GetString(snapshotBytes);
            if (!StrictUtf8.GetBytes(source).AsSpan().SequenceEqual(snapshotBytes))
            {
                return false;
            }
        }
        catch (DecoderFallbackException)
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(snapshotBytes, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = SnapshotMaximumDepth
            });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !string.Equals(root.GetRawText(), source, StringComparison.Ordinal) ||
                ContainsDuplicateProperty(root) ||
                !HasExactProperties(root, SnapshotPropertyNames) ||
                !HasExpectedString(
                    root,
                    "formatVersion",
                    GameConfigConstants.SnapshotFormatVersion) ||
                !HasExpectedString(
                    root,
                    "configurationSet",
                    expectedConfigurationSet.Value) ||
                !HasExpectedString(root, "schemaVersion", expectedSchemaVersion) ||
                !TryGetConfigurationVersion(
                    root,
                    expectedConfigurationVersionId,
                    out var configurationVersionId) ||
                !TryGetCreatedAtUtc(root, out var createdAtUtc))
            {
                return false;
            }

            if (!root.TryGetProperty("integrity", out var integrity) ||
                !HasExactProperties(integrity, IntegrityPropertyNames) ||
                !HasExpectedString(
                    integrity,
                    "algorithm",
                    GameConfigConstants.ContentHashAlgorithm) ||
                !HasExpectedString(
                    integrity,
                    "canonicalization",
                    GameConfigConstants.CanonicalizationProfile) ||
                !TryGetString(integrity, "contentHash", out var contentHash) ||
                !IsLowercaseSha256(contentHash) ||
                !root.TryGetProperty("content", out var content))
            {
                return false;
            }

            var canonicalContentJson = content.GetRawText();
            if (!string.Equals(
                    ComputeHash(StrictUtf8.GetBytes(canonicalContentJson)),
                    contentHash,
                    StringComparison.Ordinal))
            {
                return false;
            }

            var processedContent = _contentProcessor.ProcessCanonicalSnapshotContent(
                expectedSchemaVersion,
                canonicalContentJson);
            if (!processedContent.Validation.IsValid ||
                processedContent.Content is null ||
                !string.Equals(
                    processedContent.Content.CanonicalJson,
                    canonicalContentJson,
                    StringComparison.Ordinal))
            {
                return false;
            }

            snapshot = new ValidatedGameConfigSnapshot(
                configurationVersionId,
                expectedConfigurationSet,
                expectedSchemaVersion,
                createdAtUtc,
                trustedSnapshotHash,
                contentHash,
                canonicalContentJson);
            return true;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            return false;
        }
    }

    private static bool HasUtf8ByteOrderMark(ReadOnlySpan<byte> value)
    {
        return value.Length >= 3 &&
            value[0] == 0xef &&
            value[1] == 0xbb &&
            value[2] == 0xbf;
    }

    private static bool ContainsDuplicateProperty(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name) || ContainsDuplicateProperty(property.Value))
                {
                    return true;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                if (ContainsDuplicateProperty(item))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool HasExactProperties(
        JsonElement element,
        IReadOnlyCollection<string> expectedNames)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        var actualNames = element
            .EnumerateObject()
            .Select(property => property.Name)
            .ToArray();
        return actualNames.Length == expectedNames.Count &&
            actualNames.All(expectedNames.Contains);
    }

    private static bool HasExpectedString(
        JsonElement element,
        string propertyName,
        string expectedValue)
    {
        return TryGetString(element, propertyName, out var value) &&
            string.Equals(value, expectedValue, StringComparison.Ordinal);
    }

    private static bool TryGetString(
        JsonElement element,
        string propertyName,
        [NotNullWhen(true)] out string? value)
    {
        if (element.TryGetProperty(propertyName, out var property) &&
            property.ValueKind == JsonValueKind.String)
        {
            value = property.GetString();
            return value is not null;
        }

        value = null;
        return false;
    }

    private static bool TryGetConfigurationVersion(
        JsonElement root,
        Guid? expectedConfigurationVersionId,
        out Guid configurationVersionId)
    {
        configurationVersionId = Guid.Empty;
        if (!TryGetString(root, "configurationVersion", out var rawValue) ||
            !Guid.TryParseExact(rawValue, "D", out configurationVersionId) ||
            configurationVersionId == Guid.Empty ||
            !string.Equals(
                rawValue,
                configurationVersionId.ToString("D"),
                StringComparison.Ordinal))
        {
            return false;
        }

        return expectedConfigurationVersionId is null ||
            configurationVersionId == expectedConfigurationVersionId.Value;
    }

    private static bool TryGetCreatedAtUtc(JsonElement root, out DateTime createdAtUtc)
    {
        createdAtUtc = default;
        return TryGetString(root, "createdAtUtc", out var rawValue) &&
            DateTime.TryParseExact(
                rawValue,
                CreatedAtUtcFormat,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out createdAtUtc) &&
            createdAtUtc.Kind == DateTimeKind.Utc;
    }

    private static bool IsLowercaseSha256(string? value)
    {
        return value is { Length: 64 } && value.All(character =>
            character is >= '0' and <= '9' or >= 'a' and <= 'f');
    }

    private static string ComputeHash(ReadOnlySpan<byte> value)
    {
        return Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();
    }
}
